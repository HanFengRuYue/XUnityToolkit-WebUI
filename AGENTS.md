# AGENTS.md

Project-specific guidance for coding agents working in this repository.

## Scope

- This file applies to the repository root and all descendants.
- A more specific nested `AGENTS.md` or `AGENTS.override.md` takes precedence inside its subtree. This repository intentionally maintains only this root guide; do not create nested agent guides. Report and consolidate accidental duplicates into this file when asked.
- Read this file and `README.md` before changing behavior. Source code, manifests, and CI configuration are authoritative when documentation disagrees; update this guide when durable project facts or constraints change.
- Do not restore the removed `CLAUDE.md` files or use them as maintenance entry points.

## Project overview

XUnityToolkit-WebUI is a Windows desktop tool for Unity game localization workflows. It installs BepInEx and XUnity.AutoTranslator, routes captured text through `LLMTranslate.dll` to a local Web API, supports cloud and local llama.cpp translation, manages translation memory and terminology, handles TextMesh Pro and Legacy `Font` assets, diagnoses plugins, and ships an updater plus MSI packages.

Primary technologies:

- Backend and desktop host: .NET 10, ASP.NET Core Minimal APIs, SignalR, WinUI 3, WebView2, and a WinForms `NotifyIcon` compatibility layer.
- Frontend: Vue 3, TypeScript, Naive UI, Pinia, Vue Router, and Vite.
- Tests: xUnit for the backend and runtime font loader.
- Auxiliary projects: `TranslatorEndpoint` targets `net35` with C# 7.3; `Updater` targets `net10.0` with AOT; `Installer` uses WixToolset v7.

## Repository layout

- `XUnityToolkit-WebUI/`: ASP.NET Core backend and WinUI/WebView2 desktop host.
- `XUnityToolkit-Vue/`: Vue frontend; API clients, stores, composables, components, and views live under `src/`.
- `XUnityToolkit-WebUI.Tests/`: backend regression tests.
- `TranslatorEndpoint/`: `LLMTranslate.dll` for XUnity.AutoTranslator.
- `RuntimeFontLoader/`: BepInEx 5 Mono and BepInEx 6 IL2CPP runtime font plugins.
- `RuntimeFontLoader.Tests/`: runtime font loader tests.
- `Updater/`: native AOT updater for replacement, deletion, rollback, and restart.
- `Installer/`: WiX MSI project and generation script.
- `bundled/`: fonts, presets, BepInEx/XUnity packages, llama.cpp assets, and runtime font loader outputs.
- `docs/`: user and architecture documentation.
- `.github/workflows/`: independent CI build, release, and dependency-check workflows.
- `build.ps1`: local end-to-end release build.

## Development commands

Run backend commands from the repository root:

```powershell
dotnet build XUnityToolkit-WebUI/XUnityToolkit-WebUI.csproj
dotnet build XUnityToolkit-WebUI/XUnityToolkit-WebUI.csproj -p:SkipFrontendBuild=true
dotnet run --project XUnityToolkit-WebUI/XUnityToolkit-WebUI.csproj
dotnet test
```

The main project normally runs `npm ci` and `npm run build` before the backend build. Use `-p:SkipFrontendBuild=true` only when intentionally validating backend-only work.

Run frontend commands from `XUnityToolkit-Vue/`:

```powershell
npm ci
npm run dev
npm run build
npx vue-tsc --build
npx tsc --noEmit --project tsconfig.app.json
```

The frontend development proxy targets `http://127.0.0.1:51821`; do not change it to `localhost`.

Auxiliary builds:

```powershell
dotnet build TranslatorEndpoint/TranslatorEndpoint.csproj -c Release
dotnet build RuntimeFontLoader/Mono/RuntimeFontLoader.Mono.csproj -c Release
dotnet build RuntimeFontLoader/Il2Cpp/RuntimeFontLoader.Il2Cpp.csproj -c Release
dotnet publish Updater/Updater.csproj -c Release -r win-x64
```

Local release builds:

```powershell
.\build.ps1
.\build.ps1 -SkipDownload
.\build.ps1 -Edition full
.\build.ps1 -Edition no-llama
.\build.ps1 -Edition lite
```

- `build.ps1` performs post-publish `--headless-smoke` checks by default; use `-SkipSmoke` only when smoke validation is intentionally out of scope.
- Use `-ReleaseRoot .\Release\<isolated-directory>` when the default release is running. `ReleaseRoot` must remain the default `Release` directory or one of its descendants.
- Do not run the main backend build and `XUnityToolkit-WebUI.Tests` concurrently. Their StaticWebAssets steps can race on `obj/.../rpswa.dswa.cache.json`.

## Coding conventions

### Change discipline and text encoding

- Make focused changes and preserve unrelated working-tree edits. Do not edit generated output, dependencies, caches, or release artifacts unless the task explicitly requires it.
- Treat tracked source, scripts, configuration, workflows, and documentation as UTF-8 unless a file has a verified format-specific encoding requirement. Never decode or save them through the system ANSI code page.
- Before editing a file containing non-ASCII text, verify its current byte encoding. After editing, re-read it with strict UTF-8 decoding and inspect the diff for lost or transformed text.
- Reject `U+FFFD`, unexpected private-use characters, and text that has been transformed into obvious UTF-8/GBK mojibake. If such text already exists, report it and recover the intended text from history or other evidence instead of propagating it.
- Never round-trip an entire source file through locale-dependent `Get-Content`/`Set-Content`, shell redirection, or another implicit-encoding pipeline. Use `apply_patch` or an editor/API with explicit verified encoding.
- A `.ps1` file that must run under Windows PowerShell 5.1 and contains non-ASCII text must use an encoding that Windows PowerShell 5.1 detects reliably, such as UTF-8 with BOM; otherwise keep the script text ASCII-only. Declare a PowerShell 7 requirement when relying on PowerShell 7 encoding behavior.
- Preserve existing line endings unless a deliberate repository-wide normalization is requested.

### Git and collaboration text

- Git commit titles, commit bodies, PR titles, PR descriptions, and review replies must be written in Chinese, except for necessary product names, identifiers, and commands.
- Commit titles must use one line in `type: <Chinese summary>` form. Allowed lowercase types are `feat`, `fix`, `docs`, `refactor`, `perf`, `ci`, `chore`, `style`, and `test`.
- Use an ASCII colon followed by one space. Do not use scopes, emoji, bracket tags, custom prefixes, or vague summaries.
- Release titles follow `feat: 发布 vX.Y`; version-only bumps follow `chore: 版本号提升至 vX.Y`.
- Commit bodies must explain why the change exists, what changed, risks, and validation results in Chinese; do not use generic English templates or AI boilerplate.
- The update panel parses non-merge commit subjects generated by `.github/workflows/build.yml`. Keep the recognized title format in sync with `XUnityToolkit-Vue/src/views/SettingsView.vue` when changing changelog behavior.
- Credentials used to push workflow changes must include the GitHub `workflow` scope.

### Backend conventions

- Minimal API endpoints that use the standard envelope must return `Results.Ok(ApiResult<T>.Ok(...))`; validation failures must return `BadRequest`, not HTTP 200 with a failure body.
- `POST /api/translate` is the direct DLL protocol and intentionally does not use the standard `ApiResult<T>` envelope.
- Do not rebuild `AutoTranslatorConfig.ini` from scratch. Use `ConfigurationService.PatchAsync` or `PatchTranslatorEndpointAsync`; `null` means skip a field and an empty string means clear it.
- `AppSettingsService.GetAsync()` returns a cached object. Prefer `UpdateAsync` or `SaveAsync` and do not mutate cached state casually.
- Avoid disk I/O and large uncached lookups on hot paths, especially `POST /api/translate`.
- Reuse named `HttpClient` instances and their timeout, proxy, connection, and user-agent policies. Do not scatter `new HttpClient()` calls.
- Validate every `GameId` used in a path as a GUID. Validate language codes, executable names, upload names, and archive entry paths before joining paths.
- New per-game files or directories require matching deletion, cache eviction, export exclusion, and import-rebuild handling.
- This project is pre-stable and does not add legacy migrations or backward-format conversion unless explicitly required.

### Frontend conventions

- Use Vue 3 Composition API and `<script setup lang="ts">`.
- Route API calls through `@/api/client`, `@/api/games`, or the relevant shared API module. Do not add page-local axios implementations.
- Do not leave `console.*` calls in production paths.
- Put reusable behavior in `src/composables/`, `src/utils/`, or `src/constants/`; do not duplicate near-identical helpers across views.
- Do not mutate Pinia internals directly from views. Use store actions. `gamesStore.launchGame(id)` is the single frontend game-launch entry point.
- Top-level pages are cached with `KeepAlive`. SignalR connections, timers, and window listeners must handle `onActivated`, `onDeactivated`, and `onBeforeUnmount`; create and dispose `HubConnection` instances inside lifecycle handling rather than at module scope.
- When loading external state into an autosave view, use `disable -> load/assign -> nextTick -> enable`. Re-fetch shared settings when a cached settings view is reactivated.
- Use `useThemeStore().resolvedTheme` for rendering; `mode` may be `system`.
- Reuse shared classes in `src/assets/main.css`. Desktop layouts must tolerate medium content widths: use shrinkable grid/flex children, `min-width: 0`, and appropriate intermediate breakpoints.
- Use self-hosted fonts; do not add a Google Fonts CDN dependency.
- Use design-system color and border variables. Avoid inline styles except truly one-off dynamic values.
- Handle `NInputNumber` values as `number | null`. Reuse existing Naive UI patterns for dialogs, tables, color pickers, uploads, ellipsis, tabs, and collapsible panels.
- Add top-level features to the router, navigation, and cache list. Add game-specific features under `/games/:id/...` and expose them from `GameDetailView`, not the main navigation.
- Keep `FileExplorerModal.vue` mounted once globally and access it through `useFileExplorer()`. Extend the unified `TermEditorView` instead of recreating separate glossary or do-not-translate pages.

### Cross-layer synchronization

- New settings fields must update C# models/defaults, TypeScript types/defaults, stores, views, save logic, and backend clamps together.
- Keep the backend default translation prompt and `XUnityToolkit-Vue/src/constants/prompts.ts` synchronized. Both must require exact placeholder preservation and forbid invented full-sentence wrappers, speaker prefixes, or explanatory text.
- Keep `AppSettings`, `AiTranslationSettings`, `ApiEndpointConfig`, `LocalLlmSettings`, `InstallStep`, update/version models, file-explorer models, font models, plugin-health models, toolbox-agent models, and translation statistics synchronized across backend and frontend.
- `ApiEndpointConfig` changes must also update endpoint cloning, local llama endpoint registration, `AiTranslationCard.vue`, and `LlmApiAdapterTests`. The local llama endpoint remains `ChatCompletions + Default`.
- `TranslationEditor` changes must cover `TranslationEditorEndpoints.cs`, `TranslationEditorPathResolver.cs`, `src/api/types.ts`, `src/api/games.ts`, and `TranslationEditorView.vue`.
- Font replacement changes must synchronize request `sourceId`, source/status/manifest models, endpoints, service logic, `src/api/types.ts`, and `FontReplacementView.vue`.
- Build inputs, pinned versions, editions, and packaging assumptions must be kept consistent across `build.ps1`, `.github/workflows/build.yml`, `.github/workflows/dep-check.yml`, README documentation, and relevant source constants.

## Testing and validation

- Validate the smallest relevant scope first, then run broader checks in proportion to risk. Report every check not run and why.
- Backend changes require the relevant xUnit tests; use `dotnet test` or the specific test project. Translation parsing, protocol, placeholders, persistence filters, path security, and font byte handling need regression tests.
- Frontend changes require at least `npx vue-tsc --build` and `npm run build`. Also run `npx tsc --noEmit --project tsconfig.app.json` when ordinary TypeScript changes are involved.
- Perform an actual UI check for material visual or interaction changes. Use the runtime discovery file's `baseUrl`, not a hard-coded preferred port, when the backend may have fallen back from port 51821.
- Build and test the backend serially because of the StaticWebAssets cache race.
- Release, port-selection, static-root, startup, updater, or packaging changes require the relevant `build.ps1` smoke path and CI-path review. Preserve both preferred-port-free and preferred-port-occupied smoke scenarios.
- After any non-ASCII edit or whole-file rewrite, validate strict UTF-8 decoding and scan the changed text and diff for replacement characters, private-use glyphs, and UTF-8/GBK mojibake before considering the task complete.
- For a TTF replacement, immediately re-read the written Unity `Font` and verify `FontDataSize` equals the source byte length and `TtfMode == dynamicEmbedded`; treat failure as a replacement failure and roll back that font.
- For changes that affect shutdown, exercise dirty translation-memory flushing and ensure the WinUI message loop can exit without leaving the updater waiting.

## Project-specific constraints

### Runtime and desktop host

- `XUnityToolkit-WebUI/Program.cs` is the backend startup authority. It reads `aiTranslation.port` as a preferred port in `1024-65535`, defaults to 51821, and may fall back atomically to `127.0.0.1:0` only for occupied or reserved-port failures. Other listen failures must stop startup.
- Bind local protocol services to numeric `127.0.0.1`, never `localhost`. Local XUnity, discovery-probe, second-instance activation, and host-loopback clients must disable proxies; cloud LLM clients must retain normal proxy behavior. Never change the system proxy or `WebRequest.DefaultWebProxy`.
- Resolve `AppData:Root` before dependency injection and use it to isolate the single-instance lock. A second instance activates the first through `POST /api/app/activate` and then exits.
- Publish `runtime/toolbox-endpoint-v1.json` atomically after startup. It records the protocol, instance, PID, actual URL, preferred/actual ports, and startup time. Delete it on shutdown only when the instance ID matches.
- Drive tray, WebView, smoke checks, and endpoint configuration from `ToolkitRuntimeEndpointState` and the actual URL.
- Anchor `ContentRootPath` and `WebRootPath` to `AppContext.BaseDirectory`, never `Environment.CurrentDirectory`. Startup logs must include current/base/content/web roots and whether `wwwroot/index.html` exists; a missing entry file is critical.
- Keep the static cache policy distinct for `/assets/*` versus `index.html` and `favicon.ico`; the SPA fallback must set `no-cache`.
- `IDesktopWindowService` is the only entry point for activate, hide, browser fallback, and idempotent exit. Do not let tray, second-instance, or host-stop code manipulate the HWND independently.
- Preserve the WinUI title-bar design, system resize/menu/buttons/Snap Layout, 330 x 400 epx minimum content size, and read-only WebView host descriptor. Accept structured `themeChanged` messages only from the current actual loopback origin; do not restore web minimize/maximize/close messages or web drag regions.
- WebView2 data lives under `cache/webview2` within `AppData:Root`. If WebView2 is unavailable, initialization fails, the home probe fails, or navigation times out, open the default browser while keeping Kestrel and tray services available.
- Hide the loading overlay only after the first successful home navigation. Probe `GET /` through the proxy-disabled `ToolkitLoopback` client; keep the overlay and show a clear fallback error instead of exposing a raw 404.
- When the WinUI loop exits, clear `DispatcherQueueSynchronizationContext` before synchronously stopping ASP.NET Core.
- Preserve `CopyWinUIResourcesToPublishOutput`; publishing must fail if `App.xbf`, `MainWindow.xbf`, or `XUnityToolkit-WebUI.pri` is missing.
- In `Updater/Program.cs`, keep `WorkingDirectory = appDir` for both normal and rollback restarts.

### Runtime data and security

- Runtime data defaults to `%AppData%\XUnityToolkit` and may be overridden by `AppData:Root`. Centralize paths in `Infrastructure/AppDataPaths.cs`; `UpdateService` owns `update-staging/`.
- Exclude `cache/toolbox-agent-uploads/`, `toolbox-agent/conversations/`, and `runtime/toolbox-endpoint-v1.json` from settings import/export. Do not persist temporary attachment bytes or pending confirmations.
- Settings import must invalidate every affected service cache through the shared refresh path. Settings reset must schedule the exit-time `ToolboxDataResetService` flow rather than rebuilding sessions, logs, or WebView caches while the process is running.
- Encrypt API keys and SteamGridDB keys with DPAPI.
- Write JSON through `FileHelper.WriteJsonAtomicAsync`. Write other critical files with a temporary file plus atomic move.
- Use `PathSecurity.SafeJoin` for controlled path composition and `PathSecurity.ValidateExternalUrl` for external URLs. DNS results and every redirect hop must reject loopback, link-local, and private addresses as applicable.
- External image downloads must disable automatic redirects, validate every hop with `PathSecurity.SendWithValidatedRedirectsAsync(...)`, validate content type, and enforce the existing 10 MB response limit.
- ZIP imports must limit archive size, per-entry extracted size, and total extracted size and use `PathSecurity.PrepareZipExtractionPath(...)` plus `ExtractZipEntryAsync(...)`.
- All `multipart/form-data` endpoints must call `.DisableAntiforgery()`. Every `*-from-path` endpoint must apply the same validation and side effects as its multipart counterpart.
- Do not return internal absolute paths, stack traces, secrets, or sensitive configuration to the frontend; keep details in server logs.
- Analyze user DLLs only through passive PE/metadata APIs such as `PEReader` and `MetadataReader`. Never use `Assembly.Load*`, reflection, dependency injection, or another mechanism that loads or executes user code in the toolbox process.
- `QuickAccessHelper` uses Shell COM on an STA thread; release every COM object with `Marshal.ReleaseComObject`.

### Concurrency, SignalR, and logging

- Release a `SemaphoreSlim` only when the current path successfully acquired it; never release after a timeout or canceled wait.
- Preserve `BroadcastStats` throttling and `force: true` semantics. Any background or fire-and-forget operation that relies on SignalR to reset frontend state must broadcast completion or failure on success, error, and cancellation paths.
- `FileLoggerProvider`'s in-memory ring buffer is only for the live log view. `GET /api/logs/download` must return a snapshot of the current session's disk log, not a truncated ring-buffer export.
- Use shared-read modes for live BepInEx logs and other files that may be written by a running game. Keep passive analysis paths non-executing.

### Translation protocol, parsing, and persistence

- The online path is XUnity.AutoTranslator -> `LLMTranslate.dll` -> `POST /api/translate` -> `LlmTranslationService` -> DLL response.
- `LLMTranslate.dll` targets `net35` and resolves the backend in this order: valid `DiscoveryFile`, INI `ToolkitUrl`, then `http://127.0.0.1:51821`. Accept only `http://127.0.0.1:<valid-port>`.
- Preserve 10-second online heartbeats, 2-second offline rediscovery, the 140-second recovery budget with at least 30 seconds reserved before sending translation, offline concurrency of one, and retries only for connection-class errors.
- When `clientSessionId + requestId` is present, deduplicate concurrent work and cache the response for five minutes with the existing 1,000-entry cap. Preserve legacy cancellation semantics when IDs are absent.
- Treat a ping within 30 seconds as online. Display last translation time separately; it is not a heartbeat substitute.
- `TranslateDetailedAsync(...)` is authoritative. Use its persistence metadata whenever deciding whether to write translation memory, trigger term extraction, or update runtime context. `TranslateAsync(...)` is only a translations-only wrapper.
- Parse model output in this order: remove `<think>` and code-fence wrappers, JSON array, single JSON string for one-item requests, then a single raw-text candidate only for one-item compatibility. Never accept unstructured raw output for a batch.
- Run every accepted candidate through `TranslationOuterWrapperGuard`. If the source has no full-sentence outer wrapper, strip an added full-sentence wrapper such as `“”`, `「」`, `『』`, `【】`, `[]`, `""`, or `''`; reject empty results. Apply this consistently to all phases, TM hits, and persistence gates.
- Protect runtime placeholders before Phase 1 and Phase 2 with internal `{{XU_RT_n}}` tokens. Restore the exact original token spelling, brackets, case, count, and position for `[SPECIAL_01]`, `【SPECIAL_01】`, `{PLAYER}`, `{PC}`, `{Quest_Id}`, and other allowlisted forms. Any mismatch must fall back to the original source text.
- Validate the same placeholder round trip for Phase 0 TM hits; treat bad historical cache entries as misses.
- Results with `Persistable == false` may be returned to the caller but must not enter automatic term extraction, runtime context, or translation memory.
- Translation memory is per game, checks exact before fuzzy matches, updates memory synchronously, persists with a five-second debounce, and flushes dirty state on shutdown.
- `GlossaryExtractionService` owns runtime terminology candidate extraction. Every eligible translation path must call `BufferTranslation` and `TryTriggerExtraction` so extraction statistics remain correct.
- `TranslationStats.Queued` is a derived value and is not the internal `_queued` count. TM hits and failed-text statistics also have distinct meanings; do not merge them.
- `TermService` is the unified terminology store. `TermMatchingService` handles matches/placeholders and `TermAuditService` audits phases. Do not reintroduce separate glossary and do-not-translate storage.
- `ScriptTagService.NormalizeForCache` is the single cache-normalization entry point. Keep its use aligned across script tags and translation memory.
- `TranslationEditorPathResolver` is the only authority for the normal XUnity translation file. It follows `config.OutputFile` and `TargetLanguage` and must keep the result inside the game directory.
- Populate `RecentTranslation.EndpointName` explicitly with the existing `翻译记忆` label for TM hits.
- Preserve the Phase 0 TM, Phase 1 natural translation, Phase 2 terminology/DNT substitution, and Phase 3 forced-correction pipeline.

### LLM endpoints and local llama.cpp

- `ApiEndpointConfig.ApiFormat` supports `ChatCompletions` and `Responses`. The backend default must remain `ChatCompletions` for old settings; the frontend may explicitly default new OpenAI, DeepSeek, and Qwen endpoints to `Responses`.
- Build Responses requests in `LlmApiAdapter` with `/responses`, `instructions`, `input`, and `reasoning.effort`. Parse `output`, skip reasoning items, and accept only `message.content[]` entries with `type == output_text`; never fall back to `choices[0]`.
- `ReasoningEffort.Default` means do not override provider behavior; `None` means explicitly request disabled thinking. Keep provider-specific mappings in `LlmApiAdapter` and cover them with tests rather than applying one generic payload shape.
- Keep cloud default models synchronized between `LlmTranslationService.GetDefaultModel(...)` and `AiTranslationCard.vue`: `gpt-5.6-luna`, `claude-sonnet-5`, `gemini-3.6-flash`, `deepseek-v4-flash`, `qwen3.7-plus`, `glm-5.2`, and `kimi-k2.6`.
- `LocalLlmService` prefers DXGI GPU detection with WMI fallback; NVIDIA maps to CUDA, AMD/Intel to Vulkan, and no GPU to CPU. Local mode uses conservative concurrency and batching.
- llama.cpp is pinned to `b10375`. Keep that value synchronized in `build.ps1`, `.github/workflows/build.yml`, `LocalLlmService.LlamaVersion`, asset naming, and documentation.
- Launch models only through `LocalLlmLaunchPathResolver`: relative path first, Windows 8.3 short path second, then an ASCII hard-link or symbolic-link alias in `llama/launch-cache/`.
- Keep llama runtime binaries (`bundled/llama/` or downloaded runtime) distinct from model files under `%AppData%\XUnityToolkit\models`.

### Font replacement and generation

- `FontReplacementService` supports TMP assets and Unity Legacy `Font`. Replace `dynamicEmbedded` TTF/OTF data directly and convert supported `osFallback` or name-mapped dynamic fonts to embedded data while retaining original `FontNames` as fallback.
- Report Legacy fonts with `CharacterRects` static atlases or unknown modes as unsupported. Scan `staticAtlas` and `unknown`, but do not replace them.
- Legacy `m_FontData` often has `vector -> Array -> char` shape and may expose `Value == null`. Use `m_FontData["Array"].Children.Count` for byte length when appropriate, not only `AsByteArray`.
- Legacy byte-array elements may be `UInt8` or `Int8/char`; use `AsByte` or `AsSByte` according to `AssetValueType` to avoid signed overflow during `SetNewData`.
- Keep custom sources isolated by game under `custom-fonts/<gameId>/ttf/` and `custom-fonts/<gameId>/tmp/`. Allow cumulative sources and carry a per-font `sourceId` through requests, status, and backup manifests.
- `POST /font-replacement/scan` is authoritative for live `ttfMode` and `fontDataSize`. `GET /font-replacement/status` primarily summarizes `manifest.json`, sources, backups, and external restore state.
- Create backups before replacement; restoration depends on the manifest and hashes.
- `TmpFontGeneratorService` uses FreeTypeSharp plus Felzenszwalb EDT. Atlas size, padding, gradient scale, and render mode are coupled; do not change one in isolation.
- `RuntimeFontLoader/` produces BepInEx 5 Mono `net35` and BepInEx 6 IL2CPP `net6.0` plugins with ID `com.xunitytoolkit.runtimefontloader`. `prepare-references.ps1` extracts references from pinned BepInEx archives.
- `RuntimeTmpFontService` owns `GET/POST/DELETE /api/games/{id}/tmp-font`. New installs use built-in TTF source `ttf-default` in `fallback` mode; `override` mode uses the `XUnityToolkit.RuntimeFont` sentinel for XUnity 5.6.1.
- Runtime font configuration, manifest, and status live at `BepInEx/config/com.xunitytoolkit.runtimefontloader.cfg`, `.manifest.json`, and `.status.json`. Show a pending-restart state until a newer runtime status timestamp appears.
- Accept only valid TTF/OTF files inside `BepInEx/Font`; reject traversal, reparse points, bad magic, and SHA-256 mismatch. Never silently replace a failing custom font with a built-in asset.
- `BundledFontCatalog` is authoritative: runtime uses `SourceHanSansCN-VF.ttf` 2.005R and Legacy replacement uses `SourceHanSansCN-Regular.otf`. Ship the source manifest and OFL license.

### Toolbox agent and plugin health

- Toolbox conversations, plugin diagnosis, and repair planning use the single global cloud endpoint selected by `ToolboxAgentEndpointResolver` from `AiTranslation.AgentEndpointId`. `null` selects the highest-priority valid enabled non-local endpoint. An explicitly selected invalid endpoint fails closed; never fall back. Local llama is unsupported for agent work.
- Endpoint selection is independent of real-time translation `ActiveMode` and `AiTranslation.Enabled`. Expose only endpoint ID, name, provider, and model summaries; never expose URL or API keys, and do not let the chat request choose an endpoint.
- `GET /api/games/{id}/health-check` refreshes objective local facts only and must never call a model. Install-time `VerifyForInstallAsync(...)` also performs only local file, startup-log, and ping checks and must state that AI was not called.
- `PluginHealthCheckService` owns objective facts, `PluginDiagnosticAgentService` owns explicitly triggered two-stage cloud diagnosis and constrained repair planning, and `PluginAutoRepairService` alone executes validated backup/repair steps. Do not restore generic regex-based canned causes or advice.
- `PluginHealthReport.analysisState` is one of `NotRun`, `Running`, `Completed`, `Stale`, `Unavailable`, or `Failed`. Deterministic local errors take priority; evidence-validated AI warnings/errors may lower status. `Healthy` requires a fresh run, new log, ping, clean local facts, and no valid AI issue.
- Serialize analysis per game. Cache reports only in process and fingerprint log, configuration, plugin metadata, and environment evidence. Stale reports may display but must not influence current health.
- `PluginDiagnosticArtifactCollector` may collect only in-game Doorstop/BepInEx/XUnity configuration, BepInEx logs, third-party `.cfg`, passive plugin PE metadata/references, and essential game-file facts. Reject traversal and reparse points along the full parent chain; use shared reads and enforce file, line, and total-context limits.
- Treat logs, configuration, paths, game names, script output, and plugin metadata as untrusted prompt content. Never send complete settings, API keys, tokens, passwords, authorization headers, cookies, or sensitive URL parameters to a model. Keep generic API bridge results redacted.
- User-added game directories and the complete `AppData:Root` are explicit trusted roots for toolbox file access. Selected raw content and absolute paths from those roots may be sent to the configured cloud endpoint; content outside them remains subject to the per-operation external-read confirmations below.
- Validate stage-one artifact IDs against the current backend list. Every reported issue needs a backend-validated artifact ID and line number; generate displayed excerpts on the backend from selected lines. Allow one structured-JSON repair attempt, then keep local facts and mark failure rather than falling back to regex rules.
- `POST /api/games/{id}/bepinex-log/analyze` is only a compatibility adapter over the shared structured report. It must not own a separate prompt or call the model again. Render all model text as plain text; never use `v-html`.
- `PluginDiagnosticReport.vue` is shared by the health card and BepInEx log view. Both surfaces must show the same analysis time, endpoint, evidence, key artifacts, truncation, and staleness state.
- Automatic repair is limited to `set_ini_value`, `disable_plugin`, and `reinstall_component`. Configuration targets must come from reviewed artifacts in the Doorstop/BepInEx scope; third-party plugins may only be renamed disabled; toolbox components may only be restored from bundled packages. Never silently overwrite unknown/custom `LLMTranslate.dll`, and skip game-file changes while the game is running.
- Show repair only when a deterministic plan is non-empty or a fresh completed AI report contains a medium/high-confidence evidence-backed warning/error. Back up every specialized repair target under `backups/<gameId>/agent-repair/`, then recollect facts. Static verification is not proof of a successful game run.
- The generic `ToolboxAgentToolExecutor` API bridge may call only the current `127.0.0.1` toolbox and must exclude recursive agent APIs, host browsing, complete settings/secrets, settings reset, update apply, and binary downloads. DELETE, uninstall, import, process launch, and other high-impact actions require UI confirmation.
- `ToolboxAgentHostAccessService` `game` and `toolbox` scopes accept relative paths only and reject traversal, alternate data streams, and reparse-point escape. They may read original content and, after one `manage_files` batch confirmation, create, overwrite, copy, move, rename, or delete files, but must not modify/delete the trusted root itself. Generic operations do not create backups automatically.
- `external` scope accepts ordinary absolute paths for read-only access. Confirm every directory enumeration, file read, and chunk separately, displaying purpose, resolved path, and raw content to be sent. Reject all external writes. Binary access is limited to passive hashes, signatures, PE/assembly/ZIP metadata, and bounded hex blocks.
- `run_script` requires separate confirmation for every PowerShell or CMD script, showing purpose, host, timeout, full script, and the fact that the backend cannot prove it read-only. Run only with current-user permissions and no elevation. Prompts must forbid system mutation, trusted-root bypass, or performing external-environment repairs.
- `reset_toolbox_data` is a terminal, dedicated confirmation action. Copy `Updater.exe` to the system temp directory, wait for exit, delete the complete `AppData:Root`, and restart. Never delete game directories, create a backup, call the model again, or persist new conversation data after reset is scheduled.
- Keep at most 100 conversations, 200 visible messages per conversation, and the existing 40-message/80,000-character model-context limits. A new conversation must not delete old history; never restore pending confirmations after restart.
- Attachments are session-isolated and temporary. Enforce existing type/size limits, do not send binary bytes to the model, and do not persist pending operations. `apply_custom_font` validates/registers TTF/OTF and delegates to `RuntimeTmpFontService`; it must not generate a TMP bundle or rewrite game assets. Both `apply_custom_font` and `configure_tmp_runtime_font` require confirmation.

### Build, packaging, updater, and CI

- `build.ps1` and `.github/workflows/build.yml` are independent implementations. Change both when build inputs, downloaded assets, runtime font references, endpoint embedding, editions, smoke behavior, or packaging changes.
- Keep pinned BepInEx 5.4.23.5, BepInEx 6 BE 785, XUnity.AutoTranslator 5.6.1, and llama.cpp versions synchronized with dependency automation and documentation.
- Release builds must build and embed `LLMTranslate.dll`; missing `TranslatorEndpoint/libs` references are fatal. The endpoint keeps its own fixed semantic/informational version; do not pass the main timestamp version, edition, or debug properties into it. Verify the endpoint SHA-256 does not change across main publish.
- Official endpoint identity requires both embedded SHA-256 metadata and `translator-endpoint-metadata.json`. Call a DLL current official only on exact hash match. A metadata-compatible unknown hash may be `CompatibleCurrent` but must not be called hash-verified or overwritten silently. Inspect metadata passively.
- The current app publish is multifile, not single-file. Preserve `SatelliteResourceLanguages=en`, the WinUI XBF/PRI copy checks, PowerShell ZIP handling, component ZIP naming, and edition behavior.
- Pass timestamped product versions through `InformationalVersion`; do not overload `Version` and trigger `AssemblyVersion` overflow.
- `Updater` is an AOT project. Do not add WinForms, reflection-based JSON serialization, or the normal managed `Microsoft.Win32.Registry` wrappers; use P/Invoke for required registry access.
- `app-file-inventory-v1.json` is authoritative for application component files, including root files, `runtimes/`, and WinUI native resource directories while excluding `wwwroot/`, `bundled/`, `data/`, and `appsettings*`. CI component ZIPs and remote manifests must match it exactly.
- Incremental deletion should use the installed inventory. Only legacy installs without an inventory may use the bounded fallback rules for root files, `runtimes/`, `wwwroot/`, and `bundled/`; never delete extra user files through the new inventory path.
- Keep `Installer/Installer.wixproj` on WixToolset v7 with `<AcceptEula>wix7</AcceptEula>`. Recheck the EULA ID and maintenance terms when upgrading WiX. Clean `Installer/obj/...` between edition builds.
- Preserve per-user MSI behavior, `MajorUpgrade` scheduling, optional `%AppData%\XUnityToolkit` removal, registry synchronization, license consistency, localized strings, and `SuppressValidation=true`.
- CI does not invoke `build.ps1`. Review `.github/workflows/build.yml`, `release.yml`, and `dep-check.yml` directly. Dependency checks track BepInEx and XUnity; llama.cpp remains manually pinned.
