# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.
See also: `XUnityToolkit-Vue/CLAUDE.md` (frontend) and `XUnityToolkit-WebUI/CLAUDE.md` (backend) for package-specific details.

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

# Quick local build (skips asset downloads)
.\build-local.ps1

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
- **Real-time:** SignalR via single `InstallProgressHub` (groups: `game-{id}`, `ai-translation`, `logs`, `pre-translation-{gameId}`, `local-llm`, `font-replacement-{gameId}`, `font-generation`, `update`)
- **Persistence:** JSON files in `{programDir}/data/` (`library.json`, `settings.json`) — portable app pattern; API keys encrypted with DPAPI
- **System Tray:** NotifyIcon on dedicated STA thread; `ShowNotification` marshals to STA via `SynchronizationContext.Post`; `_trayIcon`/`_syncContext` are `volatile`
- **No console:** `OutputType=WinExe` — no console window; do NOT revert to `Exe`
- **TranslatorEndpoint:** net35 `LLMTranslate.dll` — XUnity.AutoTranslator custom endpoint forwarding game text to `POST /api/translate`; configurable via `[LLMTranslate]` INI section
- **AI Translation:** `LlmTranslationService` calls LLM APIs (OpenAI/Claude/Gemini/DeepSeek/Qwen/GLM/Kimi/Custom); multi-provider load balancing; batch mode bounded by `SemaphoreSlim`; per-game glossary, translation memory, AI description, do-not-translate list; real-time stats via SignalR
- **Do-Not-Translate:** `DoNotTranslateService` stores per-game lists at `data/do-not-translate/{gameId}.json`; `LlmTranslationService` replaces matched words with `{{DNT_x}}` placeholders before LLM calls, restores after; prompt hint tells LLM to preserve placeholders; entries sorted longest-first; per-entry case sensitivity
- **Translation post-processing order:** Glossary restore → Glossary post-process → DNT restore; **DNT restoration MUST happen AFTER glossary post-processing** — otherwise `ApplyGlossaryPostProcess` (which does `string.Replace` of glossary originals in translated text) will replace restored DNT words with glossary translations, undoing the do-not-translate intent
- **Pre-computed placeholder texts:** Texts entirely replaced by a single placeholder are resolved before the LLM call (pre-computed); they bypass both the LLM and all post-processing steps (`ApplyGlossaryPostProcess`, placeholder restoration) since their results are final; the `preComputed` dictionary tracks these indices
- **Local LLM:** `LocalLlmService` manages llama-server process; GPU detection via DXGI with WMI fallback; llama binaries bundled as ZIPs, lazy-extracted on first use; local mode forces concurrency=1, disables glossary extraction
- **Asset Extraction:** `AssetExtractionService` uses AssetsTools.NET to extract strings from Unity `.assets` and bundle files; `PreTranslationService` batch-translates and writes XUnity cache files
- **Backup/Restore:** `BackupService` creates per-game `BackupManifest` for clean uninstallation; manifests at `{programDir}/data/backups/{gameId}.json`
- **Font Replacement:** `FontReplacementService` uses AssetsTools.NET to scan and replace TMP_FontAsset in game `.assets` and bundle files; field-level replacement preserves PPtr references; automatic Addressables CRC clearing; backups at `{programDir}/data/font-backups/{gameId}/`; custom fonts at `{programDir}/data/custom-fonts/{gameId}/`
- **Font Generation:** `TmpFontGeneratorService` renders AA bitmaps via FreeType (`FT_RENDER_MODE_NORMAL`) then generates SDF via `DistanceFieldGenerator` (Felzenszwalb EDT), replicating Unity's FontEngine approach; supports SDFAA/SDF8/SDF16/SDF32 render modes with upsampling (8x/16x/32x); dynamic padding (percentage/pixel mode); auto-sizing binary search; `GradientScale = padding + 1` injected into Material; RectpackSharp for atlas packing; multi-atlas support; `CharacterSetService` resolves stackable character sets (built-in/custom TXT/XUnity translation file); `BuiltinCharsets` enumerates GB2312/GBK/CJK Common/CJK Full/Japanese; disk-temp SDF bitmaps for memory control; generation reports saved as `.report.json` sidecars; outputs at `{programDir}/data/generated-fonts/`
- **BepInEx Log:** `BepInExLogService` reads `{GamePath}/BepInEx/LogOutput.log` with `FileShare.ReadWrite`; AI analysis via `LlmTranslationService.CallLlmRawAsync` (no semaphore contention); diagnostic prompt is predefined Chinese; log truncated to last 4000 lines for LLM context; `hasBepInEx` computed includes `PartiallyInstalled` state
- **Online Update:** `UpdateService` checks GitHub Releases for new versions; manifest-based differential download (app/wwwroot/bundled component ZIPs); `Updater.exe` (AOT, no runtime dependency) handles file replacement and restart; staging at `data/update-staging/`; two-phase backup-then-replace for atomicity; rollback on failure; prerelease opt-in via `AppSettings.ReceivePreReleaseUpdates`

## Code Conventions

- **Git commit messages:** Titles and body MUST be written in Chinese (conventional commit prefixes like `feat:`/`fix:`/`ci:`/`docs:` may remain in English)
- **Target Framework:** net10.0-windows
- **Root Namespace:** XUnityToolkit_WebUI
- **Nullable reference types:** Enabled; **Implicit usings:** Enabled

## API Endpoints

- **Game:** `GET/POST /api/games/`, `GET/DELETE /api/games/{id}`, `POST .../add-with-detection`, `PUT /api/games/{id}` (rename), `POST .../detect`, `POST .../open-folder`, `POST .../launch`
- **TMP Font:** `GET/POST/DELETE /api/games/{id}/tmp-font` — check/install/uninstall bundled TMP font (version-matched to game's Unity version); used by `ConfigPanel.vue`
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
- **Do-Not-Translate:** `GET/PUT /api/games/{id}/do-not-translate`
- **Asset Extraction:** `POST .../extract-assets`, `GET/DELETE .../extracted-texts`
- **Pre-Translation:** `POST .../pre-translate`, `GET .../pre-translate/status`, `POST .../pre-translate/cancel`
- **Translation Editor:** `GET/PUT .../translation-editor`, `POST .../import`, `GET .../export` (**not ApiResult**)
- **Font Replacement:** `POST .../font-replacement/scan`, `POST .../replace`, `POST .../restore`, `GET .../status`, `POST .../upload`, `POST .../cancel`
- **BepInEx Log:** `GET /api/games/{id}/bepinex-log`, `GET .../download` (**not ApiResult**), `POST .../analyze`
- **Font Generation:** `POST /api/font-generation/upload` (multipart, 50MB), `POST .../generate`, `GET .../status`, `POST .../cancel`, `GET .../download/{fileName}` (**not ApiResult**), `GET .../history`, `DELETE .../{fileName}`, `POST .../install-tmp-font/{gameId}` (installs to `BepInEx/Font/` + patches INI), `GET .../charsets`, `POST .../charset/preview`, `POST .../charset/upload-custom`, `POST .../charset/upload-translation`, `GET .../report/{fileName}`
- **Plugin Package:** `POST .../plugin-package/export` (ZIP, **not ApiResult**), `POST .../import`
- **Logs:** `GET /api/logs?count=`, `GET .../history?lines=`, `GET .../download` (**not ApiResult**)
- **Update:** `GET /api/update/check`, `GET .../status`, `POST .../download`, `POST .../cancel`, `POST .../apply`, `POST .../dismiss`
- **ApiResult pattern:** `ApiResult<T>.Ok(data)` / `ApiResult.Ok()`; request records at bottom of Endpoints files

## Development Notes

### TranslatorEndpoint

- Targets net35 (C# 7.3); `Microsoft.NETFramework.ReferenceAssemblies.net35` NuGet
- **Reference DLLs:** `TranslatorEndpoint/libs/` auto-extracted from bundled XUnity ZIP by `build.ps1`; committed files serve as fallback
- `DisableSpamChecks()` removes stabilization wait; `SetTranslationDelay(float)` min 0.1s; available v5.4.3+
- `GetOrCreateSetting` reads existing INI; changing DLL defaults won't affect installed games — use `PatchSectionAsync`
- **"Endpoint" vs "Provider":** "translation endpoint" = `LLMTranslate.dll`; "provider" = `ApiEndpointConfig` LLM API config

### Sync Points

- **InstallStep enum:** Sync 4 places: `Models/InstallationStatus.cs`, `src/api/types.ts`, `InstallProgressDrawer.vue`, `InstallOrchestrator.cs`
- **Adding AppSettings fields:** Sync 4 places: `Models/AppSettings.cs`, `src/api/types.ts`, store's `loadPreferences`/`savePreferences`, `SettingsView.vue`
- **Adding AiTranslationSettings fields:** Sync 4 places: `Models/AiTranslationSettings.cs`, `src/api/types.ts`, `AiTranslationView.vue` (`DEFAULT_AI_TRANSLATION`), `SettingsView.vue`
- **Adding DoNotTranslateEntry fields:** Sync 2 places: `Models/DoNotTranslateEntry.cs`, `src/api/types.ts`
- **Adding TranslationStats/RecentTranslation/TranslationError fields:** Sync 2 places: `Models/TranslationStats.cs`, `src/api/types.ts`; display in `AiTranslationView.vue`
- **`RecordError` call sites:** `LlmTranslationService.RecordError` called from: internal (`TranslateAsync` early-exit), external (`TranslateEndpoints.cs` catch blocks) — signature changes must update both
- **Font generation models:** Sync `CharacterSetConfig`/`FontGenerationReport`/`CharsetInfo` between `Models/FontGeneration.cs` ↔ `src/api/types.ts`; phase values between `TmpFontGeneratorService` ↔ `FontGeneratorView.vue` phaseLabels; charset IDs between `BuiltinCharsets` ↔ frontend checkbox values
- **Font replacement models:** Sync `FontInfo`/`FontReplacementStatus` between `Models/FontReplacement.cs` ↔ `src/api/types.ts` ↔ `FontReplacementView.vue`
- **TMP font models:** Sync `TmpFontStatus` between `Models/` ↔ `src/api/types.ts`; API methods in `src/api/games.ts`
- Frontend state lifecycle: `GameDetailView.loadGame()` resets state when `isInstalled=false`
- Install store `operationType` tracks install vs uninstall
- **`[LLMTranslate]` INI config:** Written in 3 places — `POST /ai-endpoint`, `InstallOrchestrator`, DLL `Initialize`
- **Update status model:** `Models/UpdateInfo.cs` → `src/api/types.ts` → `src/stores/update.ts` → `SettingsView.vue`
- **AppSettings.ReceivePreReleaseUpdates:** Sync 4 places: `Models/AppSettings.cs`, `src/api/types.ts`, `SettingsView.vue` (settings default + NSwitch)
- **MSI registry keys:** Written by MSI (`Components.wxs`), read by `Program.cs` (DataPath) and `Updater/Program.cs` (MsiProductCode); key path: `HKCU\Software\XUnityToolkit`

### Build & Deploy

- `dotnet build` auto-runs frontend; skip with `-p:SkipFrontendBuild=true`
- `build.ps1`: downloads bundled assets → extracts XUnity reference DLLs → updates classdata.tpk (requires `gh` CLI) → frontend → TranslatorEndpoint → publish to `Release/{rid}/`; `-SkipDownload` skips all download/extraction steps; cleanup: remove `web.config`, `*.pdb`, `*.staticwebassets.endpoints.json`
- **Versioning:** `build.ps1` auto-generates `1.0.{YYYYMMDDHHmm}` via `-p:InformationalVersion`; **must use `InformationalVersion` not `Version`** — `Version` sets `AssemblyVersion` (UInt16 max 65535) which overflows with timestamp
- **Multi-file publishing:** `PublishSingleFile` removed; `ExcludeFromSingleFile` target removed; LibCpp2IL.dll works naturally in multi-file mode
- **Satellite assemblies:** `SatelliteResourceLanguages=en` strips all language folders (cs/de/fr/ja/ko/etc.) from publish output; WinForms satellite resources are unused (UI is Vue, native dialogs use OS localization)
- **Updater:** `Updater/Updater.csproj` (net10.0, PublishAot); **win-x64 only** (ARM64 AOT requires C++ ARM64 build tools); x64 binary runs on ARM64 via emulation; `--data-dir` CLI arg directs log/backup paths to `paths.Root`
- **MSI Installer:** `Installer/Installer.wixproj` (WixToolset.Sdk); per-user install to `%LocalAppData%\Programs\`; `build.ps1` auto-generates `Installer/Generated/HarvestedFiles.wxs` from publish output; MSI version: `{YYYY-2024}.{MMDD}.{HHmm}` (differs from `InformationalVersion`)
- **MSI + Updater coexistence:** Updater.exe syncs `DisplayVersion`/`InstallDate` in HKCU Uninstall key after delta update via P/Invoke (AOT-safe)
- **Installed vs portable mode:** `Program.cs` checks `HKCU\Software\XUnityToolkit\DataPath` registry; present → installed mode (`%AppData%\XUnityToolkit\`); absent → portable mode (`{programDir}/data/`)
- **Updater AOT P/Invoke:** `DllImport`/`const`/`static readonly` cannot be used in top-level statements — must wrap in `partial class Program`; cannot use `Microsoft.Win32.Registry` — must P/Invoke advapi32.dll directly
- **AppDataPaths config write-back:** After modifying `appDataRoot` source in `Program.cs`, **must** execute `builder.Configuration["AppData:Root"] = appDataRoot` — otherwise `AppDataPaths` (reads `IConfiguration` via DI) won't pick up the new value
- **WiX build artifact cleanup:** WiX produces `.wixpdb` files in `OutputPath`; must clean up after moving MSI, otherwise they pollute release ZIPs
- **CI shared PowerShell functions:** Extract to standalone `.ps1` files (e.g., `Installer/Generate-InstallerWxs.ps1`); both `build.ps1` and CI source via `. ./path/to/script.ps1`
- **Update manifest:** `manifest-{rid}.json` generated per release with SHA256 hashes; component ZIPs: `app-{rid}.zip`, `wwwroot.zip`, `bundled.zip`
- **Bundled assets:** `bundled/{bepinex5,bepinex6,xunity,llama}/` — ALL auto-detect latest versions via API; no hardcoded version pins; llama.cpp prefers CUDA 12.4; copied post-publish
- **TMP fonts:** `bundled/fonts/` (tracked in git); release build uses `build.ps1` post-publish `Copy-Item`
- **PowerShell ZIP:** Do NOT use `Compress-Archive` (broken on PowerShell 7.5.5 — module load error); use `[System.IO.Compression.ZipFile]` instead
- **gitignore:** `docs/` is gitignored; use `git add -f` when committing spec/plan documents
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
