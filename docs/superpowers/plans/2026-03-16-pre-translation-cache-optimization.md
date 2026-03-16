# Pre-Translation Cache Optimization Implementation Plan

> **For agentic workers:** REQUIRED: Use superpowers:subagent-driven-development (if subagents available) or superpowers:executing-plans to implement this plan. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make pre-translation cache entries actually match XUnity.AutoTranslator's runtime text lookups by leveraging XUnity's native whitespace tolerance, rich text stripping, number templating, and splitter regex features, plus real-time cache hit monitoring.

**Architecture:** Four-layer optimization gated behind a per-game `EnablePreTranslationCache` toggle (default off, experimental). Layer 1: XUnity INI config. Layer 2: Cache key normalization in `PreTranslationService`. Layer 3: `sr:` regex file generation. Layer 4: `PreTranslationCacheMonitor` singleton tracking cache hits/misses via SignalR.

**Tech Stack:** ASP.NET Core (.NET 10), Vue 3 + TypeScript + Naive UI + Pinia, SignalR

**Spec:** `docs/superpowers/specs/2026-03-16-pre-translation-cache-optimization-design.md`

---

## Chunk 1: Backend Models & NormalizeForCache

### Task 1: Add `EnablePreTranslationCache` to AiTranslationSettings

**Files:**
- Modify: `XUnityToolkit-WebUI/Models/AiTranslationSettings.cs`

- [ ] **Step 1: Add the field**

Add at the end of `AiTranslationSettings` (before `Endpoints`):

```csharp
/// <summary>
/// Experimental: enable pre-translation cache optimization (normalization, regex generation, monitoring).
/// </summary>
public bool EnablePreTranslationCache { get; set; }
```

- [ ] **Step 2: Build backend**

```bash
dotnet build XUnityToolkit-WebUI/XUnityToolkit-WebUI.csproj -p:SkipFrontendBuild=true
```

Expected: Build succeeded.

- [ ] **Step 3: Commit**

```bash
git add XUnityToolkit-WebUI/Models/AiTranslationSettings.cs
git commit -m "feat: 添加 EnablePreTranslationCache 开关到 AiTranslationSettings"
```

---

### Task 2: Add new XUnityConfig fields

**Files:**
- Modify: `XUnityToolkit-WebUI/Models/XUnityConfig.cs`

- [ ] **Step 1: Add three new fields and change TemplateAllNumberAway default**

In the `// [Behaviour]` section, add after `CacheParsedTranslations`:

```csharp
public bool CacheWhitespaceDifferences { get; set; }
public bool IgnoreWhitespaceInDialogue { get; set; } = true;
public int MinDialogueChars { get; set; } = 4;
```

Also change the existing `TemplateAllNumberAway` default — but wait, the spec says to change default to `true` only when the feature is enabled. The default in `XUnityConfig` stays `false` (it's the model default). The `true` default is applied conditionally during installation when `EnablePreTranslationCache` is on. So **do NOT change the model default**.

- [ ] **Step 2: Build backend**

```bash
dotnet build XUnityToolkit-WebUI/XUnityToolkit-WebUI.csproj -p:SkipFrontendBuild=true
```

- [ ] **Step 3: Commit**

```bash
git add XUnityToolkit-WebUI/Models/XUnityConfig.cs
git commit -m "feat: 添加 XUnity 空白容错和对话忽略空白配置字段"
```

---

### Task 3: Add PreTranslationCacheStats model

**Files:**
- Modify: `XUnityToolkit-WebUI/Models/TranslationStats.cs`

- [ ] **Step 1: Add records at end of file**

```csharp
public sealed record PreTranslationCacheStats(
    int TotalPreTranslated,
    int CacheHits,
    int CacheMisses,
    int NewTexts,
    double HitRate,
    IList<CacheMissEntry> RecentMisses
);

public sealed record CacheMissEntry(
    string PreTranslatedKey,
    string RuntimeText,
    DateTime Timestamp
);
```

- [ ] **Step 2: Build and commit**

```bash
dotnet build XUnityToolkit-WebUI/XUnityToolkit-WebUI.csproj -p:SkipFrontendBuild=true
git add XUnityToolkit-WebUI/Models/TranslationStats.cs
git commit -m "feat: 添加 PreTranslationCacheStats 和 CacheMissEntry 模型"
```

---

### Task 4: Add NormalizeForCache to XUnityTranslationFormat

**Files:**
- Modify: `XUnityToolkit-WebUI/Services/XUnityTranslationFormat.cs`

- [ ] **Step 1: Add the regex and method**

Add at the top of the class (after the class declaration):

```csharp
private static readonly Regex RichTextTagRegex = new(
    @"</?(?:color|b|i|size|sprite|material|quad|voffset|indent|link|mark|sup|sub|font|cspace|align|mspace|uppercase|lowercase|smallcaps|noparse|nobr|space|width|margin|rotate|s|u|line-height|line-indent|page|style|br)(?:=[^>]*)?>",
    RegexOptions.Compiled | RegexOptions.IgnoreCase);

/// <summary>
/// Normalize text for XUnity cache key: strip known rich text tags and trim whitespace.
/// Mirrors XUnity's HandleRichText=True preprocessing so extracted asset text
/// matches runtime text after XUnity's own preprocessing.
/// </summary>
internal static string NormalizeForCache(string text)
{
    var stripped = RichTextTagRegex.Replace(text, string.Empty);
    return stripped.Trim();
}
```

Also add `using System.Text.RegularExpressions;` at the top if not present.

- [ ] **Step 2: Build and commit**

```bash
dotnet build XUnityToolkit-WebUI/XUnityToolkit-WebUI.csproj -p:SkipFrontendBuild=true
git add XUnityToolkit-WebUI/Services/XUnityTranslationFormat.cs
git commit -m "feat: 添加 NormalizeForCache 方法用于预翻译缓存键归一化"
```

---

### Task 5: Add AppDataPaths for regex patterns

**Files:**
- Modify: `XUnityToolkit-WebUI/Infrastructure/AppDataPaths.cs`

- [ ] **Step 1: Add directory and file path**

Add a new directory property (follow existing patterns like `DoNotTranslateDirectory`):

```csharp
public string PreTranslationRegexDirectory => Path.Combine(CacheDirectory, "pre-translation-regex");
```

Add a per-game file helper:

```csharp
public string PreTranslationRegexFile(string gameId) =>
    Path.Combine(PreTranslationRegexDirectory, $"{gameId}.txt");
```

Add `PreTranslationRegexDirectory` to `EnsureDirectoriesExist()` (follow the pattern of other `Directory.CreateDirectory` calls).

- [ ] **Step 2: Build and commit**

```bash
dotnet build XUnityToolkit-WebUI/XUnityToolkit-WebUI.csproj -p:SkipFrontendBuild=true
git add XUnityToolkit-WebUI/Infrastructure/AppDataPaths.cs
git commit -m "feat: 添加预翻译正则模式的 AppDataPaths 路径"
```

---

## Chunk 2: Backend Services

### Task 6: Update ConfigurationService for new INI fields

**Files:**
- Modify: `XUnityToolkit-WebUI/Services/ConfigurationService.cs`

- [ ] **Step 1: Add reading of new fields**

In the method that reads `[Behaviour]` section keys (where `MaxCharactersPerTranslation`, `HandleRichText` etc. are read), add:

```csharp
CacheWhitespaceDifferences = GetBool(behaviour, "CacheWhitespaceDifferences"),
IgnoreWhitespaceInDialogue = GetBool(behaviour, "IgnoreWhitespaceInDialogue", true),
MinDialogueChars = GetInt(behaviour, "MinDialogueChars", 4),
```

- [ ] **Step 2: Add writing of new fields**

In `BuildSectionMods` (the method that builds section modifications from `XUnityConfig`), add entries for the `[Behaviour]` section:

```csharp
{ "CacheWhitespaceDifferences", config.CacheWhitespaceDifferences.ToString() },
{ "IgnoreWhitespaceInDialogue", config.IgnoreWhitespaceInDialogue.ToString() },
{ "MinDialogueChars", config.MinDialogueChars.ToString() },
```

Follow the exact pattern used by existing `[Behaviour]` fields like `HandleRichText`.

- [ ] **Step 3: Build and commit**

```bash
dotnet build XUnityToolkit-WebUI/XUnityToolkit-WebUI.csproj -p:SkipFrontendBuild=true
git add XUnityToolkit-WebUI/Services/ConfigurationService.cs
git commit -m "feat: ConfigurationService 支持读写空白容错和对话忽略空白配置"
```

---

### Task 7: Update PreTranslationService for normalization and regex

**Files:**
- Modify: `XUnityToolkit-WebUI/Services/PreTranslationService.cs`

- [ ] **Step 1: Inject AppSettingsService to check EnablePreTranslationCache**

The service already injects `AppSettingsService`. In `WriteTranslationCacheAsync`, read the setting:

```csharp
var settings = await appSettings.GetAsync();
var enableCacheOptimization = settings.AiTranslation.EnablePreTranslationCache;
```

- [ ] **Step 2: Apply normalization conditionally**

In `WriteTranslationCacheAsync`, when building each entry, conditionally normalize:

```csharp
var originalKey = enableCacheOptimization
    ? XUnityTranslationFormat.NormalizeForCache(original)
    : original;

if (string.IsNullOrEmpty(originalKey)) continue; // skip if normalization emptied it

var encodedOriginal = XUnityTranslationFormat.Encode(originalKey);
```

- [ ] **Step 3: Add regex file generation**

After writing `_PreTranslated.txt`, if `enableCacheOptimization` is true, write `_PreTranslated_Regex.txt`:

```csharp
if (enableCacheOptimization)
{
    await WriteRegexPatternsAsync(gamePath, toLang, gameId, ct);
}
```

Add the method:

```csharp
private async Task WriteRegexPatternsAsync(string gamePath, string toLang, string gameId, CancellationToken ct)
{
    var dir = Path.Combine(gamePath, "BepInEx", "Translation", toLang, "Text");
    Directory.CreateDirectory(dir);
    var filePath = Path.Combine(dir, "_PreTranslated_Regex.txt");

    var sb = new StringBuilder();
    sb.AppendLine("// Pre-translation regex patterns generated by XUnity Toolkit");
    sb.AppendLine();
    sb.AppendLine("// Two-line concatenation (both groups are text)");
    sb.AppendLine("sr:\"^([\\S\\s]+?)\\n([\\S\\s]+)$\"=$1\\n$2");
    sb.AppendLine();
    sb.AppendLine("// Three-line concatenation");
    sb.AppendLine("sr:\"^([\\S\\s]+?)\\n([\\S\\s]+?)\\n([\\S\\s]+)$\"=$1\\n$2\\n$3");

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
```

Inject `AppDataPaths` into the constructor if not already present.

- [ ] **Step 4: Build and commit**

```bash
dotnet build XUnityToolkit-WebUI/XUnityToolkit-WebUI.csproj -p:SkipFrontendBuild=true
git add XUnityToolkit-WebUI/Services/PreTranslationService.cs
git commit -m "feat: PreTranslationService 支持缓存键归一化和正则模式生成"
```

---

### Task 8: Create PreTranslationCacheMonitor service

**Files:**
- Create: `XUnityToolkit-WebUI/Services/PreTranslationCacheMonitor.cs`

- [ ] **Step 1: Write the service**

```csharp
using System.Collections.Concurrent;
using Microsoft.AspNetCore.SignalR;
using XUnityToolkit_WebUI.Hubs;
using XUnityToolkit_WebUI.Models;

namespace XUnityToolkit_WebUI.Services;

public sealed class PreTranslationCacheMonitor(
    IHubContext<InstallProgressHub> hubContext,
    ILogger<PreTranslationCacheMonitor> logger)
{
    private volatile string? _activeGameId;
    private HashSet<string> _preTranslatedKeys = [];
    private readonly ConcurrentDictionary<string, string> _misses = new();
    private long _newTexts;
    private long _lastBroadcastTicks;
    private Timer? _summaryTimer;

    private const int MaxRecentMisses = 50;
    private readonly ConcurrentQueue<CacheMissEntry> _recentMisses = new();

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
            keys.Add(XUnityTranslationFormat.NormalizeForCache(decoded));
        }

        _preTranslatedKeys = keys;
        _activeGameId = gameId;
        _summaryTimer = new Timer(BroadcastSummary, null, TimeSpan.FromSeconds(60), TimeSpan.FromSeconds(60));

        logger.LogInformation("预翻译缓存已加载: {GameId}, {Count} 条条目", gameId, keys.Count);
    }

    public void UnloadCache()
    {
        var prev = _activeGameId;
        _activeGameId = null;
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
            var normalized = XUnityTranslationFormat.NormalizeForCache(text);
            if (_preTranslatedKeys.Contains(normalized))
            {
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
```

- [ ] **Step 2: Register in Program.cs**

Add after `builder.Services.AddSingleton<PreTranslationService>();`:

```csharp
builder.Services.AddSingleton<PreTranslationCacheMonitor>();
```

- [ ] **Step 3: Build and commit**

```bash
dotnet build XUnityToolkit-WebUI/XUnityToolkit-WebUI.csproj -p:SkipFrontendBuild=true
git add XUnityToolkit-WebUI/Services/PreTranslationCacheMonitor.cs XUnityToolkit-WebUI/Program.cs
git commit -m "feat: 添加 PreTranslationCacheMonitor 服务实现缓存命中监控"
```

---

## Chunk 3: Backend Endpoints & Integration

### Task 9: Update TranslateEndpoints

**Files:**
- Modify: `XUnityToolkit-WebUI/Endpoints/TranslateEndpoints.cs`

- [ ] **Step 1: Inject PreTranslationCacheMonitor and add cache-stats endpoint**

In `MapTranslateEndpoints`, add the cache-stats endpoint:

```csharp
group.MapGet("/cache-stats", (PreTranslationCacheMonitor cacheMonitor) =>
    ApiResult<PreTranslationCacheStats>.Ok(cacheMonitor.GetStats()));
```

- [ ] **Step 2: Call RecordTexts in POST /api/translate**

In the `POST /api/translate` handler, after validating `request.Texts` and before calling `translationService.TranslateAsync`, add:

```csharp
if (!string.IsNullOrEmpty(request.GameId))
    cacheMonitor.RecordTexts(request.GameId, request.Texts);
```

Add `PreTranslationCacheMonitor cacheMonitor` to the handler's parameter list.

- [ ] **Step 3: Build and commit**

```bash
dotnet build XUnityToolkit-WebUI/XUnityToolkit-WebUI.csproj -p:SkipFrontendBuild=true
git add XUnityToolkit-WebUI/Endpoints/TranslateEndpoints.cs
git commit -m "feat: TranslateEndpoints 集成缓存监控和 cache-stats 端点"
```

---

### Task 10: Add regex CRUD endpoints in AssetEndpoints

**Files:**
- Modify: `XUnityToolkit-WebUI/Endpoints/AssetEndpoints.cs`

- [ ] **Step 1: Add GET and PUT endpoints for custom regex patterns**

In `MapAssetEndpoints`, add:

```csharp
group.MapGet("/{id}/pre-translate/regex", async (string id, AppDataPaths paths) =>
{
    var file = paths.PreTranslationRegexFile(id);
    if (!File.Exists(file))
        return ApiResult<string>.Ok("");
    var content = await File.ReadAllTextAsync(file);
    return ApiResult<string>.Ok(content);
});

group.MapPut("/{id}/pre-translate/regex", async (string id, RegexPatternsRequest request, AppDataPaths paths) =>
{
    // Validate patterns
    foreach (var line in (request.Patterns ?? "").Split('\n', StringSplitOptions.RemoveEmptyEntries))
    {
        var trimmed = line.Trim();
        if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith("//")) continue;
        if (!trimmed.StartsWith("sr:") && !trimmed.StartsWith("r:"))
            return ApiResult.Fail("每行必须以 sr: 或 r: 开头，或为 // 注释");
        if (!trimmed.Contains('='))
            return ApiResult.Fail($"模式缺少 = 分隔符: {trimmed}");

        // Extract regex pattern for validation
        var patternStart = trimmed.IndexOf('"') + 1;
        var patternEnd = trimmed.IndexOf('"', patternStart);
        if (patternStart > 0 && patternEnd > patternStart)
        {
            var pattern = trimmed[patternStart..patternEnd];
            try { _ = new System.Text.RegularExpressions.Regex(pattern, default, TimeSpan.FromSeconds(1)); }
            catch (Exception ex) { return ApiResult.Fail($"无效的正则表达式: {ex.Message}"); }
        }
    }

    var file = paths.PreTranslationRegexFile(id);
    await File.WriteAllTextAsync(file, request.Patterns ?? "");
    return ApiResult.Ok();
});
```

Add the request record at the bottom of the file:

```csharp
record RegexPatternsRequest(string? Patterns);
```

- [ ] **Step 2: Build and commit**

```bash
dotnet build XUnityToolkit-WebUI/XUnityToolkit-WebUI.csproj -p:SkipFrontendBuild=true
git add XUnityToolkit-WebUI/Endpoints/AssetEndpoints.cs
git commit -m "feat: 添加预翻译自定义正则模式的 CRUD 端点"
```

---

### Task 11: Update InstallOrchestrator for conditional XUnity config defaults

**Files:**
- Modify: `XUnityToolkit-WebUI/Services/InstallOrchestrator.cs`

- [ ] **Step 1: Apply pre-translation cache config during installation**

After `configService.ApplyOptimalDefaultsAsync(game.GamePath, ct)` and user config override, add a conditional section:

```csharp
// Apply pre-translation cache optimization config if enabled
var settings = await appSettings.GetAsync();
if (settings.AiTranslation.EnablePreTranslationCache)
{
    await configService.PatchSectionAsync(game.GamePath, "Behaviour", new Dictionary<string, string>
    {
        ["CacheWhitespaceDifferences"] = "False",
        ["IgnoreWhitespaceInDialogue"] = "True",
        ["MinDialogueChars"] = "4",
        ["TemplateAllNumberAway"] = "True"
    }, ct);
}
```

- [ ] **Step 2: Build and commit**

```bash
dotnet build XUnityToolkit-WebUI/XUnityToolkit-WebUI.csproj -p:SkipFrontendBuild=true
git add XUnityToolkit-WebUI/Services/InstallOrchestrator.cs
git commit -m "feat: 安装时根据预翻译缓存开关条件应用 XUnity 配置"
```

---

## Chunk 4: Frontend

### Task 12: Update frontend types

**Files:**
- Modify: `XUnityToolkit-Vue/src/api/types.ts`

- [ ] **Step 1: Add EnablePreTranslationCache to AiTranslationSettings**

```typescript
enablePreTranslationCache: boolean
```

- [ ] **Step 2: Add XUnityConfig fields**

In the `XUnityConfig` interface, add in the `[Behaviour]` section:

```typescript
cacheWhitespaceDifferences: boolean
ignoreWhitespaceInDialogue: boolean
minDialogueChars: number
```

- [ ] **Step 3: Add PreTranslationCacheStats and CacheMissEntry**

```typescript
export interface PreTranslationCacheStats {
  totalPreTranslated: number
  cacheHits: number
  cacheMisses: number
  newTexts: number
  hitRate: number
  recentMisses: CacheMissEntry[]
}

export interface CacheMissEntry {
  preTranslatedKey: string
  runtimeText: string
  timestamp: string
}
```

- [ ] **Step 4: Commit**

```bash
git add XUnityToolkit-Vue/src/api/types.ts
git commit -m "feat: 添加预翻译缓存相关的前端类型定义"
```

---

### Task 13: Update aiTranslation store

**Files:**
- Modify: `XUnityToolkit-Vue/src/stores/aiTranslation.ts`

- [ ] **Step 1: Add cacheStats ref and SignalR subscription**

Add a new ref:

```typescript
const cacheStats = ref<PreTranslationCacheStats | null>(null)
```

In the `connect()` method, after subscribing to `extractionStatsUpdate`, add:

```typescript
connection.on('preCacheStatsUpdate', (data: PreTranslationCacheStats) => {
  cacheStats.value = data
})
```

Add a `fetchCacheStats` method:

```typescript
async function fetchCacheStats() {
  try {
    const res = await fetch('/api/translate/cache-stats')
    const json = await res.json()
    if (json.success) cacheStats.value = json.data
  } catch { /* ignore */ }
}
```

Export `cacheStats` and `fetchCacheStats` from the store.

- [ ] **Step 2: Commit**

```bash
git add XUnityToolkit-Vue/src/stores/aiTranslation.ts
git commit -m "feat: aiTranslation store 添加缓存统计订阅和获取"
```

---

### Task 14: Add cache status card to AiTranslationView

**Files:**
- Modify: `XUnityToolkit-Vue/src/views/AiTranslationView.vue`

- [ ] **Step 1: Update DEFAULT_AI_TRANSLATION**

Add to the `DEFAULT_AI_TRANSLATION` object:

```typescript
enablePreTranslationCache: false,
```

- [ ] **Step 2: Add cacheStats to template data**

In `onMounted`, after `aiStore.fetchStats()`, add:

```typescript
aiStore.fetchCacheStats()
```

- [ ] **Step 3: Add the cache status card template**

Add after the Status Dashboard card and before the Recent Translations card. The card should follow the existing `.section-card` pattern:

```html
<!-- Pre-Translation Cache Stats -->
<Transition name="card-slide">
  <div v-if="aiStore.cacheStats && aiStore.cacheStats.totalPreTranslated > 0" class="section-card" style="animation-delay: 0.12s">
    <div class="section-header">
      <div class="section-title">
        <span class="section-icon"><DatabaseOutlined /></span>
        预翻译缓存
        <NTag size="small" type="warning" style="margin-left: 8px">实验性</NTag>
      </div>
    </div>
    <div class="cache-metrics">
      <div class="metric-pill">
        <span class="metric-label">总条目</span>
        <span class="metric-value">{{ aiStore.cacheStats.totalPreTranslated }}</span>
      </div>
      <div class="metric-pill">
        <span class="metric-label">命中</span>
        <span class="metric-value" style="color: var(--success-color, #18a058)">{{ aiStore.cacheStats.cacheHits }}</span>
      </div>
      <div class="metric-pill">
        <span class="metric-label">未命中</span>
        <span class="metric-value" style="color: var(--error-color, #d03050)">{{ aiStore.cacheStats.cacheMisses }}</span>
      </div>
      <div class="metric-pill">
        <span class="metric-label">命中率</span>
        <span class="metric-value">{{ aiStore.cacheStats.hitRate }}%</span>
      </div>
    </div>
    <NProgress
      type="line"
      :percentage="aiStore.cacheStats.hitRate"
      :color="aiStore.cacheStats.hitRate > 50 ? '#18a058' : '#d03050'"
      :rail-color="'rgba(128,128,128,0.2)'"
      style="margin: 12px 0"
    />
    <NCollapse v-if="aiStore.cacheStats.recentMisses.length > 0">
      <NCollapseItem title="最近未命中" :name="1">
        <div class="recent-list">
          <div v-for="miss in aiStore.cacheStats.recentMisses" :key="miss.timestamp" class="miss-item">
            <div class="miss-row">
              <span class="miss-label">预翻译键:</span>
              <code class="miss-text">{{ miss.preTranslatedKey }}</code>
            </div>
            <div class="miss-row">
              <span class="miss-label">运行时文本:</span>
              <code class="miss-text">{{ miss.runtimeText }}</code>
            </div>
          </div>
        </div>
      </NCollapseItem>
    </NCollapse>
  </div>
</Transition>
```

- [ ] **Step 4: Add styles**

Add in the `<style scoped>` section:

```css
.cache-metrics {
  display: flex;
  gap: 12px;
  flex-wrap: wrap;
}

.miss-item {
  padding: 8px 0;
  border-bottom: 1px solid rgba(128, 128, 128, 0.1);
}

.miss-item:last-child {
  border-bottom: none;
}

.miss-row {
  display: flex;
  align-items: baseline;
  gap: 8px;
  margin: 2px 0;
}

.miss-label {
  font-size: 12px;
  color: var(--text-color-3);
  white-space: nowrap;
}

.miss-text {
  font-size: 12px;
  word-break: break-all;
}
```

- [ ] **Step 5: Add necessary imports**

Import `NTag`, `NProgress`, `NCollapse`, `NCollapseItem` from naive-ui if not already imported. Import `DatabaseOutlined` icon (or use an existing icon that fits).

- [ ] **Step 6: Build frontend and commit**

```bash
cd XUnityToolkit-Vue && npx vue-tsc --noEmit
git add XUnityToolkit-Vue/src/views/AiTranslationView.vue
git commit -m "feat: AI 翻译页面添加预翻译缓存状态卡片"
```

---

### Task 15: Update ConfigPanel for new XUnity config fields

**Files:**
- Modify: `XUnityToolkit-Vue/src/components/config/ConfigPanel.vue`

- [ ] **Step 1: Add UI controls**

In the "翻译行为" (Translation Behaviour) collapse section (`.collapse-grid`), add after existing fields:

```html
<NFormItem label="忽略空白差异">
  <NSwitch v-model:value="form.cacheWhitespaceDifferences" :default-value="false">
    <template #checked>缓存</template>
    <template #unchecked>忽略</template>
  </NSwitch>
</NFormItem>
<NFormItem label="对话忽略空白">
  <NSwitch v-model:value="form.ignoreWhitespaceInDialogue" />
</NFormItem>
<NFormItem label="最小对话字符数">
  <NInputNumber v-model:value="form.minDialogueChars" :min="1" :max="100" />
</NFormItem>
```

- [ ] **Step 2: Update form defaults**

In the `form` ref initialization, add the new fields with defaults:

```typescript
cacheWhitespaceDifferences: false,
ignoreWhitespaceInDialogue: true,
minDialogueChars: 4,
```

- [ ] **Step 3: Build and commit**

```bash
cd XUnityToolkit-Vue && npx vue-tsc --noEmit
git add XUnityToolkit-Vue/src/components/config/ConfigPanel.vue
git commit -m "feat: ConfigPanel 添加空白容错和对话忽略空白配置控件"
```

---

### Task 16: Add feature toggle UI in pre-translation section

**Files:**
- Modify: The pre-translation UI component (find the component that renders the pre-translation section in game detail — likely in `GameDetailView.vue` or a sub-component of it)

- [ ] **Step 1: Find the pre-translation UI component**

Search for `startPreTranslation` or `pre-translate` in Vue components to locate where the pre-translation UI lives.

- [ ] **Step 2: Add NSwitch toggle and NAlert warning**

Add before the pre-translate button:

```html
<div style="margin-bottom: 16px">
  <div style="display: flex; align-items: center; gap: 8px; margin-bottom: 8px">
    <NSwitch v-model:value="aiSettings.enablePreTranslationCache" @update:value="saveSettings" />
    <span>预翻译缓存优化</span>
    <NTag size="small" type="warning">实验性</NTag>
  </div>
  <NAlert v-if="aiSettings.enablePreTranslationCache" type="warning" style="margin-bottom: 12px">
    这是一个实验性功能。它会修改 XUnity.AutoTranslator 配置并生成正则翻译模式以提高预翻译缓存命中率。效果因游戏而异。如果启用后出现翻译问题，请关闭此功能并重新运行预翻译。
  </NAlert>
</div>
```

The exact integration depends on where `aiSettings` is accessible. If it's not available in this component, it may need to be loaded from the settings API.

- [ ] **Step 3: Build and commit**

```bash
cd XUnityToolkit-Vue && npx vue-tsc --noEmit
git add -A
git commit -m "feat: 添加预翻译缓存优化开关和实验性功能提示"
```

---

## Chunk 5: Documentation & Final Build

### Task 17: Update CLAUDE.md

**Files:**
- Modify: `CLAUDE.md`

- [ ] **Step 1: Add new API endpoints**

In the API Endpoints section, update the Pre-Translation line:

```
- **Pre-Translation:** `POST .../pre-translate`, `GET .../pre-translate/status`, `POST .../pre-translate/cancel`, `GET/PUT .../pre-translate/regex`
```

Add to the AI Translation line:

```
- **AI Translation:** `POST /api/translate` (**not ApiResult** — DLL calls directly), `GET /api/translate/stats`, `POST /api/translate/test`, `GET /api/translate/cache-stats`
```

- [ ] **Step 2: Add sync point**

In Sync Points, add:

```
- **Adding AiTranslationSettings fields:** (already exists — `EnablePreTranslationCache` follows same pattern)
- **PreTranslationCacheStats fields:** Sync 2 places: `Models/TranslationStats.cs`, `src/api/types.ts`; display in `AiTranslationView.vue`
```

- [ ] **Step 3: Commit**

```bash
git add CLAUDE.md
git commit -m "docs: 更新 CLAUDE.md 添加预翻译缓存优化的端点和同步点"
```

---

### Task 18: Full build verification

- [ ] **Step 1: Build everything**

```bash
dotnet build XUnityToolkit-WebUI/XUnityToolkit-WebUI.csproj
```

This builds both backend and frontend.

- [ ] **Step 2: Type-check frontend**

```bash
cd XUnityToolkit-Vue && npx vue-tsc --noEmit
```

- [ ] **Step 3: Fix any issues**

If build fails, fix issues and commit fixes.

- [ ] **Step 4: Final commit if needed**

Only if fixes were needed.
