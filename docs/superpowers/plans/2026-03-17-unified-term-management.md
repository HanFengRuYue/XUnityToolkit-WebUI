# Unified Term Management Implementation Plan

> **For agentic workers:** REQUIRED: Use superpowers:subagent-driven-development (if subagents available) or superpowers:executing-plans to implement this plan. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the separate glossary and do-not-translate systems with a unified term management system featuring a multi-phase translation pipeline with term audit.

**Architecture:** A single `TermService` replaces `GlossaryService` + `DoNotTranslateService`, storing unified `TermEntry` records per game. The translation pipeline gains three phases: natural LLM translation with matched-term injection, placeholder fallback, and force correction — each gated by a term audit check. Frontend consolidates into one filter-chip-driven table view.

**Tech Stack:** ASP.NET Core (.NET 10.0), Vue 3 + TypeScript + Naive UI, SignalR

**Spec:** `docs/superpowers/specs/2026-03-17-unified-term-management-design.md`

---

## File Structure

### Backend — New Files
| File | Responsibility |
|------|---------------|
| `Models/TermEntry.cs` | `TermEntry` record, `TermType` enum, `TermCategory` enum |
| `Services/TermService.cs` | Unified term CRUD, caching, migration, cross-game import |
| `Services/TermAuditService.cs` | Term audit logic (check translation output against matched terms) |
| `Services/TermMatchingService.cs` | Match source texts against term entries, exactMatch/CJK boundary logic, token budget estimation |

### Backend — Modified Files
| File | Change |
|------|--------|
| `Endpoints/GameEndpoints.cs` | New `/terms` endpoints + compat proxies; game deletion cleanup |
| `Services/LlmTranslationService.cs` | Multi-phase pipeline (Phase 1/2/3), call TermService/TermMatchingService/TermAuditService |
| `Services/GlossaryExtractionService.cs` | Replace GlossaryService+DoNotTranslateService with TermService |
| `Models/AiTranslationSettings.cs` | Add `TermAuditEnabled`, `NaturalTranslationMode` |
| `Models/TranslationStats.cs` | Add `termAuditPhase1PassCount`, `termAuditPhase2PassCount`, `termAuditForceCorrectedCount` |
| `Infrastructure/AppDataPaths.cs` | Mark `DoNotTranslateDirectory` as `[Obsolete]` |
| `Program.cs` | Replace DI registrations |
| `Endpoints/SettingsEndpoints.cs` | Handle unified storage in export/import |

### Backend — Delete Files
| File | Reason |
|------|--------|
| `Models/GlossaryEntry.cs` | Replaced by `TermEntry` |
| `Models/DoNotTranslateEntry.cs` | Replaced by `TermEntry` |
| `Services/GlossaryService.cs` | Replaced by `TermService` |
| `Services/DoNotTranslateService.cs` | Replaced by `TermService` |

### Frontend — New Files
| File | Responsibility |
|------|---------------|
| `src/views/TermEditorView.vue` | Unified term editor with filter chips, table, import/export |

### Frontend — Modified Files
| File | Change |
|------|--------|
| `src/api/types.ts` | `TermEntry`, `TermType`, `TermCategory` types; new stats/settings fields |
| `src/api/games.ts` | `getTerms`, `saveTerms`, `importTermsFromGame` API methods |
| `src/router/index.ts` | Route rename + redirect |
| `src/views/GameDetailView.vue` | Navigation link update |
| `src/views/AiTranslationView.vue` | Display audit stats |
| `src/views/SettingsView.vue` | New term audit settings toggles |

### Frontend — Delete Files
| File | Reason |
|------|--------|
| `src/views/GlossaryEditorView.vue` | Replaced by `TermEditorView.vue` |

---

## Task 1: Backend Data Model

**Files:**
- Create: `XUnityToolkit-WebUI/Models/TermEntry.cs`
- Modify: `XUnityToolkit-WebUI/Infrastructure/AppDataPaths.cs`
- Delete: `XUnityToolkit-WebUI/Models/GlossaryEntry.cs` (deferred until Task 3)
- Delete: `XUnityToolkit-WebUI/Models/DoNotTranslateEntry.cs` (deferred until Task 3)

- [ ] **Step 1: Create `TermEntry.cs`**

```csharp
// Models/TermEntry.cs
using System.Text.Json.Serialization;

namespace XUnityToolkit_WebUI.Models;

[JsonConverter(typeof(JsonStringEnumConverter<TermType>))]
public enum TermType
{
    Translate,
    DoNotTranslate
}

[JsonConverter(typeof(JsonStringEnumConverter<TermCategory>))]
public enum TermCategory
{
    Character,
    Location,
    Item,
    Skill,
    Organization,
    General
}

public sealed record TermEntry
{
    public TermType Type { get; set; } = TermType.Translate;
    public required string Original { get; set; }
    public string? Translation { get; set; }
    public TermCategory? Category { get; set; }
    public string? Description { get; set; }
    public bool IsRegex { get; set; }
    public bool CaseSensitive { get; set; } = true;
    public bool ExactMatch { get; set; }
    public int Priority { get; set; }
}
```

- [ ] **Step 2: Mark `DoNotTranslateDirectory` as obsolete in `AppDataPaths.cs`**

In `Infrastructure/AppDataPaths.cs`, add `[Obsolete("Migrated to glossaries/. Kept for migration path.")]` to the `DoNotTranslateDirectory` property/constant. Do NOT remove it — it's needed for migration. Also do NOT remove the `Directory.CreateDirectory(DoNotTranslateDirectory)` call in `EnsureDirectoriesExist()` — migration reads from this directory.

- [ ] **Step 3: Verify build**

Run: `dotnet build XUnityToolkit-WebUI/XUnityToolkit-WebUI.csproj -p:SkipFrontendBuild=true`
Expected: Build succeeds (old models still exist, new model added alongside)

- [ ] **Step 4: Commit**

```bash
git add XUnityToolkit-WebUI/Models/TermEntry.cs XUnityToolkit-WebUI/Infrastructure/AppDataPaths.cs
git commit -m "feat: 添加统一术语条目数据模型 TermEntry"
```

---

## Task 2: TermService — Core CRUD + Migration

**Files:**
- Create: `XUnityToolkit-WebUI/Services/TermService.cs`

- [ ] **Step 1: Create `TermService.cs` with caching and CRUD**

Follow the same `ConcurrentDictionary<string, List<TermEntry>>` + `SemaphoreSlim` pattern used in the existing `GlossaryService.cs` (lines 1-85) and `DoNotTranslateService.cs` (lines 1-80).

Key methods:
- `GetAsync(gameId)` — double-checked locking; on first load, check for old DNT file at `AppDataPaths.DoNotTranslateDirectory/{gameId}.json`, if exists: read, convert to `TermEntry` with `Type=DoNotTranslate`, merge by `Original` dedup, save unified file, rename old file to `.migrated`. All migration inside `_lock` semaphore.
- `SaveAsync(gameId, entries)` — filter blanks, dedup by `Original`, atomic write (`.tmp` + `File.Move`)
- `MergeAsync(gameId, newEntries)` — load existing, skip entries whose `Original` already exists, append new, save. Used by extraction service.
- `ReplaceByTypeAsync(gameId, type, newEntries)` — for compat proxy PUT: holds `_lock` for entire read-modify-write. Load all terms, replace entries of given `TermType` with `newEntries`, preserve entries of other type, save atomically.
- `RemoveCache(gameId)` — evict from `ConcurrentDictionary`
- `ImportFromGameAsync(targetGameId, sourceGameId)` — load both, dedup by `Original`, append new entries from source to target, save target. Return `(added, skipped)` counts.

Constructor DI: `AppDataPaths`, `ILogger<TermService>`

- [ ] **Step 2: Verify build**

Run: `dotnet build XUnityToolkit-WebUI/XUnityToolkit-WebUI.csproj -p:SkipFrontendBuild=true`
Expected: Build succeeds

- [ ] **Step 3: Commit**

```bash
git add XUnityToolkit-WebUI/Services/TermService.cs
git commit -m "feat: 添加 TermService 统一术语服务（含数据迁移）"
```

---

## Task 3: TermMatchingService — Term Matching + ExactMatch

**Files:**
- Create: `XUnityToolkit-WebUI/Services/TermMatchingService.cs`

- [ ] **Step 1: Create `TermMatchingService.cs`**

Responsibilities:
- `FindMatchedTerms(List<TermEntry> allTerms, List<string> sourceTexts)` — scan all source texts, return subset of terms that match at least one source text. Sort result by `Priority` descending, then `Original.Length` descending.
- `BuildExactMatchPattern(string original)` — for `ExactMatch=true`, build regex: if `original` contains CJK chars, use `(?<![\\p{IsCJKUnifiedIdeographs}\\p{IsHiragana}\\p{IsKatakana}])` + `Regex.Escape(original)` + negative lookbehind/lookahead. If Western-only, use `\\b` + `Regex.Escape(original)` + `\\b`.
- `EstimateTokenCount(string text)` — rough estimate: count CJK chars as 1 token each, Western words as 1.3 tokens each. Used for token budget decisions.
- `EstimateTermTokens(List<TermEntry> terms)` — estimate total tokens for matched terms when injected into prompt (~20 tokens per entry).

No external dependencies besides `ILogger`.

- [ ] **Step 2: Verify build**

Run: `dotnet build XUnityToolkit-WebUI/XUnityToolkit-WebUI.csproj -p:SkipFrontendBuild=true`

- [ ] **Step 3: Commit**

```bash
git add XUnityToolkit-WebUI/Services/TermMatchingService.cs
git commit -m "feat: 添加 TermMatchingService 术语匹配与精确匹配逻辑"
```

---

## Task 4: TermAuditService — Translation Audit

**Files:**
- Create: `XUnityToolkit-WebUI/Services/TermAuditService.cs`

- [ ] **Step 1: Create `TermAuditService.cs`**

Key method:
- `AuditTranslation(string translatedText, List<TermEntry> matchedTerms)` → `TermAuditResult`
  - For each matched term (skip `IsRegex=true`):
    - `Translate`: check `translatedText.Contains(term.Translation)` (ordinal or case-insensitive based on `CaseSensitive`)
    - `DoNotTranslate`: check `translatedText.Contains(term.Original)` (respecting `CaseSensitive`)
  - Return: `{ Passed: bool, FailedTerms: List<TermEntry> }`

Record type:
```csharp
public sealed record TermAuditResult(bool Passed, List<TermEntry> FailedTerms);
```

- [ ] **Step 2: Verify build**

Run: `dotnet build XUnityToolkit-WebUI/XUnityToolkit-WebUI.csproj -p:SkipFrontendBuild=true`

- [ ] **Step 3: Commit**

```bash
git add XUnityToolkit-WebUI/Services/TermAuditService.cs
git commit -m "feat: 添加 TermAuditService 术语审查服务"
```

---

## Task 5: API Endpoints

**Files:**
- Modify: `XUnityToolkit-WebUI/Endpoints/GameEndpoints.cs` (lines 651-723 for old endpoints)

- [ ] **Step 1: Add new `/terms` endpoints**

Add after the existing glossary/DNT endpoints (around line 723):

- `GET /api/games/{id}/terms` → `termService.GetAsync(id)`; return `List<TermEntry>`
- `PUT /api/games/{id}/terms` → validate (max 10000, regex 1s timeout), `termService.SaveAsync(id, entries)`; return `ApiResult.Ok()`
- `POST /api/games/{id}/terms/import-from-game` → body `{ sourceGameId }`, call `termService.ImportFromGameAsync(id, sourceGameId)`; return `ApiResult<ImportResult>.Ok(new { added, skipped })`

Inject `TermService` into the endpoint mapping method.

- [ ] **Step 2: Convert old glossary/DNT endpoints to compat proxies**

Replace the existing 4 endpoints (lines 651-723) to route through `TermService`:

- `GET /glossary`: `termService.GetAsync(id)` → filter `Type == Translate` → map to old `GlossaryEntry` shape
- `PUT /glossary`: load all terms via `termService.GetAsync(id)`, replace `Translate` entries with submitted ones (converted from `GlossaryEntry` → `TermEntry`), preserve `DoNotTranslate` entries, `termService.SaveAsync`. Must acquire `_lock` — call a new `TermService.ReplaceByTypeAsync(gameId, TermType.Translate, newEntries)` method.
- `GET /do-not-translate`: similar filter for `DoNotTranslate`
- `PUT /do-not-translate`: similar replace logic for `DoNotTranslate` type

- [ ] **Step 3: Update game deletion handler**

In `DELETE /api/games/{id}` (around line 446):
- Add: delete glossary file `glossaryPath = Path.Combine(paths.GlossariesDirectory, $"{id}.json")` + `File.Delete` (if exists)
- Add: delete old DNT file `dntPath = Path.Combine(paths.DoNotTranslateDirectory, $"{id}.json")` + `File.Delete` (if exists, may not yet be migrated)
- Add: also delete `.migrated` file if exists
- Add: `termService.RemoveCache(id)`
- Remove: old `dntService.RemoveCache(id)` calls
- **Bug fix:** current code never deletes glossary files — this fixes the orphaned file bug

- [ ] **Step 4: Verify build**

Run: `dotnet build XUnityToolkit-WebUI/XUnityToolkit-WebUI.csproj -p:SkipFrontendBuild=true`

- [ ] **Step 5: Commit**

```bash
git add XUnityToolkit-WebUI/Endpoints/GameEndpoints.cs
git commit -m "feat: 添加统一术语 API 端点及兼容代理"
```

---

## Task 6: Update DI, Settings, Stats Models

**Files:**
- Modify: `XUnityToolkit-WebUI/Program.cs` (lines 140-141)
- Modify: `XUnityToolkit-WebUI/Models/AiTranslationSettings.cs`
- Modify: `XUnityToolkit-WebUI/Models/TranslationStats.cs`

- [ ] **Step 1: Update DI in `Program.cs`**

Replace lines 140-141:
```csharp
// Old:
builder.Services.AddSingleton<GlossaryService>();
builder.Services.AddSingleton<DoNotTranslateService>();

// New:
builder.Services.AddSingleton<TermService>();
builder.Services.AddSingleton<TermMatchingService>();
builder.Services.AddSingleton<TermAuditService>();
```

Keep old service registrations temporarily if other files still reference them — remove after Task 7 and Task 8 update all callers.

- [ ] **Step 2: Add settings fields to `AiTranslationSettings.cs`**

Add two new properties:
```csharp
public bool TermAuditEnabled { get; set; } = true;
public bool NaturalTranslationMode { get; set; } = true;
```

- [ ] **Step 3: Add stats fields to `TranslationStats.cs`**

**IMPORTANT:** `TranslationStats` is a positional record — all fields are constructor parameters. To avoid updating every construction call site, add the new fields as **non-positional properties with defaults** (after the closing `)` and before the `;`):

```csharp
public sealed record TranslationStats(
    long TotalTranslated,
    // ... existing positional params ...
    string? CurrentGameId
)
{
    public int TermAuditPhase1PassCount { get; init; }
    public int TermAuditPhase2PassCount { get; init; }
    public int TermAuditForceCorrectedCount { get; init; }
}
```

This way existing constructors don't break. In `LlmTranslationService` where stats are built, use `with { TermAuditPhase1PassCount = N }` to set the new fields.

- [ ] **Step 4: Verify build**

Run: `dotnet build XUnityToolkit-WebUI/XUnityToolkit-WebUI.csproj -p:SkipFrontendBuild=true`

- [ ] **Step 5: Commit**

```bash
git add XUnityToolkit-WebUI/Program.cs XUnityToolkit-WebUI/Models/AiTranslationSettings.cs XUnityToolkit-WebUI/Models/TranslationStats.cs
git commit -m "feat: 更新 DI 注册、添加术语审查设置和统计字段"
```

---

## Task 7: Update GlossaryExtractionService

**Files:**
- Modify: `XUnityToolkit-WebUI/Services/GlossaryExtractionService.cs`

- [ ] **Step 1: Replace constructor dependencies**

Change constructor (lines 11-17):
```csharp
// Old:
GlossaryService glossaryService,
DoNotTranslateService doNotTranslateService,

// New:
TermService termService,
```

- [ ] **Step 2: Update all method calls**

- Wherever `glossaryService.GetAsync(gameId)` is called → `termService.GetAsync(gameId)` then filter `.Where(t => t.Type == TermType.Translate)`
- Wherever `doNotTranslateService.GetAsync(gameId)` is called → same `GetAsync` then filter `.Where(t => t.Type == TermType.DoNotTranslate)`
- `glossaryService.MergeAsync(gameId, newEntries)` → convert `GlossaryEntry` to `TermEntry` (Type=Translate), call `termService.MergeAsync(gameId, newTermEntries)`
- Update `BuildExtractionSystemPrompt` to accept `List<TermEntry>` (filter by type internally)

- [ ] **Step 3: Verify build**

Run: `dotnet build XUnityToolkit-WebUI/XUnityToolkit-WebUI.csproj -p:SkipFrontendBuild=true`

- [ ] **Step 4: Commit**

```bash
git add XUnityToolkit-WebUI/Services/GlossaryExtractionService.cs
git commit -m "refactor: 将 GlossaryExtractionService 迁移到 TermService"
```

---

## Task 8: Multi-Phase Translation Pipeline

**Files:**
- Modify: `XUnityToolkit-WebUI/Services/LlmTranslationService.cs`

This is the most complex task. Modify `TranslateAsync` and related methods.

- [ ] **Step 1: Replace service dependencies**

In constructor/fields, replace `GlossaryService` + `DoNotTranslateService` with `TermService`, `TermMatchingService`, `TermAuditService`.

- [ ] **Step 2: Refactor `TranslateAsync` — term loading and matching**

At the beginning of `TranslateAsync` (around lines 173-187 where glossary/DNT are loaded):
- Call `termService.GetAsync(gameId)` to get all terms
- Call `termMatchingService.FindMatchedTerms(allTerms, sourceTexts)` to get matched subset
- Split matched terms: `translateTerms = matched.Where(t.Type == Translate)`, `dntTerms = matched.Where(t.Type == DoNotTranslate)`

- [ ] **Step 3: Implement Phase 1 — Natural Translation**

Before the existing placeholder logic, add Phase 1:
- Check `settings.NaturalTranslationMode` is true
- Check token budget: `termMatchingService.EstimateTermTokens(matchedTerms)` ≤ 70% of remaining context
- If both pass: build prompt with matched terms injected (structured format), send source texts unmodified to LLM
- After LLM returns: if `settings.TermAuditEnabled`, run `termAuditService.AuditTranslation()` per segment
- Collect passed and failed segments separately
- Update stats: `stats.TermAuditPhase1PassCount += passedCount`

- [ ] **Step 4: Implement Phase 2 — Placeholder Fallback**

For segments that failed Phase 1 audit (or all segments if Phase 1 was skipped):
- Refactor existing `ApplyGlossaryReplacements` + `ApplyDoNotTranslateReplacements` to work with `List<TermEntry>` sorted by priority
- Apply `ExactMatch` logic via `TermMatchingService.BuildExactMatchPattern` when `ExactMatch=true`
- Apply `CaseSensitive` to glossary replacements (new — currently hardcoded ordinal)
- Re-batch failed segments into one LLM call
- Post-processing: glossary restore → glossary post-process → DNT restore (order preserved)
- Pre-computed placeholder optimization preserved
- **Token budget truncation:** if even placeholder-mode terms exceed remaining context budget, truncate matched terms by `Priority` descending — only apply highest-priority terms that fit
- If `settings.TermAuditEnabled`: audit Phase 2 results
- Update stats: `stats.TermAuditPhase2PassCount += passedCount`

- [ ] **Step 5: Implement Phase 3 — Force Correction**

For segments that failed Phase 2 audit:
- `Translate` entries: `string.Replace(term.Original, term.Translation)` on the Phase 2 output
- `DoNotTranslate` entries: log warning if original is missing (cannot recover)
- Update stats: `stats.TermAuditForceCorrectedCount += count`

- [ ] **Step 6: Update `BuildSystemPrompt`**

Modify `BuildSystemPrompt` (lines 949-992) to accept `List<TermEntry>` instead of separate glossary/DNT lists. Format:
- `Translate` entries: `原文 → 译文 (描述)` or regex format
- `DoNotTranslate` entries: listed under "禁止翻译" section

For Phase 1 (natural translation): inject only matched terms.
For Phase 2 (placeholder): inject placeholder hints only (no term list in prompt).

- [ ] **Step 7: Verify build**

Run: `dotnet build XUnityToolkit-WebUI/XUnityToolkit-WebUI.csproj -p:SkipFrontendBuild=true`

- [ ] **Step 8: Commit**

```bash
git add XUnityToolkit-WebUI/Services/LlmTranslationService.cs
git commit -m "feat: 实现多阶段翻译管线（自然翻译→占位符→强制修正）+ 术语审查"
```

---

## Task 9: Delete Old Models and Services

**Files:**
- Delete: `XUnityToolkit-WebUI/Models/GlossaryEntry.cs`
- Delete: `XUnityToolkit-WebUI/Models/DoNotTranslateEntry.cs`
- Delete: `XUnityToolkit-WebUI/Services/GlossaryService.cs`
- Delete: `XUnityToolkit-WebUI/Services/DoNotTranslateService.cs`
- Modify: `XUnityToolkit-WebUI/Program.cs` — remove old DI registrations if still present

- [ ] **Step 1: Delete old files**

Delete the 4 files listed above. Remove any remaining old DI registrations from `Program.cs`.

- [ ] **Step 2: Fix any remaining compilation errors**

Search for any remaining references to `GlossaryEntry`, `DoNotTranslateEntry`, `GlossaryService`, `DoNotTranslateService` across the entire backend. Fix any missed references.

Check: `Endpoints/SettingsEndpoints.cs` for export/import — if it references old models/services, update to use `TermService`.

- [ ] **Step 3: Verify build**

Run: `dotnet build XUnityToolkit-WebUI/XUnityToolkit-WebUI.csproj -p:SkipFrontendBuild=true`

- [ ] **Step 4: Commit**

```bash
git add -A
git commit -m "refactor: 删除旧的 GlossaryEntry/DoNotTranslateEntry 模型和服务"
```

---

## Task 10: Frontend Types and API

**Files:**
- Modify: `XUnityToolkit-Vue/src/api/types.ts` (lines 308-318)
- Modify: `XUnityToolkit-Vue/src/api/games.ts` (lines 57-63)

- [ ] **Step 1: Update `types.ts`**

Replace `GlossaryEntry` and `DoNotTranslateEntry` interfaces with:

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

Add to `AiTranslationSettings` interface:
```typescript
termAuditEnabled: boolean
naturalTranslationMode: boolean
```

Add to `TranslationStats` interface:
```typescript
termAuditPhase1PassCount: number
termAuditPhase2PassCount: number
termAuditForceCorrectedCount: number
```

- [ ] **Step 2: Update `games.ts` API methods**

Replace old glossary/DNT methods with:
```typescript
getTerms: (id: string) => api.get<TermEntry[]>(`/api/games/${id}/terms`),
saveTerms: (id: string, entries: TermEntry[]) => api.put(`/api/games/${id}/terms`, entries),
importTermsFromGame: (id: string, sourceGameId: string) =>
  api.post<{ added: number; skipped: number }>(`/api/games/${id}/terms/import-from-game`, { sourceGameId }),
```

Keep old `getGlossary`/`saveGlossary`/`getDoNotTranslate`/`saveDoNotTranslate` temporarily for `TranslationEditorView` compat (these hit the compat proxy endpoints).

- [ ] **Step 3: Verify frontend types**

Run: `cd XUnityToolkit-Vue && npx vue-tsc --noEmit`
Expected: May have errors in `GlossaryEditorView.vue` (will be replaced) and `TranslationEditorView.vue` (uses old types). Fix `TranslationEditorView` references if needed, or keep old types aliased.

- [ ] **Step 4: Commit**

```bash
git add XUnityToolkit-Vue/src/api/types.ts XUnityToolkit-Vue/src/api/games.ts
git commit -m "feat: 添加前端 TermEntry 类型和 API 方法"
```

---

## Task 11: Frontend TermEditorView

**Files:**
- Create: `XUnityToolkit-Vue/src/views/TermEditorView.vue`
- Delete: `XUnityToolkit-Vue/src/views/GlossaryEditorView.vue`
- Modify: `XUnityToolkit-Vue/src/router/index.ts` (lines 38-40)
- Modify: `XUnityToolkit-Vue/src/views/GameDetailView.vue` (line 928)

This is the largest frontend task. Build the unified term editor.

- [ ] **Step 1: Create `TermEditorView.vue` with basic structure**

Use `<script setup lang="ts">` + `<style scoped>`. Include:
- Imports: NButton, NIcon, NInput, NDataTable, NSwitch, NTag, NSpin, NEmpty, NSelect, NInputNumber, NPopover, NCheckbox, NModal, useMessage, useDialog
- State: `entries` ref (TermEntry + _id), `loading`, `game`, filter state, search state, column visibility state
- Load data via `gamesApi.getTerms(gameId)` on mount
- Single `useAutoSave` instance (2s debounce, deep watch)

- [ ] **Step 2: Implement filter chip bar**

- `activeTypeFilter`: `'all' | 'translate' | 'doNotTranslate'`
- `activeCategoryFilter`: `TermCategory | null`
- Computed `filteredEntries`: apply type filter → category filter → search keyword
- Computed counts per type and category from unfiltered `entries`
- Render chips using styled `<span>` elements with click handlers and active state

- [ ] **Step 3: Implement data table with inline editing**

NDataTable columns defined in `computed`:
- Drag handle column (24px) — render `⋮⋮` text (move-up/move-down buttons in a later step)
- Type column (60px) — NTag with click-to-toggle, blue for translate, orange/warning for DNT. **On toggle to DNT:** clear translation field and disable it. **On toggle to translate:** re-enable translation field.
- Original column (flex, minWidth: 140) — NInput inline edit
- Translation column (flex, minWidth: 140) — NInput, disabled when type=doNotTranslate (show "—" placeholder)
- Category column (90px) — NSelect with category options
- Regex column (50px) — NSwitch
- ExactMatch column (50px) — NSwitch
- Delete column (32px) — delete button

Hidden columns (controlled by visibility state):
- Description (flex) — NInput
- CaseSensitive (50px) — NSwitch
- Priority (60px) — NInputNumber

Use `virtual-scroll`, `size="small"`, `striped`, `:max-height="560"`, `:row-key="(row) => row._id"`.

- [ ] **Step 4: Implement column visibility control**

Gear button in toolbar → NPopover with NCheckbox list for toggling hidden columns.
Store preferences in `localStorage` key `term-editor-columns`.

- [ ] **Step 5: Implement add entry row**

- Type select (NSelect: 翻译术语/禁止翻译)
- Original input (NInput)
- Translation input (NInput, disabled when type=doNotTranslate)
- Category select (NSelect)
- Add button
- Enter key support on inputs
- Duplicate check by `original`

- [ ] **Step 6: Implement import/export**

Import:
- Hidden `<input type="file" accept=".json,.csv,.tsv">`
- JSON: parse, auto-detect old `GlossaryEntry[]` format (has `isRegex` but no `type`) → convert; auto-detect old `DoNotTranslateEntry[]` format (has `caseSensitive` but no `type`/`translation`) → convert
- CSV/TSV: parse header row, match columns by name (order-independent), auto-detect delimiter by extension (`.csv` → comma, `.tsv` → tab). Column names: `type, original, translation, category, description, isRegex, caseSensitive, exactMatch, priority`
- Merge: skip duplicates by `original`

Export:
- NDropdown or dialog to choose JSON vs CSV
- JSON: `JSON.stringify` with indent
- CSV: header row + data rows, proper escaping (quote fields containing commas/newlines)
- **Filename:** `{gameName}_术语库.json` or `{gameName}_术语库.csv`

Import from game:
- NModal with game list (fetched via `gamesApi.list()`)
- On select: call `gamesApi.importTermsFromGame(gameId, sourceGameId)`
- Show result message

- [ ] **Step 7: Implement toolbar actions**

- Manual save button (disable auto-save during save, re-enable after)
- Clear all with NDialog confirmation
- Counts in header

- [ ] **Step 8: Implement move-up/move-down for priority ordering**

In the drag handle column, render two small arrow buttons (up/down) instead of a drag handle:
- Click up: swap with previous entry in array
- Click down: swap with next entry
- Disabled when filter/search is active

- [ ] **Step 9: Update router and navigation**

In `src/router/index.ts` (lines 37-42), replace the glossary-editor route:
```typescript
{
  path: '/games/:id/term-editor',
  name: 'term-editor',
  component: () => import('@/views/TermEditorView.vue'),
  meta: { depth: 3 },
},
{
  path: '/games/:id/glossary-editor',
  redirect: to => `/games/${to.params.id}/term-editor`,
},
```

In `GameDetailView.vue` (line 928), change `/glossary-editor` to `/term-editor`.

- [ ] **Step 10: Delete `GlossaryEditorView.vue`**

Remove the old file.

- [ ] **Step 11: Update `TranslationEditorView.vue`**

This view calls `gamesApi.getGlossary()` and `gamesApi.saveGlossary()` (using old `GlossaryEntry` type) to add glossary entries from the translation editor. Update to use the compat proxy return shape, or better yet, migrate to the new API:
- Replace `GlossaryEntry` with `TermEntry` type
- Replace `gamesApi.getGlossary()` with `gamesApi.getTerms()` (filter `type === 'translate'` if needed)
- Replace `gamesApi.saveGlossary()` with `gamesApi.saveTerms()`
- When adding a new entry from translation editor, set `type: 'translate'` and reasonable defaults for new fields

- [ ] **Step 12: Verify frontend build**

Run: `cd XUnityToolkit-Vue && npx vue-tsc --noEmit && npm run build`
Expected: Build succeeds

- [ ] **Step 13: Commit**

```bash
git add -A
git commit -m "feat: 实现统一术语编辑器 TermEditorView（替换 GlossaryEditorView）"
```

---

## Task 12: Frontend Settings & Stats Integration

**Files:**
- Modify: `XUnityToolkit-Vue/src/views/AiTranslationView.vue`
- Modify: `XUnityToolkit-Vue/src/views/SettingsView.vue`

- [ ] **Step 1: Add audit stats display to `AiTranslationView.vue`**

In the stats section, add a new row or card showing:
- Phase 1 pass count
- Phase 2 pass count
- Force corrected count
- Computed pass rate percentage

Reference the existing stats display pattern in the file.

- [ ] **Step 2: Add settings toggles to `SettingsView.vue`**

In the AI translation settings section, add:
- `NSwitch` for `termAuditEnabled` (术语审查)
- `NSwitch` for `naturalTranslationMode` (自然翻译模式)

Follow the sync point pattern (4 places per CLAUDE.md): `Models/AiTranslationSettings.cs` (done in Task 6), `src/api/types.ts` (done in Task 10), `AiTranslationView.vue` (`DEFAULT_AI_TRANSLATION` object — add `termAuditEnabled: true, naturalTranslationMode: true`), `SettingsView.vue` (add NSwitch toggles + `loadPreferences`/`savePreferences`).

- [ ] **Step 3: Verify frontend build**

Run: `cd XUnityToolkit-Vue && npx vue-tsc --noEmit && npm run build`

- [ ] **Step 4: Commit**

```bash
git add XUnityToolkit-Vue/src/views/AiTranslationView.vue XUnityToolkit-Vue/src/views/SettingsView.vue
git commit -m "feat: 添加术语审查统计显示和设置开关"
```

---

## Task 13: Update CLAUDE.md Files

**Files:**
- Modify: `CLAUDE.md` (root)
- Modify: `XUnityToolkit-WebUI/CLAUDE.md` (backend-specific)

- [ ] **Step 1: Update root `CLAUDE.md` sync points and documentation**

Changes needed:
- Replace all references to `GlossaryEntry`/`DoNotTranslateEntry` with `TermEntry`
- Replace `GlossaryService`/`DoNotTranslateService` with `TermService`
- Update API endpoints section: add new `/terms` endpoints, note compat proxies
- Update sync points: `TermEntry` fields sync 2 places (`Models/TermEntry.cs` ↔ `src/api/types.ts`)
- Update `AiTranslationSettings` sync point to include new fields
- Update `TranslationStats` sync point to include new fields
- Update input size limits: unified 10000 (was glossary 5000 + DNT 10000)
- Update placeholder substitution order docs to reflect priority-based ordering
- Add note about multi-phase translation pipeline
- Update `GlossaryEditorView` references to `TermEditorView`

- [ ] **Step 2: Update `XUnityToolkit-WebUI/CLAUDE.md`**

Update the backend CLAUDE.md:
- Replace `GlossaryService`/`DoNotTranslateService` references with `TermService`
- Update "Glossary Extraction" section to reference `TermService` and `TermEntry` model
- Update `GlossaryEntry model` description to `TermEntry model` with new fields
- Add notes about multi-phase pipeline in "AI Translation Context" section

- [ ] **Step 3: Commit**

```bash
git add CLAUDE.md XUnityToolkit-WebUI/CLAUDE.md
git commit -m "docs: 更新 CLAUDE.md 术语管理相关文档"
```

---

## Task 14: Settings Export/Import Compatibility

**Files:**
- Modify: `XUnityToolkit-WebUI/Endpoints/SettingsEndpoints.cs`

- [ ] **Step 1: Update export/import handlers**

Export: ensure the unified `glossaries/` directory is included (it already should be since it exists today).

Import: when importing a ZIP that contains an old `do-not-translate/` directory, do NOT extract it to the new data path — instead, extract the files temporarily, convert each `DoNotTranslateEntry` JSON to `TermEntry` format (using the new `TermEntry` model, not the deleted old model), merge into the corresponding `glossaries/{gameId}.json` file. If the ZIP already has `glossaries/` entries, those take priority. Parse old format manually via `JsonSerializer.Deserialize` with anonymous type or `JsonElement`.

- [ ] **Step 2: Verify build**

Run: `dotnet build XUnityToolkit-WebUI/XUnityToolkit-WebUI.csproj -p:SkipFrontendBuild=true`

- [ ] **Step 3: Commit**

```bash
git add XUnityToolkit-WebUI/Endpoints/SettingsEndpoints.cs
git commit -m "feat: 更新数据导入导出以支持统一术语格式"
```

---

## Task 15: Full Integration Verification

- [ ] **Step 1: Full backend build**

Run: `dotnet build XUnityToolkit-WebUI/XUnityToolkit-WebUI.csproj`
Expected: Succeeds (includes frontend build)

- [ ] **Step 2: Full frontend type check**

Run: `cd XUnityToolkit-Vue && npx vue-tsc --noEmit`
Expected: No errors

- [ ] **Step 3: Frontend production build**

Run: `cd XUnityToolkit-Vue && npm run build`
Expected: Succeeds

- [ ] **Step 4: Manual smoke test**

Start app: `dotnet run --project XUnityToolkit-WebUI/XUnityToolkit-WebUI.csproj`
Verify:
- Navigate to a game's term editor
- Add translate and DNT entries
- Filter by type and category
- Import/export JSON and CSV
- Check auto-save works
- Verify old glossary-editor URL redirects

- [ ] **Step 5: Clean up mockup file**

Delete `docs/mockup-glossary-approaches.html` (brainstorming artifact).

- [ ] **Step 6: Final commit**

```bash
git add -A
git commit -m "chore: 清理临时文件，完成统一术语管理系统"
```
