# Script Tag Cleaning for Pre-Translation Cache

**Date:** 2026-03-17
**Status:** Draft
**Scope:** Backend service + API endpoints + frontend UI + global preset system

## Problem Statement

Asset extraction reads raw strings from Unity binary files, which include game-specific script instruction codes (e.g., `tk,1,text`, `stk,-1,text`, `%%,-1,buttonText,#BTN_A01`). These instruction codes are stripped by the game engine before text reaches UI components. XUnity.AutoTranslator hooks at the UI component level, so it sees only the clean text portion.

This creates two problems:
1. **Cache key mismatch**: Pre-translation cache keys contain instruction codes that XUnity never sees at runtime, resulting in 0% hit rate for affected entries.
2. **LLM translation quality degradation**: Instruction codes confuse the LLM, producing JSON arrays, dropped prefixes, corrupted commas, and semantic errors.

**Measured impact** (KODOKU game): 2897/5458 entries (53%) contain instruction codes. 50 entries returned JSON arrays. 184 entries had dropped prefixes. 38 entries had corrupted commas.

## Game Code Verification

Verified against the game's decompiled source (`NPC_SinarioSC.cs`, `NerSavePoint.cs`, `UI_TalkWindowCS.cs`):

**Text flow for `tk,N,text`:**
```
TextAsset line: "tk,1,お話しましょう"
  → aList = line.Split(',')          // ["tk", "1", "お話しましょう"]
  → Talk_Nomal(aList[2], ...)        // Only text portion passed
  → Go_TalkWindow("", Talk_Text, CrtNum)
  → Talk_Text.Replace("$$PLNAME", playerName)
  → UI_TalkWD_CS.TalkText(MyDataCS, Talk_Text, true)
  → StockText = value                // Only "お話しましょう"
  → UI_TextValue.text += char        // XUnity hooks here
```

**Text flow for `%%,-1,buttonText,#link`:**
```
TextAsset lines after "%sentaku":
  → array = line.Split(',')          // ["%%", "-1", "buttonText", "#BTN_A01"]
  → BTN_Values.Add(array[2])         // Only button text
  → BTN_Links.Add(array[3])          // Link stored separately
  → SentakuBTN_Open(BTN_Values, BTN_Links, ...)
  → UI_ButtonTextList[i].text = textValue[i]  // XUnity hooks here
```

**Confirmed**: XUnity sees ONLY the text portion. Instruction prefixes/suffixes are never part of displayed text.

## Solution: ScriptTagService Module

### Architecture

A standalone, modular service that cleanses game-specific script instruction codes from extracted text. The main flow only interacts through a simple interface.

```
ScriptTagService (standalone module)
├── Models:    ScriptTagRule, ScriptTagConfig
├── Storage:   {appData}/script-tags/{gameId}.json
├── Preset:    bundled/script-tag-presets.json (versioned, auto-update)
├── Cache:     ConcurrentDictionary<gameId, CompiledRuleSet>
├── Interface:
│   ├── CleanText(gameId, text) → string?
│   ├── FilterAndCleanAsync(gameId, texts) → (cleanTexts, mapping)
│   └── NormalizeForCache(gameId, text) → string
├── Config:    GetAsync / SaveAsync / RemoveCache
└── Endpoints: GET/PUT /api/games/{id}/script-tags
               GET /api/script-tag-presets
```

### Module Boundary

| Concern | Owner |
|---------|-------|
| Rich text tag stripping (`<color>`, `<b>`, etc.) | `XUnityTranslationFormat` (unchanged) |
| Game script instruction cleaning | `ScriptTagService` (new) |
| Pre-translation orchestration | `PreTranslationService` (calls interface only) |
| Cache hit monitoring | `PreTranslationCacheMonitor` (calls interface only) |
| Rule configuration UI | Frontend new card (independent component) |

### Data Model

```csharp
// Models/ScriptTagRule.cs
public sealed class ScriptTagRule
{
    public string Pattern { get; set; } = "";
    public string Action { get; set; } = "extract";  // "extract" | "exclude"
    public string? Description { get; set; }
    public bool IsBuiltin { get; set; }
}

// Models/ScriptTagConfig.cs
public sealed class ScriptTagConfig
{
    public int PresetVersion { get; set; }
    public List<ScriptTagRule> Rules { get; set; } = [];
}
```

### Storage

- Path: `{appData}/script-tags/{gameId}.json`
- `AppDataPaths` adds: `ScriptTagsDirectory`, `ScriptTagFile(gameId)`
- Format: JSON, same serialization options as `DoNotTranslateService`
- Atomic writes: write-to-temp + `File.Move(overwrite: true)`

### Compiled Rule Cache

```csharp
private sealed class CompiledRuleSet
{
    public required List<(Regex Regex, string Action)> Rules { get; init; }
}

private readonly ConcurrentDictionary<string, CompiledRuleSet> _compiled = new();
```

Regex compiled with `RegexOptions.Compiled` and 1-second timeout. Compiled on `SaveAsync` after validation. Lazily compiled on first `GetAsync` if not cached.

### Core Methods

**`CleanText(string gameId, string text) → string?`**
1. Get compiled rules for game (returns text as-is if no rules).
2. Iterate rules in order; first match wins (short-circuit).
3. `exclude` action → return `null`.
4. `extract` action → return `match.Groups[1].Value` (first capture group).
5. No match → return original text unchanged.

**`FilterAndCleanAsync(string gameId, List<ExtractedText> texts) → (List<string> cleanTexts, Dictionary<string, string> cleanToOriginal)`**
1. Load rules for game.
2. For each text: apply `CleanText`.
3. `null` results (excluded) are dropped.
4. Non-null results are collected with clean→original mapping.
5. Deduplicate by clean text (different originals may clean to the same text).

**`NormalizeForCache(string gameId, string text) → string`**
```csharp
var stripped = XUnityTranslationFormat.NormalizeForCache(text);  // Rich text first
return CleanText(gameId, stripped) ?? "";                         // Then script tags
```
Rich text stripping first (matches XUnity's runtime preprocessing), then script tag cleaning.

### Global Preset System

**Preset file**: `bundled/script-tag-presets.json`

```json
{
  "version": 1,
  "rules": [
    { "pattern": "^(?:stk|tk|ts|FTK),[^,]*,(.+)$", "action": "extract", "description": "Dialogue prefix (stk/tk/ts/FTK,N,text)" },
    { "pattern": "^%%,[^,]*,(.*),#\\w+,?$",          "action": "extract", "description": "Choice button (%%,N,text,#link)" },
    { "pattern": "^\\$\\w+",                          "action": "exclude", "description": "System command ($command)" },
    { "pattern": "^#\\w+$",                           "action": "exclude", "description": "Label (#anchor)" },
    { "pattern": "^Voice_",                            "action": "exclude", "description": "Voice trigger (Voice_*)" },
    { "pattern": "^@",                                 "action": "exclude", "description": "Event identifier (@event)" },
    { "pattern": "^n\\d+,#PL,",                        "action": "exclude", "description": "Numbered menu (n1,#PL,...)" },
    { "pattern": "^as\\d+,",                           "action": "exclude", "description": "Greeting data (as3,...)" }
  ]
}
```

As more games are adapted, rules are appended and `version` is incremented. Rules that don't match a particular game have no effect.

**Auto-update logic** (in `GetAsync`):
```
if (stored.PresetVersion < bundled.Version) {
    Remove all stored rules where IsBuiltin == true
    Insert new preset rules with IsBuiltin = true
    Preserve all rules where IsBuiltin == false
    Update PresetVersion, write back to file
}
```

### API Endpoints

```
GET  /api/script-tag-presets          → { version: int, rules: ScriptTagRule[] }
GET  /api/games/{id}/script-tags      → ScriptTagConfig
PUT  /api/games/{id}/script-tags      → ScriptTagConfig (validated)
```

**PUT validation:**
- `Pattern` non-empty
- `Action` is `"extract"` or `"exclude"`
- `extract` rules must have at least one capture group `(...)`
- Regex compiles without error (1-second timeout)
- Maximum 100 rules
- `IsBuiltin` flag on submitted rules is ignored server-side (only preset import sets it)

### Integration Points

#### 1. `PreTranslationService.ExecutePreTranslationAsync`

**Before** (line 113):
```csharp
var textList = texts.Select(t => t.Text).ToList();
```

**After**:
```csharp
var (cleanTexts, cleanToOriginal) = await scriptTagService.FilterAndCleanAsync(gameId, texts);
// cleanTexts → sent to LLM (clean text only)
// status.TotalTexts = cleanTexts.Count (reflects actual translation work)
```

Batch loop sends `cleanTexts` to LLM. `translations` dictionary keys are clean texts. Cache writing uses clean texts directly as keys.

#### 2. `PreTranslationService.WriteTranslationCacheAsync`

**Before** (line 212-214):
```csharp
var originalKey = enableCacheOptimization
    ? XUnityTranslationFormat.NormalizeForCache(original)
    : original;
```

**After**:
```csharp
var originalKey = enableCacheOptimization
    ? scriptTagService.NormalizeForCache(gameId, original)
    : original;
```

Since input is already cleaned by `FilterAndClean`, this is a safety net (rich text stripping + idempotent script tag cleaning).

#### 3. `PreTranslationCacheMonitor.LoadCache` (line 78)

**Before**:
```csharp
keys.Add(XUnityTranslationFormat.NormalizeForCache(decoded));
```

**After**:
```csharp
keys.Add(scriptTagService.NormalizeForCache(gameId, decoded));
```

#### 4. `PreTranslationCacheMonitor.RecordTexts` (line 111)

**Before**:
```csharp
var normalized = XUnityTranslationFormat.NormalizeForCache(text);
```

**After**:
```csharp
var normalized = scriptTagService.NormalizeForCache(gameId, text);
```

`PreTranslationCacheMonitor` constructor adds `ScriptTagService` dependency.

#### 5. `DELETE /api/games/{id}` cleanup

Add alongside existing DNT cleanup in `GameEndpoints.cs`:
```csharp
var scriptTagFile = appDataPaths.ScriptTagFile(id);
if (File.Exists(scriptTagFile)) File.Delete(scriptTagFile);
scriptTagService.RemoveCache(id);
```

#### 6. DI Registration

```csharp
builder.Services.AddSingleton<ScriptTagService>();
```

### What Does NOT Change

| File | Reason |
|------|--------|
| `XUnityTranslationFormat.cs` | Static rich-text-only method, unchanged |
| `LlmTranslationService.cs` | Cleaning happens in caller (PreTranslationService) |
| `AssetExtractionService.cs` | Extracts raw text as-is; cleaning at translation time |
| `TranslateEndpoints.cs` | Runtime `POST /api/translate` from DLL; text already clean from XUnity |

### Frontend

**Location**: New collapsible card in the pre-translation panel, alongside "Custom regex patterns".

**UI structure**:
```
┌─ Script Tag Cleaning Rules ──────────────────────────────┐
│                                                           │
│  [Import Built-in Rules]         [+ Add Rule]    [Save]   │
│                                                           │
│  Pattern                    Action    Desc        Op      │
│  ─────────────────────────────────────────────────────── │
│  ^(?:stk|tk|ts|FTK),...     extract   Dialogue    lock    │
│  ^%%,[^,]*,(.*),#\w+,?$    extract   Choice      lock    │
│  ^\$\w+                    exclude   SysCmd      lock    │
│  ^#\w+$                    exclude   Label       lock    │
│  ^Voice_                   exclude   Voice       lock    │
│  ^@                        exclude   Event       lock    │
│  ^custom-pattern$          exclude   Custom      [x]     │
│                                                           │
│  Built-in rules auto-refresh on app update.               │
└───────────────────────────────────────────────────────────┘
```

- Built-in rules (`isBuiltin=true`): displayed with lock icon, not editable/deletable.
- Custom rules (`isBuiltin=false`): fully editable, deletable via [x] button.
- "Import Built-in Rules" button: fetches `GET /api/script-tag-presets`, applies with `isBuiltin=true`.
- Action column: `<n-select>` with two options.
- Pattern column: `<n-input>` (monospace font recommended).
- Save calls `PUT /api/games/{id}/script-tags`.

**Types** (`src/api/types.ts`):
```typescript
export interface ScriptTagRule {
  pattern: string
  action: 'extract' | 'exclude'
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

**API functions** (`src/api/games.ts`):
```typescript
export const getScriptTags = (gameId: string) =>
  api.get<ScriptTagConfig>(`/api/games/${gameId}/script-tags`)

export const saveScriptTags = (gameId: string, config: ScriptTagConfig) =>
  api.put<ScriptTagConfig>(`/api/games/${gameId}/script-tags`, config)

export const getScriptTagPresets = () =>
  api.get<ScriptTagPreset>('/api/script-tag-presets')
```

## Asset Extraction Coverage

**Verified for KODOKU game:**

| Source | Extracted? | Notes |
|--------|-----------|-------|
| TextAsset (.bytes) | Yes | UTF-16LE BOM correctly detected. Lines split by `\n`, each passes `IsGameText`. |
| MonoBehaviour fields | Yes | Recursive `CollectStrings` up to depth 10. |
| Localization bundles | Yes | `.bundle` extension files in `StreamingAssets/aa/` found by recursive scan. |
| Loose .assets | Yes | Top-level `{GameName}_Data/*.assets` scanned. |

**`IsGameText` filter coverage**: Instruction-prefixed lines (`tk,1,...`, `%%,-1,...`, `$Go_link,...`, `#BTN_A01`) all pass through the filter. This is acceptable because `ScriptTagService` handles the cleaning/exclusion at pre-translation time.

## Sync Points (for CLAUDE.md)

- **ScriptTagRule/ScriptTagConfig fields**: Sync 2 places: `Models/ScriptTagRule.cs` + `Models/ScriptTagConfig.cs` ↔ `src/api/types.ts`
- **Per-game data cleanup**: `DELETE /api/games/{id}` in `GameEndpoints.cs` must delete `scriptTagFile` + call `RemoveCache`
- **`NormalizeForCache` call sites**: 3 places must all use `ScriptTagService.NormalizeForCache(gameId, text)`: `WriteTranslationCacheAsync`, `LoadCache`, `RecordTexts`
- **Adding preset rules**: Update `bundled/script-tag-presets.json`, increment `version`

## Future Extensibility

This design accommodates future game adaptation with zero code changes:

1. **New game with different instruction format**: User configures rules via UI, or we add patterns to the global preset file.
2. **New rule action types** (e.g., `replace`): Add to `ScriptTagService.CleanText` switch, existing callers unchanged.
3. **Per-game adaptation workflow**: User provides exported game project → developer analyzes script parsers → identifies instruction patterns → adds to preset or user configures custom rules.
4. **Preset rules grow over time**: Each app update can ship expanded presets that auto-apply to all games.
