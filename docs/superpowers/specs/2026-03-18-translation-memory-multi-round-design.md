# Translation Memory, Dynamic Pattern Detection & Multi-Round Translation

**Date:** 2026-03-18
**Status:** Draft

## Problem Statement

Pre-translation cache is implemented on the XUnity plugin side using exact-match key lookup. When game dialogue contains dynamic player names (either template variables like `{PlayerName}` or hardcoded names), changing the player name invalidates all cached translations — the entire game needs re-translation. This is extremely inefficient.

## Goals

1. **Multi-layer defense** against cache misses caused by dynamic name changes
2. **Automatic detection** of dynamic text patterns (no manual user configuration)
3. **Translation Memory (TM)** on the toolkit side to reuse prior translations
4. **Auto term extraction** from translated texts via LLM
5. **Multi-round translation** pipeline: Round 1 = initial translation + term extraction, Round 2 = apply terms + polish

## Non-Goals

- Replacing XUnity's cache mechanism (we augment it, not replace it)
- Manual dynamic variable configuration UI (fully automatic)
- More than 2 translation rounds (diminishing returns)

## Architecture: Three Lines of Defense

```
Game Runtime Text (XUnity)
        │
        ▼
┌──────────────────────────────────┐
│ Defense 1: XUnity Regex Cache     │  ← _PreTranslated_Regex.txt
│ r:"(.+?)今天去了商店"=...          │     Template vars + LLM-detected patterns
│ Hit → use directly, no request    │
└──────────────────────────────────┘
        │ Miss
        ▼
┌──────────────────────────────────┐
│ POST /api/translate               │
│                                   │
│ Defense 2: Translation Memory     │  ← TranslationMemoryService
│ Edit distance comparison,         │     In-memory index + JSON persistence
│ differential segment replacement  │
│ Hit → return directly, no LLM    │
└──────────────────────────────────┘
        │ Miss
        ▼
┌──────────────────────────────────┐
│ Defense 3: Normal LLM Translation │  ← Existing LlmTranslationService
│ Translation result → write to TM  │     Results backfill TM
└──────────────────────────────────┘
```

## Defense 1: LLM-Assisted Template Mining + Regex Generation

### 1.1 Template Variable Auto-Detection (Zero Cost)

Before pre-translation begins, scan extracted texts with regex to detect template variables:
- Common patterns: `{PlayerName}`, `{PC}`, `<player>`, `%name%`, `[name]`
- Detected variables recorded in a per-game "dynamic variable table"
- These feed into both regex generation and translation memory normalization

### 1.2 LLM-Assisted Dynamic Fragment Identification (Batch, One-Time)

**Trigger:** Before pre-translation starts, as a new "analysis phase"

**Input:** Batches of extracted source texts (reuses existing LLM call infrastructure)

**Prompt design:**
> "Below are dialogue texts extracted from a game. Identify parts that may be dynamic variables (e.g., player names, custom character names that change based on player input). For each text, output the variable position and inferred variable type. Output null if no dynamic parts exist."

**Output format:**
```json
[
  { "index": 0, "text": "张三今天去了商店", "variables": [{"start": 0, "end": 2, "type": "playerName"}] },
  { "index": 1, "text": "欢迎来到冒险世界", "variables": null }
]
```

### 1.3 Regex Cache Generation

Based on detected dynamic fragments, generate XUnity `r:` regex entries for `_PreTranslated_Regex.txt`:

- Template variable: `{PlayerName}今天去了商店` translated as `{PlayerName} went to the store today`
  → `r:"(.+?)今天去了商店"=$1 went to the store today`
- LLM-identified: `张三今天去了商店` translated as `张三 went to the store today`
  → `r:"(.+?)今天去了商店"=$1 went to the store today`

**Regex generation rules:**
- Dynamic fragments replaced with `(.+?)` capture groups
- Remaining text escaped as regex literals
- Translation side uses `$1`, `$2` etc. backreferences at corresponding positions
- Texts with multiple dynamic fragments generate multi-capture-group regexes
- Safety constraint: regex must have at least 3 non-capture literal characters (prevents overly broad matching)

### 1.4 Data Storage

Analysis results persisted at `{dataRoot}/dynamic-patterns/{gameId}.json`:
```json
{
  "patterns": [
    {
      "originalTemplate": "(.+?)今天去了商店",
      "translatedTemplate": "$1 went to the store today",
      "variablePositions": [{"type": "playerName", "groupIndex": 1}],
      "source": "llm"
    }
  ]
}
```

## Defense 2: Translation Memory + Edit Distance Fuzzy Matching

### 2.1 TranslationMemoryService

**Data sources:**
- Pre-translation results (batch write after `PreTranslationService` completes)
- Runtime translation results (per-entry write after `LlmTranslationService.TranslateAsync`)

**In-memory structure:**
```csharp
ConcurrentDictionary<string, TranslationMemoryStore> _stores;

class TranslationMemoryStore
{
    // Exact match index: normalized original → translation
    Dictionary<string, string> ExactIndex;

    // Fuzzy match candidate pool: bucketed by text length
    Dictionary<int, List<TranslationMemoryEntry>> LengthBuckets;

    // Timestamp of last terms modification (for audit invalidation)
    DateTime LastTermsModified;
}

class TranslationMemoryEntry
{
    string Original;
    string Translation;
    string NormalizedKey;     // After NormalizeForCache processing
    int Length;               // Original text length (for bucketing)
    DateTime TranslatedAt;   // For term audit invalidation
    int Round;                // 1 = initial, 2 = polished
    bool IsFinal;             // true if from round 2 or single-round mode
}
```

**Persistence:** `{dataRoot}/translation-memory/{gameId}.json`, atomic write (write-to-temp + `File.Move`)

**Loading strategy:** Lazy load on first `POST /api/translate` (double-checked locking pattern, same as `PreTranslationCacheMonitor`)

**Size control:** LRU eviction when file exceeds 50MB, retaining most recently used entries

### 2.2 Matching Flow

For each text arriving at `POST /api/translate`, before LLM call:

```
For each text:
  1. NormalizeForCache(gameId, text) → normalizedText
  2. Exact match: ExactIndex[normalizedText]
     → hit: proceed to term audit check
  3. Dynamic pattern match: iterate dynamic-patterns/{gameId}.json regexes
     → hit: assemble translation via capture groups + translatedTemplate
     → proceed to term audit check
  4. Edit distance fuzzy match:
     a. Select adjacent length buckets (±20% of normalizedText.Length)
     b. Compute Levenshtein distance against bucket entries
     c. Best candidate must satisfy:
        - Similarity > 85% (1 - editDistance / maxLength)
        - Diff regions are contiguous (not scattered across multiple locations)
     d. Hit → extract diff segment, locate and replace in translation
     → proceed to term audit check
  5. All hits: TermAuditService.AuditTranslation() against current term table
     → audit pass: return TM result
     → audit fail: discard TM result, continue to Phase 1/2/3
```

### 2.3 Diff Segment Replacement Logic

When fuzzy match hits:

**Example:**
- TM stored: `张三今天去了商店` → `张三 went to the store today`
- New text: `李四今天去了商店`
- Diff result: positions 0-2 changed from `张三` to `李四`, rest identical

**Replacement strategy:**
1. Locate diff segment in source: `张三` → `李四`
2. Find old diff segment `张三` in translation (dynamic names typically preserved as-is)
3. Replace with new diff segment `李四`
4. Result: `李四 went to the store today`

**Edge case handling:**
- If old diff segment not found in translation (name was transliterated, e.g., `Zhang San`): discard fuzzy match, fall through to LLM
- If diff segments > 1 and total diff length > 30% of original: discard (too much change, not a simple name swap)
- If diff segment matches a known term/DNT entry: defer to term system

### 2.4 Term Audit Integration

TM hits must respect the latest term configuration:

- `TermService.SaveAsync()` records `lastTermsModified` timestamp
- TM entries carry `translatedAt` timestamp
- If `translatedAt < lastTermsModified`: force re-audit via `TermAuditService.AuditTranslation()`
- Audit fail → discard TM result, continue to normal LLM pipeline

### 2.5 Integration Point in Translation Pipeline

In `LlmTranslationService.TranslateAsync`, insert TM lookup **before Phase 1**:

```
TranslateAsync(texts):
  Phase 0 (NEW): TranslationMemory matching
    → hits marked as resolved, skip all subsequent phases
    → misses continue to Phase 1/2/3
  Phase 1: Natural Translation (existing)
  Phase 2: Placeholder Translation (existing)
  Phase 3: Force Correction (existing)

  All new translation results → write to TranslationMemory
```

## Multi-Round Translation Pipeline

### 3.1 Overview

Default: **2 rounds enabled** (can be disabled to 1 round via setting).

```
Extracted texts → Script tag cleaning → Template variable detection → LLM pattern analysis
  │
  ▼
┌──────────────────────────────────────────────┐
│ Round 1: Initial Translation + Term Extraction│
│                                               │
│ • Batch translation (existing Phase 1/2/3)    │
│ • LLM simultaneously extracts term candidates │
│   (character, location, item, skill, org)     │
│ • Extracted terms written to term table       │
│   (auto-marked source=AI, priority=0)         │
│ • Initial translations written to TM          │
└──────────────────────────────────────────────┘
  │
  ▼
┌──────────────────────────────────────────────┐
│ [Optional] User reviews extracted terms       │
│ • Frontend shows AI-extracted term candidates │
│ • User can accept/reject/modify each          │
│ • One-click accept-all / reject-all           │
│ • Skip = auto-apply AI results                │
└──────────────────────────────────────────────┘
  │
  ▼
┌──────────────────────────────────────────────┐
│ Round 2: Apply Terms + Polish & Proofread     │
│                                               │
│ • Apply round-1 extracted terms (full Phase)  │
│ • Prompt includes round-1 translation as ref  │
│ • LLM unifies terminology, polishes style,    │
│   corrects errors                             │
│ • Term audit ensures compliance               │
│ • Final translations overwrite TM             │
└──────────────────────────────────────────────┘
  │
  ▼
Write XUnity cache files + regex cache
```

### 3.2 Auto Term Extraction (TermExtractionService)

**Extraction timing:** After Round 1 translation completes, as a **separate dedicated LLM call** (not inline in translation prompt — avoids changing the critical translation output format).

**Flow:**
1. Round 1 translates texts normally (existing format, unchanged)
2. After Round 1, send source+translation pairs to LLM in batches for extraction
3. Extraction prompt asks for structured JSON output only

**Extraction prompt:**
> "Below are source texts and their translations from a game. Extract proper nouns and output as JSON array:
> `[{"original": "source term", "translation": "translated term", "category": "Character|Location|Item|Skill|Organization"}]`
> Only output the JSON array, nothing else."

**Output parsing:**
- Parse JSON array from LLM response
- Deduplicate candidates, merge with existing term table
- Auto-mark: `Source = TermSource.AI`, `Type = Translate`, `Priority = 0` (lower than user-added terms)
- **Fallback:** If extraction call fails or returns unparseable JSON, log warning and continue without extracted terms (non-blocking)

**Deduplication strategy:**
- If `Original` already exists in term table → skip (never overwrite user-configured terms)
- If same `Original` extracted multiple times with different `Translation` → use most frequent

**Storage:**
- Candidates first stored at `{dataRoot}/term-candidates/{gameId}.json`
- After user confirmation (or auto-apply mode) → merged into `{dataRoot}/glossaries/{gameId}.json`

### 3.3 Round 2 Prompt Design

Round 2 is not simple re-translation — it is **reference-based polishing**:

**System prompt:**
> "You are a translation proofreading expert. Below are initial translations of game texts. Polish them according to the term glossary.
> Requirements:
> 1. Strictly follow term translations in the glossary
> 2. Maintain semantic accuracy, correct obvious translation errors
> 3. Unify translations of the same character/location names
> 4. Maintain conversational tone appropriate for game dialogue
> 5. Preserve all format tags and variable placeholders"

**User prompt:**
> "Source: {original}
> Initial translation: {round1_translation}
> Output the polished final translation:"

### 3.4 Round 2 Optimization

Not all texts need Round 2. Skip Round 2 for texts where:
- Round 1 passed term audit with 100% compliance
- No extracted terms are relevant to this text
- Text is a single word or very short (< 5 characters)

This reduces LLM costs — typically only 30-60% of texts need Round 2.

## API Endpoints

### New Endpoints

```
# Translation Memory management
GET    /api/games/{id}/translation-memory/stats     # TM statistics (entry count, hit rates)
DELETE /api/games/{id}/translation-memory            # Clear game's TM

# Dynamic pattern management
GET    /api/games/{id}/dynamic-patterns              # View identified dynamic patterns
DELETE /api/games/{id}/dynamic-patterns              # Clear, re-analyze on next pre-translation

# Term extraction candidates
GET    /api/games/{id}/term-candidates               # View AI-extracted term candidates
POST   /api/games/{id}/term-candidates/apply         # Apply selected candidates to term table
DELETE /api/games/{id}/term-candidates                # Discard candidates
```

### Modified Endpoints

- `POST /api/games/{id}/pre-translate` — new phases added (pattern analysis → round 1 → term review → round 2)
- `GET /api/translate/stats` — new fields for TM hit/miss counts

## Configuration

New fields in `AiTranslationSettings`:

```csharp
// Translation Memory
bool EnableTranslationMemory { get; set; } = true;
int FuzzyMatchThreshold { get; set; } = 85;          // 0-100 similarity threshold

// LLM Pattern Analysis (pre-translation phase)
bool EnableLlmPatternAnalysis { get; set; } = true;

// Multi-Round Translation (default ON = 2 rounds)
bool EnableMultiRoundTranslation { get; set; } = true;

// Auto Term Extraction
bool EnableAutoTermExtraction { get; set; } = true;
bool AutoApplyExtractedTerms { get; set; } = false;   // false = user review required
```

**Sync points:** `AiTranslationSettings` fields must sync across `Models/AiTranslationSettings.cs`, `src/api/types.ts`, `AiTranslationView.vue` (defaults), `SettingsView.vue`.

## Pre-Translation Flow Changes

**Existing flow:**
```
Extract texts → Script tag cleaning → Batch LLM translation → Write cache files
```

**New flow:**
```
Extract texts → Script tag cleaning
  → [NEW] Template variable detection (zero cost, regex scan)
  → [NEW] LLM dynamic fragment analysis (batch, optional)
  → [NEW] Round 1: Batch LLM translation + auto term extraction
  → [NEW] Term candidate review (optional, if AutoApplyExtractedTerms=false)
  → [NEW] Round 2: Apply terms + polish (if EnableMultiRoundTranslation=true)
  → Write cache files
  → [NEW] Generate dynamic regex → _PreTranslated_Regex.txt
  → [NEW] Batch write to Translation Memory
```

## Runtime Translation Flow Changes

**Existing `POST /api/translate` flow:**
```
Text arrives → PreTranslationCacheMonitor stats → LLM translation (Phase 1/2/3)
```

**New flow:**
```
Text arrives → PreTranslationCacheMonitor stats
  → [NEW] Phase 0: TranslationMemoryService lookup
      Exact match → Dynamic pattern match → Edit distance fuzzy match
      → Term audit check on all hits
  → Misses continue to Phase 1/2/3
  → [NEW] Translation results → write to TM
```

## Statistics Extension

`TranslationStats` new fields:
```csharp
int TranslationMemoryHits { get; set; }          // TM exact hits
int TranslationMemoryFuzzyHits { get; set; }     // TM fuzzy hits
int TranslationMemoryPatternHits { get; set; }   // TM dynamic pattern hits
int TranslationMemoryMisses { get; set; }        // TM misses
int DynamicPatternCount { get; set; }            // Identified dynamic patterns
int ExtractedTermCount { get; set; }             // AI-extracted terms
```

**Sync points:** `TranslationStats` fields must sync across `Models/TranslationStats.cs`, `src/api/types.ts`, `AiTranslationView.vue`.

## SignalR Events

New events (see "SignalR Group Assignments" section below for group details):
- `translationMemoryHit` — broadcast on TM hit (for real-time frontend stats) → `ai-translation` group
- `patternAnalysisProgress` — LLM pattern analysis progress → `pre-translation-{gameId}` group
- `termExtractionComplete` — term extraction finished, candidates ready for review → `pre-translation-{gameId}` group
- `roundProgress` — which round is currently executing (round 1/2) → `pre-translation-{gameId}` group

## Frontend Changes

### AI Translation Page (AiTranslationView.vue)
- Stats area: new "Translation Memory" card showing TM entry count, exact/fuzzy/pattern hits, hit rate
- Recent translation list: `RecentTranslation` entries tagged with source (`LLM` / `TM Exact` / `TM Fuzzy` / `Dynamic Pattern`)

### Pre-Translation Panel
- Multi-step progress display:
  ```
  [Pattern Analysis] → [Round 1: Translate + Extract Terms] → [Term Review] → [Round 2: Polish] → [Write Cache]
       ✓ Done              ██████░░ 60%                       Waiting          Pending           Pending
  ```
- Term review modal (when `AutoApplyExtractedTerms=false`):
  - List of AI-extracted term candidates
  - Each: ✓ Accept / ✗ Reject / ✏️ Edit translation
  - One-click accept-all / reject-all
  - Confirm → proceed to Round 2

### Settings Page (SettingsView.vue)
- AI Translation settings: Translation Memory toggle, fuzzy match threshold slider, LLM pattern analysis toggle
- Multi-round translation toggle, auto term extraction toggle, auto-apply toggle

## Per-Game Data Cleanup

`DELETE /api/games/{id}` must additionally delete:
- `{dataRoot}/translation-memory/{gameId}.json`
- `{dataRoot}/dynamic-patterns/{gameId}.json`
- `{dataRoot}/term-candidates/{gameId}.json`

## Error Handling

| Scenario | Handling |
|----------|----------|
| TM file corrupt / read failure | Log warning, clear in-memory cache, degrade to no-TM mode |
| LLM pattern analysis failure | Skip analysis phase, continue pre-translation normally (non-blocking) |
| Fuzzy match replacement produces anomalous result | Discard result, fall through to normal LLM translation |
| TM file exceeds 50MB | LRU eviction of oldest entries |
| Generated regex too broad (`(.+?)` matches entire text) | Minimum 3 non-capture literal characters required |
| Term extraction JSON parse failure | Log warning, skip extraction for that batch, continue translation |
| Round 2 produces worse translation than Round 1 | Term audit catches regressions; if Round 2 fails audit but Round 1 passed, keep Round 1 result |

## Testing Strategy

- **Edit distance algorithm:** Unit tests covering CJK/Japanese/English mixed text, varying lengths, multi-diff-segment scenarios
- **Diff segment replacement:** Unit tests for name at head/middle/tail, multiple names, transliterated names
- **Regex generation:** Unit tests verifying escaping correctness, multi-capture groups, XUnity format compatibility
- **Term extraction parsing:** Unit tests for LLM output parsing, deduplication, merge with existing terms
- **Multi-round pipeline:** Integration tests simulating full flow — Round 1 → extract terms → Round 2 → verify term compliance
- **Integration test:** Simulate full flow — pre-translate → write TM → text with changed name arrives → verify fuzzy hit
- **Term audit on TM hits:** Test that term table changes correctly invalidate stale TM entries

## New Services

| Service | Responsibility | DI Lifetime |
|---------|----------------|-------------|
| `TranslationMemoryService` | TM storage, loading, exact/fuzzy/pattern matching, persistence | `AddSingleton` |
| `DynamicPatternService` | Template variable detection, LLM pattern analysis, regex generation | `AddSingleton` |
| `TermExtractionService` | LLM-based term extraction, candidate management, merge with term table | `AddSingleton` |

All three services registered as singletons in `Program.cs`, consistent with existing services (e.g., `TermService`, `PreTranslationService`).

## Relationship to Existing GlossaryExtractionService

The codebase already has `GlossaryExtractionService` which performs **runtime** LLM-based term extraction from translated text pairs (called during AI translation toggle). The new `TermExtractionService` operates during **pre-translation batch mode** with a different approach (inline extraction in the translation prompt).

**Coexistence strategy:**
- `GlossaryExtractionService` continues to handle runtime extraction (when AI translation is toggled on)
- `TermExtractionService` handles pre-translation batch extraction (Round 1)
- Both write to the same term table via `TermService`, using the same deduplication logic (skip if `Original` already exists)
- No conflict: runtime extraction runs only during live translation; batch extraction runs only during pre-translation
- Long-term: consider consolidating shared extraction logic into a base class or shared utility

## Model Changes

### TermEntry — New `Source` Field

Add `Source` property to `TermEntry` to distinguish user-added vs AI-extracted terms:

```csharp
public enum TermSource { User, AI, Import }

// On TermEntry:
public TermSource Source { get; set; } = TermSource.User;
```

**Sync points for new field:**
- `Models/TermEntry.cs` — add property + enum
- `src/api/types.ts` — add TypeScript type
- `TermSource` enum uses PascalCase (default convention, no `CamelCaseJsonStringEnumConverter`)

## Infrastructure Changes

### AppDataPaths Additions

New properties and helpers in `AppDataPaths`:

```csharp
public string TranslationMemoryDirectory => Path.Combine(Root, "translation-memory");
public string DynamicPatternsDirectory => Path.Combine(Root, "dynamic-patterns");
public string TermCandidatesDirectory => Path.Combine(Root, "term-candidates");

public string TranslationMemoryFile(string gameId) => Path.Combine(TranslationMemoryDirectory, $"{gameId}.json");
public string DynamicPatternsFile(string gameId) => Path.Combine(DynamicPatternsDirectory, $"{gameId}.json");
public string TermCandidatesFile(string gameId) => Path.Combine(TermCandidatesDirectory, $"{gameId}.json");
```

All three directories registered in `EnsureDirectoriesExist()`.

### Export Exclusion

`translation-memory/` directory should be **excluded** from data export (`SettingsEndpoints.cs` `/export` endpoint) — it is regeneratable from retranslation. `dynamic-patterns/` and `term-candidates/` should also be excluded (regeneratable).

## Response Format Constraints

### `POST /api/translate` Returns Raw Response

`POST /api/translate` returns `TranslateResponse` directly (not wrapped in `ApiResult<T>`) because the TranslatorEndpoint DLL calls it directly. Phase 0 TM hits must produce the exact same `TranslateResponse` shape as LLM results — no special wrapper or different format.

## Term Extraction Output Format

### Separate LLM Call (Not Inline)

To avoid changing the critical translation output format, term extraction uses a **separate LLM call** after Round 1 translation completes (not inline in the translation prompt):

1. Round 1 translates texts normally (existing format, no output changes)
2. After Round 1, a dedicated extraction call sends source+translation pairs to the LLM
3. Extraction prompt asks for structured JSON output only
4. This avoids any risk of corrupting the translation response parsing

**Extraction prompt:**
> "Below are source texts and their translations from a game. Extract proper nouns and output as JSON array:
> `[{"original": "source term", "translation": "translated term", "category": "Character|Location|Item|Skill|Organization"}]`
> Only output the JSON array, nothing else."

**Fallback:** If extraction call fails or returns unparseable JSON, log warning and continue with Round 2 without extracted terms (non-blocking).

## Performance Considerations

### Fuzzy Match Budget

Edit distance computation against all entries in length buckets could be expensive for large TM stores. Mitigations:

1. **Length bucketing** reduces candidate pool (±20% range)
2. **Time budget:** If a length bucket has >500 entries, skip fuzzy matching for that text (fall through to LLM). Exact match and dynamic pattern match are always O(1)/O(N_patterns) and run regardless.
3. **Early termination:** Abort Levenshtein computation if partial distance already exceeds threshold
4. **Runtime vs batch:** Fuzzy matching runs in both runtime and pre-translation. For runtime `POST /api/translate`, the time budget is strict (skip if slow). For pre-translation batch, the budget can be relaxed.

### Expected Performance

- Exact match: O(1) hash lookup — microseconds
- Dynamic pattern match: O(N_patterns) regex — typically <100 patterns, sub-millisecond
- Fuzzy match: O(bucket_size × text_length) — bounded by 500-entry cap, typically <10ms
- Total Phase 0 overhead: <15ms per text, far less than LLM round-trip (seconds)

## Term Review UX During Pre-Translation

### Non-Blocking Review Flow

Pre-translation is a long-running background operation. The term review step is handled as follows:

1. Round 1 completes → extracted terms saved to `term-candidates/{gameId}.json`
2. `termExtractionComplete` SignalR event sent to `pre-translation-{gameId}` group
3. Pre-translation **pauses** and enters `AwaitingTermReview` status
4. **If user is present:** frontend shows term review modal, user reviews and confirms → `POST /api/games/{id}/term-candidates/apply` → Round 2 starts
5. **If user navigates away:** pre-translation auto-resumes after 5-minute timeout, auto-applying all extracted terms
6. **If `AutoApplyExtractedTerms=true`:** no pause, auto-apply immediately, proceed to Round 2

The `GET /api/games/{id}/pre-translate/status` response includes the `AwaitingTermReview` state so the frontend can detect and show the review UI when the user returns.

## SignalR Group Assignments

Corrected group assignments for new events:

| Event | Group | Reason |
|-------|-------|--------|
| `translationMemoryHit` | `ai-translation` | Runtime translation stats |
| `patternAnalysisProgress` | `pre-translation-{gameId}` | Pre-translation phase |
| `termExtractionComplete` | `pre-translation-{gameId}` | Pre-translation phase |
| `roundProgress` | `pre-translation-{gameId}` | Pre-translation phase |

## CLAUDE.md Updates Required

After implementation, update CLAUDE.md with:
- New service descriptions (`TranslationMemoryService`, `DynamicPatternService`, `TermExtractionService`)
- New data paths (`translation-memory/`, `dynamic-patterns/`, `term-candidates/`)
- New per-game cleanup items in `DELETE /api/games/{id}`
- New sync points for `AiTranslationSettings`, `TranslationStats`, `TermEntry.Source`
- New API endpoints
- Multi-round translation pipeline description
- Export exclusion list updates

## Data Files

| Path | Content |
|------|---------|
| `{dataRoot}/translation-memory/{gameId}.json` | Translation memory entries |
| `{dataRoot}/dynamic-patterns/{gameId}.json` | Detected dynamic text patterns |
| `{dataRoot}/term-candidates/{gameId}.json` | AI-extracted term candidates pending review |
