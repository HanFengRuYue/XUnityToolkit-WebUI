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
- **Real-time:** SignalR for install progress updates
- **Persistence:** JSON files at `%APPDATA%/XUnityToolkit/` (`library.json`, `settings.json`)
- **System Tray:** NotifyIcon on dedicated STA thread; auto-opens browser, hides console on startup
- **TranslatorEndpoint:** netstandard2.0 class library producing `LLMTranslate.dll` — XUnity.AutoTranslator custom translation endpoint that forwards game text to the toolkit backend's `POST /api/translate`; hand-rolled JSON (no System.Text.Json in Unity Mono); embedded as resource in main project and deployed to `BepInEx/plugins/XUnity.AutoTranslator/Translators/` during install; configurable `MaxConcurrency` (default 10, max 20), `MaxTranslationsPerRequest` (default 10, max 50), `DebugMode`, `GameId`, `DisableSpamChecks` (default true), `TranslationDelay` (default 0.1s) via `[LLMTranslate]` INI section
- **AI Translation:** `LlmTranslationService` calls LLM APIs (OpenAI/Claude/Gemini/DeepSeek/Qwen/GLM/Kimi/Custom); multi-provider load balancing with priority + speed/error scoring; per-text parallel API calls bounded by `SemaphoreSlim(maxConcurrency)` with separate `_translating` (active API calls) and `_queued` (waiting for semaphore) counters; settings in `AppSettings.AiTranslation` with `List<ApiEndpointConfig>` for multiple providers; per-game glossary stored at `%APPDATA%/XUnityToolkit/glossaries/{gameId}.json` (applied via prompt injection + post-processing); token usage extracted from API responses; recent translations circular buffer (10 entries); enable/disable toggle via `POST /api/ai/toggle`; model list fetching via `GET /api/ai/models`; test translation via `POST /api/translate/test` (single provider or all providers); configurable port (read from settings.json at startup); real-time stats pushed via SignalR; dedicated `/ai-translation` page with status dashboard (tokens, speed, translating/queued counts, recent translations) + multi-provider management; per-game AI engine install/uninstall + glossary management via `GET/PUT /api/games/{id}/glossary`

### Frontend Structure

```
XUnityToolkit-Vue/src/
├── api/            # API client and TypeScript types
├── assets/         # Global CSS (design system variables, animations)
├── components/
│   ├── layout/     # AppShell (sidebar + content layout)
│   ├── config/     # ConfigPanel (translation settings form)
│   ├── library/    # GameCard, CoverPickerModal (game library grid/cover management)
│   └── progress/   # InstallProgressDrawer (SignalR progress)
├── composables/    # Reusable composition functions (useAddGameFlow)
├── stores/         # Pinia stores (games, install, theme, aiTranslation)
├── views/          # LibraryView, GameDetailView, AiTranslationView, SettingsView
└── router/         # Vue Router config (/, /games/:id, /games/:id/config-editor, /ai-translation, /settings)
```

### Frontend Design System

- **Fonts:** Lexend (headings), DM Sans (body), JetBrains Mono (mono) — loaded via Google Fonts in `index.html`
- **Colors:** Deep dark base (`#0b0b11`), teal accent (`#22d3a7`), violet secondary (`#a78bfa`)
- **CSS Variables:** Defined in `main.css` (`--bg-root`, `--accent`, `--text-1`, `--radius-lg`, `--ease-out`, etc.)
- **Theme:** Dark/light mode via `data-theme` attribute on `<html>`; Pinia `useThemeStore` manages mode + localStorage persistence; Naive UI uses `darkTheme`/`null` with separate `GlobalThemeOverrides` per mode in `App.vue`
- **Animations:** Keyframes in `main.css` (`slideUp`, `fadeIn`, `floatIn`, `shimmer`, `breathe`, `pulse`); page transitions via Vue `<Transition name="page">`
- **Layout:** Custom sidebar (230px) + scrollable content area; no NLayout/NLayoutSider
- **Responsive Breakpoints:** 768px (tablet — sidebar collapses to slide-over drawer), 480px (phone — single-column layouts)
- **Mobile Navigation:** AppShell uses hamburger menu + overlay sidebar on ≤768px; sidebar state managed via `sidebarOpen` ref
- **Game library:** Grid (default) + list dual view; grid uses 2:3 aspect-ratio GameCard with cover images (Steam CDN / SteamGridDB / upload), hover play button, status badge; list retains row layout with exe icon, name/path, tags, play button; view mode + sort persisted to settings
- **Section cards:** `.section-icon` (30px colored icon box) + `.section-title` with `display: flex; gap: 10px`; color variants via classes (`.download`, `.translate`, `.warning`)
- **Info cards:** `.info-card` with `.info-card-icon` (36px, colored variants: `.folder`/`.file`/`.unity`/`.code`/`.arch`) for dashboard-style info display
- **Page layouts:** Views use full-width (no max-width); two-column grids (`.detail-columns`, `.settings-grid`) collapse to single column at 960px/768px
- **Settings page layout:** `.settings-page` uses `display: flex; flex-direction: column; gap: 16px` — adding new full-width section cards automatically gets correct spacing; do NOT add individual `margin-bottom` to cards
- **AppShell 内容区**：`.main-content` padding 为 `36px 40px`（桌面）、`20px 16px`（平板）、`16px 12px`（手机）；需要全高布局的页面用 `calc(100vh - 72px)` 等扣除 padding

## Code Conventions

- **Target Framework:** net10.0-windows
- **Root Namespace:** XUnityToolkit_WebUI
- **Nullable reference types:** Enabled
- **Implicit usings:** Enabled
- **Frontend:** Vue 3 Composition API with `<script setup lang="ts">`
- **Frontend styling:** Scoped `<style scoped>` per component; use CSS variables from `main.css` for theming
- **Frontend icons:** `@vicons/material` and `@vicons/ionicons5` wrapped in Naive UI `NIcon`

## API Endpoints

- Add game with detection: `POST /api/games/add-with-detection` — auto-detects exe, Unity status, installed plugins/frameworks; returns `AddGameResponse { needsExeSelection, game }`
- Framework uninstall: `DELETE /api/games/{id}/framework/{framework}` — uninstalls non-BepInEx mod frameworks (MelonLoader, IPA, ReiPatcher, Sybaris, UnityInjector, Standalone)
- Game icon extraction: `GET /api/games/{id}/icon` — priority: custom icon (`{gameId}.custom.png`) > exe icon (`{gameId}.png`); exe icon extracted via `System.Drawing.Icon.ExtractAssociatedIcon()`, cached at `%APPDATA%/XUnityToolkit/cache/icons/`; custom icons auto-saved from SteamGridDB `icons/game/{id}` endpoint during cover save, or manually uploaded via `POST /api/games/{id}/icon/upload`; `DELETE /api/games/{id}/icon/custom` removes custom icon
- Game rename: `PUT /api/games/{id}` with `{ name }` — frontend supports inline editing (GameDetailView) and right-click context menu (LibraryView)
- Game actions: `POST /api/games/{id}/open-folder` (opens explorer), `POST /api/games/{id}/launch` (runs game exe)
- Cache management: `GET /api/cache/downloads` (info), `DELETE /api/cache/downloads` (clear) — manages download cache at `%APPDATA%/XUnityToolkit/cache/`
- Settings: `GET /api/settings` (read), `PUT /api/settings` (save), `GET /api/settings/version` (app version), `POST /api/settings/reset` (delete all app data: settings, library, cache, backups)
- Config: `PUT /api/games/{id}/config` uses `PatchAsync` — reads existing INI, modifies only specified keys, preserves all other content (comments, unknown sections); `GET/PUT /api/games/{id}/config/raw` — 读写原始 INI 文件内容（用于配置编辑器）
- AI Translation: `POST /api/translate` — receives `{texts, from, to}` from in-game DLL, calls LLM provider, returns `{translations: [...]}`; **不使用 `ApiResult` 包装**（因为游戏内 DLL 直接调用），前端调用此端点必须用原始 `fetch` 而非 `api.post`（会自动解包 ApiResult）；errors: 400 (no API key), 502 (LLM API failure)
- AI Translation Stats: `GET /api/translate/stats` — returns `ApiResult<TranslationStats>` with in-memory session counters; `Translating` (active API calls) and `Queued` (waiting for semaphore) replace the old `InProgress` field
- AI Toggle: `POST /api/ai/toggle` — enable/disable AI translation; persists to settings; returns 503 when disabled
- AI Models: `GET /api/ai/models?provider=&apiBaseUrl=&apiKey=` — fetch model list from provider API
- AI Test: `POST /api/translate/test` — accepts `{ endpoints, systemPrompt, temperature }`, tests specified providers in parallel; returns `ApiResult<IList<EndpointTestResult>>`; uses provided config directly (no need to save first)
- Glossary: `GET/PUT /api/games/{id}/glossary` — per-game glossary CRUD; stored at `%APPDATA%/XUnityToolkit/glossaries/{gameId}.json`
- AI Endpoint Management: `GET/POST/DELETE /api/games/{id}/ai-endpoint` — check/install/uninstall `LLMTranslate.dll` for a specific game; POST requires `FullyInstalled` state
- Cover Images: `GET /api/games/{id}/cover` (raw bytes, no ApiResult, 404 if none), `POST /api/games/{id}/cover/upload` (multipart, 5MB limit, `.DisableAntiforgery()`), `POST /api/games/{id}/cover/search` (SteamGridDB game search), `POST /api/games/{id}/cover/grids` (SteamGridDB cover list), `POST /api/games/{id}/cover/select` (download + save selected cover), `POST /api/games/{id}/cover/steam-search` (Steam Store free search, no API key), `POST /api/games/{id}/cover/steam-select` (download Steam CDN cover by AppID), `DELETE /api/games/{id}/cover`; covers cached at `%APPDATA%/XUnityToolkit/cache/covers/{gameId}.img` + `.meta`; cover sources: `"steam"`, `"steamgriddb"`, `"upload"`
- **ApiResult 模式**：`ApiResult<T>.Ok(data)` 需要 data 参数；无数据成功响应用 `ApiResult.Ok()`（非泛型静态类）；request record 定义在对应 Endpoints 文件底部

## Development Notes

- `Game.IsUnityGame` defaults to `true` for backward compatibility — existing `library.json` entries without this field deserialize correctly
- `Game.DetectedFrameworks` stores detected mod frameworks (BepInEx, MelonLoader, IPA, etc.) — null when none detected
- `PluginDetectionService` detects 7 mod frameworks + XUnity.AutoTranslator variants across all frameworks; uninstalls non-BepInEx frameworks
- `UnityDetectionService.CheckIsUnityGame()` uses 5 heuristics: `{Name}_Data/`, `UnityPlayer.dll`, `GameAssembly.dll`, `globalgamemanagers`/`data.unity3d`, `MonoBleedingEdge/`
- Add-game flow: folder picker → auto-detect → if no exe found, prompt user to select exe within the directory → if not Unity, mark as non-Unity (limited features); detects `steam_appid.txt` in game root → sets `Game.SteamAppId` → auto-fetches cover from Steam CDN (best effort)
- Non-Unity games: only show run game + open folder in GameDetailView; hide install management and config cards; show "非 Unity" tag in LibraryView
- Use composables (`src/composables/`) for complex multi-step UI flows that involve dialogs and API calls — keeps stores and views clean
- Path security: when validating a file is inside a directory, normalize with `TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar` to handle root drive paths
- **`volatile` vs `Volatile.Read` 互斥**：同一字段上同时使用 `volatile` 声明和 `Volatile.Read(ref field)` 会产生 CS0420 警告；需要精确控制内存序时，优先用 `Volatile.Read`/`Volatile.Write` 而非 `volatile` 关键字
- **BroadcastStats 节流**：`LlmTranslationService.BroadcastStats(force)` 使用 `Interlocked.CompareExchange` CAS 模式做 200ms 节流，减少高并发时 SignalR 广播频率；`force: true` 用于批次完成（`TranslateAsync.finally`）和错误记录（`RecordError`）等高价值事件
- **XUnity.AutoTranslator 配置文件**：实际文件名是 `AutoTranslatorConfig.ini`（不是 BepInEx GUID 风格的 `gravydevsupreme.xunity.autotranslator.cfg`），路径 `BepInEx/config/AutoTranslatorConfig.ini`
- **P/Invoke on .NET 10**：使用 `[DllImport]` 而非 `[LibraryImport]`，后者需要 `<AllowUnsafeBlocks>true</AllowUnsafeBlocks>`，当前 csproj 未启用
- **重命名公共方法时**：全局搜索所有调用点（Endpoints、Services、Orchestrator），避免遗漏编译错误
- **INI 配置修改策略**：不要从零生成 XUnity 配置文件（会丢失插件自动生成的默认值），应使用 `PatchAsync` 读取-修改-写回
- **INI Section 兼容**：`[TextFrameworks]`（复数）为主、`[TextFramework]`（单数）为旧兼容；`PatchAsync` 检测文件中实际存在的 section 名来决定写入目标
- **INI PatchAsync 空值语义**：`BuildSectionMods` 中 `null` = 不修改（跳过），`""` = 清空值（写入 `Key=`）；`AddEngineKeyModification` 仍对 null 和 "" 都跳过（不删除已有 API key）
- **Console logging:** Configured programmatically in `Program.cs` (`ClearProviders` + `AddSimpleConsole`) — appsettings.json logging config was unreliable; all log messages are in Chinese
- **Windows console encoding:** `Console.OutputEncoding = UTF8` MUST be set before `WebApplication.CreateBuilder()`, otherwise Chinese characters will be garbled (the logging system captures encoding at init time)
- Named `HttpClient` instances: `"GitHub"` (API calls with GitHub headers), `"Mirror"` (mirror downloads, no API headers), `"LLM"` (AI translation, 120s timeout, `SocketsHttpHandler` with `MaxConnectionsPerServer=200`), `"SteamGridDB"` (cover image API, 30s timeout, base URL `https://www.steamgriddb.com/api/v2/`) — mirror URLs embed the original URL as path (e.g., `https://ghfast.top/https://github.com/...`); `GitHubReleaseService` reads mirror base URL from `AppSettingsService` at download time (empty = direct connection, no mirror fallback); `CreateClientForUrl` checks mirror host dynamically
- Download resilience: `GitHubReleaseService.DownloadAssetAsync` has retry (3 attempts, exponential backoff) + auto mirror fallback; progress reported via `IProgress<DownloadProgress>` (percent + speed + retry message)
- `InstallationStatus` has `DownloadSpeed` and `RetryMessage` fields — reset both to null on step transitions in `InstallOrchestrator.UpdateStatus`
- `dotnet build` automatically runs `npm install` + `npm run build` via `BuildFrontend` MSBuild Target in csproj; pass `-p:SkipFrontendBuild=true` to skip
- `build.ps1` builds frontend once, then TranslatorEndpoint (if XUnity reference DLLs present), then publishes self-contained single-file exe for each target runtime; output to `Release/{rid}/`
- **TranslatorEndpoint 依赖**：`TranslatorEndpoint/libs/` 下需要 `XUnity.AutoTranslator.Plugin.Core.dll` 和 `XUnity.Common.dll`（从 GitHub `bbepis/XUnity.AutoTranslator` releases 下载 BepInEx zip 提取）；目录已 gitignore
- **XUnity API 命名空间**：`XUnityWebRequest` 在 `XUnity.AutoTranslator.Plugin.Core.Web`，`HttpEndpoint`/`IHttpRequestCreationContext` 在 `XUnity.AutoTranslator.Plugin.Core.Endpoints.Http`，`IInitializationContext` 在 `XUnity.AutoTranslator.Plugin.Core.Endpoints`
- **XUnity 翻译速度优化**：`IInitializationContext.DisableSpamChecks()` 禁用文本稳定等待（默认约 1 秒延迟）；`SetTranslationDelay(float)` 设置翻译间隔（最小 0.1 秒）；两者在 `Initialize()` 中调用，通过 `GetOrCreateSetting` 暴露为 INI 配置；v5.4.3+ 可用
- **TranslatorEndpoint 约束**：目标 net35（通过 `Microsoft.NETFramework.ReferenceAssemblies.net35` NuGet 包让 `dotnet build` 支持）；最大兼容性，覆盖 Unity 5.x~2022+ 所有 Mono 运行时；C# 7.3 语言版本；不能使用 System.Text.Json，需手写 JSON 序列化/解析
- **LLM Provider base URLs:** DeepSeek (`https://api.deepseek.com/v1`), Qwen (`https://dashscope.aliyuncs.com/compatible-mode/v1`), GLM (`https://open.bigmodel.cn/api/paas/v4`), Kimi (`https://api.moonshot.cn/v1`) — all use OpenAI-compatible protocol (`CallOpenAiCompatAsync`); Claude and Gemini have dedicated implementations
- **App URL:** Default `http://127.0.0.1:51821`, configurable via `settings.json` `aiTranslation.port`; port read with `JsonDocument.Parse` before `WebApplication.CreateBuilder()` (DI not yet available); `SystemTrayService.AppUrl` reads from `IConfiguration["urls"]`; **必须使用 `127.0.0.1` 而非 `localhost`**（Unity Mono 运行时将 `localhost` 解析为 IPv6 `::1`，导致与 IPv4 后端连接挂起）
- **Publish cleanup:** Remove `web.config`, `*.pdb`, `*.staticwebassets.endpoints.json` from publish output — not needed for this desktop app
- Stop the running backend before `dotnet build` — the exe is locked while running
- Frontend changes require `npm run build` then restart backend to take effect (unless using `npm run dev`)
- Backend and frontend share `InstallStep` enum — adding/modifying步骤需同步 4 处: `Models/InstallationStatus.cs` (C# enum)、`src/api/types.ts` (TS type)、`InstallProgressDrawer.vue` (`installSteps` 数组)、`InstallOrchestrator.cs` (调用处)；`GeneratingConfig` 步骤启动游戏生成配置，`ApplyingConfig` 步骤应用最佳默认+用户配置
- `XUnityConfig` has typed API key fields (e.g., `DeepLLegitimateApiKey`) stored in engine-specific INI sections (e.g., `[DeepLLegitimate]`); `ConfigurationService.PatchAsync` reads existing INI and only modifies specified keys; engine sections use XUnity's actual section names (`[Baidu]`, `[Yandex]`, `[GoogleLegitimate]`, etc.) with backward-compat reading from old names
- `ConfigurationService` constructor injects `AppSettingsService` (for reading port in `ApplyOptimalDefaultsAsync`); `PatchSectionAsync(gamePath, section, keys)` patches a single INI section — used by `POST /api/games/{id}/ai-endpoint` to set `[LLMTranslate] ToolkitUrl`
- **AI endpoint 安装附带配置修补**：`POST /api/games/{id}/ai-endpoint` 除部署 DLL 外，还写入 `[LLMTranslate] ToolkitUrl=http://127.0.0.1:{port}` 到 INI（避免旧 `localhost` 配置残留）；`ApplyOptimalDefaultsAsync` 也写入此配置
- **翻译请求诊断日志**：`Program.cs` 中间件对 `POST /api/translate` 记录来源 IP 和路径（logger category `XUnityToolkit_WebUI.TranslateMiddleware`），方便排查游戏插件连接问题
- `SystemTrayService` runs `NotifyIcon` on a dedicated STA thread; shows `ShowBalloonTip` on startup to notify user app is running in tray; `StopAsync` only calls `Application.Exit()` — the STA thread owns the NotifyIcon lifecycle via `using` statement
- `AppSettingsService` persists settings to `%APPDATA%/XUnityToolkit/settings.json` using semaphore + atomic write; in-memory `volatile` cache — `GetAsync()` returns cached on fast path (no lock/disk I/O); `SaveAsync()` updates cache; `UpdateAsync(Action<AppSettings>)` does read-clone-mutate-save (safe for cached objects); `InvalidateCache()` forces reload; callers must NOT mutate the returned `AppSettings` directly — use `UpdateAsync` or `SaveAsync` with a new object
- `GlossaryService` uses `ConcurrentDictionary<string, List<GlossaryEntry>>` cache per game; `GetAsync` returns cached on fast path; `SaveAsync` updates cache after disk write
- Install store's `operationType` field tracks whether current operation is install or uninstall (do not infer from transient step values)
- **前端状态生命周期**：`GameDetailView.loadGame()` 在 `isInstalled=false` 时必须重置 `config.value = null`；任何新增的"仅已安装时加载"的状态也需同样处理
- **Theme-aware CSS:** Use semantic CSS variables (`--bg-subtle`, `--bg-muted`, `--bg-subtle-hover`, `--bg-muted-hover`) for semi-transparent overlay backgrounds — never hardcode `rgba(255,255,255,...)` in scoped CSS as it breaks light mode
- **Naive UI light theme:** Pass `null` (not `lightTheme`) as the `:theme` prop; accent colors need slightly darker values in light mode for contrast (e.g., `#22d3a7` → `#19b892`)
- Pinia stores: `games` (game management), `install` (installation progress + SignalR), `theme` (dark/light mode + localStorage), `aiTranslation` (AI translation stats + SignalR)
- Naive UI `NDrawer` width prop only accepts numbers (not CSS strings) — use `window.resize` listener + ref for responsive drawer width
- Naive UI `NForm` label-placement must be toggled dynamically (via computed) for mobile — cannot use CSS media queries alone
- **Naive UI NInput 绑定 `string?` 字段**：不要用 `v-model:value`（会收到 `undefined` 导致运行时警告），应使用 `:value="form.field ?? ''"` + `@update:value` 模式；API key 字段用 `v || undefined` 防止发送空字符串
- **`v-show` + `loading="lazy"` 死锁**：`v-show` 设置 `display: none` → 元素无布局尺寸 → `IntersectionObserver`（lazy loading）永远不触发加载；改用 `opacity: 0` + `position: absolute` 保持布局，让 lazy loading 正常工作
- **Vue `v-for` 组件 prop 变化时 ref 不重置**：key 不变但 prop 数据更新时，组件内 ref 保持旧值；对依赖 prop 计算的 URL 加 `watch` 来重置加载状态（如 `coverLoaded`/`coverError`）
- **NInput `@blur` + `@keyup.enter` 双重触发**：Enter 键触发 handler → handler 移除 NInput → 触发 blur → 再次调用 handler；需在 handler 开头加 flag 守卫（`if (!editing.value) return`）
- **TypeScript 对象更新**：更新 `ApiEndpointConfig` 等类型化对象时，用 `Object.assign({}, obj, patch)` 而非 `{ ...obj, ...patch }`（spread 会产生 `Partial<T>` 类型不兼容错误）
- After frontend changes, always verify with both `npx vue-tsc --noEmit` (type-check) and `npm run build` before considering done
- Verify `@vicons/material` icon availability before importing: `node -e "const m = require('@vicons/material'); console.log(m['IconName'] ? 'YES' : 'NO')"`
- Stop backend on Windows: `taskkill //f //im XUnityToolkit-WebUI.exe`
- **`Volatile.Read<T>` 要求引用类型**：`DateTime?`（`Nullable<DateTime>`）是值类型，不能用 `Volatile.Read`；需线程安全读取时，改用 `long` 存储 ticks + `Interlocked.Read/Exchange`
- **SignalR hub 复用**：`InstallProgressHub` 同时服务安装进度（`game-{id}` 组）和 AI 翻译统计（`ai-translation` 组），避免开多个 hub；`LlmTranslationService` 通过 `IHubContext<InstallProgressHub>` 广播统计
- **Plugin vs Backend concurrency**: `LLMTranslateEndpoint.MaxConcurrency` (default 10, max 20) controls concurrent HTTP connections; `MaxTranslationsPerRequest` (default 10, max 50) controls texts per request; 实际同时翻译数 = MaxConcurrency × MaxTranslationsPerRequest（默认 10×10=100）；Mono 的 HttpWebRequest 异步实现无法处理超过 ~15 个并发连接（ThreadPool 死锁），所以必须通过批量打包而非增加连接数来提高吞吐量；`AiTranslationSettings.MaxConcurrency` (default 4, max 100) controls backend LLM API parallelism via `SemaphoreSlim`
- **XUnity HTTP 传输**：XUnity 的 `HttpEndpoint` 通过 `ConnectionTrackingWebClient` → `WebClient` → `HttpWebRequest` 发送请求（不是 `UnityWebRequest`）；Mono 的 `ServicePointManager.DefaultConnectionLimit` 默认为 **2**，严重限制并发；**必须用 `ServicePointManager.FindServicePoint(uri).ConnectionLimit`** 设置特定主机的连接限制（`DefaultConnectionLimit` 只影响之后新建的 ServicePoint）；**不要设置 `Connection: close`**（Mono 的 CLOSE_WAIT bug 会导致关闭的 socket 永久占用连接槽）
- **后端 semaphore 超时**：`TranslateSingleAsync` 的 `semaphore.WaitAsync` 有 60 秒超时，防止请求无限挂起；超时返回 503
- **翻译诊断**：后端日志写入 `%APPDATA%/XUnityToolkit/logs/app.log`（`FileLoggerProvider`，5MB 自动轮转）；`LlmTranslationService` 维护内存错误环形缓冲区（最近 50 条），通过 `TranslationStats.RecentErrors` + SignalR 推送到前端 AI 翻译页面的"错误日志"区域；`TotalReceived` 计数器可与游戏端"已发送"数对比诊断网络问题
- **SemaphoreSlim 并发模式**：`TranslateSingleAsync` 接收 `SemaphoreSlim semaphore` 参数（本地引用捕获），确保 `WaitAsync`/`Release` 操作同一实例；`EnsureSemaphore` 替换信号量时旧实例延迟 3 分钟 Dispose（等待所有在飞任务完成）；绝不能在有任务等待时立即 Dispose 旧信号量
- **"端点" vs "提供商" 术语**：安装器/DLL 上下文中的"翻译端点"指 `LLMTranslate.dll`（XUnity translator endpoint plugin）；AI 翻译设置中的"提供商"指 `ApiEndpointConfig`（LLM API 配置）——这是两个不同概念，不要混淆
- **Default system prompt:** Chinese, 7 rules for game translation (preserve placeholders, format, context). `{from}` and `{to}` are the only replaced placeholders; `{0}` etc. in rule text are literal examples
- **高频调用路径性能**：`POST /api/translate` 每秒可能收到 100+ 请求（XUnity 批量翻译）；此路径上的所有 I/O（设置读取、术语表读取）必须走内存缓存，不能每次请求都读磁盘；`SemaphoreSlim(1,1)` + 文件 I/O 会将并发请求串行化，导致快速 LLM API（如 DeepSeek）的有效并发数降到 1-2
- **Lazy-loaded modals:** Use `defineAsyncComponent(() => import(...))` for heavy modal components (e.g., CoverPickerModal) — keeps initial bundle small; declare in a separate `<script lang="ts">` block or at module top of `<script setup>`
- **Steam CDN 封面 URL**：竖版 `https://cdn.akamai.steamstatic.com/steam/apps/{appId}/library_600x900.jpg`（600x900，匹配 GameCard 2:3 比例），横版回退 `header.jpg`（460x215，CSS `object-fit:cover` 自动裁剪适配）；Steam Store 搜索 API `store.steampowered.com/api/storesearch/?term={query}&l=schinese&cc=cn` 免费无需 API Key
- **CoverPickerModal tabs**：三个 tab — Steam 搜索（默认，免费无需配置）、SteamGridDB 搜索（需 API Key）、自定义上传；Steam tab 打开时自动按游戏名搜索；`GameImageService` 中 Steam 方法使用 `httpClientFactory.CreateClient()`（无名称客户端，无 base URL）
- **Mono HTTP 调试经验**：诊断 Unity Mono 网络问题时，同时检查游戏端 BepInEx 日志（`BepInEx/LogOutput.log`）和后端日志（`%APPDATA%/XUnityToolkit/logs/app.log`）对比请求发送/接收数量；后端日志路径需用完整 Windows 路径（`C:/Users/.../AppData/Roaming/`），bash 的 `$APPDATA` 变量在某些 shell 环境下可能不可用
- **XUnity GetOrCreateSetting 语义**：`context.GetOrCreateSetting(section, key, default)` 读取已存在的 INI 值，仅在 key 不存在时才写入 default；修改 DLL 的默认值不会影响已安装的游戏——必须通过 `PatchSectionAsync` 在端点安装时显式覆写 INI 值
- **截图清理:** 每次使用 Playwright 或浏览器工具测试/调试后，必须删除产生的截图文件（项目根目录的 `*.png` 和 `.playwright-mcp/` 下的截图），不要将截图留在项目文件夹内
