# Unified Term Management — Design Spec

## Summary

Redesign the glossary and do-not-translate (DNT) features into a single unified term management system. Replace the current two separate data models, services, API endpoints, and UI tabs with one integrated system featuring filter chip navigation, inline table editing, drag-and-drop priority ordering, and a multi-phase translation pipeline with term audit.

## Motivation

The current implementation maintains glossary and DNT as completely independent parallel systems (separate models, services, storage files, API endpoints, and UI tabs). This diverges from industry standards (memoQ, Phrase TMS, Weblate) where DNT is a flag/type within the glossary system. The separation causes:

- Placeholder substitution order complexity (glossary must run before DNT)
- Duplicated code (two services with nearly identical caching logic)
- No shared priority control across glossary and DNT entries
- Missing cleanup on game deletion (glossary files are orphaned)
- No cross-game term sharing

## Data Model

### TermEntry

Replaces both `GlossaryEntry` and `DoNotTranslateEntry`.

| Field | Type | Default | Description |
|-------|------|---------|-------------|
| `type` | `"translate"` \| `"doNotTranslate"` | `"translate"` | Entry type |
| `original` | `string` | — | Source text or regex pattern |
| `translation` | `string?` | `null` | Target translation (null/empty for DNT) |
| `category` | `TermCategory?` | `null` | Predefined classification |
| `description` | `string?` | `null` | Note/context |
| `isRegex` | `bool` | `false` | Whether `original` is a regex pattern |
| `caseSensitive` | `bool` | `true` | Case-sensitive matching |
| `exactMatch` | `bool` | `false` | Whole-word boundary matching |
| `priority` | `int` | `0` | Higher value = applied first |

### TermCategory Enum

`Character`, `Location`, `Item`, `Skill`, `Organization`, `General`

### TypeScript Types

```typescript
export type TermType = 'translate' | 'doNotTranslate'
export type TermCategory = 'character' | 'location' | 'item' | 'skill' | 'organization' | 'general'

export interface TermEntry {
  type: TermType
  original: string
  translation?: string
  category?: TermCategory
  description?: string
  isRegex: boolean
  caseSensitive: boolean
  exactMatch: boolean
  priority: number
}
```

### Storage

- Unified file: `{appDataRoot}/glossaries/{gameId}.json`
- Old DNT files at `{appDataRoot}/do-not-translate/{gameId}.json` migrated on first load, renamed to `.migrated`

## Backend Architecture

### TermService

Replaces `GlossaryService` + `DoNotTranslateService`.

- `ConcurrentDictionary<string, List<TermEntry>>` + `SemaphoreSlim` caching (same pattern as current)
- `GetAsync(gameId)` — lazy load with double-checked locking; on first load, auto-migrates any old DNT file
- `SaveAsync(gameId, entries)` — atomic write (temp file + `File.Move`)
- `MergeAsync(gameId, newEntries)` — for auto-extraction; dedup by `original`
- `RemoveCache(gameId)` — cache eviction (fixes current glossary cleanup bug)
- `ImportFromGameAsync(targetGameId, sourceGameId)` — cross-game import with dedup

### Data Migration

- Triggered lazily on first `GetAsync` per game
- Reads `do-not-translate/{gameId}.json`, converts entries to `type=doNotTranslate`, merges into glossary file
- Deduplicates by `original` (ordinal)
- Renames old file to `{gameId}.json.migrated`

### API Endpoints

| Method | Path | Description |
|--------|------|-------------|
| `GET` | `/api/games/{id}/terms` | Returns `List<TermEntry>` |
| `PUT` | `/api/games/{id}/terms` | Saves term list; max 10000 entries; validates regex with 1s timeout |
| `POST` | `/api/games/{id}/terms/import-from-game` | Body: `{ sourceGameId }`; returns `{ added, skipped }` |
| `GET` | `/api/games/{id}/glossary` | **Compat proxy** — filters `type=translate`, maps to old `GlossaryEntry` shape |
| `PUT` | `/api/games/{id}/glossary` | **Compat proxy** — converts to `TermEntry`, merges with existing DNT entries |
| `GET` | `/api/games/{id}/do-not-translate` | **Compat proxy** — filters `type=doNotTranslate` |
| `PUT` | `/api/games/{id}/do-not-translate` | **Compat proxy** — converts to `TermEntry`, merges with existing translate entries |

Compat proxies support `TranslationEditorView` and other existing callers during transition. To be removed in a future version.

### Game Deletion Cleanup

`DELETE /api/games/{id}` updated to:
- Delete `glossaries/{gameId}.json`
- Call `TermService.RemoveCache(gameId)`
- Remove old DNT cleanup logic

### DI Registration

`TermService` registered as singleton, replacing `GlossaryService` + `DoNotTranslateService`.

## Multi-Phase Translation Pipeline

### Overview

```
Source text + matched terms
       |
       v
 Phase 1: Natural Translation
   LLM receives matched terms + original text, applies terms naturally
       |
       v
 Term Audit
   Check if all matched terms are correctly applied
       |
    pass |         fail
      v  |           v
   Return result   Phase 2: Placeholder Translation
                     Fallback to placeholder substitution
                       |
                       v
                   Term Audit
                       |
                    pass |         fail
                      v  |           v
                   Return result   Phase 3: Force Correction
                                     string.Replace remaining terms
                                       |
                                       v
                                    Return result
```

### Phase 1 — Natural Translation

Prompt includes only **matched terms** (not the full term list):

```
Translate the following, strictly adhering to this term list:

[Terms to translate]
- 魔法師 → 法师 (character class)
- エリシア → 艾莉希亚 (character name)
- ファイアボール → 火球术 (skill name)

[Do not translate — preserve exactly]
- HP
```

Source text sent unmodified (no placeholders).

### Phase 2 — Placeholder Fallback

Only triggered when Phase 1 audit fails.

- All matched terms replaced with `{{G_N}}` (translate) or `{{DNT_N}}` (doNotTranslate) placeholders
- Ordered by `priority` descending, then by `original` length descending
- LLM translates placeholder-embedded text
- Post-processing restores placeholders: glossary restore → glossary post-process → DNT restore

### Phase 3 — Force Correction

Only triggered when Phase 2 audit also fails. No LLM call.

- `translate` entries: `string.Replace(original, translation)` on Phase 2 output
- `doNotTranslate` entries: if original is missing from output, log warning (cannot recover translated-away text)

### Term Audit

Checks applied after both Phase 1 and Phase 2:

| Entry Type | Check | Pass Condition |
|------------|-------|----------------|
| `translate` | Translation presence | `translation` appears in output |
| `doNotTranslate` | Original preservation | `original` appears in output (respecting `caseSensitive`) |
| `isRegex=true` | Skip | Cannot reliably verify regex-based terms |

Audit is per-text-segment in batch mode. Segments that pass Phase 1 are kept; only failed segments proceed to Phase 2.

### Token Budget Control

Applies to both cloud and local models. Only **matched terms** are injected into prompts (never the full term list).

```
Available context = ContextSize (user-configured)
Base system prompt ≈ estimated tokens (fixed rules, format requirements)
Source text tokens ≈ estimated (CJK: ~1 token/char, Western: ~1.3 tokens/word)
Matched terms tokens ≈ estimated (~15-30 tokens per entry)
Reserved output = source tokens × 1.5

Used = base prompt + source + reserved output
Remaining = available context - used
```

| Condition | Behavior |
|-----------|----------|
| Matched terms ≤ 70% of remaining | Phase 1 with all matched terms in prompt |
| Matched terms > 70% of remaining | Skip Phase 1, go directly to Phase 2 (placeholders, no prompt injection) |
| Placeholder text still exceeds budget | Truncate by priority descending, apply only highest-priority terms |

### Configuration

New fields in `AiTranslationSettings`:

| Field | Type | Default | Description |
|-------|------|---------|-------------|
| `TermAuditEnabled` | `bool` | `true` | Enable term audit after translation |
| `NaturalTranslationMode` | `bool` | `true` | Enable Phase 1 natural translation (false = skip to Phase 2) |

### Statistics

New fields in `TranslationStats`:

| Field | Type | Description |
|-------|------|-------------|
| `termAuditPhase1PassCount` | `int` | Segments passing Phase 1 audit |
| `termAuditPhase2PassCount` | `int` | Segments passing Phase 2 audit |
| `termAuditForceCorrectedCount` | `int` | Segments requiring Phase 3 force correction |

Broadcast via SignalR to `ai-translation` group.

## Frontend UI

### Component & Route

- `GlossaryEditorView.vue` → `TermEditorView.vue`
- Route: `/games/:id/term-editor` (old `/games/:id/glossary-editor` redirects)
- Page title: "术语管理"

### Layout (top to bottom)

1. **Page header**: back button + title "术语管理" + auto-save badge
2. **Toolbar**: Clear / Import / Export / Import from Game / Save buttons
3. **Filter chip bar**:
   - Group 1 (type): `All(N)` | `Translate(N)` | `DoNotTranslate(N)`
   - Separator
   - Group 2 (category): `Character(N)` | `Location(N)` | `Item(N)` | `Skill(N)` | `Organization(N)` | `General(N)`
   - Combinable: e.g., "Translate" + "Character" shows only translate entries with character category
4. **Search bar**: search across original, translation, description
5. **Add row**: type select + original input + translation input + category select + add button
6. **Data table**: NDataTable with virtual scroll

### Table Columns

| Column | Width | Content | Default Visible |
|--------|-------|---------|-----------------|
| Drag handle | 24px | `⋮⋮` icon for reordering | Yes |
| Type | 60px | NTag (blue "翻译" / orange "禁翻"), click to toggle | Yes |
| Original | flex | NInput inline edit | Yes |
| Translation | flex | NInput inline edit (disabled + "—" for DNT) | Yes |
| Category | 90px | NSelect dropdown | Yes |
| Description | flex | NInput | No (hidden by default) |
| Regex | 50px | NSwitch | Yes |
| Exact Match | 50px | NSwitch | Yes |
| Case Sensitive | 50px | NSwitch | No (hidden by default) |
| Priority | 60px | NInputNumber | No (hidden by default) |
| Delete | 32px | Delete button | Yes |

### Column Visibility Control

- Gear button in toolbar opens NPopover with checkbox list
- Preferences stored in `localStorage`

### Drag-and-Drop Sorting

- `vuedraggable` (SortableJS) or HTML5 drag API
- Drag changes array order, triggers auto-save
- Disabled when search/filter is active

### Import/Export

- **Import**: JSON (auto-converts old format) and CSV/TSV (auto-detect delimiter by file extension)
- **Export**: User chooses JSON or CSV; filename `{gameName}_术语库.{json|csv}`
- **Import from Game**: NModal listing all games; calls `POST .../terms/import-from-game`; shows "Added X, skipped Y duplicates"

### Type Toggle Interaction

- Click type tag to toggle translate/doNotTranslate
- Switching to DNT: translation field cleared and disabled
- Switching to translate: translation field re-enabled

### Auto-Save

- Single `useAutoSave` instance (2s debounce, deep watch)
- Same disable-during-load and disable-during-manual-save pattern as current

## Sync Points

| Location | Change |
|----------|--------|
| `Models/TermEntry.cs` | New, replaces `GlossaryEntry.cs` + `DoNotTranslateEntry.cs` |
| `Services/TermService.cs` | New, replaces `GlossaryService.cs` + `DoNotTranslateService.cs` |
| `Services/GlossaryExtractionService.cs` | Call `TermService.MergeAsync`, set `type=translate` on extracted entries |
| `Services/LlmTranslationService.cs` | Call `TermService`; implement multi-phase pipeline + term audit |
| `Endpoints/GameEndpoints.cs` | New endpoints + compat proxies + game deletion cleanup |
| `Infrastructure/AppDataPaths.cs` | Remove `DoNotTranslate` directory constant (deprecated after migration) |
| `Program.cs` DI | Register `TermService` replacing two old services |
| `Models/AiTranslationSettings.cs` | Add `TermAuditEnabled`, `NaturalTranslationMode` |
| `Models/TranslationStats.cs` | Add `termAuditPhase1PassCount`, `termAuditPhase2PassCount`, `termAuditForceCorrectedCount` |
| `src/api/types.ts` | `TermEntry` replaces `GlossaryEntry` + `DoNotTranslateEntry`; new stats fields |
| `src/api/games.ts` | New API methods `getTerms`/`saveTerms`/`importTermsFromGame` |
| `src/views/TermEditorView.vue` | New, replaces `GlossaryEditorView.vue` |
| `src/views/TranslationEditorView.vue` | Migrate to new API (or use compat proxy during transition) |
| `src/views/AiTranslationView.vue` | Display new audit stats; new settings toggles |
| `src/views/SettingsView.vue` | New AI translation settings for term audit |
| `src/router/index.ts` | Route rename + old route redirect |

## Backward Compatibility

- Old `GET/PUT .../glossary` and `GET/PUT .../do-not-translate` endpoints retained as compat proxies
- Old DNT files auto-migrated on first access, renamed to `.migrated`
- Old JSON import format auto-detected and converted
- Cleanup plan: remove compat endpoints and old type definitions in a future version

## What Does NOT Change

- `GlossaryExtractionService` extraction logic and LLM prompts (only caller changes)
- `PreTranslationService` (uses `LlmTranslationService` indirectly)
- `useAutoSave` composable
- SignalR broadcast patterns (new stats fields added, existing unchanged)
- Backup/restore logic
