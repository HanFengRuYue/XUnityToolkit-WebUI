# Script Tag Cleaning Implementation Plan

> **For agentic workers:** REQUIRED: Use superpowers:subagent-driven-development (if subagents available) or superpowers:executing-plans to implement this plan. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a per-game script tag cleaning module that strips game-specific instruction codes from pre-translation cache keys and LLM input, enabling XUnity cache hits and improving translation quality.

**Architecture:** Standalone `ScriptTagService` module with compiled regex cache, global versioned presets with auto-update, integrated at pre-translation and cache monitoring call sites. Frontend card in the asset extraction view.

**Tech Stack:** ASP.NET Core (.NET 10), Vue 3 + Naive UI + TypeScript, JSON file storage, `System.Text.RegularExpressions`

**Spec:** `docs/superpowers/specs/2026-03-17-script-tag-cleaning-design.md`

---

### Task 1: Data Models

**Files:**
- Create: `XUnityToolkit-WebUI/Models/ScriptTagAction.cs`
- Create: `XUnityToolkit-WebUI/Models/ScriptTagRule.cs`
- Create: `XUnityToolkit-WebUI/Models/ScriptTagConfig.cs`

- [ ] **Step 1: Create `ScriptTagAction` enum**

```csharp
// XUnityToolkit-WebUI/Models/ScriptTagAction.cs
namespace XUnityToolkit_WebUI.Models;

public enum ScriptTagAction { Extract, Exclude }
```

- [ ] **Step 2: Create `ScriptTagRule` model**

```csharp
// XUnityToolkit-WebUI/Models/ScriptTagRule.cs
namespace XUnityToolkit_WebUI.Models;

public sealed class ScriptTagRule
{
    public string Pattern { get; set; } = "";
    public ScriptTagAction Action { get; set; } = ScriptTagAction.Extract;
    public string? Description { get; set; }
    public bool IsBuiltin { get; set; }
}
```

- [ ] **Step 3: Create `ScriptTagConfig` model**

```csharp
// XUnityToolkit-WebUI/Models/ScriptTagConfig.cs
namespace XUnityToolkit_WebUI.Models;

public sealed class ScriptTagConfig
{
    public int PresetVersion { get; set; }
    public List<ScriptTagRule> Rules { get; set; } = [];
}
```

- [ ] **Step 4: Verify build**

Run: `dotnet build XUnityToolkit-WebUI/XUnityToolkit-WebUI.csproj`
Expected: Build succeeded

- [ ] **Step 5: Commit**

```bash
git add XUnityToolkit-WebUI/Models/ScriptTagAction.cs XUnityToolkit-WebUI/Models/ScriptTagRule.cs XUnityToolkit-WebUI/Models/ScriptTagConfig.cs
git commit -m "feat: 添加脚本指令清洗的数据模型 (ScriptTagAction/Rule/Config)"
```

---

### Task 2: Infrastructure (AppDataPaths + BundledAssetPaths + Preset File)

**Files:**
- Modify: `XUnityToolkit-WebUI/Infrastructure/AppDataPaths.cs`
- Modify: `XUnityToolkit-WebUI/Infrastructure/BundledAssetPaths.cs`
- Create: `bundled/script-tag-presets.json`
- Modify: `.gitignore`

- [ ] **Step 1: Add `ScriptTagsDirectory` and `ScriptTagFile` to `AppDataPaths.cs`**

After line 28 (`DoNotTranslateDirectory`), add:
```csharp
public string ScriptTagsDirectory => Path.Combine(_root, "script-tags");
```

After line 50 (`PreTranslationRegexFile`), add:
```csharp
public string ScriptTagFile(string gameId) =>
    Path.Combine(ScriptTagsDirectory, $"{gameId}.json");
```

In `EnsureDirectoriesExist()`, after `Directory.CreateDirectory(DoNotTranslateDirectory);` (line 90), add:
```csharp
Directory.CreateDirectory(ScriptTagsDirectory);
```

- [ ] **Step 2: Add `ScriptTagPresetsFile` to `BundledAssetPaths.cs`**

After `FontsDirectory` (line 15), add:
```csharp
public string ScriptTagPresetsFile => Path.Combine(_root, "script-tag-presets.json");
```

- [ ] **Step 3: Create the preset file**

Create `bundled/script-tag-presets.json`:
```json
{
  "version": 1,
  "rules": [
    { "pattern": "^(?:stk|tk|ts|FTK),[^,]*,(.+)$", "action": "Extract", "description": "对话前缀 (stk/tk/ts/FTK,N,文本)" },
    { "pattern": "^%%,[^,]*,(.*),#\\w+,?$", "action": "Extract", "description": "选项按钮 (%%,N,文本,#标签)" },
    { "pattern": "^\\$\\w+", "action": "Exclude", "description": "系统指令 ($命令)" },
    { "pattern": "^#\\w+$", "action": "Exclude", "description": "纯标签 (#锚点)" },
    { "pattern": "^Voice_", "action": "Exclude", "description": "语音触发 (Voice_*)" },
    { "pattern": "^@", "action": "Exclude", "description": "事件标识 (@事件)" },
    { "pattern": "^n\\d+,#PL,", "action": "Exclude", "description": "编号菜单 (n1,#PL,...)" },
    { "pattern": "^as\\d+,", "action": "Exclude", "description": "问候数据 (as3,...)" }
  ]
}
```

- [ ] **Step 4: Add `.gitignore` negation for preset file**

After line 29 (`!bundled/fonts/`), add:
```
!bundled/script-tag-presets.json
```

- [ ] **Step 5: Verify build**

Run: `dotnet build XUnityToolkit-WebUI/XUnityToolkit-WebUI.csproj`
Expected: Build succeeded

- [ ] **Step 6: Commit**

```bash
git add XUnityToolkit-WebUI/Infrastructure/AppDataPaths.cs XUnityToolkit-WebUI/Infrastructure/BundledAssetPaths.cs .gitignore
git add -f bundled/script-tag-presets.json
git commit -m "feat: 添加脚本指令清洗的基础设施 (路径、预设文件、gitignore)"
```

---

### Task 3: ScriptTagService

**Files:**
- Create: `XUnityToolkit-WebUI/Services/ScriptTagService.cs`

This is the core module. Follow `DoNotTranslateService` patterns for file I/O, locking, caching.

- [ ] **Step 1: Create `ScriptTagService.cs`**

```csharp
using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using XUnityToolkit_WebUI.Infrastructure;
using XUnityToolkit_WebUI.Models;

namespace XUnityToolkit_WebUI.Services;

public sealed class ScriptTagService(
    AppDataPaths paths,
    BundledAssetPaths bundledPaths,
    ILogger<ScriptTagService> logger)
{
    private readonly SemaphoreSlim _lock = new(1, 1);
    private readonly ConcurrentDictionary<string, ScriptTagConfig> _cache = new();
    private readonly ConcurrentDictionary<string, CompiledRuleSet> _compiled = new();
    private ScriptTagPreset? _presetCache;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() }
    };

    private sealed class CompiledRuleSet
    {
        public required List<(Regex Regex, ScriptTagAction Action)> Rules { get; init; }
    }

    // ── Preset loading ──

    public ScriptTagPreset LoadPreset()
    {
        if (_presetCache is not null) return _presetCache;

        var file = bundledPaths.ScriptTagPresetsFile;
        if (!File.Exists(file))
        {
            _presetCache = new ScriptTagPreset { Version = 0, Rules = [] };
            return _presetCache;
        }

        var json = File.ReadAllText(file);
        _presetCache = JsonSerializer.Deserialize<ScriptTagPreset>(json, JsonOptions)
            ?? new ScriptTagPreset { Version = 0, Rules = [] };
        return _presetCache;
    }

    // ── Config CRUD ──

    public async Task<ScriptTagConfig> GetAsync(string gameId, CancellationToken ct = default)
    {
        if (_cache.TryGetValue(gameId, out var cached)) return cached;

        await _lock.WaitAsync(ct);
        try
        {
            if (_cache.TryGetValue(gameId, out cached)) return cached;

            var file = paths.ScriptTagFile(gameId);
            if (!File.Exists(file))
            {
                // Return preset defaults in-memory, do NOT write to disk
                var preset = LoadPreset();
                var defaults = new ScriptTagConfig
                {
                    PresetVersion = preset.Version,
                    Rules = preset.Rules.Select(r => new ScriptTagRule
                    {
                        Pattern = r.Pattern,
                        Action = r.Action,
                        Description = r.Description,
                        IsBuiltin = true
                    }).ToList()
                };
                _cache[gameId] = defaults;
                EnsureCompiled(gameId, defaults);
                return defaults;
            }

            var json = await File.ReadAllTextAsync(file, ct);
            var config = JsonSerializer.Deserialize<ScriptTagConfig>(json, JsonOptions)
                ?? new ScriptTagConfig();

            // Auto-update: if stored preset version is behind bundled version
            var currentPreset = LoadPreset();
            if (config.PresetVersion < currentPreset.Version)
            {
                config.Rules.RemoveAll(r => r.IsBuiltin);
                var builtinRules = currentPreset.Rules.Select(r => new ScriptTagRule
                {
                    Pattern = r.Pattern,
                    Action = r.Action,
                    Description = r.Description,
                    IsBuiltin = true
                }).ToList();
                config.Rules.InsertRange(0, builtinRules);
                config.PresetVersion = currentPreset.Version;

                // Write back updated config
                var updatedJson = JsonSerializer.Serialize(config, JsonOptions);
                var tmpPath = file + ".tmp";
                await File.WriteAllTextAsync(tmpPath, updatedJson, ct);
                File.Move(tmpPath, file, overwrite: true);
                logger.LogInformation("已自动更新游戏 {GameId} 的内置脚本标签规则至版本 {Version}",
                    gameId, currentPreset.Version);
            }

            _cache[gameId] = config;
            EnsureCompiled(gameId, config);
            return config;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task SaveAsync(string gameId, ScriptTagConfig config, CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            var file = paths.ScriptTagFile(gameId);
            var json = JsonSerializer.Serialize(config, JsonOptions);
            var tmpPath = file + ".tmp";
            await File.WriteAllTextAsync(tmpPath, json, ct);
            File.Move(tmpPath, file, overwrite: true);
            _cache[gameId] = config;
            EnsureCompiled(gameId, config);
            logger.LogInformation("已保存游戏 {GameId} 的脚本标签规则: {Count} 条", gameId, config.Rules.Count);
        }
        finally
        {
            _lock.Release();
        }
    }

    public void RemoveCache(string gameId)
    {
        _cache.TryRemove(gameId, out _);
        _compiled.TryRemove(gameId, out _);
    }

    // ── Core cleaning methods ──

    public string? CleanText(string gameId, string text)
    {
        if (!_compiled.TryGetValue(gameId, out var ruleSet) || ruleSet.Rules.Count == 0)
            return text;

        foreach (var (regex, action) in ruleSet.Rules)
        {
            var match = regex.Match(text);
            if (!match.Success) continue;

            return action switch
            {
                ScriptTagAction.Exclude => null,
                ScriptTagAction.Extract => match.Groups.Count > 1
                    ? match.Groups[1].Value
                    : text,
                _ => text
            };
        }

        return text; // No match → return unchanged
    }

    public async Task<List<string>> FilterAndCleanAsync(
        string gameId, List<ExtractedText> texts, CancellationToken ct = default)
    {
        await GetAsync(gameId, ct); // Ensure rules are loaded & compiled

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var result = new List<string>();

        foreach (var item in texts)
        {
            var cleaned = CleanText(gameId, item.Text);
            if (cleaned is not null && !string.IsNullOrWhiteSpace(cleaned) && seen.Add(cleaned))
                result.Add(cleaned);
        }

        return result;
    }

    public string NormalizeForCache(string gameId, string text)
    {
        var stripped = XUnityTranslationFormat.NormalizeForCache(text);
        return CleanText(gameId, stripped) ?? "";
    }

    // ── Internal ──

    private void EnsureCompiled(string gameId, ScriptTagConfig config)
    {
        var compiled = new List<(Regex, ScriptTagAction)>();
        foreach (var rule in config.Rules)
        {
            if (string.IsNullOrWhiteSpace(rule.Pattern)) continue;
            try
            {
                var regex = new Regex(rule.Pattern,
                    RegexOptions.Compiled,
                    TimeSpan.FromSeconds(1));
                compiled.Add((regex, rule.Action));
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "跳过无效的脚本标签正则: {Pattern}", rule.Pattern);
            }
        }

        _compiled[gameId] = new CompiledRuleSet { Rules = compiled };
    }
}

// Preset model (for bundled JSON deserialization)
public sealed class ScriptTagPreset
{
    public int Version { get; set; }
    public List<ScriptTagPresetRule> Rules { get; set; } = [];
}

public sealed class ScriptTagPresetRule
{
    public string Pattern { get; set; } = "";
    public ScriptTagAction Action { get; set; } = ScriptTagAction.Extract;
    public string? Description { get; set; }
}
```

- [ ] **Step 2: Register in DI**

In `XUnityToolkit-WebUI/Program.cs`, after line 141 (`AddSingleton<DoNotTranslateService>`), add:
```csharp
builder.Services.AddSingleton<ScriptTagService>();
```

- [ ] **Step 3: Verify build**

Run: `dotnet build XUnityToolkit-WebUI/XUnityToolkit-WebUI.csproj`
Expected: Build succeeded

- [ ] **Step 4: Commit**

```bash
git add XUnityToolkit-WebUI/Services/ScriptTagService.cs XUnityToolkit-WebUI/Program.cs
git commit -m "feat: 实现 ScriptTagService 核心模块 (清洗、预设加载、自动更新)"
```

---

### Task 4: API Endpoints

**Files:**
- Create: `XUnityToolkit-WebUI/Endpoints/ScriptTagEndpoints.cs`
- Modify: `XUnityToolkit-WebUI/Program.cs` (add `app.MapScriptTagEndpoints()`)
- Modify: `XUnityToolkit-WebUI/Endpoints/GameEndpoints.cs` (cleanup on delete)

- [ ] **Step 1: Create `ScriptTagEndpoints.cs`**

```csharp
using System.Text.RegularExpressions;
using XUnityToolkit_WebUI.Models;
using XUnityToolkit_WebUI.Services;

namespace XUnityToolkit_WebUI.Endpoints;

public static class ScriptTagEndpoints
{
    public static void MapScriptTagEndpoints(this WebApplication app)
    {
        // Get global presets
        app.MapGet("/api/script-tag-presets", (ScriptTagService service) =>
        {
            var preset = service.LoadPreset();
            return Results.Ok(ApiResult<ScriptTagPreset>.Ok(preset));
        });

        // Get per-game script tag config
        app.MapGet("/api/games/{id}/script-tags", async (
            string id,
            GameLibraryService library,
            ScriptTagService service,
            CancellationToken ct) =>
        {
            var game = await library.GetByIdAsync(id);
            if (game is null)
                return Results.NotFound(ApiResult.Fail("Game not found."));

            var config = await service.GetAsync(id, ct);
            return Results.Ok(ApiResult<ScriptTagConfig>.Ok(config));
        });

        // Save per-game script tag config
        app.MapPut("/api/games/{id}/script-tags", async (
            string id,
            ScriptTagConfig config,
            GameLibraryService library,
            ScriptTagService service,
            CancellationToken ct) =>
        {
            var game = await library.GetByIdAsync(id);
            if (game is null)
                return Results.NotFound(ApiResult.Fail("Game not found."));

            if (config.Rules.Count > 100)
                return Results.Ok(ApiResult.Fail("规则数量不能超过 100 条"));

            foreach (var rule in config.Rules)
            {
                if (string.IsNullOrWhiteSpace(rule.Pattern))
                    return Results.Ok(ApiResult.Fail("正则表达式不能为空"));

                try
                {
                    var regex = new Regex(rule.Pattern, RegexOptions.None, TimeSpan.FromSeconds(1));

                    if (rule.Action == ScriptTagAction.Extract
                        && regex.GetGroupNumbers().Length < 2)
                    {
                        return Results.Ok(ApiResult.Fail(
                            $"Extract 规则必须包含至少一个捕获组 (...): {rule.Pattern}"));
                    }
                }
                catch (RegexParseException)
                {
                    return Results.Ok(ApiResult.Fail($"无效的正则表达式: {rule.Pattern}"));
                }
            }

            await service.SaveAsync(id, config, ct);
            return Results.Ok(ApiResult<ScriptTagConfig>.Ok(config));
        });
    }
}
```

- [ ] **Step 2: Register endpoints in `Program.cs`**

After `app.MapAssetEndpoints();` (line 247), add:
```csharp
app.MapScriptTagEndpoints();
```

- [ ] **Step 3: Add cleanup in `GameEndpoints.cs` DELETE handler**

After the DNT cleanup block (line 468 `dntService.RemoveCache(id);`), add:
```csharp
// Clean up script tag rules
var scriptTagFile = appDataPaths.ScriptTagFile(id);
if (File.Exists(scriptTagFile))
    File.Delete(scriptTagFile);
scriptTagService.RemoveCache(id);
```

Also add `ScriptTagService scriptTagService` to the DELETE handler's parameter list.

- [ ] **Step 4: Verify build**

Run: `dotnet build XUnityToolkit-WebUI/XUnityToolkit-WebUI.csproj`
Expected: Build succeeded

- [ ] **Step 5: Commit**

```bash
git add XUnityToolkit-WebUI/Endpoints/ScriptTagEndpoints.cs XUnityToolkit-WebUI/Program.cs XUnityToolkit-WebUI/Endpoints/GameEndpoints.cs
git commit -m "feat: 添加脚本指令清洗 API 端点 (GET/PUT + 预设 + 游戏删除清理)"
```

---

### Task 5: Integrate with PreTranslationService

**Files:**
- Modify: `XUnityToolkit-WebUI/Services/PreTranslationService.cs`

- [ ] **Step 1: Add `ScriptTagService` dependency**

Add `ScriptTagService scriptTagService` to the constructor parameters (line 10-17).

- [ ] **Step 2: Modify `ExecutePreTranslationAsync` to use `FilterAndCleanAsync`**

Replace line 113:
```csharp
var textList = texts.Select(t => t.Text).ToList();
```

With:
```csharp
var textList = await scriptTagService.FilterAndCleanAsync(gameId, texts, ct);
logger.LogInformation("脚本标签清洗: {Original} 条提取文本 → {Cleaned} 条翻译文本, 游戏 {GameId}",
    texts.Count, textList.Count, gameId);
```

Update `status.TotalTexts` on line 53 — move it AFTER `FilterAndClean`, or update it after filtering:
After the `FilterAndCleanAsync` call, add:
```csharp
status.TotalTexts = textList.Count;
```

- [ ] **Step 3: Modify `WriteTranslationCacheAsync` to use `ScriptTagService.NormalizeForCache`**

The method needs `gameId` parameter. It already receives it (line 166 call passes `gameId`). Check the method signature at line 180 — it does NOT currently have `gameId`. Add it.

Change method signature from:
```csharp
private async Task WriteTranslationCacheAsync(
    string gamePath, string toLang, string gameId,
```
(Already has `gameId` — good.)

Replace lines 212-214:
```csharp
var originalKey = enableCacheOptimization
    ? XUnityTranslationFormat.NormalizeForCache(original)
    : original;
```

With:
```csharp
var originalKey = enableCacheOptimization
    ? scriptTagService.NormalizeForCache(gameId, original)
    : original;
```

- [ ] **Step 4: Verify build**

Run: `dotnet build XUnityToolkit-WebUI/XUnityToolkit-WebUI.csproj`
Expected: Build succeeded

- [ ] **Step 5: Commit**

```bash
git add XUnityToolkit-WebUI/Services/PreTranslationService.cs
git commit -m "feat: 预翻译流程集成脚本标签清洗 (翻译前过滤 + 缓存键归一化)"
```

---

### Task 6: Integrate with PreTranslationCacheMonitor

**Files:**
- Modify: `XUnityToolkit-WebUI/Services/PreTranslationCacheMonitor.cs`

- [ ] **Step 1: Add `ScriptTagService` dependency**

Add `ScriptTagService scriptTagService` to the constructor parameters (line 8-12).

- [ ] **Step 2: Update `LoadCache` (line 78)**

Replace:
```csharp
keys.Add(XUnityTranslationFormat.NormalizeForCache(decoded));
```

With:
```csharp
keys.Add(scriptTagService.NormalizeForCache(gameId, decoded));
```

- [ ] **Step 3: Update `RecordTexts` (line 111)**

Replace:
```csharp
var normalized = XUnityTranslationFormat.NormalizeForCache(text);
```

With:
```csharp
var normalized = scriptTagService.NormalizeForCache(gameId, text);
```

- [ ] **Step 4: Verify build**

Run: `dotnet build XUnityToolkit-WebUI/XUnityToolkit-WebUI.csproj`
Expected: Build succeeded

- [ ] **Step 5: Commit**

```bash
git add XUnityToolkit-WebUI/Services/PreTranslationCacheMonitor.cs
git commit -m "feat: 缓存命中监控集成脚本标签清洗 (LoadCache + RecordTexts)"
```

---

### Task 7: Frontend Types & API

**Files:**
- Modify: `XUnityToolkit-Vue/src/api/types.ts`
- Modify: `XUnityToolkit-Vue/src/api/games.ts`

- [ ] **Step 1: Add types to `types.ts`**

Add at the end of the file (or alongside other game-related types):
```typescript
// Script Tag Cleaning
export type ScriptTagAction = 'Extract' | 'Exclude'

export interface ScriptTagRule {
  pattern: string
  action: ScriptTagAction
  description?: string
  isBuiltin: boolean
}

export interface ScriptTagConfig {
  presetVersion: number
  rules: ScriptTagRule[]
}

export interface ScriptTagPreset {
  version: number
  rules: Omit<ScriptTagRule, 'isBuiltin'>[]
}
```

- [ ] **Step 2: Add API functions to `games.ts`**

Add alongside existing per-game API functions:
```typescript
export const getScriptTags = (gameId: string) =>
  api.get<ScriptTagConfig>(`/api/games/${gameId}/script-tags`)

export const saveScriptTags = (gameId: string, config: ScriptTagConfig) =>
  api.put<ScriptTagConfig>(`/api/games/${gameId}/script-tags`, config)

export const getScriptTagPresets = () =>
  api.get<ScriptTagPreset>('/api/script-tag-presets')
```

Make sure `ScriptTagConfig`, `ScriptTagPreset`, `ScriptTagRule` are imported from `@/api/types`.

- [ ] **Step 3: Verify frontend build**

Run: `cd XUnityToolkit-Vue && npx vue-tsc --noEmit`
Expected: No errors

- [ ] **Step 4: Commit**

```bash
git add XUnityToolkit-Vue/src/api/types.ts XUnityToolkit-Vue/src/api/games.ts
git commit -m "feat: 前端添加脚本指令清洗类型和 API 函数"
```

---

### Task 8: Frontend UI Card

**Files:**
- Modify: `XUnityToolkit-Vue/src/views/AssetExtractionView.vue`

Add a collapsible card between the "Pre-Translation Cache Toggle" section and the "Action Buttons" section (around line 345).

- [ ] **Step 1: Add imports and state**

In the `<script setup>` block, add imports:
```typescript
import { LockClosedOutline } from '@vicons/ionicons5'
import type { ScriptTagRule, ScriptTagConfig, ScriptTagAction } from '@/api/types'
```

Add to the `gamesApi` import: `getScriptTags, saveScriptTags, getScriptTagPresets`.

Add reactive state:
```typescript
const scriptTagRules = ref<ScriptTagRule[]>([])
const scriptTagPresetVersion = ref(0)
const scriptTagLoading = ref(false)
const scriptTagSaving = ref(false)
const scriptTagDirty = ref(false)

const actionOptions = [
  { label: 'Extract (提取文本)', value: 'Extract' as ScriptTagAction },
  { label: 'Exclude (排除)', value: 'Exclude' as ScriptTagAction },
]
```

- [ ] **Step 2: Add load/save/import functions**

```typescript
async function loadScriptTags() {
  scriptTagLoading.value = true
  try {
    const res = await gamesApi.getScriptTags(gameId)
    scriptTagRules.value = res.rules
    scriptTagPresetVersion.value = res.presetVersion
    scriptTagDirty.value = false
  } catch (e) {
    console.error('Failed to load script tags', e)
  } finally {
    scriptTagLoading.value = false
  }
}

async function saveScriptTags() {
  scriptTagSaving.value = true
  try {
    const config: ScriptTagConfig = {
      presetVersion: scriptTagPresetVersion.value,
      rules: scriptTagRules.value,
    }
    await gamesApi.saveScriptTags(gameId, config)
    scriptTagDirty.value = false
    message.success('脚本指令规则已保存')
  } catch (e: any) {
    message.error(e?.message || '保存失败')
  } finally {
    scriptTagSaving.value = false
  }
}

async function importPresetRules() {
  try {
    const preset = await gamesApi.getScriptTagPresets()
    // Remove existing built-in rules
    scriptTagRules.value = scriptTagRules.value.filter(r => !r.isBuiltin)
    // Insert preset rules at the beginning
    const builtinRules: ScriptTagRule[] = preset.rules.map(r => ({
      pattern: r.pattern,
      action: r.action,
      description: r.description,
      isBuiltin: true,
    }))
    scriptTagRules.value.unshift(...builtinRules)
    scriptTagPresetVersion.value = preset.version
    scriptTagDirty.value = true
    message.success(`已导入 ${builtinRules.length} 条内置规则`)
  } catch (e: any) {
    message.error(e?.message || '导入失败')
  }
}

function addCustomRule() {
  scriptTagRules.value.push({
    pattern: '',
    action: 'Exclude',
    description: '',
    isBuiltin: false,
  })
  scriptTagDirty.value = true
}

function removeRule(index: number) {
  scriptTagRules.value.splice(index, 1)
  scriptTagDirty.value = true
}
```

Call `loadScriptTags()` in `onMounted` after `loadGame()`.

- [ ] **Step 3: Add template card**

Insert between the cache toggle div and the action buttons div (around line 345). Use existing project CSS classes (`.section-card`, `.section-header`, `.section-title`, `.header-actions`):

```vue
<!-- Script Tag Cleaning Rules -->
<div v-if="enablePreTranslationCache" class="section-card" style="margin-bottom: 16px">
  <div class="section-header">
    <span class="section-title">脚本指令清洗规则</span>
    <div class="header-actions">
      <NButton size="small" @click="importPresetRules">导入内置规则</NButton>
      <NButton size="small" @click="addCustomRule">+ 添加规则</NButton>
      <NButton size="small" type="primary" :loading="scriptTagSaving" :disabled="!scriptTagDirty" @click="saveScriptTags">
        保存
      </NButton>
    </div>
  </div>

  <div v-if="scriptTagRules.length === 0" class="empty-hint">
    暂无规则。点击「导入内置规则」加载预设，或手动添加自定义规则。
  </div>

  <div v-for="(rule, index) in scriptTagRules" :key="index"
       style="display: flex; align-items: center; gap: 8px; margin-bottom: 8px">
    <NInput
      v-model:value="rule.pattern"
      placeholder="正则表达式"
      :disabled="rule.isBuiltin"
      style="flex: 3; font-family: monospace"
      @update:value="scriptTagDirty = true"
    />
    <NSelect
      v-model:value="rule.action"
      :options="actionOptions"
      :disabled="rule.isBuiltin"
      style="flex: 1; min-width: 140px"
      @update:value="scriptTagDirty = true"
    />
    <NInput
      v-model:value="rule.description"
      placeholder="说明"
      :disabled="rule.isBuiltin"
      style="flex: 1.5"
      @update:value="scriptTagDirty = true"
    />
    <NButton v-if="!rule.isBuiltin" size="small" quaternary @click="removeRule(index)">
      <template #icon><NIcon :size="16"><DeleteOutlined /></NIcon></template>
    </NButton>
    <NIcon v-else :size="16" style="opacity: 0.5; min-width: 28px; display: flex; justify-content: center">
      <LockClosedOutline />
    </NIcon>
  </div>

  <div v-if="scriptTagRules.length > 0" style="margin-top: 8px; font-size: 12px; opacity: 0.6">
    内置规则随应用更新自动刷新，自定义规则不受影响。需重新运行预翻译以生效。
  </div>
</div>
```

- [ ] **Step 4: Verify frontend build**

Run: `cd XUnityToolkit-Vue && npx vue-tsc --noEmit && npm run build`
Expected: No errors

- [ ] **Step 5: Commit**

```bash
git add XUnityToolkit-Vue/src/views/AssetExtractionView.vue
git commit -m "feat: 前端添加脚本指令清洗规则配置卡片"
```

---

### Task 9: CLAUDE.md Sync Points & Build Script

**Files:**
- Modify: `CLAUDE.md` (root)
- Modify: `build.ps1` (add preset file copy)

- [ ] **Step 1: Add sync points to root `CLAUDE.md`**

In the `## Development Notes > ### Sync Points` section, add:
```
- **ScriptTagRule/ScriptTagConfig fields:** Sync 2 places: `Models/ScriptTagRule.cs` + `Models/ScriptTagConfig.cs` ↔ `src/api/types.ts`
- **Per-game data cleanup (script tags):** `DELETE /api/games/{id}` in `GameEndpoints.cs` must delete `scriptTagFile` + call `scriptTagService.RemoveCache`
- **`NormalizeForCache` call sites:** 3 places must all use `ScriptTagService.NormalizeForCache(gameId, text)`: `WriteTranslationCacheAsync`, `LoadCache`, `RecordTexts`
- **Adding preset rules:** Update `bundled/script-tag-presets.json`, increment `version`
```

Add to the API Endpoints section:
```
- **Script Tags:** `GET /api/script-tag-presets`, `GET/PUT /api/games/{id}/script-tags`
```

- [ ] **Step 2: Add preset copy to `build.ps1`**

Find the bundled fonts copy section and add nearby:
```powershell
Copy-Item "bundled/script-tag-presets.json" -Destination "$publishDir/bundled/" -Force
```

- [ ] **Step 3: Verify full build**

Run: `dotnet build XUnityToolkit-WebUI/XUnityToolkit-WebUI.csproj && cd XUnityToolkit-Vue && npm run build`
Expected: Both succeed

- [ ] **Step 4: Commit**

```bash
git add CLAUDE.md build.ps1
git commit -m "docs: 更新 CLAUDE.md 同步点和 build.ps1 预设文件复制"
```

---

### Task 10: End-to-End Verification

- [ ] **Step 1: Start the backend**

Run: `dotnet run --project XUnityToolkit-WebUI/XUnityToolkit-WebUI.csproj`
Expected: Server starts on `http://127.0.0.1:51821`

- [ ] **Step 2: Test preset endpoint**

Run: `curl http://127.0.0.1:51821/api/script-tag-presets`
Expected: JSON response with `version: 1` and 8 rules

- [ ] **Step 3: Test per-game GET (no saved config)**

Run: `curl http://127.0.0.1:51821/api/games/{gameId}/script-tags` (use a real game ID from the library)
Expected: Returns config with preset rules (all `isBuiltin: true`), no file created on disk

- [ ] **Step 4: Test per-game PUT (save custom rules)**

```bash
curl -X PUT http://127.0.0.1:51821/api/games/{gameId}/script-tags \
  -H "Content-Type: application/json" \
  -d '{"presetVersion":1,"rules":[{"pattern":"^test$","action":"Exclude","isBuiltin":false}]}'
```
Expected: 200 OK, file created at `%AppData%/XUnityToolkit/script-tags/{gameId}.json`

- [ ] **Step 5: Test frontend UI**

Open `http://127.0.0.1:51821` in browser, navigate to a game's asset extraction page.
Expected: "脚本指令清洗规则" card appears when cache optimization is enabled. "导入内置规则" populates 8 rules. Save/add/delete work.

- [ ] **Step 6: Test pre-translation with rules**

Run pre-translation for a game with script tag rules configured. Check the generated `_PreTranslated.txt` file.
Expected: Cache keys no longer contain instruction prefixes (e.g., `お話しましょう=translation` instead of `tk,1,お話しましょう=tk,1,translation`). System commands and labels are excluded from the file entirely.
