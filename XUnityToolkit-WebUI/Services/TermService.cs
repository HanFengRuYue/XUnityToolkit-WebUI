using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Serialization;
using XUnityToolkit_WebUI.Infrastructure;
using XUnityToolkit_WebUI.Models;

namespace XUnityToolkit_WebUI.Services;

public sealed class TermService(AppDataPaths paths, ILogger<TermService> logger)
{
    private readonly SemaphoreSlim _lock = new(1, 1);
    private readonly ConcurrentDictionary<string, List<TermEntry>> _cache = new();

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() }
    };

    public async Task<List<TermEntry>> GetAsync(string gameId, CancellationToken ct = default)
    {
        // Fast path: return cached terms without lock or disk I/O
        if (_cache.TryGetValue(gameId, out var cached)) return cached;

        await _lock.WaitAsync(ct);
        try
        {
            // Double-check after acquiring lock
            if (_cache.TryGetValue(gameId, out cached)) return cached;

            var file = paths.GlossaryFile(gameId);
            List<TermEntry> entries;

            if (File.Exists(file))
            {
                var json = await File.ReadAllTextAsync(file, ct);
                entries = JsonSerializer.Deserialize<List<TermEntry>>(json, JsonOptions) ?? [];
            }
            else
            {
                entries = [];
            }

            // Migrate old DoNotTranslate file if it exists
#pragma warning disable CS0612 // Obsolete DoNotTranslateDirectory needed for migration
            var dntFile = paths.DoNotTranslateFile(gameId);
#pragma warning restore CS0612
            if (File.Exists(dntFile))
            {
                var dntJson = await File.ReadAllTextAsync(dntFile, ct);
                var dntEntries = JsonSerializer.Deserialize<List<LegacyDntEntry>>(dntJson, JsonOptions) ?? [];

                var existingOriginals = new HashSet<string>(
                    entries.Select(e => e.Original), StringComparer.Ordinal);

                foreach (var dnt in dntEntries)
                {
                    if (!string.IsNullOrWhiteSpace(dnt.Original) && existingOriginals.Add(dnt.Original))
                    {
                        entries.Add(new TermEntry
                        {
                            Type = TermType.DoNotTranslate,
                            Original = dnt.Original,
                            CaseSensitive = dnt.CaseSensitive
                        });
                    }
                }

                // Save unified file and rename old DNT file
                await SaveInternalAsync(gameId, entries, ct);
                var migratedPath = dntFile + ".migrated";
                File.Move(dntFile, migratedPath, overwrite: true);
                logger.LogInformation(
                    "已迁移游戏 {GameId} 的禁翻表到统一术语库: {Count} 条",
                    gameId, dntEntries.Count);
            }

            _cache[gameId] = entries;
            return entries;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task SaveAsync(string gameId, List<TermEntry> entries, CancellationToken ct = default)
    {
        // Filter blank Original and deduplicate by Original (ordinal)
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var filtered = new List<TermEntry>();
        foreach (var entry in entries)
        {
            if (!string.IsNullOrWhiteSpace(entry.Original) && seen.Add(entry.Original))
                filtered.Add(entry);
        }

        await _lock.WaitAsync(ct);
        try
        {
            await SaveInternalAsync(gameId, filtered, ct);
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    /// Merge new entries into existing terms, skipping duplicates by Original (ordinal).
    /// </summary>
    public async Task MergeAsync(string gameId, List<TermEntry> newEntries, CancellationToken ct = default)
    {
        if (newEntries.Count == 0) return;

        await _lock.WaitAsync(ct);
        try
        {
            var existing = _cache.TryGetValue(gameId, out var cached)
                ? cached
                : await LoadFromDiskAsync(gameId, ct);

            var existingOriginals = new HashSet<string>(
                existing.Select(e => e.Original), StringComparer.Ordinal);
            var toAdd = newEntries.Where(e =>
                !string.IsNullOrWhiteSpace(e.Original) && !existingOriginals.Contains(e.Original)).ToList();
            if (toAdd.Count == 0) return;

            var merged = new List<TermEntry>(existing);
            merged.AddRange(toAdd);
            await SaveInternalAsync(gameId, merged, ct);
            logger.LogInformation("游戏 {GameId} 合并新增 {Count} 条术语（总计 {Total}）",
                gameId, toAdd.Count, merged.Count);
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    /// Replace all entries of the specified type with new entries. Used by compat proxy PUT endpoints.
    /// </summary>
    public async Task ReplaceByTypeAsync(string gameId, TermType type, List<TermEntry> newEntries,
        CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            var existing = _cache.TryGetValue(gameId, out var cached)
                ? cached
                : await LoadFromDiskAsync(gameId, ct);

            // Remove all entries of the specified type, then add the new ones
            var kept = existing.Where(e => e.Type != type).ToList();
            kept.AddRange(newEntries.Where(e => !string.IsNullOrWhiteSpace(e.Original)));
            await SaveInternalAsync(gameId, kept, ct);
            logger.LogInformation("游戏 {GameId} 替换类型 {Type} 术语: {Count} 条（总计 {Total}）",
                gameId, type, newEntries.Count, kept.Count);
        }
        finally
        {
            _lock.Release();
        }
    }

    public void RemoveCache(string gameId) => _cache.TryRemove(gameId, out _);

    /// <summary>
    /// Import terms from one game to another, dedup by Original.
    /// Returns (added, skipped) counts.
    /// </summary>
    public async Task<(int Added, int Skipped)> ImportFromGameAsync(
        string targetGameId, string sourceGameId, CancellationToken ct = default)
    {
        var sourceTerms = await GetAsync(sourceGameId, ct);
        if (sourceTerms.Count == 0) return (0, 0);

        await _lock.WaitAsync(ct);
        try
        {
            var targetTerms = _cache.TryGetValue(targetGameId, out var cached)
                ? cached
                : await LoadFromDiskAsync(targetGameId, ct);

            var existingOriginals = new HashSet<string>(
                targetTerms.Select(e => e.Original), StringComparer.Ordinal);

            var toAdd = sourceTerms.Where(e => !existingOriginals.Contains(e.Original)).ToList();
            var skipped = sourceTerms.Count - toAdd.Count;

            if (toAdd.Count > 0)
            {
                var merged = new List<TermEntry>(targetTerms);
                merged.AddRange(toAdd);
                await SaveInternalAsync(targetGameId, merged, ct);
            }

            logger.LogInformation(
                "从游戏 {Source} 导入到 {Target}: 新增 {Added} 条, 跳过 {Skipped} 条",
                sourceGameId, targetGameId, toAdd.Count, skipped);
            return (toAdd.Count, skipped);
        }
        finally
        {
            _lock.Release();
        }
    }

    private async Task<List<TermEntry>> LoadFromDiskAsync(string gameId, CancellationToken ct)
    {
        var file = paths.GlossaryFile(gameId);
        if (!File.Exists(file))
        {
            _cache[gameId] = [];
            return [];
        }

        var json = await File.ReadAllTextAsync(file, ct);
        var entries = JsonSerializer.Deserialize<List<TermEntry>>(json, JsonOptions) ?? [];
        _cache[gameId] = entries;
        return entries;
    }

    private async Task SaveInternalAsync(string gameId, List<TermEntry> entries, CancellationToken ct)
    {
        var file = paths.GlossaryFile(gameId);
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        var json = JsonSerializer.Serialize(entries, JsonOptions);
        var tmpPath = file + ".tmp";
        await File.WriteAllTextAsync(tmpPath, json, ct);
        File.Move(tmpPath, file, overwrite: true);
        _cache[gameId] = entries;
        logger.LogInformation("已保存游戏 {GameId} 的术语库: {Count} 条", gameId, entries.Count);
    }

    /// <summary>Legacy JSON shape for migrating old do-not-translate files.</summary>
    private sealed class LegacyDntEntry
    {
        public string Original { get; set; } = "";
        public bool CaseSensitive { get; set; } = true;
    }
}
