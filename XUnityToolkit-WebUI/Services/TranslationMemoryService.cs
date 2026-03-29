using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.RegularExpressions;
using XUnityToolkit_WebUI.Infrastructure;
using XUnityToolkit_WebUI.Models;

namespace XUnityToolkit_WebUI.Services;

public sealed class TranslationMemoryService(
    AppDataPaths paths,
    ScriptTagService scriptTagService,
    ILogger<TranslationMemoryService> logger) : IDisposable
{
    private const int MaxEntriesPerGame = 100_000;
    private const int FuzzyCandidateBudget = 500;
    private const double LengthToleranceRatio = 0.20;
    private const int DebounceDelayMs = 5000;

    private readonly ConcurrentDictionary<string, TranslationMemoryStore> _stores = new();
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _persistLocks = new();
    private readonly ConcurrentDictionary<string, long> _dirtyTimestamps = new();
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _debounceTimers = new();

    // Hit stats
    private long _exactHits;
    private long _patternHits;
    private long _fuzzyHits;
    private long _misses;


    // ── Internal store ──

    private sealed class TranslationMemoryStore
    {
        public ConcurrentDictionary<string, TranslationMemoryEntry> ExactIndex { get; } = new(StringComparer.Ordinal);
        public ConcurrentDictionary<int, (Lock Lock, List<TranslationMemoryEntry> Entries)> LengthBuckets { get; } = new();
        public List<(Regex Pattern, string TranslatedTemplate, List<VariablePosition> Variables)> CompiledPatterns { get; set; } = [];
        public bool LoadAttempted { get; set; }
    }

    // ── Public API ──

    /// <summary>
    /// Ensure the TM for a game is loaded. Safe to call from hot path — no-op after first load.
    /// </summary>
    public async Task EnsureLoadedAsync(string gameId, CancellationToken ct = default)
    {
        var store = _stores.GetOrAdd(gameId, _ => new TranslationMemoryStore());
        if (store.LoadAttempted) return;

        var gameLock = _persistLocks.GetOrAdd(gameId, _ => new SemaphoreSlim(1, 1));
        await gameLock.WaitAsync(ct);
        try
        {
            if (store.LoadAttempted) return;
            await LoadFromDiskAsync(gameId, store, ct);
            store.LoadAttempted = true;
        }
        finally
        {
            gameLock.Release();
        }
    }

    /// <summary>
    /// Try to match text against the TM. Returns null if no match found.
    /// Tries exact → pattern → fuzzy in order.
    /// </summary>
    public TmMatchResult? TryMatch(string gameId, string text, double fuzzyThreshold = 0.85)
    {
        if (!_stores.TryGetValue(gameId, out var store) || !store.LoadAttempted)
            return null;

        var normalized = NormalizeKey(gameId, text);
        if (string.IsNullOrEmpty(normalized)) return null;

        // 1. Exact match
        if (store.ExactIndex.TryGetValue(normalized, out var exact))
        {
            Interlocked.Increment(ref _exactHits);
            return new TmMatchResult
            {
                Translation = exact.Translation,
                MatchType = TmMatchType.Exact
            };
        }

        // 2. Pattern match
        var patternResult = TryPatternMatch(store, text);
        if (patternResult is not null)
        {
            Interlocked.Increment(ref _patternHits);
            return patternResult;
        }

        // 3. Fuzzy match
        var fuzzyResult = TryFuzzyMatch(store, normalized, fuzzyThreshold);
        if (fuzzyResult is not null)
        {
            Interlocked.Increment(ref _fuzzyHits);
            return fuzzyResult;
        }

        Interlocked.Increment(ref _misses);
        return null;
    }

    /// <summary>
    /// Add translation entries to the TM in-memory and schedule debounced persistence.
    /// Synchronous — safe to call from fire-and-forget contexts.
    /// </summary>
    public void Add(
        string gameId,
        IList<string> originals,
        IList<string> translations,
        int round,
        bool isFinal)
    {
        if (originals.Count == 0 || originals.Count != translations.Count) return;

        var store = _stores.GetOrAdd(gameId, _ => new TranslationMemoryStore());

        var added = 0;
        for (var i = 0; i < originals.Count; i++)
        {
            var original = originals[i];
            var translation = translations[i];
            if (string.IsNullOrWhiteSpace(original) || string.IsNullOrWhiteSpace(translation))
                continue;

            var normalized = NormalizeKey(gameId, original);
            if (string.IsNullOrEmpty(normalized)) continue;

            var entry = new TranslationMemoryEntry
            {
                Original = original,
                Translation = translation,
                NormalizedKey = normalized,
                TranslatedAt = DateTime.UtcNow,
                Round = round,
                IsFinal = isFinal
            };

            // Update or add to exact index
            store.ExactIndex.AddOrUpdate(normalized, entry, (_, existing) =>
                isFinal || !existing.IsFinal ? entry : existing);

            // Add to length bucket
            AddToLengthBucket(store, normalized.Length, entry);
            added++;
        }

        if (added == 0) return;

        // Evict if over limit (LRU by TranslatedAt)
        EvictIfNeeded(store);

        ScheduleDebouncedPersist(gameId);
        logger.LogDebug("翻译记忆库 {GameId}: 添加 {Count} 条（总计 {Total}）",
            gameId, added, store.ExactIndex.Count);
    }

    /// <summary>
    /// Async wrapper for backward compatibility (e.g., existing AddAsync call sites).
    /// </summary>
    public Task AddAsync(
        string gameId,
        IList<string> originals,
        IList<string> translations,
        int round,
        bool isFinal,
        CancellationToken ct = default)
    {
        Add(gameId, originals, translations, round, isFinal);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Cancel any pending debounce timer and immediately persist the TM for the given game.
    /// </summary>
    public async Task FlushAsync(string gameId, CancellationToken ct = default)
    {
        // Cancel pending debounce
        if (_debounceTimers.TryRemove(gameId, out var cts))
        {
            await cts.CancelAsync();
            cts.Dispose();
        }

        if (!_dirtyTimestamps.TryRemove(gameId, out _)) return;

        if (_stores.TryGetValue(gameId, out var store))
            await PersistAsync(gameId, store, ct);
    }

    /// <summary>
    /// Load compiled patterns from DynamicPatternService.
    /// </summary>
    public void LoadPatterns(
        string gameId,
        List<(Regex Pattern, string TranslatedTemplate, List<VariablePosition> Variables)> patterns)
    {
        var store = _stores.GetOrAdd(gameId, _ => new TranslationMemoryStore());
        store.CompiledPatterns = patterns;
        logger.LogDebug("翻译记忆库 {GameId}: 加载 {Count} 个动态模式", gameId, patterns.Count);
    }

    public void RemoveCache(string gameId)
    {
        CancelDebounceTimer(gameId);
        _stores.TryRemove(gameId, out _);
        if (_persistLocks.TryRemove(gameId, out var sem))
            sem.Dispose();
    }

    public void ClearAllCache()
    {
        foreach (var gameId in _debounceTimers.Keys.ToList())
            CancelDebounceTimer(gameId);
        _stores.Clear();
        var locks = _persistLocks.Values.ToList();
        _persistLocks.Clear();
        foreach (var sem in locks)
            sem.Dispose();
    }

    public async Task DeleteAsync(string gameId, CancellationToken ct = default)
    {
        CancelDebounceTimer(gameId);
        _stores.TryRemove(gameId, out _);
        var file = paths.TranslationMemoryFile(gameId);
        if (File.Exists(file))
        {
            await Task.Run(() => File.Delete(file), ct);
            logger.LogInformation("已删除游戏 {GameId} 的翻译记忆库", gameId);
        }
    }

    public int GetEntryCount(string gameId)
    {
        return _stores.TryGetValue(gameId, out var store) ? store.ExactIndex.Count : 0;
    }

    public (long ExactHits, long PatternHits, long FuzzyHits, long Misses) GetHitStats()
    {
        return (
            Interlocked.Read(ref _exactHits),
            Interlocked.Read(ref _patternHits),
            Interlocked.Read(ref _fuzzyHits),
            Interlocked.Read(ref _misses)
        );
    }

    public void Dispose()
    {
        // Cancel all debounce timers first to prevent races with the flush
        var dirtyGames = _dirtyTimestamps.Keys.ToList();
        foreach (var gameId in dirtyGames)
            CancelDebounceTimer(gameId);

        // Flush all dirty stores in parallel — each game writes to a separate file
        // with a per-game lock, so there is no contention
        var tasks = new List<Task>();
        foreach (var gameId in dirtyGames)
        {
            if (_dirtyTimestamps.TryRemove(gameId, out _) && _stores.TryGetValue(gameId, out var store))
            {
                var gid = gameId; // capture for closure
                tasks.Add(Task.Run(async () =>
                {
                    try { await PersistAsync(gid, store, CancellationToken.None); }
                    catch (Exception ex) { logger.LogError(ex, "关闭时保存翻译记忆库失败: {GameId}", gid); }
                }));
            }
        }

        if (tasks.Count > 0)
        {
            try { Task.WhenAll(tasks).GetAwaiter().GetResult(); }
            catch { /* individual errors already logged */ }
        }

        var locks = _persistLocks.Values.ToList();
        _persistLocks.Clear();
        foreach (var sem in locks)
            sem.Dispose();
    }

    // ── Debounce infrastructure ──

    private void ScheduleDebouncedPersist(string gameId)
    {
        _dirtyTimestamps[gameId] = DateTime.UtcNow.Ticks;

        // Cancel existing timer and atomically swap in new CTS
        var cts = new CancellationTokenSource();
        while (true)
        {
            var old = _debounceTimers.GetOrAdd(gameId, cts);
            if (old == cts)
                break; // Successfully inserted new CTS

            // Try to replace existing CTS atomically
            if (_debounceTimers.TryUpdate(gameId, cts, old))
            {
                try { old.Cancel(); } catch (ObjectDisposedException) { }
                old.Dispose();
                break;
            }
            // CAS failed — another thread replaced it; retry
        }
        var token = cts.Token;

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(DebounceDelayMs, token);
                if (token.IsCancellationRequested) return;

                if (!_dirtyTimestamps.TryRemove(gameId, out _)) return;
                if (_stores.TryGetValue(gameId, out var store))
                    await PersistAsync(gameId, store, CancellationToken.None);
            }
            catch (OperationCanceledException)
            {
                // Debounce cancelled — new write or flush will handle persistence
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "防抖保存翻译记忆库失败: {GameId}", gameId);
            }
        });
    }

    private void CancelDebounceTimer(string gameId)
    {
        if (_debounceTimers.TryRemove(gameId, out var cts))
        {
            try { cts.Cancel(); } catch { /* already disposed */ }
            cts.Dispose();
        }
    }

    // ── Pattern matching ──

    private static TmMatchResult? TryPatternMatch(
        TranslationMemoryStore store,
        string text)
    {
        foreach (var (pattern, translatedTemplate, variables) in store.CompiledPatterns)
        {
            var match = pattern.Match(text);
            if (!match.Success) continue;

            // Replace group references in translated template
            var result = translatedTemplate;
            for (var i = match.Groups.Count - 1; i >= 1; i--)
            {
                result = result.Replace($"${i}", match.Groups[i].Value);
            }

            return new TmMatchResult
            {
                Translation = result,
                MatchType = TmMatchType.Pattern
            };
        }

        return null;
    }

    // ── Fuzzy matching ──

    private TmMatchResult? TryFuzzyMatch(
        TranslationMemoryStore store,
        string normalizedText,
        double threshold)
    {
        var textLen = normalizedText.Length;
        var minLen = (int)(textLen * (1 - LengthToleranceRatio));
        var maxLen = (int)(textLen * (1 + LengthToleranceRatio));
        var maxDistance = (int)(textLen * (1 - threshold));

        // Collect candidates from length buckets within tolerance
        var candidates = new List<TranslationMemoryEntry>();
        for (var len = minLen; len <= maxLen; len++)
        {
            if (!store.LengthBuckets.TryGetValue(len, out var bucket)) continue;

            bucket.Lock.Enter();
            try
            {
                var remaining = FuzzyCandidateBudget - candidates.Count;
                if (remaining >= bucket.Entries.Count)
                    candidates.AddRange(bucket.Entries);
                else
                    candidates.AddRange(bucket.Entries.Take(remaining));
            }
            finally
            {
                bucket.Lock.Exit();
            }

            if (candidates.Count >= FuzzyCandidateBudget) break;
        }

        if (candidates.Count == 0) return null;

        // Find best match
        TranslationMemoryEntry? bestEntry = null;
        var bestDistance = maxDistance + 1;

        foreach (var candidate in candidates)
        {
            var key = candidate.NormalizedKey ?? candidate.Original;
            var distance = LevenshteinDistance(normalizedText, key, maxDistance);
            if (distance >= 0 && distance < bestDistance)
            {
                bestDistance = distance;
                bestEntry = candidate;
            }
        }

        if (bestEntry is null) return null;

        var bestKey = bestEntry.NormalizedKey ?? bestEntry.Original;
        var similarity = 1.0 - (double)bestDistance / Math.Max(normalizedText.Length, bestKey.Length);
        if (similarity < threshold) return null;

        // Verify contiguous diffs and attempt segment replacement
        var diffs = FindDiffSegments(bestKey, normalizedText);
        if (diffs.Count == 0)
        {
            // No diffs means exact match was missed — return as-is
            return new TmMatchResult
            {
                Translation = bestEntry.Translation,
                MatchType = TmMatchType.Fuzzy
            };
        }

        // Check that all diff segments from the old text exist in the translation
        var translation = bestEntry.Translation;
        foreach (var (oldSegment, newSegment) in diffs)
        {
            if (string.IsNullOrEmpty(oldSegment) || !translation.Contains(oldSegment, StringComparison.Ordinal))
            {
                // Cannot safely replace — old segment not found in translation
                return null;
            }
        }

        // Apply replacements
        foreach (var (oldSegment, newSegment) in diffs)
        {
            // Replace first occurrence only
            var idx = translation.IndexOf(oldSegment, StringComparison.Ordinal);
            if (idx >= 0)
            {
                translation = string.Concat(
                    translation.AsSpan(0, idx),
                    newSegment,
                    translation.AsSpan(idx + oldSegment.Length));
            }
        }

        return new TmMatchResult
        {
            Translation = translation,
            MatchType = TmMatchType.Fuzzy
        };
    }

    // ── Levenshtein with early termination ──

    /// <summary>
    /// Compute Levenshtein edit distance with early termination.
    /// Returns -1 if distance exceeds maxDistance.
    /// </summary>
    internal static int LevenshteinDistance(string s, string t, int maxDistance)
    {
        var sLen = s.Length;
        var tLen = t.Length;

        if (Math.Abs(sLen - tLen) > maxDistance) return -1;
        if (sLen == 0) return tLen;
        if (tLen == 0) return sLen;

        // Single-row DP
        var prev = new int[tLen + 1];
        for (var j = 0; j <= tLen; j++) prev[j] = j;

        for (var i = 1; i <= sLen; i++)
        {
            var rowMin = int.MaxValue;
            var prevDiag = prev[0];
            prev[0] = i;

            for (var j = 1; j <= tLen; j++)
            {
                var temp = prev[j];
                var cost = s[i - 1] == t[j - 1] ? 0 : 1;
                prev[j] = Math.Min(Math.Min(prev[j] + 1, prev[j - 1] + 1), prevDiag + cost);
                prevDiag = temp;
                rowMin = Math.Min(rowMin, prev[j]);
            }

            if (rowMin > maxDistance) return -1;
        }

        return prev[tLen] <= maxDistance ? prev[tLen] : -1;
    }

    /// <summary>
    /// Find contiguous diff segments between two similar strings.
    /// Returns list of (oldSegment, newSegment) pairs.
    /// </summary>
    internal static List<(string OldSegment, string NewSegment)> FindDiffSegments(string original, string changed)
    {
        var result = new List<(string, string)>();

        // Find common prefix
        var prefixLen = 0;
        var minLen = Math.Min(original.Length, changed.Length);
        while (prefixLen < minLen && original[prefixLen] == changed[prefixLen])
            prefixLen++;

        // Find common suffix (not overlapping with prefix)
        var suffixLen = 0;
        while (suffixLen < minLen - prefixLen &&
               original[original.Length - 1 - suffixLen] == changed[changed.Length - 1 - suffixLen])
            suffixLen++;

        var oldMiddle = original.Substring(prefixLen, original.Length - prefixLen - suffixLen);
        var newMiddle = changed.Substring(prefixLen, changed.Length - prefixLen - suffixLen);

        if (oldMiddle.Length > 0 || newMiddle.Length > 0)
        {
            result.Add((oldMiddle, newMiddle));
        }

        return result;
    }

    // ── Storage helpers ──

    private string NormalizeKey(string gameId, string text)
    {
        return scriptTagService.NormalizeForCache(gameId, text);
    }

    private static void AddToLengthBucket(TranslationMemoryStore store, int length, TranslationMemoryEntry entry)
    {
        var bucket = store.LengthBuckets.GetOrAdd(length, _ => (new Lock(), []));
        bucket.Lock.Enter();
        try
        {
            // Remove existing entry with same key to avoid duplicates
            var key = entry.NormalizedKey ?? entry.Original;
            bucket.Entries.RemoveAll(e => string.Equals(e.NormalizedKey ?? e.Original, key, StringComparison.Ordinal));
            bucket.Entries.Add(entry);
        }
        finally
        {
            bucket.Lock.Exit();
        }
    }

    private static void EvictIfNeeded(TranslationMemoryStore store)
    {
        if (store.ExactIndex.Count <= MaxEntriesPerGame) return;

        // LRU eviction: remove oldest entries
        var toRemove = store.ExactIndex
            .OrderBy(kv => kv.Value.TranslatedAt)
            .Take(store.ExactIndex.Count - MaxEntriesPerGame)
            .Select(kv => kv.Key)
            .ToList();

        foreach (var key in toRemove)
        {
            if (store.ExactIndex.TryRemove(key, out var removed))
            {
                var normalizedLen = (removed.NormalizedKey ?? removed.Original).Length;
                if (store.LengthBuckets.TryGetValue(normalizedLen, out var bucket))
                {
                    bucket.Lock.Enter();
                    try
                    {
                        bucket.Entries.RemoveAll(e =>
                            string.Equals(e.NormalizedKey ?? e.Original, key, StringComparison.Ordinal));
                        // Clean up empty buckets to prevent unbounded growth
                        if (bucket.Entries.Count == 0)
                            store.LengthBuckets.TryRemove(normalizedLen, out _);
                    }
                    finally
                    {
                        bucket.Lock.Exit();
                    }
                }
            }
        }
    }

    private async Task LoadFromDiskAsync(string gameId, TranslationMemoryStore store, CancellationToken ct)
    {
        var file = paths.TranslationMemoryFile(gameId);
        if (!File.Exists(file)) return;

        try
        {
            var json = await File.ReadAllTextAsync(file, ct);
            var entries = JsonSerializer.Deserialize<List<TranslationMemoryEntry>>(json, FileHelper.DataJsonOptions);
            if (entries is null) return;

            foreach (var entry in entries)
            {
                var key = entry.NormalizedKey ?? NormalizeKey(gameId, entry.Original);
                if (string.IsNullOrEmpty(key)) continue;

                var normalizedEntry = entry with { NormalizedKey = key };
                store.ExactIndex[key] = normalizedEntry;
                AddToLengthBucket(store, key.Length, normalizedEntry);
            }

            logger.LogInformation("已加载游戏 {GameId} 的翻译记忆库: {Count} 条", gameId, entries.Count);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "加载翻译记忆库失败: {GameId}", gameId);
        }
    }

    private async Task PersistAsync(string gameId, TranslationMemoryStore store, CancellationToken ct)
    {
        var gameLock = _persistLocks.GetOrAdd(gameId, _ => new SemaphoreSlim(1, 1));
        await gameLock.WaitAsync(ct);
        try
        {
            var entries = store.ExactIndex.Values
                .OrderByDescending(e => e.TranslatedAt)
                .ToList();

            var file = paths.TranslationMemoryFile(gameId);
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            var json = JsonSerializer.Serialize(entries, FileHelper.DataJsonOptions);
            var tmpPath = file + ".tmp";
            await File.WriteAllTextAsync(tmpPath, json, ct);
            File.Move(tmpPath, file, overwrite: true);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "保存翻译记忆库失败: {GameId}", gameId);
        }
        finally
        {
            gameLock.Release();
        }
    }
}
