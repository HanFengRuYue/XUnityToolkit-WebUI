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

## Layer 4: Cache Hit Monitoring & Logging

### Concept

When XUnity's cache hits a pre-translated entry, the text never reaches `POST /api/translate`. Therefore cache hits are inferred by elimination: texts that arrive at the endpoint despite having a pre-translation are **misses**; pre-translated texts never seen at the endpoint are **hits**.

### Backend: `PreTranslationCacheMonitor` Service

A new singleton service that tracks cache effectiveness per game:

**State:**
```csharp
// Loaded from _PreTranslated.txt when monitoring starts
HashSet<string> preTranslatedKeys;           // all normalized pre-translated originals
ConcurrentDictionary<string, string> misses; // normalized key → raw runtime text (came through endpoint)
long hitCount;                               // preTranslatedKeys.Count - misses.Count (updated lazily)
```

**Lifecycle:**
1. When AI translation is enabled for a game, load that game's `_PreTranslated.txt` entries into `preTranslatedKeys` (normalized form)
2. On each `POST /api/translate` call, check each incoming text against `preTranslatedKeys` (after normalizing). If match found → record as miss (pre-translation existed but XUnity didn't match it)
3. Expose stats via `GetCacheStats(gameId)` → `PreTranslationCacheStats`

**Integration point:** In `TranslateEndpoints.cs`, after receiving texts from the endpoint, call `cacheMonitor.RecordTexts(gameId, texts)` before passing to `LlmTranslationService`.

### Stats Model

```csharp
public sealed record PreTranslationCacheStats(
    int TotalPreTranslated,    // total entries in _PreTranslated.txt
    int CacheHits,             // entries NOT seen at endpoint (inferred hits)
    int CacheMisses,           // entries that arrived at endpoint despite pre-translation
    int NewTexts,              // texts at endpoint with no pre-translation at all
    double HitRate,            // CacheHits / TotalPreTranslated (0 if no pre-translations)
    IList<CacheMissEntry> RecentMisses  // last N misses for debugging
);

public sealed record CacheMissEntry(
    string PreTranslatedKey,   // the normalized pre-translated original
    string RuntimeText,        // what XUnity actually sent
    DateTime Timestamp
);
```

### SignalR Broadcasting

Extend the existing `ai-translation` SignalR group with a new message:
- Message name: `preCacheStatsUpdate`
- Payload: `PreTranslationCacheStats`
- Throttle: same 200ms CAS pattern as `BroadcastStats`
- Triggered: after each `RecordTexts` call

### API Endpoint

- `GET /api/translate/cache-stats` — Get current pre-translation cache stats for the active game

### Frontend: Cache Status Card on AiTranslationView

Add a new card section in `AiTranslationView.vue` (between the Status Dashboard and Recent Translations):

**Card layout:**
- Title: "Pre-Translation Cache" (预翻译缓存)
- Metrics row: Total entries | Hits | Misses | Hit Rate %
- Color-coded progress bar: green (hits) / red (misses) / gray (unseen)
- Collapsible "Recent Misses" list showing `CacheMissEntry` items — displays the pre-translated key vs actual runtime text side by side, helping users diagnose WHY mismatches occur

**Visibility:** Only shown when `totalPreTranslated > 0` (game has pre-translations).

**Data source:** Subscribe to `preCacheStatsUpdate` SignalR message in `aiTranslation.ts` store; also fetch via REST on mount.

### Logging

All log messages go through standard `ILogger<PreTranslationCacheMonitor>`, automatically flowing to file log + SignalR `logs` group.

| Event | Level | Message |
|-------|-------|---------|
| Cache loaded | Info | `预翻译缓存已加载: {gameId}, {count} 条条目` |
| Cache miss detected | Debug | `预翻译缓存未命中: 预翻译键="{key}", 运行时文本="{runtime}"` |
| Periodic summary (every 60s while active) | Info | `预翻译缓存统计: 总计={total}, 命中={hits}, 未命中={misses}, 命中率={rate}%` |
| Cache unloaded | Info | `预翻译缓存已卸载: {gameId}` |

The Debug-level miss logs are key for post-hoc diagnosis — users can view them in the Logs page or download the log file.

## Files Changed

### Backend

| File | Change |
|------|--------|
| `Models/XUnityConfig.cs` | Add `CacheWhitespaceDifferences`, `IgnoreWhitespaceInDialogue`, `MinDialogueChars` |
| `Models/TranslationStats.cs` | Add `PreTranslationCacheStats` and `CacheMissEntry` records |
| `Services/ConfigurationService.cs` | Read/write new INI keys in `[Behaviour]` section |
| `Services/XUnityTranslationFormat.cs` | Add `NormalizeForCache()` static method |
| `Services/PreTranslationService.cs` | Apply normalization before writing cache; generate `_PreTranslated_Regex.txt` |
| `Services/PreTranslationCacheMonitor.cs` | **New** — singleton service tracking cache hits/misses per game |
| `Services/InstallOrchestrator.cs` | Set defaults for new config fields during installation |
| `Endpoints/AssetEndpoints.cs` | Add endpoint for custom regex patterns CRUD |
| `Endpoints/TranslateEndpoints.cs` | Call `cacheMonitor.RecordTexts()` on incoming texts; add `GET /api/translate/cache-stats` |
| `Models/AppDataPaths.cs` | Add `PreTranslationRegexFile(string gameId)` path |
| `Program.cs` | Register `PreTranslationCacheMonitor` as singleton |

### Frontend

| File | Change |
|------|--------|
| `src/api/types.ts` | Add `XUnityConfig` fields, `PreTranslationCacheStats`, `CacheMissEntry` types |
| `src/stores/aiTranslation.ts` | Subscribe to `preCacheStatsUpdate` SignalR message; add `cacheStats` ref |
| `src/views/AiTranslationView.vue` | Add pre-translation cache status card with metrics + recent misses list |
| `src/components/config/ConfigPanel.vue` | Add controls for new config fields |
| Pre-translation UI component | Add custom regex pattern text area |

### No Changes Needed

- `TranslatorEndpoint/LLMTranslateEndpoint.cs` — No changes; all optimization happens at cache level
- `LlmTranslationService.cs` — No changes; translation logic unchanged

## API Endpoints (New)

- `GET /api/games/{id}/pre-translate/regex` — Get custom regex patterns for game
- `PUT /api/games/{id}/pre-translate/regex` — Save custom regex patterns for game
- `GET /api/translate/cache-stats` — Get pre-translation cache monitoring stats for active game

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
- **PreTranslationCacheStats model:** Sync 2 places: `Models/TranslationStats.cs`, `src/api/types.ts`; display in `AiTranslationView.vue`
- **SignalR `preCacheStatsUpdate` message:** Backend broadcast in `PreTranslationCacheMonitor`; frontend subscribe in `aiTranslation.ts` store

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
- AI Translation page shows pre-translation cache card with real-time hit/miss stats
- Cache misses are logged with both the pre-translated key and runtime text for diagnosis
- All cache monitoring events appear in the Logs page via standard logging infrastructure
