using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Serialization;
using XUnityToolkit_WebUI.Infrastructure;
using XUnityToolkit_WebUI.Models;

namespace XUnityToolkit_WebUI.Services;

public sealed class DoNotTranslateService(AppDataPaths paths, ILogger<DoNotTranslateService> logger)
{
    private readonly SemaphoreSlim _lock = new(1, 1);
    private readonly ConcurrentDictionary<string, List<DoNotTranslateEntry>> _cache = new();

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() }
    };

    public async Task<List<DoNotTranslateEntry>> GetAsync(string gameId, CancellationToken ct = default)
    {
        // Fast path: return cached list without lock or disk I/O (hot-path safe)
        if (_cache.TryGetValue(gameId, out var cached)) return cached;

        await _lock.WaitAsync(ct);
        try
        {
            // Double-check after acquiring lock
            if (_cache.TryGetValue(gameId, out cached)) return cached;

            var file = paths.DoNotTranslateFile(gameId);
            if (!File.Exists(file))
            {
                _cache[gameId] = [];
                return [];
            }

            var json = await File.ReadAllTextAsync(file, ct);
            var entries = JsonSerializer.Deserialize<List<DoNotTranslateEntry>>(json, JsonOptions) ?? [];
            _cache[gameId] = entries;
            return entries;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task SaveAsync(string gameId, List<DoNotTranslateEntry> entries, CancellationToken ct = default)
    {
        // Filter empty and deduplicate by Original
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var filtered = new List<DoNotTranslateEntry>();
        foreach (var entry in entries)
        {
            if (!string.IsNullOrWhiteSpace(entry.Original) && seen.Add(entry.Original))
                filtered.Add(entry);
        }

        await _lock.WaitAsync(ct);
        try
        {
            var file = paths.DoNotTranslateFile(gameId);
            var json = JsonSerializer.Serialize(filtered, JsonOptions);
            var tmpPath = file + ".tmp";
            await File.WriteAllTextAsync(tmpPath, json, ct);
            File.Move(tmpPath, file, overwrite: true);
            _cache[gameId] = filtered;
            logger.LogInformation("已保存游戏 {GameId} 的禁翻表: {Count} 条", gameId, filtered.Count);
        }
        finally
        {
            _lock.Release();
        }
    }

    public void RemoveCache(string gameId) => _cache.TryRemove(gameId, out _);
}
