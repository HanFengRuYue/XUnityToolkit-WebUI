# Pre-Translation Cache Optimization Design

## Problem

The pre-translation feature extracts text strings from Unity asset files and translates them ahead of time, writing results to XUnity.AutoTranslator cache files (`_PreTranslated.txt`). However, the cache keys (extracted asset text) rarely match what XUnity intercepts at runtime, making pre-translation essentially useless.

Two types of mismatches occur simultaneously:

1. **Minor differences** — Extra whitespace, punctuation, rich text tags between extracted and runtime text
2. **Structural differences** — Runtime text is a concatenation of multiple extracted fragments (XUnity sees one combined string, extraction yields individual pieces)

XUnity.AutoTranslator uses **exact string matching** for cache lookups (after its own preprocessing). There is no built-in fuzzy matching.

## Solution Overview

Leverage XUnity.AutoTranslator's native features across three layers:

1. **XUnity configuration optimization** — Enable built-in whitespace tolerance
2. **Cache key normalization** — Apply the same text preprocessing XUnity does before writing cache keys
3. **Splitter regex generation** — Use XUnity's `sr:` regex feature to split concatenated runtime text into fragments that can individually hit the cache

## Layer 1: XUnity Configuration Optimization

### New Config Fields

Add four fields to `XUnityConfig` model under `[Behaviour]`:

| Field | Type | Default | INI Key |
|-------|------|---------|---------|
| `CacheWhitespaceDifferences` | `bool` | `false` | `CacheWhitespaceDifferences` |
| `IgnoreWhitespaceInDialogue` | `bool` | `true` | `IgnoreWhitespaceInDialogue` |
| `MinDialogueChars` | `int` | `4` | `MinDialogueChars` |
| `TemplateAllNumberAway` | `bool` | `true` | `TemplateAllNumberAway` |

Note: `TemplateAllNumberAway` already exists in `XUnityConfig` (currently defaults to `false`). Change its default to `true` for pre-translation optimization.

**Rationale:**
- `CacheWhitespaceDifferences=False` (XUnity default) — Texts differing only by whitespace share a single cache entry
- `IgnoreWhitespaceInDialogue=True` (XUnity default) — Strips whitespace from text before cache lookup when text length exceeds `MinDialogueChars`
- `MinDialogueChars=4` (lowered from XUnity default of 20) — Ensures whitespace stripping applies to shorter game strings too
- `TemplateAllNumberAway=True` (changed from XUnity default `false`) — Replaces all numbers with `{{A}}`, `{{B}}` placeholders before cache lookup, so "HP: 100" and "HP: 200" share one cache key. This handles text+number concatenation patterns that `sr:` regex cannot cleanly address (since `sr:` would try to independently translate number-only groups)

**Interaction note:** When `TemplateAllNumberAway=True`, pre-translation should also write number-templated cache keys. For extracted text containing numbers (e.g., "Stage 1"), the cache key should be written as the templated form (e.g., "Stage {{A}}") so it matches the runtime lookup key.

### Implementation

- Add fields to `Models/XUnityConfig.cs`
- Update `ConfigurationService` to read/write these INI keys in the `[Behaviour]` section
- Set defaults during installation in `InstallOrchestrator`
- Add frontend controls in `ConfigPanel.vue`
- Sync types in `src/api/types.ts`

## Layer 2: Cache Key Normalization

### `NormalizeForCache` Method

Add a static method to `XUnityTranslationFormat`:

```csharp
public static string NormalizeForCache(string text)
```

**Normalization steps (in order):**
1. Strip known Unity/TMP rich text tags via whitelist regex — matches only recognized tags: `</?(?:color|b|i|size|sprite|material|quad|voffset|indent|link|mark|sup|sub|font|cspace|align|mspace|uppercase|lowercase|smallcaps|noparse|nobr|space|width|margin|rotate|s|u|line-height|line-indent|page|style|br)(?:=[^>]*)?>`. This avoids stripping legitimate angle-bracket content like `"Press <A> button"`.
2. Trim leading/trailing whitespace

**Important:** Only these two steps are applied. Whitespace collapsing and Unicode NFC normalization are intentionally omitted because XUnity does not perform these operations during its preprocessing. Adding them would create *more* mismatches, not fewer. The `CacheWhitespaceDifferences=False` config (Layer 1) handles internal whitespace differences at the XUnity level.

**Rationale:** XUnity's `HandleRichText=True` (our default) strips rich text tags from runtime text before cache lookup. By applying the same tag stripping to extracted text, cache keys become more likely to match.

### Integration Point

In `PreTranslationService.WriteTranslationCacheAsync`, apply `NormalizeForCache()` to the original text before encoding:

```
original → NormalizeForCache(original) → XUnityTranslationFormat.Encode() → write as cache key
```

The translation value remains unchanged (it's the translated text, not a lookup key).

**Edge cases:**
- If normalization produces an empty string, skip the entry
- If normalization produces a duplicate key, keep the first occurrence
- When re-running pre-translation on a game with existing `_PreTranslated.txt`, the file is regenerated from scratch (not appended), so old non-normalized keys are naturally replaced

## Layer 3: Splitter Regex (`sr:`) Generation

### Concept

XUnity's `sr:` regex feature splits matched text into capture groups, translates each group individually via normal cache lookup, then reassembles. This elegantly handles concatenated runtime text: if each fragment has a normal translation entry, the composite text gets translated correctly.

### Output File

Write `sr:` entries to a separate file: `{gamePath}/BepInEx/Translation/{toLang}/Text/_PreTranslated_Regex.txt`

XUnity loads all `.txt` files in the `Text/` directory, so this file is automatically picked up.

### Important: `sr:` Semantics

`sr:` translates **each capture group independently** via normal cache lookup, then reassembles. This means every capture group should contain translatable text that has a corresponding cache entry. Non-text groups (pure numbers, punctuation) would be sent to the translation endpoint as standalone strings, which is wasteful and may produce garbage.

For text + number patterns (e.g., "HP: 100"), use `TemplateAllNumberAway=True` in Layer 1 instead of `sr:` regex. This makes XUnity replace numbers with `{{A}}`, `{{B}}` placeholders before lookup, so "HP: 100" becomes "HP: {{A}}" — a single consistent cache key.

### Built-in Patterns

A conservative set targeting text-only concatenation patterns:

```
// Pre-translation regex patterns generated by XUnity Toolkit

// Two-line concatenation (both groups are text)
sr:"^([\S\s]+?)\n([\S\s]+)$"=$1\n$2

// Three-line concatenation
sr:"^([\S\s]+?)\n([\S\s]+?)\n([\S\s]+)$"=$1\n$2\n$3
```

**Intentionally excluded:** Number-prefix and text+number patterns. These are better handled by `TemplateAllNumberAway=True` (see Layer 1) which normalizes numbers to placeholders before cache lookup, avoiding wasteful translation of number-only groups.

These patterns are always generated when pre-translation runs. The set is kept minimal to avoid XUnity performance degradation (regex patterns are evaluated for every text lookup).

### User Custom Patterns

Provide a text area in the pre-translation UI for users to add game-specific `sr:` patterns. Stored per-game at `{dataRoot}/cache/pre-translation-regex/{gameId}.txt`.

Custom patterns are appended after built-in patterns in `_PreTranslated_Regex.txt`.

### Validation

Before writing, validate each custom pattern:
- Must start with `sr:` or `r:` prefix
- Must contain an `=` separator
- Test regex compilation with a 1-second timeout (consistent with existing glossary regex validation)

## Files Changed

### Backend

| File | Change |
|------|--------|
| `Models/XUnityConfig.cs` | Add `CacheWhitespaceDifferences`, `IgnoreWhitespaceInDialogue`, `MinDialogueChars` |
| `Services/ConfigurationService.cs` | Read/write new INI keys in `[Behaviour]` section |
| `Services/XUnityTranslationFormat.cs` | Add `NormalizeForCache()` static method |
| `Services/PreTranslationService.cs` | Apply normalization before writing cache; generate `_PreTranslated_Regex.txt` |
| `Services/InstallOrchestrator.cs` | Set defaults for new config fields during installation |
| `Endpoints/AssetEndpoints.cs` | Add endpoint for custom regex patterns CRUD |
| `Models/AppDataPaths.cs` | Add `PreTranslationRegexFile(string gameId)` path |

### Frontend

| File | Change |
|------|--------|
| `src/api/types.ts` | Add new `XUnityConfig` fields |
| `src/components/config/ConfigPanel.vue` | Add controls for new config fields |
| Pre-translation UI component | Add custom regex pattern text area |

### No Changes Needed

- `TranslatorEndpoint/LLMTranslateEndpoint.cs` — No changes; all optimization happens at cache level
- `LlmTranslationService.cs` — No changes; translation logic unchanged

## API Endpoints (New)

- `GET /api/games/{id}/pre-translate/regex` — Get custom regex patterns for game
- `PUT /api/games/{id}/pre-translate/regex` — Save custom regex patterns for game

## Backup & Cleanup

- `_PreTranslated_Regex.txt` is written to the game directory alongside `_PreTranslated.txt` — must be included in:
  - `BackupService` backup manifest (for uninstall cleanup)
  - Plugin package export (`PluginPackageService`)
  - `DELETE /api/games/{id}` cleanup handler
- Custom regex patterns at `{dataRoot}/cache/pre-translation-regex/{gameId}.txt` — user data, should be included in settings export

## Sync Points

- **XUnityConfig fields:** Sync 4 places per CLAUDE.md pattern: `Models/XUnityConfig.cs`, `ConfigurationService.cs` (read/write), `src/api/types.ts`, `ConfigPanel.vue`
- **`TemplateAllNumberAway` default change:** Update the default in `XUnityConfig.cs` from `false` to `true`; update `InstallOrchestrator` to write the new default
- **Pre-translation regex path:** Add to `AppDataPaths`; add `cache/pre-translation-regex/` to `EnsureDirectoriesExist()`; evaluate for export exclusion list in `SettingsEndpoints.cs` (should be exported as user data)
- **New API endpoints:** Add to CLAUDE.md API Endpoints section

## Risks & Mitigations

| Risk | Mitigation |
|------|-----------|
| `sr:` patterns cause false splits on unexpected text | Keep built-in patterns conservative (line-split only); user can disable/customize |
| Normalization strips legitimate angle-bracket content | Whitelist regex matches only known Unity/TMP rich text tag names; `<A>` button prompts etc. are preserved |
| `TemplateAllNumberAway=True` breaks games relying on number-specific translations | Make it configurable in UI; user can disable per game |
| Regex patterns degrade XUnity performance | XUnity docs warn to use regex sparingly; limit to a small set of patterns |
| `MinDialogueChars=4` too aggressive | Make it configurable; user can raise if it causes issues |

## Success Criteria

- Pre-translated text fragments match XUnity runtime cache lookups for:
  - Text with minor whitespace differences
  - Text with rich text tag differences
  - Concatenated text that follows common patterns (number+text, text+value, newline-joined)
- No regression in existing translation features
- XUnity performance not noticeably degraded
