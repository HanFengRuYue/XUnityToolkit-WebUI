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
- **Real-time:** SignalR via single `InstallProgressHub` (groups: `game-{id}`, `ai-translation`, `logs`, `pre-translation-{gameId}`, `local-llm`, `font-replacement-{gameId}`)
- **Persistence:** JSON files in `{programDir}/data/` (`library.json`, `settings.json`) — portable app pattern; API keys encrypted with DPAPI
- **System Tray:** NotifyIcon on dedicated STA thread; `ShowNotification` marshals to STA via `SynchronizationContext.Post`; `_trayIcon`/`_syncContext` are `volatile`
- **Console hiding:** `OutputType=WinExe` — no console window on startup (works with both conhost and Windows Terminal/ConPTY); "Show Console" tray menu uses `AllocConsole()` + stream redirect on demand; close button removed via `DeleteMenu(SC_CLOSE)` to prevent accidental app termination; do NOT revert to `Exe` + `ShowWindow(SW_HIDE)` (fails under ConPTY)
- **TranslatorEndpoint:** net35 `LLMTranslate.dll` — XUnity.AutoTranslator custom endpoint forwarding game text to `POST /api/translate`; configurable via `[LLMTranslate]` INI section
- **AI Translation:** `LlmTranslationService` calls LLM APIs (OpenAI/Claude/Gemini/DeepSeek/Qwen/GLM/Kimi/Custom); multi-provider load balancing; batch mode bounded by `SemaphoreSlim`; per-game glossary, translation memory, AI description; real-time stats via SignalR
- **Local LLM:** `LocalLlmService` manages llama-server process; GPU detection via DXGI with WMI fallback; llama binaries bundled as ZIPs, lazy-extracted on first use; local mode forces concurrency=1, disables glossary extraction
- **Asset Extraction:** `AssetExtractionService` uses AssetsTools.NET to extract strings from Unity `.assets` and bundle files; `PreTranslationService` batch-translates and writes XUnity cache files
- **Backup/Restore:** `BackupService` creates per-game `BackupManifest` for clean uninstallation; manifests at `{programDir}/data/backups/{gameId}.json`
- **Font Replacement:** `FontReplacementService` uses AssetsTools.NET to scan and replace TMP_FontAsset in game `.assets` and bundle files; field-level replacement preserves PPtr references; automatic Addressables CRC clearing; backups at `{programDir}/data/font-backups/{gameId}/`; custom fonts at `{programDir}/data/custom-fonts/{gameId}/`

### Frontend Design System

- **Theme:** Dark/light via `data-theme`; `useThemeStore` (localStorage + OS) is authoritative, not backend `AppSettings.Theme`; light mode auto-darkens accent 15%
- **CSS Variables:** Defined in `main.css` (`--bg-root`, `--accent`, `--text-1`, etc.); theme-aware: use `--bg-subtle`/`--bg-muted` — never hardcode `rgba(255,255,255,...)`
- **Layout:** Sidebar (230px) + scrollable content; responsive at 768px (tablet drawer) and 480px (phone single-column)
- **Cards:** `.section-card` with `.section-icon`/`.section-title` (base `--accent`; semantic `.danger`/`.warning` only); decorative colors → `--accent`; `color-mix()` for translucent backgrounds
- **Content padding:** `24px 28px` (desktop), `20px 20px` (tablet), `16px 12px` (phone) — hero backdrop negative margins must match

## Code Conventions

- **Git commit messages:** Titles and body MUST be written in Chinese (conventional commit prefixes like `feat:`/`fix:`/`ci:`/`docs:` may remain in English)
- **Target Framework:** net10.0-windows
- **Root Namespace:** XUnityToolkit_WebUI
- **Nullable reference types:** Enabled; **Implicit usings:** Enabled
- **Frontend:** Vue 3 Composition API with `<script setup lang="ts">`
- **Frontend styling:** Scoped `<style scoped>`; use CSS variables from `main.css`
- **Frontend icons:** `@vicons/material` and `@vicons/ionicons5` wrapped in Naive UI `NIcon`

## API Endpoints

- **Game:** `GET/POST /api/games/`, `GET/DELETE /api/games/{id}`, `POST .../add-with-detection`, `PUT /api/games/{id}` (rename), `POST .../detect`, `POST .../open-folder`, `POST .../launch`
- **Framework:** `DELETE /api/games/{id}/framework/{framework}`
- **Install:** `POST /api/games/{id}/install`, `DELETE .../install` (uninstall), `GET .../status`, `POST .../cancel`
- **Icon:** `GET /api/games/{id}/icon` (custom > exe icon), `POST .../icon/upload`, `DELETE .../icon/custom`, `POST .../icon/{search,grids,select}` (SteamGridDB), `POST .../icon/web-search`, `POST .../icon/web-select`
- **Cover:** `GET .../cover`, `POST .../cover/upload` (5MB), `POST .../cover/{search,grids,select,steam-search,steam-select,web-search,web-select}`, `DELETE .../cover`
- **Background:** `GET .../background`, `POST .../background/upload` (10MB), `POST .../background/{search,heroes,select,steam-search,steam-select,web-search,web-select}`, `DELETE .../background`
- **Config:** `GET/PUT /api/games/{id}/config` (PatchAsync read-modify-write on PUT), `GET/PUT .../config/raw`
- **Settings:** `GET/PUT /api/settings`, `GET .../version`, `POST .../reset`
- **Dialogs:** `POST /api/dialog/{select-folder,select-file}`
- **AI Translation:** `POST /api/translate` (**not ApiResult** — DLL calls directly; frontend must use raw `fetch`), `GET /api/translate/stats`, `POST /api/translate/test`
- **AI Control:** `POST /api/ai/toggle`, `GET /api/ai/models?provider=&apiBaseUrl=&apiKey=`, `GET /api/ai/extraction/stats`
- **Local LLM:** `GET/PUT /api/local-llm/settings` (PUT merges gpuLayers/contextLength only), `GET .../status`, `GET .../gpus`, `POST .../gpus/refresh`, `GET .../catalog`, `GET .../llama-status`, `POST .../test` (requires Running), `POST .../start`, `POST .../stop`, `.../download` (model) + `/pause` + `/cancel` variants, `GET .../models`, `POST .../models/add`, `DELETE .../models/{id}`
- **AI Endpoint:** `GET/POST/DELETE /api/games/{id}/ai-endpoint` — manage `LLMTranslate.dll`; POST also patches `[LLMTranslate] ToolkitUrl` + `GameId` in INI
- **Glossary:** `GET/PUT /api/games/{id}/glossary` | **Description:** `GET/PUT .../description`
- **Asset Extraction:** `POST .../extract-assets`, `GET/DELETE .../extracted-texts`
- **Pre-Translation:** `POST .../pre-translate`, `GET .../pre-translate/status`, `POST .../pre-translate/cancel`
- **Translation Editor:** `GET/PUT .../translation-editor`, `POST .../import`, `GET .../export` (**not ApiResult**)
- **Font Replacement:** `POST .../font-replacement/scan`, `POST .../replace`, `POST .../restore`, `GET .../status`, `POST .../upload`, `POST .../cancel`
- **Plugin Package:** `POST .../plugin-package/export` (ZIP, **not ApiResult**), `POST .../import`
- **Logs:** `GET /api/logs?count=`, `GET .../history?lines=`, `GET .../download` (**not ApiResult**)
- **ApiResult pattern:** `ApiResult<T>.Ok(data)` / `ApiResult.Ok()`; request records at bottom of Endpoints files

## Development Notes

### Backend Core

- **No migration code:** Project is pre-stable — no backward-compat migrations or old-format converters
- `AppSettingsService`: in-memory cache; `GetAsync()` no disk I/O; must NOT mutate returned object — use `UpdateAsync`/`SaveAsync`
- **Data storage:** `{programDir}/data/` for all runtime data; `AppDataPaths` centralizes paths; shipped assets (`bundled/`, `wwwroot/`) stay at program root
- **Sensitive data encryption:** `DpapiProtector` (DPAPI CurrentUser) encrypts `ApiEndpointConfig.ApiKey` and `SteamGridDbApiKey`; prefix `ENC:DPAPI:` + Base64; encrypt/decrypt in `AppSettingsService.ReadAsync`/`WriteAsync` boundary; decryption failure preserves ciphertext + creates `.bak` backup
- **Pre-DI paths:** `Program.cs` reads `builder.Configuration["AppData:Root"]` fallback to `{baseDir}/data` before DI — must stay in sync with `AppDataPaths._root` formula
- **App URL:** `http://127.0.0.1:51821`; **MUST use `127.0.0.1` not `localhost`** (Unity Mono resolves to IPv6 `::1`)
- Named `HttpClient`: `"LLM"` (120s/200conn), `"SteamGridDB"` (30s), `"LocalLlmDownload"` (12h), `"WebImageSearch"` (15s, browser UA)
- **Mirror:** `AppSettings.HfMirrorUrl`; HF host-replacement for model downloads; plugins/llama binaries are bundled (no runtime GitHub downloads)
- **Fire-and-forget:** `CancellationToken.None` in `Task.Run`; `CancellationTokenSource` dicts for user cancellation
- **HTTP Range 416:** Verify completeness via `Content-Range`; size mismatch → delete and restart
- `Console.OutputEncoding = UTF8` before `WebApplication.CreateBuilder()`
- P/Invoke: `[DllImport]` not `[LibraryImport]`; renaming methods → search all call sites
- **Dialog foreground:** `DialogEndpoints.ForceForegroundWindow` uses `AttachThreadInput` — do NOT simplify to bare `SetForegroundWindow` (silently fails from background)
- **Console P/Invoke:** `AllocConsole`/`FreeConsole` (kernel32), `GetSystemMenu`/`DeleteMenu` (user32); `OutputType=WinExe` in csproj is critical — do NOT revert to `Exe`

### INI Configuration

- **Never generate XUnity config from scratch** — always `PatchAsync` read-modify-write
- Config filename: `AutoTranslatorConfig.ini` at `BepInEx/config/`
- PatchAsync null semantics: `null` = skip, `""` = clear value
- `ApplyOptimalDefaultsAsync` sets `[General] Language = zh`, `OverrideFont = Microsoft YaHei`, `Endpoint = LLMTranslate` with `FallbackEndpoint = GoogleTranslateV2`
- **Sensitive sections** (sanitize for export): `GoogleLegitimate`, `BingLegitimate`, `Baidu`, `Yandex`, `DeepLLegitimate`, `PapagoTranslate`, `LingoCloud`, `Watson`, `Custom`, `LecPowerTranslator15`, `ezTrans`

### Glossary Extraction

- `GlossaryExtractionService`: buffer → trigger → drain → LLM extract → merge
- **GlossaryEntry model:** `Original`, `Translation`, `IsRegex`, `Description` (nullable)
- **Critical:** settings check (async) BEFORE buffer drain — otherwise pairs lost when disabled
- `TryTriggerExtraction` is synchronous (hot-path); async work deferred to `DrainAndExtractAsync`
- DLL must send `gameId` in `POST /api/translate` — requires `[LLMTranslate] GameId` in INI
- **All translation paths** must call `BufferTranslation` + `TryTriggerExtraction`, guarded by `!string.IsNullOrEmpty(gameId)` AND `!isLocalMode`

### Concurrency & Performance

- **Hot-path:** `POST /api/translate` receives 100+ req/s; all I/O must use in-memory caches, never disk per request
- `SemaphoreSlim`: one slot per batch; `EnsureSemaphore` delays Dispose 3 min; 60s timeout → 503; **critical:** semaphore wait and LLM call in separate `try` blocks
- **Hot-path caching:** Never `GameLibraryService.GetByIdAsync` on hot path; use `ConcurrentDictionary` + explicit invalidation
- `BroadcastStats`: CAS throttle 200ms; `force: true` for completion/errors
- **Stats counters unit:** `_queued`, `_translating`, `_totalReceived`, `_totalTranslated` must ALL count individual texts (not batches/HTTP requests)
- **RecordError:** `LlmTranslationService.RecordError` is sole site — endpoint catch must NOT double-count
- `volatile` vs `Volatile.Read`: don't combine; `DateTime?` → `long` ticks + `Interlocked`; async cannot have `ref`/`in`/`out` → wrapper class
- **Plugin concurrency:** DLL 10x10 = 100 texts; Mono >15 connections deadlocks — batch instead
- **XUnity HTTP:** Mono `DefaultConnectionLimit` = 2 → `FindServicePoint(uri).ConnectionLimit`; no `Connection: close` (CLOSE_WAIT bug)
- **Pre-translation:** `Parallel.ForEachAsync` over batches of 10; CAS-throttled 200ms progress

### AI Translation Context

- **Batch mode:** entire batch as one LLM call; JSON array I/O
- **Translation memory:** per-game volatile; cloud `ContextSize` (10, max 100), local `LocalContextSize` (0, max 10)
- **Game description:** `Game.AiDescription`; `_descriptionCache` invalidated by `PUT /description`; truncated to 500 chars
- **SystemPrompt order:** template → description → glossary → memory → [texts]
- **ParseTranslationArray:** strips `<think>...</think>` then extracts JSON array (handles non-fenced)

### TranslatorEndpoint

- Targets net35 (C# 7.3); `Microsoft.NETFramework.ReferenceAssemblies.net35` NuGet
- Dependencies in `TranslatorEndpoint/libs/` (tracked in git)
- `DisableSpamChecks()` removes stabilization wait; `SetTranslationDelay(float)` min 0.1s; available v5.4.3+
- `GetOrCreateSetting` reads existing INI; changing DLL defaults won't affect installed games — use `PatchSectionAsync`
- **"Endpoint" vs "Provider":** "translation endpoint" = `LLMTranslate.dll`; "provider" = `ApiEndpointConfig` LLM API config

### Asset Extraction (AssetsTools.NET)

- `MonoCecilTempGenerator`/`Cpp2IlTempGenerator` both in `AssetsTools.NET.Extra`; IL2CPP: fully qualified `AssetsTools.NET.Cpp2IL.Cpp2IlTempGenerator`
- `classdata.tpk` embedded as resource
- `LoadAssetsFile()` holds file handles — must `UnloadAssetsFile()` per iteration
- **Bundle files:** `LoadBundleFile(path, true)` → iterate DirectoryInfos (skip `.resource`/`.resS`) → `LoadAssetsFileFromBundle` → `UnloadBundleFile`
- **TypeTree fallback:** bundles usually embed type trees — check `afile.Metadata.TypeTreeEnabled`
- Install flow auto-extracts → detects language → patches `[General] FromLanguage` → caches; failure doesn't block install
- **Language detection:** Non-Latin scripts prioritized over Latin (Unity internals always contribute English); only classify as English when non-Latin count < 10; `IsGameText` is heuristic exclusion filter
- XUnity cache format: `encoded_original=encoded_translation`; escapes `\\`, `\n`, `\r`, `\=`; `XUnityTranslationFormat` static class
- **`{Lang}` in OutputFile:** substitute with `config.TargetLanguage`; guard against path traversal

### Local LLM

- GPU detection: `DxgiGpuDetector` (DXGI, 64-bit VRAM, PCI VendorId) primary; WMI fallback (uint32 caps at 4GB)
- Backend selection: NVIDIA→CUDA, AMD/Intel→Vulkan, none→CPU; binaries at `{programDir}/data/llama/{cuda,vulkan,cpu}/`
- **Bundled binaries:** ZIPs in `bundled/llama/`; lazy-extracted on first `POST /start` via `ExtractLlamaZipAsync`
- **Model downloads:** `.downloading` temp files + HTTP Range; `_pauseRequests` differentiates pause from cancel
- **GPU monitoring:** nvidia-smi polled every 3s when CUDA running
- **Reasoning disabled:** `--reasoning-budget 0` prevents `<think>` blocks
- Endpoint auto-registers as `Custom` provider with Priority=8; stable `EndpointId`
- Settings: `data/local-llm-settings.json`; mirror settings unified in `AppSettings`

### BepInEx Installation (Bundled)

- **Mono games:** BepInEx 5 from `bundled/bepinex5/` (x64 + x86)
- **IL2CPP games:** BepInEx 6 BE from `bundled/bepinex6/` (x64 + x86); supports IL2CPP metadata v31+
- `BepInExInstallerService.InstallAsync` is plain ZIP extraction; version parsed from filename

### Plugin Package (Export/Import)

- `PluginPackageService`: exports BepInEx + XUnity as ZIP (max compression)
- **INI sanitization:** blanks sensitive section values; `[LLMTranslate]` only blanks `GameId`
- **ZIP filename:** `{sanitized game name}_{target language}_{yyyy-MM-dd}.zip`

### DI & Provider Gotchas

- Pre-constructed `ILoggerProvider`: use `AddSingleton<T>(_ => instance)` to avoid double-dispose
- `IHubContext<T>.SendAsync` is extension method — requires `using Microsoft.AspNetCore.SignalR;`
- **`SystemTrayService` DI:** `AddSingleton` + `AddHostedService(sp => sp.GetRequired...)` for injection + hosting

### Adding a New Page

- **Top-level page:** Add view in `src/views/`, lazy route in `router/index.ts`, nav item in `AppShell.vue` `navItems`
- **Game sub-page:** lazy route at `/games/:id/{name}`, button in `GameDetailView.vue` — do NOT add to `navItems`
- **Page transitions:** `meta.depth` on routes (1=top-level, 2=game detail, 3=game sub-pages); adding a new route requires `meta: { depth: N }`
- SignalR store: guard `connect()` with `state !== Disconnected`, re-join group in `onreconnected`
- Reading log files: must use `FileShare.ReadWrite` to avoid `IOException`

### Frontend Patterns

- Use composables (`src/composables/`) for complex multi-step UI flows
- **Auto-save:** `useAutoSave(source, saveFn, { debounceMs, deep })`; `disable()` → load → `nextTick()` → `enable()`; `disable()` MUST clear pending timer; `onBeforeUnmount` auto-flushes; manual save MUST `disable()` before data reassign, `enable()` in `finally`
- **ConfigPanel** auto-saves internally (2s), no `save` event
- Naive UI: light theme pass `null`; `NDrawer` width numbers only; `NForm` label-placement via computed (not CSS); `NInput` `string?` use `:value` + `@update:value`; `NInput` blur+enter double-fire → flag guard
- `NDataTable`: `virtual-scroll` and `pagination` mutually exclusive; empty state guard with `filteredEntries.length > 0`
- `v-show` + `loading="lazy"` deadlock: use `opacity: 0` + `position: absolute`
- `onBeforeRouteLeave` with async: must `return new Promise<boolean>()` — NOT `next()` callback
- **RouterView key:** `:key="route.path"` ensures transitions fire for same-component different-route navigations
- **RouteMeta extension:** `env.d.ts` declares `depth?: number` on `RouteMeta` for TypeScript
- **GameDetailView animation:** 0.05s increments; inserting a card shifts ALL subsequent delays
- **Blob download:** `fetch` → `blob()` → `createObjectURL` → `a.click()` → `setTimeout(revokeObjectURL, 1000)`
- After changes: verify with `npx vue-tsc --noEmit` and `npm run build`
- Verify icon: `node -e "const m = require('@vicons/material'); console.log(m['IconName'] ? 'YES' : 'NO')"`
- **`embedded` prop pattern:** conditionally render card wrapper based on standalone vs nested usage
- **`LocalAiPanel.vue`:** receives settings via `v-model`; shared settings flow through parent's `useAutoSave`; local-only settings saved via `PUT /api/local-llm/settings`
- TypeScript: `Object.assign({}, obj, patch)` not spread for typed objects; lazy modals: `defineAsyncComponent`
- **`NTabs` equal-width segments:** `:deep(.n-tabs-tab) { flex: 1; justify-content: center; }`
- **C# `[GeneratedRegex]` with quotes:** Raw string literals (`"""..."""`) fail when regex contains `"` — use regular escaped strings instead

### Web Image Search

- **`WebImageSearchService`:** Scrapes Bing and Google for image results; no API key needed
- **`WebImageSearchTab` modes:** `'cover'` | `'icon'` | `'background'` — each with distinct search suffix, size filter, and grid aspect ratio
- **SSRF protection:** `ValidateImageUrl` rejects non-http(s), loopback, private IPs before server-side download
- **Content-Type validation:** Must check `IsAllowedContentType` before saving downloaded images
- **GameDetailView title-icon:** left-click → IconPickerModal; right-click → context menu (Change Cover / Change Background / Search Icon / Upload Icon / Delete Icon / Delete Background)

### Game Detail Background Image

- **Hero backdrop:** `GameDetailView.vue` — absolute-positioned behind content; NO blur on image (`filter: brightness(0.65) saturate(1.1)` only); gradient + vignette overlays fade to `--bg-root`
- **Acrylic cards:** `.section-card` uses `backdrop-filter: blur(20px)` with semi-transparent `--bg-card` — cards blur the background, not the image itself
- **Parallax:** scroll listener on `.main-content` (parent); `translateY(scrollTop * 0.3)` on hero img; `passive: true`
- **bgTimestamp:** separate `ref(Date.now())` for cache-busting background URL independently of `game.updatedAt`

### Sync Points

- **InstallStep enum:** Sync 4 places: `Models/InstallationStatus.cs`, `src/api/types.ts`, `InstallProgressDrawer.vue`, `InstallOrchestrator.cs`
- **Adding AppSettings fields:** Sync 4 places: `Models/AppSettings.cs`, `src/api/types.ts`, store's `loadPreferences`/`savePreferences`, `SettingsView.vue`
- **Adding AiTranslationSettings fields:** Sync 4 places: `Models/AiTranslationSettings.cs`, `src/api/types.ts`, `AiTranslationView.vue` (`DEFAULT_AI_TRANSLATION`), `SettingsView.vue`
- Frontend state lifecycle: `GameDetailView.loadGame()` resets state when `isInstalled=false`
- Install store `operationType` tracks install vs uninstall
- **`[LLMTranslate]` INI config:** Written in 3 places — `POST /ai-endpoint`, `InstallOrchestrator`, DLL `Initialize`

### Build & Deploy

- `dotnet build` auto-runs frontend; skip with `-p:SkipFrontendBuild=true`
- `build.ps1`: downloads bundled assets (`-SkipDownload` to skip) → frontend → TranslatorEndpoint (if libs exist) → publish to `Release/{rid}/`; cleanup: remove `web.config`, `*.pdb`, `*.staticwebassets.endpoints.json`
- **Versioning:** `build.ps1` auto-generates `1.0.{YYYYMMDDHHmm}` via `-p:InformationalVersion`; **must use `InformationalVersion` not `Version`** — `Version` sets `AssemblyVersion` (UInt16 max 65535) which overflows with timestamp
- **Bundled assets:** `bundled/{bepinex5,bepinex6,xunity,llama}/` — ALL auto-detect latest versions via API; no hardcoded version pins; llama.cpp prefers CUDA 12.4; copied post-publish (NOT via csproj Content Include — `PublishSingleFile` silently drops files with `+` in names)
- **TMP fonts:** `bundled/fonts/` (tracked in git); release build uses `build.ps1` post-publish `Copy-Item`
- **Single-file gotcha:** `LibCpp2IL.dll` uses `Assembly.Load` internally; must be excluded via `ExcludeFromSingleFile` MSBuild target
- **gitignore negation:** `bundled/` (directory pattern) blocks child negations; use `bundled/*` (wildcard) to allow `!bundled/fonts/`
- **CI/CD:** GitHub Actions; `build.yml` (reusable), `release.yml` (tag `v*`), `dep-check.yml` (daily update check → auto pre-release)
- **CI version tracking:** `.github/deps.json` stores last-known versions; `dep-check.yml` compares upstream
- **CI cannot call `build.ps1`** — `Wait-Exit` blocks in non-interactive; workflow replicates logic inline
- **.NET 10 preview in CI:** `setup-dotnet@v5` with `dotnet-quality: 'preview'`
- **CI gotcha — `$GITHUB_OUTPUT`:** multiline values corrupt format; use heredoc (`key<<EOF`) or `jq -c` for JSON
- **CI gotcha — `gh release create --notes`:** backticks in `${{ }}` become bash command substitution; use `--notes-file` instead
- Stop backend before build: `taskkill //f //im XUnityToolkit-WebUI.exe`
- Default system prompt: Chinese, 7 rules; `{from}`/`{to}` replaced; `{0}` etc. literal
- Logs: `{programDir}/data/logs/XUnityToolkit_YYYY-MM-DD_HH-mm-ss.log`; 500-entry ring buffer + `LogBroadcast`
- Screenshot cleanup: delete project root `*.png` and `.playwright-mcp/` after testing
