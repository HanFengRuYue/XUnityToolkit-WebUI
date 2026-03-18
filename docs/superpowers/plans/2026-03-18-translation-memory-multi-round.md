# Translation Memory, Dynamic Pattern Detection & Multi-Round Translation — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a three-layer defense system against cache misses caused by dynamic player names, plus multi-round translation with auto term extraction.

**Architecture:** New `TranslationMemoryService` provides persistent TM with exact/fuzzy/pattern matching (Phase 0 in translation pipeline). `DynamicPatternService` detects template variables and uses LLM to identify dynamic fragments, generating XUnity regex cache. `TermExtractionService` extracts terms via dedicated LLM calls during pre-translation. `PreTranslationService` gains multi-round capability (Round 1: translate + extract, Round 2: polish with terms).

**Tech Stack:** ASP.NET Core (.NET 10), Vue 3 + TypeScript + Naive UI, SignalR

**Spec:** `docs/superpowers/specs/2026-03-18-translation-memory-multi-round-design.md`

---

## File Structure

### New Files (Backend)

| File | Responsibility |
|------|----------------|
| `Services/TranslationMemoryService.cs` | Persistent TM: load/save JSON, exact match, dynamic pattern match, Levenshtein fuzzy match, diff segment replacement |
| `Services/DynamicPatternService.cs` | Template variable regex detection, LLM-assisted dynamic fragment analysis, XUnity `r:` regex generation |
| `Services/TermExtractionService.cs` | Dedicated LLM calls for term extraction, candidate storage, merge with TermService |
| `Models/TranslationMemory.cs` | `TranslationMemoryEntry`, `TranslationMemoryStore`, `DynamicPattern`, `TermCandidate` records |
| `Endpoints/TranslationMemoryEndpoints.cs` | TM stats, clear, dynamic patterns, term candidates CRUD |

### Modified Files (Backend)

| File | Changes |
|------|---------|
| `Infrastructure/AppDataPaths.cs` | Add `TranslationMemoryDirectory`, `DynamicPatternsDirectory`, `TermCandidatesDirectory` + per-game file helpers + `EnsureDirectoriesExist` |
| `Models/AiTranslationSettings.cs` | Add 5 new settings fields |
| `Models/TranslationStats.cs` | Add 6 new TM stats fields |
| `Models/TermEntry.cs` | Add `TermSource` enum + `Source` property |
| `Services/LlmTranslationService.cs` | Insert Phase 0 TM lookup before Phase 1; write results to TM after translation |
| `Services/PreTranslationService.cs` | Multi-round pipeline, dynamic pattern analysis phase, term extraction phase |
| `Endpoints/TranslateEndpoints.cs` | Inject `TranslationMemoryService`, pass to TM write-back |
| `Endpoints/GameEndpoints.cs` | Per-game cleanup for 3 new data directories |
| `Endpoints/SettingsEndpoints.cs` | Export exclusion for new directories; settings reset cache clearing |
| `Program.cs` | Register 3 new singletons |

### Modified Files (Frontend)

| File | Changes |
|------|---------|
| `src/api/types.ts` | New interfaces/fields for TM stats, settings, term candidates, TermSource |
| `src/views/AiTranslationView.vue` | TM stats card, translation source tags |
| `src/views/SettingsView.vue` | TM/multi-round/extraction settings controls |
| `src/views/AssetExtractionView.vue` | Multi-step pre-translation progress, term review modal |

---

## Task 1: Infrastructure — AppDataPaths + Models

**Files:**
- Modify: `XUnityToolkit-WebUI/Infrastructure/AppDataPaths.cs:28-101`
- Create: `XUnityToolkit-WebUI/Models/TranslationMemory.cs`
- Modify: `XUnityToolkit-WebUI/Models/TermEntry.cs:14-46`
- Modify: `XUnityToolkit-WebUI/Models/AiTranslationSettings.cs:5-53`
- Modify: `XUnityToolkit-WebUI/Models/TranslationStats.cs:3-27`

- [ ] **Step 1: Add new directories to AppDataPaths**

In `AppDataPaths.cs`, after line 31 (`PreTranslationRegexDirectory`), add:

```csharp
public string TranslationMemoryDirectory => Path.Combine(_root, "translation-memory");
public string DynamicPatternsDirectory => Path.Combine(_root, "dynamic-patterns");
public string TermCandidatesDirectory => Path.Combine(_root, "term-candidates");

public string TranslationMemoryFile(string gameId) =>
    Path.Combine(TranslationMemoryDirectory, $"{gameId}.json");
public string DynamicPatternsFile(string gameId) =>
    Path.Combine(DynamicPatternsDirectory, $"{gameId}.json");
public string TermCandidatesFile(string gameId) =>
    Path.Combine(TermCandidatesDirectory, $"{gameId}.json");
```

In `EnsureDirectoriesExist()`, add:
```csharp
Directory.CreateDirectory(TranslationMemoryDirectory);
Directory.CreateDirectory(DynamicPatternsDirectory);
Directory.CreateDirectory(TermCandidatesDirectory);
```

- [ ] **Step 2: Create TranslationMemory.cs model file**

```csharp
namespace XUnityToolkit_WebUI.Models;

public sealed record TranslationMemoryEntry
{
    public required string Original { get; init; }
    public required string Translation { get; init; }
    public string? NormalizedKey { get; init; }
    public DateTime TranslatedAt { get; init; } = DateTime.UtcNow;
    public int Round { get; init; } = 1;
    public bool IsFinal { get; init; }
}

public sealed record DynamicPattern
{
    public required string OriginalTemplate { get; set; }
    public required string TranslatedTemplate { get; set; }
    public List<VariablePosition> VariablePositions { get; set; } = [];
    public string Source { get; set; } = "template-detection"; // "template-detection" or "llm"
}

public sealed record VariablePosition
{
    public string Type { get; set; } = "playerName";
    public int GroupIndex { get; set; }
}

public sealed record DynamicPatternStore
{
    public List<DynamicPattern> Patterns { get; set; } = [];
}

public sealed record TermCandidate
{
    public required string Original { get; set; }
    public required string Translation { get; set; }
    public TermCategory Category { get; set; } = TermCategory.General;
    public int Frequency { get; set; } = 1;
}

public sealed record TermCandidateStore
{
    public List<TermCandidate> Candidates { get; set; } = [];
    public DateTime ExtractedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>TM match result with source info for stats tracking.</summary>
public sealed record TmMatchResult
{
    public required string Translation { get; set; }
    public required TmMatchType MatchType { get; set; }
}

public enum TmMatchType { Exact, Pattern, Fuzzy }
```

- [ ] **Step 3: Add TermSource enum and Source property to TermEntry**

In `TermEntry.cs`, after line 31 (`TermCategory` enum), add:

```csharp
public enum TermSource { User, AI, Import }
```

In the `TermEntry` record, after `Priority` property (line 45), add:

```csharp
public TermSource Source { get; set; } = TermSource.User;
```

Note: `TermSource` uses default PascalCase JSON serialization (no `CamelCaseJsonStringEnumConverter`).

- [ ] **Step 4: Add settings to AiTranslationSettings**

In `AiTranslationSettings.cs`, after `NaturalTranslationMode` (line 48), add:

```csharp
// Translation Memory
public bool EnableTranslationMemory { get; set; } = true;
public int FuzzyMatchThreshold { get; set; } = 85;

// LLM Pattern Analysis (pre-translation phase)
public bool EnableLlmPatternAnalysis { get; set; } = true;

// Multi-Round Translation (default ON = 2 rounds)
public bool EnableMultiRoundTranslation { get; set; } = true;

// Auto Term Extraction
public bool EnableAutoTermExtraction { get; set; } = true;
public bool AutoApplyExtractedTerms { get; set; }
```

- [ ] **Step 5: Add TM stats to TranslationStats**

In `TranslationStats.cs`, after `TermAuditForceCorrectedCount` (line 26), add:

```csharp
public int TranslationMemoryHits { get; init; }
public int TranslationMemoryFuzzyHits { get; init; }
public int TranslationMemoryPatternHits { get; init; }
public int TranslationMemoryMisses { get; init; }
public int DynamicPatternCount { get; init; }
public int ExtractedTermCount { get; init; }
```

- [ ] **Step 6: Add translation source to RecentTranslation**

In `TranslationStats.cs` `RecentTranslation` record, after `TermAuditResult` (line 49), add:

```csharp
/// <summary>
/// Translation source: null=LLM, "tmExact", "tmFuzzy", "tmPattern"
/// </summary>
public string? TranslationSource { get; init; }
```

- [ ] **Step 7: Build to verify models compile**

Run: `dotnet build XUnityToolkit-WebUI/XUnityToolkit-WebUI.csproj -p:SkipFrontendBuild=true`
Expected: Build succeeded

- [ ] **Step 8: Commit**

```bash
git add XUnityToolkit-WebUI/Infrastructure/AppDataPaths.cs XUnityToolkit-WebUI/Models/
git commit -m "feat: 添加翻译记忆基础模型和数据路径"
```

---

## Task 2: TranslationMemoryService — Core Storage + Exact Match

**Files:**
- Create: `XUnityToolkit-WebUI/Services/TranslationMemoryService.cs`
- Modify: `XUnityToolkit-WebUI/Program.cs:128-155`

- [ ] **Step 1: Create TranslationMemoryService with loading/saving**

Create `Services/TranslationMemoryService.cs`. Follow `TermService.cs` pattern: `SemaphoreSlim` + `ConcurrentDictionary` cache + atomic JSON writes.

```csharp
using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.RegularExpressions;
using XUnityToolkit_WebUI.Infrastructure;
using XUnityToolkit_WebUI.Models;

namespace XUnityToolkit_WebUI.Services;

public sealed class TranslationMemoryService(
    AppDataPaths paths,
    ScriptTagService scriptTagService,
    TermService termService,
    TermAuditService termAuditService,
    ILogger<TranslationMemoryService> logger)
{
    private readonly ConcurrentDictionary<string, TranslationMemoryStore> _stores = new();
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new();
    private readonly ConcurrentDictionary<string, bool> _loadAttempted = new();

    // ── Stats (thread-safe) ──
    private long _exactHits;
    private long _fuzzyHits;
    private long _patternHits;
    private long _misses;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private const int MaxEntriesPerGame = 100_000;
    private const long MaxFileSizeBytes = 50 * 1024 * 1024; // 50 MB

    /// <summary>Ensure TM is loaded for a game. Hot-path safe (volatile read after first load).</summary>
    public async Task EnsureLoadedAsync(string gameId, CancellationToken ct = default)
    {
        if (_loadAttempted.ContainsKey(gameId)) return;

        var sem = _locks.GetOrAdd(gameId, _ => new SemaphoreSlim(1, 1));
        await sem.WaitAsync(ct);
        try
        {
            if (_loadAttempted.ContainsKey(gameId)) return;
            await LoadAsync(gameId, ct);
            _loadAttempted[gameId] = true;
        }
        finally { sem.Release(); }
    }

    private async Task LoadAsync(string gameId, CancellationToken ct)
    {
        var file = paths.TranslationMemoryFile(gameId);
        if (!File.Exists(file)) return;

        try
        {
            var json = await File.ReadAllTextAsync(file, ct);
            var entries = JsonSerializer.Deserialize<List<TranslationMemoryEntry>>(json, JsonOptions);
            if (entries is null) return;

            var store = GetOrCreateStore(gameId);
            foreach (var entry in entries)
            {
                var key = entry.NormalizedKey ?? entry.Original;
                store.ExactIndex[key] = entry;
                AddToLengthBucket(store, entry);
            }
            logger.LogInformation("翻译记忆已加载: 游戏 {GameId}, {Count} 条", gameId, entries.Count);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "翻译记忆加载失败: 游戏 {GameId}, 降级为无 TM 模式", gameId);
        }
    }

    /// <summary>Add translation pairs to TM (called after successful translation).</summary>
    public async Task AddAsync(string gameId, IList<string> originals, IList<string> translations,
        int round = 1, bool isFinal = false, CancellationToken ct = default)
    {
        var store = GetOrCreateStore(gameId);
        for (int i = 0; i < originals.Count && i < translations.Count; i++)
        {
            if (string.IsNullOrWhiteSpace(originals[i]) || string.IsNullOrWhiteSpace(translations[i]))
                continue;

            var normalizedKey = scriptTagService.NormalizeForCache(gameId, originals[i]);
            var entry = new TranslationMemoryEntry
            {
                Original = originals[i],
                Translation = translations[i],
                NormalizedKey = normalizedKey,
                TranslatedAt = DateTime.UtcNow,
                Round = round,
                IsFinal = isFinal
            };

            // Prefer final (round 2) over non-final
            if (store.ExactIndex.TryGetValue(normalizedKey, out var existing) && existing.IsFinal && !isFinal)
                continue;

            store.ExactIndex[normalizedKey] = entry;
            AddToLengthBucket(store, entry);
        }

        // Evict if over limit
        if (store.ExactIndex.Count > MaxEntriesPerGame)
            EvictOldest(store, store.ExactIndex.Count - MaxEntriesPerGame);

        await SaveAsync(gameId, store, ct);
    }

    /// <summary>Try to find a TM match for a single text. Returns null if no match.</summary>
    public TmMatchResult? TryMatch(string gameId, string text, int fuzzyThreshold)
    {
        if (!_stores.TryGetValue(gameId, out var store)) return null;

        var normalizedText = scriptTagService.NormalizeForCache(gameId, text);

        // 1. Exact match
        if (store.ExactIndex.TryGetValue(normalizedText, out var exactEntry))
        {
            Interlocked.Increment(ref _exactHits);
            return new TmMatchResult { Translation = exactEntry.Translation, MatchType = TmMatchType.Exact };
        }

        // 2. Dynamic pattern match
        var patternResult = TryPatternMatch(gameId, normalizedText, store);
        if (patternResult is not null)
        {
            Interlocked.Increment(ref _patternHits);
            return patternResult;
        }

        // 3. Fuzzy match (edit distance)
        var fuzzyResult = TryFuzzyMatch(normalizedText, store, fuzzyThreshold);
        if (fuzzyResult is not null)
        {
            Interlocked.Increment(ref _fuzzyHits);
            return fuzzyResult;
        }

        Interlocked.Increment(ref _misses);
        return null;
    }

    public (long exact, long fuzzy, long pattern, long misses) GetHitStats() =>
        (Volatile.Read(ref _exactHits), Volatile.Read(ref _fuzzyHits),
         Volatile.Read(ref _patternHits), Volatile.Read(ref _misses));

    public int GetEntryCount(string gameId) =>
        _stores.TryGetValue(gameId, out var store) ? store.ExactIndex.Count : 0;

    public void RemoveCache(string gameId)
    {
        _stores.TryRemove(gameId, out _);
        _loadAttempted.TryRemove(gameId, out _);
    }

    public void ClearAllCache()
    {
        _stores.Clear();
        _loadAttempted.Clear();
    }

    public async Task DeleteAsync(string gameId)
    {
        RemoveCache(gameId);
        var file = paths.TranslationMemoryFile(gameId);
        if (File.Exists(file)) File.Delete(file);
    }

    // ── Private helpers ──

    private TranslationMemoryStore GetOrCreateStore(string gameId) =>
        _stores.GetOrAdd(gameId, _ => new TranslationMemoryStore());

    private static void AddToLengthBucket(TranslationMemoryStore store, TranslationMemoryEntry entry)
    {
        var len = (entry.NormalizedKey ?? entry.Original).Length;
        var (bucketLock, bucket) = store.LengthBuckets.GetOrAdd(len, _ => (new Lock(), []));
        bucketLock.Enter();
        try
        {
            var key = entry.NormalizedKey ?? entry.Original;
            bucket.RemoveAll(e => (e.NormalizedKey ?? e.Original) == key);
            bucket.Add(entry);
        }
        finally { bucketLock.Exit(); }
    }

    private static void EvictOldest(TranslationMemoryStore store, int count)
    {
        var oldest = store.ExactIndex
            .OrderBy(kv => kv.Value.TranslatedAt)
            .Take(count)
            .Select(kv => kv.Key)
            .ToList();

        foreach (var key in oldest)
        {
            if (store.ExactIndex.Remove(key, out var entry))
            {
                var len = (entry.NormalizedKey ?? entry.Original).Length;
                if (store.LengthBuckets.TryGetValue(len, out var bucketTuple))
                {
                    var (bucketLock, bucket) = bucketTuple;
                    bucketLock.Enter();
                    try { bucket.RemoveAll(e => (e.NormalizedKey ?? e.Original) == key); }
                    finally { bucketLock.Exit(); }
                }
            }
        }
    }

    private async Task SaveAsync(string gameId, TranslationMemoryStore store, CancellationToken ct)
    {
        var file = paths.TranslationMemoryFile(gameId);
        var entries = store.ExactIndex.Values.ToList();
        var json = JsonSerializer.Serialize(entries, JsonOptions);
        var tmp = file + ".tmp";
        await File.WriteAllTextAsync(tmp, json, ct);
        File.Move(tmp, file, overwrite: true);
    }

    // Fuzzy match and pattern match are implemented in Task 3
    private TmMatchResult? TryPatternMatch(string gameId, string text, TranslationMemoryStore store) => null;
    private TmMatchResult? TryFuzzyMatch(string text, TranslationMemoryStore store, int threshold) => null;
}

internal sealed class TranslationMemoryStore
{
    public ConcurrentDictionary<string, TranslationMemoryEntry> ExactIndex { get; } = new();
    // Use lock per bucket for thread safety on List<T> mutations
    public ConcurrentDictionary<int, (Lock @lock, List<TranslationMemoryEntry> entries)> LengthBuckets { get; } = new();
}
```

- [ ] **Step 2: Register service in Program.cs**

After line 150 (`PreTranslationCacheMonitor`), add:

```csharp
builder.Services.AddSingleton<TranslationMemoryService>();
```

- [ ] **Step 3: Build to verify**

Run: `dotnet build XUnityToolkit-WebUI/XUnityToolkit-WebUI.csproj -p:SkipFrontendBuild=true`
Expected: Build succeeded

- [ ] **Step 4: Commit**

```bash
git add XUnityToolkit-WebUI/Services/TranslationMemoryService.cs XUnityToolkit-WebUI/Program.cs
git commit -m "feat: 添加 TranslationMemoryService 核心存储和精确匹配"
```

---

## Task 3: TranslationMemoryService — Fuzzy Match + Pattern Match

**Files:**
- Modify: `XUnityToolkit-WebUI/Services/TranslationMemoryService.cs`

- [ ] **Step 1: Implement Levenshtein distance algorithm**

Add a private static method inside `TranslationMemoryService`:

```csharp
/// <summary>Compute Levenshtein distance with early termination.</summary>
private static int LevenshteinDistance(string s, string t, int maxDistance)
{
    if (s == t) return 0;
    if (s.Length == 0) return t.Length;
    if (t.Length == 0) return s.Length;
    if (Math.Abs(s.Length - t.Length) > maxDistance) return maxDistance + 1;

    var prev = new int[t.Length + 1];
    var curr = new int[t.Length + 1];
    for (int j = 0; j <= t.Length; j++) prev[j] = j;

    for (int i = 1; i <= s.Length; i++)
    {
        curr[0] = i;
        var rowMin = i;
        for (int j = 1; j <= t.Length; j++)
        {
            int cost = s[i - 1] == t[j - 1] ? 0 : 1;
            curr[j] = Math.Min(Math.Min(curr[j - 1] + 1, prev[j] + 1), prev[j - 1] + cost);
            rowMin = Math.Min(rowMin, curr[j]);
        }
        if (rowMin > maxDistance) return maxDistance + 1; // Early termination
        (prev, curr) = (curr, prev);
    }
    return prev[t.Length];
}
```

- [ ] **Step 2: Implement diff segment extraction**

```csharp
/// <summary>Find contiguous diff segments between two similar strings.</summary>
private static List<(int start, int end, string oldText, string newText)> FindDiffSegments(string original, string changed)
{
    var diffs = new List<(int start, int end, string oldText, string newText)>();

    int left = 0;
    while (left < original.Length && left < changed.Length && original[left] == changed[left])
        left++;

    int rightO = original.Length - 1;
    int rightC = changed.Length - 1;
    while (rightO > left && rightC > left && original[rightO] == changed[rightC])
    {
        rightO--;
        rightC--;
    }

    if (left <= rightO || left <= rightC)
    {
        diffs.Add((left, rightO + 1,
            original[left..(rightO + 1)],
            changed[left..(rightC + 1)]));
    }

    return diffs;
}
```

- [ ] **Step 3: Implement TryFuzzyMatch**

Replace the stub `TryFuzzyMatch` method:

```csharp
private TmMatchResult? TryFuzzyMatch(string text, TranslationMemoryStore store, int threshold)
{
    var textLen = text.Length;
    var minLen = (int)(textLen * 0.8);
    var maxLen = (int)(textLen * 1.2);
    var maxDistance = (int)(textLen * (100 - threshold) / 100.0);

    TranslationMemoryEntry? bestEntry = null;
    int bestDistance = int.MaxValue;
    int candidatesChecked = 0;

    for (int len = minLen; len <= maxLen; len++)
    {
        if (!store.LengthBuckets.TryGetValue(len, out var bucketTuple)) continue;
        var (bucketLock, bucket) = bucketTuple;

        // Take snapshot under lock to avoid concurrent modification
        List<TranslationMemoryEntry> snapshot;
        bucketLock.Enter();
        try { snapshot = [.. bucket]; }
        finally { bucketLock.Exit(); }

        foreach (var entry in snapshot)
        {
            if (candidatesChecked >= 500) break; // Time budget (distributed across buckets)
            candidatesChecked++;

            var key = entry.NormalizedKey ?? entry.Original;
            var dist = LevenshteinDistance(text, key, maxDistance);
            if (dist < bestDistance)
            {
                bestDistance = dist;
                bestEntry = entry;
            }
        }
        if (candidatesChecked >= 500) break;
    }

    if (bestEntry is null || bestDistance > maxDistance) return null;

    // Verify diff is contiguous (not scattered)
    var bestKey = bestEntry.NormalizedKey ?? bestEntry.Original;
    var diffs = FindDiffSegments(bestKey, text);
    if (diffs.Count > 2) return null; // Too many scattered diffs
    var totalDiffLen = diffs.Sum(d => Math.Max(d.oldText.Length, d.newText.Length));
    if (totalDiffLen > text.Length * 0.3) return null; // Too much change

    // Try to replace diff segments in translation
    var translation = bestEntry.Translation;
    foreach (var (_, _, oldText, newText) in diffs)
    {
        var idx = translation.IndexOf(oldText, StringComparison.Ordinal);
        if (idx < 0) return null; // Old text not found in translation — can't substitute
        translation = string.Concat(translation.AsSpan(0, idx), newText, translation.AsSpan(idx + oldText.Length));
    }

    return new TmMatchResult { Translation = translation, MatchType = TmMatchType.Fuzzy };
}
```

- [ ] **Step 4: Implement TryPatternMatch stub (wired in Task 5)**

Replace the stub `TryPatternMatch`:

```csharp
private readonly ConcurrentDictionary<string, List<(Regex regex, string template)>> _patternCache = new();

public void LoadPatterns(string gameId, List<DynamicPattern> patterns)
{
    var compiled = new List<(Regex regex, string template)>();
    foreach (var p in patterns)
    {
        try
        {
            var regex = new Regex(p.OriginalTemplate, RegexOptions.Compiled, TimeSpan.FromSeconds(1));
            compiled.Add((regex, p.TranslatedTemplate));
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "动态模式正则编译失败: {Pattern}", p.OriginalTemplate);
        }
    }
    _patternCache[gameId] = compiled;
}

private TmMatchResult? TryPatternMatch(string gameId, string text, TranslationMemoryStore store)
{
    if (!_patternCache.TryGetValue(gameId, out var patterns)) return null;

    foreach (var (regex, template) in patterns)
    {
        var match = regex.Match(text);
        if (!match.Success) continue;

        // Build translation from template using captured groups
        var translation = template;
        for (int i = 1; i <= match.Groups.Count - 1; i++)
            translation = translation.Replace($"${i}", match.Groups[i].Value);

        return new TmMatchResult { Translation = translation, MatchType = TmMatchType.Pattern };
    }
    return null;
}
```

- [ ] **Step 5: Build to verify**

Run: `dotnet build XUnityToolkit-WebUI/XUnityToolkit-WebUI.csproj -p:SkipFrontendBuild=true`

- [ ] **Step 6: Commit**

```bash
git add XUnityToolkit-WebUI/Services/TranslationMemoryService.cs
git commit -m "feat: 实现编辑距离模糊匹配和动态模式匹配"
```

---

## Task 4: DynamicPatternService — Template Detection + Regex Generation

**Files:**
- Create: `XUnityToolkit-WebUI/Services/DynamicPatternService.cs`
- Modify: `XUnityToolkit-WebUI/Program.cs`

- [ ] **Step 1: Create DynamicPatternService**

```csharp
using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using XUnityToolkit_WebUI.Infrastructure;
using XUnityToolkit_WebUI.Models;

namespace XUnityToolkit_WebUI.Services;

public sealed partial class DynamicPatternService(
    LlmTranslationService translationService,
    AppSettingsService settingsService,
    AppDataPaths paths,
    ILogger<DynamicPatternService> logger)
{
    private readonly ConcurrentDictionary<string, DynamicPatternStore> _cache = new();

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    // Common template variable patterns
    [GeneratedRegex(@"\{[A-Za-z_]\w*\}|<[A-Za-z_]\w*>|%[A-Za-z_]\w*%|\[[A-Za-z_]\w*\]")]
    private static partial Regex TemplateVariableRegex();

    /// <summary>Detect template variables in extracted texts (zero cost, regex scan).</summary>
    public List<(int index, string text, List<(int start, int end, string variable)> variables)>
        DetectTemplateVariables(IList<string> texts)
    {
        var results = new List<(int, string, List<(int, int, string)>)>();

        for (int i = 0; i < texts.Count; i++)
        {
            var matches = TemplateVariableRegex().Matches(texts[i]);
            if (matches.Count == 0) continue;

            var vars = matches.Select(m => (m.Index, m.Index + m.Length, m.Value)).ToList();
            results.Add((i, texts[i], vars));
        }

        return results;
    }

    /// <summary>Generate XUnity r: regex entries from detected variables and translations.</summary>
    public List<string> GenerateRegexEntries(
        IList<(string original, string translation, List<(int start, int end, string variable)> variables)> entries)
    {
        var regexEntries = new List<string>();

        foreach (var (original, translation, variables) in entries)
        {
            if (variables.Count == 0) continue;

            // Build regex pattern: replace variable positions with (.+?) capture groups
            var pattern = new StringBuilder();
            var replacement = new StringBuilder();
            int lastEnd = 0;
            int groupIndex = 1;

            // Sort variables by position
            var sortedVars = variables.OrderBy(v => v.start).ToList();

            foreach (var (start, end, variable) in sortedVars)
            {
                // Escape the literal part before this variable
                var literal = original[lastEnd..start];
                pattern.Append(Regex.Escape(literal));
                pattern.Append("(.+?)");
                lastEnd = end;
                groupIndex++;
            }
            pattern.Append(Regex.Escape(original[lastEnd..]));

            // Safety: must have at least 3 literal chars
            var literalChars = original.Length - sortedVars.Sum(v => v.end - v.start);
            if (literalChars < 3) continue;

            // Build replacement template
            var translationTemplate = BuildTranslationTemplate(original, translation, sortedVars);

            regexEntries.Add($"r:\"{pattern}\"={translationTemplate}");
        }

        return regexEntries;
    }

    private static string BuildTranslationTemplate(string original, string translation,
        List<(int start, int end, string variable)> variables)
    {
        // Find each variable in the translation and replace with $N
        var result = translation;
        for (int i = 0; i < variables.Count; i++)
        {
            var variable = variables[i].variable;
            result = result.Replace(variable, $"${i + 1}");
        }
        return result;
    }

    /// <summary>LLM-assisted dynamic fragment analysis (batch, one-time).</summary>
    public async Task<List<DynamicPattern>> AnalyzeDynamicFragmentsAsync(
        string gameId, IList<(string original, string translation)> pairs,
        CancellationToken ct = default)
    {
        var settings = await settingsService.GetAsync(ct);
        if (!settings.AiTranslation.EnableLlmPatternAnalysis) return [];

        var patterns = new List<DynamicPattern>();

        // Process in batches of 20
        const int batchSize = 20;
        for (int i = 0; i < pairs.Count; i += batchSize)
        {
            ct.ThrowIfCancellationRequested();
            var batch = pairs.Skip(i).Take(batchSize).ToList();

            try
            {
                var prompt = BuildAnalysisPrompt(batch);
                // CallLlmRawAsync signature: (ApiEndpointConfig, string systemPrompt, string userContent, double temperature, CancellationToken)
                // Returns (string content, long tokens) tuple
                var settings2 = await settingsService.GetAsync(ct);
                var endpoint = settings2.AiTranslation.Endpoints
                    .Where(e => e.Enabled && !string.IsNullOrWhiteSpace(e.ApiKey))
                    .OrderByDescending(e => e.Priority)
                    .FirstOrDefault() ?? throw new InvalidOperationException("无可用的 LLM 端点");
                var (result, _) = await translationService.CallLlmRawAsync(
                    endpoint, "你是一名游戏文本动态变量分析专家。", prompt,
                    settings2.AiTranslation.Temperature, ct);
                var detected = ParseAnalysisResult(result, batch);
                patterns.AddRange(detected);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "LLM 动态片段分析失败 (batch {Batch}), 跳过", i / batchSize);
            }
        }

        // Save patterns
        var store = new DynamicPatternStore { Patterns = patterns };
        await SavePatternsAsync(gameId, store, ct);
        _cache[gameId] = store;

        logger.LogInformation("动态模式分析完成: 游戏 {GameId}, 识别 {Count} 个模式", gameId, patterns.Count);
        return patterns;
    }

    public async Task<DynamicPatternStore> GetPatternsAsync(string gameId, CancellationToken ct = default)
    {
        if (_cache.TryGetValue(gameId, out var cached)) return cached;

        var file = paths.DynamicPatternsFile(gameId);
        if (!File.Exists(file)) return new DynamicPatternStore();

        var json = await File.ReadAllTextAsync(file, ct);
        var store = JsonSerializer.Deserialize<DynamicPatternStore>(json, JsonOptions) ?? new DynamicPatternStore();
        _cache[gameId] = store;
        return store;
    }

    public async Task DeleteAsync(string gameId)
    {
        _cache.TryRemove(gameId, out _);
        var file = paths.DynamicPatternsFile(gameId);
        if (File.Exists(file)) File.Delete(file);
    }

    public void RemoveCache(string gameId) => _cache.TryRemove(gameId, out _);
    public void ClearAllCache() => _cache.Clear();

    public int GetPatternCount(string gameId) =>
        _cache.TryGetValue(gameId, out var store) ? store.Patterns.Count : 0;

    private static string BuildAnalysisPrompt(List<(string original, string translation)> batch)
    {
        var sb = new StringBuilder();
        sb.AppendLine("以下是从游戏中提取的对话文本及其翻译。请识别其中可能是动态变量的部分（如玩家名、自定义角色名等会随玩家输入变化的内容）。");
        sb.AppendLine("对每段文本，输出 JSON 数组。如果没有动态部分，该项为 null。");
        sb.AppendLine("只输出 JSON 数组，不要其他内容。");
        sb.AppendLine();
        sb.AppendLine("格式: [{\"index\": 0, \"variables\": [{\"start\": 0, \"end\": 2, \"type\": \"playerName\"}]}, ...]");
        sb.AppendLine();

        for (int i = 0; i < batch.Count; i++)
            sb.AppendLine($"[{i}] 原文: {batch[i].original} | 译文: {batch[i].translation}");

        return sb.ToString();
    }

    private List<DynamicPattern> ParseAnalysisResult(string result,
        List<(string original, string translation)> batch)
    {
        var patterns = new List<DynamicPattern>();

        try
        {
            // Strip markdown fencing if present
            var json = result.Trim();
            if (json.StartsWith("```")) json = json[(json.IndexOf('\n') + 1)..];
            if (json.EndsWith("```")) json = json[..json.LastIndexOf('\n')];
            json = json.Trim();

            using var doc = JsonDocument.Parse(json);
            foreach (var item in doc.RootElement.EnumerateArray())
            {
                if (!item.TryGetProperty("index", out var indexProp)) continue;
                var index = indexProp.GetInt32();
                if (index < 0 || index >= batch.Count) continue;

                if (!item.TryGetProperty("variables", out var varsProp) ||
                    varsProp.ValueKind == JsonValueKind.Null) continue;

                var vars = new List<(int start, int end, string type)>();
                foreach (var v in varsProp.EnumerateArray())
                {
                    var start = v.GetProperty("start").GetInt32();
                    var end = v.GetProperty("end").GetInt32();
                    var type = v.TryGetProperty("type", out var tp) ? tp.GetString() ?? "playerName" : "playerName";
                    vars.Add((start, end, type));
                }

                if (vars.Count == 0) continue;

                var (original, translation) = batch[index];

                // Build regex pattern from detected variables
                var patternStr = new StringBuilder();
                int lastEnd = 0;
                var positions = new List<VariablePosition>();

                foreach (var (start, end, type) in vars.OrderBy(v => v.start))
                {
                    if (start > lastEnd)
                        patternStr.Append(Regex.Escape(original[lastEnd..start]));
                    patternStr.Append("(.+?)");
                    positions.Add(new VariablePosition { Type = type, GroupIndex = positions.Count + 1 });
                    lastEnd = end;
                }
                if (lastEnd < original.Length)
                    patternStr.Append(Regex.Escape(original[lastEnd..]));

                // Safety: at least 3 literal characters
                var literalLen = original.Length - vars.Sum(v => v.end - v.start);
                if (literalLen < 3) continue;

                // Build translated template
                var transTemplate = translation;
                foreach (var (start, end, _) in vars.OrderByDescending(v => v.start))
                {
                    var fragment = original[start..end];
                    var fragIdx = transTemplate.IndexOf(fragment, StringComparison.Ordinal);
                    if (fragIdx >= 0)
                    {
                        var groupNum = vars.OrderBy(v => v.start).ToList().FindIndex(v => v.start == start) + 1;
                        transTemplate = string.Concat(transTemplate.AsSpan(0, fragIdx), $"${groupNum}", transTemplate.AsSpan(fragIdx + fragment.Length));
                    }
                }

                patterns.Add(new DynamicPattern
                {
                    OriginalTemplate = patternStr.ToString(),
                    TranslatedTemplate = transTemplate,
                    VariablePositions = positions,
                    Source = "llm"
                });
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "解析 LLM 动态片段分析结果失败");
        }

        return patterns;
    }

    private async Task SavePatternsAsync(string gameId, DynamicPatternStore store, CancellationToken ct)
    {
        var file = paths.DynamicPatternsFile(gameId);
        var json = JsonSerializer.Serialize(store, JsonOptions);
        var tmp = file + ".tmp";
        await File.WriteAllTextAsync(tmp, json, ct);
        File.Move(tmp, file, overwrite: true);
    }
}
```

- [ ] **Step 2: Register in Program.cs**

Add after `TranslationMemoryService`:
```csharp
builder.Services.AddSingleton<DynamicPatternService>();
```

- [ ] **Step 3: Build to verify**

Run: `dotnet build XUnityToolkit-WebUI/XUnityToolkit-WebUI.csproj -p:SkipFrontendBuild=true`

- [ ] **Step 4: Commit**

```bash
git add XUnityToolkit-WebUI/Services/DynamicPatternService.cs XUnityToolkit-WebUI/Program.cs
git commit -m "feat: 添加 DynamicPatternService 模板检测和正则生成"
```

---

## Task 5: TermExtractionService

**Files:**
- Create: `XUnityToolkit-WebUI/Services/TermExtractionService.cs`
- Modify: `XUnityToolkit-WebUI/Program.cs`

- [ ] **Step 1: Create TermExtractionService**

Follow the `GlossaryExtractionService` prompt+parse pattern but use a dedicated LLM call after translation instead of inline. Store candidates at `{dataRoot}/term-candidates/{gameId}.json`.

Key methods:
- `ExtractFromPairsAsync(gameId, pairs, ct)` — batch LLM extraction calls
- `GetCandidatesAsync(gameId, ct)` — load candidates
- `ApplyCandidatesAsync(gameId, selectedOriginals, ct)` — merge selected candidates into term table via `TermService`
- `DeleteCandidatesAsync(gameId)` — clear candidates

The extraction prompt should be similar to `GlossaryExtractionService.ExtractionPrompt` but simplified (JSON array output only, no merge logic — just collect candidates).

- [ ] **Step 2: Register in Program.cs**

```csharp
builder.Services.AddSingleton<TermExtractionService>();
```

- [ ] **Step 3: Build to verify**

- [ ] **Step 4: Commit**

```bash
git add XUnityToolkit-WebUI/Services/TermExtractionService.cs XUnityToolkit-WebUI/Program.cs
git commit -m "feat: 添加 TermExtractionService 自动术语提取"
```

---

## Task 6: Phase 0 Integration — LlmTranslationService

**Files:**
- Modify: `XUnityToolkit-WebUI/Services/LlmTranslationService.cs:13-21,153-260,693-713`
- Modify: `XUnityToolkit-WebUI/Endpoints/TranslateEndpoints.cs:11-19`

- [ ] **Step 1: Add TranslationMemoryService to LlmTranslationService DI**

In `LlmTranslationService.cs` constructor (line 13), add `TranslationMemoryService translationMemoryService` parameter.

- [ ] **Step 2: Insert Phase 0 in TranslateAsync**

After the enabled endpoint check and before Phase 1 (around line 254), add Phase 0:

```csharp
// ── Phase 0: Translation Memory lookup ──
var tmResults = new Dictionary<int, string>();
if (ai.EnableTranslationMemory && !string.IsNullOrEmpty(gameId))
{
    await translationMemoryService.EnsureLoadedAsync(gameId, ct);
    var currentTerms = await termService.GetAsync(gameId, ct);

    for (int i = 0; i < texts.Count; i++)
    {
        var tmMatch = translationMemoryService.TryMatch(gameId, texts[i], ai.FuzzyMatchThreshold);
        if (tmMatch is null) continue;

        // Term audit check — ensure TM result respects current terms
        if (currentTerms.Count > 0 && ai.TermAuditEnabled)
        {
            var matchedTerms = termMatchingService.FindMatchedTerms(currentTerms, [texts[i]]);
            if (matchedTerms.Count > 0)
            {
                // AuditTranslation takes (translatedText, matchedTerms) — 2 params
                var auditResult = termAuditService.AuditTranslation(tmMatch.Translation, matchedTerms);
                if (!auditResult.Passed) // PascalCase — C# record property
                {
                    continue; // Audit failed — don't use TM, fall through to LLM
                }
            }
        }

        tmResults[i] = tmMatch.Translation;
    }
}

// Filter out TM-resolved texts for Phase 1/2/3
var unresolvedIndices = Enumerable.Range(0, texts.Count)
    .Where(i => !tmResults.ContainsKey(i))
    .ToList();
var unresolvedTexts = unresolvedIndices.Select(i => texts[i]).ToList();
```

Then at the end, merge TM results with LLM results into the final `translations` array.

- [ ] **Step 3: Write to TM after successful translation**

After the existing translation memory accumulation (line 699-704), add:

```csharp
// Persist to TranslationMemory
if (ai.EnableTranslationMemory && !string.IsNullOrEmpty(gameId))
{
    _ = Task.Run(async () =>
    {
        try { await translationMemoryService.AddAsync(gameId, texts, translations, ct: CancellationToken.None); }
        catch (Exception ex) { logger.LogWarning(ex, "翻译记忆写入失败"); }
    });
}
```

- [ ] **Step 4: Track TM stats in RecentTranslation**

For texts resolved by Phase 0, create `RecentTranslation` entries with `TranslationSource = "tmExact"` / `"tmFuzzy"` / `"tmPattern"` based on `tmMatch.MatchType`.

- [ ] **Step 5: Add TM stats to GetStats()**

Wire the `translationMemoryService.GetHitStats()` into the `TranslationStats` returned by `GetStats()`.

- [ ] **Step 6: Inject TranslationMemoryService in TranslateEndpoints.cs**

In `TranslateEndpoints.cs` line 11, add `TranslationMemoryService` to the endpoint handler parameters. Call `EnsureLoadedAsync` alongside the existing `cacheMonitor.EnsureCacheAsync`.

- [ ] **Step 7: Build to verify**

Run: `dotnet build XUnityToolkit-WebUI/XUnityToolkit-WebUI.csproj -p:SkipFrontendBuild=true`

- [ ] **Step 8: Commit**

```bash
git add XUnityToolkit-WebUI/Services/LlmTranslationService.cs XUnityToolkit-WebUI/Endpoints/TranslateEndpoints.cs
git commit -m "feat: 集成 Phase 0 翻译记忆查询到翻译管线"
```

---

## Task 7: Multi-Round PreTranslation Pipeline

**Files:**
- Modify: `XUnityToolkit-WebUI/Services/PreTranslationService.cs:10-19,42-107`

- [ ] **Step 1: Add new service dependencies to PreTranslationService constructor**

Add: `DynamicPatternService dynamicPatternService`, `TermExtractionService termExtractionService`, `TranslationMemoryService translationMemoryService`, `TermService termService`

- [ ] **Step 2: Update PreTranslationStatus model**

In `Models/AssetExtraction.cs`:

a) Add `AwaitingTermReview` to `PreTranslationState` enum (after `Cancelled`, line 45):
```csharp
AwaitingTermReview
```

b) Add multi-round progress fields to `PreTranslationStatus` class (after `Error`, line 36):
```csharp
public int CurrentRound { get; set; }
public string? CurrentPhase { get; set; } // "patternAnalysis", "round1", "termReview", "round2", "writingCache"
public int ExtractedTermCount { get; set; }
public int DynamicPatternCount { get; set; }
```

- [ ] **Step 3: Modify ExecutePreTranslationAsync for multi-round pipeline**

Restructure `ExecutePreTranslationAsync` to:

1. Template variable detection (`dynamicPatternService.DetectTemplateVariables`)
2. Round 1: existing batch translation (current logic)
3. After Round 1: call `termExtractionService.ExtractFromPairsAsync` with source+translation pairs
4. Term candidate review wait (if `AutoApplyExtractedTerms=false`): set status to `AwaitingTermReview`, start 5-min timeout timer
5. Apply candidates → `termExtractionService.ApplyCandidatesAsync`
6. Round 2 (if `EnableMultiRoundTranslation`): re-translate with terms applied, using polish prompt via `overrideSystemPrompt`
7. Write cache files (existing logic)
8. Generate dynamic regex patterns and write to `_PreTranslated_Regex.txt`
9. Batch write to translation memory

- [ ] **Step 4: Add resume endpoint for term review**

Add a method `ResumeAfterTermReview(gameId)` that signals the paused pre-translation to continue.

- [ ] **Step 5: LLM dynamic pattern analysis integration**

After Round 1 translation, if `EnableLlmPatternAnalysis`, call `dynamicPatternService.AnalyzeDynamicFragmentsAsync` with the translated pairs to detect dynamic fragments and generate regex.

- [ ] **Step 6: Write dynamic regex to _PreTranslated_Regex.txt**

Append dynamic pattern regex entries (from both template detection and LLM analysis) to the existing regex file written by `WriteRegexPatternsAsync`.

- [ ] **Step 7: Batch write to TM**

After writing cache files, call `translationMemoryService.AddAsync` with all translated pairs. If multi-round, mark Round 1 as non-final and Round 2 as final.

- [ ] **Step 8: Add SignalR events**

Broadcast `patternAnalysisProgress`, `termExtractionComplete`, `roundProgress` to `pre-translation-{gameId}` group at appropriate points.

- [ ] **Step 9: Build to verify**

Run: `dotnet build XUnityToolkit-WebUI/XUnityToolkit-WebUI.csproj -p:SkipFrontendBuild=true`

- [ ] **Step 10: Commit**

```bash
git add XUnityToolkit-WebUI/Services/PreTranslationService.cs XUnityToolkit-WebUI/Models/AssetExtraction.cs
git commit -m "feat: 预翻译多轮管线 + 动态模式分析 + 术语提取"
```

---

## Task 8: API Endpoints

**Files:**
- Create: `XUnityToolkit-WebUI/Endpoints/TranslationMemoryEndpoints.cs`
- Modify: `XUnityToolkit-WebUI/Endpoints/GameEndpoints.cs`
- Modify: `XUnityToolkit-WebUI/Endpoints/SettingsEndpoints.cs`
- Modify: `XUnityToolkit-WebUI/Program.cs` (endpoint mapping)

- [ ] **Step 1: Create TranslationMemoryEndpoints**

New file with endpoints:
- `GET /api/games/{id}/translation-memory/stats` → return entry count + hit rates
- `DELETE /api/games/{id}/translation-memory` → clear TM
- `GET /api/games/{id}/dynamic-patterns` → return patterns
- `DELETE /api/games/{id}/dynamic-patterns` → clear patterns
- `GET /api/games/{id}/term-candidates` → return candidates
- `POST /api/games/{id}/term-candidates/apply` → apply selected candidates (request body: `{ originals: string[] }`), **then call `preTranslationService.ResumeAfterTermReview(gameId)`** to resume paused pre-translation
- `DELETE /api/games/{id}/term-candidates` → discard candidates

All wrapped in `ApiResult<T>` pattern. The apply endpoint must inject `PreTranslationService` and call resume.

- [ ] **Step 2: Map endpoints in Program.cs**

Add `app.MapTranslationMemoryEndpoints();` alongside other endpoint mappings.

- [ ] **Step 3: Add per-game cleanup to GameEndpoints.cs**

In `DELETE /api/games/{id}` handler (at `GameEndpoints.cs`), first update the endpoint DI parameter list to inject `TranslationMemoryService`, `DynamicPatternService`, and `TermExtractionService`. Then add cleanup:
```csharp
translationMemoryService.RemoveCache(gameId);
dynamicPatternService.RemoveCache(gameId);
termExtractionService.RemoveCache(gameId);
// Delete files
var tmFile = paths.TranslationMemoryFile(gameId);
if (File.Exists(tmFile)) File.Delete(tmFile);
var dpFile = paths.DynamicPatternsFile(gameId);
if (File.Exists(dpFile)) File.Delete(dpFile);
var tcFile = paths.TermCandidatesFile(gameId);
if (File.Exists(tcFile)) File.Delete(tcFile);
```

- [ ] **Step 4: Add export exclusion in SettingsEndpoints.cs**

Add `"translation-memory"`, `"dynamic-patterns"`, `"term-candidates"` to the `excludedDirs` set in the `/export` endpoint.

- [ ] **Step 5: Add cache clearing in settings reset**

In `POST /api/settings/reset` handler (`SettingsEndpoints.cs`), first update the endpoint DI parameter list to inject `TranslationMemoryService`, `DynamicPatternService`, and `TermExtractionService`. Then add:
```csharp
translationMemoryService.ClearAllCache();
dynamicPatternService.ClearAllCache();
termExtractionService.ClearAllCache();
```

- [ ] **Step 6: Build to verify**

- [ ] **Step 7: Commit**

```bash
git add XUnityToolkit-WebUI/Endpoints/ XUnityToolkit-WebUI/Program.cs
git commit -m "feat: 添加翻译记忆 API 端点和游戏清理逻辑"
```

---

## Task 9: Frontend Types + Settings

**Files:**
- Modify: `XUnityToolkit-Vue/src/api/types.ts`
- Modify: `XUnityToolkit-Vue/src/views/SettingsView.vue`

- [ ] **Step 1: Update types.ts**

Add to `AiTranslationSettings` interface:
```typescript
enableTranslationMemory: boolean
fuzzyMatchThreshold: number
enableLlmPatternAnalysis: boolean
enableMultiRoundTranslation: boolean
enableAutoTermExtraction: boolean
autoApplyExtractedTerms: boolean
```

Add to `TranslationStats` interface:
```typescript
translationMemoryHits: number
translationMemoryFuzzyHits: number
translationMemoryPatternHits: number
translationMemoryMisses: number
dynamicPatternCount: number
extractedTermCount: number
```

Add to `RecentTranslation` interface:
```typescript
translationSource?: string | null
```

Add `TermSource` type:
```typescript
export type TermSource = 'User' | 'AI' | 'Import'
```

Add `Source` to `TermEntry`:
```typescript
source?: TermSource
```

Add new interfaces:
```typescript
export interface TranslationMemoryStats {
  entryCount: number
  exactHits: number
  fuzzyHits: number
  patternHits: number
  misses: number
}

export interface DynamicPattern {
  originalTemplate: string
  translatedTemplate: string
  source: string
}

export interface TermCandidate {
  original: string
  translation: string
  category: TermCategory
  frequency: number
}
```

- [ ] **Step 2: Add settings to SettingsView.vue**

In the AI Translation settings section, add controls:
- `NSwitch` for `enableTranslationMemory`
- `NSlider` for `fuzzyMatchThreshold` (0-100)
- `NSwitch` for `enableLlmPatternAnalysis`
- `NSwitch` for `enableMultiRoundTranslation`
- `NSwitch` for `enableAutoTermExtraction`
- `NSwitch` for `autoApplyExtractedTerms`

Follow existing pattern for settings controls in this file.

- [ ] **Step 3: Update AiTranslationView.vue defaults**

Add default values for new settings fields in `DEFAULT_AI_TRANSLATION` constant.

- [ ] **Step 4: Add API call functions in src/api/games.ts**

Add functions for the new endpoints in `src/api/games.ts`:
```typescript
// Translation Memory
export const getTranslationMemoryStats = (gameId: string) =>
  api.get<TranslationMemoryStats>(`/api/games/${gameId}/translation-memory/stats`)
export const clearTranslationMemory = (gameId: string) =>
  api.del(`/api/games/${gameId}/translation-memory`)

// Dynamic Patterns
export const getDynamicPatterns = (gameId: string) =>
  api.get<DynamicPattern[]>(`/api/games/${gameId}/dynamic-patterns`)
export const clearDynamicPatterns = (gameId: string) =>
  api.del(`/api/games/${gameId}/dynamic-patterns`)

// Term Candidates
export const getTermCandidates = (gameId: string) =>
  api.get<TermCandidate[]>(`/api/games/${gameId}/term-candidates`)
export const applyTermCandidates = (gameId: string, originals: string[]) =>
  api.post(`/api/games/${gameId}/term-candidates/apply`, { originals })
export const clearTermCandidates = (gameId: string) =>
  api.del(`/api/games/${gameId}/term-candidates`)
```

- [ ] **Step 5: Run type check**

Run: `cd XUnityToolkit-Vue && npx vue-tsc --build`

- [ ] **Step 6: Commit**

```bash
git add XUnityToolkit-Vue/src/api/types.ts XUnityToolkit-Vue/src/api/games.ts XUnityToolkit-Vue/src/views/SettingsView.vue XUnityToolkit-Vue/src/views/AiTranslationView.vue
git commit -m "feat: 前端添加翻译记忆类型定义和设置控件"
```

---

## Task 10: Frontend — TM Stats Display + Translation Source Tags

**Files:**
- Modify: `XUnityToolkit-Vue/src/views/AiTranslationView.vue`

- [ ] **Step 1: Add TM stats card**

In the stats section of `AiTranslationView.vue`, add a new card showing:
- TM entry count (for current game)
- Exact / Fuzzy / Pattern hit counts
- TM hit rate percentage
- Dynamic pattern count

Follow existing stats card pattern (e.g., the pre-translation cache stats card).

- [ ] **Step 2: Add translation source tags to recent translations**

In the recent translations list, add a colored tag showing the translation source:
- `LLM` (default, no tag or subtle tag)
- `TM 精确` (green tag)
- `TM 模糊` (blue tag)
- `动态模式` (purple tag)

Use `NTag` component with appropriate type/color.

- [ ] **Step 3: Run type check and build**

Run: `cd XUnityToolkit-Vue && npx vue-tsc --build && npm run build`

- [ ] **Step 4: Commit**

```bash
git add XUnityToolkit-Vue/src/views/AiTranslationView.vue
git commit -m "feat: 前端显示翻译记忆统计和翻译来源标签"
```

---

## Task 11: Frontend — Pre-Translation Multi-Step Progress + Term Review

**Files:**
- Modify: `XUnityToolkit-Vue/src/views/AssetExtractionView.vue`

- [ ] **Step 1: Update pre-translation progress display**

Replace the single progress bar with a multi-step display showing:
```
[模式分析] → [第一轮翻译] → [术语审核] → [第二轮润色] → [写入缓存]
```

Each step shows: completed (✓), in-progress (spinner), pending (gray), or waiting (yellow for term review).

- [ ] **Step 2: Add term review modal**

When pre-translation enters `AwaitingTermReview` state (detected via SignalR `termExtractionComplete` event or status polling):

- Show a modal with the list of AI-extracted term candidates
- Each row: Original, Translation, Category, ✓ Accept / ✗ Reject / ✏️ Edit buttons
- One-click "全部接受" / "全部拒绝" buttons
- "确认" button calls `POST /api/games/{id}/term-candidates/apply`

- [ ] **Step 3: Handle SignalR events**

Subscribe to `roundProgress`, `patternAnalysisProgress`, `termExtractionComplete` events in the pre-translation SignalR group. Update the multi-step display accordingly.

- [ ] **Step 4: Run type check and build**

Run: `cd XUnityToolkit-Vue && npx vue-tsc --build && npm run build`

- [ ] **Step 5: Commit**

```bash
git add XUnityToolkit-Vue/src/views/AssetExtractionView.vue
git commit -m "feat: 前端预翻译多步骤进度和术语审核弹窗"
```

---

## Task 12: Full Build + CLAUDE.md Update

**Files:**
- Modify: `CLAUDE.md`

- [ ] **Step 1: Full build**

Run: `dotnet build XUnityToolkit-WebUI/XUnityToolkit-WebUI.csproj`
(This also builds the frontend via MSBuild target)

- [ ] **Step 2: Fix any build errors**

- [ ] **Step 3: Update CLAUDE.md**

Add documentation for:
- New service descriptions (TranslationMemoryService, DynamicPatternService, TermExtractionService)
- Translation Memory architecture section
- Multi-round translation pipeline description
- New data paths (translation-memory/, dynamic-patterns/, term-candidates/)
- New per-game cleanup items
- New sync points (AiTranslationSettings 5 fields, TranslationStats 6 fields, TermEntry.Source)
- New API endpoints
- Export exclusion updates

- [ ] **Step 4: Final commit**

```bash
git add CLAUDE.md
git commit -m "docs: 更新 CLAUDE.md 添加翻译记忆和多轮翻译文档"
```
