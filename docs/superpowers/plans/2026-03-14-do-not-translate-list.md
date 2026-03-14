# Do-Not-Translate List Implementation Plan

> **For agentic workers:** REQUIRED: Use superpowers:subagent-driven-development (if subagents available) or superpowers:executing-plans to implement this plan. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a per-game do-not-translate list that uses placeholder substitution to guarantee protected words are never translated by LLM.

**Architecture:** New `DoNotTranslateService` (mirrors `GlossaryService` cache pattern) stores per-game JSON lists. `LlmTranslationService` replaces matched words with `{{DNT_x}}` placeholders before LLM calls and restores them after. Frontend adds a second tab in the glossary editor page.

**Tech Stack:** C# / ASP.NET Core Minimal API, Vue 3 + TypeScript + Naive UI

**Spec:** `docs/superpowers/specs/2026-03-14-do-not-translate-list-design.md`

---

## Chunk 1: Backend Foundation

### Task 1: Data Model + AppDataPaths

**Files:**
- Create: `XUnityToolkit-WebUI/Models/DoNotTranslateEntry.cs`
- Modify: `XUnityToolkit-WebUI/Infrastructure/AppDataPaths.cs`

- [ ] **Step 1: Create DoNotTranslateEntry model**

Create `XUnityToolkit-WebUI/Models/DoNotTranslateEntry.cs`:

```csharp
namespace XUnityToolkit_WebUI.Models;

public sealed class DoNotTranslateEntry
{
    public string Original { get; set; } = "";
    public bool CaseSensitive { get; set; } = true;
}
```

- [ ] **Step 2: Add DoNotTranslate paths to AppDataPaths**

In `XUnityToolkit-WebUI/Infrastructure/AppDataPaths.cs`:

After line 27 (`GeneratedFontsDirectory`), add:

```csharp
public string DoNotTranslateDirectory => Path.Combine(_root, "do-not-translate");
```

After `GlossaryFile` method (line 36), add:

```csharp
public string DoNotTranslateFile(string gameId) =>
    Path.Combine(DoNotTranslateDirectory, $"{gameId}.json");
```

In `EnsureDirectoriesExist()`, after `Directory.CreateDirectory(GeneratedFontsDirectory)` (line 75), add:

```csharp
Directory.CreateDirectory(DoNotTranslateDirectory);
```

- [ ] **Step 3: Verify build**

Run: `dotnet build XUnityToolkit-WebUI/XUnityToolkit-WebUI.csproj`
Expected: Build succeeded

- [ ] **Step 4: Commit**

```bash
git add XUnityToolkit-WebUI/Models/DoNotTranslateEntry.cs XUnityToolkit-WebUI/Infrastructure/AppDataPaths.cs
git commit -m "feat: 添加 DoNotTranslateEntry 模型和 AppDataPaths 路径"
```

---

### Task 2: DoNotTranslateService

**Files:**
- Create: `XUnityToolkit-WebUI/Services/DoNotTranslateService.cs`
- Modify: `XUnityToolkit-WebUI/Program.cs`

- [ ] **Step 1: Create DoNotTranslateService**

Create `XUnityToolkit-WebUI/Services/DoNotTranslateService.cs`, mirroring `GlossaryService` pattern:

```csharp
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
```

- [ ] **Step 2: Register in DI**

In `XUnityToolkit-WebUI/Program.cs`, after line 123 (`builder.Services.AddSingleton<GlossaryService>()`), add:

```csharp
builder.Services.AddSingleton<DoNotTranslateService>();
```

- [ ] **Step 3: Verify build**

Run: `dotnet build XUnityToolkit-WebUI/XUnityToolkit-WebUI.csproj`
Expected: Build succeeded

- [ ] **Step 4: Commit**

```bash
git add XUnityToolkit-WebUI/Services/DoNotTranslateService.cs XUnityToolkit-WebUI/Program.cs
git commit -m "feat: 添加 DoNotTranslateService 缓存服务"
```

---

### Task 3: API Endpoints

**Files:**
- Modify: `XUnityToolkit-WebUI/Endpoints/GameEndpoints.cs`

- [ ] **Step 1: Add GET/PUT endpoints**

In `GameEndpoints.cs`, after the `PUT /{id}/glossary` endpoint (after line 652), add:

```csharp
// Do-not-translate list management
group.MapGet("/{id}/do-not-translate", async (
    string id,
    GameLibraryService library,
    DoNotTranslateService dntService,
    CancellationToken ct) =>
{
    var game = await library.GetByIdAsync(id);
    if (game is null)
        return Results.NotFound(ApiResult.Fail("Game not found."));

    var entries = await dntService.GetAsync(id, ct);
    return Results.Ok(ApiResult<List<DoNotTranslateEntry>>.Ok(entries));
});

group.MapPut("/{id}/do-not-translate", async (
    string id,
    List<DoNotTranslateEntry> entries,
    GameLibraryService library,
    DoNotTranslateService dntService,
    CancellationToken ct) =>
{
    var game = await library.GetByIdAsync(id);
    if (game is null)
        return Results.NotFound(ApiResult.Fail("Game not found."));

    await dntService.SaveAsync(id, entries, ct);
    var saved = await dntService.GetAsync(id, ct);
    return Results.Ok(ApiResult<List<DoNotTranslateEntry>>.Ok(saved));
});
```

- [ ] **Step 2: Add cleanup to DELETE handler**

In the `DELETE /{id}` handler (line 435), add `DoNotTranslateService dntService` to the parameter list and add cleanup before `return Results.Ok`:

After the custom font directory cleanup (line 451), add:

```csharp
// Clean up do-not-translate list
var dntFile = appDataPaths.DoNotTranslateFile(id);
if (File.Exists(dntFile))
    File.Delete(dntFile);
dntService.RemoveCache(id);
```

- [ ] **Step 3: Add required using**

Ensure `using XUnityToolkit_WebUI.Models;` is present at top of file (already should be).

- [ ] **Step 4: Verify build**

Run: `dotnet build XUnityToolkit-WebUI/XUnityToolkit-WebUI.csproj`
Expected: Build succeeded

- [ ] **Step 5: Commit**

```bash
git add XUnityToolkit-WebUI/Endpoints/GameEndpoints.cs
git commit -m "feat: 添加禁翻表 API 端点 (GET/PUT/DELETE 清理)"
```

---

### Task 4: LlmTranslationService — Placeholder Replacement & Restoration

**Files:**
- Modify: `XUnityToolkit-WebUI/Services/LlmTranslationService.cs`

- [ ] **Step 1: Add DoNotTranslateService constructor dependency**

Change constructor (line 13-19) from:

```csharp
public sealed class LlmTranslationService(
    IHttpClientFactory httpClientFactory,
    AppSettingsService settingsService,
    GlossaryService glossaryService,
    GameLibraryService gameLibraryService,
    IHubContext<InstallProgressHub> hubContext,
    ILogger<LlmTranslationService> logger)
```

to:

```csharp
public sealed class LlmTranslationService(
    IHttpClientFactory httpClientFactory,
    AppSettingsService settingsService,
    GlossaryService glossaryService,
    DoNotTranslateService doNotTranslateService,
    GameLibraryService gameLibraryService,
    IHubContext<InstallProgressHub> hubContext,
    ILogger<LlmTranslationService> logger)
```

- [ ] **Step 2: Add replacement and restoration methods**

Before the `BuildSystemPrompt` method (before line 616), add:

```csharp
// ── Do-not-translate placeholder substitution ──

private static readonly Regex DntRestoreRegex = new(
    @"\{{1,2}\s*DNT_(\d+)\s*\}{1,2}",
    RegexOptions.Compiled);

private static (List<string> replacedTexts, Dictionary<string, string> mapping)
    ApplyDoNotTranslateReplacements(IList<string> texts, List<DoNotTranslateEntry> entries)
{
    // Sort by length descending to match longer patterns first
    var sorted = entries
        .Where(e => !string.IsNullOrWhiteSpace(e.Original))
        .OrderByDescending(e => e.Original.Length)
        .ToList();

    // mapping: "{{DNT_0}}" → actual matched text
    var mapping = new Dictionary<string, string>();
    // reverse lookup: matched text → placeholder (to reuse same placeholder for same text)
    var textToPlaceholder = new Dictionary<string, string>(StringComparer.Ordinal);
    int nextIndex = 0;

    var result = new List<string>(texts.Count);
    foreach (var text in texts)
    {
        var current = text;
        foreach (var entry in sorted)
        {
            if (entry.CaseSensitive)
            {
                // Find all occurrences and replace, preserving original casing
                int searchStart = 0;
                while (true)
                {
                    int idx = current.IndexOf(entry.Original, searchStart, StringComparison.Ordinal);
                    if (idx < 0) break;

                    var matched = current.Substring(idx, entry.Original.Length);
                    if (!textToPlaceholder.TryGetValue(matched, out var placeholder))
                    {
                        placeholder = $"{{{{DNT_{nextIndex}}}}}";
                        textToPlaceholder[matched] = placeholder;
                        mapping[placeholder] = matched;
                        nextIndex++;
                    }

                    current = current[..idx] + placeholder + current[(idx + entry.Original.Length)..];
                    searchStart = idx + placeholder.Length;
                }
            }
            else
            {
                // Case-insensitive: use Regex to capture actual matched text
                var pattern = Regex.Escape(entry.Original);
                current = Regex.Replace(current, pattern, m =>
                {
                    var matched = m.Value;
                    if (!textToPlaceholder.TryGetValue(matched, out var placeholder))
                    {
                        placeholder = $"{{{{DNT_{nextIndex}}}}}";
                        textToPlaceholder[matched] = placeholder;
                        mapping[placeholder] = matched;
                        nextIndex++;
                    }
                    return placeholder;
                }, RegexOptions.IgnoreCase);
            }
        }
        result.Add(current);
    }

    return (result, mapping);
}

private static List<string> RestoreDoNotTranslatePlaceholders(
    IList<string> translations, Dictionary<string, string> mapping)
{
    var result = new List<string>(translations.Count);
    foreach (var text in translations)
    {
        var restored = DntRestoreRegex.Replace(text, m =>
        {
            var index = m.Groups[1].Value;
            var fullKey = $"{{{{DNT_{index}}}}}";
            return mapping.TryGetValue(fullKey, out var original) ? original : m.Value;
        });
        result.Add(restored);
    }
    return result;
}
```

- [ ] **Step 3: Integrate into TranslateAsync**

In `TranslateAsync()` method:

**3a.** After loading glossary (after line 170), add DNT loading:

```csharp
// Load do-not-translate list (applies in both cloud and local mode)
List<DoNotTranslateEntry>? dntEntries = null;
if (!string.IsNullOrEmpty(gameId))
{
    dntEntries = await doNotTranslateService.GetAsync(gameId, ct);
    if (dntEntries.Count == 0) dntEntries = null;
}
```

**3b.** Before the `TranslateBatchAsync` call (before line 205), add placeholder replacement:

```csharp
// Apply do-not-translate placeholder substitution
Dictionary<string, string>? dntMapping = null;
IList<string> textsToTranslate = texts;
string? dntHint = null;
if (dntEntries is not null)
{
    var (replaced, mapping) = ApplyDoNotTranslateReplacements(texts, dntEntries);
    if (mapping.Count > 0)
    {
        textsToTranslate = replaced;
        dntMapping = mapping;
        dntHint = "\n\n文本中的 {{DNT_x}} 是不可翻译的占位符，请在翻译结果中原样保留，不要修改、翻译或删除。";
    }
}
```

**3c.** Change the `TranslateBatchAsync` call (line 205-207) to pass `textsToTranslate` and `dntHint`:

```csharp
var (batchResult, tokens, ms, endpointName) = await TranslateBatchAsync(
    textsToTranslate, from, to, ai, enabledEndpoints, glossary,
    gameDescription, memoryContext, dntHint, semaphore, ct);
```

**3d.** After creating the mutable `translations` list (after line 210), add restoration before glossary post-processing:

```csharp
// Restore do-not-translate placeholders (before glossary post-processing)
if (dntMapping is not null)
    translations = RestoreDoNotTranslatePlaceholders(translations, dntMapping);
```

- [ ] **Step 4: Thread dntHint through the call chain**

**4a.** Update `TranslateBatchAsync` signature (line 242-247) to add `string? dntHint`:

```csharp
private async Task<(IList<string> translations, long tokens, double ms, string endpointName)> TranslateBatchAsync(
    IList<string> texts, string from, string to,
    AiTranslationSettings ai, List<ApiEndpointConfig> endpoints,
    List<GlossaryEntry>? glossary, string? gameDescription,
    IList<TranslationMemoryEntry>? memoryContext,
    string? dntHint, SemaphoreSlim semaphore, CancellationToken ct)
```

**4b.** Update `CallProviderAsync` call inside `TranslateBatchAsync` (line 282-283) to pass `dntHint`:

```csharp
var (result, tokens) = await CallProviderAsync(
    chosenEndpoint, ai, texts, from, to, glossary, gameDescription, memoryContext, dntHint, ct);
```

**4c.** Update `CallProviderAsync` signature (line 365-369) to add `string? dntHint`:

```csharp
private async Task<(IList<string> translations, long tokens)> CallProviderAsync(
    ApiEndpointConfig endpoint, AiTranslationSettings ai,
    IList<string> texts, string from, string to,
    List<GlossaryEntry>? glossary, string? gameDescription,
    IList<TranslationMemoryEntry>? memoryContext, string? dntHint, CancellationToken ct)
```

**4d.** Update the provider dispatch switch in `CallProviderAsync` (lines 371-391) to pass `dntHint` to every arm:

```csharp
return endpoint.Provider switch
{
    LlmProvider.OpenAI => await CallOpenAiCompatAsync(endpoint, ai, texts, from, to, glossary,
        gameDescription, memoryContext, dntHint, GetDefaultBaseUrl(endpoint), ct),
    LlmProvider.DeepSeek => await CallOpenAiCompatAsync(endpoint, ai, texts, from, to, glossary,
        gameDescription, memoryContext, dntHint, GetDefaultBaseUrl(endpoint), ct),
    LlmProvider.Qwen => await CallOpenAiCompatAsync(endpoint, ai, texts, from, to, glossary,
        gameDescription, memoryContext, dntHint, GetDefaultBaseUrl(endpoint), ct),
    LlmProvider.GLM => await CallOpenAiCompatAsync(endpoint, ai, texts, from, to, glossary,
        gameDescription, memoryContext, dntHint, GetDefaultBaseUrl(endpoint), ct),
    LlmProvider.Kimi => await CallOpenAiCompatAsync(endpoint, ai, texts, from, to, glossary,
        gameDescription, memoryContext, dntHint, GetDefaultBaseUrl(endpoint), ct),
    LlmProvider.Custom => await CallOpenAiCompatAsync(endpoint, ai, texts, from, to, glossary,
        gameDescription, memoryContext, dntHint, endpoint.ApiBaseUrl, ct),
    LlmProvider.Claude => await CallClaudeAsync(endpoint, ai, texts, from, to, glossary,
        gameDescription, memoryContext, dntHint, ct),
    LlmProvider.Gemini => await CallGeminiAsync(endpoint, ai, texts, from, to, glossary,
        gameDescription, memoryContext, dntHint, ct),
    _ => throw new NotSupportedException($"未支持的 LLM 提供商: {endpoint.Provider}")
};
```

**4e.** Update the 3 provider method signatures and their `BuildSystemPrompt` calls:

`CallOpenAiCompatAsync` (line 490) — add `string? dntHint` before `string baseUrl`:

```csharp
private async Task<(IList<string>, long)> CallOpenAiCompatAsync(
    ApiEndpointConfig ep, AiTranslationSettings ai,
    IList<string> texts, string from, string to,
    List<GlossaryEntry>? glossary, string? gameDescription,
    IList<TranslationMemoryEntry>? memoryContext, string? dntHint, string baseUrl, CancellationToken ct)
{
    var systemPrompt = BuildSystemPrompt(ai.SystemPrompt, from, to, glossary, gameDescription, memoryContext, dntHint);
    // ... rest unchanged
```

`CallClaudeAsync` (line 547) — add `string? dntHint` before `CancellationToken ct`:

```csharp
private async Task<(IList<string>, long)> CallClaudeAsync(
    ApiEndpointConfig ep, AiTranslationSettings ai,
    IList<string> texts, string from, string to,
    List<GlossaryEntry>? glossary, string? gameDescription,
    IList<TranslationMemoryEntry>? memoryContext, string? dntHint, CancellationToken ct)
{
    var systemPrompt = BuildSystemPrompt(ai.SystemPrompt, from, to, glossary, gameDescription, memoryContext, dntHint);
    // ... rest unchanged
```

`CallGeminiAsync` (line 602) — add `string? dntHint` before `CancellationToken ct`:

```csharp
private async Task<(IList<string>, long)> CallGeminiAsync(
    ApiEndpointConfig ep, AiTranslationSettings ai,
    IList<string> texts, string from, string to,
    List<GlossaryEntry>? glossary, string? gameDescription,
    IList<TranslationMemoryEntry>? memoryContext, string? dntHint, CancellationToken ct)
{
    var systemPrompt = BuildSystemPrompt(ai.SystemPrompt, from, to, glossary, gameDescription, memoryContext, dntHint);
    // ... rest unchanged
```

**4f.** Update `BuildSystemPrompt` signature (line 616-618) to add `string? dntHint`:

```csharp
private static string BuildSystemPrompt(string template, string from, string to,
    List<GlossaryEntry>? glossary, string? gameDescription = null,
    IList<TranslationMemoryEntry>? memoryContext = null, string? dntHint = null)
```

**4g.** In `BuildSystemPrompt`, after the memory context section (after line 653), add:

```csharp
if (!string.IsNullOrEmpty(dntHint))
    sb.Append(dntHint);
```

**4h.** Update `TestTranslateAsync` (line 809) — the `CallProviderAsync` call needs `null` for `dntHint`:

```csharp
var (translations, _) = await CallProviderAsync(ep, ai, testTexts, "en", "zh", null, null, null, null, ct);
```

- [ ] **Step 5: Verify build**

Run: `dotnet build XUnityToolkit-WebUI/XUnityToolkit-WebUI.csproj`
Expected: Build succeeded

- [ ] **Step 6: Commit**

```bash
git add XUnityToolkit-WebUI/Services/LlmTranslationService.cs
git commit -m "feat: 实现禁翻表占位符替换与还原逻辑"
```

---

## Chunk 2: Frontend

### Task 5: Frontend Types + API Client

**Files:**
- Modify: `XUnityToolkit-Vue/src/api/types.ts`
- Modify: `XUnityToolkit-Vue/src/api/games.ts`

- [ ] **Step 1: Add DoNotTranslateEntry type**

In `src/api/types.ts`, after the `GlossaryEntry` interface (around line 293), add:

```typescript
export interface DoNotTranslateEntry {
  original: string
  caseSensitive: boolean
}
```

- [ ] **Step 2: Add API methods**

In `src/api/games.ts`, after the `saveGlossary` method (line 59), add:

```typescript
getDoNotTranslate: (id: string) => api.get<DoNotTranslateEntry[]>(`/api/games/${id}/do-not-translate`),
saveDoNotTranslate: (id: string, entries: DoNotTranslateEntry[]) =>
  api.put<DoNotTranslateEntry[]>(`/api/games/${id}/do-not-translate`, entries),
```

Add `DoNotTranslateEntry` to the import from `./types` (line 2).

- [ ] **Step 3: Verify type-check**

Run: `cd XUnityToolkit-Vue && npx vue-tsc --noEmit`
Expected: No errors

- [ ] **Step 4: Commit**

```bash
git add XUnityToolkit-Vue/src/api/types.ts XUnityToolkit-Vue/src/api/games.ts
git commit -m "feat: 添加禁翻表前端类型定义和 API 客户端"
```

---

### Task 6: GlossaryEditorView — Add Tabs + Do-Not-Translate Tab

**Files:**
- Modify: `XUnityToolkit-Vue/src/views/GlossaryEditorView.vue`

This is the largest task. Transform the page into a tabbed layout with NTabs.

- [ ] **Step 1: Add NTabs and NTabPane imports**

In the imports from `naive-ui` (line 4-14), add `NTabs` and `NTabPane`:

```typescript
import {
  NButton,
  NIcon,
  NInput,
  NDataTable,
  NSwitch,
  NTag,
  NSpin,
  NEmpty,
  NTabs,
  NTabPane,
  useMessage,
} from 'naive-ui'
```

Add `DoNotTranslateEntry` to the types import (line 27):

```typescript
import type { Game, GlossaryEntry, DoNotTranslateEntry } from '@/api/types'
```

- [ ] **Step 2: Add Do-Not-Translate state and logic**

After the glossary editor state declarations (after `const searchKeyword = ref('')` around line 55), add:

```typescript
// ── Do-Not-Translate state ──
interface DntRow extends DoNotTranslateEntry {
  _id: number
}

let dntNextId = 1
const dntEntries = ref<DntRow[]>([])
const dntNewOriginal = ref('')
const dntNewCaseSensitive = ref(true)
const dntSearchKeyword = ref('')
const dntManualSaving = ref(false)
const dntImportFileInput = ref<HTMLInputElement | null>(null)

function toDntRows(items: DoNotTranslateEntry[]): DntRow[] {
  return items.map(e => ({ ...e, _id: dntNextId++ }))
}

function getValidDntEntries(): DoNotTranslateEntry[] {
  return dntEntries.value
    .filter(e => e.original.trim())
    .map(({ original, caseSensitive }) => ({ original, caseSensitive }))
}

const { saving: dntAutoSaving, enable: enableDntAutoSave, disable: disableDntAutoSave } = useAutoSave(
  () => dntEntries.value,
  async () => {
    try {
      const valid = getValidDntEntries()
      await gamesApi.saveDoNotTranslate(gameId, valid)
    } catch {
      message.error('自动保存禁翻表失败')
    }
  },
  { debounceMs: 2000, deep: true },
)

const filteredDntEntries = computed(() => {
  const kw = dntSearchKeyword.value.toLowerCase()
  if (!kw) return dntEntries.value
  return dntEntries.value.filter(e => e.original.toLowerCase().includes(kw))
})

const dntTableColumns = computed<DataTableColumns<DntRow>>(() => [
  {
    title: '原文',
    key: 'original',
    resizable: true,
    minWidth: 200,
    render(row) {
      return h(NInput, {
        value: row.original,
        size: 'small',
        type: 'text',
        placeholder: '不翻译的文本',
        'onUpdate:value': (v: string) => { row.original = v },
      })
    },
  },
  {
    title: '大小写敏感',
    key: 'caseSensitive',
    width: 100,
    align: 'center',
    render(row) {
      return h(NSwitch, {
        value: row.caseSensitive,
        size: 'small',
        'onUpdate:value': (v: boolean) => { row.caseSensitive = v },
      })
    },
  },
  {
    title: '',
    key: 'actions',
    width: 50,
    render(row) {
      return h(NButton, {
        size: 'tiny',
        quaternary: true,
        type: 'error',
        onClick: () => {
          const idx = dntEntries.value.findIndex(e => e._id === row._id)
          if (idx >= 0) dntEntries.value.splice(idx, 1)
        },
      }, {
        icon: () => h(NIcon, { size: 16 }, () => h(DeleteOutlined)),
      })
    },
  },
])
```

- [ ] **Step 3: Add Do-Not-Translate action functions**

After the glossary action functions (after `handleExport`, around line 308), add:

```typescript
// ── Do-Not-Translate Actions ──

function handleAddDntEntry() {
  if (!dntNewOriginal.value.trim()) {
    message.warning('请输入禁翻文本')
    return
  }
  if (dntEntries.value.some(e => e.original === dntNewOriginal.value)) {
    message.warning('该文本已存在')
    return
  }
  dntEntries.value.unshift({
    _id: dntNextId++,
    original: dntNewOriginal.value,
    caseSensitive: dntNewCaseSensitive.value,
  })
  dntNewOriginal.value = ''
  dntNewCaseSensitive.value = true
}

async function handleDntManualSave() {
  dntManualSaving.value = true
  disableDntAutoSave()
  try {
    const valid = getValidDntEntries()
    await gamesApi.saveDoNotTranslate(gameId, valid)
    dntEntries.value = toDntRows(valid)
    await nextTick()
    message.success('保存成功')
  } catch (e) {
    message.error(e instanceof Error ? e.message : '保存失败')
  } finally {
    dntManualSaving.value = false
    enableDntAutoSave()
  }
}

function handleDntImportClick() {
  dntImportFileInput.value?.click()
}

async function handleDntImportFile(e: Event) {
  const file = (e.target as HTMLInputElement).files?.[0]
  if (!file) return

  try {
    const text = await file.text()
    let imported: DoNotTranslateEntry[]
    try {
      imported = JSON.parse(text)
    } catch {
      message.error('文件格式错误：不是有效的 JSON')
      return
    }

    if (!Array.isArray(imported)) {
      message.error('文件格式错误：应为 JSON 数组')
      return
    }

    const valid: DoNotTranslateEntry[] = []
    for (const item of imported) {
      if (typeof item.original === 'string' && item.original.trim()) {
        valid.push({
          original: item.original,
          caseSensitive: typeof item.caseSensitive === 'boolean' ? item.caseSensitive : true,
        })
      }
    }

    const existingOriginals = new Set(dntEntries.value.map(e => e.original))
    let added = 0
    for (const entry of valid) {
      if (!existingOriginals.has(entry.original)) {
        dntEntries.value.push({ ...entry, _id: dntNextId++ })
        existingOriginals.add(entry.original)
        added++
      }
    }
    message.success(`导入完成: 新增 ${added} 条，跳过 ${valid.length - added} 条重复`)
  } catch (e) {
    message.error(e instanceof Error ? e.message : '导入失败')
  } finally {
    if (dntImportFileInput.value) dntImportFileInput.value.value = ''
  }
}

function handleDntExport() {
  const data = getValidDntEntries()
  if (data.length === 0) {
    message.warning('禁翻表为空，无法导出')
    return
  }
  const json = JSON.stringify(data, null, 2)
  const blob = new Blob([json], { type: 'application/json' })
  const a = document.createElement('a')
  a.href = URL.createObjectURL(blob)
  const gameName = game.value?.name?.replace(/[\\/:*?"<>|]/g, '_') ?? 'dnt'
  a.download = `${gameName}_禁翻表.json`
  a.click()
  setTimeout(() => URL.revokeObjectURL(a.href), 1000)
}
```

- [ ] **Step 4: Update onMounted to load DNT data**

Update the `onMounted` (line 178-194) to also load DNT entries:

```typescript
onMounted(async () => {
  disableAutoSave()
  disableDntAutoSave()
  try {
    const [gameData, glossaryData, dntData] = await Promise.all([
      gamesApi.get(gameId),
      gamesApi.getGlossary(gameId),
      gamesApi.getDoNotTranslate(gameId),
    ])
    game.value = gameData
    entries.value = toRows(glossaryData)
    dntEntries.value = toDntRows(dntData)
  } catch {
    message.error('加载失败')
  } finally {
    loading.value = false
  }
  await nextTick()
  enableAutoSave()
  enableDntAutoSave()
})
```

- [ ] **Step 5: Update template with NTabs**

Replace the entire `<template>` section with the following complete template:

```html
<template>
  <div v-if="loading" class="loading-state">
    <NSpin size="large" />
  </div>

  <div v-else-if="game" class="editor-page">
    <!-- Header -->
    <div class="page-header" style="animation-delay: 0s">
      <button class="back-button" @click="router.push(`/games/${gameId}`)">
        <NIcon :size="20"><ArrowBackOutlined /></NIcon>
        <span>{{ game.name }}</span>
      </button>
    </div>

    <h1 class="page-title" style="animation-delay: 0.05s">
      <span class="page-title-icon">
        <NIcon :size="24"><MenuBookOutlined /></NIcon>
      </span>
      AI 翻译词库
      <span v-if="autoSaving || dntAutoSaving" class="auto-save-badge">保存中...</span>
    </h1>

    <!-- Tabbed content -->
    <NTabs type="segment" animated class="editor-tabs" style="animation-delay: 0.1s">
      <!-- ═══ Tab 1: Glossary ═══ -->
      <NTabPane name="glossary" tab="术语表">
        <div class="section-card">
          <div class="section-header">
            <h2 class="section-title">
              <span class="section-icon">
                <NIcon :size="16"><MenuBookOutlined /></NIcon>
              </span>
              术语管理
              <NTag size="small" :bordered="false" style="margin-left: 8px">
                {{ entries.length }} 条
              </NTag>
            </h2>
            <div class="header-btn-group">
              <NButton size="small" @click="handleImportClick">
                <template #icon><NIcon :size="16"><FileUploadOutlined /></NIcon></template>
                导入
              </NButton>
              <NButton size="small" :disabled="entries.length === 0" @click="handleExport">
                <template #icon><NIcon :size="16"><FileDownloadOutlined /></NIcon></template>
                导出
              </NButton>
              <NButton size="small" type="primary" :loading="manualSaving" @click="handleManualSave">
                <template #icon><NIcon :size="16"><SaveOutlined /></NIcon></template>
                保存
              </NButton>
            </div>
          </div>

          <div class="add-entry-row">
            <NInput v-model:value="newOriginal" placeholder="原文" size="small" style="flex: 1" @keyup.enter="handleAddEntry" />
            <NInput v-model:value="newTranslation" placeholder="译文" size="small" style="flex: 1" @keyup.enter="handleAddEntry" />
            <NButton size="small" type="primary" :disabled="!newOriginal.trim()" @click="handleAddEntry">
              <template #icon><NIcon :size="16"><AddOutlined /></NIcon></template>
              添加
            </NButton>
          </div>

          <NInput v-model:value="searchKeyword" placeholder="搜索原文、译文或描述..." clearable size="small" style="margin-bottom: 12px">
            <template #prefix><NIcon :size="16"><SearchOutlined /></NIcon></template>
          </NInput>

          <div v-if="filteredEntries.length > 0" class="table-container">
            <NDataTable :columns="tableColumns" :data="filteredEntries" :max-height="560" :item-size="40" :row-key="(row: GlossaryRow) => row._id" virtual-scroll size="small" striped />
          </div>
          <NEmpty v-else-if="entries.length > 0" description="没有匹配的术语" style="padding: 40px 0" />
          <NEmpty v-else description="暂无术语条目，点击添加或导入" style="padding: 40px 0" />
        </div>
      </NTabPane>

      <!-- ═══ Tab 2: Do-Not-Translate ═══ -->
      <NTabPane name="do-not-translate" tab="禁翻表">
        <div class="section-card">
          <div class="section-header">
            <h2 class="section-title">
              <span class="section-icon">
                <NIcon :size="16"><MenuBookOutlined /></NIcon>
              </span>
              禁翻管理
              <NTag size="small" :bordered="false" style="margin-left: 8px">
                {{ dntEntries.length }} 条
              </NTag>
            </h2>
            <div class="header-btn-group">
              <NButton size="small" @click="handleDntImportClick">
                <template #icon><NIcon :size="16"><FileUploadOutlined /></NIcon></template>
                导入
              </NButton>
              <NButton size="small" :disabled="dntEntries.length === 0" @click="handleDntExport">
                <template #icon><NIcon :size="16"><FileDownloadOutlined /></NIcon></template>
                导出
              </NButton>
              <NButton size="small" type="primary" :loading="dntManualSaving" @click="handleDntManualSave">
                <template #icon><NIcon :size="16"><SaveOutlined /></NIcon></template>
                保存
              </NButton>
            </div>
          </div>

          <div class="add-entry-row">
            <NInput v-model:value="dntNewOriginal" placeholder="不翻译的文本" size="small" style="flex: 1" @keyup.enter="handleAddDntEntry" />
            <div class="case-sensitive-toggle">
              <span class="toggle-label">大小写敏感</span>
              <NSwitch v-model:value="dntNewCaseSensitive" size="small" />
            </div>
            <NButton size="small" type="primary" :disabled="!dntNewOriginal.trim()" @click="handleAddDntEntry">
              <template #icon><NIcon :size="16"><AddOutlined /></NIcon></template>
              添加
            </NButton>
          </div>

          <NInput v-model:value="dntSearchKeyword" placeholder="搜索禁翻文本..." clearable size="small" style="margin-bottom: 12px">
            <template #prefix><NIcon :size="16"><SearchOutlined /></NIcon></template>
          </NInput>

          <div v-if="filteredDntEntries.length > 0" class="table-container">
            <NDataTable :columns="dntTableColumns" :data="filteredDntEntries" :max-height="560" :item-size="40" :row-key="(row: DntRow) => row._id" virtual-scroll size="small" striped />
          </div>
          <NEmpty v-else-if="dntEntries.length > 0" description="没有匹配的禁翻条目" style="padding: 40px 0" />
          <NEmpty v-else description="暂无禁翻条目，点击添加或导入" style="padding: 40px 0" />
        </div>
      </NTabPane>
    </NTabs>

    <!-- Hidden file inputs for import -->
    <input ref="importFileInput" type="file" accept=".json" style="display: none" @change="handleImportFile" />
    <input ref="dntImportFileInput" type="file" accept=".json" style="display: none" @change="handleDntImportFile" />
  </div>
</template>
```

Also add this CSS rule inside `<style scoped>` for the case-sensitive toggle in the add row:

```css
.case-sensitive-toggle {
  display: flex;
  align-items: center;
  gap: 6px;
  white-space: nowrap;
}
.toggle-label {
  font-size: 12px;
  color: var(--text-3);
}
```

And add animation for the tabs container:

```css
.editor-tabs {
  animation: slideUp 0.5s var(--ease-out) backwards;
}
```

- [ ] **Step 6: Verify type-check and build**

Run: `cd XUnityToolkit-Vue && npx vue-tsc --noEmit && npm run build`
Expected: No errors, build succeeded

- [ ] **Step 7: Commit**

```bash
git add XUnityToolkit-Vue/src/views/GlossaryEditorView.vue
git commit -m "feat: 术语编辑器添加禁翻表标签页"
```

---

## Chunk 3: Integration Verification + Documentation

### Task 7: End-to-End Verification

- [ ] **Step 1: Full backend build**

Run: `dotnet build XUnityToolkit-WebUI/XUnityToolkit-WebUI.csproj`
Expected: Build succeeded with no warnings related to our changes

- [ ] **Step 2: Full frontend check**

Run: `cd XUnityToolkit-Vue && npx vue-tsc --noEmit && npm run build`
Expected: No type errors, build succeeded

- [ ] **Step 3: Run application and verify**

Run: `dotnet run --project XUnityToolkit-WebUI/XUnityToolkit-WebUI.csproj`

Manual verification:
1. Open http://127.0.0.1:51821, navigate to a game's glossary editor
2. Verify two tabs appear: "术语表" and "禁翻表"
3. Switch to "禁翻表" tab, add an entry, verify auto-save works
4. Test import/export functionality
5. Verify the glossary tab still works unchanged

---

### Task 8: Update CLAUDE.md

**Files:**
- Modify: `CLAUDE.md`

- [ ] **Step 1: Add API endpoints documentation**

In the `## API Endpoints` section, after the Glossary line (`- **Glossary:** ...`), add:

```
- **Do-Not-Translate:** `GET/PUT /api/games/{id}/do-not-translate`
```

- [ ] **Step 2: Add sync point notes**

In the `### Sync Points` section, add:

```
- **Adding DoNotTranslateEntry fields:** Sync 2 places: `Models/DoNotTranslateEntry.cs`, `src/api/types.ts`
```

- [ ] **Step 3: Commit**

```bash
git add CLAUDE.md
git commit -m "docs: 更新 CLAUDE.md 添加禁翻表 API 和同步点文档"
```
