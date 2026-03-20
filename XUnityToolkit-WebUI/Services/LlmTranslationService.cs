using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.SignalR;
using XUnityToolkit_WebUI.Hubs;
using XUnityToolkit_WebUI.Models;

namespace XUnityToolkit_WebUI.Services;

public sealed class LlmTranslationService(
    IHttpClientFactory httpClientFactory,
    AppSettingsService settingsService,
    TermService termService,
    TermMatchingService termMatchingService,
    TermAuditService termAuditService,
    TranslationMemoryService translationMemoryService,
    GameLibraryService gameLibraryService,
    IHubContext<InstallProgressHub> hubContext,
    ILogger<LlmTranslationService> logger)
{
    // ── Translation memory (per-game, volatile) ──
    private record TranslationMemoryEntry(string Original, string Translated);

    private sealed class TranslationMemoryBuffer
    {
        private readonly ConcurrentQueue<TranslationMemoryEntry> _queue = new();

        public void Add(IList<string> originals, IList<string> translations, int maxSize)
        {
            for (int i = 0; i < originals.Count && i < translations.Count; i++)
                _queue.Enqueue(new TranslationMemoryEntry(originals[i], translations[i]));
            while (_queue.Count > maxSize)
                _queue.TryDequeue(out _);
        }

        public IList<TranslationMemoryEntry> GetSnapshot() => _queue.ToArray();
    }

    private readonly ConcurrentDictionary<string, TranslationMemoryBuffer> _translationMemory = new();

    // ── Per-game description cache (avoids disk I/O on hot path) ──
    private readonly ConcurrentDictionary<string, string?> _descriptionCache = new();

    /// <summary>
    /// Update the cached description for a game. Call from the description PUT endpoint.
    /// </summary>
    public void InvalidateDescriptionCache(string gameId, string? description)
    {
        _descriptionCache[gameId] = description;
    }

    // ── Core stats (thread-safe) ──
    private long _totalTranslated;
    private long _totalReceived; // total incoming translation requests
    private long _totalErrors;   // total failed translations
    private long _translating;   // text count actively calling LLM API (inside semaphore)
    private long _queued;        // text count waiting for semaphore slot
    private long _lastRequestTicks; // 0 = never
    private long _totalTokensUsed;
    private long _totalResponseTimeMs;
    private long _totalRequests;
    private bool _enabled = true;

    // ── Term audit stats ──
    private long _termMatchedTextCount;
    private long _termAuditPhase1PassCount;
    private long _termAuditPhase2PassCount;
    private long _termAuditForceCorrectedCount;

    // ── Translation memory stats ──
    private long _tmExactHits;
    private long _tmFuzzyHits;
    private long _tmPatternHits;
    private long _tmMisses;

    // ── Per-endpoint runtime stats ──
    private readonly ConcurrentDictionary<string, EndpointStats> _endpointStats = new();

    // ── Recent translations circular buffer ──
    private readonly ConcurrentQueue<RecentTranslation> _recentTranslations = new();
    private const int MaxRecentTranslations = 10;

    // ── Recent errors circular buffer ──
    private readonly ConcurrentQueue<TranslationError> _recentErrors = new();
    private const int MaxRecentErrors = 50;

    // ── Requests-per-minute tracking ──
    private readonly ConcurrentQueue<long> _requestTimestamps = new();

    // ── Concurrency control ──
    private SemaphoreSlim? _semaphore; // Read via Volatile.Read at capture site for ARM safety
    private volatile int _currentMaxConcurrency;

    // ── Current game tracking ──
    private string? _currentGameId;

    // ── BroadcastStats throttle ──
    private long _lastBroadcastTicks;

    public bool HasPendingTranslations => Interlocked.Read(ref _translating) > 0 || Interlocked.Read(ref _queued) > 0;

    public bool Enabled
    {
        get => Volatile.Read(ref _enabled);
        set => Volatile.Write(ref _enabled, value);
    }

    public TranslationStats GetStats()
    {
        // Clean old timestamps (older than 60s) for RPM calculation
        var cutoff = DateTime.UtcNow.AddMinutes(-1).Ticks;
        while (_requestTimestamps.TryPeek(out var ts) && ts < cutoff)
            _requestTimestamps.TryDequeue(out _);

        var totalReqs = Interlocked.Read(ref _totalRequests);
        var avgMs = totalReqs > 0
            ? (double)Interlocked.Read(ref _totalResponseTimeMs) / totalReqs
            : 0;

        return new TranslationStats(
            TotalTranslated: Interlocked.Read(ref _totalTranslated),
            Translating: (int)Interlocked.Read(ref _translating),
            Queued: (int)Interlocked.Read(ref _queued),
            LastRequestAt: Interlocked.Read(ref _lastRequestTicks) is var ticks and > 0
                ? new DateTime(ticks, DateTimeKind.Utc)
                : null,
            TotalTokensUsed: Interlocked.Read(ref _totalTokensUsed),
            AverageResponseTimeMs: Math.Round(avgMs, 1),
            RequestsPerMinute: _requestTimestamps.Count,
            Enabled: Volatile.Read(ref _enabled),
            RecentTranslations: _recentTranslations.ToArray(),
            TotalReceived: Interlocked.Read(ref _totalReceived),
            TotalErrors: Interlocked.Read(ref _totalErrors),
            RecentErrors: _recentErrors.ToArray(),
            CurrentGameId: Volatile.Read(ref _currentGameId)
        )
        {
            TermMatchedTextCount = (int)Interlocked.Read(ref _termMatchedTextCount),
            TermAuditPhase1PassCount = (int)Interlocked.Read(ref _termAuditPhase1PassCount),
            TermAuditPhase2PassCount = (int)Interlocked.Read(ref _termAuditPhase2PassCount),
            TermAuditForceCorrectedCount = (int)Interlocked.Read(ref _termAuditForceCorrectedCount),
            TranslationMemoryHits = (int)Interlocked.Read(ref _tmExactHits),
            TranslationMemoryFuzzyHits = (int)Interlocked.Read(ref _tmFuzzyHits),
            TranslationMemoryPatternHits = (int)Interlocked.Read(ref _tmPatternHits),
            TranslationMemoryMisses = (int)Interlocked.Read(ref _tmMisses),
            MaxConcurrency = _currentMaxConcurrency,
        };
    }

    /// <summary>
    /// Record a translation error for display in the web UI.
    /// </summary>
    public void RecordError(string message, string? endpointName = null, string? gameId = null)
    {
        Interlocked.Increment(ref _totalErrors);
        _recentErrors.Enqueue(new TranslationError(message, DateTime.UtcNow, endpointName, gameId));
        while (_recentErrors.Count > MaxRecentErrors)
            _recentErrors.TryDequeue(out _);
        _ = BroadcastStats(force: true);
    }

    public async Task<IList<string>> TranslateAsync(
        IList<string> texts, string from, string to,
        string? gameId = null, CancellationToken ct = default)
    {
        if (texts.Count == 0) return [];

        Interlocked.Add(ref _totalReceived, texts.Count);
        Interlocked.Exchange(ref _lastRequestTicks, DateTime.UtcNow.Ticks);
        if (!string.IsNullOrEmpty(gameId))
            Volatile.Write(ref _currentGameId, gameId);
        _ = BroadcastStats();

        if (!Volatile.Read(ref _enabled))
        {
            RecordError("AI 翻译功能已停用", gameId: gameId);
            throw new InvalidOperationException("AI 翻译功能已停用");
        }

        try
        {
            var settings = await settingsService.GetAsync(ct);
            var ai = settings.AiTranslation;

            // Local mode restrictions
            var isLocalMode = string.Equals(ai.ActiveMode, "local", StringComparison.OrdinalIgnoreCase);

            var enabledEndpoints = ai.Endpoints.Where(e => e.Enabled && !string.IsNullOrWhiteSpace(e.ApiKey)).ToList();
            if (enabledEndpoints.Count == 0)
            {
                logger.LogWarning("没有可用的 AI 提供商: 总端点数={Total}, 各端点状态=[{Details}]",
                    ai.Endpoints.Count,
                    string.Join(", ", ai.Endpoints.Select(e => $"{e.Name}(Enabled={e.Enabled}, HasKey={!string.IsNullOrWhiteSpace(e.ApiKey)})")));
                RecordError("没有可用的 AI 提供商，请在 AI 翻译页面配置至少一个提供商", gameId: gameId);
                throw new InvalidOperationException("没有可用的 AI 提供商，请在 AI 翻译页面配置至少一个提供商");
            }

            // Load unified terms
            List<TermEntry>? allTerms = null;
            List<TermEntry>? matchedTerms = null;
            if (!string.IsNullOrEmpty(gameId))
            {
                allTerms = await termService.GetAsync(gameId, ct);
                if (allTerms.Count > 0)
                    matchedTerms = termMatchingService.FindMatchedTerms(allTerms, texts.ToList());
            }

            // Split matched terms into glossary (Translate) and DNT lists
            List<TermEntry>? glossary = null;
            List<TermEntry>? dntEntries = null;
            if (matchedTerms is { Count: > 0 })
            {
                var translateTerms = matchedTerms.Where(t => t.Type == TermType.Translate).ToList();
                if (translateTerms.Count > 0)
                    glossary = translateTerms;

                var dntTerms = matchedTerms.Where(t => t.Type == TermType.DoNotTranslate).ToList();
                if (dntTerms.Count > 0)
                    dntEntries = dntTerms;
            }

            if (logger.IsEnabled(LogLevel.Debug))
            {
                logger.LogDebug("翻译上下文: gameId={GameId}, 术语={TermCount}条(匹配={MatchedCount}条), 文本数={TextCount}",
                    gameId ?? "(空)", allTerms?.Count ?? 0, matchedTerms?.Count ?? 0, texts.Count);
                if (glossary is { Count: > 0 })
                    logger.LogDebug("翻译术语: {Count} 条 [{Terms}]",
                        glossary.Count, string.Join(", ", glossary.Select(g => $"\"{g.Original}\"→\"{g.Translation}\"")));
                if (dntEntries is { Count: > 0 })
                    logger.LogDebug("禁翻词: {Count} 条 [{Terms}]",
                        dntEntries.Count, string.Join(", ", dntEntries.Select(d => $"\"{d.Original}\"")));
            }

            // Load per-game description (cached in-memory, no disk I/O)
            string? gameDescription = null;
            if (!string.IsNullOrEmpty(gameId))
            {
                if (!_descriptionCache.TryGetValue(gameId, out gameDescription))
                {
                    // First access: populate cache from disk (one-time per game per session)
                    var game = await gameLibraryService.GetByIdAsync(gameId, ct);
                    gameDescription = game?.AiDescription;
                    _descriptionCache[gameId] = gameDescription;
                }
                if (gameDescription is { Length: > 500 })
                    gameDescription = gameDescription[..500];
            }

            // Load translation memory context — local mode uses separate LocalContextSize (capped at 10)
            var contextSize = isLocalMode
                ? Math.Clamp(ai.LocalContextSize, 0, 10)
                : Math.Clamp(ai.ContextSize, 0, 100);
            IList<TranslationMemoryEntry>? memoryContext = null;
            if (contextSize > 0 && !string.IsNullOrEmpty(gameId))
            {
                var buffer = _translationMemory.GetOrAdd(gameId, _ => new TranslationMemoryBuffer());
                memoryContext = buffer.GetSnapshot();
                if (memoryContext.Count == 0) memoryContext = null;
            }

            // Ensure semaphore matches configured concurrency — forced to 1 in local mode
            var maxConc = isLocalMode ? 1 : Math.Clamp(ai.MaxConcurrency, 1, 100);
            EnsureSemaphore(maxConc);
            var semaphore = Volatile.Read(ref _semaphore)!; // Acquire-read for ARM safety

            // ═══════════════════════════════════════════════════════════
            // Multi-phase translation pipeline
            // Phase 0: Translation Memory lookup (exact/pattern/fuzzy)
            // Phase 1: Natural translation (terms in system prompt, no placeholders)
            // Phase 2: Placeholder-based translation (existing logic)
            // Phase 3: Force correction for segments that still fail audit
            // ═══════════════════════════════════════════════════════════

            long tokens = 0;
            double ms = 0;
            string endpointName = "";
            var translations = new List<string>(new string[texts.Count]);
            var phase1Resolved = new HashSet<int>(); // indices resolved by Phase 1

            // Per-text term tracking for RecentTranslation metadata
            var perTextHasTerms = new bool[texts.Count];
            var perTextHasDnt = new bool[texts.Count];
            var perTextAuditResult = new string?[texts.Count];
            var perTextTranslationSource = new string?[texts.Count];

            // Compute per-text term info
            if (matchedTerms is { Count: > 0 })
            {
                int matchedCount = 0;
                for (int i = 0; i < texts.Count; i++)
                {
                    var relevant = termMatchingService.FindMatchedTerms(matchedTerms, [texts[i]]);
                    perTextHasTerms[i] = relevant.Any(t => t.Type == TermType.Translate);
                    perTextHasDnt[i] = relevant.Any(t => t.Type == TermType.DoNotTranslate);
                    if (perTextHasTerms[i] || perTextHasDnt[i])
                        matchedCount++;
                }
                Interlocked.Add(ref _termMatchedTextCount, matchedCount);
            }

            // ── Phase 0: Translation Memory lookup ──
            var tmResults = new Dictionary<int, (string translation, TmMatchType matchType)>();
            if (ai.EnableTranslationMemory && !string.IsNullOrEmpty(gameId))
            {
                await translationMemoryService.EnsureLoadedAsync(gameId, ct);

                for (int i = 0; i < texts.Count; i++)
                {
                    var tmMatch = translationMemoryService.TryMatch(gameId, texts[i], ai.FuzzyMatchThreshold / 100.0);
                    if (tmMatch is null)
                    {
                        Interlocked.Increment(ref _tmMisses);
                        continue;
                    }

                    // Term audit check if terms exist
                    if (matchedTerms is { Count: > 0 } && ai.TermAuditEnabled)
                    {
                        var termsForText = termMatchingService.FindMatchedTerms(allTerms!, [texts[i]]);
                        if (termsForText.Count > 0)
                        {
                            var auditResult = termAuditService.AuditTranslation(tmMatch.Translation, termsForText);
                            if (!auditResult.Passed)
                            {
                                Interlocked.Increment(ref _tmMisses);
                                continue; // Audit failed, fall through to LLM
                            }
                        }
                    }

                    tmResults[i] = (tmMatch.Translation, tmMatch.MatchType);

                    // Track stats
                    switch (tmMatch.MatchType)
                    {
                        case TmMatchType.Exact: Interlocked.Increment(ref _tmExactHits); break;
                        case TmMatchType.Fuzzy: Interlocked.Increment(ref _tmFuzzyHits); break;
                        case TmMatchType.Pattern: Interlocked.Increment(ref _tmPatternHits); break;
                    }
                }

                if (tmResults.Count > 0)
                    logger.LogDebug("Phase 0 TM 命中: {Count}/{Total} 条", tmResults.Count, texts.Count);
            }

            // Apply Phase 0 results — mark resolved indices so Phases 1-3 skip them
            var phase0Resolved = new HashSet<int>();
            if (tmResults.Count > 0)
            {
                foreach (var (idx, (translation, matchType)) in tmResults)
                {
                    translations[idx] = translation;
                    phase0Resolved.Add(idx);
                    perTextTranslationSource[idx] = matchType switch
                    {
                        TmMatchType.Exact => "tmExact",
                        TmMatchType.Fuzzy => "tmFuzzy",
                        TmMatchType.Pattern => "tmPattern",
                        _ => null
                    };
                }

                if (tmResults.Count == texts.Count)
                {
                    // All texts resolved by TM — skip Phases 1-3 entirely
                    goto PipelineComplete;
                }
            }

            // ── Phase 1: Natural Translation Mode ──
            // Send source texts unmodified to LLM with terms in structured prompt format.
            // Only when NaturalTranslationMode is enabled, matched terms exist, and token budget allows.
            var usePhase1 = ai.NaturalTranslationMode
                && !isLocalMode
                && matchedTerms is { Count: > 0 };

            if (usePhase1)
            {
                // Token budget check: term tokens ≤ 70% of remaining context
                var termTokens = termMatchingService.EstimateTermTokens(matchedTerms!);
                var systemPromptEstimate = termMatchingService.EstimateTokenCount(ai.SystemPrompt) + 200; // base prompt overhead
                var sourceEstimate = texts.Sum(t => termMatchingService.EstimateTokenCount(t));
                var outputReserve = sourceEstimate; // rough: output ≈ input size
                var contextBudget = ai.ContextSize > 0 ? ai.ContextSize : 4096;
                var remainingContext = contextBudget - systemPromptEstimate - sourceEstimate - outputReserve;
                var tokenBudgetOk = termTokens <= remainingContext * 0.7;

                if (tokenBudgetOk)
                {
                    // Build natural-mode system prompt with structured term list
                    var naturalPrompt = BuildSystemPromptWithTerms(
                        ai.SystemPrompt, from, to, matchedTerms!, gameDescription, memoryContext);

                    // Local mode: skip glossary in system prompt — not applicable here since usePhase1 excludes local mode
                    List<TermEntry>? nullGlossary = null;

                    // Filter out Phase 0 TM-resolved texts to avoid wasting LLM tokens
                    var p1Indices = new List<int>();
                    var p1Texts = new List<string>();
                    for (int i = 0; i < texts.Count; i++)
                    {
                        if (!phase0Resolved.Contains(i))
                        {
                            p1Indices.Add(i);
                            p1Texts.Add(texts[i]);
                        }
                    }

                    // Send unmodified source texts to LLM
                    if (p1Texts.Count > 0)
                    {
                        var (p1Result, p1Tokens, p1Ms, p1Endpoint) = await TranslateBatchAsync(
                            p1Texts, from, to, ai, enabledEndpoints, nullGlossary,
                            gameDescription, memoryContext, null, semaphore, ct,
                            overrideSystemPrompt: naturalPrompt);
                        tokens += p1Tokens;
                        ms += p1Ms;
                        endpointName = p1Endpoint;

                        // Audit Phase 1 results if enabled
                        for (int j = 0; j < p1Indices.Count; j++)
                        {
                            var i = p1Indices[j];
                            var translated = j < p1Result.Count ? p1Result[j] : texts[i];

                            if (ai.TermAuditEnabled && matchedTerms is { Count: > 0 })
                            {
                                // Find terms relevant to this specific text
                                var relevantTerms = termMatchingService.FindMatchedTerms(
                                    matchedTerms, [texts[i]]);
                                if (relevantTerms.Count > 0)
                                {
                                    var audit = termAuditService.AuditTranslation(translated, relevantTerms);
                                    if (audit.Passed)
                                    {
                                        translations[i] = translated;
                                        phase1Resolved.Add(i);
                                        perTextAuditResult[i] = "phase1Pass";
                                        Interlocked.Increment(ref _termAuditPhase1PassCount);
                                    }
                                    // else: leave for Phase 2
                                    continue;
                                }
                            }

                            // No audit or no relevant terms — accept Phase 1 result
                            translations[i] = translated;
                            phase1Resolved.Add(i);
                        }
                    }

                    if (logger.IsEnabled(LogLevel.Debug))
                        logger.LogDebug("Phase 1 自然翻译: {Resolved}/{Total} 段通过审查",
                            phase1Resolved.Count, texts.Count);
                }
            }

            // ── Phase 2: Placeholder-based translation ──
            // For segments not resolved by Phase 0 or Phase 1
            var phase2Indices = new List<int>();
            for (int i = 0; i < texts.Count; i++)
            {
                if (!phase0Resolved.Contains(i) && !phase1Resolved.Contains(i))
                    phase2Indices.Add(i);
            }

            var phase2Resolved = new HashSet<int>();

            if (phase2Indices.Count > 0)
            {
                // Collect texts for Phase 2
                var phase2SourceTexts = phase2Indices.Select(i => texts[i]).ToList();

                // Glossary placeholders are applied BEFORE DNT to ensure longer glossary
                // terms take priority. Example: glossary "魔法師"→"大法师" must match before
                // DNT "魔法" can consume the substring.
                IList<string> textsToTranslate = phase2SourceTexts;
                string? dntHint = null;

                // Apply glossary placeholder substitution for non-regex entries
                Dictionary<string, string>? glossaryMapping = null;
                // Local mode: skip glossary in system prompt to save context tokens;
                // placeholder substitution + post-processing still enforce glossary terms.
                List<TermEntry>? promptGlossary = isLocalMode ? null : glossary;
                if (glossary is not null)
                {
                    var nonRegexEntries = glossary.Where(e => !e.IsRegex && !string.IsNullOrWhiteSpace(e.Original)).ToList();
                    if (nonRegexEntries.Count > 0)
                    {
                        var (replaced, mapping) = ApplyGlossaryReplacements(textsToTranslate, nonRegexEntries);
                        if (mapping.Count > 0)
                        {
                            textsToTranslate = replaced;
                            glossaryMapping = mapping;
                            dntHint = "\n\n文本中的 {{G_x}} 是术语占位符，请在翻译结果中原样保留，不要修改、翻译或删除。";

                            if (logger.IsEnabled(LogLevel.Debug))
                            {
                                var mappingStr = string.Join(", ", mapping.Select(kv => $"{kv.Key}→\"{kv.Value}\""));
                                logger.LogDebug("术语占位符替换: {Count} 条映射: {Mapping}", mapping.Count, mappingStr);
                            }
                        }
                    }
                }

                // Apply do-not-translate placeholder substitution (on glossary-processed text)
                Dictionary<string, string>? dntMapping = null;
                if (dntEntries is not null)
                {
                    var (replaced, mapping) = ApplyDoNotTranslateReplacements(textsToTranslate, dntEntries);
                    if (mapping.Count > 0)
                    {
                        textsToTranslate = replaced;
                        dntMapping = mapping;
                        dntHint = (dntHint ?? "") +
                            "\n\n文本中的 {{DNT_x}} 是不可翻译的占位符，请在翻译结果中原样保留，不要修改、翻译或删除。";

                        if (logger.IsEnabled(LogLevel.Debug))
                        {
                            var mappingStr = string.Join(", ", mapping.Select(kv => $"{kv.Key}→\"{kv.Value}\""));
                            logger.LogDebug("禁翻占位符替换: {Count} 条映射: {Mapping}", mapping.Count, mappingStr);
                        }
                    }
                }

                // Pre-compute results for texts that are ENTIRELY a single placeholder.
                var preComputed = new Dictionary<int, string>(); // index within phase2SourceTexts → result
                var llmTexts = new List<string>();
                var llmIndexMap = new List<int>(); // llmTexts idx → phase2SourceTexts idx

                for (int i = 0; i < textsToTranslate.Count; i++)
                {
                    var text = textsToTranslate[i];
                    string? directResult = null;

                    if (glossaryMapping is not null)
                    {
                        var gm = FullGlossaryPlaceholderRegex.Match(text);
                        if (gm.Success)
                        {
                            var key = $"{{{{G_{gm.Groups[1].Value}}}}}";
                            if (glossaryMapping.TryGetValue(key, out var val))
                                directResult = val;
                        }
                    }

                    if (directResult is null && dntMapping is not null)
                    {
                        var dm = FullDntPlaceholderRegex.Match(text);
                        if (dm.Success)
                        {
                            var key = $"{{{{DNT_{dm.Groups[1].Value}}}}}";
                            if (dntMapping.TryGetValue(key, out var val))
                                directResult = val;
                        }
                    }

                    if (directResult is not null)
                    {
                        preComputed[i] = directResult;
                    }
                    else
                    {
                        llmTexts.Add(text);
                        llmIndexMap.Add(i);
                    }
                }

                if (logger.IsEnabled(LogLevel.Debug) && preComputed.Count > 0)
                {
                    logger.LogDebug("预计算占位符: {Count} 段文本跳过LLM（全为单个占位符直接替换）",
                        preComputed.Count);
                }

                // Call LLM only for texts that actually need translation
                IList<string> batchResult;

                if (llmTexts.Count > 0)
                {
                    if (isLocalMode && llmTexts.Count > 1)
                    {
                        var singleResults = new List<string>(llmTexts.Count);
                        foreach (var text in llmTexts)
                        {
                            var (r, t, m, e) = await TranslateBatchAsync(
                                new List<string> { text }, from, to, ai, enabledEndpoints, promptGlossary,
                                gameDescription, memoryContext, dntHint, semaphore, ct);
                            singleResults.Add(r[0]);
                            tokens += t;
                            ms += m;
                            endpointName = e;
                        }
                        batchResult = singleResults;
                    }
                    else
                    {
                        IList<string> p2Result;
                        long p2Tokens;
                        double p2Ms;
                        string p2Endpoint;
                        (p2Result, p2Tokens, p2Ms, p2Endpoint) = await TranslateBatchAsync(
                            llmTexts, from, to, ai, enabledEndpoints, promptGlossary,
                            gameDescription, memoryContext, dntHint, semaphore, ct);
                        batchResult = p2Result;
                        tokens += p2Tokens;
                        ms += p2Ms;
                        endpointName = p2Endpoint;
                    }
                }
                else
                {
                    batchResult = [];
                }

                // Merge LLM results with pre-computed results
                var phase2Translations = new List<string>(textsToTranslate.Count);
                int llmIdx = 0;
                for (int i = 0; i < textsToTranslate.Count; i++)
                {
                    if (preComputed.TryGetValue(i, out var direct))
                        phase2Translations.Add(direct);
                    else
                        phase2Translations.Add(batchResult[llmIdx++]);
                }

                // Restore glossary placeholders (for partial-placeholder texts from LLM)
                if (glossaryMapping is not null)
                {
                    phase2Translations = RestoreGlossaryPlaceholders(phase2Translations, glossaryMapping);
                    if (logger.IsEnabled(LogLevel.Debug))
                        logger.LogDebug("术语占位符恢复完成: {Count} 条映射已还原", glossaryMapping.Count);
                }

                // Apply glossary post-processing BEFORE DNT restoration
                if (glossary is not null)
                {
                    int postProcessCount = 0;
                    for (int i = 0; i < phase2Translations.Count; i++)
                    {
                        if (preComputed.ContainsKey(i)) continue; // pre-computed results are final
                        var before = phase2Translations[i];
                        phase2Translations[i] = ApplyGlossaryPostProcess(phase2Translations[i], glossary, logger);
                        if (before != phase2Translations[i]) postProcessCount++;
                    }
                    if (logger.IsEnabled(LogLevel.Debug) && postProcessCount > 0)
                        logger.LogDebug("术语后处理: {Count} 段译文被修正", postProcessCount);
                }

                // Restore do-not-translate placeholders AFTER glossary post-processing
                if (dntMapping is not null)
                {
                    phase2Translations = RestoreDoNotTranslatePlaceholders(phase2Translations, dntMapping);
                    if (logger.IsEnabled(LogLevel.Debug))
                        logger.LogDebug("禁翻占位符恢复完成: {Count} 条映射已还原", dntMapping.Count);
                }

                // Write Phase 2 results back and optionally audit
                for (int p2i = 0; p2i < phase2Indices.Count; p2i++)
                {
                    var originalIdx = phase2Indices[p2i];
                    var translated = phase2Translations[p2i];

                    if (ai.TermAuditEnabled && matchedTerms is { Count: > 0 })
                    {
                        var relevantTerms = termMatchingService.FindMatchedTerms(
                            matchedTerms, [texts[originalIdx]]);
                        if (relevantTerms.Count > 0)
                        {
                            var audit = termAuditService.AuditTranslation(translated, relevantTerms);
                            if (audit.Passed)
                            {
                                Interlocked.Increment(ref _termAuditPhase2PassCount);
                                phase2Resolved.Add(originalIdx);
                                perTextAuditResult[originalIdx] = "phase2Pass";
                            }
                            // else: leave for Phase 3 force correction
                        }
                        else
                        {
                            phase2Resolved.Add(originalIdx);
                        }
                    }
                    else
                    {
                        phase2Resolved.Add(originalIdx);
                    }

                    translations[originalIdx] = translated;
                }
            }

            if (logger.IsEnabled(LogLevel.Debug) && matchedTerms is { Count: > 0 })
            {
                var phase3Candidates = texts.Count - phase0Resolved.Count - phase1Resolved.Count - phase2Resolved.Count;
                logger.LogDebug("术语审查汇总: Phase0(TM)={P0}, Phase1通过={P1}/{Total}, Phase2通过={P2}, 待强制修正={P3}",
                    phase0Resolved.Count, phase1Resolved.Count, texts.Count, phase2Resolved.Count, phase3Candidates);
            }

            // ── Phase 3: Force Correction ──
            // For segments that failed both Phase 1 and Phase 2 audit
            if (ai.TermAuditEnabled && matchedTerms is { Count: > 0 })
            {
                for (int i = 0; i < texts.Count; i++)
                {
                    if (phase0Resolved.Contains(i) || phase1Resolved.Contains(i) || phase2Resolved.Contains(i))
                        continue;

                    // This segment failed audit in previous phases — apply force correction
                    var translated = translations[i];
                    if (string.IsNullOrEmpty(translated))
                        continue;

                    var relevantTerms = termMatchingService.FindMatchedTerms(
                        matchedTerms, [texts[i]]);

                    // Track protected spans in the translated text to prevent shorter terms
                    // from corrupting substrings inside already-correct longer terms.
                    var protectedSpans = new List<(int Start, int Length)>();

                    foreach (var term in relevantTerms)
                    {
                        if (term.IsRegex) continue;

                        var comparison = term.CaseSensitive
                            ? StringComparison.Ordinal
                            : StringComparison.OrdinalIgnoreCase;

                        switch (term.Type)
                        {
                            case TermType.Translate when !string.IsNullOrEmpty(term.Translation):
                                if (translated.Contains(term.Translation, comparison))
                                {
                                    // Term is already correctly applied — protect its spans
                                    ProtectExistingSpans(translated, term.Translation, comparison, protectedSpans);
                                }
                                else
                                {
                                    var before = translated;
                                    translated = ReplaceWithProtection(translated, term.Original, term.Translation,
                                        StringComparison.Ordinal, protectedSpans);
                                    if (before != translated)
                                        logger.LogDebug(
                                            "Phase 3 强制修正: 术语 \"{Original}\"→\"{Translation}\", 修正前=\"{Before}\", 修正后=\"{After}\"",
                                            term.Original, term.Translation,
                                            before.Length > 80 ? before[..80] + "..." : before,
                                            translated.Length > 80 ? translated[..80] + "..." : translated);
                                }
                                break;

                            case TermType.DoNotTranslate:
                                if (translated.Contains(term.Original, comparison))
                                {
                                    ProtectExistingSpans(translated, term.Original, comparison, protectedSpans);
                                }
                                else
                                {
                                    logger.LogWarning(
                                        "Phase 3 强制修正: 禁翻词 \"{Original}\" 在翻译结果中缺失，无法自动修正。译文=\"{Translated}\"",
                                        term.Original, translated.Length > 80 ? translated[..80] + "..." : translated);
                                }
                                break;
                        }
                    }

                    translations[i] = translated;
                    perTextAuditResult[i] = "forceCorrected";
                    Interlocked.Increment(ref _termAuditForceCorrectedCount);
                }
            }

            PipelineComplete:

            // Guard: empty translations cause XUnity.AutoTranslator to count as errors;
            // 5 consecutive errors trigger automatic translator shutdown.
            // Fall back to the original text when the LLM returns an empty string.
            for (int i = 0; i < translations.Count; i++)
            {
                if (string.IsNullOrWhiteSpace(translations[i]))
                    translations[i] = texts[i];
            }

            for (int i = 0; i < translations.Count; i++)
            {
                // Record recent translation with term metadata
                var tokensPerText = texts.Count > 0 ? tokens / texts.Count : 0;
                var effectiveEndpoint = perTextTranslationSource[i] is not null ? "翻译记忆" : endpointName;
                var recent = new RecentTranslation(
                    texts[i], translations[i], DateTime.UtcNow, tokensPerText, Math.Round(ms, 1), effectiveEndpoint, gameId)
                {
                    HasTerms = perTextHasTerms[i],
                    HasDnt = perTextHasDnt[i],
                    TermAuditResult = perTextAuditResult[i],
                    TranslationSource = perTextTranslationSource[i],
                };
                _recentTranslations.Enqueue(recent);
                while (_recentTranslations.Count > MaxRecentTranslations)
                    _recentTranslations.TryDequeue(out _);
            }

            // Accumulate volatile translation memory (for LLM context)
            if (contextSize > 0 && !string.IsNullOrEmpty(gameId))
            {
                var buffer = _translationMemory.GetOrAdd(gameId, _ => new TranslationMemoryBuffer());
                buffer.Add(texts, translations, contextSize);
            }

            // Persist to Translation Memory service (in-memory + debounced disk write)
            if (ai.EnableTranslationMemory && !string.IsNullOrEmpty(gameId))
            {
                try { translationMemoryService.Add(gameId, texts, translations, round: 1, isFinal: true); }
                catch (Exception ex) { logger.LogWarning(ex, "翻译记忆写入失败"); }
            }

            Interlocked.Add(ref _totalTranslated, texts.Count);
            return translations;
        }
        finally
        {
            _ = BroadcastStats(force: true);
        }
    }

    private async Task<(IList<string> translations, long tokens, double ms, string endpointName)> TranslateBatchAsync(
        IList<string> texts, string from, string to,
        AiTranslationSettings ai, List<ApiEndpointConfig> endpoints,
        List<TermEntry>? glossary, string? gameDescription,
        IList<TranslationMemoryEntry>? memoryContext,
        string? dntHint, SemaphoreSlim semaphore, CancellationToken ct,
        string? overrideSystemPrompt = null)
    {
        Interlocked.Increment(ref _queued);
        _ = BroadcastStats(force: true);

        bool semaphoreAcquired = false;
        try
        {
            semaphoreAcquired = await semaphore.WaitAsync(TimeSpan.FromSeconds(60), ct);
            if (!semaphoreAcquired)
            {
                logger.LogWarning("翻译队列超时: 等待超过 60 秒，当前排队 {Queued}", Interlocked.Read(ref _queued));
                throw new InvalidOperationException("翻译队列已满，请稍后重试");
            }
        }
        catch
        {
            // Semaphore not acquired (timeout or cancellation): only decrement queued
            Interlocked.Decrement(ref _queued);
            _ = BroadcastStats(force: true);
            throw;
        }

        // Acquired semaphore: transition from queued to translating
        Interlocked.Decrement(ref _queued);
        Interlocked.Increment(ref _translating);
        _ = BroadcastStats(force: true);

        const int maxRetries = 2;
        ApiEndpointConfig? chosenEndpoint = null;
        try
        {
            for (int attempt = 0; attempt <= maxRetries; attempt++)
            {
                try
                {
                    chosenEndpoint = SelectEndpoint(endpoints);
                    var sw = Stopwatch.StartNew();

                    var (result, tokens) = await CallProviderAsync(
                        chosenEndpoint, ai, texts, from, to, glossary, gameDescription, memoryContext, dntHint, ct,
                        overrideSystemPrompt);
                    sw.Stop();

                    var elapsedMs = sw.Elapsed.TotalMilliseconds;

                    // Update per-endpoint stats
                    var stats = _endpointStats.GetOrAdd(chosenEndpoint.Id, _ => new EndpointStats());
                    stats.RecordSuccess(elapsedMs);

                    // Update global stats
                    Interlocked.Add(ref _totalTokensUsed, tokens);
                    Interlocked.Add(ref _totalResponseTimeMs, (long)elapsedMs);
                    Interlocked.Increment(ref _totalRequests);
                    _requestTimestamps.Enqueue(DateTime.UtcNow.Ticks);

                    // Defensive: ensure result count matches input
                    if (result.Count != texts.Count)
                    {
                        logger.LogWarning("LLM 返回数量不匹配: 期望 {Expected}, 实际 {Actual}", texts.Count, result.Count);
                        var padded = new List<string>(texts.Count);
                        for (int i = 0; i < texts.Count; i++)
                            padded.Add(i < result.Count ? result[i] : texts[i]);
                        result = padded;
                    }

                    return (result, tokens, elapsedMs, chosenEndpoint.Name);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex) when (attempt < maxRetries && IsTransientError(ex))
                {
                    if (chosenEndpoint is not null)
                    {
                        var stats = _endpointStats.GetOrAdd(chosenEndpoint.Id, _ => new EndpointStats());
                        stats.RecordError();
                    }
                    var delay = TimeSpan.FromSeconds(Math.Pow(2, attempt) * 2);
                    logger.LogWarning(ex, "提供商 {Name} 翻译失败 (尝试 {Attempt}/{Max}), {Delay}s 后重试",
                        chosenEndpoint?.Name, attempt + 1, maxRetries + 1, delay.TotalSeconds);
                    await Task.Delay(delay, ct);
                }
                catch (Exception ex)
                {
                    if (chosenEndpoint is not null)
                    {
                        var stats = _endpointStats.GetOrAdd(chosenEndpoint.Id, _ => new EndpointStats());
                        stats.RecordError();
                        logger.LogWarning(ex, "提供商 {Name} 翻译失败", chosenEndpoint.Name);
                    }
                    throw;
                }
            }

            // Should not reach here, but satisfy compiler
            throw new InvalidOperationException("重试已用尽");
        }
        finally
        {
            // Only release semaphore and decrement translating if we actually acquired it
            if (semaphoreAcquired)
            {
                Interlocked.Decrement(ref _translating);
                semaphore.Release();
                _ = BroadcastStats(force: true);
            }
        }
    }

    private static bool IsTransientError(Exception ex) =>
        ex is TaskCanceledException or HttpRequestException or TimeoutException;

    // ── Load Balancer ──

    private ApiEndpointConfig SelectEndpoint(List<ApiEndpointConfig> endpoints)
    {
        if (endpoints.Count == 1) return endpoints[0];

        ApiEndpointConfig? best = null;
        double bestScore = double.MinValue;

        foreach (var ep in endpoints)
        {
            var stats = _endpointStats.GetOrAdd(ep.Id, _ => new EndpointStats());
            var score = CalculateScore(ep.Priority, stats);
            if (score > bestScore)
            {
                bestScore = score;
                best = ep;
            }
        }

        return best ?? endpoints[0];
    }

    private static double CalculateScore(int priority, EndpointStats stats)
    {
        var errorRate = stats.TotalCalls > 0 ? (double)stats.ErrorCount / stats.TotalCalls : 0;
        var avgMs = stats.AverageResponseTimeMs;

        // Priority weight + speed bonus - error penalty
        return priority * 10.0
            - errorRate * 50.0
            + 1000.0 / (avgMs + 100.0);
    }

    // ── Provider dispatch ──

    private async Task<(IList<string> translations, long tokens)> CallProviderAsync(
        ApiEndpointConfig endpoint, AiTranslationSettings ai,
        IList<string> texts, string from, string to,
        List<TermEntry>? glossary, string? gameDescription,
        IList<TranslationMemoryEntry>? memoryContext, string? dntHint, CancellationToken ct,
        string? overrideSystemPrompt = null)
    {
        return endpoint.Provider switch
        {
            LlmProvider.OpenAI => await CallOpenAiCompatAsync(endpoint, ai, texts, from, to, glossary,
                gameDescription, memoryContext, dntHint, GetDefaultBaseUrl(endpoint), ct, overrideSystemPrompt),
            LlmProvider.DeepSeek => await CallOpenAiCompatAsync(endpoint, ai, texts, from, to, glossary,
                gameDescription, memoryContext, dntHint, GetDefaultBaseUrl(endpoint), ct, overrideSystemPrompt),
            LlmProvider.Qwen => await CallOpenAiCompatAsync(endpoint, ai, texts, from, to, glossary,
                gameDescription, memoryContext, dntHint, GetDefaultBaseUrl(endpoint), ct, overrideSystemPrompt),
            LlmProvider.GLM => await CallOpenAiCompatAsync(endpoint, ai, texts, from, to, glossary,
                gameDescription, memoryContext, dntHint, GetDefaultBaseUrl(endpoint), ct, overrideSystemPrompt),
            LlmProvider.Kimi => await CallOpenAiCompatAsync(endpoint, ai, texts, from, to, glossary,
                gameDescription, memoryContext, dntHint, GetDefaultBaseUrl(endpoint), ct, overrideSystemPrompt),
            LlmProvider.Custom => await CallOpenAiCompatAsync(endpoint, ai, texts, from, to, glossary,
                gameDescription, memoryContext, dntHint, endpoint.ApiBaseUrl, ct, overrideSystemPrompt),
            LlmProvider.Claude => await CallClaudeAsync(endpoint, ai, texts, from, to, glossary,
                gameDescription, memoryContext, dntHint, ct, overrideSystemPrompt),
            LlmProvider.Gemini => await CallGeminiAsync(endpoint, ai, texts, from, to, glossary,
                gameDescription, memoryContext, dntHint, ct, overrideSystemPrompt),
            _ => throw new NotSupportedException($"未支持的 LLM 提供商: {endpoint.Provider}")
        };
    }

    private static string GetDefaultBaseUrl(ApiEndpointConfig ep)
    {
        if (!string.IsNullOrWhiteSpace(ep.ApiBaseUrl)) return ep.ApiBaseUrl;
        return ep.Provider switch
        {
            LlmProvider.OpenAI => "https://api.openai.com/v1",
            LlmProvider.DeepSeek => "https://api.deepseek.com",
            LlmProvider.Qwen => "https://dashscope.aliyuncs.com/compatible-mode/v1",
            LlmProvider.GLM => "https://open.bigmodel.cn/api/paas/v4",
            LlmProvider.Kimi => "https://api.moonshot.cn/v1",
            _ => ep.ApiBaseUrl
        };
    }

    private static string GetDefaultModel(ApiEndpointConfig ep) => ep.Provider switch
    {
        LlmProvider.OpenAI => "gpt-4o-mini",
        LlmProvider.Claude => "claude-haiku-4-5-20251001",
        LlmProvider.Gemini => "gemini-2.0-flash",
        LlmProvider.DeepSeek => "deepseek-chat",
        LlmProvider.Qwen => "qwen-plus",
        LlmProvider.GLM => "glm-4-flash",
        LlmProvider.Kimi => "moonshot-v1-auto",
        _ => ""
    };

    // ── Raw LLM call (used by GlossaryExtractionService) ──

    /// <summary>
    /// Call an LLM endpoint with arbitrary system prompt and user content.
    /// Returns the raw text content and token count.
    /// </summary>
    public async Task<(string content, long tokens)> CallLlmRawAsync(
        ApiEndpointConfig endpoint, string systemPrompt, string userContent,
        double temperature, CancellationToken ct)
    {
        return endpoint.Provider switch
        {
            LlmProvider.OpenAI or LlmProvider.DeepSeek or LlmProvider.Qwen
                or LlmProvider.GLM or LlmProvider.Kimi
                => await CallOpenAiCompatRawAsync(endpoint, systemPrompt, userContent,
                    temperature, GetDefaultBaseUrl(endpoint), ct),
            LlmProvider.Custom => await CallOpenAiCompatRawAsync(endpoint, systemPrompt, userContent,
                temperature, endpoint.ApiBaseUrl, ct),
            LlmProvider.Claude => await CallClaudeRawAsync(endpoint, systemPrompt, userContent, temperature, ct),
            LlmProvider.Gemini => await CallGeminiRawAsync(endpoint, systemPrompt, userContent, temperature, ct),
            _ => throw new NotSupportedException($"未支持的 LLM 提供商: {endpoint.Provider}")
        };
    }

    // ── OpenAI-compatible (OpenAI, DeepSeek, Qwen, GLM, Kimi, Custom) ──

    private async Task<(string content, long tokens)> CallOpenAiCompatRawAsync(
        ApiEndpointConfig ep, string systemPrompt, string userContent,
        double temperature, string baseUrl, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(baseUrl))
            throw new InvalidOperationException("API Base URL 未配置");

        var model = string.IsNullOrWhiteSpace(ep.ModelName) ? GetDefaultModel(ep) : ep.ModelName;
        var endpoint = baseUrl.TrimEnd('/') + "/chat/completions";

        var body = new
        {
            model,
            temperature,
            messages = new object[]
            {
                new { role = "system", content = systemPrompt },
                new { role = "user", content = userContent }
            }
        };

        var client = httpClientFactory.CreateClient("LLM");
        using var req = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json")
        };
        req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", ep.ApiKey);

        using var resp = await client.SendAsync(req, ct);
        var json = await resp.Content.ReadAsStringAsync(ct);

        if (!resp.IsSuccessStatusCode)
        {
            logger.LogError("{Provider} API 错误 {Status}: {Body}", ep.Provider, resp.StatusCode, json);
            throw new HttpRequestException($"LLM API 返回错误: {resp.StatusCode}");
        }

        var node = JsonNode.Parse(json);
        var content = node?["choices"]?[0]?["message"]?["content"]?.GetValue<string>()
            ?? throw new InvalidOperationException("LLM 响应格式无效");

        var tokens = ExtractOpenAiTokens(node);
        return (content, tokens);
    }

    private async Task<(IList<string>, long)> CallOpenAiCompatAsync(
        ApiEndpointConfig ep, AiTranslationSettings ai,
        IList<string> texts, string from, string to,
        List<TermEntry>? glossary, string? gameDescription,
        IList<TranslationMemoryEntry>? memoryContext, string? dntHint, string baseUrl, CancellationToken ct,
        string? overrideSystemPrompt = null)
    {
        var systemPrompt = overrideSystemPrompt
            ?? BuildSystemPrompt(ai.SystemPrompt, from, to, glossary, gameDescription, memoryContext, dntHint);
        var userContent = JsonSerializer.Serialize(texts);
        var (content, tokens) = await CallOpenAiCompatRawAsync(ep, systemPrompt, userContent, ai.Temperature, baseUrl, ct);
        return (ParseTranslationArray(content, texts.Count, logger), tokens);
    }

    // ── Claude ──

    private async Task<(string content, long tokens)> CallClaudeRawAsync(
        ApiEndpointConfig ep, string systemPrompt, string userContent,
        double temperature, CancellationToken ct)
    {
        var baseUrl = string.IsNullOrWhiteSpace(ep.ApiBaseUrl) ? "https://api.anthropic.com/v1" : ep.ApiBaseUrl;
        var model = string.IsNullOrWhiteSpace(ep.ModelName) ? GetDefaultModel(ep) : ep.ModelName;
        var endpoint = baseUrl.TrimEnd('/') + "/messages";

        var body = new
        {
            model,
            max_tokens = 4096,
            temperature,
            system = systemPrompt,
            messages = new[] { new { role = "user", content = userContent } }
        };

        var client = httpClientFactory.CreateClient("LLM");
        using var req = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json")
        };
        req.Headers.Add("x-api-key", ep.ApiKey);
        req.Headers.Add("anthropic-version", "2023-06-01");

        using var resp = await client.SendAsync(req, ct);
        var json = await resp.Content.ReadAsStringAsync(ct);

        if (!resp.IsSuccessStatusCode)
        {
            logger.LogError("Claude API 错误 {Status}: {Body}", resp.StatusCode, json);
            throw new HttpRequestException($"LLM API 返回错误: {resp.StatusCode}");
        }

        var node = JsonNode.Parse(json);
        var content = node?["content"]?[0]?["text"]?.GetValue<string>()
            ?? throw new InvalidOperationException("Claude 响应格式无效");

        var inputTokens = node?["usage"]?["input_tokens"]?.GetValue<long>() ?? 0;
        var outputTokens = node?["usage"]?["output_tokens"]?.GetValue<long>() ?? 0;
        return (content, inputTokens + outputTokens);
    }

    private async Task<(IList<string>, long)> CallClaudeAsync(
        ApiEndpointConfig ep, AiTranslationSettings ai,
        IList<string> texts, string from, string to,
        List<TermEntry>? glossary, string? gameDescription,
        IList<TranslationMemoryEntry>? memoryContext, string? dntHint, CancellationToken ct,
        string? overrideSystemPrompt = null)
    {
        var systemPrompt = overrideSystemPrompt
            ?? BuildSystemPrompt(ai.SystemPrompt, from, to, glossary, gameDescription, memoryContext, dntHint);
        var userContent = JsonSerializer.Serialize(texts);
        var (content, tokens) = await CallClaudeRawAsync(ep, systemPrompt, userContent, ai.Temperature, ct);
        return (ParseTranslationArray(content, texts.Count, logger), tokens);
    }

    // ── Gemini ──

    private async Task<(string content, long tokens)> CallGeminiRawAsync(
        ApiEndpointConfig ep, string systemPrompt, string userContent,
        double temperature, CancellationToken ct)
    {
        var model = string.IsNullOrWhiteSpace(ep.ModelName) ? GetDefaultModel(ep) : ep.ModelName;
        var baseUrl = string.IsNullOrWhiteSpace(ep.ApiBaseUrl)
            ? "https://generativelanguage.googleapis.com/v1beta"
            : ep.ApiBaseUrl;
        var endpoint = $"{baseUrl.TrimEnd('/')}/models/{model}:generateContent";

        var combinedContent = systemPrompt + "\n\n" + userContent;
        var body = new
        {
            contents = new[] { new { role = "user", parts = new[] { new { text = combinedContent } } } },
            generationConfig = new { temperature }
        };

        var client = httpClientFactory.CreateClient("LLM");
        using var req = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json")
        };
        req.Headers.TryAddWithoutValidation("x-goog-api-key", ep.ApiKey);

        using var resp = await client.SendAsync(req, ct);
        var json = await resp.Content.ReadAsStringAsync(ct);

        if (!resp.IsSuccessStatusCode)
        {
            logger.LogError("Gemini API 错误 {Status}: {Body}", resp.StatusCode, json);
            throw new HttpRequestException($"LLM API 返回错误: {resp.StatusCode}");
        }

        var node = JsonNode.Parse(json);
        var content = node?["candidates"]?[0]?["content"]?["parts"]?[0]?["text"]?.GetValue<string>()
            ?? throw new InvalidOperationException("Gemini 响应格式无效");

        var promptTokens = node?["usageMetadata"]?["promptTokenCount"]?.GetValue<long>() ?? 0;
        var candidateTokens = node?["usageMetadata"]?["candidatesTokenCount"]?.GetValue<long>() ?? 0;
        return (content, promptTokens + candidateTokens);
    }

    private async Task<(IList<string>, long)> CallGeminiAsync(
        ApiEndpointConfig ep, AiTranslationSettings ai,
        IList<string> texts, string from, string to,
        List<TermEntry>? glossary, string? gameDescription,
        IList<TranslationMemoryEntry>? memoryContext, string? dntHint, CancellationToken ct,
        string? overrideSystemPrompt = null)
    {
        var systemPrompt = overrideSystemPrompt
            ?? BuildSystemPrompt(ai.SystemPrompt, from, to, glossary, gameDescription, memoryContext, dntHint);
        var userContent = JsonSerializer.Serialize(texts);
        var (content, tokens) = await CallGeminiRawAsync(ep, systemPrompt, userContent, ai.Temperature, ct);
        return (ParseTranslationArray(content, texts.Count, logger), tokens);
    }

    // ── Do-not-translate placeholder substitution ──

    // Restore regexes tolerate full-width braces (U+FF5B/U+FF5D) and case variation
    // because Chinese LLMs (Qwen, GLM, Kimi, DeepSeek) commonly output full-width punctuation.
    private static readonly Regex DntRestoreRegex = new(
        @"[{｛]{1,2}\s*DNT_(\d+)\s*[}｝]{1,2}",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex GlossaryRestoreRegex = new(
        @"[{｛]{1,2}\s*G_(\d+)\s*[}｝]{1,2}",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // Strict full-match patterns: detect texts that are ENTIRELY a single placeholder
    private static readonly Regex FullDntPlaceholderRegex = new(
        @"^\{{2}DNT_(\d+)\}{2}$",
        RegexOptions.Compiled);

    private static readonly Regex FullGlossaryPlaceholderRegex = new(
        @"^\{{2}G_(\d+)\}{2}$",
        RegexOptions.Compiled);

    private static (List<string> replacedTexts, Dictionary<string, string> mapping)
        ApplyDoNotTranslateReplacements(IList<string> texts, List<TermEntry> entries)
    {
        var sorted = entries
            .Where(e => !string.IsNullOrWhiteSpace(e.Original))
            .OrderByDescending(e => e.Original.Length)
            .ToList();

        var mapping = new Dictionary<string, string>();
        var textToPlaceholder = new Dictionary<string, string>(StringComparer.Ordinal);
        int nextIndex = 0;

        var result = new List<string>(texts.Count);
        foreach (var text in texts)
        {
            var current = text;
            foreach (var entry in sorted)
            {
                if (entry.CaseSensitive)
                {
                    int searchStart = 0;
                    while (true)
                    {
                        int idx = current.IndexOf(entry.Original, searchStart, StringComparison.Ordinal);
                        if (idx < 0) break;

                        var matched = current.Substring(idx, entry.Original.Length);
                        if (!textToPlaceholder.TryGetValue(matched, out var placeholder))
                        {
                            placeholder = $"{{{{DNT_{nextIndex}}}}}";
                            textToPlaceholder[matched] = placeholder;
                            mapping[placeholder] = matched;
                            nextIndex++;
                        }

                        current = current[..idx] + placeholder + current[(idx + entry.Original.Length)..];
                        searchStart = idx + placeholder.Length;
                    }
                }
                else
                {
                    var pattern = Regex.Escape(entry.Original);
                    current = Regex.Replace(current, pattern, m =>
                    {
                        var matched = m.Value;
                        if (!textToPlaceholder.TryGetValue(matched, out var placeholder))
                        {
                            placeholder = $"{{{{DNT_{nextIndex}}}}}";
                            textToPlaceholder[matched] = placeholder;
                            mapping[placeholder] = matched;
                            nextIndex++;
                        }
                        return placeholder;
                    }, RegexOptions.IgnoreCase);
                }
            }
            result.Add(current);
        }

        return (result, mapping);
    }

    private static List<string> RestoreDoNotTranslatePlaceholders(
        IList<string> translations, Dictionary<string, string> mapping)
    {
        var result = new List<string>(translations.Count);
        foreach (var text in translations)
        {
            var restored = DntRestoreRegex.Replace(text, m =>
            {
                var index = m.Groups[1].Value;
                var fullKey = $"{{{{DNT_{index}}}}}";
                return mapping.TryGetValue(fullKey, out var original) ? original : m.Value;
            });
            result.Add(restored);
        }
        return result;
    }

    // ── Glossary placeholder substitution ──

    private static (List<string> replacedTexts, Dictionary<string, string> mapping)
        ApplyGlossaryReplacements(IList<string> texts, List<TermEntry> nonRegexEntries)
    {
        var sorted = nonRegexEntries
            .OrderByDescending(e => e.Original.Length)
            .ToList();

        // mapping: placeholder → translation (for post-restoration)
        var mapping = new Dictionary<string, string>();
        var originalToPlaceholder = new Dictionary<string, string>(StringComparer.Ordinal);
        int nextIndex = 0;

        var result = new List<string>(texts.Count);
        foreach (var text in texts)
        {
            var current = text;
            foreach (var entry in sorted)
            {
                int searchStart = 0;
                while (true)
                {
                    int idx = current.IndexOf(entry.Original, searchStart, StringComparison.Ordinal);
                    if (idx < 0) break;

                    if (!originalToPlaceholder.TryGetValue(entry.Original, out var placeholder))
                    {
                        placeholder = $"{{{{G_{nextIndex}}}}}";
                        originalToPlaceholder[entry.Original] = placeholder;
                        mapping[placeholder] = entry.Translation ?? "";
                        nextIndex++;
                    }

                    current = current[..idx] + placeholder + current[(idx + entry.Original.Length)..];
                    searchStart = idx + placeholder.Length;
                }
            }
            result.Add(current);
        }

        return (result, mapping);
    }

    private static List<string> RestoreGlossaryPlaceholders(
        IList<string> translations, Dictionary<string, string> mapping)
    {
        var result = new List<string>(translations.Count);
        foreach (var text in translations)
        {
            var restored = GlossaryRestoreRegex.Replace(text, m =>
            {
                var index = m.Groups[1].Value;
                var fullKey = $"{{{{G_{index}}}}}";
                return mapping.TryGetValue(fullKey, out var translation) ? translation : m.Value;
            });
            result.Add(restored);
        }
        return result;
    }

    // ── System prompt + glossary injection ──

    private static string GetCategoryLabel(TermCategory? category) => category switch
    {
        TermCategory.Character => "角色",
        TermCategory.Location => "地点",
        TermCategory.Item => "物品",
        TermCategory.Skill => "技能",
        TermCategory.Organization => "组织",
        TermCategory.General => "通用",
        _ => ""
    };

    /// <summary>
    /// Format term annotation combining category and description.
    /// Uses full-width parentheses for Phase 1 natural mode, ASCII for Phase 2 placeholder mode.
    /// </summary>
    private static void AppendTermAnnotation(StringBuilder sb, TermEntry entry, bool fullWidth)
    {
        var cat = GetCategoryLabel(entry.Category);
        var desc = entry.Description?.Trim();
        var hasCategory = !string.IsNullOrEmpty(cat);
        var hasDescription = !string.IsNullOrEmpty(desc);

        if (!hasCategory && !hasDescription) return;

        sb.Append(fullWidth ? '（' : '(');

        if (hasCategory && hasDescription)
            sb.Append($"{cat}，{desc}");
        else if (hasCategory)
            sb.Append(cat);
        else
            sb.Append(desc);

        sb.Append(fullWidth ? '）' : ')');
    }

    private static string BuildSystemPrompt(string template, string from, string to,
        List<TermEntry>? glossary, string? gameDescription = null,
        IList<TranslationMemoryEntry>? memoryContext = null, string? dntHint = null)
    {
        var sb = new StringBuilder(template.Replace("{from}", from).Replace("{to}", to));

        AppendGameDescription(sb, gameDescription);

        if (glossary is { Count: > 0 })
        {
            sb.Append("\n\n术语表（翻译时必须严格使用以下译文，不得自行翻译）：\n");
            foreach (var entry in glossary)
            {
                if (entry.IsRegex)
                    sb.Append($"  正则匹配: /{entry.Original}/ → {entry.Translation}");
                else
                    sb.Append($"  {entry.Original} → {entry.Translation}");

                AppendTermAnnotation(sb, entry, fullWidth: false);

                sb.Append('\n');
            }
        }

        AppendMemoryContext(sb, memoryContext, from, to);

        if (!string.IsNullOrEmpty(dntHint))
            sb.Append(dntHint);

        return sb.ToString();
    }

    /// <summary>
    /// Build system prompt for Phase 1 natural translation mode.
    /// Includes matched terms in structured format instead of placeholder hints.
    /// </summary>
    private static string BuildSystemPromptWithTerms(string template, string from, string to,
        List<TermEntry> matchedTerms, string? gameDescription = null,
        IList<TranslationMemoryEntry>? memoryContext = null)
    {
        var sb = new StringBuilder(template.Replace("{from}", from).Replace("{to}", to));

        AppendGameDescription(sb, gameDescription);

        var translateTerms = matchedTerms.Where(t => t.Type == TermType.Translate).ToList();
        var dntTerms = matchedTerms.Where(t => t.Type == TermType.DoNotTranslate).ToList();

        if (translateTerms.Count > 0)
        {
            sb.Append("\n\n术语表（翻译时必须严格使用以下译文）：\n");
            foreach (var term in translateTerms)
            {
                if (term.IsRegex)
                    sb.Append($"  正则匹配: /{term.Original}/ → {term.Translation}");
                else
                    sb.Append($"  {term.Original} → {term.Translation}");

                AppendTermAnnotation(sb, term, fullWidth: true);

                sb.Append('\n');
            }
        }

        if (dntTerms.Count > 0)
        {
            sb.Append("\n\n禁止翻译（必须原样保留）：\n");
            foreach (var term in dntTerms)
            {
                sb.Append($"  - {term.Original}");
                AppendTermAnnotation(sb, term, fullWidth: true);
                sb.Append('\n');
            }
        }

        AppendMemoryContext(sb, memoryContext, from, to);

        return sb.ToString();
    }

    private static void AppendGameDescription(StringBuilder sb, string? gameDescription)
    {
        if (!string.IsNullOrWhiteSpace(gameDescription))
        {
            sb.Append("\n\n游戏背景：\n");
            sb.Append(gameDescription);
        }
    }

    private static void AppendMemoryContext(StringBuilder sb,
        IList<TranslationMemoryEntry>? memoryContext, string from, string to)
    {
        if (memoryContext is not { Count: > 0 }) return;
        sb.Append("\n\n以下是近期翻译示例（供参考，保持风格一致）：\n");
        foreach (var entry in memoryContext)
        {
            sb.Append($"  {from}: {entry.Original}\n");
            sb.Append($"  {to}: {entry.Translated}\n");
        }
    }

    // ── Glossary post-processing ──

    private static string ApplyGlossaryPostProcess(string translated, List<TermEntry> glossary,
        ILogger? log = null)
    {
        // Sort longest-first to prevent shorter entries from shadowing longer ones
        // (matches the strategy in ApplyGlossaryReplacements)
        var sorted = glossary
            .Where(e => !string.IsNullOrWhiteSpace(e.Original))
            .OrderByDescending(e => e.Original!.Length)
            .ToList();

        // Phase 1: Apply regex entries (these use Regex.Replace which is independent)
        foreach (var entry in sorted.Where(e => e.IsRegex))
        {
            try
            {
                translated = Regex.Replace(translated, entry.Original!, entry.Translation ?? "",
                    RegexOptions.None, TimeSpan.FromMilliseconds(100));
            }
            catch (Exception ex)
            {
                log?.LogDebug(ex, "术语后处理正则失败: /{Pattern}/", entry.Original);
            }
        }

        // Phase 2: Apply non-regex entries using positional replacement to prevent
        // shorter terms from corrupting substrings inside longer terms' translations.
        // Track replaced spans so shorter terms cannot match inside them.
        var nonRegex = sorted.Where(e => !e.IsRegex).ToList();
        // replacedSpans: list of (start, length) in the current translated string
        // that have been produced by a term replacement and should not be touched.
        var protectedSpans = new List<(int Start, int Length)>();

        foreach (var entry in nonRegex)
        {
            var original = entry.Original!;
            var translation = entry.Translation ?? "";
            int searchStart = 0;

            while (true)
            {
                int idx = translated.IndexOf(original, searchStart, StringComparison.Ordinal);
                if (idx < 0) break;

                // Check if this match overlaps any protected span
                int matchEnd = idx + original.Length;
                bool overlaps = false;
                foreach (var span in protectedSpans)
                {
                    int spanEnd = span.Start + span.Length;
                    if (idx < spanEnd && matchEnd > span.Start)
                    {
                        overlaps = true;
                        // Skip past this protected span
                        searchStart = spanEnd;
                        break;
                    }
                }
                if (overlaps) continue;

                // Perform the replacement
                translated = translated[..idx] + translation + translated[matchEnd..];
                int delta = translation.Length - original.Length;

                // Shift all existing protected spans that come after the replacement point
                for (int i = 0; i < protectedSpans.Count; i++)
                {
                    if (protectedSpans[i].Start >= matchEnd)
                        protectedSpans[i] = (protectedSpans[i].Start + delta, protectedSpans[i].Length);
                }

                // Protect the newly inserted translation span
                protectedSpans.Add((idx, translation.Length));
                searchStart = idx + translation.Length;
            }
        }

        return translated;
    }

    /// <summary>
    /// Replace <paramref name="original"/> with <paramref name="translation"/> in <paramref name="text"/>,
    /// respecting protected spans. Returns modified text and updates protected spans in-place.
    /// </summary>
    private static string ReplaceWithProtection(string text, string original, string translation,
        StringComparison comparison, List<(int Start, int Length)> protectedSpans)
    {
        int searchStart = 0;
        while (true)
        {
            int idx = text.IndexOf(original, searchStart, comparison);
            if (idx < 0) break;

            int matchEnd = idx + original.Length;
            bool overlaps = false;
            foreach (var span in protectedSpans)
            {
                int spanEnd = span.Start + span.Length;
                if (idx < spanEnd && matchEnd > span.Start)
                {
                    overlaps = true;
                    searchStart = spanEnd;
                    break;
                }
            }
            if (overlaps) continue;

            text = text[..idx] + translation + text[matchEnd..];
            int delta = translation.Length - original.Length;

            for (int i = 0; i < protectedSpans.Count; i++)
            {
                if (protectedSpans[i].Start >= matchEnd)
                    protectedSpans[i] = (protectedSpans[i].Start + delta, protectedSpans[i].Length);
            }

            protectedSpans.Add((idx, translation.Length));
            searchStart = idx + translation.Length;
        }
        return text;
    }

    /// <summary>
    /// Find all occurrences of <paramref name="value"/> in <paramref name="text"/> and add them
    /// as protected spans (so shorter terms cannot replace substrings within them).
    /// </summary>
    private static void ProtectExistingSpans(string text, string value,
        StringComparison comparison, List<(int Start, int Length)> protectedSpans)
    {
        int searchStart = 0;
        while (true)
        {
            int idx = text.IndexOf(value, searchStart, comparison);
            if (idx < 0) break;
            protectedSpans.Add((idx, value.Length));
            searchStart = idx + value.Length;
        }
    }

    // ── Token extraction helpers ──

    private static long ExtractOpenAiTokens(JsonNode? node)
    {
        var usage = node?["usage"];
        if (usage is null) return 0;
        var total = usage["total_tokens"]?.GetValue<long>();
        if (total.HasValue) return total.Value;
        var prompt = usage["prompt_tokens"]?.GetValue<long>() ?? 0;
        var completion = usage["completion_tokens"]?.GetValue<long>() ?? 0;
        return prompt + completion;
    }

    // ── Response parsing ──

    private static IList<string> ParseTranslationArray(string content, int expectedCount,
        ILogger? log = null)
    {
        var json = Infrastructure.LlmResponseParser.ExtractJsonArray(content);

        try
        {
            var result = JsonSerializer.Deserialize<List<string>>(json);
            if (result is not null)
                return result;
        }
        catch (Exception ex)
        {
            var preview = content.Length > 200 ? content[..200] + "..." : content;
            log?.LogDebug(ex, "LLM响应JSON解析失败，回退到原文: \"{Preview}\"", preview);
        }

        // Fallback: return raw content for all slots
        return Enumerable.Repeat(content, expectedCount).ToList();
    }

    // ── Concurrency ──

    private readonly object _semaphoreLock = new();

    private void EnsureSemaphore(int maxConcurrency)
    {
        lock (_semaphoreLock)
        {
            if (_semaphore is not null && _currentMaxConcurrency == maxConcurrency) return;
            var old = _semaphore;
            _semaphore = new SemaphoreSlim(maxConcurrency, maxConcurrency);
            _currentMaxConcurrency = maxConcurrency;
            // Defer disposal: in-flight tasks still hold references to the old semaphore.
            // Wait longer than the LLM HTTP timeout (120s) before disposing.
            if (old is not null)
                _ = Task.Delay(TimeSpan.FromMinutes(3)).ContinueWith(_ => old.Dispose());
        }
    }

    // ── SignalR ──

    /// <summary>
    /// Throttled broadcast: skips if last broadcast was less than 200ms ago.
    /// High-concurrency translation can trigger hundreds of state changes per second;
    /// throttling reduces SignalR overhead while keeping the dashboard responsive.
    /// </summary>
    private async Task BroadcastStats(bool force = false)
    {
        if (!force)
        {
            var now = DateTime.UtcNow.Ticks;
            var last = Interlocked.Read(ref _lastBroadcastTicks);
            if (now - last < TimeSpan.FromMilliseconds(200).Ticks)
                return;
            // CAS: only the thread that wins the race proceeds to broadcast
            if (Interlocked.CompareExchange(ref _lastBroadcastTicks, now, last) != last)
                return;
        }

        try
        {
            await hubContext.Clients.Group("ai-translation")
                .SendAsync("statsUpdate", GetStats());
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "AI 翻译统计推送失败");
        }
    }

    // ── Test translation ──

    public async Task<IList<EndpointTestResult>> TestTranslateAsync(
        IList<ApiEndpointConfig> endpoints, string systemPrompt, double temperature, CancellationToken ct)
    {
        if (endpoints.Count == 0)
            throw new InvalidOperationException("没有要测试的 AI 提供商");

        string[] testTexts = ["Hello, world!", "Good morning"];
        var ai = new AiTranslationSettings { SystemPrompt = systemPrompt, Temperature = temperature };

        var tasks = endpoints.Select(async ep =>
        {
            try
            {
                var sw = Stopwatch.StartNew();
                var (translations, _) = await CallProviderAsync(ep, ai, testTexts, "en", "zh", null, null, null, null, ct);
                sw.Stop();
                return new EndpointTestResult(ep.Id, ep.Name, true, translations, null, Math.Round(sw.Elapsed.TotalMilliseconds, 1));
            }
            catch (OperationCanceledException)
            {
                return new EndpointTestResult(ep.Id, ep.Name, false, null, "请求已取消或超时", 0);
            }
            catch (HttpRequestException ex)
            {
                return new EndpointTestResult(ep.Id, ep.Name, false, null, ex.Message, 0);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "测试翻译失败: 提供商 {Name}", ep.Name);
                return new EndpointTestResult(ep.Id, ep.Name, false, null, "连接失败，请检查 API 配置", 0);
            }
        });

        return await Task.WhenAll(tasks);
    }

    // ── Model list fetching ──

    public async Task<IList<string>> FetchModelsAsync(LlmProvider provider, string apiBaseUrl, string apiKey, CancellationToken ct)
    {
        return provider switch
        {
            LlmProvider.OpenAI or LlmProvider.DeepSeek or LlmProvider.Qwen or LlmProvider.Kimi or LlmProvider.Custom
                => await FetchOpenAiModelsAsync(provider, apiBaseUrl, apiKey, ct),
            LlmProvider.Gemini => await FetchGeminiModelsAsync(apiBaseUrl, apiKey, ct),
            _ => [] // Claude, GLM — no model list API
        };
    }

    private async Task<IList<string>> FetchOpenAiModelsAsync(LlmProvider provider, string apiBaseUrl, string apiKey, CancellationToken ct)
    {
        var baseUrl = string.IsNullOrWhiteSpace(apiBaseUrl)
            ? GetDefaultBaseUrl(new ApiEndpointConfig { Provider = provider })
            : apiBaseUrl;
        var endpoint = baseUrl.TrimEnd('/') + "/models";

        var client = httpClientFactory.CreateClient("LLM");
        using var req = new HttpRequestMessage(HttpMethod.Get, endpoint);
        req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);

        using var resp = await client.SendAsync(req, ct);
        if (!resp.IsSuccessStatusCode) return [];

        var json = await resp.Content.ReadAsStringAsync(ct);
        var node = JsonNode.Parse(json);
        var data = node?["data"]?.AsArray();
        if (data is null) return [];

        return data
            .Select(m => m?["id"]?.GetValue<string>())
            .Where(id => !string.IsNullOrEmpty(id))
            .OrderBy(id => id)
            .ToList()!;
    }

    private async Task<IList<string>> FetchGeminiModelsAsync(string apiBaseUrl, string apiKey, CancellationToken ct)
    {
        var baseUrl = string.IsNullOrWhiteSpace(apiBaseUrl)
            ? "https://generativelanguage.googleapis.com/v1beta"
            : apiBaseUrl;
        var endpoint = $"{baseUrl.TrimEnd('/')}/models";

        var client = httpClientFactory.CreateClient("LLM");
        using var req = new HttpRequestMessage(HttpMethod.Get, endpoint);
        req.Headers.TryAddWithoutValidation("x-goog-api-key", apiKey);

        using var resp = await client.SendAsync(req, ct);
        if (!resp.IsSuccessStatusCode) return [];

        var json = await resp.Content.ReadAsStringAsync(ct);
        var node = JsonNode.Parse(json);
        var models = node?["models"]?.AsArray();
        if (models is null) return [];

        return models
            .Select(m => m?["name"]?.GetValue<string>()?.Replace("models/", ""))
            .Where(name => !string.IsNullOrEmpty(name) && name.StartsWith("gemini"))
            .OrderBy(name => name)
            .ToList()!;
    }

    // ── Per-endpoint stats tracker ──

    private sealed class EndpointStats
    {
        private long _totalCalls;
        private long _errorCount;
        private long _totalMs;

        public long TotalCalls => Interlocked.Read(ref _totalCalls);
        public long ErrorCount => Interlocked.Read(ref _errorCount);
        public double AverageResponseTimeMs
        {
            get
            {
                var total = Interlocked.Read(ref _totalCalls) - Interlocked.Read(ref _errorCount);
                return total > 0 ? (double)Interlocked.Read(ref _totalMs) / total : 500;
            }
        }

        public void RecordSuccess(double ms)
        {
            Interlocked.Increment(ref _totalCalls);
            Interlocked.Add(ref _totalMs, (long)ms);
        }

        public void RecordError()
        {
            Interlocked.Increment(ref _totalCalls);
            Interlocked.Increment(ref _errorCount);
        }
    }
}
