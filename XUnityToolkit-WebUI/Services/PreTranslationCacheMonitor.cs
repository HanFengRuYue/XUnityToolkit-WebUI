using System.Collections.Concurrent;
using Microsoft.AspNetCore.SignalR;
using XUnityToolkit_WebUI.Hubs;
using XUnityToolkit_WebUI.Models;

namespace XUnityToolkit_WebUI.Services;

public sealed class PreTranslationCacheMonitor(
    IHubContext<InstallProgressHub> hubContext,
    GameLibraryService gameLibraryService,
    AppSettingsService settingsService,
    ScriptTagService scriptTagService,
    ILogger<PreTranslationCacheMonitor> logger)
{
    private volatile string? _activeGameId;
    private volatile string? _loadAttemptedForGameId;
    private volatile HashSet<string> _preTranslatedKeys = [];
    private readonly ConcurrentDictionary<string, string> _misses = new();
    private long _newTexts;
    private long _lastBroadcastTicks;
    private System.Threading.Timer? _summaryTimer;
    private readonly SemaphoreSlim _loadLock = new(1, 1);

    private const int MaxRecentMisses = 50;
    private readonly ConcurrentQueue<CacheMissEntry> _recentMisses = new();

    public async Task EnsureCacheAsync(string gameId, string toLang, CancellationToken ct)
    {
        if (_loadAttemptedForGameId == gameId) return;

        await _loadLock.WaitAsync(ct);
        try
        {
            if (_loadAttemptedForGameId == gameId) return;

            var settings = await settingsService.GetAsync(ct);
            if (!settings.AiTranslation.EnablePreTranslationCache)
            {
                if (_activeGameId is not null) UnloadCache();
                _loadAttemptedForGameId = gameId;
                return;
            }

            var game = await gameLibraryService.GetByIdAsync(gameId, ct);
            if (game is null)
            {
                _loadAttemptedForGameId = gameId;
                return;
            }

            LoadCache(gameId, game.GamePath, toLang);
            _loadAttemptedForGameId = gameId;
        }
        finally
        {
            _loadLock.Release();
        }
    }

    public void LoadCache(string gameId, string gamePath, string toLang)
    {
        UnloadCache();

        var filePath = Path.Combine(gamePath, "BepInEx", "Translation", toLang, "Text", "_PreTranslated.txt");
        if (!File.Exists(filePath))
        {
            logger.LogInformation("预翻译缓存文件不存在: {GameId}", gameId);
            return;
        }

        var lines = File.ReadAllLines(filePath);
        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line) || line.StartsWith("//")) continue;
            var eqIdx = XUnityTranslationFormat.FindUnescapedEquals(line);
            if (eqIdx < 0) continue;
            var decoded = XUnityTranslationFormat.Decode(line[..eqIdx]);
            keys.Add(scriptTagService.NormalizeForCache(gameId, decoded));
        }

        _preTranslatedKeys = keys;
        _activeGameId = gameId;
        _summaryTimer = new System.Threading.Timer(BroadcastSummary, null, TimeSpan.FromSeconds(60), TimeSpan.FromSeconds(60));

        logger.LogInformation("预翻译缓存已加载: {GameId}, {Count} 条条目", gameId, keys.Count);
    }

    public void UnloadCache()
    {
        var prev = _activeGameId;
        _activeGameId = null;
        _loadAttemptedForGameId = null;
        _preTranslatedKeys = [];
        _misses.Clear();
        Interlocked.Exchange(ref _newTexts, 0);
        _summaryTimer?.Dispose();
        _summaryTimer = null;

        while (_recentMisses.TryDequeue(out _)) { }

        if (prev is not null)
            logger.LogInformation("预翻译缓存已卸载: {GameId}", prev);
    }

    public void RecordTexts(string gameId, IList<string> texts)
    {
        if (_activeGameId != gameId || _preTranslatedKeys.Count == 0) return;

        foreach (var text in texts)
        {
            var normalized = scriptTagService.NormalizeForCache(gameId, text);
            if (_preTranslatedKeys.Contains(normalized))
            {
                // Text was pre-translated but XUnity still asked for it — cache miss
                // (key format/normalization mismatch between pre-translation and runtime)
                if (_misses.TryAdd(normalized, text))
                {
                    var entry = new CacheMissEntry(normalized, text, DateTime.UtcNow);
                    _recentMisses.Enqueue(entry);
                    while (_recentMisses.Count > MaxRecentMisses)
                        _recentMisses.TryDequeue(out _);

                    logger.LogDebug("预翻译缓存未命中: 预翻译键=\"{Key}\", 运行时文本=\"{Runtime}\"", normalized, text);
                }
            }
            else
            {
                Interlocked.Increment(ref _newTexts);
            }
        }

        ThrottledBroadcast();
    }

    public PreTranslationCacheStats GetStats()
    {
        var total = _preTranslatedKeys.Count;
        var misses = _misses.Count;
        var hits = Math.Max(0, total - misses);
        var newTexts = Interlocked.Read(ref _newTexts);
        var hitRate = total > 0 ? (double)hits / total * 100 : 0;

        return new PreTranslationCacheStats(
            total, hits, misses, (int)newTexts, Math.Round(hitRate, 1),
            _recentMisses.ToArray());
    }

    private void ThrottledBroadcast()
    {
        var now = Environment.TickCount64;
        var prev = Interlocked.Read(ref _lastBroadcastTicks);
        if (now - prev < 200) return;
        if (Interlocked.CompareExchange(ref _lastBroadcastTicks, now, prev) != prev) return;

        _ = hubContext.Clients.Group("ai-translation")
            .SendAsync("preCacheStatsUpdate", GetStats());
    }

    private void BroadcastSummary(object? state)
    {
        if (_activeGameId is null) return;
        var stats = GetStats();
        logger.LogInformation(
            "预翻译缓存统计: 总计={Total}, 命中={Hits}, 未命中={Misses}, 命中率={Rate}%",
            stats.TotalPreTranslated, stats.CacheHits, stats.CacheMisses, stats.HitRate);
    }
}
