# Update Check Rate Limit Bypass — Design Spec

## Problem

The `UpdateService` checks GitHub Releases API for updates. Unauthenticated requests are limited to 60/hour per IP. Users frequently hit this limit, receiving "GitHub API 请求频率超限" errors. The existing ETag/`If-None-Match` conditional request optimization does **not** save rate limit quota for unauthenticated requests (GitHub only exempts authenticated 304 responses).

## Solution

A three-layer update check strategy that eliminates GitHub API calls entirely under normal conditions, falling back to the existing API path only when CDN/Atom Feed approaches fail.

### Layer 1: CDN Direct Download (Stable Releases)

Download `update-check.json` from `https://github.com/{owner}/{repo}/releases/latest/download/update-check.json`. This URL serves assets from the latest non-prerelease via `objects.githubusercontent.com` CDN, which has separate (much more generous) rate limits from `api.github.com`.

### Layer 2: Atom Feed + CDN (Pre-release Releases)

1. Fetch `https://github.com/{owner}/{repo}/releases.atom` (web server, not API)
2. Parse first `<entry>` to extract the latest release tag (regardless of pre-release status — users who opt into pre-releases want the latest release of any kind)
3. Download `update-check.json` from `https://github.com/{owner}/{repo}/releases/download/{tag}/update-check.json`

### Layer 3: GitHub API Fallback

If CDN/Atom Feed fails (404 for old releases without `update-check.json`, network errors, etc.), fall back to the existing GitHub API logic. This preserves full backward compatibility.

## `update-check.json` Format

CI generates this file for every release and uploads it as a Release asset:

```json
{
  "tag": "v2.2.202603151430",
  "version": "2.2.202603151430",
  "changelog": "- Fixed xxx\n- Added yyy",
  "prerelease": false,
  "assets": {
    "manifest-win-x64.json": 12345,
    "app-win-x64.zip": 6789000,
    "wwwroot.zip": 1234000,
    "bundled.zip": 45678000
  }
}
```

Fields:
- `tag`: Raw git tag name (e.g., `v2.2.xxx` for manual releases, `auto-2.2.xxx` for dep-check auto-releases). Required for constructing CDN download URLs in the download phase.
- `version`: Version string without `v`/`auto-` prefix
- `changelog`: Extracted `### Changelog` section from Release body (empty string for local builds)
- `prerelease`: Whether this is a pre-release
- `assets`: Map of asset filename → size in bytes; used to calculate download size before fetching the full manifest

## Internal State for Download Phase

New field `_resolvedTag` in `UpdateService` stores the tag resolved during check:
- CDN stable path: read from `update-check.json`'s `tag` field
- Atom Feed path: parsed from `<link>` URL, also cross-checked with `update-check.json`'s `tag`
- API fallback path: from `GitHubRelease.TagName`

`DownloadUpdateAsync` uses `_resolvedTag` to construct CDN download URLs:
- `https://github.com/{owner}/{repo}/releases/download/{_resolvedTag}/{zipName}`

When `_resolvedTag` is null (should not happen, defensive), falls back to existing `_releaseAssets` + `BrowserDownloadUrl` logic.

The existing `_releaseAssets` field is populated only by the API fallback path. CDN/Atom paths set it to `null` — `DownloadUpdateAsync` checks `_resolvedTag` first, then `_releaseAssets` as fallback.

## Check Flow

```
CheckForUpdateAsync(bypassThrottle: false)
│
├─ Throttle guard: if !bypassThrottle && last check < 5 min ago
│    → return _lastCheckResult (no network request)
│
├─ if (ReceivePreReleaseUpdates)
│    (info, tag) = TryCheckViaAtomFeedAsync()
│  else
│    (info, tag) = TryCheckViaCdnAsync()
│
├─ if (info == null)  // CDN/Atom failed
│    (info, tag) = CheckViaGitHubApiAsync(isPreRelease, ct)
│
├─ _lastCheckTime = DateTime.UtcNow  // update AFTER attempt (success or fail)
│
├─ Compare version, check dismissed
│
├─ Reset: _resolvedTag = null, _releaseAssets = null
│
├─ Download manifest (using GitHubCdn client for CDN, GitHubUpdate for API):
│  - if tag != null: CDN /releases/download/{tag}/manifest-{rid}.json
│  - else: asset.BrowserDownloadUrl (API fallback, existing)
│
├─ Store _resolvedTag = tag (for DownloadUpdateAsync)
├─ Store _releaseAssets (only from API path, null from CDN paths)
│
├─ Calculate download size:
│  - CDN/Atom path: from UpdateCheckInfo.Assets[zipName]
│  - API path: from release.Assets.FirstOrDefault(a => a.Name == zipName)?.Size
│
├─ Compute file diff (existing logic, unchanged)
│
└─ Broadcast result via SignalR (existing logic, unchanged)
```

## New Private Methods in UpdateService

All three methods return the same tuple type `(UpdateCheckInfo? info, string? tag)` for uniform handling by the caller.

### `TryCheckViaCdnAsync(CancellationToken ct) → (UpdateCheckInfo?, string?)`

1. Construct URL: `https://github.com/{GitHubOwner}/{GitHubRepo}/releases/latest/download/update-check.json`
2. GET with `GitHubCdn` HttpClient
3. If 200: deserialize `UpdateCheckInfo`, return `(info, info.Tag)`
4. If 404 or any error: log at Debug level, return `(null, null)`

### `TryCheckViaAtomFeedAsync(CancellationToken ct) → (UpdateCheckInfo?, string?)`

1. GET `https://github.com/{GitHubOwner}/{GitHubRepo}/releases.atom` with `GitHubCdn` HttpClient
2. Parse XML with Atom namespace (`http://www.w3.org/2005/Atom`): `XNamespace atom = "http://www.w3.org/2005/Atom"`
3. Extract first `<entry>`'s `<link rel="alternate" href="..."/>` → split URL by `/`, take last segment as tag
4. Construct URL: `https://github.com/{GitHubOwner}/{GitHubRepo}/releases/download/{tag}/update-check.json`
5. GET with `GitHubCdn` HttpClient
6. If 200: deserialize `UpdateCheckInfo`, return `(info, tag)`
7. If any error: log at Debug level, return `(null, null)`

**Note:** Tag names with `/` are theoretically valid in git but not used by this project. This parsing assumption is safe.

### `CheckViaGitHubApiAsync(bool isPreRelease, CancellationToken ct) → (UpdateCheckInfo?, string?)`

Extracted from existing `CheckForUpdateAsync` logic (lines 124-239). Converts the `GitHubRelease` response into `UpdateCheckInfo` format. Also populates `_releaseAssets` for download phase compatibility. Returns `(info, release.TagName)`.

**Version extraction:** The tag may be `v2.2.xxx` (manual release) or `auto-2.2.xxx` (dep-check auto-release). Version is extracted by stripping known prefixes:

```csharp
var version = release.TagName;
if (version.StartsWith("auto-")) version = version["auto-".Length..];
else version = version.TrimStart('v');
```

ETag/`If-None-Match` logic is retained in this method only (not used for CDN requests). The 403/429 handling is also only in this method.

## New Model

In `UpdateInfo.cs`:

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

All properties use explicit `[JsonPropertyName]` attributes for safety, consistent with `GitHubRelease`/`GitHubAsset` convention in this codebase.

## New HttpClient Registration

In `Program.cs`:

```csharp
builder.Services.AddHttpClient("GitHubCdn", client =>
{
    client.Timeout = TimeSpan.FromSeconds(30);
    client.DefaultRequestHeaders.Add("User-Agent", "XUnityToolkit-WebUI");
});
```

No `Accept: application/vnd.github+json` header — these are web/CDN requests, not API requests.

## UpdateCheckCache Changes

Add `ApiUrl` field to `UpdateCheckCache`:

```csharp
public sealed class UpdateCheckCache
{
    public string? ETag { get; set; }
    public string? LatestVersion { get; set; }
    public DateTime LastChecked { get; set; }
    public string? ApiUrl { get; set; }  // NEW
}
```

### ETag Cache Initialization Fix

In `CheckViaGitHubApiAsync`, before constructing the HTTP request, load the cached `ApiUrl` to initialize `_lastCheckedApiUrl` if it is still null:

```csharp
// Load cached ApiUrl on first API call (fixes startup ETag bug)
if (_lastCheckedApiUrl is null && cache?.ApiUrl is not null)
    _lastCheckedApiUrl = cache.ApiUrl;
```

When saving the cache, also persist the current `apiUrl`:

```csharp
var newCache = new UpdateCheckCache
{
    ETag = response.Headers.ETag?.Tag,
    LatestVersion = release.TagName.TrimStart('v'),
    LastChecked = DateTime.UtcNow,
    ApiUrl = apiUrl  // NEW
};
```

ETag logic is only relevant for the API fallback path. CDN requests do not use ETag caching.

## Check Interval Throttle

New field in `UpdateService`:

```csharp
private DateTime _lastCheckTime = DateTime.MinValue;
```

In `CheckForUpdateAsync`, **before the semaphore**, before any network request:

```csharp
if (!bypassThrottle && DateTime.UtcNow - _lastCheckTime < TimeSpan.FromMinutes(5))
{
    return _lastCheckResult ?? new UpdateCheckResult();
}
```

`_lastCheckTime` is updated **after** the check attempt completes (both success and failure), inside the `try` block after all network calls finish. This means a failed check also triggers the 5-minute cooldown, preventing hammering on errors.

`CheckForUpdateAsync` signature gains an internal parameter: `bool bypassThrottle = false`. `AutoCheckOnStartupAsync` calls `CheckForUpdateAsync(bypassThrottle: true)`. The public endpoint continues to call `CheckForUpdateAsync(ct: ct)` without bypass.

## HTTP 429 Handling

In the API fallback path (`CheckViaGitHubApiAsync`), handle both 403 and 429:

```csharp
if ((int)response.StatusCode is 403 or 429)
{
    var retryAfter = response.Headers.RetryAfter?.Delta?.TotalSeconds
        ?? response.Headers.RetryAfter?.Date?.Subtract(DateTimeOffset.UtcNow).TotalSeconds;
    var waitMsg = retryAfter > 0 ? $"，请 {(int)retryAfter} 秒后重试" : "，请稍后再试";
    logger.LogWarning("GitHub API 速率限制 ({StatusCode})", (int)response.StatusCode);
    throw new InvalidOperationException($"GitHub API 请求频率超限{waitMsg}");
}
```

## CI Changes

### `build.ps1`

After `Generate-Manifest` and `Create-ComponentZips`, add `Generate-UpdateCheckJson` function:

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
        tag = if ($Tag) { $Tag } else { "v$Version" }
        version = $Version
        changelog = ""
        prerelease = $false
        assets = $assets
    }
    $updateCheck | ConvertTo-Json -Depth 3 |
        Set-Content -Path (Join-Path $outputDir "update-check.json") -Encoding utf8
}
```

Uses `[ordered]@{}` for deterministic key ordering in JSON output.

Call site in `build.ps1` (after `Create-ComponentZips`):

```powershell
Generate-UpdateCheckJson -ReleaseDir $OutputDir -Rid $rid -Version $BuildVersion
```

### `build.yml`

**Key constraint:** Release notes (changelog) are constructed in the `release` job, not the `build` job. The `update-check.json` must be generated in the `release` job after the notes file is created, then uploaded alongside other component files.

Implementation:

1. In the `release` job, after creating the release and uploading component packages, add a step to generate `update-check.json`:
   - Read `release_notes.md` and extract `### Changelog` section via PowerShell string processing
   - Read component file sizes from the downloaded artifacts
   - Construct JSON with `tag` from `inputs.release_tag`, `version` from `needs.build.outputs.version_prefix`, `prerelease` from `inputs.prerelease`
   - Write `update-check.json`

2. Upload `update-check.json` to the release via `gh release upload` (can be a separate step or appended to the existing upload command with `--clobber`)

3. Add `update-check.json` to the artifact upload paths in the `build` job for local build artifact preservation (with empty changelog and `tag` defaulting to `v{version}`)

## DownloadUpdateAsync Changes

### Guard Condition Update

The existing guard rejects when `_releaseAssets is null`, which is always true for CDN/Atom paths. Must update:

```csharp
// BEFORE (rejects CDN paths):
if (_lastCheckResult is not { UpdateAvailable: true } || _remoteManifest is null || _releaseAssets is null)

// AFTER (accepts CDN paths when _resolvedTag is set):
if (_lastCheckResult is not { UpdateAvailable: true } || _remoteManifest is null
    || (_resolvedTag is null && _releaseAssets is null))
```

### URL Resolution

```csharp
// Resolve download URL for each component ZIP
string GetDownloadUrl(string zipName)
{
    // Prefer CDN direct URL via resolved tag
    if (_resolvedTag is not null)
        return $"https://github.com/{GitHubOwner}/{GitHubRepo}/releases/download/{_resolvedTag}/{zipName}";

    // Fallback: use BrowserDownloadUrl from API response
    var asset = _releaseAssets?.FirstOrDefault(a => a.Name == zipName);
    return asset?.BrowserDownloadUrl
        ?? throw new InvalidOperationException($"找不到下载链接: {zipName}");
}
```

### HttpClient for Downloads

`DownloadUpdateAsync` continues using the `"GitHubUpdate"` client (60s timeout) for all ZIP downloads, regardless of whether the URL was constructed from `_resolvedTag` or `_releaseAssets`. The 30s timeout on `"GitHubCdn"` is too short for large ZIPs (bundled.zip ~45MB). GitHub CDN URLs work fine with the `Accept: application/vnd.github+json` header (CDN ignores it).

## Backward Compatibility

| Scenario | Behavior |
|---|---|
| New client + new release (has `update-check.json`) | CDN path, zero API calls |
| New client + old release (no `update-check.json`) | CDN returns 404, falls back to API |
| Old client + new release | Ignores `update-check.json`, uses API as before |
| Old client + old release | No change |

## Files Modified

1. `XUnityToolkit-WebUI/Services/UpdateService.cs` — refactor check flow, add CDN/Atom/API methods, update download URL resolution
2. `XUnityToolkit-WebUI/Models/UpdateInfo.cs` — add `UpdateCheckInfo`, extend `UpdateCheckCache`
3. `XUnityToolkit-WebUI/Program.cs` — register `GitHubCdn` HttpClient
4. `build.ps1` — add `Generate-UpdateCheckJson` function
5. `.github/workflows/build.yml` — generate and upload `update-check.json` in release job

## CLAUDE.md Updates

- Add `"GitHubCdn"` to the Named HttpClient list in `XUnityToolkit-WebUI/CLAUDE.md` (with 30s timeout)
- Add note about `update-check.json` as Release asset in root `CLAUDE.md` Architecture → Online Update section

## Not Changed

- `ApplyUpdateAsync` — unrelated to check flow
- `UpdateEndpoints.cs` — API surface unchanged
- Frontend — no changes needed (same `UpdateCheckResult` shape returned)
