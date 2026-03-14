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

1. Load do-not-translate list via `DoNotTranslateService.GetAsync(gameId)` (alongside glossary load, no extra I/O)
2. Sort entries by `Original.Length` descending (longer matches first to prevent partial overlap issues, e.g., "Alice Walker" before "Alice")
3. For each text in the batch, scan for substring matches:
   - Case-sensitive entries: `string.Contains(original)`
   - Case-insensitive entries: `string.Contains(original, StringComparison.OrdinalIgnoreCase)`
4. Replace matches with `{{DNT_0}}`, `{{DNT_1}}`, etc.
5. Maintain a shared `Dictionary<string, string>` mapping for the entire batch (same word across texts uses same placeholder index)

### Phase 2: Prompt Hint

In `BuildSystemPrompt()`, when replacements were made, append:

```
文本中的 {{DNT_x}} 是不可翻译的占位符，请在翻译结果中原样保留，不要修改、翻译或删除。
```

### Phase 3: Post-Translation Restoration

After LLM returns translations:

1. For each translated text, replace `{{DNT_x}}` back to original text using the mapping dictionary
2. Fault tolerance: use regex `\{?\{?\s*DNT_(\d+)\s*\}?\}?` for lenient matching to handle LLM formatting variations (e.g., `{{ DNT_0 }}`, `{DNT_0}`)

## API Endpoints

Added to `GameEndpoints.cs`, adjacent to glossary endpoints:

| Method | Path | Request | Response |
|--------|------|---------|----------|
| GET | `/api/games/{id}/do-not-translate` | — | `ApiResult<List<DoNotTranslateEntry>>` |
| PUT | `/api/games/{id}/do-not-translate` | `List<DoNotTranslateEntry>` | `ApiResult<List<DoNotTranslateEntry>>` |

### Cleanup

`DELETE /api/games/{id}` handler must also delete `data/do-not-translate/{gameId}.json`.

## Backend Services

### DoNotTranslateService

Mirrors `GlossaryService` pattern:

- `ConcurrentDictionary<string, List<DoNotTranslateEntry>>` in-memory cache
- `GetAsync(gameId)`: fast path from cache, slow path loads from disk with semaphore
- `SaveAsync(gameId, entries)`: write to disk + update cache
- Registered as singleton in DI (`Program.cs`)

### LlmTranslationService Changes

New private methods:

- `ApplyDoNotTranslateReplacements(List<string> texts, List<DoNotTranslateEntry> entries)` → returns `(List<string> replacedTexts, Dictionary<string, string> mapping)`
- `RestoreDoNotTranslatePlaceholders(List<string> translations, Dictionary<string, string> mapping)` → returns `List<string>`

Called in `TranslateAsync()`:
- Replacement: after loading glossary/description/memory, before `TranslateBatchAsync()`
- Restoration: after `TranslateBatchAsync()` returns, before post-processing glossary replacements

### AppDataPaths

Add `DoNotTranslate` directory property: `Path.Combine(_root, "do-not-translate")`.

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
  - Import/Export: JSON format buttons

## Data Flow

```
Game texts: ["Alice「こんにちは」", "Bob arrived"]
    │
    ▼ Load do-not-translate list
    │ [Alice (case-sensitive), Bob (case-insensitive)]
    │
    ▼ Sort by length desc, replace substrings
    │ ["{{DNT_0}}「こんにちは」", "{{DNT_1}} arrived"]
    │ Mapping: {DNT_0: "Alice", DNT_1: "Bob"}
    │
    ▼ BuildSystemPrompt appends hint
    │ "...{{DNT_x}} 是不可翻译的占位符，请原样保留..."
    │
    ▼ Send to LLM
    │ LLM returns: ["{{DNT_0}}「你好」", "{{DNT_1}}到了"]
    │
    ▼ Restore placeholders (with fault-tolerant regex)
    │ ["Alice「你好」", "Bob到了"]
    │
    ▼ Return to game
```

## Files Modified

| Layer | File | Change |
|-------|------|--------|
| Model | `Models/DoNotTranslateEntry.cs` | New file |
| Service | `Services/DoNotTranslateService.cs` | New file — cache + persistence |
| Service | `Services/LlmTranslationService.cs` | Add replacement/restoration logic + prompt hint |
| Paths | `Infrastructure/AppDataPaths.cs` | Add DoNotTranslate directory |
| Endpoints | `Endpoints/GameEndpoints.cs` | GET/PUT endpoints + delete cleanup |
| DI | `Program.cs` | Register DoNotTranslateService |
| Types | `src/api/types.ts` | DoNotTranslateEntry interface |
| API | `src/api/games.ts` | get/save methods |
| View | `src/views/GlossaryEditorView.vue` | NTabs layout + do-not-translate tab |

## Edge Cases

- **Overlapping matches:** Longer entries matched first; once replaced, the placeholder won't match shorter entries
- **Empty original:** Reject entries with empty/whitespace-only `Original` on save (both frontend validation and backend filter)
- **Placeholder collision:** `{{DNT_x}}` format is unlikely in game text; if collision occurs, the text would be incorrectly restored — acceptable risk given the format rarity
- **LLM drops placeholder:** Fault-tolerant regex handles formatting variations; if placeholder is completely removed by LLM, the original text is lost in that translation — an inherent limitation documented for users
- **Local LLM mode:** Do-not-translate applies equally — placeholder substitution is model-agnostic
- **Pre-translation batch mode:** `PreTranslationService` also calls `LlmTranslationService.TranslateAsync()`, so do-not-translate automatically applies
