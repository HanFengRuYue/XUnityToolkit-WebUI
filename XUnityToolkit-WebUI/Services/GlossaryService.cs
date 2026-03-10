using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Serialization;
using XUnityToolkit_WebUI.Infrastructure;
using XUnityToolkit_WebUI.Models;

namespace XUnityToolkit_WebUI.Services;

public sealed class GlossaryService(AppDataPaths paths, ILogger<GlossaryService> logger)
{
    private readonly SemaphoreSlim _lock = new(1, 1);
    private readonly ConcurrentDictionary<string, List<GlossaryEntry>> _cache = new();

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() }
    };

    public async Task<List<GlossaryEntry>> GetAsync(string gameId, CancellationToken ct = default)
    {
        // Fast path: return cached glossary without lock or disk I/O
        if (_cache.TryGetValue(gameId, out var cached)) return cached;

        await _lock.WaitAsync(ct);
        try
        {
            // Double-check after acquiring lock
            if (_cache.TryGetValue(gameId, out cached)) return cached;

            var file = paths.GlossaryFile(gameId);
            if (!File.Exists(file))
            {
                _cache[gameId] = [];
                return [];
            }

            var json = await File.ReadAllTextAsync(file, ct);
            var entries = JsonSerializer.Deserialize<List<GlossaryEntry>>(json, JsonOptions) ?? [];
            _cache[gameId] = entries;
            return entries;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task SaveAsync(string gameId, List<GlossaryEntry> entries, CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            var file = paths.GlossaryFile(gameId);
            var json = JsonSerializer.Serialize(entries, JsonOptions);
            var tmpPath = file + ".tmp";
            await File.WriteAllTextAsync(tmpPath, json, ct);
            File.Move(tmpPath, file, overwrite: true);
            _cache[gameId] = entries; // Update cache
            logger.LogInformation("已保存游戏 {GameId} 的术语库: {Count} 条", gameId, entries.Count);
        }
        finally
        {
            _lock.Release();
        }
    }
}
