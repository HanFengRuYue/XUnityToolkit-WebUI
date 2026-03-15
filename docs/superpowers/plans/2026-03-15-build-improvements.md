# Build Improvements Implementation Plan

> **For agentic workers:** REQUIRED: Use superpowers:subagent-driven-development (if subagents available) or superpowers:executing-plans to implement this plan. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Remove ARM64 support, pin llama.cpp to B8354, add network timeout/retry to CI, and align local/CI build scripts.

**Architecture:** Four independent changes applied to both `build.ps1` (local) and `.github/workflows/build.yml` (CI), plus minor edits to `release.yml`, `dep-check.yml`, `UpdateService.cs`, and `LocalLlmService.cs`.

**Tech Stack:** PowerShell, GitHub Actions YAML, C#

---

## Chunk 1: Remove ARM64 Support

### Task 1: Remove ARM64 from build.ps1

**Files:**
- Modify: `build.ps1:2,5,165,185,519,551,581,612`

- [ ] **Step 1: Update parameter and header**

Replace lines 1-8:
```powershell
# build.ps1 - XUnityToolkit-WebUI 一键构建脚本
# 用法: .\build.ps1 [-SkipDownload]

param(
    [switch]$SkipDownload
)
```

- [ ] **Step 2: Remove $Runtimes array, use single RID**

Replace line 165:
```powershell
$Runtimes = if ($Runtime -eq 'all') { @('win-x64', 'win-arm64') } else { @($Runtime) }
```
with:
```powershell
$rid = 'win-x64'
```

- [ ] **Step 3: Update step count**

Replace line 185:
```powershell
$stepCount = 3 + (2 * $Runtimes.Count) + $(if ($hasEndpoint) { 1 } else { 0 }) + $(if (-not $SkipDownload) { 1 } else { 0 })
```
with:
```powershell
$stepCount = 5 + $(if ($hasEndpoint) { 1 } else { 0 }) + $(if (-not $SkipDownload) { 1 } else { 0 })
```

- [ ] **Step 4: Update target display**

Replace line 190:
```powershell
Write-Host "    Target: $($Runtimes -join ', ')" -ForegroundColor DarkGray
```
with:
```powershell
Write-Host "    Target: $rid" -ForegroundColor DarkGray
```

- [ ] **Step 5: Replace foreach loop with single-pass**

Replace `foreach ($rid in $Runtimes) {` (line 519) with just a comment:
```powershell
# ── Publish win-x64 ──
```
Remove the corresponding closing `}` (after line 605, before the Summary section).

The body stays the same — it already uses `$rid` which we set in Step 2.

- [ ] **Step 6: Remove Updater ARM64 comment**

Replace line 551:
```powershell
    # Copy Updater.exe (always use win-x64 build — ARM64 AOT requires C++ ARM64 build tools)
```
with:
```powershell
    # Copy Updater.exe
```

- [ ] **Step 7: Simplify WiX platform**

Replace line 581:
```powershell
        $wixPlatform = if ($rid -eq 'win-arm64') { 'arm64' } else { 'x64' }
```
with:
```powershell
        $wixPlatform = 'x64'
```

- [ ] **Step 8: Replace summary foreach**

Replace lines 612-625:
```powershell
foreach ($rid in $Runtimes) {
    $dir = Join-Path $ReleaseRoot $rid
    Write-Host "$rid :" -ForegroundColor Yellow
    ...
    Write-Host ""
}
```
with (same body but no loop, using `$rid` set earlier):
```powershell
$dir = Join-Path $ReleaseRoot $rid
Write-Host "$rid :" -ForegroundColor Yellow
Get-ChildItem $dir | ForEach-Object {
    if ($_.PSIsContainer) {
        $folderSize = [math]::Round(((Get-ChildItem $_.FullName -File -Recurse | Measure-Object -Property Length -Sum).Sum / 1MB), 1)
        $size = "<DIR> $folderSize MB"
    } else {
        $size = "$([math]::Round($_.Length / 1MB, 1)) MB"
    }
    Write-Host "  $($_.Name.PadRight(35)) $size"
}
Write-Host ""
```

- [ ] **Step 9: Commit**

```bash
git add build.ps1
git commit -m "feat: 从 build.ps1 移除 ARM64 支持"
```

### Task 2: Remove ARM64 from build.yml

**Files:**
- Modify: `.github/workflows/build.yml`

The matrix `rid: [win-x64, win-arm64]` must be removed. All `${{ matrix.rid }}` references become literal `win-x64`. The `strategy:` block is removed entirely.

- [ ] **Step 1: Remove matrix strategy**

Delete lines 42-45:
```yaml
    strategy:
      fail-fast: false
      matrix:
        rid: [win-x64, win-arm64]
```

- [ ] **Step 2: Replace all `${{ matrix.rid }}` with `win-x64`**

Global find-replace in the file: `${{ matrix.rid }}` → `win-x64`

- [ ] **Step 3: Remove Updater conditional**

Line 309: remove `if: matrix.rid == 'win-x64'` from the "Build Updater" step (it always runs now).

- [ ] **Step 4: Remove arch-independent upload condition**

In "Upload component packages" step, remove the `if [ "${{ matrix.rid }}" = "win-x64" ]` conditional around wwwroot.zip/bundled.zip upload — always upload them:
```bash
gh release upload "${{ inputs.release_tag }}" \
  "Release/manifest-win-x64.json" \
  "Release/app-win-x64.zip" \
  "Release/wwwroot.zip" \
  "Release/bundled.zip" \
  --clobber
```

- [ ] **Step 5: Fix release job — only upload win-x64.zip**

In the `release` job's "Create GitHub Release" step, change:
```bash
gh release create "${{ inputs.release_tag }}" \
  XUnityToolkit-WebUI-win-x64.zip \
  XUnityToolkit-WebUI-win-arm64.zip \
```
to:
```bash
gh release create "${{ inputs.release_tag }}" \
  XUnityToolkit-WebUI-win-x64.zip \
```

- [ ] **Step 6: Remove WiX ARM64 platform mapping**

Replace:
```powershell
$wixPlatform = if ($rid -eq 'win-arm64') { 'arm64' } else { 'x64' }
```
with:
```powershell
$wixPlatform = 'x64'
```

- [ ] **Step 7: Simplify artifact/step names**

Remove ` (${{ matrix.rid }})` from step names since there's only one RID now. E.g.:
- `Build MSI (${{ matrix.rid }})` → `Build MSI`
- `Upload MSI (${{ matrix.rid }})` → `Upload MSI`
- `Publish ${{ matrix.rid }}` → `Publish win-x64`

Artifact name: `release-${{ matrix.rid }}` → `release-win-x64`
Download pattern: `release-*` → `release-win-x64`

- [ ] **Step 8: Commit**

```bash
git add .github/workflows/build.yml
git commit -m "feat: 从 CI 工作流移除 ARM64 构建矩阵"
```

### Task 3: Remove ARM64 from release.yml and dep-check.yml

**Files:**
- Modify: `.github/workflows/release.yml:22-23`
- Modify: `.github/workflows/dep-check.yml:129-130`

- [ ] **Step 1: Update release.yml download table**

Replace the release_body table:
```yaml
      release_body: |
        ## XUnityToolkit-WebUI ${{ github.ref_name }}

        ### Downloads
        | File | Platform |
        |------|----------|
        | `XUnityToolkit-WebUI-win-x64.zip` | Windows x64 |

        ### Installation
        Download the ZIP, extract, and run `XUnityToolkit-WebUI.exe`.
```

- [ ] **Step 2: Update dep-check.yml release notes table**

Replace lines 127-130:
```bash
              echo "| File | Platform |"
              echo "|------|----------|"
              echo "| \`XUnityToolkit-WebUI-win-x64.zip\` | Windows x64 |"
```
(Remove the `win-arm64` row)

- [ ] **Step 3: Commit**

```bash
git add .github/workflows/release.yml .github/workflows/dep-check.yml
git commit -m "feat: 从 release 和 dep-check 工作流移除 ARM64 引用"
```

### Task 4: Remove ARM64 from UpdateService.cs

**Files:**
- Modify: `XUnityToolkit-WebUI/Services/UpdateService.cs:54`

- [ ] **Step 1: Remove Arm64 branch**

Replace:
```csharp
        return arch switch
        {
            System.Runtime.InteropServices.Architecture.X64 => "win-x64",
            System.Runtime.InteropServices.Architecture.Arm64 => "win-arm64",
            _ => throw new PlatformNotSupportedException($"Unsupported architecture: {arch}")
        };
```
with:
```csharp
        return arch switch
        {
            System.Runtime.InteropServices.Architecture.X64 => "win-x64",
            _ => throw new PlatformNotSupportedException($"Unsupported architecture: {arch}")
        };
```

- [ ] **Step 2: Commit**

```bash
git add XUnityToolkit-WebUI/Services/UpdateService.cs
git commit -m "feat: 从 UpdateService 移除 ARM64 RID 映射"
```

## Chunk 2: Pin llama.cpp to B8354

### Task 5: Pin llama.cpp in build.ps1

**Files:**
- Modify: `build.ps1:390-396`

- [ ] **Step 1: Replace dynamic release query with fixed tag**

Replace lines 390-396:
```powershell
    Write-Host "  Fetching llama.cpp latest release..." -ForegroundColor DarkGray
    $llamaReleases = Invoke-WithRetry -Operation "Fetch llama.cpp releases" -ScriptBlock {
        Invoke-RestMethod -Uri "https://api.github.com/repos/ggml-org/llama.cpp/releases?per_page=10" -Headers $GitHubHeaders -TimeoutSec 30
    }
    $llamaRelease = $llamaReleases | Where-Object { -not $_.prerelease } | Select-Object -First 1
    if (-not $llamaRelease) { throw "llama.cpp release not found" }
    Write-Host "  llama.cpp: $($llamaRelease.tag_name)" -ForegroundColor DarkGray
```
with:
```powershell
    # llama.cpp pinned to b8354 — update tag to change version
    $llamaTag = "b8354"
    Write-Host "  Fetching llama.cpp $llamaTag..." -ForegroundColor DarkGray
    $llamaRelease = Invoke-WithRetry -Operation "Fetch llama.cpp $llamaTag" -ScriptBlock {
        Invoke-RestMethod -Uri "https://api.github.com/repos/ggml-org/llama.cpp/releases/tags/$llamaTag" -Headers $GitHubHeaders -TimeoutSec 30
    }.GetNewClosure()
    Write-Host "  llama.cpp: $($llamaRelease.tag_name)" -ForegroundColor DarkGray
```

- [ ] **Step 2: Commit**

```bash
git add build.ps1
git commit -m "feat: 固定 llama.cpp 版本为 b8354"
```

### Task 6: Pin llama.cpp in build.yml

**Files:**
- Modify: `.github/workflows/build.yml:159-168`

- [ ] **Step 1: Replace dynamic release query with fixed tag**

Replace:
```powershell
          Write-Host "Fetching llama.cpp..."
          $llamareleases = Invoke-RestMethod `
            -Uri "https://api.github.com/repos/ggml-org/llama.cpp/releases?per_page=10" `
            -Headers $headers
          $llamarel = $llamareleases |
            Where-Object { -not $_.prerelease } |
            Select-Object -First 1
          if (-not $llamarel) { throw "llama.cpp release not found" }
          Write-Host "  llama.cpp: $($llamarel.tag_name)"
```
with:
```powershell
          # llama.cpp pinned to b8354 — update tag to change version
          $llamaTag = "b8354"
          Write-Host "Fetching llama.cpp $llamaTag..."
          $llamarel = Invoke-WithRetry -Operation "Fetch llama.cpp $llamaTag" -ScriptBlock {
            Invoke-RestMethod -Uri "https://api.github.com/repos/ggml-org/llama.cpp/releases/tags/$llamaTag" -Headers $headers -TimeoutSec 30
          }.GetNewClosure()
          Write-Host "  llama.cpp: $($llamarel.tag_name)"
```

Note: This step depends on Task 8 adding `Invoke-WithRetry` to build.yml. If executing in order, this reference will be valid by commit time.

- [ ] **Step 2: Commit**

```bash
git add .github/workflows/build.yml
git commit -m "feat: CI 中固定 llama.cpp 版本为 b8354"
```

### Task 7: Update LlamaVersion constant

**Files:**
- Modify: `XUnityToolkit-WebUI/Services/LocalLlmService.cs:49`

- [ ] **Step 1: Update constant**

Replace:
```csharp
    public const string LlamaVersion = "b8272";
```
with:
```csharp
    public const string LlamaVersion = "b8354";
```

- [ ] **Step 2: Commit**

```bash
git add XUnityToolkit-WebUI/Services/LocalLlmService.cs
git commit -m "feat: 更新 LlamaVersion 常量为 b8354"
```

## Chunk 3: Add Timeout/Retry to CI + Align Builds

### Task 8: Add Invoke-WithRetry and timeouts to build.yml

**Files:**
- Modify: `.github/workflows/build.yml` (Download bundled assets step)

- [ ] **Step 1: Add Invoke-WithRetry function and timeouts**

At the top of the "Download bundled assets" PowerShell block (after the `$headers` definition and `Download-Asset` function), add the `Invoke-WithRetry` function. Also update `Download-Asset` to use retry and timeout:

Replace the `Download-Asset` function (lines 87-100):
```powershell
          function Download-Asset {
            param([string]$Url, [string]$DestDir, [string]$FileName = $null)
            if (-not $FileName) {
              $FileName = [System.Uri]::UnescapeDataString(($Url -split '/')[-1])
            }
            $destPath = Join-Path $DestDir $FileName
            if (Test-Path $destPath) {
              Write-Host "  [cached] $FileName"
              return
            }
            Write-Host "  Downloading $FileName..."
            $ProgressPreference = 'SilentlyContinue'
            Invoke-WebRequest -Uri $Url -OutFile $destPath -UseBasicParsing
          }
```
with:
```powershell
          function Invoke-WithRetry {
            param(
              [scriptblock]$ScriptBlock,
              [int]$MaxRetries = 3,
              [string]$Operation = "Operation"
            )
            $attempt = 0
            while ($true) {
              try {
                $attempt++
                return (& $ScriptBlock)
              } catch {
                if ($attempt -ge $MaxRetries) {
                  throw "${Operation}: failed after $MaxRetries attempts. Last error: $($_.Exception.Message)"
                }
                $waitSec = [math]::Pow(2, $attempt)
                Write-Host "  [retry] $Operation failed (attempt $attempt/$MaxRetries), retrying in ${waitSec}s..."
                Write-Host "          $($_.Exception.Message)"
                Start-Sleep -Seconds $waitSec
              }
            }
          }

          function Download-Asset {
            param([string]$Url, [string]$DestDir, [string]$FileName = $null)
            if (-not $FileName) {
              $FileName = [System.Uri]::UnescapeDataString(($Url -split '/')[-1])
            }
            $destPath = Join-Path $DestDir $FileName
            if (Test-Path $destPath) {
              Write-Host "  [cached] $FileName"
              return
            }
            Write-Host "  Downloading $FileName..."
            $ProgressPreference = 'SilentlyContinue'
            Invoke-WithRetry -Operation "Download $FileName" -ScriptBlock {
              Invoke-WebRequest -Uri $Url -OutFile $destPath -UseBasicParsing -TimeoutSec 600
            }.GetNewClosure()
          }
```

- [ ] **Step 2: Wrap all API calls with Invoke-WithRetry and add timeouts**

BepInEx 5 API call — replace:
```powershell
          $b5releases = Invoke-RestMethod `
            -Uri "https://api.github.com/repos/BepInEx/BepInEx/releases?per_page=20" `
            -Headers $headers
```
with:
```powershell
          $b5releases = Invoke-WithRetry -Operation "Fetch BepInEx 5 releases" -ScriptBlock {
            Invoke-RestMethod -Uri "https://api.github.com/repos/BepInEx/BepInEx/releases?per_page=20" -Headers $headers -TimeoutSec 30
          }.GetNewClosure()
```

BepInEx 6 HTML scrape — replace:
```powershell
          $buildsHtml = (Invoke-WebRequest -Uri "https://builds.bepinex.dev/projects/bepinex_be" -UseBasicParsing).Content
```
with:
```powershell
          $buildsHtml = (Invoke-WithRetry -Operation "Fetch BepInEx 6 BE page" -ScriptBlock {
            Invoke-WebRequest -Uri "https://builds.bepinex.dev/projects/bepinex_be" -UseBasicParsing -TimeoutSec 60
          }).Content
```

XUnity API call — replace:
```powershell
          $xureleases = Invoke-RestMethod `
            -Uri "https://api.github.com/repos/bbepis/XUnity.AutoTranslator/releases?per_page=5" `
            -Headers $headers
```
with:
```powershell
          $xureleases = Invoke-WithRetry -Operation "Fetch XUnity releases" -ScriptBlock {
            Invoke-RestMethod -Uri "https://api.github.com/repos/bbepis/XUnity.AutoTranslator/releases?per_page=5" -Headers $headers -TimeoutSec 30
          }.GetNewClosure()
```

(llama.cpp API call was already updated in Task 6 with retry and timeout.)

- [ ] **Step 3: Commit**

```bash
git add .github/workflows/build.yml
git commit -m "feat: 为 CI 构建添加网络请求超时和重试机制"
```

### Task 9: Fix Compress-Archive and MSI lookup in build.yml

**Files:**
- Modify: `.github/workflows/build.yml` (Create component ZIPs step, Package as ZIP step, Build MSI step)

- [ ] **Step 1: Replace Compress-Archive in component ZIPs**

Replace the "Create component ZIPs" step body with `[System.IO.Compression.ZipFile]`:
```powershell
          $rid = "win-x64"
          $releaseDir = "Release/$rid"

          Add-Type -AssemblyName System.IO.Compression.FileSystem

          # app ZIP — root-level files (exclude appsettings*)
          $appFiles = Get-ChildItem -Path $releaseDir -File | Where-Object { $_.Name -notmatch '^appsettings' }
          if ($appFiles) {
            $appZip = "Release/app-$rid.zip"
            if (Test-Path $appZip) { Remove-Item $appZip }
            $zip = [System.IO.Compression.ZipFile]::Open($appZip, 'Create')
            try {
              foreach ($f in $appFiles) {
                [System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip, $f.FullName, $f.Name) | Out-Null
              }
            } finally { $zip.Dispose() }
          }

          # wwwroot ZIP
          $wwwrootDir = Join-Path $releaseDir "wwwroot"
          if (Test-Path $wwwrootDir) {
            $tempDir = "Release/_wwwroot_wrap"
            if (Test-Path $tempDir) { Remove-Item $tempDir -Recurse -Force }
            New-Item -ItemType Directory -Path $tempDir | Out-Null
            Copy-Item $wwwrootDir (Join-Path $tempDir "wwwroot") -Recurse
            [System.IO.Compression.ZipFile]::CreateFromDirectory($tempDir, "Release/wwwroot.zip")
            Remove-Item $tempDir -Recurse -Force
          }

          # bundled ZIP
          $bundledDir = Join-Path $releaseDir "bundled"
          if (Test-Path $bundledDir) {
            $tempDir = "Release/_bundled_wrap"
            if (Test-Path $tempDir) { Remove-Item $tempDir -Recurse -Force }
            New-Item -ItemType Directory -Path $tempDir | Out-Null
            Copy-Item $bundledDir (Join-Path $tempDir "bundled") -Recurse
            [System.IO.Compression.ZipFile]::CreateFromDirectory($tempDir, "Release/bundled.zip")
            Remove-Item $tempDir -Recurse -Force
          }
```

- [ ] **Step 2: Replace Compress-Archive in Package as ZIP**

Replace the "Package as ZIP" step body:
```powershell
          Add-Type -AssemblyName System.IO.Compression.FileSystem
          $zipName = "XUnityToolkit-WebUI-win-x64.zip"
          if (Test-Path $zipName) { Remove-Item $zipName }
          [System.IO.Compression.ZipFile]::CreateFromDirectory("Release\win-x64", $zipName)
          $sizeMB = [math]::Round((Get-Item $zipName).Length / 1MB, 1)
          Write-Host "Created $zipName ($sizeMB MB)"
```

- [ ] **Step 3: Add -Recurse to MSI lookup**

In "Build MSI" step, replace:
```powershell
          $msiFile = Get-ChildItem "$wixOutputDir/*.msi" -ErrorAction SilentlyContinue | Select-Object -First 1
```
with:
```powershell
          $msiFile = Get-ChildItem "$wixOutputDir/*.msi" -Recurse -ErrorAction SilentlyContinue | Select-Object -First 1
```

- [ ] **Step 4: Commit**

```bash
git add .github/workflows/build.yml
git commit -m "fix: CI 使用 ZipFile 替代 Compress-Archive，MSI 查找添加 -Recurse"
```

### Task 10: Update CLAUDE.md

**Files:**
- Modify: `CLAUDE.md`

- [ ] **Step 1: Update relevant sections**

In the Build Commands section, remove `build-local.ps1` reference and update `build.ps1` usage:
```bash
# One-click release build (self-contained, win-x64)
.\build.ps1
```

In the Architecture section, update the bundled assets note:
- Change "ALL auto-detect latest versions via API; no hardcoded version pins" to note llama.cpp is pinned to b8354
- Remove any ARM64 references

In Build & Deploy section:
- Update `build.ps1` description to remove ARM64 mentions
- Update Updater note to remove ARM64 reasoning
- Note llama.cpp is pinned

- [ ] **Step 2: Commit**

```bash
git add CLAUDE.md
git commit -m "docs: 更新 CLAUDE.md 反映 ARM64 移除和 llama 版本固定"
```
