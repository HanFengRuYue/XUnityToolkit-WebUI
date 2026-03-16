# Update Check Rate Limit Bypass Implementation Plan

> **For agentic workers:** REQUIRED: Use superpowers:subagent-driven-development (if subagents available) or superpowers:executing-plans to implement this plan. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Eliminate GitHub API rate limit errors during update checks by using CDN direct downloads and Atom Feed, falling back to the existing API path for backward compatibility.

**Architecture:** Three-layer check strategy — CDN direct download for stable releases, Atom Feed + CDN for pre-releases, GitHub API as fallback. CI generates a static `update-check.json` per release. `DownloadUpdateAsync` gains CDN URL resolution via `_resolvedTag`.

**Tech Stack:** ASP.NET Core HttpClient, System.Xml.Linq (Atom Feed parsing), PowerShell (CI), GitHub Actions YAML

**Spec:** `docs/superpowers/specs/2026-03-16-update-check-rate-limit-bypass-design.md`

---

## Chunk 1: Models & HttpClient Registration

### Task 1: Add `UpdateCheckInfo` model and extend `UpdateCheckCache`

**Files:**
- Modify: `XUnityToolkit-WebUI/Models/UpdateInfo.cs`

- [ ] **Step 1: Add `UpdateCheckInfo` class**

Add after the existing `UpdateCheckCache` class (after line 66 of `UpdateInfo.cs`):

```csharp
public sealed class UpdateCheckInfo
{
    [JsonPropertyName("tag")]
    public required string Tag { get; set; }

    [JsonPropertyName("version")]
    public required string Version { get; set; }

    [JsonPropertyName("changelog")]
    public string? Changelog { get; set; }

    [JsonPropertyName("prerelease")]
    public bool Prerelease { get; set; }

    [JsonPropertyName("assets")]
    public Dictionary<string, long> Assets { get; set; } = [];
}
```

The file already has `using System.Text.Json.Serialization;` at line 3.

- [ ] **Step 2: Add `ApiUrl` field to `UpdateCheckCache`**

In the existing `UpdateCheckCache` class (line 61-66), add:

```csharp
public string? ApiUrl { get; set; }
```

After the `LastChecked` property.

- [ ] **Step 3: Verify build**

Run: `dotnet build XUnityToolkit-WebUI/XUnityToolkit-WebUI.csproj -p:SkipFrontendBuild=true`
Expected: Build succeeds with no errors.

- [ ] **Step 4: Commit**

```bash
git add XUnityToolkit-WebUI/Models/UpdateInfo.cs
git commit -m "feat(更新): 添加 UpdateCheckInfo 模型和 UpdateCheckCache.ApiUrl 字段"
```

### Task 2: Register `GitHubCdn` HttpClient

**Files:**
- Modify: `XUnityToolkit-WebUI/Program.cs:112-118`

- [ ] **Step 1: Add GitHubCdn HttpClient registration**

After the existing `GitHubUpdate` HttpClient registration (after line 118 of `Program.cs`), add:

```csharp
// HTTP client for GitHub CDN/web requests (not API — no rate limit)
builder.Services.AddHttpClient("GitHubCdn", client =>
{
    client.Timeout = TimeSpan.FromSeconds(30);
    client.DefaultRequestHeaders.Add("User-Agent", "XUnityToolkit-WebUI");
});
```

No `Accept: application/vnd.github+json` header — these are CDN/web requests, not API requests.

- [ ] **Step 2: Verify build**

Run: `dotnet build XUnityToolkit-WebUI/XUnityToolkit-WebUI.csproj -p:SkipFrontendBuild=true`
Expected: Build succeeds.

- [ ] **Step 3: Commit**

```bash
git add XUnityToolkit-WebUI/Program.cs
git commit -m "feat(更新): 注册 GitHubCdn HttpClient 用于 CDN/Web 请求"
```

---

## Chunk 2: UpdateService — Extract API Logic & Add CDN/Atom Methods

### Task 3: Add new fields and refactor `CheckForUpdateAsync` orchestration

**Files:**
- Modify: `XUnityToolkit-WebUI/Services/UpdateService.cs`

This is the largest task. It refactors `CheckForUpdateAsync` into an orchestrator that calls three private methods, and updates `DownloadUpdateAsync`.

- [ ] **Step 1: Add new fields**

Add after the existing `_dismissedVersion` field (line 47):

```csharp
private string? _resolvedTag;
private DateTime _lastCheckTime = DateTime.MinValue;
```

- [ ] **Step 2: Add `ExtractVersionFromTag` helper**

Add as a new private static method (after `IsNewerVersion`, around line 85):

```csharp
private static string ExtractVersionFromTag(string tag)
{
    if (tag.StartsWith("auto-")) return tag["auto-".Length..];
    return tag.TrimStart('v');
}
```

- [ ] **Step 3: Add `TryCheckViaCdnAsync` method**

Add as a new private method at the end of the class (before the closing `}`):

```csharp
private async Task<(UpdateCheckInfo?, string?)> TryCheckViaCdnAsync(CancellationToken ct)
{
    try
    {
        var client = httpClientFactory.CreateClient("GitHubCdn");
        var url = $"https://github.com/{GitHubOwner}/{GitHubRepo}/releases/latest/download/update-check.json";
        var json = await client.GetStringAsync(url, ct);
        var info = JsonSerializer.Deserialize<UpdateCheckInfo>(json, JsonOptions);
        if (info is null) return (null, null);
        logger.LogDebug("CDN 获取 update-check.json 成功: v{Version}", info.Version);
        return (info, info.Tag);
    }
    catch (Exception ex)
    {
        logger.LogDebug(ex, "CDN 获取 update-check.json 失败，将回退到 API");
        return (null, null);
    }
}
```

- [ ] **Step 4: Add `TryCheckViaAtomFeedAsync` method**

Add after `TryCheckViaCdnAsync`:

```csharp
private async Task<(UpdateCheckInfo?, string?)> TryCheckViaAtomFeedAsync(CancellationToken ct)
{
    try
    {
        var client = httpClientFactory.CreateClient("GitHubCdn");

        // Fetch Atom Feed
        var feedUrl = $"https://github.com/{GitHubOwner}/{GitHubRepo}/releases.atom";
        var feedXml = await client.GetStringAsync(feedUrl, ct);

        // Parse first entry's tag from link href
        var ns = System.Xml.Linq.XNamespace.Get("http://www.w3.org/2005/Atom");
        var doc = System.Xml.Linq.XDocument.Parse(feedXml);
        var firstEntry = doc.Root?.Element(ns + "entry");
        var link = firstEntry?.Elements(ns + "link")
            .FirstOrDefault(e => e.Attribute("rel")?.Value == "alternate");
        var href = link?.Attribute("href")?.Value;
        if (href is null)
        {
            logger.LogDebug("Atom Feed 解析失败: 未找到 link 元素");
            return (null, null);
        }

        var tag = href.Split('/')[^1];
        logger.LogDebug("Atom Feed 获取最新 tag: {Tag}", tag);

        // Download update-check.json for this tag
        var checkUrl = $"https://github.com/{GitHubOwner}/{GitHubRepo}/releases/download/{tag}/update-check.json";
        var json = await client.GetStringAsync(checkUrl, ct);
        var info = JsonSerializer.Deserialize<UpdateCheckInfo>(json, JsonOptions);
        if (info is null) return (null, null);
        return (info, tag);
    }
    catch (Exception ex)
    {
        logger.LogDebug(ex, "Atom Feed 检查失败，将回退到 API");
        return (null, null);
    }
}
```

- [ ] **Step 5: Add `CheckViaGitHubApiAsync` method**

This extracts the existing API logic from `CheckForUpdateAsync` lines 124-239 into a standalone method. Add after `TryCheckViaAtomFeedAsync`:

```csharp
private async Task<(UpdateCheckInfo?, string?)> CheckViaGitHubApiAsync(
    bool isPreRelease, CancellationToken ct)
{
    var client = httpClientFactory.CreateClient("GitHubUpdate");
    var apiUrl = isPreRelease
        ? $"https://api.github.com/repos/{GitHubOwner}/{GitHubRepo}/releases"
        : $"https://api.github.com/repos/{GitHubOwner}/{GitHubRepo}/releases/latest";

    // Load cached ETag
    var cachePath = Path.Combine(paths.Root, "update-check-cache.json");
    UpdateCheckCache? cache = null;
    if (File.Exists(cachePath))
    {
        var cacheJson = await File.ReadAllTextAsync(cachePath, ct);
        cache = JsonSerializer.Deserialize<UpdateCheckCache>(cacheJson, JsonOptions);
    }

    // Fix startup ETag bug: restore _lastCheckedApiUrl from disk cache
    if (_lastCheckedApiUrl is null && cache?.ApiUrl is not null)
        _lastCheckedApiUrl = cache.ApiUrl;

    using var request = new HttpRequestMessage(HttpMethod.Get, apiUrl);
    if (cache?.ETag is not null && _lastCheckedApiUrl == apiUrl)
        request.Headers.TryAddWithoutValidation("If-None-Match", cache.ETag);
    _lastCheckedApiUrl = apiUrl;

    using var response = await client.SendAsync(request, ct);

    if (response.StatusCode == System.Net.HttpStatusCode.NotModified)
    {
        logger.LogInformation("GitHub API 返回 304 Not Modified，无新版本");
        return (null, null);
    }

    if ((int)response.StatusCode is 403 or 429)
    {
        var retryAfter = response.Headers.RetryAfter?.Delta?.TotalSeconds
            ?? response.Headers.RetryAfter?.Date?.Subtract(DateTimeOffset.UtcNow).TotalSeconds;
        var waitMsg = retryAfter > 0 ? $"，请 {(int)retryAfter} 秒后重试" : "，请稍后再试";
        logger.LogWarning("GitHub API 速率限制 ({StatusCode})", (int)response.StatusCode);
        throw new InvalidOperationException($"GitHub API 请求频率超限{waitMsg}");
    }

    response.EnsureSuccessStatusCode();
    var json = await response.Content.ReadAsStringAsync(ct);

    GitHubRelease? release;
    if (isPreRelease)
    {
        var releases = JsonSerializer.Deserialize<List<GitHubRelease>>(json, JsonOptions);
        release = releases?.FirstOrDefault();
    }
    else
    {
        release = JsonSerializer.Deserialize<GitHubRelease>(json, JsonOptions);
    }

    if (release is null) return (null, null);

    // Save ETag cache
    var newCache = new UpdateCheckCache
    {
        ETag = response.Headers.ETag?.Tag,
        LatestVersion = ExtractVersionFromTag(release.TagName),
        LastChecked = DateTime.UtcNow,
        ApiUrl = apiUrl
    };
    await File.WriteAllTextAsync(cachePath,
        JsonSerializer.Serialize(newCache, JsonOptions), ct);

    // Store release assets for download phase (API fallback only)
    _releaseAssets = release.Assets;

    // Build UpdateCheckInfo from API response
    var version = ExtractVersionFromTag(release.TagName);
    var changelog = ExtractChangelog(release.Body);
    var assets = new Dictionary<string, long>();
    foreach (var asset in release.Assets)
        assets[asset.Name] = asset.Size;

    var info = new UpdateCheckInfo
    {
        Tag = release.TagName,
        Version = version,
        Changelog = changelog,
        Prerelease = release.Prerelease,
        Assets = assets
    };

    return (info, release.TagName);
}
```

- [ ] **Step 6: Rewrite `CheckForUpdateAsync` as orchestrator**

Replace the entire `CheckForUpdateAsync` method body (lines 114-334) with the new orchestration logic:

```csharp
public async Task<UpdateCheckResult> CheckForUpdateAsync(
    CancellationToken ct = default, bool bypassThrottle = false)
{
    // Throttle guard (before semaphore)
    if (!bypassThrottle && DateTime.UtcNow - _lastCheckTime < TimeSpan.FromMinutes(5))
        return _lastCheckResult ?? new UpdateCheckResult();

    if (!await _lock.WaitAsync(0, ct))
        return _lastCheckResult ?? new UpdateCheckResult();

    try
    {
        _status = new UpdateStatusInfo { State = UpdateState.Checking, Message = "正在检查更新..." };
        await BroadcastStatus();

        var settings = await settingsService.GetAsync(ct);
        var rid = GetCurrentRid();
        var localVersion = GetCurrentVersion();

        // Reset state from previous check
        _resolvedTag = null;
        _releaseAssets = null;

        // Layer 1/2: Try CDN or Atom Feed first
        UpdateCheckInfo? info;
        string? tag;

        if (settings.ReceivePreReleaseUpdates)
            (info, tag) = await TryCheckViaAtomFeedAsync(ct);
        else
            (info, tag) = await TryCheckViaCdnAsync(ct);

        // Layer 3: Fall back to GitHub API
        if (info is null)
            (info, tag) = await CheckViaGitHubApiAsync(settings.ReceivePreReleaseUpdates, ct);

        if (info is null)
        {
            _status = new UpdateStatusInfo { State = UpdateState.None };
            await BroadcastStatus();
            _lastCheckResult = new UpdateCheckResult();
            return _lastCheckResult;
        }

        var remoteVersion = info.Version;

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
        string? manifestJson;
        if (tag is not null)
        {
            // CDN path
            var cdnClient = httpClientFactory.CreateClient("GitHubCdn");
            var manifestUrl = $"https://github.com/{GitHubOwner}/{GitHubRepo}/releases/download/{tag}/manifest-{rid}.json";
            manifestJson = await cdnClient.GetStringAsync(manifestUrl, ct);
        }
        else if (_releaseAssets is not null)
        {
            // API fallback path
            var apiClient = httpClientFactory.CreateClient("GitHubUpdate");
            var manifestAsset = _releaseAssets.FirstOrDefault(a => a.Name == $"manifest-{rid}.json");
            if (manifestAsset is null)
            {
                logger.LogWarning("Release {Version} 缺少 manifest-{Rid}.json", remoteVersion, rid);
                _status = new UpdateStatusInfo { State = UpdateState.None };
                await BroadcastStatus();
                _lastCheckResult = new UpdateCheckResult();
                return _lastCheckResult;
            }
            manifestJson = await apiClient.GetStringAsync(manifestAsset.BrowserDownloadUrl, ct);
        }
        else
        {
            logger.LogWarning("无法获取 manifest: 既无 tag 也无 release assets");
            _status = new UpdateStatusInfo { State = UpdateState.None };
            await BroadcastStatus();
            _lastCheckResult = new UpdateCheckResult();
            return _lastCheckResult;
        }

        _remoteManifest = JsonSerializer.Deserialize<UpdateManifest>(manifestJson, JsonOptions);
        _resolvedTag = tag;

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

        foreach (var (relativePath, entry) in _remoteManifest.Files)
        {
            var localPath = Path.Combine(appDir, relativePath.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(localPath) || ComputeFileHash(localPath) != entry.Hash)
            {
                changedPackages.Add(entry.Package);
                changedCount++;
            }
        }

        foreach (var filePath in Directory.EnumerateFiles(appDir, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(appDir, filePath).Replace(Path.DirectorySeparatorChar, '/');
            if (relative.StartsWith("data/", StringComparison.OrdinalIgnoreCase)) continue;
            if (relative.StartsWith("appsettings", StringComparison.OrdinalIgnoreCase)) continue;
            if (!ManagedExtensions.Contains(Path.GetExtension(filePath))) continue;
            if (!_remoteManifest.Files.ContainsKey(relative))
                deletedCount++;
        }

        // Calculate download size
        long downloadSize = 0;
        foreach (var pkg in changedPackages)
        {
            var zipName = pkg == "app" ? $"app-{rid}.zip" : $"{pkg}.zip";
            if (_resolvedTag is not null)
            {
                // CDN/Atom path: use UpdateCheckInfo.Assets
                if (info.Assets.TryGetValue(zipName, out var size))
                    downloadSize += size;
            }
            else if (_releaseAssets is not null)
            {
                // API path: use release assets
                var asset = _releaseAssets.FirstOrDefault(a => a.Name == zipName);
                if (asset is not null) downloadSize += asset.Size;
            }
        }

        var changelog = info.Changelog;

        _lastCheckResult = new UpdateCheckResult
        {
            UpdateAvailable = changedCount > 0 || deletedCount > 0,
            NewVersion = remoteVersion,
            Changelog = changelog,
            DownloadSize = downloadSize,
            ChangedPackages = changedPackages.ToList(),
            ChangedFileCount = changedCount,
            DeletedFileCount = deletedCount
        };

        if (_lastCheckResult.UpdateAvailable)
        {
            _availableInfo = new UpdateAvailableInfo
            {
                Version = remoteVersion,
                Changelog = changelog,
                DownloadSize = downloadSize,
                ChangedPackages = changedPackages.ToList()
            };
            _status = new UpdateStatusInfo { State = UpdateState.Available, Message = $"新版本 {remoteVersion} 可用" };
            await BroadcastStatus();
            await hubContext.Clients.Group("update").SendAsync("UpdateAvailable", _availableInfo, ct);
            logger.LogInformation("发现新版本 {Version}，需要下载 {Packages}",
                remoteVersion, string.Join(", ", changedPackages));
            trayService.ShowNotification("XUnityToolkit", $"发现新版本 v{remoteVersion} 可用");
        }
        else
        {
            _status = new UpdateStatusInfo { State = UpdateState.None };
            await BroadcastStatus();
        }

        return _lastCheckResult;
    }
    catch (Exception ex) when (ex is not InvalidOperationException)
    {
        logger.LogError(ex, "检查更新失败");
        _status = new UpdateStatusInfo { State = UpdateState.Error, Error = "检查更新失败，请检查网络连接" };
        await BroadcastStatus();
        throw new InvalidOperationException("检查更新失败，请检查网络连接", ex);
    }
    finally
    {
        _lastCheckTime = DateTime.UtcNow; // always throttle (success, error, rate limit)
        _lock.Release();
    }
}
```

- [ ] **Step 7: Update `DownloadUpdateAsync` guard and URL resolution**

In `DownloadUpdateAsync` (line 338), change the guard condition:

```csharp
// BEFORE:
if (_lastCheckResult is not { UpdateAvailable: true } || _remoteManifest is null || _releaseAssets is null)

// AFTER:
if (_lastCheckResult is not { UpdateAvailable: true } || _remoteManifest is null
    || (_resolvedTag is null && _releaseAssets is null))
```

Then inside the `foreach (var pkg in ...)` loop (around line 373), replace the asset URL resolution:

```csharp
// BEFORE:
var asset = _releaseAssets.FirstOrDefault(a => a.Name == zipName);
if (asset is null)
{
    logger.LogWarning("找不到 Release Asset: {Name}", zipName);
    continue;
}
// ... later: await client.GetAsync(asset.BrowserDownloadUrl, ...)

// AFTER:
string downloadUrl;
if (_resolvedTag is not null)
{
    downloadUrl = $"https://github.com/{GitHubOwner}/{GitHubRepo}/releases/download/{_resolvedTag}/{zipName}";
}
else
{
    var asset = _releaseAssets?.FirstOrDefault(a => a.Name == zipName);
    if (asset is null)
    {
        logger.LogWarning("找不到 Release Asset: {Name}", zipName);
        continue;
    }
    downloadUrl = asset.BrowserDownloadUrl;
}
// ... use downloadUrl instead of asset.BrowserDownloadUrl
```

Replace `await client.GetAsync(asset.BrowserDownloadUrl, ...)` with `await client.GetAsync(downloadUrl, ...)`.

- [ ] **Step 8: Update `AutoCheckOnStartupAsync` to pass `bypassThrottle`**

In `AutoCheckOnStartupAsync` (line 682), change:

```csharp
// BEFORE:
await CheckForUpdateAsync();

// AFTER:
await CheckForUpdateAsync(ct: default, bypassThrottle: true);
```

- [ ] **Step 9: Verify build**

Run: `dotnet build XUnityToolkit-WebUI/XUnityToolkit-WebUI.csproj -p:SkipFrontendBuild=true`
Expected: Build succeeds with no errors.

- [ ] **Step 10: Commit**

```bash
git add XUnityToolkit-WebUI/Services/UpdateService.cs
git commit -m "feat(更新): 重构更新检查为三层策略（CDN → Atom Feed → API 回退）

实现 CDN 直链下载、Atom Feed 解析和 GitHub API 回退三层检查策略，
消除未认证 API 速率限制问题。
- 添加 TryCheckViaCdnAsync（稳定版 CDN 直链）
- 添加 TryCheckViaAtomFeedAsync（预发布版 Atom Feed）
- 提取 CheckViaGitHubApiAsync（保留现有 API 逻辑）
- 修复 ETag 缓存启动时不生效的 bug
- 添加 HTTP 429 处理
- 添加 5 分钟检查间隔限制
- 更新 DownloadUpdateAsync 支持 CDN URL 解析"
```

---

## Chunk 3: CI Changes

### Task 4: Add `Generate-UpdateCheckJson` to `build.ps1`

**Files:**
- Modify: `build.ps1`

- [ ] **Step 1: Add `Generate-UpdateCheckJson` function**

Add after the existing `Create-ComponentZips` function (after line 143 of `build.ps1`):

```powershell
function Generate-UpdateCheckJson {
    param([string]$ReleaseDir, [string]$Rid, [string]$Version, [string]$Tag = "")
    $outputDir = Split-Path $ReleaseDir
    $assets = [ordered]@{}
    @("manifest-$Rid.json", "app-$Rid.zip", "wwwroot.zip", "bundled.zip") | ForEach-Object {
        $path = Join-Path $outputDir $_
        if (Test-Path $path) { $assets[$_] = (Get-Item $path).Length }
    }
    $updateCheck = [ordered]@{
        tag = $(if ($Tag) { $Tag } else { "v$Version" })
        version = $Version
        changelog = ""
        prerelease = $false
        assets = $assets
    }
    $updateCheck | ConvertTo-Json -Depth 3 |
        Set-Content -Path (Join-Path $outputDir "update-check.json") -Encoding utf8
    Write-Host "  Generated: update-check.json" -ForegroundColor Green
}
```

- [ ] **Step 2: Add function call**

Find where `Create-ComponentZips` is called in the build script (search for `Create-ComponentZips -ReleaseDir`). Add immediately after that call:

```powershell
Generate-UpdateCheckJson -ReleaseDir $OutputDir -Rid $rid -Version $BuildVersion
```

Where `$OutputDir` and `$rid` and `$BuildVersion` match the existing variables at the call site.

- [ ] **Step 3: Commit**

```bash
git add build.ps1
git commit -m "ci(构建): 在 build.ps1 中生成 update-check.json"
```

### Task 5: Add `update-check.json` generation and upload to `build.yml`

**Files:**
- Modify: `.github/workflows/build.yml`

- [ ] **Step 1: Export `build_version` as job output and generate `update-check.json` in build job**

First, add `build_version` to the build job's `outputs` map (line 49-50 of `build.yml`):

```yaml
    outputs:
      version_prefix: ${{ steps.ver.outputs.version_prefix }}
      build_version: ${{ steps.ver.outputs.build_version }}
```

Then, after the manifest generation step (after line 507, `$manifest | ConvertTo-Json ...`), add:

```yaml
          # Generate update-check.json for CDN-based update checks
          $ucAssets = [ordered]@{}
          @("manifest-$rid.json", "app-$rid.zip", "wwwroot.zip", "bundled.zip") | ForEach-Object {
            $p = "Release/$_"
            if (Test-Path $p) { $ucAssets[$_] = (Get-Item $p).Length }
          }
          $updateCheck = [ordered]@{
            tag = ""
            version = "${{ steps.ver.outputs.build_version }}"
            changelog = ""
            prerelease = $false
            assets = $ucAssets
          }
          $updateCheck | ConvertTo-Json -Depth 3 | Set-Content -Path "Release/update-check.json" -Encoding utf8
          Write-Host "Generated update-check.json"
```

Then add `Release/update-check.json` to the `release-components` artifact upload paths (line 638-644):

```yaml
          path: |
            Release/manifest-win-x64.json
            Release/app-win-x64.zip
            Release/wwwroot.zip
            Release/bundled.zip
            Release/update-check.json
            Release/XUnityToolkit-WebUI-v${{ steps.ver.outputs.version_prefix }}-win-x64.msi
```

- [ ] **Step 2: Generate final `update-check.json` with changelog in release job**

After the "Upload component packages to release" step (line 728), add a new step:

```yaml
      - name: Generate and upload update-check.json
        env:
          GH_TOKEN: ${{ secrets.GITHUB_TOKEN }}
        shell: bash
        run: |
          VER="${{ needs.build.outputs.build_version }}"
          TAG="${{ inputs.release_tag }}"
          PRERELEASE="${{ inputs.prerelease }}"

          # Extract ### Changelog section from release notes
          CHANGELOG=""
          if [ -f release_notes.md ]; then
            CHANGELOG=$(awk '/^### Changelog/{found=1; next} /^## |^### /{if(found) exit} found{print}' release_notes.md | sed 's/^ *//' | sed '/^$/d')
          fi

          # Get component file sizes
          MANIFEST_SIZE=$(stat -c%s components/manifest-win-x64.json 2>/dev/null || echo 0)
          APP_SIZE=$(stat -c%s components/app-win-x64.zip 2>/dev/null || echo 0)
          WWWROOT_SIZE=$(stat -c%s components/wwwroot.zip 2>/dev/null || echo 0)
          BUNDLED_SIZE=$(stat -c%s components/bundled.zip 2>/dev/null || echo 0)

          # Build JSON (jq for proper escaping)
          jq -n \
            --arg tag "$TAG" \
            --arg version "$VER" \
            --arg changelog "$CHANGELOG" \
            --argjson prerelease "${PRERELEASE:-false}" \
            --argjson manifest_size "$MANIFEST_SIZE" \
            --argjson app_size "$APP_SIZE" \
            --argjson wwwroot_size "$WWWROOT_SIZE" \
            --argjson bundled_size "$BUNDLED_SIZE" \
            '{
              tag: $tag,
              version: $version,
              changelog: $changelog,
              prerelease: $prerelease,
              assets: {
                "manifest-win-x64.json": $manifest_size,
                "app-win-x64.zip": $app_size,
                "wwwroot.zip": $wwwroot_size,
                "bundled.zip": $bundled_size
              }
            }' > update-check.json

          gh release upload "${{ inputs.release_tag }}" update-check.json \
            --clobber --repo "${{ github.repository }}"
          echo "Uploaded update-check.json"
```

This step runs in the `release` job (ubuntu-latest) where `release_notes.md` and component artifacts are available. It uses `jq` for proper JSON escaping of the changelog string.

- [ ] **Step 3: Commit**

```bash
git add .github/workflows/build.yml
git commit -m "ci(发布): 在构建和发布流程中生成并上传 update-check.json"
```

---

## Chunk 4: Documentation Updates

### Task 6: Update CLAUDE.md files

**Files:**
- Modify: `XUnityToolkit-WebUI/CLAUDE.md`
- Modify: `CLAUDE.md`

- [ ] **Step 1: Add `GitHubCdn` to Named HttpClient list**

In `XUnityToolkit-WebUI/CLAUDE.md`, find the line with Named HttpClient (line 14):

```
- Named `HttpClient`: `"LLM"` (120s/200conn), `"SteamGridDB"` (30s), `"LocalLlmDownload"` (12h), `"WebImageSearch"` (15s, browser UA), `"GitHubUpdate"` (60s)
```

Add `"GitHubCdn"` to the list:

```
- Named `HttpClient`: `"LLM"` (120s/200conn), `"SteamGridDB"` (30s), `"LocalLlmDownload"` (12h), `"WebImageSearch"` (15s, browser UA), `"GitHubUpdate"` (60s), `"GitHubCdn"` (30s, CDN/web requests)
```

- [ ] **Step 2: Update Online Update section in root CLAUDE.md**

In root `CLAUDE.md`, find the Online Update line in the Architecture section:

```
- **Online Update:** `UpdateService` checks GitHub Releases for new versions; manifest-based differential download ...
```

Add mention of the three-layer strategy and `update-check.json`:

```
- **Online Update:** `UpdateService` three-layer check strategy: CDN (`update-check.json` asset) → Atom Feed (`/releases.atom`) → GitHub API fallback; CDN/Atom paths use `GitHubCdn` client (zero API calls); `update-check.json` generated by CI per release; manifest-based differential download (app/wwwroot/bundled component ZIPs); `Updater.exe` (AOT, no runtime dependency) handles file replacement and restart; staging at `data/update-staging/`; two-phase backup-then-replace for atomicity; rollback on failure; prerelease opt-in via `AppSettings.ReceivePreReleaseUpdates`
```

- [ ] **Step 3: Update Build & Deploy section**

In root `CLAUDE.md`, find the "Update manifest" line in Build & Deploy:

```
- **Update manifest:** `manifest-{rid}.json` generated per release with SHA256 hashes; component ZIPs: `app-{rid}.zip`, `wwwroot.zip`, `bundled.zip`
```

Add `update-check.json`:

```
- **Update manifest:** `manifest-{rid}.json` generated per release with SHA256 hashes; component ZIPs: `app-{rid}.zip`, `wwwroot.zip`, `bundled.zip`; `update-check.json` generated per release (tag, version, changelog, prerelease, asset sizes) for CDN-based update checks
```

- [ ] **Step 4: Commit**

```bash
git add XUnityToolkit-WebUI/CLAUDE.md CLAUDE.md
git commit -m "docs: 更新 CLAUDE.md 中的更新检查相关文档"
```
