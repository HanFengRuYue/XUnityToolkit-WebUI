# Online Differential Update System Implementation Plan

> **For agentic workers:** REQUIRED: Use superpowers:subagent-driven-development (if subagents available) or superpowers:executing-plans to implement this plan. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add differential online update capability so users download only changed component packages (~10MB) instead of the full application (~141MB).

**Architecture:** Switch from single-file to multi-file publishing. Each release includes a SHA256 manifest and component ZIPs (app/wwwroot/bundled). A backend `UpdateService` checks GitHub Releases, downloads packages, and launches an AOT-compiled `Updater.exe` to replace files and restart.

**Tech Stack:** ASP.NET Core (.NET 10), .NET AOT (Updater), Vue 3 + Pinia + Naive UI, SignalR, GitHub Releases API, PowerShell (build scripts)

**Spec:** `docs/superpowers/specs/2026-03-15-online-update-design.md`

---

## File Structure

### New Files
| File | Responsibility |
|------|---------------|
| `Updater/Updater.csproj` | AOT-compiled update applicator project |
| `Updater/Program.cs` | Wait for main app exit → backup → replace → delete → restart |
| `XUnityToolkit-WebUI/Models/UpdateInfo.cs` | Models: `UpdateCheckResult`, `UpdateStatus`, `UpdateManifest`, `ManifestFileEntry` |
| `XUnityToolkit-WebUI/Services/UpdateService.cs` | Check, download, apply update logic + SignalR notifications |
| `XUnityToolkit-WebUI/Endpoints/UpdateEndpoints.cs` | API: check, status, download, cancel, apply, dismiss |
| `XUnityToolkit-Vue/src/stores/update.ts` | Pinia store with SignalR integration for update state |
| `XUnityToolkit-Vue/src/api/update.ts` | API client methods for update endpoints |

### Modified Files
| File | Changes |
|------|---------|
| `XUnityToolkit-WebUI/XUnityToolkit-WebUI.csproj` | Remove `PublishSingleFile`, remove `ExcludeFromSingleFile` target |
| `XUnityToolkit-WebUI/Models/AppSettings.cs` | Add `ReceivePreReleaseUpdates` field |
| `XUnityToolkit-WebUI/Hubs/InstallProgressHub.cs` | Add `JoinUpdateGroup`/`LeaveUpdateGroup` methods |
| `XUnityToolkit-WebUI/Program.cs` | Register `UpdateService`, `"GitHubUpdate"` HttpClient, startup auto-check |
| `XUnityToolkit-Vue/src/api/types.ts` | Add update-related TypeScript interfaces |
| `XUnityToolkit-Vue/src/views/SettingsView.vue` | Add update section UI + prerelease toggle |
| `build.ps1` | Remove single-file, add Updater build, manifest gen, component ZIPs |
| `.github/workflows/build.yml` | Mirror build.ps1 changes for CI |
| `.github/workflows/dep-check.yml` | Generate manifest + component ZIPs for prerelease builds |
| `CLAUDE.md` | Add update-related architecture docs and sync points |

---

## Chunk 1: Updater Project & Build System

### Task 1: Create Updater Project

**Files:**
- Create: `Updater/Updater.csproj`
- Create: `Updater/Program.cs`

- [ ] **Step 1: Create `Updater/Updater.csproj`**

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <RootNamespace>Updater</RootNamespace>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <PublishAot>true</PublishAot>
    <InvariantGlobalization>true</InvariantGlobalization>
    <OptimizationPreference>Size</OptimizationPreference>
  </PropertyGroup>
</Project>
```

Notes:
- Use `net10.0` (not `net10.0-windows`) to keep AOT output minimal
- `InvariantGlobalization` reduces binary size
- `OptimizationPreference=Size` over speed since the updater is short-lived
- Do NOT add WinForms, UI frameworks, or any unnecessary dependencies

- [ ] **Step 2: Create `Updater/Program.cs`**

Implement the full updater logic:

1. Parse CLI arguments: `--pid`, `--app-dir`, `--staging-dir`, `--delete-list`, `--exe-name`
2. Wait for main process to exit by PID (poll `Process.GetProcessById`, timeout 30s)
3. Open log file at `{app-dir}/data/update-temp/updater.log`
4. **Phase 1 — BACKUP ALL:** For each file in staging-dir (preserving relative paths), copy the corresponding file from app-dir to `{app-dir}/data/update-backup/`. If any backup fails, abort immediately (no files modified yet).
5. **Phase 2 — REPLACE ALL:** Copy all files from staging-dir to app-dir, overwriting. If any replace fails, go to rollback.
6. **Phase 3 — DELETE:** Read `delete-list` file (one relative path per line). For each, delete from app-dir. If any delete fails, go to rollback.
7. **Cleanup:** Delete `data/update-staging/`, `data/update-backup/`, `data/update-temp/` directories.
8. **Launch:** Start `{app-dir}/{exe-name}` via `Process.Start`.
9. Exit.

**Rollback (on Phase 2/3 failure):**
1. Restore all files from `update-backup/` to their original locations in app-dir.
2. Write `data/update-error.json` with error details (version, error message, timestamp, phase, filesReplaced, filesRemaining).
3. Launch old main app.
4. Exit with non-zero code.

Key implementation details:
- Use `System.Diagnostics.Process` for PID waiting (no `Process.WaitForExit` — use polling since we may not have permission to open the process handle; catch `ArgumentException`/`InvalidOperationException` as "process exited")
- Use `File.Copy(src, dst, overwrite: true)` for replace
- Use `Directory.CreateDirectory` for backup path parents
- Log every operation to `updater.log` with timestamps
- All paths use `Path.Combine` — never hardcode separators

- [ ] **Step 3: Verify Updater builds locally**

Run: `dotnet build Updater/Updater.csproj`
Expected: Build succeeds without errors.

- [ ] **Step 4: Commit**

```bash
git add Updater/
git commit -m "feat: 添加 Updater 项目（AOT 编译的更新应用器）"
```

---

### Task 2: Modify Build Configuration for Multi-File Publishing

**Files:**
- Modify: `XUnityToolkit-WebUI/XUnityToolkit-WebUI.csproj`

- [ ] **Step 1: Remove single-file publishing from .csproj**

In `XUnityToolkit-WebUI/XUnityToolkit-WebUI.csproj`:

1. The `.csproj` does NOT contain `PublishSingleFile` in PropertyGroup (it's passed via CLI in `build.ps1`), so no PropertyGroup change needed.
2. Remove the entire `ExcludeLibCpp2ILFromSingleFile` target (the `<Target Name="ExcludeLibCpp2ILFromSingleFile" ...>` block) — no longer needed in multi-file mode since LibCpp2IL.dll is naturally on disk.

- [ ] **Step 2: Verify build still works**

Run: `dotnet build XUnityToolkit-WebUI/XUnityToolkit-WebUI.csproj -p:SkipFrontendBuild=true`
Expected: Build succeeds.

- [ ] **Step 3: Commit**

```bash
git add XUnityToolkit-WebUI/XUnityToolkit-WebUI.csproj
git commit -m "feat: 移除单文件发布配置，切换到多文件发布模式"
```

---

### Task 3: Update build.ps1

**Files:**
- Modify: `build.ps1`

- [ ] **Step 1: Remove single-file from publish command**

In `build.ps1`, find the `dotnet publish` command and remove `-p:PublishSingleFile=true` and `-p:IncludeNativeLibrariesForSelfExtract=true` flags.

- [ ] **Step 2: Add Updater build step**

After the TranslatorEndpoint build section and before the publish step, add:

```powershell
# ── Step: Build Updater (AOT) ──
Write-Host "`n=== Building Updater ===" -ForegroundColor Cyan
foreach ($rid in $runtimes) {
    Write-Host "Building Updater for $rid..." -ForegroundColor Yellow
    dotnet publish Updater/Updater.csproj -c Release -r $rid /p:PublishAot=true
    if ($LASTEXITCODE -ne 0) { throw "Updater build failed for $rid" }
}
```

- [ ] **Step 3: Add Updater.exe copy to post-publish**

In the post-publish section (after bundled asset copy), add:

```powershell
# Copy Updater.exe
$updaterPath = "Updater/bin/Release/net10.0/$rid/publish/Updater.exe"
if (Test-Path $updaterPath) {
    Copy-Item $updaterPath "$releaseDir/" -Force
    Write-Host "  Copied Updater.exe" -ForegroundColor Green
}
```

- [ ] **Step 4: Add manifest generation function**

Add a function `Generate-Manifest` that:
1. Walks all files in `Release/{rid}/` recursively
2. Skips `data/` directory and `appsettings*.json`
3. For each file, computes SHA256 hash and determines package:
   - Files under `wwwroot/` → package `"wwwroot"`
   - Files under `bundled/` → package `"bundled"`
   - All other files (root `*.exe`, `*.dll`, etc.) → package `"app"`
4. Builds JSON object matching the manifest format from the spec
5. Writes to `Release/manifest-{rid}.json`

```powershell
function Generate-Manifest {
    param([string]$ReleaseDir, [string]$Rid, [string]$Version)

    $manifest = @{
        version = $Version
        rid = $Rid
        files = @{}
    }

    $basePath = (Resolve-Path $ReleaseDir).Path
    Get-ChildItem -Path $ReleaseDir -Recurse -File | ForEach-Object {
        $relativePath = $_.FullName.Substring($basePath.Length + 1).Replace('\', '/')

        # Skip data/ and appsettings
        if ($relativePath -match '^data/' -or $relativePath -match '^appsettings') { return }

        $hash = (Get-FileHash -Path $_.FullName -Algorithm SHA256).Hash.ToLower()
        $package = if ($relativePath -match '^wwwroot/') { "wwwroot" }
                   elseif ($relativePath -match '^bundled/') { "bundled" }
                   else { "app" }

        $manifest.files[$relativePath] = @{
            hash = "sha256:$hash"
            size = $_.Length
            package = $package
        }
    }

    $manifestPath = Join-Path (Split-Path $ReleaseDir) "manifest-$Rid.json"
    $manifest | ConvertTo-Json -Depth 4 | Set-Content -Path $manifestPath -Encoding utf8
    Write-Host "  Generated manifest: $manifestPath" -ForegroundColor Green
}
```

- [ ] **Step 5: Add component ZIP creation function**

Add a function `Create-ComponentZips` that creates `app-{rid}.zip`, `wwwroot.zip`, `bundled.zip`:

```powershell
function Create-ComponentZips {
    param([string]$ReleaseDir, [string]$Rid)

    $outputDir = Split-Path $ReleaseDir

    # app.zip — root-level exe, dll (exclude bundled/, wwwroot/, data/, appsettings*)
    $appFiles = Get-ChildItem -Path $ReleaseDir -File | Where-Object {
        $_.Name -notmatch '^appsettings'
    }
    $appZip = Join-Path $outputDir "app-$Rid.zip"
    if (Test-Path $appZip) { Remove-Item $appZip }
    Compress-Archive -Path $appFiles.FullName -DestinationPath $appZip
    Write-Host "  Created: $appZip" -ForegroundColor Green

    # wwwroot.zip
    $wwwrootDir = Join-Path $ReleaseDir "wwwroot"
    if (Test-Path $wwwrootDir) {
        $wwwrootZip = Join-Path $outputDir "wwwroot.zip"
        if (Test-Path $wwwrootZip) { Remove-Item $wwwrootZip }
        Compress-Archive -Path $wwwrootDir -DestinationPath $wwwrootZip
        Write-Host "  Created: $wwwrootZip" -ForegroundColor Green
    }

    # bundled.zip
    $bundledDir = Join-Path $ReleaseDir "bundled"
    if (Test-Path $bundledDir) {
        $bundledZip = Join-Path $outputDir "bundled.zip"
        if (Test-Path $bundledZip) { Remove-Item $bundledZip }
        Compress-Archive -Path $bundledDir -DestinationPath $bundledZip
        Write-Host "  Created: $bundledZip" -ForegroundColor Green
    }
}
```

- [ ] **Step 6: Call manifest and ZIP functions in the per-runtime loop**

After post-publish cleanup and bundled copy, add:

```powershell
# Generate manifest and component ZIPs
Generate-Manifest -ReleaseDir $releaseDir -Rid $rid -Version $BuildVersion
Create-ComponentZips -ReleaseDir $releaseDir -Rid $rid
```

- [ ] **Step 7: Verify build.ps1 runs locally**

Run: `.\build.ps1 -Runtime win-x64 -SkipDownload`
Expected: Builds successfully, generates `Release/manifest-win-x64.json`, `Release/app-win-x64.zip`, `Release/wwwroot.zip`, `Release/bundled.zip`.

- [ ] **Step 8: Commit**

```bash
git add build.ps1
git commit -m "feat: 更新构建脚本，支持多文件发布、Updater 构建、清单和组件包生成"
```

---

## Chunk 2: Backend Models & UpdateService

### Task 4: Add Update Models

**Files:**
- Create: `XUnityToolkit-WebUI/Models/UpdateInfo.cs`
- Modify: `XUnityToolkit-WebUI/Models/AppSettings.cs`

- [ ] **Step 1: Create `Models/UpdateInfo.cs`**

```csharp
namespace XUnityToolkit_WebUI.Models;

// Manifest file entry
public sealed class ManifestFileEntry
{
    public required string Hash { get; set; }
    public long Size { get; set; }
    public required string Package { get; set; }
}

// Full manifest from GitHub Release
public sealed class UpdateManifest
{
    public required string Version { get; set; }
    public required string Rid { get; set; }
    public Dictionary<string, ManifestFileEntry> Files { get; set; } = [];
}

// Result of checking for updates
public sealed class UpdateCheckResult
{
    public bool UpdateAvailable { get; set; }
    public string? NewVersion { get; set; }
    public string? Changelog { get; set; }
    public long DownloadSize { get; set; }
    public List<string> ChangedPackages { get; set; } = [];
    public int ChangedFileCount { get; set; }
    public int DeletedFileCount { get; set; }
}

// Current update state enum
public enum UpdateState
{
    None,
    Checking,
    Available,
    Downloading,
    Ready,
    Applying,
    Error
}

// Status broadcasted via SignalR
public sealed class UpdateStatusInfo
{
    public UpdateState State { get; set; }
    public double Progress { get; set; }
    public long DownloadedBytes { get; set; }
    public long TotalBytes { get; set; }
    public string? CurrentPackage { get; set; }
    public string? Message { get; set; }
    public string? Error { get; set; }
}

// Update available notification via SignalR
public sealed class UpdateAvailableInfo
{
    public required string Version { get; set; }
    public string? Changelog { get; set; }
    public long DownloadSize { get; set; }
    public List<string> ChangedPackages { get; set; } = [];
}

// Cached check result persisted to disk
public sealed class UpdateCheckCache
{
    public string? ETag { get; set; }
    public string? LatestVersion { get; set; }
    public DateTime LastChecked { get; set; }
}

// GitHub Release API response (relevant fields only)
public sealed class GitHubRelease
{
    public required string TagName { get; set; }
    public string? Body { get; set; }
    public bool Prerelease { get; set; }
    public List<GitHubAsset> Assets { get; set; } = [];
}

public sealed class GitHubAsset
{
    public required string Name { get; set; }
    public required string BrowserDownloadUrl { get; set; }
    public long Size { get; set; }
}
```

- [ ] **Step 2: Add `ReceivePreReleaseUpdates` to `AppSettings.cs`**

In `XUnityToolkit-WebUI/Models/AppSettings.cs`, add to the `AppSettings` class:

```csharp
public bool ReceivePreReleaseUpdates { get; set; }
```

- [ ] **Step 3: Verify build**

Run: `dotnet build XUnityToolkit-WebUI/XUnityToolkit-WebUI.csproj -p:SkipFrontendBuild=true`
Expected: Build succeeds.

- [ ] **Step 4: Commit**

```bash
git add XUnityToolkit-WebUI/Models/UpdateInfo.cs XUnityToolkit-WebUI/Models/AppSettings.cs
git commit -m "feat: 添加在线更新数据模型和 AppSettings 预发布更新选项"
```

---

### Task 5: Implement UpdateService

**Files:**
- Create: `XUnityToolkit-WebUI/Services/UpdateService.cs`

This is the largest single file. It handles: check, download, apply, status, and SignalR notifications.

- [ ] **Step 1: Create `UpdateService.cs` with constructor and fields**

```csharp
namespace XUnityToolkit_WebUI.Services;

using System.IO.Compression;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.SignalR;
using XUnityToolkit_WebUI.Hubs;
using XUnityToolkit_WebUI.Infrastructure;
using XUnityToolkit_WebUI.Models;

public sealed class UpdateService(
    IHttpClientFactory httpClientFactory,
    IHubContext<InstallProgressHub> hubContext,
    AppSettingsService settingsService,
    AppDataPaths paths,
    IHostApplicationLifetime lifetime,
    ILogger<UpdateService> logger)
{
    private const string GitHubOwner = "HanFengRuYue";
    private const string GitHubRepo = "XUnityToolkit-WebUI";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    // Managed file extensions for delete-list computation (used in check and download)
    private static readonly HashSet<string> ManagedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".exe", ".dll", ".json", ".js", ".css", ".html", ".map", ".svg",
        ".png", ".woff", ".woff2", ".ttf", ".zip"
    };

    private readonly SemaphoreSlim _lock = new(1, 1);
    private UpdateStatusInfo _status = new() { State = UpdateState.None };
    private UpdateCheckResult? _lastCheckResult;
    private UpdateManifest? _remoteManifest;
    private List<GitHubAsset>? _releaseAssets;
    private CancellationTokenSource? _downloadCts;
    private bool _hasAutoChecked;
    private string? _lastCheckedApiUrl; // Track API URL for ETag cache invalidation

    // ...methods below...
}
```

Note: The `GitHubOwner` and `GitHubRepo` constants should be confirmed with the actual repository. These are used to construct the GitHub API URL.

- [ ] **Step 2: Add RID detection and version comparison helpers**

```csharp
    private static string GetCurrentRid()
    {
        var arch = RuntimeInformation.ProcessArchitecture;
        return arch switch
        {
            Architecture.X64 => "win-x64",
            Architecture.Arm64 => "win-arm64",
            _ => throw new PlatformNotSupportedException($"Unsupported architecture: {arch}")
        };
    }

    private static string GetCurrentVersion()
    {
        var version = Assembly.GetExecutingAssembly()
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion ?? "1.0.0";
        // Strip +metadata suffix (e.g., "1.3.202603151430+abc123" → "1.3.202603151430")
        var plusIndex = version.IndexOf('+');
        return plusIndex >= 0 ? version[..plusIndex] : version;
    }

    private static bool IsNewerVersion(string remote, string local)
    {
        if (remote == local) return false;

        var remoteParts = remote.Split('.');
        var localParts = local.Split('.');
        var len = Math.Max(remoteParts.Length, localParts.Length);

        for (int i = 0; i < len; i++)
        {
            if (!long.TryParse(i < remoteParts.Length ? remoteParts[i] : "0", out var r)) return true;
            if (!long.TryParse(i < localParts.Length ? localParts[i] : "0", out var l)) return true;
            if (r > l) return true;
            if (r < l) return false;
        }
        return false;
    }

    private static string ComputeFileHash(string filePath)
    {
        using var stream = File.OpenRead(filePath);
        var hash = SHA256.HashData(stream);
        return "sha256:" + Convert.ToHexStringLower(hash);
    }
```

- [ ] **Step 3: Add `GetUpdateStatus` and `CheckForUpdateAsync`**

```csharp
    public UpdateStatusInfo GetUpdateStatus() => _status;

    public async Task<UpdateCheckResult> CheckForUpdateAsync(CancellationToken ct = default)
    {
        if (!await _lock.WaitAsync(0, ct))
            return _lastCheckResult ?? new UpdateCheckResult();

        try
        {
            _status = new UpdateStatusInfo { State = UpdateState.Checking, Message = "正在检查更新..." };
            await BroadcastStatus();

            var settings = await settingsService.GetAsync(ct);
            var client = httpClientFactory.CreateClient("GitHubUpdate");
            var rid = GetCurrentRid();
            var localVersion = GetCurrentVersion();

            // Fetch latest release from GitHub
            var apiUrl = settings.ReceivePreReleaseUpdates
                ? $"https://api.github.com/repos/{GitHubOwner}/{GitHubRepo}/releases"
                : $"https://api.github.com/repos/{GitHubOwner}/{GitHubRepo}/releases/latest";

            // Load cached ETag (only use if API URL matches)
            var cachePath = Path.Combine(paths.Root, "update-check-cache.json");
            UpdateCheckCache? cache = null;
            if (File.Exists(cachePath))
            {
                var cacheJson = await File.ReadAllTextAsync(cachePath, ct);
                cache = JsonSerializer.Deserialize<UpdateCheckCache>(cacheJson, JsonOptions);
            }

            using var request = new HttpRequestMessage(HttpMethod.Get, apiUrl);
            if (cache?.ETag is not null && _lastCheckedApiUrl == apiUrl)
                request.Headers.TryAddWithoutValidation("If-None-Match", cache.ETag);
            _lastCheckedApiUrl = apiUrl;

            using var response = await client.SendAsync(request, ct);

            if (response.StatusCode == System.Net.HttpStatusCode.NotModified)
            {
                logger.LogInformation("GitHub API 返回 304 Not Modified，无新版本");
                _status = new UpdateStatusInfo { State = UpdateState.None };
                await BroadcastStatus();
                _lastCheckResult = new UpdateCheckResult();
                return _lastCheckResult;
            }

            if ((int)response.StatusCode == 403)
            {
                logger.LogWarning("GitHub API 速率限制，跳过检查");
                _status = new UpdateStatusInfo { State = UpdateState.None };
                await BroadcastStatus();
                return _lastCheckResult ?? new UpdateCheckResult();
            }

            response.EnsureSuccessStatusCode();
            var json = await response.Content.ReadAsStringAsync(ct);

            GitHubRelease? release;
            if (settings.ReceivePreReleaseUpdates)
            {
                var releases = JsonSerializer.Deserialize<List<GitHubRelease>>(json, JsonOptions);
                release = releases?.FirstOrDefault();
            }
            else
            {
                release = JsonSerializer.Deserialize<GitHubRelease>(json, JsonOptions);
            }

            if (release is null)
            {
                _status = new UpdateStatusInfo { State = UpdateState.None };
                await BroadcastStatus();
                _lastCheckResult = new UpdateCheckResult();
                return _lastCheckResult;
            }

            // Save ETag cache
            var newCache = new UpdateCheckCache
            {
                ETag = response.Headers.ETag?.Tag,
                LatestVersion = release.TagName.TrimStart('v'),
                LastChecked = DateTime.UtcNow
            };
            await File.WriteAllTextAsync(cachePath,
                JsonSerializer.Serialize(newCache, JsonOptions), ct);

            var remoteVersion = release.TagName.TrimStart('v');

            // Check if dismissed
            var dismissedPath = Path.Combine(paths.Root, "update-dismissed-version.txt");
            if (File.Exists(dismissedPath))
            {
                var dismissed = (await File.ReadAllTextAsync(dismissedPath, ct)).Trim();
                if (dismissed == remoteVersion)
                {
                    _dismissedVersion = dismissed;
                    _status = new UpdateStatusInfo { State = UpdateState.None };
                    await BroadcastStatus();
                    _lastCheckResult = new UpdateCheckResult();
                    return _lastCheckResult;
                }
            }

            if (!IsNewerVersion(remoteVersion, localVersion))
            {
                logger.LogInformation("当前版本 {Local} 已是最新", localVersion);
                _status = new UpdateStatusInfo { State = UpdateState.None };
                await BroadcastStatus();
                _lastCheckResult = new UpdateCheckResult();
                return _lastCheckResult;
            }

            // Download remote manifest
            var manifestAsset = release.Assets
                .FirstOrDefault(a => a.Name == $"manifest-{rid}.json");
            if (manifestAsset is null)
            {
                logger.LogWarning("Release {Version} 缺少 manifest-{Rid}.json", remoteVersion, rid);
                _status = new UpdateStatusInfo { State = UpdateState.None };
                await BroadcastStatus();
                _lastCheckResult = new UpdateCheckResult();
                return _lastCheckResult;
            }

            var manifestJson = await client.GetStringAsync(manifestAsset.BrowserDownloadUrl, ct);
            _remoteManifest = JsonSerializer.Deserialize<UpdateManifest>(manifestJson, JsonOptions);
            _releaseAssets = release.Assets;

            if (_remoteManifest is null)
            {
                _status = new UpdateStatusInfo { State = UpdateState.None };
                await BroadcastStatus();
                _lastCheckResult = new UpdateCheckResult();
                return _lastCheckResult;
            }

            // Generate local manifest and compute diff
            var appDir = AppContext.BaseDirectory;
            var changedPackages = new HashSet<string>();
            var changedCount = 0;
            var deletedCount = 0;

            // Check remote files against local
            foreach (var (relativePath, entry) in _remoteManifest.Files)
            {
                var localPath = Path.Combine(appDir, relativePath.Replace('/', Path.DirectorySeparatorChar));
                if (!File.Exists(localPath) || ComputeFileHash(localPath) != entry.Hash)
                {
                    changedPackages.Add(entry.Package);
                    changedCount++;
                }
            }

            // Check for files to delete (local files not in remote manifest)
            // Uses static ManagedExtensions field defined at class level

            foreach (var filePath in Directory.EnumerateFiles(appDir, "*", SearchOption.AllDirectories))
            {
                var relative = Path.GetRelativePath(appDir, filePath).Replace(Path.DirectorySeparatorChar, '/');
                if (relative.StartsWith("data/", StringComparison.OrdinalIgnoreCase)) continue;
                if (relative.StartsWith("appsettings", StringComparison.OrdinalIgnoreCase)) continue;
                if (!ManagedExtensions.Contains(Path.GetExtension(filePath))) continue;
                if (!_remoteManifest.Files.ContainsKey(relative))
                    deletedCount++;
            }

            // Calculate download size (sum of package ZIP sizes)
            long downloadSize = 0;
            foreach (var pkg in changedPackages)
            {
                var zipName = pkg == "app" ? $"app-{rid}.zip" : $"{pkg}.zip";
                var asset = release.Assets.FirstOrDefault(a => a.Name == zipName);
                if (asset is not null) downloadSize += asset.Size;
            }

            _lastCheckResult = new UpdateCheckResult
            {
                UpdateAvailable = changedCount > 0 || deletedCount > 0,
                NewVersion = remoteVersion,
                Changelog = release.Body,
                DownloadSize = downloadSize,
                ChangedPackages = changedPackages.ToList(),
                ChangedFileCount = changedCount,
                DeletedFileCount = deletedCount
            };

            if (_lastCheckResult.UpdateAvailable)
            {
                _status = new UpdateStatusInfo { State = UpdateState.Available, Message = $"新版本 {remoteVersion} 可用" };
                await BroadcastStatus();
                await hubContext.Clients.Group("update").SendAsync("UpdateAvailable", new UpdateAvailableInfo
                {
                    Version = remoteVersion,
                    Changelog = release.Body,
                    DownloadSize = downloadSize,
                    ChangedPackages = changedPackages.ToList()
                }, ct);
                logger.LogInformation("发现新版本 {Version}，需要下载 {Packages}",
                    remoteVersion, string.Join(", ", changedPackages));
            }
            else
            {
                _status = new UpdateStatusInfo { State = UpdateState.None };
                await BroadcastStatus();
            }

            return _lastCheckResult;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "检查更新失败");
            _status = new UpdateStatusInfo { State = UpdateState.Error, Error = ex.Message };
            await BroadcastStatus();
            return _lastCheckResult ?? new UpdateCheckResult();
        }
        finally
        {
            _lock.Release();
        }
    }
```

- [ ] **Step 4: Add `DownloadUpdateAsync`**

```csharp
    public async Task DownloadUpdateAsync(CancellationToken ct = default)
    {
        if (_lastCheckResult is not { UpdateAvailable: true } || _remoteManifest is null || _releaseAssets is null)
            throw new InvalidOperationException("没有可用的更新");

        _downloadCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var token = _downloadCts.Token;

        var stagingDir = Path.Combine(paths.Root, "update-staging");
        var filesDir = Path.Combine(stagingDir, "files");

        try
        {
            // Clean up any existing staging
            if (Directory.Exists(stagingDir))
                Directory.Delete(stagingDir, true);
            Directory.CreateDirectory(filesDir);

            var client = httpClientFactory.CreateClient("GitHubUpdate");
            var rid = GetCurrentRid();
            var appDir = AppContext.BaseDirectory;
            var totalBytes = _lastCheckResult.DownloadSize;
            long downloadedBytes = 0;

            _status = new UpdateStatusInfo
            {
                State = UpdateState.Downloading,
                TotalBytes = totalBytes,
                Message = "正在下载更新..."
            };
            await BroadcastStatus();

            // Download each required component ZIP
            foreach (var pkg in _lastCheckResult.ChangedPackages)
            {
                token.ThrowIfCancellationRequested();

                var zipName = pkg == "app" ? $"app-{rid}.zip" : $"{pkg}.zip";
                var asset = _releaseAssets.FirstOrDefault(a => a.Name == zipName);
                if (asset is null)
                {
                    logger.LogWarning("找不到 Release Asset: {Name}", zipName);
                    continue;
                }

                _status.CurrentPackage = zipName;
                await BroadcastStatus();

                var zipPath = Path.Combine(stagingDir, zipName);

                // Download with progress
                using var response = await client.GetAsync(asset.BrowserDownloadUrl,
                    HttpCompletionOption.ResponseHeadersRead, token);
                response.EnsureSuccessStatusCode();

                await using var stream = await response.Content.ReadAsStreamAsync(token);
                await using var fileStream = File.Create(zipPath);
                var buffer = new byte[81920];
                int bytesRead;
                while ((bytesRead = await stream.ReadAsync(buffer, token)) > 0)
                {
                    await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), token);
                    downloadedBytes += bytesRead;
                    _status.DownloadedBytes = downloadedBytes;
                    _status.Progress = totalBytes > 0 ? (double)downloadedBytes / totalBytes * 100 : 0;
                    await BroadcastStatus();
                }

                logger.LogInformation("已下载: {Name} ({Size:N0} bytes)", zipName, new FileInfo(zipPath).Length);

                // Extract only changed files from this package
                using var archive = ZipFile.OpenRead(zipPath);
                foreach (var entry in archive.Entries)
                {
                    if (string.IsNullOrEmpty(entry.Name)) continue; // Skip directories

                    var entryRelativePath = entry.FullName.Replace('\\', '/');
                    // For wwwroot.zip and bundled.zip, the zip may contain the directory name
                    // Normalize path
                    if (!_remoteManifest.Files.TryGetValue(entryRelativePath, out var manifestEntry))
                        continue;

                    // Check if this file actually changed
                    var localPath = Path.Combine(appDir, entryRelativePath.Replace('/', Path.DirectorySeparatorChar));
                    if (File.Exists(localPath) && ComputeFileHash(localPath) == manifestEntry.Hash)
                        continue;

                    // Extract to staging
                    var destPath = Path.Combine(filesDir, entryRelativePath.Replace('/', Path.DirectorySeparatorChar));
                    Directory.CreateDirectory(Path.GetDirectoryName(destPath)!);
                    entry.ExtractToFile(destPath, overwrite: true);
                }

                // Clean up downloaded ZIP
                File.Delete(zipPath);
            }

            // Build delete list
            var deleteList = new List<string>();
            // Uses static ManagedExtensions field defined at class level

            foreach (var filePath in Directory.EnumerateFiles(appDir, "*", SearchOption.AllDirectories))
            {
                var relative = Path.GetRelativePath(appDir, filePath).Replace(Path.DirectorySeparatorChar, '/');
                if (relative.StartsWith("data/", StringComparison.OrdinalIgnoreCase)) continue;
                if (relative.StartsWith("appsettings", StringComparison.OrdinalIgnoreCase)) continue;
                if (!ManagedExtensions.Contains(Path.GetExtension(filePath))) continue;
                if (!_remoteManifest.Files.ContainsKey(relative))
                    deleteList.Add(relative);
            }

            if (deleteList.Count > 0)
            {
                var deleteListPath = Path.Combine(stagingDir, "delete-list.txt");
                await File.WriteAllLinesAsync(deleteListPath, deleteList, token);
            }

            // Verify extracted files
            foreach (var filePath in Directory.EnumerateFiles(filesDir, "*", SearchOption.AllDirectories))
            {
                var relative = Path.GetRelativePath(filesDir, filePath).Replace(Path.DirectorySeparatorChar, '/');
                if (_remoteManifest.Files.TryGetValue(relative, out var expected))
                {
                    var actualHash = ComputeFileHash(filePath);
                    if (actualHash != expected.Hash)
                        throw new InvalidOperationException($"文件校验失败: {relative} (预期: {expected.Hash}, 实际: {actualHash})");
                }
            }

            // Write update manifest for staging recovery
            var updateManifestPath = Path.Combine(stagingDir, "update-manifest.json");
            await File.WriteAllTextAsync(updateManifestPath,
                JsonSerializer.Serialize(_remoteManifest, JsonOptions), token);

            _status = new UpdateStatusInfo
            {
                State = UpdateState.Ready,
                Progress = 100,
                DownloadedBytes = totalBytes,
                TotalBytes = totalBytes,
                Message = "更新已下载完成，等待应用"
            };
            await BroadcastStatus();

            logger.LogInformation("更新下载完成，等待应用");
        }
        catch (OperationCanceledException)
        {
            logger.LogInformation("更新下载已取消");
            if (Directory.Exists(stagingDir))
                Directory.Delete(stagingDir, true);
            _status = new UpdateStatusInfo { State = UpdateState.Available, Message = "下载已取消" };
            await BroadcastStatus();
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "下载更新失败");
            if (Directory.Exists(stagingDir))
                Directory.Delete(stagingDir, true);
            _status = new UpdateStatusInfo { State = UpdateState.Error, Error = ex.Message };
            await BroadcastStatus();
            throw;
        }
        finally
        {
            _downloadCts = null;
        }
    }
```

- [ ] **Step 5: Add `CancelDownloadAsync`**

```csharp
    public void CancelDownload()
    {
        _downloadCts?.Cancel();
    }
```

- [ ] **Step 6: Add `ApplyUpdateAsync`**

```csharp
    public async Task<string> ApplyUpdateAsync(CancellationToken ct = default)
    {
        var appDir = AppContext.BaseDirectory;
        var stagingDir = Path.Combine(paths.Root, "update-staging");
        var filesDir = Path.Combine(stagingDir, "files");
        var updaterSrc = Path.Combine(appDir, "Updater.exe");

        if (!Directory.Exists(filesDir))
            throw new InvalidOperationException("没有就绪的更新");

        if (!File.Exists(updaterSrc))
            throw new InvalidOperationException("找不到 Updater.exe");

        // Safety checks — check for active operations
        // These are injected/checked at the endpoint level to avoid circular DI dependencies.
        // The endpoint should validate before calling this method.

        // Copy Updater.exe to temp directory
        var tempDir = Path.Combine(paths.Root, "update-temp");
        Directory.CreateDirectory(tempDir);
        var updaterDst = Path.Combine(tempDir, "Updater.exe");
        File.Copy(updaterSrc, updaterDst, overwrite: true);

        // Build arguments
        var pid = Environment.ProcessId;
        var exeName = Path.GetFileName(Environment.ProcessPath ?? "XUnityToolkit-WebUI.exe");
        var deleteListPath = Path.Combine(stagingDir, "delete-list.txt");
        var args = $"--pid {pid} --app-dir \"{appDir}\" --staging-dir \"{filesDir}\" --exe-name \"{exeName}\"";
        if (File.Exists(deleteListPath))
            args += $" --delete-list \"{deleteListPath}\"";

        // Launch Updater
        logger.LogInformation("启动 Updater.exe: {Args}", args);
        var process = new System.Diagnostics.Process
        {
            StartInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = updaterDst,
                Arguments = args,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };
        process.Start();

        _status = new UpdateStatusInfo { State = UpdateState.Applying, Message = "正在应用更新..." };
        await BroadcastStatus();

        // Schedule shutdown after response is sent
        _ = Task.Run(async () =>
        {
            await Task.Delay(500, CancellationToken.None);
            lifetime.StopApplication();
        });

        return "更新正在应用，应用即将重启";
    }
```

- [ ] **Step 7: Add `DismissUpdateAsync` and startup helpers**

```csharp
    public async Task DismissUpdateAsync(CancellationToken ct = default)
    {
        if (_lastCheckResult?.NewVersion is { } version)
        {
            var dismissedPath = Path.Combine(paths.Root, "update-dismissed-version.txt");
            await File.WriteAllTextAsync(dismissedPath, version, ct);
            _dismissedVersion = version;
        }
        _status = new UpdateStatusInfo { State = UpdateState.None };
        _lastCheckResult = null;
        await BroadcastStatus();
    }

    public async Task AutoCheckOnStartupAsync()
    {
        if (_hasAutoChecked) return;
        _hasAutoChecked = true;

        // Check for stale staging directory
        var stagingDir = Path.Combine(paths.Root, "update-staging");
        if (Directory.Exists(stagingDir))
        {
            var manifestPath = Path.Combine(stagingDir, "update-manifest.json");
            if (File.Exists(manifestPath))
            {
                // Verify staged files
                try
                {
                    var json = await File.ReadAllTextAsync(manifestPath);
                    var manifest = JsonSerializer.Deserialize<UpdateManifest>(json, JsonOptions);
                    var filesDir = Path.Combine(stagingDir, "files");
                    if (manifest is not null && Directory.Exists(filesDir))
                    {
                        _remoteManifest = manifest;
                        _status = new UpdateStatusInfo
                        {
                            State = UpdateState.Ready,
                            Message = "上次下载的更新已就绪"
                        };
                        await BroadcastStatus();
                        logger.LogInformation("检测到已就绪的更新 (v{Version})", manifest.Version);
                        return;
                    }
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "读取暂存更新清单失败，清理暂存目录");
                }
            }
            // Stale staging — clean up
            try { Directory.Delete(stagingDir, true); } catch { /* best effort */ }
        }

        // Check for update error from previous Updater run
        var errorPath = Path.Combine(paths.Root, "update-error.json");
        if (File.Exists(errorPath))
        {
            try
            {
                var errorJson = await File.ReadAllTextAsync(errorPath);
                logger.LogWarning("上次更新失败: {Error}", errorJson);
                _status = new UpdateStatusInfo
                {
                    State = UpdateState.Error,
                    Error = errorJson
                };
                await BroadcastStatus();
                File.Delete(errorPath);
            }
            catch { /* best effort */ }
            return;
        }

        // Clean up temp directories from previous update
        var tempDir = Path.Combine(paths.Root, "update-temp");
        if (Directory.Exists(tempDir))
        {
            try { Directory.Delete(tempDir, true); } catch { /* best effort */ }
        }
        var backupDir = Path.Combine(paths.Root, "update-backup");
        if (Directory.Exists(backupDir))
        {
            try { Directory.Delete(backupDir, true); } catch { /* best effort */ }
        }

        // Auto-check for updates
        await Task.Delay(5000);
        try
        {
            await CheckForUpdateAsync();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "启动时自动检查更新失败");
        }
    }

    private Task BroadcastStatus()
    {
        return hubContext.Clients.Group("update").SendAsync("UpdateStatus", _status);
    }
```

- [ ] **Step 8: Verify build**

Run: `dotnet build XUnityToolkit-WebUI/XUnityToolkit-WebUI.csproj -p:SkipFrontendBuild=true`
Expected: Build succeeds.

- [ ] **Step 9: Commit**

```bash
git add XUnityToolkit-WebUI/Services/UpdateService.cs
git commit -m "feat: 实现 UpdateService 在线更新核心服务（检查、下载、应用）"
```

---

## Chunk 3: Backend Endpoints & Program.cs Integration

### Task 6: Create Update Endpoints

**Files:**
- Create: `XUnityToolkit-WebUI/Endpoints/UpdateEndpoints.cs`

- [ ] **Step 1: Create `UpdateEndpoints.cs`**

```csharp
namespace XUnityToolkit_WebUI.Endpoints;

using XUnityToolkit_WebUI.Models;
using XUnityToolkit_WebUI.Services;

public static class UpdateEndpoints
{
    public static void MapUpdateEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/update");

        group.MapGet("/check", async (UpdateService updateService, CancellationToken ct) =>
        {
            var result = await updateService.CheckForUpdateAsync(ct);
            return Results.Ok(ApiResult<UpdateCheckResult>.Ok(result));
        });

        group.MapGet("/status", (UpdateService updateService) =>
        {
            var status = updateService.GetUpdateStatus();
            return Results.Ok(ApiResult<UpdateStatusInfo>.Ok(status));
        });

        group.MapPost("/download", async (UpdateService updateService, CancellationToken ct) =>
        {
            try
            {
                await updateService.DownloadUpdateAsync(ct);
                return Results.Ok(ApiResult.Ok());
            }
            catch (OperationCanceledException)
            {
                return Results.Ok(ApiResult.Fail("下载已取消"));
            }
            catch (Exception ex)
            {
                return Results.Ok(ApiResult.Fail(ex.Message));
            }
        });

        group.MapPost("/cancel", (UpdateService updateService) =>
        {
            updateService.CancelDownload();
            return Results.Ok(ApiResult.Ok());
        });

        group.MapPost("/apply", async (
            UpdateService updateService,
            InstallOrchestrator installOrchestrator,
            LlmTranslationService translationService,
            LocalLlmService localLlmService,
            TmpFontGeneratorService fontGenService,
            PreTranslationService preTranslationService,
            CancellationToken ct) =>
        {
            // Pre-apply safety checks
            if (installOrchestrator.HasActiveOperation)
                return Results.Ok(ApiResult.Fail("有正在进行的安装/卸载操作，请先完成或取消"));

            if (translationService.HasPendingTranslations)
                return Results.Ok(ApiResult.Fail("有正在进行的 AI 翻译任务，请先停止"));

            if (localLlmService.IsRunning)
                return Results.Ok(ApiResult.Fail("本地 LLM 服务正在运行，请先停止"));

            if (fontGenService.IsGenerating)
                return Results.Ok(ApiResult.Fail("字体生成正在进行中，请先取消"));

            if (preTranslationService.IsRunning)
                return Results.Ok(ApiResult.Fail("预翻译正在进行中，请先取消"));

            try
            {
                var message = await updateService.ApplyUpdateAsync(ct);
                return Results.Ok(ApiResult<string>.Ok(message));
            }
            catch (Exception ex)
            {
                return Results.Ok(ApiResult.Fail(ex.Message));
            }
        });

        group.MapPost("/dismiss", async (UpdateService updateService, CancellationToken ct) =>
        {
            await updateService.DismissUpdateAsync(ct);
            return Results.Ok(ApiResult.Ok());
        });
    }
}
```

Note: The safety check properties must be added to the services first (Step 2) before this file will compile.

- [ ] **Step 2: Add missing safety-check properties to services**

Add the following public properties to existing services. Each reads existing internal state:

**`Services/InstallOrchestrator.cs`** — add:
```csharp
public bool HasActiveOperation => _statuses.Values.Any(s =>
    s.Step is not (InstallStep.Idle or InstallStep.Complete or InstallStep.Failed));
```

**`Services/LlmTranslationService.cs`** — add:
```csharp
public bool HasPendingTranslations =>
    Interlocked.Read(ref _translating) > 0 || Interlocked.Read(ref _queued) > 0;
```

**`Services/LocalLlmService.cs`** — add:
```csharp
public bool IsRunning => _state == LocalLlmServerState.Running;
```

**`Services/TmpFontGeneratorService.cs`** — add (if not already public):
```csharp
public bool IsGenerating => _isGenerating;
```

**`Services/PreTranslationService.cs`** — add:
```csharp
public bool IsRunning => _statuses.Values.Any(s => s.IsRunning);
```

Note: The exact field/property names may vary — check each service's internals and adjust accordingly. The key is to expose a simple boolean that indicates active work.

- [ ] **Step 3: Add `JoinUpdateGroup`/`LeaveUpdateGroup` to `InstallProgressHub`**

In `XUnityToolkit-WebUI/Hubs/InstallProgressHub.cs`, add the following methods (following the existing pattern of `JoinLogGroup`/`JoinAiTranslationGroup`):

```csharp
public Task JoinUpdateGroup() =>
    Groups.AddToGroupAsync(Context.ConnectionId, "update");

public Task LeaveUpdateGroup() =>
    Groups.RemoveFromGroupAsync(Context.ConnectionId, "update");
```

- [ ] **Step 4: Verify build**

Run: `dotnet build XUnityToolkit-WebUI/XUnityToolkit-WebUI.csproj -p:SkipFrontendBuild=true`
Expected: Build succeeds.

- [ ] **Step 5: Commit**

```bash
git add XUnityToolkit-WebUI/Endpoints/UpdateEndpoints.cs XUnityToolkit-WebUI/Hubs/InstallProgressHub.cs
git add XUnityToolkit-WebUI/Services/InstallOrchestrator.cs XUnityToolkit-WebUI/Services/LlmTranslationService.cs
git add XUnityToolkit-WebUI/Services/LocalLlmService.cs XUnityToolkit-WebUI/Services/TmpFontGeneratorService.cs
git add XUnityToolkit-WebUI/Services/PreTranslationService.cs
git commit -m "feat: 添加在线更新 API 端点和服务安全检查属性"
```

---

### Task 7: Register Services in Program.cs

**Files:**
- Modify: `XUnityToolkit-WebUI/Program.cs`

- [ ] **Step 1: Register `UpdateService` as singleton**

In `Program.cs`, in the service registration section (around where other singletons are registered), add:

```csharp
builder.Services.AddSingleton<UpdateService>();
```

- [ ] **Step 2: Register `"GitHubUpdate"` named HttpClient**

In the `builder.Services.AddHttpClient(...)` section, add:

```csharp
builder.Services.AddHttpClient("GitHubUpdate", client =>
{
    client.Timeout = TimeSpan.FromSeconds(60);
    client.DefaultRequestHeaders.Add("User-Agent", "XUnityToolkit-WebUI");
    client.DefaultRequestHeaders.Add("Accept", "application/vnd.github+json");
});
```

- [ ] **Step 3: Map update endpoints**

In the endpoint mapping section (around where other `app.Map*Endpoints()` calls are), add:

```csharp
app.MapUpdateEndpoints();
```

- [ ] **Step 4: Add startup auto-check**

After the existing startup initialization code (after `app.Lifetime.ApplicationStarted` or near the end before `app.Run()`), add:

```csharp
app.Lifetime.ApplicationStarted.Register(() =>
{
    _ = Task.Run(async () =>
    {
        var updateService = app.Services.GetRequiredService<UpdateService>();
        await updateService.AutoCheckOnStartupAsync();
    });
});
```

- [ ] **Step 5: Verify build and run**

Run: `dotnet build XUnityToolkit-WebUI/XUnityToolkit-WebUI.csproj -p:SkipFrontendBuild=true`
Expected: Build succeeds.

Optionally test run: `dotnet run --project XUnityToolkit-WebUI/XUnityToolkit-WebUI.csproj`
Expected: Starts without errors, auto-check fires after 5s (may fail on GitHub API if no releases exist yet — that's expected).

- [ ] **Step 6: Commit**

```bash
git add XUnityToolkit-WebUI/Program.cs
git commit -m "feat: 注册 UpdateService、GitHub HttpClient 和启动自动更新检查"
```

---

## Chunk 4: Frontend Implementation

### Task 8: Add Frontend Types and API Client

**Files:**
- Modify: `XUnityToolkit-Vue/src/api/types.ts`
- Create: `XUnityToolkit-Vue/src/api/update.ts`

- [ ] **Step 1: Add update types to `types.ts`**

Append the following interfaces to `src/api/types.ts`:

```typescript
// Online Update
export type UpdateState = 'none' | 'checking' | 'available' | 'downloading' | 'ready' | 'applying' | 'error'

export interface UpdateCheckResult {
  updateAvailable: boolean
  newVersion?: string
  changelog?: string
  downloadSize: number
  changedPackages: string[]
  changedFileCount: number
  deletedFileCount: number
}

export interface UpdateStatusInfo {
  state: UpdateState
  progress: number
  downloadedBytes: number
  totalBytes: number
  currentPackage?: string
  message?: string
  error?: string
}

export interface UpdateAvailableInfo {
  version: string
  changelog?: string
  downloadSize: number
  changedPackages: string[]
}
```

Also add to `AppSettings` interface:

```typescript
receivePreReleaseUpdates: boolean
```

- [ ] **Step 2: Create `src/api/update.ts`**

```typescript
import { api } from './client'
import type { UpdateCheckResult, UpdateStatusInfo } from './types'

export const updateApi = {
  check: () => api.get<UpdateCheckResult>('/api/update/check'),
  getStatus: () => api.get<UpdateStatusInfo>('/api/update/status'),
  download: () => api.post<void>('/api/update/download'),
  cancel: () => api.post<void>('/api/update/cancel'),
  apply: () => api.post<string>('/api/update/apply'),
  dismiss: () => api.post<void>('/api/update/dismiss'),
}
```

- [ ] **Step 3: Commit**

```bash
git add XUnityToolkit-Vue/src/api/types.ts XUnityToolkit-Vue/src/api/update.ts
git commit -m "feat: 添加在线更新前端类型定义和 API 客户端"
```

---

### Task 9: Create Update Pinia Store

**Files:**
- Create: `XUnityToolkit-Vue/src/stores/update.ts`

- [ ] **Step 1: Create `src/stores/update.ts`**

Follow the existing store patterns (see `install.ts` for SignalR integration pattern):

```typescript
import { ref, computed } from 'vue'
import { defineStore } from 'pinia'
import * as signalR from '@microsoft/signalr'
import { updateApi } from '@/api/update'
import type { UpdateState, UpdateCheckResult, UpdateStatusInfo, UpdateAvailableInfo } from '@/api/types'

export const useUpdateStore = defineStore('update', () => {
  const state = ref<UpdateState>('none')
  const progress = ref(0)
  const downloadedBytes = ref(0)
  const totalBytes = ref(0)
  const currentPackage = ref<string>()
  const message = ref<string>()
  const error = ref<string>()

  const checkResult = ref<UpdateCheckResult>()
  const availableInfo = ref<UpdateAvailableInfo>()
  const showDetails = ref(false)

  let connection: signalR.HubConnection | null = null

  const isUpdateAvailable = computed(() => state.value === 'available')
  const isDownloading = computed(() => state.value === 'downloading')
  const isReady = computed(() => state.value === 'ready')
  const hasError = computed(() => state.value === 'error')

  function formatBytes(bytes: number): string {
    if (bytes < 1024) return `${bytes} B`
    if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`
    return `${(bytes / (1024 * 1024)).toFixed(1)} MB`
  }

  async function connectHub() {
    if (connection) return

    connection = new signalR.HubConnectionBuilder()
      .withUrl('/hubs/install')
      .withAutomaticReconnect()
      .build()

    connection.on('UpdateStatus', (status: UpdateStatusInfo) => {
      state.value = status.state
      progress.value = status.progress
      downloadedBytes.value = status.downloadedBytes
      totalBytes.value = status.totalBytes
      currentPackage.value = status.currentPackage ?? undefined
      message.value = status.message ?? undefined
      error.value = status.error ?? undefined
    })

    connection.on('UpdateAvailable', (info: UpdateAvailableInfo) => {
      availableInfo.value = info
      state.value = 'available'
    })

    try {
      await connection.start()
      await connection.invoke('JoinUpdateGroup')
    } catch (err) {
      console.error('Failed to connect update hub:', err)
    }
  }

  async function disconnectHub() {
    if (!connection) return
    try {
      await connection.invoke('LeaveUpdateGroup')
      await connection.stop()
    } catch { /* best effort */ }
    connection = null
  }

  async function checkForUpdate() {
    try {
      checkResult.value = await updateApi.check()
      if (checkResult.value.updateAvailable) {
        availableInfo.value = {
          version: checkResult.value.newVersion!,
          changelog: checkResult.value.changelog,
          downloadSize: checkResult.value.downloadSize,
          changedPackages: checkResult.value.changedPackages,
        }
      }
    } catch (err) {
      console.error('Check for update failed:', err)
    }
  }

  async function downloadUpdate() {
    try {
      await updateApi.download()
    } catch (err) {
      console.error('Download update failed:', err)
    }
  }

  async function cancelDownload() {
    try {
      await updateApi.cancel()
    } catch (err) {
      console.error('Cancel download failed:', err)
    }
  }

  async function applyUpdate() {
    try {
      await updateApi.apply()
      // App will restart — connection will be lost
    } catch (err) {
      console.error('Apply update failed:', err)
    }
  }

  async function dismissUpdate() {
    try {
      await updateApi.dismiss()
      state.value = 'none'
      checkResult.value = undefined
      availableInfo.value = undefined
      showDetails.value = false
    } catch (err) {
      console.error('Dismiss update failed:', err)
    }
  }

  return {
    state,
    progress,
    downloadedBytes,
    totalBytes,
    currentPackage,
    message,
    error,
    checkResult,
    availableInfo,
    showDetails,
    isUpdateAvailable,
    isDownloading,
    isReady,
    hasError,
    formatBytes,
    connectHub,
    disconnectHub,
    checkForUpdate,
    downloadUpdate,
    cancelDownload,
    applyUpdate,
    dismissUpdate,
  }
})
```

- [ ] **Step 2: Commit**

```bash
git add XUnityToolkit-Vue/src/stores/update.ts
git commit -m "feat: 添加在线更新 Pinia Store（SignalR 实时状态 + API 交互）"
```

---

### Task 10: Add Update UI to SettingsView

**Files:**
- Modify: `XUnityToolkit-Vue/src/views/SettingsView.vue`

- [ ] **Step 1: Import update store and connect SignalR**

In the `<script setup>` section:

1. Add Vue lifecycle import (if not present): `import { onMounted, onUnmounted, ref, computed, watch } from 'vue'`
2. Add Naive UI imports: Add `NProgress`, `NSpin`, `NSwitch` to the existing naive-ui import line
3. Add icon import: `import { SystemUpdateAltOutlined } from '@vicons/material'`
4. Add store import: `import { useUpdateStore } from '@/stores/update'`
5. Add store instantiation: `const updateStore = useUpdateStore()`

In the `onMounted` hook, add:

```typescript
updateStore.connectHub()
```

Add `onUnmounted` hook:

```typescript
onUnmounted(() => {
  updateStore.disconnectHub()
})
```

- [ ] **Step 2: Add `receivePreReleaseUpdates` to settings data flow**

In the settings loading function (`loadSettings` or equivalent), ensure `receivePreReleaseUpdates` is included in the settings object (it should auto-sync since it's part of `AppSettings`). If the existing auto-save pattern handles it automatically via deep watch, no extra code is needed.

- [ ] **Step 3: Add update section template**

In the template, find the "About" section (the section containing version display). Replace or extend the version info card to include the full update UI:

```vue
<!-- Update Section -->
<div class="section-card">
  <div class="section-header">
    <NIcon :size="20"><SystemUpdateAltOutlined /></NIcon>
    <span>更新</span>
  </div>

  <!-- Version & Check Button Row -->
  <div class="info-row">
    <div class="info-card">
      <div class="info-card-icon version">
        <NIcon :size="18"><LocalOfferOutlined /></NIcon>
      </div>
      <div class="info-card-content">
        <span class="info-label">版本</span>
        <span class="info-value mono">v{{ shortVersion }}</span>
      </div>
    </div>

    <div class="update-status">
      <template v-if="updateStore.state === 'none'">
        <span class="status-ok">✓ 已是最新版本</span>
      </template>
      <template v-else-if="updateStore.state === 'checking'">
        <NSpin :size="14" />
        <span>检查中...</span>
      </template>
      <template v-else-if="updateStore.isUpdateAvailable">
        <span class="status-update">新版本 v{{ updateStore.availableInfo?.version }} 可用</span>
      </template>
      <template v-else-if="updateStore.isReady">
        <span class="status-ready">更新已就绪</span>
      </template>
      <template v-else-if="updateStore.hasError">
        <span class="status-error">更新出错</span>
      </template>
    </div>

    <NButton
      size="small"
      :loading="updateStore.state === 'checking'"
      :disabled="updateStore.isDownloading"
      @click="updateStore.checkForUpdate()"
    >
      检查更新
    </NButton>
  </div>

  <!-- Update Available Details -->
  <template v-if="updateStore.isUpdateAvailable && updateStore.availableInfo">
    <div class="update-details">
      <div class="update-header">
        <span class="update-title">更新可用: v{{ updateStore.availableInfo.version }}</span>
      </div>

      <div v-if="updateStore.availableInfo.changelog" class="update-changelog">
        <div class="changelog-label">更新内容:</div>
        <div class="changelog-content">{{ updateStore.availableInfo.changelog }}</div>
      </div>

      <div class="update-packages">
        <div class="packages-label">需要下载:</div>
        <div v-for="pkg in updateStore.availableInfo.changedPackages" :key="pkg" class="package-item">
          ● {{ pkg }}.zip
        </div>
        <div class="packages-total">
          总计: {{ updateStore.formatBytes(updateStore.availableInfo.downloadSize) }}
        </div>
      </div>

      <div class="update-actions">
        <NButton type="primary" @click="updateStore.downloadUpdate()">开始更新</NButton>
        <NButton @click="updateStore.dismissUpdate()">暂不更新</NButton>
      </div>
    </div>
  </template>

  <!-- Download Progress -->
  <template v-if="updateStore.isDownloading">
    <div class="update-progress">
      <div class="progress-header">
        正在下载更新
        <span v-if="updateStore.currentPackage">: {{ updateStore.currentPackage }}</span>
      </div>
      <NProgress
        type="line"
        :percentage="Math.round(updateStore.progress)"
        :show-indicator="true"
      />
      <div class="progress-detail">
        {{ updateStore.formatBytes(updateStore.downloadedBytes) }} / {{ updateStore.formatBytes(updateStore.totalBytes) }}
      </div>
      <NButton size="small" @click="updateStore.cancelDownload()">取消</NButton>
    </div>
  </template>

  <!-- Ready to Apply -->
  <template v-if="updateStore.isReady">
    <div class="update-ready">
      <span>更新已下载完成，需要重启应用以完成更新</span>
      <NButton type="primary" @click="updateStore.applyUpdate()">立即重启更新</NButton>
    </div>
  </template>

  <!-- Error -->
  <template v-if="updateStore.hasError">
    <div class="update-error">
      <span>{{ updateStore.error || '更新过程中出现错误' }}</span>
      <NButton size="small" @click="updateStore.checkForUpdate()">重试</NButton>
    </div>
  </template>

  <!-- Prerelease Toggle -->
  <div class="setting-row" style="margin-top: 12px;">
    <div class="setting-info">
      <span class="setting-label">接收预发布更新</span>
      <span class="setting-description">接收尚未正式发布的测试版本，可能包含新功能但稳定性较低</span>
    </div>
    <NSwitch v-model:value="settings.receivePreReleaseUpdates" />
  </div>
</div>
```

Note: Import `SystemUpdateAltOutlined` from `@vicons/material`. Also import `NProgress`, `NSpin` from naive-ui if not already imported. The existing `LocalOfferOutlined` icon import should already be present.

- [ ] **Step 4: Add CSS styles for update section**

Add to the `<style scoped>` section:

```css
.update-status {
  display: flex;
  align-items: center;
  gap: 6px;
  font-size: 13px;
}
.status-ok { color: var(--success-color, #18a058); }
.status-update { color: var(--accent); font-weight: 500; }
.status-ready { color: var(--accent); font-weight: 500; }
.status-error { color: var(--error-color, #d03050); }

.update-details {
  margin-top: 12px;
  padding: 16px;
  border-radius: 8px;
  background: var(--card-bg);
  border: 1px solid var(--border-color);
}
.update-header { margin-bottom: 12px; }
.update-title { font-weight: 600; font-size: 14px; }
.update-changelog { margin-bottom: 12px; }
.changelog-label { font-size: 13px; color: var(--text-secondary); margin-bottom: 4px; }
.changelog-content { font-size: 13px; white-space: pre-line; }
.packages-label { font-size: 13px; color: var(--text-secondary); margin-bottom: 4px; }
.package-item { font-size: 13px; font-family: monospace; }
.packages-total { font-size: 13px; font-weight: 500; margin-top: 4px; }
.update-actions { display: flex; gap: 8px; margin-top: 16px; }

.update-progress {
  margin-top: 12px;
  padding: 16px;
  border-radius: 8px;
  background: var(--card-bg);
  border: 1px solid var(--border-color);
}
.progress-header { font-size: 14px; margin-bottom: 8px; }
.progress-detail { font-size: 12px; color: var(--text-secondary); margin-top: 4px; margin-bottom: 8px; }

.update-ready, .update-error {
  margin-top: 12px;
  padding: 16px;
  border-radius: 8px;
  background: var(--card-bg);
  border: 1px solid var(--border-color);
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 12px;
}
.update-error { border-color: var(--error-color, #d03050); }
```

- [ ] **Step 5: Verify frontend builds**

Run: `cd XUnityToolkit-Vue && npm run build`
Expected: Build succeeds without errors.

Run: `cd XUnityToolkit-Vue && npx vue-tsc --noEmit`
Expected: Type-check passes.

- [ ] **Step 6: Commit**

```bash
git add XUnityToolkit-Vue/src/views/SettingsView.vue
git commit -m "feat: 在设置页面添加在线更新 UI（检查、下载进度、一键重启）"
```

---

## Chunk 5: System Tray Notifications, CI, & Documentation

### Task 11: Add System Tray Update Notifications

**Files:**
- Modify: `XUnityToolkit-WebUI/Services/UpdateService.cs`

- [ ] **Step 1: Inject SystemTrayService and add notifications**

Add `SystemTrayService` to the constructor parameters:

```csharp
public sealed class UpdateService(
    IHttpClientFactory httpClientFactory,
    IHubContext<InstallProgressHub> hubContext,
    AppSettingsService settingsService,
    SystemTrayService trayService,
    AppDataPaths paths,
    IHostApplicationLifetime lifetime,
    ILogger<UpdateService> logger)
```

In `CheckForUpdateAsync`, after the `UpdateAvailable` SignalR broadcast, add:

```csharp
trayService.ShowNotification("XUnityToolkit", $"发现新版本 v{remoteVersion} 可用");
```

In `DownloadUpdateAsync`, after setting state to `Ready`, add:

```csharp
trayService.ShowNotification("XUnityToolkit", "更新已就绪，请在设置中重启更新");
```

- [ ] **Step 2: Commit**

```bash
git add XUnityToolkit-WebUI/Services/UpdateService.cs
git commit -m "feat: 添加系统托盘更新通知（新版本可用、更新就绪）"
```

---

### Task 12: Update CI Workflow

**Files:**
- Modify: `.github/workflows/build.yml`

- [ ] **Step 1: Remove single-file from publish step**

Find the `dotnet publish` command in `build.yml` and remove `-p:PublishSingleFile=true` and `-p:IncludeNativeLibrariesForSelfExtract=true`.

- [ ] **Step 2: Add Updater AOT build step**

After the TranslatorEndpoint build step and before the publish step, add:

```yaml
- name: Build Updater (AOT)
  run: |
    dotnet publish Updater/Updater.csproj \
      -c Release \
      -r ${{ matrix.rid }} \
      /p:PublishAot=true
```

- [ ] **Step 3: Add Updater.exe copy after publish**

In the post-publish steps, add:

```yaml
- name: Copy Updater.exe
  run: |
    cp Updater/bin/Release/net10.0/${{ matrix.rid }}/publish/Updater.exe \
       Release/${{ matrix.rid }}/
```

- [ ] **Step 4: Add manifest generation step**

After all files are in `Release/{rid}/`, add a step that generates `manifest-{rid}.json`. Use a PowerShell or bash script equivalent to the `Generate-Manifest` function from `build.ps1`:

```yaml
- name: Generate update manifest
  shell: pwsh
  run: |
    $rid = "${{ matrix.rid }}"
    $version = "${{ steps.ver.outputs.build_version }}"
    $releaseDir = "Release/$rid"
    # ... same logic as Generate-Manifest function
```

- [ ] **Step 5: Add component ZIP creation step**

```yaml
- name: Create component ZIPs
  shell: pwsh
  run: |
    $rid = "${{ matrix.rid }}"
    $releaseDir = "Release/$rid"
    # Create app-{rid}.zip, wwwroot.zip, bundled.zip
    # ... same logic as Create-ComponentZips function
```

- [ ] **Step 6: Upload additional release assets**

In the release creation step, add the new assets:

```yaml
- name: Upload component packages
  run: |
    gh release upload "${{ inputs.release_tag }}" \
      "Release/manifest-${{ matrix.rid }}.json" \
      "Release/app-${{ matrix.rid }}.zip" \
      --clobber
    # Only upload wwwroot.zip and bundled.zip once (not per-RID)
    if [ "${{ matrix.rid }}" = "win-x64" ]; then
      gh release upload "${{ inputs.release_tag }}" \
        "Release/wwwroot.zip" \
        "Release/bundled.zip" \
        --clobber
    fi
```

- [ ] **Step 7: Commit**

```bash
git add .github/workflows/build.yml
git commit -m "ci: 更新 CI 工作流支持多文件发布、Updater 构建和组件包上传"
```

---

### Task 13: Update dep-check.yml

**Files:**
- Modify: `.github/workflows/dep-check.yml`

- [ ] **Step 1: Add manifest and component ZIP generation**

In `dep-check.yml`, the auto-prerelease workflow calls `build.yml`. Since `build.yml` is being updated in Task 12 to include manifest generation and component ZIP creation, the prerelease builds will automatically produce these artifacts. Verify that `dep-check.yml` passes the required inputs to `build.yml` correctly.

If `dep-check.yml` has any inline build logic that duplicates `build.yml`, update it to also remove `-p:PublishSingleFile=true` and add the manifest/ZIP steps.

- [ ] **Step 2: Commit (if changes needed)**

```bash
git add .github/workflows/dep-check.yml
git commit -m "ci: 更新 dep-check 工作流支持多文件发布和组件包"
```

---

### Task 14: Update CLAUDE.md

**Files:**
- Modify: `CLAUDE.md`

- [ ] **Step 1: Add update-related documentation to CLAUDE.md**

In the Architecture section, add after the BepInEx Log bullet:

```markdown
- **Online Update:** `UpdateService` checks GitHub Releases for new versions; manifest-based differential download (app/wwwroot/bundled component ZIPs); `Updater.exe` (AOT, no runtime dependency) handles file replacement and restart; staging at `data/update-staging/`; two-phase backup-then-replace for atomicity; rollback on failure; prerelease opt-in via `AppSettings.ReceivePreReleaseUpdates`
```

In the SignalR groups list in the Architecture section, add `update` to the list.

In the API Endpoints section, add:

```markdown
- **Update:** `GET /api/update/check`, `GET .../status`, `POST .../download`, `POST .../cancel`, `POST .../apply`, `POST .../dismiss`
```

In the Sync Points section, add:

```markdown
- **Adding AppSettings fields (ReceivePreReleaseUpdates):** Sync 4 places: `Models/AppSettings.cs`, `src/api/types.ts`, store's `loadPreferences`/`savePreferences`, `SettingsView.vue`
- **Update status model:** `Models/UpdateInfo.cs` → `src/api/types.ts` → `src/stores/update.ts` → `SettingsView.vue`
```

In the Build & Deploy section, add:

```markdown
- **Multi-file publishing:** `PublishSingleFile` removed; `ExcludeFromSingleFile` target removed; LibCpp2IL.dll works naturally in multi-file mode
- **Updater:** `Updater/Updater.csproj` (net10.0, PublishAot); built per-RID; copied to Release/{rid}/ post-publish
- **Update manifest:** `manifest-{rid}.json` generated per release with SHA256 hashes; component ZIPs: `app-{rid}.zip`, `wwwroot.zip`, `bundled.zip`
```

- [ ] **Step 2: Commit**

```bash
git add CLAUDE.md
git commit -m "docs: 更新 CLAUDE.md 添加在线更新系统架构文档"
```

---

## Verification Checklist

After all tasks are complete:

- [ ] `dotnet build XUnityToolkit-WebUI/XUnityToolkit-WebUI.csproj` succeeds
- [ ] `dotnet build Updater/Updater.csproj` succeeds
- [ ] `cd XUnityToolkit-Vue && npm run build` succeeds
- [ ] `cd XUnityToolkit-Vue && npx vue-tsc --noEmit` passes
- [ ] `dotnet run --project XUnityToolkit-WebUI/XUnityToolkit-WebUI.csproj` starts without errors
- [ ] `GET /api/update/check` returns valid JSON response
- [ ] `GET /api/update/status` returns `{ state: "none" }` initially
- [ ] Settings page shows update section with version and "Check for Updates" button
- [ ] Prerelease toggle appears and is wired to settings save
- [ ] `.\build.ps1 -Runtime win-x64 -SkipDownload` generates manifest and component ZIPs
