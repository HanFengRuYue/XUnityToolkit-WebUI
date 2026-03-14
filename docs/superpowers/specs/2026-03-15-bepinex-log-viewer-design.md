# BepInEx Log Viewer & AI Analysis

## Overview

Add a per-game sub-page for viewing BepInEx's `LogOutput.log`, with keyword search, log level filtering, syntax highlighting, file export, and one-click AI-powered diagnostics using the user's configured LLM providers.

## Architecture

### Backend

#### New Service: `BepInExLogService`

**Registered as:** `AddSingleton<BepInExLogService>()` in `Program.cs` (consistent with all other services in the project)

**Dependencies:** `GameLibraryService`, `AppSettingsService`, `LlmTranslationService`

**Responsibilities:**

- Read `{GamePath}/BepInEx/LogOutput.log` with `FileShare.ReadWrite` (game may be running)
- Parse log lines to extract level (Info/Warning/Error) for structured response; continuation lines (stack traces, multi-line messages without bracket prefix) inherit the level of the preceding line
- Call LLM for log analysis via `LlmTranslationService.CallLlmRawAsync(endpoint, systemPrompt, userContent, temperature, ct)` — this bypasses the translation semaphore (no contention with hot-path translation)
- Auto-select endpoint: `AiTranslationSettings.Endpoints.Where(e => e.Enabled && !string.IsNullOrWhiteSpace(e.ApiKey)).OrderByDescending(e => e.Priority).First()` — higher Priority value = preferred (consistent with `LlmTranslationService.CalculateScore`); works in both cloud and local mode (local LLM auto-registers as a Custom provider with Priority=8)

**Methods:**

- `ReadLogAsync(Game game)` — returns full log content + metadata (file size, last modified)
- `AnalyzeLogAsync(string logContent, ApiEndpointConfig endpoint, CancellationToken ct)` — builds diagnostic prompt, calls `LlmTranslationService.CallLlmRawAsync`, returns Markdown analysis report
- `ResolveEndpoint(AiTranslationSettings settings)` — selects best available endpoint (lowest Priority value = highest priority); returns `null` if none available

**LLM Call Strategy:**

- Invoke `LlmTranslationService.CallLlmRawAsync(endpoint, systemPrompt, userContent, 0.3, ct)` — same pattern as `GlossaryExtractionService.ExtractChunkAsync`
- If log exceeds ~4000 lines, truncate to the last 4000 lines before sending
- Single request, no batching needed (low-frequency operation)
- Uses named "LLM" HttpClient (120s timeout) — sufficient for single diagnostic call

**Diagnostic Prompt Design:**

Predefined Chinese system prompt instructing the AI to analyze:
1. Plugin loading status (BepInEx bootstrap, XUnity initialization)
2. Translation endpoint connection (`LLMTranslate` endpoint status)
3. Error/exception summary with severity assessment
4. Specific failure patterns (missing DLLs, version mismatches, config issues)
5. Actionable fix suggestions

Output format: Markdown with headings and bullet points.

#### New Endpoints: `BepInExLogEndpoints`

**Registration:** `app.MapBepInExLogEndpoints()` in `Program.cs`, using `MapGroup("/api/games/{id}/bepinex-log")` pattern (consistent with `FontReplacementEndpoints`).

| Method | Path | Returns | Notes |
|--------|------|---------|-------|
| `GET` | `/` | `ApiResult<BepInExLogResponse>` | Full log content + metadata |
| `GET` | `/download` | File download | Raw file, **not ApiResult** |
| `POST` | `/analyze` | `ApiResult<BepInExLogAnalysis>` | Markdown diagnostic report; empty request body `{}` |

**Models (sync: `Models/BepInExLog.cs` ↔ `src/api/types.ts`):**

```csharp
record BepInExLogResponse(string Content, long FileSize, DateTime LastModified);
record BepInExLogAnalysis(string Report, string EndpointName, DateTime AnalyzedAt);
```

**TypeScript types (add to `src/api/types.ts`):**

```typescript
interface BepInExLogResponse {
  content: string
  fileSize: number
  lastModified: string
}

interface BepInExLogAnalysis {
  report: string
  endpointName: string
  analyzedAt: string
}
```

**Error cases:**
- Game not found → 404
- BepInEx not installed / no LogOutput.log → 404 with message
- No configured AI endpoint → 400 with message (for analyze only)

### Frontend

#### New Page: `BepInExLogView.vue`

**Route:** `/games/:id/bepinex-log`, `meta: { depth: 3 }` in `router/index.ts` (lazy-loaded). Not KeepAlive-cached (depth 3).

**`defineOptions({ name: 'BepInExLogView' })` after imports.**

**Layout:**

```
┌─────────────────────────────────────────────────┐
│ ← Back    BepInEx Log                           │
├─────────────────────────────────────────────────┤
│ [Search...] [Level ▾] [Refresh] [Export] [AI ⚡]│
├─────────────────────────────────────────────────┤
│                                                 │
│  Log content area                               │
│  - Syntax highlighted by level                  │
│  - Error lines: red                             │
│  - Warning lines: yellow/orange                 │
│  - Info lines: default text color               │
│  - Continuation lines: inherit preceding level  │
│  - Keyword search highlights matches            │
│                                                 │
├─────────────────────────────────────────────────┤
│ AI Analysis Result (shown after clicking AI)    │
│ ┌─────────────────────────────────────────────┐ │
│ │ Markdown rendered diagnostic report         │ │
│ │ - Plugin status                             │ │
│ │ - Errors found                              │ │
│ │ - Suggestions                               │ │
│ └─────────────────────────────────────────────┘ │
└─────────────────────────────────────────────────┘
```

**Features:**

- **Keyword search:** Client-side text filtering, highlights matched text
- **Level filter:** Dropdown with All/Info/Warning/Error; client-side filtering on parsed log lines
- **Syntax highlighting:** CSS-based; parse each line's `[Level` bracket prefix, apply color classes; continuation lines (no bracket prefix) inherit preceding line's level
- **Refresh:** Re-fetch log from backend
- **Export:** Download via `GET /bepinex-log/download` using blob download pattern (`fetch` → `blob()` → `createObjectURL` → `a.click()` → `setTimeout(revokeObjectURL, 1000)`)
- **AI Analysis:** POST to analyze endpoint, show loading spinner, render Markdown result below log area

**BepInEx Log Line Format (typical):**

```
[Info   :   BepInEx] BepInEx 5.4.23.2 - unity-mono (win64)
[Warning: XUnity.AutoTranslator] Some warning message
[Error  :  MonoMod] Some error message
System.NullReferenceException: Object reference not set...
   at SomeClass.SomeMethod() in ...
```

Level is extracted from the bracket prefix for filtering and highlighting. Lines without bracket prefix are continuation lines.

**Markdown rendering:** Add `marked` npm package for rendering AI analysis output. Use `v-html` with `marked.parse(report)`. Sanitization is acceptable since content comes from our own LLM calls, not user input.

**API client functions (add to `src/api/games.ts`):**

```typescript
getBepInExLog: (id: string) => api.get<BepInExLogResponse>(`/api/games/${id}/bepinex-log`),
analyzeBepInExLog: (id: string) => api.post<BepInExLogAnalysis>(`/api/games/${id}/bepinex-log/analyze`),
// download uses blob pattern with raw fetch, not api helper
```

#### GameDetailView Entry Point

Add a button/card in `GameDetailView.vue`:
- Only visible when BepInEx is installed; update existing `hasBepInEx` computed to also include `PartiallyInstalled` state (currently only checks `BepInExOnly` and `FullyInstalled`), since `PartiallyInstalled` means BepInEx is present but XUnity is missing — exactly the debugging scenario where log viewing is most useful
- Navigates to `/games/:id/bepinex-log`
- Styled consistently with existing sub-page navigation buttons
- Animation delay: insert at appropriate position; shift ALL subsequent card delays by +0.05s

### Data Flow

```
User clicks "View BepInEx Log"
  → Router navigates to /games/:id/bepinex-log
  → BepInExLogView.vue mounts
  → GET /api/games/{id}/bepinex-log
  → BepInExLogService.ReadLogAsync(game)
  → Read {GamePath}/BepInEx/LogOutput.log (FileShare.ReadWrite)
  → Return BepInExLogResponse(content, size, lastModified)
  → Frontend parses lines, applies level colors, renders

User clicks "AI Analysis"
  → POST /api/games/{id}/bepinex-log/analyze
  → BepInExLogService.ResolveEndpoint selects best endpoint
  → Builds diagnostic prompt + truncated log content (last 4000 lines)
  → Calls LlmTranslationService.CallLlmRawAsync (no semaphore contention)
  → Returns BepInExLogAnalysis(markdownReport, endpointName, timestamp)
  → Frontend renders Markdown via marked.parse() in analysis panel

User clicks "Export"
  → GET /api/games/{id}/bepinex-log/download
  → Backend streams file with Content-Disposition
  → Browser saves LogOutput.log
```

### Integration Checklist

- [ ] `Program.cs`: Register `AddSingleton<BepInExLogService>()` in DI + `app.MapBepInExLogEndpoints()`
- [ ] `Models/BepInExLog.cs`: Add C# record types
- [ ] `src/api/types.ts`: Add TypeScript interfaces (sync with C# models)
- [ ] `src/api/games.ts`: Add API client functions
- [ ] `src/router/index.ts`: Add lazy route with `meta: { depth: 3 }`
- [ ] `GameDetailView.vue`: Update `hasBepInEx` to include `PartiallyInstalled`; add entry button (guarded by `hasBepInEx`), adjust animation delays
- [ ] `npm install marked` + `@types/marked` (dev dependency)
- [ ] CLAUDE.md: Update API Endpoints section: `- **BepInEx Log:** \`GET /api/games/{id}/bepinex-log\`, \`GET .../download\` (**not ApiResult**), \`POST .../analyze\``

### Dependencies

- **Backend:** `GameLibraryService`, `AppSettingsService`, `LlmTranslationService` (for `CallLlmRawAsync`)
- **Frontend:** Naive UI components (NInput, NSelect, NButton, NSpin), `marked` (new npm dependency for Markdown rendering)

### Out of Scope

- Real-time log streaming (LogOutput.log is a static file, not streamed)
- Multi-turn AI conversation about the log
- Log persistence/history across game sessions
- Parsing non-BepInEx log formats
- Dedicated AI endpoint configuration for log analysis (reuses translation endpoints)
- Cancel button for AI analysis (single LLM call, 120s timeout is acceptable)
