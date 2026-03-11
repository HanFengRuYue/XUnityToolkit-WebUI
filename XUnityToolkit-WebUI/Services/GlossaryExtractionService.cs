using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.SignalR;
using XUnityToolkit_WebUI.Hubs;
using XUnityToolkit_WebUI.Models;

namespace XUnityToolkit_WebUI.Services;

public sealed class GlossaryExtractionService(
    LlmTranslationService translationService,
    GlossaryService glossaryService,
    AppSettingsService settingsService,
    IHubContext<InstallProgressHub> hubContext,
    ILogger<GlossaryExtractionService> logger)
{
    // ── Per-game buffers ──
    private readonly ConcurrentDictionary<string, ConcurrentQueue<TranslationPair>> _buffers = new();
    private readonly ConcurrentDictionary<string, long> _totalPerGame = new();
    private readonly ConcurrentDictionary<string, GameExtractionState> _gameStates = new();

    // ── Concurrency control ──
    private readonly SemaphoreSlim _extractionSemaphore = new(3, 3);

    // ── Stats (thread-safe) ──
    private long _totalExtracted;
    private long _activeExtractions;
    private long _totalExtractionCalls;
    private long _totalErrors;

    // ── Recent extractions circular buffer ──
    private readonly ConcurrentQueue<RecentExtraction> _recentExtractions = new();
    private const int MaxRecentExtractions = 20;

    // ── Broadcast throttle ──
    private long _lastBroadcastTicks;

    private const int ChunkSize = 20;

    private const string ExtractionPrompt =
        "你是一名专业的游戏术语提取专家。根据以下游戏文本的原文和译文对，提取出有价值的术语。\n\n" +
        "请提取以下类型的术语：\n" +
        "1. 角色名、人名\n" +
        "2. 地名、场所名\n" +
        "3. 物品名、道具名\n" +
        "4. 技能名、魔法名\n" +
        "5. 组织名、种族名\n" +
        "6. 其他游戏特有的专有名词\n\n" +
        "要求：\n" +
        "1. 仅返回 JSON 数组，每个元素包含 \"original\"（原文术语）和 \"translation\"（译文术语）字段。\n" +
        "2. 不要提取常见词汇、语法结构或通用表达。\n" +
        "3. 不要提取单个字符、数字或标点符号。\n" +
        "4. 确保提取的术语是完整的词或短语，不是句子片段。\n" +
        "5. 如果没有找到有价值的术语，返回空数组 []。\n" +
        "6. 不要添加任何解释、说明或 markdown 格式。";

    /// <summary>
    /// Buffer a completed translation pair for future extraction.
    /// Called from the translate endpoint after successful translation.
    /// </summary>
    public void BufferTranslation(string gameId, string original, string translated)
    {
        var buffer = _buffers.GetOrAdd(gameId, _ => new ConcurrentQueue<TranslationPair>());
        buffer.Enqueue(new TranslationPair(original, translated));
        _totalPerGame.AddOrUpdate(gameId, 1, (_, v) => v + 1);
    }

    /// <summary>
    /// Check if extraction should trigger for this game and fire it if so.
    /// Returns immediately — extraction runs as background tasks.
    /// </summary>
    public void TryTriggerExtraction(string gameId)
    {
        var total = _totalPerGame.GetOrAdd(gameId, 0);
        var state = _gameStates.GetOrAdd(gameId, _ => new GameExtractionState());
        var interval = CalculateInterval(total);

        var lastAt = Interlocked.Read(ref state.LastExtractionAt);
        if (total - lastAt < interval) return;

        // CAS to prevent duplicate triggers
        if (Interlocked.CompareExchange(ref state.LastExtractionAt, total, lastAt) != lastAt)
            return;

        // Fire-and-forget with exception handling
        _ = DrainAndExtractAsync(gameId, total);
    }

    /// <summary>
    /// Check settings, drain buffer, and run extraction.
    /// Settings are checked BEFORE draining so the buffer stays intact when disabled.
    /// </summary>
    private async Task DrainAndExtractAsync(string gameId, long triggerTotal)
    {
        try
        {
            var settings = await settingsService.GetAsync();
            var ai = settings.AiTranslation;
            if (!ai.GlossaryExtractionEnabled)
                return; // Buffer untouched; LastExtractionAt prevents rapid re-trigger

            // Drain buffer
            if (!_buffers.TryGetValue(gameId, out var buffer)) return;
            var pairs = new List<TranslationPair>();
            while (buffer.TryDequeue(out var pair))
                pairs.Add(pair);

            if (pairs.Count == 0) return;

            logger.LogInformation("术语提取: 触发提取 (游戏 {GameId}, {Count} 条翻译, 累计 {Total})",
                gameId, pairs.Count, triggerTotal);

            await ExtractAllAsync(gameId, pairs, ai);
        }
        catch (Exception ex)
        {
            Interlocked.Increment(ref _totalErrors);
            logger.LogWarning(ex, "术语提取触发失败 (游戏 {GameId})", gameId);
        }
    }

    private async Task ExtractAllAsync(string gameId, List<TranslationPair> pairs, AiTranslationSettings ai)
    {
        // Split into chunks
        var chunks = new List<List<TranslationPair>>();
        for (int i = 0; i < pairs.Count; i += ChunkSize)
        {
            chunks.Add(pairs.GetRange(i, Math.Min(ChunkSize, pairs.Count - i)));
        }

        // Process chunks concurrently
        var tasks = chunks.Select(chunk => ExtractChunkAsync(gameId, chunk, ai));
        await Task.WhenAll(tasks);
    }

    private async Task ExtractChunkAsync(string gameId, List<TranslationPair> pairs, AiTranslationSettings ai)
    {
        var acquired = await _extractionSemaphore.WaitAsync(TimeSpan.FromSeconds(30));
        if (!acquired)
        {
            // Re-queue pairs and reset trigger so they'll be picked up next time
            var buffer = _buffers.GetOrAdd(gameId, _ => new ConcurrentQueue<TranslationPair>());
            foreach (var pair in pairs) buffer.Enqueue(pair);
            var state = _gameStates.GetOrAdd(gameId, _ => new GameExtractionState());
            Interlocked.Exchange(ref state.LastExtractionAt, 0);
            logger.LogWarning("术语提取信号量超时，{Count} 条翻译已重新入队", pairs.Count);
            return;
        }

        try
        {
            Interlocked.Increment(ref _activeExtractions);
            _ = BroadcastStats(force: true);

            // Resolve extraction endpoint
            var endpoint = ResolveEndpoint(ai);
            if (endpoint is null)
            {
                logger.LogWarning("术语提取: 没有可用的 AI 提供商");
                return;
            }

            // Load existing glossary for dedup context
            var existingGlossary = await glossaryService.GetAsync(gameId);

            // Build prompt
            var systemPrompt = BuildExtractionSystemPrompt(existingGlossary);
            var userContent = BuildUserContent(pairs);

            // Call LLM
            Interlocked.Increment(ref _totalExtractionCalls);
            var (content, _) = await translationService.CallLlmRawAsync(
                endpoint, systemPrompt, userContent, 0.1, CancellationToken.None);

            // Parse result
            var entries = ParseExtractionResult(content);
            if (entries.Count == 0) return;

            // Merge into glossary (atomic dedup + save)
            var added = await glossaryService.MergeAsync(gameId, entries);
            if (added > 0)
            {
                Interlocked.Add(ref _totalExtracted, added);

                _recentExtractions.Enqueue(new RecentExtraction(gameId, added, DateTime.UtcNow));
                while (_recentExtractions.Count > MaxRecentExtractions)
                    _recentExtractions.TryDequeue(out _);

                logger.LogInformation("术语提取: 游戏 {GameId} 从 {PairCount} 条翻译中提取了 {Added} 条新术语",
                    gameId, pairs.Count, added);
            }
        }
        catch (Exception ex)
        {
            Interlocked.Increment(ref _totalErrors);
            logger.LogWarning(ex, "术语提取失败 (游戏 {GameId})", gameId);
        }
        finally
        {
            Interlocked.Decrement(ref _activeExtractions);
            _extractionSemaphore.Release();
            _ = BroadcastStats(force: true);
        }
    }

    private ApiEndpointConfig? ResolveEndpoint(AiTranslationSettings ai)
    {
        var enabled = ai.Endpoints.Where(e => e.Enabled && !string.IsNullOrWhiteSpace(e.ApiKey)).ToList();
        if (enabled.Count == 0) return null;

        if (!string.IsNullOrEmpty(ai.GlossaryExtractionEndpointId))
        {
            var specific = enabled.FirstOrDefault(e => e.Id == ai.GlossaryExtractionEndpointId);
            if (specific is not null) return specific;
        }

        // Fallback: use the first enabled endpoint
        return enabled[0];
    }

    private static string BuildExtractionSystemPrompt(List<GlossaryEntry> existingGlossary)
    {
        var sb = new StringBuilder(ExtractionPrompt);

        if (existingGlossary.Count > 0)
        {
            sb.Append("\n\n以下术语已存在于术语表中，请不要重复提取：\n");
            foreach (var entry in existingGlossary.TakeLast(50)) // Limit context size
            {
                sb.Append($"  {entry.Original} → {entry.Translation}\n");
            }
        }

        return sb.ToString();
    }

    private static string BuildUserContent(List<TranslationPair> pairs)
    {
        var items = pairs.Select(p => new { original = p.Original, translated = p.Translated });
        return JsonSerializer.Serialize(items);
    }

    private static List<GlossaryEntry> ParseExtractionResult(string content)
    {
        var json = content.Trim();

        // Strip markdown code fences
        if (json.StartsWith("```"))
        {
            var start = json.IndexOf('[');
            var end = json.LastIndexOf(']');
            if (start >= 0 && end > start)
                json = json[start..(end + 1)];
        }

        try
        {
            var arr = JsonNode.Parse(json)?.AsArray();
            if (arr is null) return [];

            var result = new List<GlossaryEntry>();
            foreach (var item in arr)
            {
                var original = item?["original"]?.GetValue<string>();
                var translation = item?["translation"]?.GetValue<string>();
                if (!string.IsNullOrWhiteSpace(original) && !string.IsNullOrWhiteSpace(translation))
                {
                    result.Add(new GlossaryEntry
                    {
                        Original = original,
                        Translation = translation,
                        IsRegex = false
                    });
                }
            }
            return result;
        }
        catch
        {
            return [];
        }
    }

    /// <summary>
    /// Calculate extraction trigger interval based on total translations per game.
    /// Starts at 5, grows linearly, caps at 20.
    /// </summary>
    private static long CalculateInterval(long totalTranslated)
        => Math.Min(20, Math.Max(5, totalTranslated / 5));

    public GlossaryExtractionStats GetStats() => new(
        Enabled: true, // Actual enabled state is in settings; this is for the stats shape
        TotalExtracted: Interlocked.Read(ref _totalExtracted),
        ActiveExtractions: (int)Interlocked.Read(ref _activeExtractions),
        TotalExtractionCalls: Interlocked.Read(ref _totalExtractionCalls),
        TotalErrors: Interlocked.Read(ref _totalErrors),
        RecentExtractions: _recentExtractions.ToArray()
    );

    private async Task BroadcastStats(bool force = false)
    {
        if (!force)
        {
            var now = DateTime.UtcNow.Ticks;
            var last = Interlocked.Read(ref _lastBroadcastTicks);
            if (now - last < TimeSpan.FromMilliseconds(500).Ticks)
                return;
            if (Interlocked.CompareExchange(ref _lastBroadcastTicks, now, last) != last)
                return;
        }

        try
        {
            await hubContext.Clients.Group("ai-translation")
                .SendAsync("extractionStatsUpdate", GetStats());
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "术语提取统计推送失败");
        }
    }

    private sealed record TranslationPair(string Original, string Translated);

    private sealed class GameExtractionState
    {
        public long LastExtractionAt;
    }
}
