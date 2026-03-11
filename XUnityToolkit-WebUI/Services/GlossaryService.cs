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
            await SaveInternalAsync(gameId, entries, ct);
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    /// Atomically merge new entries into an existing glossary, skipping duplicates by Original.
    /// Returns the number of entries actually added.
    /// </summary>
    public async Task<int> MergeAsync(string gameId, List<GlossaryEntry> newEntries, CancellationToken ct = default)
    {
        if (newEntries.Count == 0) return 0;

        await _lock.WaitAsync(ct);
        try
        {
            var existing = _cache.TryGetValue(gameId, out var cached)
                ? cached
                : await LoadFromDiskAsync(gameId, ct);

            var existingOriginals = new HashSet<string>(existing.Select(e => e.Original), StringComparer.Ordinal);
            var toAdd = newEntries.Where(e =>
                !string.IsNullOrWhiteSpace(e.Original) && !existingOriginals.Contains(e.Original)).ToList();
            if (toAdd.Count == 0) return 0;

            var merged = new List<GlossaryEntry>(existing);
            merged.AddRange(toAdd);
            await SaveInternalAsync(gameId, merged, ct);
            logger.LogInformation("自动提取: 游戏 {GameId} 新增 {Count} 条术语（总计 {Total}）",
                gameId, toAdd.Count, merged.Count);
            return toAdd.Count;
        }
        finally
        {
            _lock.Release();
        }
    }

    private async Task<List<GlossaryEntry>> LoadFromDiskAsync(string gameId, CancellationToken ct)
    {
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

    private async Task SaveInternalAsync(string gameId, List<GlossaryEntry> entries, CancellationToken ct)
    {
        var file = paths.GlossaryFile(gameId);
        var json = JsonSerializer.Serialize(entries, JsonOptions);
        var tmpPath = file + ".tmp";
        await File.WriteAllTextAsync(tmpPath, json, ct);
        File.Move(tmpPath, file, overwrite: true);
        _cache[gameId] = entries;
        logger.LogInformation("已保存游戏 {GameId} 的术语库: {Count} 条", gameId, entries.Count);
    }
}
