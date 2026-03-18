using System.Collections.Concurrent;
using System.Text;
using Microsoft.AspNetCore.SignalR;
using XUnityToolkit_WebUI.Hubs;
using XUnityToolkit_WebUI.Infrastructure;
using XUnityToolkit_WebUI.Models;

namespace XUnityToolkit_WebUI.Services;

public sealed class PreTranslationService(
    LlmTranslationService translationService,
    AppSettingsService settingsService,
    GameLibraryService gameLibrary,
    ScriptTagService scriptTagService,
    AppDataPaths appDataPaths,
    SystemTrayService trayService,
    DynamicPatternService dynamicPatternService,
    TermExtractionService termExtractionService,
    TranslationMemoryService translationMemoryService,
    IHubContext<InstallProgressHub> hubContext,
    ILogger<PreTranslationService> logger)
{
    private readonly ConcurrentDictionary<string, PreTranslationStatus> _statuses = [];
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _cancellations = [];
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = [];
    private readonly ConcurrentDictionary<string, TaskCompletionSource> _termReviewCompletions = [];

    // ── Broadcast throttle (per-game) ──
    private readonly ConcurrentDictionary<string, long> _lastBroadcastTicks = [];

    /// <summary>Thread-safe progress counters for Parallel.ForEachAsync.</summary>
    private sealed class ProgressCounters
    {
        public int Translated;
        public int Failed;
    }

    public bool IsPreTranslating => _statuses.Values.Any(s =>
        s.State is PreTranslationState.Running or PreTranslationState.AwaitingTermReview);

    public PreTranslationStatus GetStatus(string gameId)
    {
        return _statuses.GetOrAdd(gameId, id => new PreTranslationStatus { GameId = id });
    }

    public async Task<PreTranslationStatus> StartPreTranslationAsync(
        string gameId, List<ExtractedText> texts, string fromLang, string toLang)
    {
        var gameLock = _locks.GetOrAdd(gameId, _ => new SemaphoreSlim(1, 1));
        await gameLock.WaitAsync();
        try
        {
            var status = GetStatus(gameId);

            if (status.State is PreTranslationState.Running or PreTranslationState.AwaitingTermReview)
                throw new InvalidOperationException("预翻译任务已在运行中");

            status.State = PreTranslationState.Running;
            status.TotalTexts = texts.Count;
            status.TranslatedTexts = 0;
            status.FailedTexts = 0;
            status.Error = null;
            status.CurrentRound = 0;
            status.CurrentPhase = null;
            status.ExtractedTermCount = 0;
            status.DynamicPatternCount = 0;

            var cts = new CancellationTokenSource();
            if (_cancellations.TryRemove(gameId, out var oldCts))
                oldCts.Dispose();
            _cancellations[gameId] = cts;

            _ = Task.Run(async () =>
            {
                try
                {
                    await ExecutePreTranslationAsync(gameId, texts, fromLang, toLang, status, cts.Token);
                }
                catch (OperationCanceledException)
                {
                    status.State = PreTranslationState.Cancelled;
                    status.Error = "预翻译已取消";
                    await BroadcastStatus(gameId, status);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "预翻译失败: 游戏 {GameId}", gameId);
                    status.State = PreTranslationState.Failed;
                    status.Error = "预翻译过程中发生内部错误，请查看日志获取详情";
                    await BroadcastStatus(gameId, status);
                }
                finally
                {
                    _termReviewCompletions.TryRemove(gameId, out _);
                    if (_cancellations.TryRemove(gameId, out var doneCts))
                        doneCts.Dispose();
                }
            });

            return status;
        }
        finally
        {
            gameLock.Release();
        }
    }

    public void Cancel(string gameId)
    {
        if (_cancellations.TryGetValue(gameId, out var cts))
            cts.Cancel();

        // Also unblock any pending term review wait
        if (_termReviewCompletions.TryRemove(gameId, out var tcs))
            tcs.TrySetCanceled();
    }

    /// <summary>
    /// Resume pre-translation after term review. Called from the endpoint when the user
    /// has finished reviewing extracted terms.
    /// </summary>
    public void ResumeAfterTermReview(string gameId)
    {
        if (_termReviewCompletions.TryRemove(gameId, out var tcs))
            tcs.TrySetResult();
    }

    private async Task ExecutePreTranslationAsync(
        string gameId, List<ExtractedText> texts, string fromLang, string toLang,
        PreTranslationStatus status, CancellationToken ct)
    {
        var game = await gameLibrary.GetByIdAsync(gameId, ct)
            ?? throw new KeyNotFoundException($"Game {gameId} not found.");

        await BroadcastStatus(gameId, status);

        var translations = new ConcurrentDictionary<string, string>();
        var textList = await scriptTagService.FilterAndCleanAsync(gameId, texts, ct);
        logger.LogInformation("脚本标签清洗: {Original} 条提取文本 → {Cleaned} 条翻译文本, 游戏 {GameId}",
            texts.Count, textList.Count, gameId);
        status.TotalTexts = textList.Count;

        var settings = await settingsService.GetAsync(ct);
        var aiSettings = settings.AiTranslation;
        var isLocalMode = string.Equals(aiSettings.ActiveMode, "local", StringComparison.OrdinalIgnoreCase);
        var maxConc = isLocalMode ? 1 : Math.Clamp(aiSettings.MaxConcurrency, 1, 100);
        var batchSize = isLocalMode ? 1 : 10;

        // ── Pre-Round 1: Template variable detection (regex-based, zero LLM cost) ──
        var templateVars = dynamicPatternService.DetectTemplateVariables(textList);
        logger.LogInformation("模板变量检测: 发现 {Count} 条含模板变量的文本, 游戏 {GameId}",
            templateVars.Count, gameId);

        var dynamicPatternCount = 0;

        // ── Round 1: Batch Translation ──
        status.CurrentRound = 1;
        status.CurrentPhase = "round1";
        status.PhaseProgress = 0;
        status.PhaseTotal = 0;
        await BroadcastStatus(gameId, status);
        await BroadcastEvent(gameId, "roundProgress",
            new { gameId, round = 1, phase = "translating" });

        var round1Counters = new ProgressCounters();
        var batches = textList.Chunk(batchSize).ToList();

        await Parallel.ForEachAsync(batches,
            new ParallelOptions { MaxDegreeOfParallelism = maxConc, CancellationToken = ct },
            async (batch, token) =>
            {
                try
                {
                    var batchList = batch.ToList();
                    var results = await translationService.TranslateAsync(
                        batchList, fromLang, toLang, gameId, token);

                    for (int i = 0; i < batchList.Count; i++)
                    {
                        translations[batchList[i]] = results[i];
                    }

                    Interlocked.Add(ref round1Counters.Translated, batchList.Count);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
                catch (Exception ex)
                {
                    Interlocked.Add(ref round1Counters.Failed, batch.Length);
                    logger.LogWarning(ex, "预翻译批次失败 ({Count} 条文本)", batch.Length);
                }

                await ThrottledBroadcastStatus(gameId, status, round1Counters);
            });

        // Snapshot Round 1 final counters
        status.TranslatedTexts = Volatile.Read(ref round1Counters.Translated);
        status.FailedTexts = Volatile.Read(ref round1Counters.Failed);
        await BroadcastStatus(gameId, status);

        logger.LogInformation("Round 1 完成: {Translated}/{Total} 翻译成功, {Failed} 失败, 游戏 {GameId}",
            status.TranslatedTexts, textList.Count, status.FailedTexts, gameId);

        // ── Post-Round 1: LLM Pattern Analysis (needs translation pairs) ──
        if (aiSettings.EnableLlmPatternAnalysis && translations.Count > 0)
        {
            ct.ThrowIfCancellationRequested();
            status.CurrentPhase = "patternAnalysis";
            status.PhaseProgress = 0;
            status.PhaseTotal = 0;
            await BroadcastStatus(gameId, status);
            try
            {
                var pairs = translations
                    .Select(kv => (Original: kv.Key, Translation: kv.Value))
                    .ToList();
                logger.LogInformation("开始模式分析: {Count} 对翻译, 游戏 {GameId}", pairs.Count, gameId);
                dynamicPatternCount = await dynamicPatternService.AnalyzeDynamicFragmentsAsync(
                    gameId, pairs, ct,
                    onBatchProgress: (done, total) =>
                    {
                        status.PhaseProgress = done;
                        status.PhaseTotal = total;
                        _ = BroadcastStatus(gameId, status);
                    });
                status.DynamicPatternCount = dynamicPatternCount;
                await BroadcastStatus(gameId, status);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "LLM 动态模式分析失败, 游戏 {GameId}", gameId);
            }
        }

        // ── Post-Round 1: Term Extraction ──
        var extractedTermCount = 0;
        if (aiSettings.EnableAutoTermExtraction && translations.Count > 0)
        {
            ct.ThrowIfCancellationRequested();
            status.CurrentPhase = "termExtraction";
            status.PhaseProgress = 0;
            status.PhaseTotal = 0;
            await BroadcastStatus(gameId, status);

            try
            {
                var pairs = translations
                    .Select(kv => (original: kv.Key, translation: kv.Value))
                    .ToList();
                logger.LogInformation("开始术语提取: {Count} 对翻译, 游戏 {GameId}", pairs.Count, gameId);
                var candidates = await termExtractionService.ExtractFromPairsAsync(
                    gameId, pairs, ct,
                    onBatchProgress: (done, total) =>
                    {
                        status.PhaseProgress = done;
                        status.PhaseTotal = total;
                        _ = BroadcastStatus(gameId, status);
                    });
                extractedTermCount = candidates.Count;
                status.ExtractedTermCount = extractedTermCount;
                await BroadcastStatus(gameId, status);

                if (extractedTermCount > 0)
                {
                    await BroadcastEvent(gameId, "termExtractionComplete",
                        new { gameId, candidateCount = extractedTermCount });

                    // Term review gate: pause if manual review is required
                    if (!aiSettings.AutoApplyExtractedTerms)
                    {
                        // Create and store TCS BEFORE broadcasting — otherwise
                        // ResumeAfterTermReview can fire before TCS exists (race condition)
                        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                        _termReviewCompletions[gameId] = tcs;

                        status.State = PreTranslationState.AwaitingTermReview;
                        status.CurrentPhase = "termReview";
                        await BroadcastStatus(gameId, status);

                        logger.LogInformation("等待术语审核: {Count} 条候选术语, 游戏 {GameId}",
                            extractedTermCount, gameId);

                        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                        timeoutCts.CancelAfter(TimeSpan.FromMinutes(5));

                        try
                        {
                            await tcs.Task.WaitAsync(timeoutCts.Token);
                            logger.LogInformation("术语审核完成，继续预翻译, 游戏 {GameId}", gameId);
                        }
                        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                        {
                            // Timeout — auto-apply and continue
                            logger.LogInformation("术语审核超时，自动应用候选术语, 游戏 {GameId}", gameId);
                        }
                        finally
                        {
                            _termReviewCompletions.TryRemove(gameId, out _);
                        }

                        ct.ThrowIfCancellationRequested();
                        status.State = PreTranslationState.Running;
                    }

                    // Apply extracted terms (either auto-apply or after review/timeout)
                    try
                    {
                        var applied = await termExtractionService.ApplyCandidatesAsync(gameId, null, ct);
                        logger.LogInformation("已应用 {Count} 条提取术语, 游戏 {GameId}", applied, gameId);
                    }
                    catch (Exception ex)
                    {
                        logger.LogWarning(ex, "应用提取术语失败, 游戏 {GameId}", gameId);
                    }
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "术语提取失败, 游戏 {GameId}", gameId);
            }
        }

        // ── Round 2: Re-translate with Terms (if enabled) ──
        if (aiSettings.EnableMultiRoundTranslation && translations.Count > 0 && extractedTermCount > 0)
        {
            ct.ThrowIfCancellationRequested();

            status.CurrentRound = 2;
            status.CurrentPhase = "round2";
            status.PhaseProgress = 0;
            status.PhaseTotal = 0;
            await BroadcastStatus(gameId, status);
            await BroadcastEvent(gameId, "roundProgress",
                new { gameId, round = 2, phase = "translating" });

            logger.LogInformation("开始 Round 2 (带术语重新翻译): 游戏 {GameId}", gameId);

            // Re-translate all texts — the standard Phase 1/2/3 pipeline
            // will automatically pick up the newly extracted terms
            var round2Translations = new ConcurrentDictionary<string, string>();
            var round2Counters = new ProgressCounters();

            // Reset progress for Round 2
            status.TranslatedTexts = 0;
            status.FailedTexts = 0;
            await BroadcastStatus(gameId, status);

            await Parallel.ForEachAsync(batches,
                new ParallelOptions { MaxDegreeOfParallelism = maxConc, CancellationToken = ct },
                async (batch, token) =>
                {
                    try
                    {
                        var batchList = batch.ToList();
                        var results = await translationService.TranslateAsync(
                            batchList, fromLang, toLang, gameId, token);

                        for (int i = 0; i < batchList.Count; i++)
                            round2Translations[batchList[i]] = results[i];

                        Interlocked.Add(ref round2Counters.Translated, batchList.Count);
                    }
                    catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
                    catch (Exception ex)
                    {
                        Interlocked.Add(ref round2Counters.Failed, batch.Length);
                        logger.LogWarning(ex, "Round 2 预翻译批次失败 ({Count} 条文本)", batch.Length);
                    }

                    await ThrottledBroadcastStatus(gameId, status, round2Counters);
                });

            // Merge Round 2 results (overwrite Round 1 translations)
            foreach (var (key, value) in round2Translations)
                translations[key] = value;

            status.TranslatedTexts = Volatile.Read(ref round2Counters.Translated);
            status.FailedTexts = Volatile.Read(ref round2Counters.Failed);

            logger.LogInformation("Round 2 完成: {Translated}/{Total} 翻译成功, 游戏 {GameId}",
                status.TranslatedTexts, textList.Count, gameId);
        }

        // ── Write Cache Files ──
        status.CurrentPhase = "writeCache";
        status.PhaseProgress = 0;
        status.PhaseTotal = 0;
        await BroadcastStatus(gameId, status);

        if (translations.Count > 0)
        {
            await WriteTranslationCacheAsync(game.GamePath, toLang, gameId, translations, ct);

            // Generate dynamic regex entries and append to regex file
            if (templateVars.Count > 0)
            {
                try
                {
                    await AppendDynamicRegexEntriesAsync(
                        game.GamePath, toLang, gameId, textList, translations, templateVars, ct);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "写入动态正则表达式失败, 游戏 {GameId}", gameId);
                }
            }

            logger.LogInformation("预翻译完成: {Count}/{Total} 条翻译已写入缓存, 游戏 {GameId}",
                translations.Count, textList.Count, gameId);
        }

        // ── Batch Write to Translation Memory ──
        if (translations.Count > 0)
        {
            try
            {
                var originals = translations.Keys.ToList();
                var translationValues = originals.Select(k => translations[k]).ToList();
                var finalRound = status.CurrentRound > 0 ? status.CurrentRound : 1;
                translationMemoryService.Add(gameId, originals, translationValues,
                    finalRound, isFinal: true);
                await translationMemoryService.FlushAsync(gameId, ct);
                logger.LogInformation("翻译记忆库: 写入 {Count} 条, 游戏 {GameId}",
                    translations.Count, gameId);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "写入翻译记忆库失败, 游戏 {GameId}", gameId);
            }
        }

        status.State = PreTranslationState.Completed;
        await BroadcastStatus(gameId, status);

        trayService.ShowNotification("预翻译完成",
            $"「{game.Name}」已翻译 {translations.Count}/{textList.Count} 条文本");
    }

    /// <summary>
    /// Generate dynamic regex entries from template variables and append to the regex file.
    /// </summary>
    private async Task AppendDynamicRegexEntriesAsync(
        string gamePath, string toLang, string gameId,
        List<string> textList,
        ConcurrentDictionary<string, string> translations,
        List<(int Index, string Text, List<(int Start, int End, string Variable)> Variables)> templateVars,
        CancellationToken ct)
    {
        // Build entries for GenerateRegexEntries: need (Original, Translation, Variables)
        var entries = new List<(string Original, string Translation, List<(int Start, int End, string Variable)> Variables)>();
        foreach (var (index, text, variables) in templateVars)
        {
            if (index >= 0 && index < textList.Count && translations.TryGetValue(text, out var translation))
            {
                entries.Add((text, translation, variables));
            }
        }

        if (entries.Count == 0) return;

        var regexEntries = dynamicPatternService.GenerateRegexEntries(entries);
        if (regexEntries.Count == 0) return;

        var dir = Path.Combine(gamePath, "BepInEx", "Translation", toLang, "Text");
        var filePath = Path.Combine(dir, "_PreTranslated_Regex.txt");

        var sb = new StringBuilder();
        sb.AppendLine();
        sb.AppendLine("// Dynamic template variable patterns");
        foreach (var (pattern, replacement) in regexEntries)
        {
            sb.AppendLine($"r:\"{pattern}\"={replacement}");
        }

        await File.AppendAllTextAsync(filePath, sb.ToString(), ct);
        logger.LogInformation("动态正则: 写入 {Count} 条模式, 游戏 {GameId}", regexEntries.Count, gameId);
    }

    /// <summary>
    /// Write translations to XUnity.AutoTranslator cache file format.
    /// Format: encoded_original=encoded_translation (one per line)
    /// When cache optimization is enabled, normalizes keys via NormalizeForCache and generates regex patterns.
    /// </summary>
    private async Task WriteTranslationCacheAsync(
        string gamePath, string toLang, string gameId,
        ConcurrentDictionary<string, string> translations,
        CancellationToken ct)
    {
        var settings = await settingsService.GetAsync(ct);
        var enableCacheOptimization = settings.AiTranslation.EnablePreTranslationCache;

        var translationDir = Path.Combine(gamePath, "BepInEx", "Translation", toLang, "Text");
        Directory.CreateDirectory(translationDir);

        var filePath = Path.Combine(translationDir, "_PreTranslated.txt");

        // Load existing translations to avoid duplicates
        var existing = new HashSet<string>();
        if (File.Exists(filePath))
        {
            foreach (var line in await File.ReadAllLinesAsync(filePath, ct))
            {
                if (string.IsNullOrWhiteSpace(line) || line.StartsWith("//")) continue;
                var eqIdx = FindUnescapedEquals(line);
                if (eqIdx > 0)
                    existing.Add(line[..eqIdx]);
            }
        }

        var sb = new StringBuilder();
        if (!File.Exists(filePath))
            sb.AppendLine("// Pre-translated by XUnity Toolkit");

        foreach (var (original, translation) in translations)
        {
            var originalKey = enableCacheOptimization
                ? scriptTagService.NormalizeForCache(gameId, original)
                : original;
            if (string.IsNullOrEmpty(originalKey)) continue;

            var encodedOriginal = EncodeForXUnity(originalKey);
            if (existing.Contains(encodedOriginal)) continue;

            var encodedTranslation = EncodeForXUnity(translation);
            sb.AppendLine($"{encodedOriginal}={encodedTranslation}");
        }

        await File.AppendAllTextAsync(filePath, sb.ToString(), ct);

        if (enableCacheOptimization)
            await WriteRegexPatternsAsync(gamePath, toLang, gameId, ct);
    }

    /// <summary>
    /// Generate regex pattern file for multi-line text concatenation matching.
    /// Appends any user-defined custom patterns from the app data directory.
    /// </summary>
    private async Task WriteRegexPatternsAsync(string gamePath, string toLang, string gameId, CancellationToken ct)
    {
        var dir = Path.Combine(gamePath, "BepInEx", "Translation", toLang, "Text");
        Directory.CreateDirectory(dir);
        var filePath = Path.Combine(dir, "_PreTranslated_Regex.txt");

        var sb = new StringBuilder();
        sb.AppendLine("// Pre-translation regex patterns generated by XUnity Toolkit");
        sb.AppendLine();
        sb.AppendLine("// Two-line concatenation (both groups are text)");
        sb.AppendLine(@"sr:""^([\S\s]+?)\n([\S\s]+)$""=$1\n$2");
        sb.AppendLine();
        sb.AppendLine("// Three-line concatenation");
        sb.AppendLine(@"sr:""^([\S\s]+?)\n([\S\s]+?)\n([\S\s]+)$""=$1\n$2\n$3");

        // Append user custom patterns if they exist
        var customFile = appDataPaths.PreTranslationRegexFile(gameId);
        if (File.Exists(customFile))
        {
            sb.AppendLine();
            sb.AppendLine("// Custom patterns");
            var customContent = await File.ReadAllTextAsync(customFile, ct);
            sb.Append(customContent);
        }

        await File.WriteAllTextAsync(filePath, sb.ToString(), ct);
    }

    private static string EncodeForXUnity(string text) => XUnityTranslationFormat.Encode(text);
    private static int FindUnescapedEquals(string line) => XUnityTranslationFormat.FindUnescapedEquals(line);

    /// <summary>
    /// Throttled broadcast: CAS-based, skips if last broadcast was less than 200ms ago.
    /// Only the CAS-winning thread snapshots the counters into the status object,
    /// avoiding concurrent writes to the plain int properties.
    /// </summary>
    private async Task ThrottledBroadcastStatus(
        string gameId, PreTranslationStatus status, ProgressCounters counters)
    {
        var now = DateTime.UtcNow.Ticks;
        var last = _lastBroadcastTicks.GetOrAdd(gameId, 0L);
        if (now - last < TimeSpan.FromMilliseconds(200).Ticks)
            return;
        if (!_lastBroadcastTicks.TryUpdate(gameId, now, last))
            return;

        status.TranslatedTexts = Volatile.Read(ref counters.Translated);
        status.FailedTexts = Volatile.Read(ref counters.Failed);
        await BroadcastStatus(gameId, status);
    }

    private async Task BroadcastStatus(string gameId, PreTranslationStatus status)
    {
        try
        {
            await hubContext.Clients.Group($"pre-translation-{gameId}")
                .SendAsync("preTranslationUpdate", status);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "预翻译状态推送失败: 游戏 {GameId}", gameId);
        }
    }

    private async Task BroadcastEvent(string gameId, string eventName, object data)
    {
        try
        {
            await hubContext.Clients.Group($"pre-translation-{gameId}")
                .SendAsync(eventName, data);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "预翻译事件推送失败: {Event}, 游戏 {GameId}", eventName, gameId);
        }
    }
}
