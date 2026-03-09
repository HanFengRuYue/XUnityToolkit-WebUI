# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

XUnityToolkit-WebUI is a web-based tool for one-click installation of XUnity.AutoTranslator and BepInEx plugins into Unity games. It features a Vue 3 frontend served by an ASP.NET Core backend.

## Build Commands

```bash
# Build backend (also builds frontend automatically via MSBuild Target)
dotnet build XUnityToolkit-WebUI/XUnityToolkit-WebUI.csproj

# Run backend (serves the web UI on https://localhost:51821)
dotnet run --project XUnityToolkit-WebUI/XUnityToolkit-WebUI.csproj

# Build frontend (output to XUnityToolkit-WebUI/wwwroot/)
cd xunity-webui && npm run build

# Frontend dev server (proxies API to backend)
cd xunity-webui && npm run dev

# Type-check frontend
cd xunity-webui && npx vue-tsc --noEmit
```

## Architecture

- **Backend:** ASP.NET Core Minimal API (.NET 10.0, Windows Forms for native dialogs)
- **Frontend:** Vue 3 + TypeScript + Naive UI + Pinia (in `xunity-webui/`)
- **Real-time:** SignalR for install progress updates
- **Persistence:** JSON files at `%APPDATA%/XUnityToolkit/` (`library.json`, `settings.json`)
- **System Tray:** NotifyIcon on dedicated STA thread; auto-opens browser, hides console on startup

### Frontend Structure

```
xunity-webui/src/
├── api/            # API client and TypeScript types
├── assets/         # Global CSS (design system variables, animations)
├── components/
│   ├── layout/     # AppShell (sidebar + content layout)
│   ├── config/     # ConfigPanel (translation settings form)
│   └── progress/   # InstallProgressDrawer (SignalR progress)
├── composables/    # Reusable composition functions (useAddGameFlow)
├── stores/         # Pinia stores (games, install)
├── views/          # LibraryView, GameDetailView, SettingsView
└── router/         # Vue Router config (/, /games/:id, /settings)
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
- **Game list:** Full-width row layout with exe icon, name/path, tags, and status; staggered entrance animations
- **Section cards:** `.section-icon` (30px colored icon box) + `.section-title` with `display: flex; gap: 10px`; color variants via classes (`.download`, `.translate`, `.warning`)
- **Info cards:** `.info-card` with `.info-card-icon` (36px, colored variants: `.folder`/`.file`/`.unity`/`.code`/`.arch`) for dashboard-style info display
- **Page layouts:** Views use full-width (no max-width); two-column grids (`.detail-columns`, `.settings-grid`) collapse to single column at 960px/768px

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
- Game icon extraction: `GET /api/games/{id}/icon` — extracts icon from game exe via `System.Drawing.Icon.ExtractAssociatedIcon()`, cached at `%APPDATA%/XUnityToolkit/cache/icons/`
- Game actions: `POST /api/games/{id}/open-folder` (opens explorer), `POST /api/games/{id}/launch` (runs game exe)
- Cache management: `GET /api/cache/downloads` (info), `DELETE /api/cache/downloads` (clear) — manages download cache at `%APPDATA%/XUnityToolkit/cache/`
- Settings: `GET /api/settings` (read), `PUT /api/settings` (save), `GET /api/settings/version` (app version)
- Config: `PUT /api/games/{id}/config` uses `PatchAsync` — reads existing INI, modifies only specified keys, preserves all other content (comments, unknown sections)

## Development Notes

- `Game.IsUnityGame` defaults to `true` for backward compatibility — existing `library.json` entries without this field deserialize correctly
- `Game.DetectedFrameworks` stores detected mod frameworks (BepInEx, MelonLoader, IPA, etc.) — null when none detected
- `PluginDetectionService` detects 7 mod frameworks + XUnity.AutoTranslator variants across all frameworks; uninstalls non-BepInEx frameworks
- `UnityDetectionService.CheckIsUnityGame()` uses 5 heuristics: `{Name}_Data/`, `UnityPlayer.dll`, `GameAssembly.dll`, `globalgamemanagers`/`data.unity3d`, `MonoBleedingEdge/`
- Add-game flow: folder picker → auto-detect → if no exe found, prompt user to select exe within the directory → if not Unity, mark as non-Unity (limited features)
- Non-Unity games: only show run game + open folder in GameDetailView; hide install management and config cards; show "非 Unity" tag in LibraryView
- Use composables (`src/composables/`) for complex multi-step UI flows that involve dialogs and API calls — keeps stores and views clean
- Path security: when validating a file is inside a directory, normalize with `TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar` to handle root drive paths
- **XUnity.AutoTranslator 配置文件**：实际文件名是 `AutoTranslatorConfig.ini`（不是 BepInEx GUID 风格的 `gravydevsupreme.xunity.autotranslator.cfg`），路径 `BepInEx/config/AutoTranslatorConfig.ini`
- **P/Invoke on .NET 10**：使用 `[DllImport]` 而非 `[LibraryImport]`，后者需要 `<AllowUnsafeBlocks>true</AllowUnsafeBlocks>`，当前 csproj 未启用
- **重命名公共方法时**：全局搜索所有调用点（Endpoints、Services、Orchestrator），避免遗漏编译错误
- **INI 配置修改策略**：不要从零生成 XUnity 配置文件（会丢失插件自动生成的默认值），应使用 `PatchAsync` 读取-修改-写回
- **Console logging:** Configured programmatically in `Program.cs` (`ClearProviders` + `AddSimpleConsole`) — appsettings.json logging config was unreliable; all log messages are in Chinese
- **Windows console encoding:** `Console.OutputEncoding = UTF8` MUST be set before `WebApplication.CreateBuilder()`, otherwise Chinese characters will be garbled (the logging system captures encoding at init time)
- Named `HttpClient` instances: `"GitHub"` (API calls with GitHub headers), `"Mirror"` (mirror downloads, no API headers) — mirror URLs embed the original URL as path (e.g., `https://ghfast.top/https://github.com/...`), so `url.Contains("github.com")` checks will match mirror URLs too; check mirror host first
- Download resilience: `GitHubReleaseService.DownloadAssetAsync` has retry (3 attempts, exponential backoff) + auto mirror fallback; progress reported via `IProgress<DownloadProgress>` (percent + speed + retry message)
- `InstallationStatus` has `DownloadSpeed` and `RetryMessage` fields — reset both to null on step transitions in `InstallOrchestrator.UpdateStatus`
- `dotnet build` automatically runs `npm install` + `npm run build` via `BuildFrontend` MSBuild Target in csproj
- Stop the running backend before `dotnet build` — the exe is locked while running
- Frontend changes require `npm run build` then restart backend to take effect (unless using `npm run dev`)
- Backend and frontend share `InstallStep` enum — keep `Models/InstallationStatus.cs` and `src/api/types.ts` in sync; `GeneratingConfig` step launches game to auto-generate XUnity config, then patches user settings
- `XUnityConfig` has typed API key fields (e.g., `DeepLTranslateApiKey`) stored in engine-specific INI sections (e.g., `[DeepLTranslate]`); `ConfigurationService.PatchAsync` reads existing INI and only modifies specified keys
- `SystemTrayService` runs `NotifyIcon` on a dedicated STA thread; `StopAsync` only calls `Application.Exit()` — the STA thread owns the NotifyIcon lifecycle via `using` statement
- `AppSettingsService` persists settings to `%APPDATA%/XUnityToolkit/settings.json` using same semaphore + atomic write pattern as `GameLibraryService`
- Install store's `operationType` field tracks whether current operation is install or uninstall (do not infer from transient step values)
- **Theme-aware CSS:** Use semantic CSS variables (`--bg-subtle`, `--bg-muted`, `--bg-subtle-hover`, `--bg-muted-hover`) for semi-transparent overlay backgrounds — never hardcode `rgba(255,255,255,...)` in scoped CSS as it breaks light mode
- **Naive UI light theme:** Pass `null` (not `lightTheme`) as the `:theme` prop; accent colors need slightly darker values in light mode for contrast (e.g., `#22d3a7` → `#19b892`)
- Pinia stores: `games` (game management), `install` (installation progress + SignalR), `theme` (dark/light mode + localStorage)
- Naive UI `NDrawer` width prop only accepts numbers (not CSS strings) — use `window.resize` listener + ref for responsive drawer width
- Naive UI `NForm` label-placement must be toggled dynamically (via computed) for mobile — cannot use CSS media queries alone
- After frontend changes, always verify with both `npx vue-tsc --noEmit` (type-check) and `npm run build` before considering done
- Verify `@vicons/material` icon availability before importing: `node -e "const m = require('@vicons/material'); console.log(m['IconName'] ? 'YES' : 'NO')"`
- Stop backend on Windows: `taskkill //f //im XUnityToolkit-WebUI.exe`
