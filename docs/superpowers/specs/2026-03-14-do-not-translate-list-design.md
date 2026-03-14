# Do-Not-Translate List Feature Design

## Overview

Add a per-game do-not-translate list that guarantees certain words/text are never translated by the AI. Uses placeholder substitution before LLM calls plus prompt hints for double protection.

## Requirements

- Per-game independent lists (stored alongside glossary)
- Substring matching within game text
- Per-entry configurable case sensitivity (default: case-sensitive)
- 100% guarantee: protected words must never be translated
- User manually manages entries via UI

## Data Model

### DoNotTranslateEntry

```csharp
public sealed class DoNotTranslateEntry
{
    public string Original { get; set; } = "";
    public bool CaseSensitive { get; set; } = true;
}
```

### Storage

- Path: `{dataDir}/do-not-translate/{gameId}.json`
- Format: JSON array of `DoNotTranslateEntry`
- Pattern: identical to `GlossaryService` — `ConcurrentDictionary` cache + disk persistence

## Implementation Approach: Placeholder Substitution + Prompt Hint (Hybrid)

### Phase 1: Pre-Translation Replacement

In `LlmTranslationService.TranslateAsync()`, before sending texts to LLM:

1. Load do-not-translate list via `DoNotTranslateService.GetAsync(gameId)` (alongside glossary load, no extra I/O). **Critical:** `GetAsync` MUST use `ConcurrentDictionary` fast-path cache — `POST /api/translate` receives 100+ req/s, no disk I/O per request.
2. Sort entries by `Original.Length` descending (longer matches first to prevent partial overlap issues, e.g., "Alice Walker" before "Alice")
3. For each text in the batch, scan for substring matches:
   - Case-sensitive entries: use `string.Replace(original, placeholder)` (literal string matching)
   - Case-insensitive entries: use `Regex.Replace` with `Regex.Escape(original)` + `RegexOptions.IgnoreCase` to ensure Original is treated as literal text, not a regex pattern
4. Replace matches with `{{DNT_0}}`, `{{DNT_1}}`, etc.
5. Maintain a `Dictionary<string, string>` mapping (key: placeholder like `"{{DNT_0}}"`, value: matched text). **Each unique matched string gets its own placeholder index**, even if they come from the same entry. For example, entry `"alice"` (case-insensitive) matching `"Alice"` in text 1 and `"ALICE"` in text 2 → `DNT_0` → `"Alice"`, `DNT_1` → `"ALICE"`. This preserves original casing per occurrence.
6. **Case-insensitive casing preservation:** The mapping stores the **actual matched substring** from the source text (not the entry's `Original`), so restoration preserves the original casing as it appeared in the game text.

### Phase 2: Prompt Hint

In `BuildSystemPrompt()`, when replacements were made, append:

```
文本中的 {{DNT_x}} 是不可翻译的占位符，请在翻译结果中原样保留，不要修改、翻译或删除。
```

### Phase 3: Post-Translation Restoration

After LLM returns translations:

1. For each translated text, replace `{{DNT_x}}` back to original text using the mapping dictionary
2. Fault tolerance: use regex `\{{1,2}\s*DNT_(\d+)\s*\}{1,2}` for lenient matching to handle LLM formatting variations (e.g., `{{ DNT_0 }}`, `{DNT_0}`). Requires at least one brace on each side to avoid false positives on bare `DNT_0` text.
3. **Ordering:** Restoration MUST happen before glossary post-processing (`ApplyGlossaryPostProcess`) to ensure DNT-protected text is restored first. Note: if a glossary entry's `Original` matches text within a restored DNT term, the glossary post-process could modify it — this is a known limitation (glossary should not target DNT-protected terms).

## API Endpoints

Added to `GameEndpoints.cs`, adjacent to glossary endpoints:

| Method | Path | Request | Response |
|--------|------|---------|----------|
| GET | `/api/games/{id}/do-not-translate` | — | `ApiResult<List<DoNotTranslateEntry>>` |
| PUT | `/api/games/{id}/do-not-translate` | `List<DoNotTranslateEntry>` | `ApiResult<List<DoNotTranslateEntry>>` |

### Cleanup

`DELETE /api/games/{id}` handler must:
1. Delete file via `appDataPaths.DoNotTranslateFile(id)` with `File.Delete`
2. Evict cache entry via `DoNotTranslateService.RemoveCache(gameId)`

Note: The existing DELETE handler also lacks glossary file cleanup (pre-existing gap, out of scope for this feature).

## Backend Services

### DoNotTranslateService

Mirrors `GlossaryService` pattern:

- `ConcurrentDictionary<string, List<DoNotTranslateEntry>>` in-memory cache
- `GetAsync(gameId)`: fast path from `ConcurrentDictionary` (no lock, no disk I/O), slow path loads from disk with semaphore
- `SaveAsync(gameId, entries)`: write to disk + update cache
- `RemoveCache(gameId)`: evict cache entry (called on game deletion)
- Registered as singleton in DI (`Program.cs`)

### LlmTranslationService Changes

`DoNotTranslateService` added as constructor dependency (DI injection).

New private methods:

- `ApplyDoNotTranslateReplacements(List<string> texts, List<DoNotTranslateEntry> entries)` → returns `(List<string> replacedTexts, Dictionary<string, string> mapping)`
- `RestoreDoNotTranslatePlaceholders(List<string> translations, Dictionary<string, string> mapping)` → returns `List<string>`

Called in `TranslateAsync()`:
- Replacement: after loading glossary/description/memory, before `TranslateBatchAsync()`
- Restoration: after `TranslateBatchAsync()` returns, before glossary post-processing

The DNT hint string is threaded through the call chain: `TranslateAsync()` → `TranslateBatchAsync()` → `CallProviderAsync()` → `Call*Async()` → `BuildSystemPrompt()` via a new optional parameter `string? doNotTranslateHint = null`. When `mapping.Count > 0`, the hint string is passed; `BuildSystemPrompt` appends it after the glossary section.

### AppDataPaths

- Add `DoNotTranslateDirectory` property: `Path.Combine(_root, "do-not-translate")`
- Add `DoNotTranslateFile(string gameId)` method: `Path.Combine(DoNotTranslateDirectory, $"{gameId}.json")` (mirrors `GlossaryFile()` pattern)
- Add `Directory.CreateDirectory(DoNotTranslateDirectory)` to `EnsureDirectoriesExist()`

## Frontend

### Type Definition (`types.ts`)

```typescript
export interface DoNotTranslateEntry {
  original: string
  caseSensitive: boolean
}
```

### API Client (`games.ts`)

```typescript
getDoNotTranslate: (id: string) => api.get<DoNotTranslateEntry[]>(`/api/games/${id}/do-not-translate`)
saveDoNotTranslate: (id: string, entries: DoNotTranslateEntry[]) =>
  api.put<DoNotTranslateEntry[]>(`/api/games/${id}/do-not-translate`, entries)
```

### UI Changes (`GlossaryEditorView.vue`)

Transform the existing page into a tabbed layout using `NTabs`:

- **Tab 1: 术语表 (Glossary)** — existing glossary editor content, unchanged
- **Tab 2: 禁翻表 (Do-Not-Translate)** — new tab with:
  - Add row: `NInput` (original text) + `NSwitch` (case-sensitive, default on) + Add button
  - `NDataTable` with virtual scroll:
    - Column: Original text (editable `NInput`)
    - Column: Case-sensitive (`NSwitch`)
    - Column: Delete button
  - Search/filter bar
  - Auto-save: `useAutoSave` with 2s debounce, deep watch
  - Import/Export: JSON format (`DoNotTranslateEntry[]` array), same pattern as glossary

No `defineOptions` needed (game sub-page at depth 3, not KeepAlive-cached). Route `meta.depth` remains unchanged.

## Data Flow

```
Game texts: ["Alice「こんにちは」", "Bob arrived"]
    │
    ▼ Load do-not-translate list (from ConcurrentDictionary cache)
    │ [Alice (case-sensitive), Bob (case-insensitive)]
    │
    ▼ Sort by length desc, replace substrings
    │ ["{{DNT_0}}「こんにちは」", "{{DNT_1}} arrived"]
    │ Mapping: {DNT_0: "Alice", DNT_1: "Bob"}
    │  (stores actual matched text, not entry Original)
    │
    ▼ BuildSystemPrompt appends hint
    │ "...{{DNT_x}} 是不可翻译的占位符，请原样保留..."
    │
    ▼ Texts serialized as JSON array → sent to LLM
    │ ({{DNT_x}} preserved through JSON serialization)
    │
    ▼ LLM returns: ["{{DNT_0}}「你好」", "{{DNT_1}}到了"]
    │
    ▼ Restore placeholders (with fault-tolerant regex)
    │ ["Alice「你好」", "Bob到了"]
    │
    ▼ Glossary post-processing (after DNT restoration)
    │
    ▼ Return to game
```

## Files Modified

| Layer | File | Change |
|-------|------|--------|
| Model | `Models/DoNotTranslateEntry.cs` | New file |
| Service | `Services/DoNotTranslateService.cs` | New file — cache + persistence + RemoveCache |
| Service | `Services/LlmTranslationService.cs` | Add DoNotTranslateService DI + replacement/restoration logic + prompt hint |
| Paths | `Infrastructure/AppDataPaths.cs` | Add DoNotTranslateDirectory + DoNotTranslateFile() + EnsureDirectoriesExist |
| Endpoints | `Endpoints/GameEndpoints.cs` | GET/PUT endpoints + delete cleanup + cache eviction |
| DI | `Program.cs` | Register DoNotTranslateService singleton |
| Types | `src/api/types.ts` | DoNotTranslateEntry interface |
| API | `src/api/games.ts` | get/save methods |
| View | `src/views/GlossaryEditorView.vue` | NTabs layout + do-not-translate tab |
| Docs | `CLAUDE.md` | Add API endpoints, sync point notes |

## Edge Cases

- **Overlapping matches:** Longer entries matched first; once replaced, the placeholder won't match shorter entries
- **Empty original:** Reject entries with empty/whitespace-only `Original` on save (both frontend validation and backend filter)
- **Duplicate entries:** Deduplicate by `Original` (exact match) on save. If two entries have the same `Original` but differ in `CaseSensitive`, keep the first one. Frontend validates on add; backend filters on PUT.
- **Placeholder collision:** `{{DNT_x}}` format is unlikely in game text; if collision occurs, the text would be incorrectly restored — acceptable risk given the format rarity
- **LLM drops placeholder:** Fault-tolerant regex handles formatting variations; if placeholder is completely removed by LLM, the original text is lost in that translation — an inherent limitation documented for users
- **Local LLM mode:** Do-not-translate applies equally — placeholder substitution is model-agnostic
- **Pre-translation batch mode:** `PreTranslationService` also calls `LlmTranslationService.TranslateAsync()`, so do-not-translate automatically applies
- **Special characters in Original:** Entries are treated as literal strings. Case-sensitive uses `string.Replace`; case-insensitive uses `Regex.Replace` with `Regex.Escape(original)` to prevent regex injection
- **Glossary post-process interaction:** DNT restoration happens before glossary post-processing. If a glossary entry targets text within a restored DNT term, the glossary will modify it — users should not add glossary entries that conflict with DNT entries
- **Cache invalidation on game delete:** `RemoveCache(gameId)` called alongside file deletion to prevent stale cache entries
