# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

XUnityToolkit-WebUI is a web-based tool for one-click installation of XUnity.AutoTranslator and BepInEx plugins into Unity games. It features a Vue 3 frontend served by an ASP.NET Core backend.

## Build Commands

```bash
# Build backend (also builds frontend automatically via MSBuild Target)
dotnet build XUnityToolkit-WebUI/XUnityToolkit-WebUI.csproj

# Run backend (serves the web UI on http://127.0.0.1:51821)
dotnet run --project XUnityToolkit-WebUI/XUnityToolkit-WebUI.csproj

# One-click release build (self-contained single-file, win-x64 + win-arm64)
.\build.ps1                    # both architectures
.\build.ps1 -Runtime win-x64  # x64 only

# Build frontend (output to XUnityToolkit-WebUI/wwwroot/)
cd XUnityToolkit-Vue && npm run build

# Build TranslatorEndpoint (LLMTranslate.dll)
dotnet build TranslatorEndpoint/TranslatorEndpoint.csproj -c Release

# Frontend dev server (proxies API to backend)
cd XUnityToolkit-Vue && npm run dev

# Type-check frontend
cd XUnityToolkit-Vue && npx vue-tsc --noEmit
```

## Architecture

- **Backend:** ASP.NET Core Minimal API (.NET 10.0, Windows Forms for native dialogs)
- **Frontend:** Vue 3 + TypeScript + Naive UI + Pinia (in `XUnityToolkit-Vue/`)
- **Real-time:** SignalR via single `InstallProgressHub` (groups: `game-{id}` for install progress, `ai-translation` for stats, `logs` for real-time log streaming)
- **Persistence:** JSON files in program directory (`library.json`, `settings.json`) — portable, survives OS reinstall
- **System Tray:** NotifyIcon on dedicated STA thread; auto-opens browser, hides console on startup
- **TranslatorEndpoint:** net35 class library producing `LLMTranslate.dll` — XUnity.AutoTranslator custom endpoint forwarding game text to `POST /api/translate`; hand-rolled JSON (no System.Text.Json in Unity Mono); configurable via `[LLMTranslate]` INI section: `MaxConcurrency` (10), `MaxTranslationsPerRequest` (10), `DebugMode`, `GameId`, `DisableSpamChecks` (true), `TranslationDelay` (0.1s)
- **AI Translation:** `LlmTranslationService` calls LLM APIs (OpenAI/Claude/Gemini/DeepSeek/Qwen/GLM/Kimi/Custom); multi-provider load balancing with priority + speed/error scoring; per-text parallel calls bounded by `SemaphoreSlim`; per-game glossary at `{programDir}/glossaries/{gameId}.json`; real-time stats via SignalR

### Frontend Structure

```
XUnityToolkit-Vue/src/
├── api/            # API client and TypeScript types
├── assets/         # Global CSS (design system variables, animations)
├── components/
│   ├── layout/     # AppShell (sidebar + content layout)
│   ├── config/     # ConfigPanel (translation settings form)
│   ├── library/    # GameCard, CoverPickerModal, LibraryCustomizer
│   ├── progress/   # InstallProgressDrawer (SignalR progress)
│   └── settings/   # Settings page components
├── composables/    # Reusable composition functions (useAddGameFlow)
├── stores/         # Pinia stores (games, install, theme, aiTranslation, log)
├── views/          # LibraryView, GameDetailView, AiTranslationView, LogView, SettingsView, ConfigEditorView
└── router/         # Vue Router config (/, /games/:id, /games/:id/config-editor, /ai-translation, /logs, /settings)
```

### Frontend Design System

- **Fonts:** Lexend (headings), DM Sans (body), JetBrains Mono (mono) — loaded via Google Fonts
- **Colors:** Deep dark base (`#0b0b11`), dynamic accent (default `#3b82f6`, user-customizable), violet secondary (`#a78bfa`)
- **CSS Variables:** Defined in `main.css` (`--bg-root`, `--accent`, `--text-1`, `--radius-lg`, etc.)
- **Theme:** Dark/light mode via `data-theme` attribute; `useThemeStore` manages mode + accentColor + localStorage; light mode auto-darkens accent 15%
- **Layout:** Custom sidebar (230px) + scrollable content; responsive at 768px (tablet drawer) and 480px (phone single-column)
- **Game library:** Grid (default) + list view; 2:3 GameCard with covers (Steam CDN / SteamGridDB / upload); `LibraryCustomizer` controls card size (S/M/L/XL), gap, labels, accent
- **Section cards:** `.section-icon` + `.section-title` — base uses `--accent-soft`/`--accent`; only semantic overrides (`.danger` → `--danger`, `.warning` → `--warning`)
- **Info cards:** `.info-card` with `.info-card-icon` — base uses `--accent-soft`/`--accent`; no per-type color variants
- **Page layouts:** Full-width; two-column grids collapse at 960px/768px
- **Settings page:** `display: flex; flex-direction: column; gap: 16px` — no individual `margin-bottom`
- **Content padding:** `36px 40px` (desktop), `20px 16px` (tablet), `16px 12px` (phone)

## Code Conventions

- **Target Framework:** net10.0-windows
- **Root Namespace:** XUnityToolkit_WebUI
- **Nullable reference types:** Enabled; **Implicit usings:** Enabled
- **Frontend:** Vue 3 Composition API with `<script setup lang="ts">`
- **Frontend styling:** Scoped `<style scoped>`; use CSS variables from `main.css`
- **Frontend icons:** `@vicons/material` and `@vicons/ionicons5` wrapped in Naive UI `NIcon`

## API Endpoints

- **Game CRUD:** `POST /api/games/add-with-detection` (auto-detect), `PUT /api/games/{id}` (rename), `POST /api/games/{id}/detect` (re-detect)
- **Game actions:** `POST /api/games/{id}/open-folder`, `POST /api/games/{id}/launch`
- **Framework:** `DELETE /api/games/{id}/framework/{framework}` — uninstall non-BepInEx frameworks
- **Install:** `GET /api/games/{id}/status`, `POST /api/games/{id}/cancel`
- **Icon:** `GET /api/games/{id}/icon` (custom > exe icon), `POST .../icon/upload`, `DELETE .../icon/custom`
- **Cover:** `GET /api/games/{id}/cover` (raw bytes, 404 if none), `POST .../cover/upload` (5MB), `POST .../cover/search` (SteamGridDB), `POST .../cover/grids`, `POST .../cover/select`, `POST .../cover/steam-search` (free), `POST .../cover/steam-select`, `DELETE .../cover`
- **Config:** `PUT /api/games/{id}/config` (PatchAsync read-modify-write), `GET/PUT .../config/raw` (raw INI)
- **Settings:** `GET/PUT /api/settings`, `GET /api/settings/version`, `POST /api/settings/reset`
- **Cache:** `GET/DELETE /api/cache/downloads`
- **Releases:** `GET /api/releases/bepinex`, `GET /api/releases/xunity`
- **Dialogs:** `POST /api/dialog/select-folder`, `POST /api/dialog/select-file`
- **AI Translation:** `POST /api/translate` (**not ApiResult** — DLL calls directly; frontend must use raw `fetch`), `GET /api/translate/stats`, `POST /api/translate/test`
- **AI Control:** `POST /api/ai/toggle`, `GET /api/ai/models?provider=&apiBaseUrl=&apiKey=`
- **AI Endpoint:** `GET/POST/DELETE /api/games/{id}/ai-endpoint` — manage `LLMTranslate.dll`; POST also patches `[LLMTranslate] ToolkitUrl` + `GameId` in INI
- **Glossary:** `GET/PUT /api/games/{id}/glossary`
- **Logs:** `GET /api/logs?count=` (from ring buffer), `GET /api/logs/history?lines=` (from file), `GET /api/logs/download` (raw file stream, **not ApiResult**)
- **ApiResult pattern:** `ApiResult<T>.Ok(data)` with data; `ApiResult.Ok()` without data; request records at bottom of Endpoints files

## Development Notes

### Backend Core

- `Game.IsUnityGame` defaults to `true` for backward compatibility with existing `library.json`
- Add-game flow: folder picker → auto-detect → exe selection if needed → Unity detection; detects `steam_appid.txt` → auto-fetches Steam cover
- Non-Unity games: only show run + open folder; hide install/config
- `AppSettingsService`: in-memory volatile cache; `GetAsync()` no disk I/O; callers must NOT mutate returned object — use `UpdateAsync`/`SaveAsync`
- `GlossaryService`: `ConcurrentDictionary` cache per game; `GetAsync` returns cached on fast path
- **Data storage:** All data (config, cache, logs, glossaries, backups) stored in `AppContext.BaseDirectory` (program exe directory) — portable app pattern; `AppDataPaths` is the centralized path manager; `Program.cs` bootstrap duplicates the root path for pre-DI settings/log init
- **App URL:** Default `http://127.0.0.1:51821`; **MUST use `127.0.0.1` not `localhost`** (Unity Mono resolves to IPv6 `::1`, hangs)
- Named `HttpClient` instances: `"GitHub"`, `"Mirror"`, `"LLM"` (120s timeout, 200 max connections), `"SteamGridDB"` (30s)
- `Console.OutputEncoding = UTF8` MUST be set before `WebApplication.CreateBuilder()` (logging captures encoding at init)
- P/Invoke: Use `[DllImport]` not `[LibraryImport]` (no `AllowUnsafeBlocks` in csproj)
- When renaming public methods: search all call sites globally (Endpoints, Services, Orchestrator)

### INI Configuration

- **Never generate XUnity config from scratch** — always `PatchAsync` read-modify-write (preserves plugin defaults)
- Config filename: `AutoTranslatorConfig.ini` at `BepInEx/config/` (not BepInEx GUID-style)
- Section compatibility: `[TextFrameworks]` (primary) / `[TextFramework]` (legacy); `PatchAsync` detects actual name
- PatchAsync null semantics: `null` = skip, `""` = clear value; `AddEngineKeyModification` skips both null and ""
- `PatchSectionAsync(gamePath, section, keys)` patches single section — used to set `[LLMTranslate] ToolkitUrl`

### Glossary Extraction

- `GlossaryExtractionService`: buffer → trigger → drain → LLM extract → merge into glossary
- **Critical:** settings check (async) must happen BEFORE buffer drain — otherwise pairs are lost when disabled
- `TryTriggerExtraction` is synchronous (called from hot-path); async work deferred to `DrainAndExtractAsync`
- DLL must send `gameId` in `POST /api/translate` for extraction to work — requires `[LLMTranslate] GameId` in INI

### Concurrency & Performance

- **Hot-path:** `POST /api/translate` receives 100+ req/s; all I/O must use in-memory caches, never disk per request
- `SemaphoreSlim` pattern: `TranslateSingleAsync` receives semaphore as parameter (local capture); `EnsureSemaphore` delays Dispose 3 min; 60s WaitAsync timeout returns 503
- `BroadcastStats` throttling: CAS pattern, 200ms; `force: true` for batch completion and errors
- `volatile` vs `Volatile.Read`: don't combine on same field; `DateTime?` is value type — use `long` ticks + `Interlocked`
- **Plugin concurrency:** DLL `MaxConcurrency` (10) × `MaxTranslationsPerRequest` (10) = 100 concurrent texts; Mono can't handle >15 connections (ThreadPool deadlock) — increase throughput via batching
- **XUnity HTTP:** Uses `HttpWebRequest`; Mono `DefaultConnectionLimit` = 2; **use `FindServicePoint(uri).ConnectionLimit`**; do NOT set `Connection: close` (Mono CLOSE_WAIT bug)

### TranslatorEndpoint

- Targets net35 (C# 7.3); `Microsoft.NETFramework.ReferenceAssemblies.net35` NuGet for `dotnet build`
- Dependencies in `TranslatorEndpoint/libs/` (gitignored): `XUnity.AutoTranslator.Plugin.Core.dll`, `XUnity.Common.dll`
- `DisableSpamChecks()` removes text stabilization wait; `SetTranslationDelay(float)` min 0.1s; available v5.4.3+
- `GetOrCreateSetting` reads existing INI; changing DLL defaults won't affect installed games — use `PatchSectionAsync`
- **"Endpoint" vs "Provider":** "translation endpoint" = `LLMTranslate.dll`; "provider" = `ApiEndpointConfig` LLM API config
- LLM base URLs: DeepSeek (`api.deepseek.com/v1`), Qwen (`dashscope.aliyuncs.com/compatible-mode/v1`), GLM (`open.bigmodel.cn/api/paas/v4`), Kimi (`api.moonshot.cn/v1`) — all OpenAI-compatible; Claude and Gemini have dedicated implementations

### DI & Provider Gotchas

- When registering a pre-constructed `ILoggerProvider` instance via both `AddProvider` and `AddSingleton`, use factory registration `AddSingleton<T>(_ => instance)` to avoid double-dispose (logging infra owns disposal)
- `Program.cs` top-level statements: `IHubContext<T>.SendAsync` is an extension method — requires `using Microsoft.AspNetCore.SignalR;`

### Adding a New Page

- Add view in `src/views/`, lazy route in `router/index.ts`, nav item in `AppShell.vue` `navItems` array (with icon from `@vicons/material`)
- SignalR store pattern: guard `connect()` with `connection.state !== Disconnected` (not just `=== Connected`), re-join group in `onreconnected`
- Reading log files held open by `FileLoggerProvider`: must use `FileShare.ReadWrite` to avoid `IOException`

### Frontend Patterns

- Use composables (`src/composables/`) for complex multi-step UI flows
- Theme-aware CSS: use `--bg-subtle`, `--bg-muted` etc. — never hardcode `rgba(255,255,255,...)`
- Naive UI light theme: pass `null` (not `lightTheme`); accent needs darker values for contrast
- `NDrawer` width: numbers only — use `window.resize` listener + ref
- `NForm` label-placement: toggle dynamically via computed (CSS media queries won't work)
- `NInput` binding `string?`: use `:value="field ?? ''"` + `@update:value` (not `v-model`)
- `NInput` `@blur` + `@keyup.enter` double-fire: add flag guard (`if (!editing.value) return`)
- `v-show` + `loading="lazy"` deadlock: use `opacity: 0` + `position: absolute` instead
- Vue `v-for` ref not resetting on prop change: add `watch` to reset loading state
- TypeScript: use `Object.assign({}, obj, patch)` not spread for typed objects
- Lazy-loaded modals: `defineAsyncComponent(() => import(...))`
- **Icon/card colors:** decorative icons use `--accent`; only use `--danger`/`--warning`/`--success` for semantic status. Use `color-mix(in srgb, var(--xxx) N%, transparent)` instead of hardcoded `rgba()` for theme-aware translucent backgrounds
- After changes, verify with both `npx vue-tsc --noEmit` and `npm run build`
- Verify icon availability: `node -e "const m = require('@vicons/material'); console.log(m['IconName'] ? 'YES' : 'NO')"`

### Sync Points

- **InstallStep enum:** Sync 4 places: `Models/InstallationStatus.cs`, `src/api/types.ts`, `InstallProgressDrawer.vue` (`installSteps`), `InstallOrchestrator.cs`
- **Adding AppSettings fields:** Sync 4 places: `Models/AppSettings.cs`, `src/api/types.ts`, store's `loadPreferences`/`savePreferences`, `SettingsView.vue`
- Frontend state lifecycle: `GameDetailView.loadGame()` resets state when `isInstalled=false`; new "load only when installed" state needs same treatment
- Install store `operationType` tracks install vs uninstall (don't infer from step values)
- **`[LLMTranslate]` INI config:** Written in 3 places — `POST /ai-endpoint` (GameEndpoints), `InstallOrchestrator` (after install), DLL `Initialize` (GetOrCreateSetting defaults). All 3 must stay in sync for keys like `ToolkitUrl`, `GameId`, `MaxConcurrency`

### Build & Deploy

- `dotnet build` auto-runs frontend build; skip with `-p:SkipFrontendBuild=true`
- `build.ps1`: frontend once → TranslatorEndpoint (if libs exist) → publish per runtime to `Release/{rid}/`
- Publish cleanup: remove `web.config`, `*.pdb`, `*.staticwebassets.endpoints.json`
- Stop backend before build: `taskkill //f //im XUnityToolkit-WebUI.exe`
- Frontend changes require `npm run build` + restart backend (unless using `npm run dev`)
- Default system prompt: Chinese, 7 translation rules; `{from}`/`{to}` replaced; `{0}` etc. are literal
- Logs: `{programDir}/logs/app.log` (5MB rotation); error ring buffer (50) pushed via SignalR; `FileLoggerProvider` also has 500-entry `ConcurrentQueue` ring buffer + `LogBroadcast` callback for real-time log page
- Screenshot cleanup: delete project root `*.png` and `.playwright-mcp/` after testing
