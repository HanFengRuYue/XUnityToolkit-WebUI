using System.Collections.Concurrent;
using System.Text;
using Microsoft.AspNetCore.SignalR;
using XUnityToolkit_WebUI.Hubs;
using XUnityToolkit_WebUI.Infrastructure;
using XUnityToolkit_WebUI.Models;

namespace XUnityToolkit_WebUI.Services;

public sealed class PreTranslationService(
    LlmTranslationService translationService,
    GlossaryExtractionService extractionService,
    AppSettingsService settingsService,
    GameLibraryService gameLibrary,
    ScriptTagService scriptTagService,
    AppDataPaths appDataPaths,
    SystemTrayService trayService,
    IHubContext<InstallProgressHub> hubContext,
    ILogger<PreTranslationService> logger)
{
    private readonly ConcurrentDictionary<string, PreTranslationStatus> _statuses = [];
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _cancellations = [];
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = [];

    // ── Broadcast throttle (per-game) ──
    private readonly ConcurrentDictionary<string, long> _lastBroadcastTicks = [];

    /// <summary>Thread-safe progress counters for Parallel.ForEachAsync.</summary>
    private sealed class ProgressCounters
    {
        public int Translated;
        public int Failed;
    }

    public bool IsPreTranslating => _statuses.Values.Any(s => s.State == PreTranslationState.Running);

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

            if (status.State == PreTranslationState.Running)
                throw new InvalidOperationException("预翻译任务已在运行中");

            status.State = PreTranslationState.Running;
            status.TotalTexts = texts.Count;
            status.TranslatedTexts = 0;
            status.FailedTexts = 0;
            status.Error = null;

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
                    status.Error = ex.Message;
                    await BroadcastStatus(gameId, status);
                }
                finally
                {
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

        // Read max concurrency from settings — forced to 1 in local mode
        var settings = await settingsService.GetAsync(ct);
        var isLocalMode = string.Equals(settings.AiTranslation.ActiveMode, "local", StringComparison.OrdinalIgnoreCase);
        var maxConc = isLocalMode ? 1 : Math.Clamp(settings.AiTranslation.MaxConcurrency, 1, 100);
        var batchSize = isLocalMode ? 1 : 10; // Local mode: one text at a time

        var counters = new ProgressCounters();

        // Chunk texts into batches for efficient LLM calls with context
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

                        // Buffer for glossary extraction
                        if (!string.IsNullOrEmpty(gameId))
                            extractionService.BufferTranslation(gameId, batchList[i], results[i]);
                    }
                    if (!string.IsNullOrEmpty(gameId))
                        extractionService.TryTriggerExtraction(gameId);

                    Interlocked.Add(ref counters.Translated, batchList.Count);
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    Interlocked.Add(ref counters.Failed, batch.Length);
                    logger.LogWarning(ex, "预翻译批次失败 ({Count} 条文本)", batch.Length);
                }

                await ThrottledBroadcastStatus(gameId, status, counters);
            });

        // Final status update (single-threaded after Parallel.ForEachAsync completes)
        status.TranslatedTexts = Volatile.Read(ref counters.Translated);
        status.FailedTexts = Volatile.Read(ref counters.Failed);

        // Write translation cache file
        if (translations.Count > 0)
        {
            await WriteTranslationCacheAsync(game.GamePath, toLang, gameId, translations, ct);
            logger.LogInformation("预翻译完成: {Count}/{Total} 条翻译已写入缓存, 游戏 {GameId}",
                translations.Count, textList.Count, gameId);
        }

        status.State = PreTranslationState.Completed;
        await BroadcastStatus(gameId, status);

        trayService.ShowNotification("预翻译完成",
            $"「{game.Name}」已翻译 {translations.Count}/{textList.Count} 条文本");
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
}
