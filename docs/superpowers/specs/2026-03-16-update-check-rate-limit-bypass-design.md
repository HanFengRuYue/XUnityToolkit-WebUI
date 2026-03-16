# Update Check Rate Limit Bypass — Design Spec

## Problem

The `UpdateService` checks GitHub Releases API for updates. Unauthenticated requests are limited to 60/hour per IP. Users frequently hit this limit, receiving "GitHub API 请求频率超限" errors. The existing ETag/`If-None-Match` conditional request optimization does **not** save rate limit quota for unauthenticated requests (GitHub only exempts authenticated 304 responses).

## Solution

A three-layer update check strategy that eliminates GitHub API calls entirely under normal conditions, falling back to the existing API path only when CDN/Atom Feed approaches fail.

### Layer 1: CDN Direct Download (Stable Releases)

Download `update-check.json` from `https://github.com/{owner}/{repo}/releases/latest/download/update-check.json`. This URL serves assets from the latest non-prerelease via `objects.githubusercontent.com` CDN, which has separate (much more generous) rate limits from `api.github.com`.

### Layer 2: Atom Feed + CDN (Pre-release Releases)

1. Fetch `https://github.com/{owner}/{repo}/releases.atom` (web server, not API)
2. Parse first `<entry>` to extract the latest release tag
3. Download `update-check.json` from `https://github.com/{owner}/{repo}/releases/download/{tag}/update-check.json`

### Layer 3: GitHub API Fallback

If CDN/Atom Feed fails (404 for old releases without `update-check.json`, network errors, etc.), fall back to the existing GitHub API logic. This preserves full backward compatibility.

## `update-check.json` Format

CI generates this file for every release and uploads it as a Release asset:

```json
{
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
- `version`: Version string without `v` prefix
- `changelog`: Extracted `### Changelog` section from Release body (empty string for local builds)
- `prerelease`: Whether this is a pre-release
- `assets`: Map of asset filename → size in bytes; used to calculate download size before fetching the full manifest

## Check Flow

```
CheckForUpdateAsync()
│
├─ Rate limit guard: if last check < 5 minutes ago, return cached result
│  (startup auto-check bypasses this guard)
│
├─ if (ReceivePreReleaseUpdates)
│    result = TryCheckViaAtomFeedAsync()
│  else
│    result = TryCheckViaCdnAsync()
│
├─ if (result == null)  // CDN/Atom failed
│    result = CheckViaGitHubApiAsync()  // existing logic
│
├─ Compare version, check dismissed
│
├─ Download manifest via CDN or API BrowserDownloadUrl
│  - Stable CDN: /releases/latest/download/manifest-{rid}.json
│  - Pre-release CDN: /releases/download/{tag}/manifest-{rid}.json
│  - API fallback: asset.BrowserDownloadUrl (existing)
│
├─ Compute file diff (existing logic, unchanged)
│
└─ Broadcast result via SignalR (existing logic, unchanged)
```

## New Private Methods in UpdateService

### `TryCheckViaCdnAsync(CancellationToken ct) → UpdateCheckInfo?`

1. Construct URL: `https://github.com/{GitHubOwner}/{GitHubRepo}/releases/latest/download/update-check.json`
2. GET with `GitHubCdn` HttpClient
3. If 200: deserialize and return `UpdateCheckInfo`
4. If 404 or any error: log, return `null`

### `TryCheckViaAtomFeedAsync(CancellationToken ct) → (UpdateCheckInfo?, string? tag)`

1. GET `https://github.com/{GitHubOwner}/{GitHubRepo}/releases.atom` with `GitHubCdn` HttpClient
2. Parse XML, extract first `<entry>`'s `<link href="..."/>` → parse tag from URL path
3. Construct URL: `https://github.com/{GitHubOwner}/{GitHubRepo}/releases/download/{tag}/update-check.json`
4. GET with `GitHubCdn` HttpClient
5. If 200: deserialize and return `(UpdateCheckInfo, tag)`
6. If any error: log, return `(null, null)`

### `CheckViaGitHubApiAsync(CancellationToken ct) → (UpdateCheckInfo?, GitHubRelease?, string? tag)`

Extracted from existing `CheckForUpdateAsync` logic (lines 124-239). Returns parsed release info in `UpdateCheckInfo` format plus the original `GitHubRelease` for asset URL access.

## New Model

In `UpdateInfo.cs`:

```csharp
public sealed class UpdateCheckInfo
{
    public required string Version { get; set; }
    public string? Changelog { get; set; }
    public bool Prerelease { get; set; }
    public Dictionary<string, long> Assets { get; set; } = [];
}
```

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

On startup, load `ApiUrl` from disk cache to initialize `_lastCheckedApiUrl`, fixing the bug where the first check after startup never sends `If-None-Match`.

## Check Interval Throttle

New field in `UpdateService`:

```csharp
private DateTime _lastCheckTime = DateTime.MinValue;
```

In `CheckForUpdateAsync`, before any network request:

```csharp
if (!bypassThrottle && DateTime.UtcNow - _lastCheckTime < TimeSpan.FromMinutes(5))
{
    return _lastCheckResult ?? new UpdateCheckResult();
}
```

`AutoCheckOnStartupAsync` passes `bypassThrottle: true` (via internal parameter or separate field).

## HTTP 429 Handling

In the API fallback path, handle both 403 and 429:

```csharp
if ((int)response.StatusCode is 403 or 429)
{
    var retryAfter = response.Headers.RetryAfter?.Delta?.TotalSeconds
        ?? response.Headers.RetryAfter?.Date?.Subtract(DateTimeOffset.UtcNow).TotalSeconds;
    var waitMsg = retryAfter > 0 ? $"，请 {(int)retryAfter} 秒后重试" : "，请稍后再试";
    // ...
}
```

## CI Changes

### `build.ps1`

After `Generate-Manifest`, add `Generate-UpdateCheckJson` function:

```powershell
function Generate-UpdateCheckJson {
    param([string]$ReleaseDir, [string]$Rid, [string]$Version)
    $outputDir = Split-Path $ReleaseDir
    $assets = @{}
    @("manifest-$Rid.json", "app-$Rid.zip", "wwwroot.zip", "bundled.zip") | ForEach-Object {
        $path = Join-Path $outputDir $_
        if (Test-Path $path) { $assets[$_] = (Get-Item $path).Length }
    }
    $updateCheck = @{
        version = $Version
        changelog = ""
        prerelease = $false
        assets = $assets
    }
    $updateCheck | ConvertTo-Json -Depth 3 |
        Set-Content -Path (Join-Path $outputDir "update-check.json") -Encoding utf8
}
```

### `build.yml`

1. After manifest generation step, add `update-check.json` generation with proper `changelog` extraction from release notes and `prerelease` from `inputs.prerelease`
2. Add `update-check.json` to the `gh release upload` command alongside other component files
3. Add `update-check.json` to the upload-artifact paths

## Backward Compatibility

| Scenario | Behavior |
|---|---|
| New client + new release (has `update-check.json`) | CDN path, zero API calls |
| New client + old release (no `update-check.json`) | CDN returns 404, falls back to API |
| Old client + new release | Ignores `update-check.json`, uses API as before |
| Old client + old release | No change |

## Files Modified

1. `XUnityToolkit-WebUI/Services/UpdateService.cs` — refactor check flow, add CDN/Atom methods
2. `XUnityToolkit-WebUI/Models/UpdateInfo.cs` — add `UpdateCheckInfo`, extend `UpdateCheckCache`
3. `XUnityToolkit-WebUI/Program.cs` — register `GitHubCdn` HttpClient
4. `build.ps1` — add `Generate-UpdateCheckJson` function
5. `.github/workflows/build.yml` — generate and upload `update-check.json`

## Not Changed

- `DownloadUpdateAsync` — ZIP downloads already use `BrowserDownloadUrl` (CDN links)
- `ApplyUpdateAsync` — unrelated to check flow
- `UpdateEndpoints.cs` — API surface unchanged
- Frontend — no changes needed (same `UpdateCheckResult` shape)
