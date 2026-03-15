# Online Differential Update System

## Overview

Add an online update feature to XUnityToolkit-WebUI that allows users to download only changed files instead of the entire application. This requires switching from single-file publishing to multi-file publishing and implementing a manifest-based differential update mechanism.

## Motivation

The current single-file exe is ~141MB. Users must re-download the entire package for every update, which is slow and inconvenient. With differential updates, a typical code-only update would be ~10MB (93% reduction).

## Architecture

### Publishing Change: Single-File → Multi-File

Remove `PublishSingleFile=true` from the build configuration. The published output becomes:

```
Release/win-x64/
├── XUnityToolkit-WebUI.exe          (~150KB, .NET host)
├── XUnityToolkit-WebUI.dll          (~2MB, main app logic)
├── Updater.exe                      (~1-2MB, AOT-compiled)
├── *.dll                            (~80MB, .NET runtime + third-party)
├── wwwroot/                         (~2-3MB, Vue SPA)
├── bundled/                         (~30-50MB, plugin assets)
├── LibCpp2IL.dll
├── appsettings.json
└── data/                            (runtime user data, excluded from updates)
```

### Component Packages

CI splits published output into component ZIPs:

| Package | Contents | Rule |
|---------|----------|------|
| `app.zip` | `*.exe`, `*.dll` (root level) | Excludes `bundled/`, `wwwroot/`, `data/`, `appsettings*.json` |
| `wwwroot.zip` | `wwwroot/**` | Frontend assets |
| `bundled.zip` | `bundled/**` | Plugin assets (BepInEx, XUnity, llama) |

### Manifest Format

Each Release includes `manifest-{rid}.json` (e.g., `manifest-win-x64.json`):

```json
{
  "version": "1.3.202603151430",
  "rid": "win-x64",
  "files": {
    "XUnityToolkit-WebUI.exe": {
      "hash": "sha256:abcdef...",
      "size": 153600,
      "package": "app"
    },
    "wwwroot/index.html": {
      "hash": "sha256:...",
      "size": 1234,
      "package": "wwwroot"
    },
    "bundled/bepinex5/BepInEx_x64_5.4.23.5.zip": {
      "hash": "sha256:...",
      "size": 1048576,
      "package": "bundled"
    }
  }
}
```

- `hash`: SHA256 for local file comparison
- `package`: Identifies which component ZIP contains the file
- `data/` directory is never included in the manifest
- `appsettings.json` is excluded from the manifest — users may customize it (e.g., `AppData:Root` override); it is only included in the full install ZIP

### GitHub Release Assets Structure

Each release publishes:

```
manifest-win-x64.json
manifest-win-arm64.json
app-win-x64.zip
app-win-arm64.zip
wwwroot.zip                          (architecture-independent)
bundled.zip                          (architecture-independent)
XUnityToolkit-WebUI-win-x64.zip     (full package for fresh install)
XUnityToolkit-WebUI-win-arm64.zip   (full package for fresh install)
```

## Update Check Flow

### Backend: `UpdateService` (Singleton)

```
UpdateService
├── CheckForUpdateAsync()     ← Compare local vs GitHub latest Release
├── DownloadUpdateAsync()     ← Download required component packages
├── ApplyUpdateAsync()        ← Extract to staging, launch Updater.exe
└── GetUpdateStatus()         ← Return current update state
```

### RID Detection

Use `System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture` to determine the current architecture:

```csharp
var arch = RuntimeInformation.ProcessArchitecture;
var rid = arch switch
{
    Architecture.X64 => "win-x64",
    Architecture.Arm64 => "win-arm64",
    _ => throw new PlatformNotSupportedException($"Unsupported architecture: {arch}")
};
```

This returns a stable value (`X64` or `Arm64`) regardless of .NET version. Do NOT use `RuntimeInformation.RuntimeIdentifier` as it may include OS version suffixes in multi-file mode.

### Version Comparison

The `InformationalVersion` format is `{major}.{minor}.{timestamp}` (e.g., `1.3.202603151430`). Version comparison uses:

1. **Quick check:** If remote version string equals local version string → no update.
2. **Comparison:** Split by `.`, compare each segment numerically. The timestamp segment (`YYYYMMDDHHmm`) is naturally sortable as a number.
3. **Fallback:** If the local version cannot be parsed (e.g., dev build), treat as "update available" and let the user decide.

### Check Process

1. **Fetch latest Release:** `GET https://api.github.com/repos/{owner}/{repo}/releases/latest` (or `/releases` for prerelease). Use `If-None-Match` with cached ETag from previous check to save API quota.
2. **Download remote manifest:** From Release Assets, download `manifest-{rid}.json` (RID determined by `ProcessArchitecture`, see above)
3. **Generate local manifest:** Walk application directory, compute SHA256 for each file (skip `data/`, `appsettings*.json`)
4. **Compute diff:** Determine changed files + required component packages. Also detect files present locally but absent from remote manifest (files to delete).
5. **Return result:** New version, required packages, total download size (sum of package ZIP sizes, not individual file sizes), changelog (Release body)

### GitHub API Rate Limiting

- Cache the last check result (version, ETag, timestamp) in memory and in `data/update-check-cache.json`.
- Use `If-None-Match` header with the cached ETag for conditional requests (returns 304 Not Modified without counting against rate limit).
- Do not auto-check more than once per app session.
- If rate-limited (HTTP 403 with `X-RateLimit-Remaining: 0`), skip silently and retry on next startup or manual check.

### Auto-Check on Startup

In `Program.cs`, after application starts, delay 5 seconds then check asynchronously (non-blocking):

```csharp
app.Lifetime.ApplicationStarted.Register(() => {
    _ = Task.Run(async () => {
        await Task.Delay(5000);
        await updateService.CheckForUpdateAsync();
        // If update available, notify frontend via SignalR
    });
});
```

### Prerelease Updates

`AppSettings` new field:

```csharp
public bool ReceivePreReleaseUpdates { get; set; } = false;
```

- `false` (default): Uses `GET /releases/latest` (stable only)
- `true`: Uses `GET /releases`, takes first result (includes prerelease)

Sync points: `AppSettings.cs` → `types.ts` → `SettingsView.vue` (loadPreferences/savePreferences)

## Download Flow

1. User confirms update in frontend → `POST /api/update/download`
2. `UpdateService` downloads required component ZIPs from GitHub Release Assets
3. Download progress broadcast via SignalR (`update` group) in real-time
4. ZIPs saved to `{programDir}/data/update-staging/`
5. Extract only changed files to `{programDir}/data/update-staging/files/`
6. Verify extracted files' SHA256 against manifest
7. Write `data/update-staging/update-manifest.json` (the remote manifest + list of files to delete)

### Download Cancellation

- `POST /api/update/cancel` cancels an in-progress download via `CancellationTokenSource`.
- Partial downloads are cleaned up (delete `data/update-staging/`).
- No resumable download support — component ZIPs are small enough that re-downloading is acceptable.

### Stale Staging Recovery

On app startup, if `data/update-staging/` exists:
- If `update-manifest.json` is present and all staged files pass SHA256 verification → treat as "ready to apply" state, notify frontend.
- Otherwise → clean up the stale staging directory.

## Updater.exe

### Project Structure

New `Updater` project in the solution:

```
Updater/
├── Updater.csproj      ← net10.0, PublishAot=true (no -windows TFM, keep minimal)
└── Program.cs          ← Single-file entry point
```

Uses .NET AOT publishing to produce a small (~1-2MB) native exe with no .NET runtime dependency (critical since runtime DLLs are being replaced). The Updater should NOT reference Windows Forms or any UI framework — it is a headless console utility. Use `net10.0` (not `net10.0-windows`) to minimize AOT output size. The Updater writes a log to `data/update-temp/updater.log` for debugging failed updates.

### Pre-Apply Safety Checks

Before launching Updater.exe, `ApplyUpdateAsync` checks for active operations:
- Active game install/uninstall (InstallOrchestrator)
- Active AI translation (LlmTranslationService has pending translations)
- In-progress font generation or pre-translation batch
- Running local LLM server (llama-server child process)

If any are active, return an error asking the user to stop them first. Do NOT forcefully terminate.

### Update Application Flow

```
Main App: ApplyUpdate()
  │
  ├─ 1. Check for active operations (fail if any)
  ├─ 2. Copy Updater.exe to {app-dir}/data/update-temp/ (avoid self-update conflict)
  ├─ 3. Launch {app-dir}/data/update-temp/Updater.exe with arguments:
  │     --pid {main process PID}
  │     --app-dir {application root directory}
  │     --staging-dir {data/update-staging/files/}
  │     --delete-list {data/update-staging/delete-list.txt}
  │     --exe-name {XUnityToolkit-WebUI.exe}
  │
  └─ 4. Return HTTP response first, then schedule shutdown:
        _ = Task.Run(async () => { await Task.Delay(500); lifetime.StopApplication(); });

Updater.exe execution:
  │
  ├─ 1. Wait for main process to exit (by PID, timeout 30s)
  ├─ 2. Phase 1 — BACKUP ALL: Copy every file that will be replaced/deleted
  │     to {app-dir}/data/update-backup/ (preserving relative paths)
  ├─ 3. Phase 2 — REPLACE ALL: Copy all staging files to app-dir (overwrite)
  ├─ 4. Phase 3 — DELETE: Remove files listed in delete-list.txt
  ├─ 5. Clean up staging, backup, and update-temp directories
  ├─ 6. Start new main app (Process.Start exe-name)
  └─ 7. Updater exits
```

The two-phase approach (backup ALL first, then replace ALL) ensures atomicity. If any file fails during backup (Phase 1), no files have been modified and we abort cleanly. If replacement (Phase 2) fails partway, we have a complete backup to restore from.

### Failure Rollback

If Updater fails during Phase 2 or Phase 3:

1. Restore ALL backed-up files from `update-backup/` to their original locations
2. Start old main app
3. Write error info to `data/update-error.json`:
   ```json
   {
     "version": "1.4.202603201030",
     "error": "Failed to replace file: XUnityToolkit-WebUI.dll — Access denied",
     "timestamp": "2026-03-20T10:35:00Z",
     "phase": "replace",
     "filesReplaced": 12,
     "filesRemaining": 3
   }
   ```
4. Main app detects this file on startup, notifies frontend via SignalR, then deletes the file

### File Deletion Handling

When a new version removes files that existed in the old version:
- During diff computation, compare local files against the remote manifest.
- Files present locally (and matching a managed package path pattern) but absent from the remote manifest are added to `data/update-staging/delete-list.txt`.
- The Updater reads this list and deletes them in Phase 3 (after replacement, so rollback can restore them).
- `data/`, `appsettings*.json`, and any files outside managed package paths are never deleted.

### Updater Distribution

- Updater.exe ships as part of the application in root directory
- Included in `app` component package manifest
- Can update itself (copied to `data/update-temp/` before running, so the original can be overwritten)
- `data/update-temp/` is cleaned up by the Updater on success, or by the main app on next startup

## API Endpoints

```
GET  /api/update/check          ← Manual check for updates
GET  /api/update/status         ← Current update state (checking/available/downloading/ready/none/error)
POST /api/update/download       ← Start downloading update
POST /api/update/cancel         ← Cancel in-progress download
POST /api/update/apply          ← Apply update (launch Updater.exe, exit main app)
POST /api/update/dismiss        ← Dismiss update notification for this version (persists across restarts via data/update-dismissed-version.txt; reset when a newer version is detected)
```

## SignalR Notifications

Via existing `InstallProgressHub`, new group `update`:

```
UpdateAvailable  { version, changelog, downloadSize, changedPackages[] }
UpdateStatus     { state, progress, downloadedBytes, totalBytes, currentPackage, message, error? }
```

## Frontend UI

### SettingsView - Update Section

**No update:**
```
Version  v1.3    ✓ Up to date    [Check for Updates]

☐ Receive pre-release updates
  Receive test versions that may contain new features but less stability
```

**Update available (expanded):**
```
┌─────────────────────────────────────────┐
│  Update available: v1.4                 │
│                                         │
│  Changes:                               │
│  - Fixed font replacement compatibility │
│  - Added batch import feature           │
│                                         │
│  Downloads required:                    │
│  ● app.zip (8.2 MB)                    │
│  ● wwwroot.zip (2.1 MB)               │
│  Total: 10.3 MB                        │
│                                         │
│  [Start Update]          [Not Now]      │
└─────────────────────────────────────────┘
```

**Downloading:**
```
┌─────────────────────────────────────────┐
│  Downloading update v1.4                │
│  ████████████░░░░░░░░  58%  6.0/10.3 MB│
│  Downloading: app.zip                   │
└─────────────────────────────────────────┘
```

**Ready:**
```
┌─────────────────────────────────────────┐
│  Update downloaded. Restart to apply.   │
│  [Restart Now]                          │
└─────────────────────────────────────────┘
```

### System Tray Notifications

Using existing `SystemTrayService.ShowNotification()`:
- New version detected: balloon tip "New version v1.4 available"
- Download complete: balloon tip "Update ready, restart from Settings to apply"

### Frontend Data Flow

```
SignalR "update" group
  ├── UpdateAvailable → Pinia updateStore → SettingsView shows update prompt
  └── UpdateStatus   → Pinia updateStore → SettingsView shows download progress

API calls:
  updateApi.checkUpdate()     → GET /api/update/check
  updateApi.downloadUpdate()  → POST /api/update/download
  updateApi.applyUpdate()     → POST /api/update/apply
  updateApi.dismissUpdate()   → POST /api/update/dismiss
  updateApi.getUpdateStatus() → GET /api/update/status
```

## Build Flow Changes

### .csproj

Remove single-file publishing:

```diff
- <PublishSingleFile>true</PublishSingleFile>
- <IncludeNativeLibrariesForSelfExtract>true</IncludeNativeLibrariesForSelfExtract>
```

Remove `ExcludeFromSingleFile` target (no longer needed). `LibCpp2IL.dll` (which uses `Assembly.Load` internally) works without special handling in multi-file mode since all DLLs are already on disk.

Note: With multi-file publishing, the `bundled/` copy workaround (needed because `PublishSingleFile` drops files with `+` in names) can be simplified — `bundled/` contents could potentially use csproj `Content` items, though post-publish copy remains acceptable.

### build.ps1

1. **Publish command change:** Remove `-p:PublishSingleFile=true`
2. **New: Build Updater:**
   ```powershell
   dotnet publish Updater/Updater.csproj -c Release -r $rid /p:PublishAot=true
   Copy-Item "Updater/bin/.../publish/Updater.exe" "Release/$rid/"
   ```
3. **New: Generate manifest:**
   ```powershell
   # Walk Release/{rid}/, compute SHA256 for each file (exclude data/),
   # classify into packages (app/wwwroot/bundled), write manifest-{rid}.json
   ```
4. **New: Create component ZIPs:**
   ```powershell
   # app.zip     ← root *.exe, *.dll, *.json (exclude bundled/, wwwroot/, data/)
   # wwwroot.zip ← wwwroot/**
   # bundled.zip ← bundled/**
   # Plus full package XUnityToolkit-WebUI-{rid}.zip for fresh install
   ```

### CI (build.yml)

Mirror build.ps1 changes:
1. Updater AOT build step
2. Manifest generation step
3. Component packaging step
4. Upload new assets: `manifest-{rid}.json`, `app-{rid}.zip`, `wwwroot.zip`, `bundled.zip`

Note: `wwwroot.zip` is architecture-independent (Vue SPA assets). CI should generate it once (not per-RID matrix entry) to avoid duplicate uploads. Either move frontend build to a pre-matrix job or use a matrix `if` condition.

### dep-check.yml

Auto prerelease builds also generate manifest + component packages, so prerelease update subscribers can receive differential updates.

## Sync Points

- **AppSettings.ReceivePreReleaseUpdates:** `AppSettings.cs` → `types.ts` → `SettingsView.vue` (loadPreferences/savePreferences)
- **Update status model:** `Models/UpdateInfo.cs` → `types.ts` → `SettingsView.vue`
- **SignalR group:** `update` added to `InstallProgressHub` group list
- **API endpoints:** `UpdateEndpoints.cs` → `src/api/` → `SettingsView.vue`
- **AppDataPaths:** `update-staging/`, `update-backup/`, `update-temp/` are transient — created on demand by `UpdateService`/Updater, not in `EnsureDirectoriesExist()`
- **Named HttpClient:** Register `"GitHubUpdate"` in `Program.cs` with `User-Agent` header and appropriate timeout
- **CLAUDE.md Architecture section:** Add `update` to SignalR groups list
