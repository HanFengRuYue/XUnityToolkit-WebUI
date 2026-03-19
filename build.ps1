# build.ps1 - XUnityToolkit-WebUI 本地构建脚本（便携版）
# 用法: .\build.ps1 [-SkipDownload]

param(
    [switch]$SkipDownload
)

$ErrorActionPreference = 'Stop'

function Wait-Exit {
    param([int]$ExitCode = 0)
    Write-Host ""
    try {
        Write-Host "Press 0 to exit..." -ForegroundColor DarkGray
        do {
            $key = $Host.UI.RawUI.ReadKey('NoEcho,IncludeKeyDown')
        } while ($key.Character -ne '0')
    } catch {
        # ReadKey may fail in non-interactive or non-standard hosts
        Read-Host "Press Enter to exit"
    }
    exit $ExitCode
}

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
            Write-Host "  [retry] $Operation failed (attempt $attempt/$MaxRetries), retrying in ${waitSec}s..." -ForegroundColor DarkYellow
            Write-Host "          $($_.Exception.Message)" -ForegroundColor DarkGray
            Start-Sleep -Seconds $waitSec
        }
    }
}

try {

# Ensure TLS 1.2+ for all HTTPS requests (PowerShell 5.1 defaults to TLS 1.0)
[Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12

# Suppress PowerShell progress bars globally (Invoke-WebRequest/RestMethod progress noise)
$ProgressPreference = 'SilentlyContinue'

$ProjectRoot = $PSScriptRoot
$ProjectFile = Join-Path $ProjectRoot 'XUnityToolkit-WebUI\XUnityToolkit-WebUI.csproj'
$FrontendDir = Join-Path $ProjectRoot 'XUnityToolkit-Vue'
$ReleaseRoot = Join-Path $ProjectRoot 'Release'
$BundledRoot = Join-Path $ProjectRoot 'bundled'

$EndpointProject = Join-Path $ProjectRoot 'TranslatorEndpoint\TranslatorEndpoint.csproj'
$UpdaterProject = Join-Path $ProjectRoot 'Updater\Updater.csproj'
$rid = 'win-x64'
$hasEndpoint = Test-Path $EndpointProject
$hasUpdater = Test-Path $UpdaterProject

# Generate version: 3.1.{YYYYMMDDHHmm}
$BuildVersion = "3.1.$(Get-Date -Format 'yyyyMMddHHmm')"

# ── GitHub repo owners ──
$BepInEx5Owner = "BepInEx"
$BepInEx5Repo = "BepInEx"
$XUnityOwner = "bbepis"
$XUnityRepo = "XUnity.AutoTranslator"

$stepCount = 3 + $(if ($hasEndpoint) { 1 } else { 0 }) + $(if ($hasUpdater) { 1 } else { 0 }) + $(if (-not $SkipDownload) { 1 } else { 0 })

Write-Host ""
Write-Host "=== XUnityToolkit-WebUI Build ===" -ForegroundColor Cyan
Write-Host "    Version: $BuildVersion" -ForegroundColor DarkGray
Write-Host "    Target: $rid" -ForegroundColor DarkGray
Write-Host ""

$currentStep = 0

# ── Resolve GitHub auth token for API rate limits (60/h unauthenticated → 5000/h authenticated) ──
$GitHubHeaders = @{ 'User-Agent' = 'XUnityToolkit-Build/1.0' }
if ($env:GITHUB_TOKEN) {
    $GitHubHeaders['Authorization'] = "Bearer $env:GITHUB_TOKEN"
    Write-Host "  GitHub auth: using GITHUB_TOKEN env var" -ForegroundColor DarkGray
} elseif (Get-Command gh -ErrorAction SilentlyContinue) {
    try {
        $ghToken = & gh auth token 2>$null
        if ($LASTEXITCODE -eq 0 -and $ghToken) {
            $GitHubHeaders['Authorization'] = "Bearer $($ghToken.Trim())"
            Write-Host "  GitHub auth: using gh CLI token" -ForegroundColor DarkGray
        }
    } catch { }
}

# ── Step: Download bundled assets ──
if (-not $SkipDownload) {
    $currentStep++
    Write-Host "[$currentStep/$stepCount] Downloading bundled assets..." -ForegroundColor Yellow

    # Create bundled directories
    $dirs = @(
        (Join-Path $BundledRoot 'bepinex5'),
        (Join-Path $BundledRoot 'bepinex6'),
        (Join-Path $BundledRoot 'xunity'),
        (Join-Path $BundledRoot 'llama')
    )
    foreach ($d in $dirs) {
        if (-not (Test-Path $d)) { New-Item -ItemType Directory -Path $d -Force | Out-Null }
    }

    function Download-IfMissing {
        param([string]$Url, [string]$DestDir, [string]$FileName = $null)
        if (-not $FileName) { $FileName = [System.Uri]::UnescapeDataString(($Url -split '/')[-1]) }
        $destPath = Join-Path $DestDir $FileName
        if (Test-Path $destPath) {
            Write-Host "  [cached] $FileName" -ForegroundColor DarkGray
            return
        }
        # Clean up leftover partial download from previous failed attempt
        $tmpPath = "$destPath.downloading"
        if (Test-Path $tmpPath) { Remove-Item $tmpPath -Force }
        Write-Host "  Downloading $FileName ..."
        Invoke-WithRetry -Operation "Download $FileName" -ScriptBlock {
            Invoke-WebRequest -Uri $Url -OutFile $tmpPath -UseBasicParsing -TimeoutSec 600
        }.GetNewClosure()
        Move-Item $tmpPath $destPath -Force
        Write-Host "  [done] $FileName" -ForegroundColor Green
    }

    # Remove old version ZIPs and leftover temp files
    function Remove-OldVersions {
        param([string]$Dir, [string[]]$ExpectedFiles)
        # Clean up .downloading temp files from interrupted downloads
        Get-ChildItem -Path $Dir -Filter "*.downloading" -ErrorAction SilentlyContinue |
            ForEach-Object {
                Write-Host "  [cleanup] Removing partial: $($_.Name)" -ForegroundColor DarkYellow
                Remove-Item $_.FullName -Force
            }
        $existing = Get-ChildItem -Path $Dir -Filter "*.zip" -ErrorAction SilentlyContinue
        foreach ($file in $existing) {
            if ($file.Name -notin $ExpectedFiles) {
                Write-Host "  [cleanup] Removing old: $($file.Name)" -ForegroundColor DarkYellow
                Remove-Item $file.FullName -Force
            }
        }
    }

    # ── BepInEx 5 (Mono) from GitHub Releases ──
    Write-Host "  Fetching BepInEx 5 latest stable release..." -ForegroundColor DarkGray
    $bepinex5Releases = Invoke-WithRetry -Operation "Fetch BepInEx 5 releases" -ScriptBlock {
        Invoke-RestMethod -Uri "https://api.github.com/repos/$BepInEx5Owner/$BepInEx5Repo/releases?per_page=20" -Headers $GitHubHeaders -TimeoutSec 30
    }.GetNewClosure()
    $bepinex5Release = $bepinex5Releases | Where-Object { -not $_.prerelease -and $_.tag_name -like 'v5*' } | Select-Object -First 1
    if (-not $bepinex5Release) { throw "BepInEx 5 stable release not found" }
    Write-Host "  BepInEx 5: $($bepinex5Release.tag_name)" -ForegroundColor DarkGray

    $expectedBepInEx5 = @()
    foreach ($arch in @('x64', 'x86')) {
        $asset = $bepinex5Release.assets | Where-Object { $_.name -like "BepInEx_win_${arch}_*" } | Select-Object -First 1
        if ($asset) {
            Download-IfMissing -Url $asset.browser_download_url -DestDir (Join-Path $BundledRoot 'bepinex5')
            $expectedBepInEx5 += $asset.name
        } else {
            Write-Host "  [skip] BepInEx 5 $arch asset not found" -ForegroundColor DarkYellow
        }
    }
    Remove-OldVersions -Dir (Join-Path $BundledRoot 'bepinex5') -ExpectedFiles $expectedBepInEx5

    # ── BepInEx 6 BE (IL2CPP) from builds.bepinex.dev ──
    Write-Host "  Fetching BepInEx 6 BE latest build..." -ForegroundColor DarkGray
    $buildsPage = Invoke-WithRetry -Operation "Fetch BepInEx 6 BE page" -ScriptBlock {
        Invoke-WebRequest -Uri "https://builds.bepinex.dev/projects/bepinex_be" -UseBasicParsing -TimeoutSec 60
    }
    $buildsHtml = $buildsPage.Content

    # Parse latest build number and commit from IL2CPP x64 link
    if ($buildsHtml -match 'href="[^"]*?/(\d+)/BepInEx-Unity\.IL2CPP-win-x64-6\.0\.0-be\.\d+%2B([a-f0-9]+)\.zip"') {
        $be6BuildNo = $Matches[1]
        $be6Commit = $Matches[2]
        Write-Host "  BepInEx 6 BE: build $be6BuildNo ($be6Commit)" -ForegroundColor DarkGray
    } else {
        throw "Failed to parse BepInEx 6 BE build info from builds.bepinex.dev (page size: $($buildsHtml.Length) chars)"
    }

    $expectedBepInEx6 = @()
    foreach ($arch in @('x64', 'x86')) {
        $be6Url = "https://builds.bepinex.dev/projects/bepinex_be/$be6BuildNo/BepInEx-Unity.IL2CPP-win-$arch-6.0.0-be.$be6BuildNo%2B$be6Commit.zip"
        $be6FileName = "BepInEx-Unity.IL2CPP-win-$arch-6.0.0-be.$be6BuildNo+$be6Commit.zip"
        Download-IfMissing -Url $be6Url -DestDir (Join-Path $BundledRoot 'bepinex6') -FileName $be6FileName
        $expectedBepInEx6 += $be6FileName
    }
    Remove-OldVersions -Dir (Join-Path $BundledRoot 'bepinex6') -ExpectedFiles $expectedBepInEx6

    # ── XUnity.AutoTranslator from GitHub Releases ──
    Write-Host "  Fetching XUnity.AutoTranslator latest release..." -ForegroundColor DarkGray
    $xunityReleases = Invoke-WithRetry -Operation "Fetch XUnity releases" -ScriptBlock {
        Invoke-RestMethod -Uri "https://api.github.com/repos/$XUnityOwner/$XUnityRepo/releases?per_page=10" -Headers $GitHubHeaders -TimeoutSec 30
    }.GetNewClosure()
    $xunityRelease = $xunityReleases | Select-Object -First 1
    if (-not $xunityRelease) { throw "XUnity.AutoTranslator release not found" }
    Write-Host "  XUnity: $($xunityRelease.tag_name)" -ForegroundColor DarkGray

    $expectedXUnity = @()
    foreach ($asset in $xunityRelease.assets) {
        if ($asset.name -like 'XUnity.AutoTranslator-BepInEx*') {
            Download-IfMissing -Url $asset.browser_download_url -DestDir (Join-Path $BundledRoot 'xunity')
            $expectedXUnity += $asset.name
        }
    }
    Remove-OldVersions -Dir (Join-Path $BundledRoot 'xunity') -ExpectedFiles $expectedXUnity

    # ── Extract XUnity reference DLLs for TranslatorEndpoint ──
    $xunityZip = Get-ChildItem -Path (Join-Path $BundledRoot 'xunity') -Filter '*.zip' |
        Where-Object { $_.Name -notlike '*IL2CPP*' } |
        Select-Object -First 1
    if ($xunityZip) {
        Write-Host "  Extracting XUnity reference DLLs..." -ForegroundColor DarkGray
        $libsDir = Join-Path $ProjectRoot 'TranslatorEndpoint\libs'
        if (-not (Test-Path $libsDir)) { New-Item -ItemType Directory -Path $libsDir -Force | Out-Null }
        try {
            Add-Type -AssemblyName System.IO.Compression.FileSystem
            $zip = [System.IO.Compression.ZipFile]::OpenRead($xunityZip.FullName)
            try {
                $dllMappings = @(
                    @{ Entry = 'BepInEx/plugins/XUnity.AutoTranslator/XUnity.AutoTranslator.Plugin.Core.dll'; Target = 'XUnity.AutoTranslator.Plugin.Core.dll' },
                    @{ Entry = 'BepInEx/core/XUnity.Common.dll'; Target = 'XUnity.Common.dll' }
                )
                foreach ($mapping in $dllMappings) {
                    $entry = $zip.GetEntry($mapping.Entry)
                    if (-not $entry) {
                        Write-Host "  [warn] Entry '$($mapping.Entry)' not found in ZIP" -ForegroundColor DarkYellow
                        continue
                    }
                    $targetPath = Join-Path $libsDir $mapping.Target
                    # Compare SHA256 to detect changes
                    $entryStream = $entry.Open()
                    $memStream = New-Object System.IO.MemoryStream
                    try {
                        $entryStream.CopyTo($memStream)
                        $newBytes = $memStream.ToArray()
                        $sha256 = [System.Security.Cryptography.SHA256]::Create()
                        try {
                            $newHash = [BitConverter]::ToString($sha256.ComputeHash($newBytes)).Replace('-','')
                        } finally {
                            $sha256.Dispose()
                        }
                    } finally {
                        $entryStream.Dispose()
                        $memStream.Dispose()
                    }
                    $changed = $true
                    if (Test-Path $targetPath) {
                        $oldHash = (Get-FileHash -Path $targetPath -Algorithm SHA256).Hash
                        if ($oldHash -eq $newHash) { $changed = $false }
                    }
                    if ($changed) {
                        [System.IO.File]::WriteAllBytes($targetPath, $newBytes)
                        Write-Host "  [updated] $($mapping.Target)" -ForegroundColor Green
                    } else {
                        Write-Host "  [unchanged] $($mapping.Target)" -ForegroundColor DarkGray
                    }
                }
            } finally {
                $zip.Dispose()
            }
        } catch {
            Write-Host "  [warn] Failed to extract XUnity DLLs: $($_.Exception.Message)" -ForegroundColor DarkYellow
            Write-Host "         Using committed DLLs as fallback." -ForegroundColor DarkGray
        }
    } else {
        Write-Host "  [skip] XUnity ZIP not found, using committed DLLs" -ForegroundColor DarkGray
    }

    # ── llama.cpp binaries from GitHub Releases (pinned version) ──
    $llamaTag = "b8416"
    Write-Host "  Fetching llama.cpp $llamaTag..." -ForegroundColor DarkGray
    $llamaRelease = Invoke-WithRetry -Operation "Fetch llama.cpp $llamaTag" -ScriptBlock {
        Invoke-RestMethod -Uri "https://api.github.com/repos/ggml-org/llama.cpp/releases/tags/$llamaTag" -Headers $GitHubHeaders -TimeoutSec 30
    }.GetNewClosure()
    Write-Host "  llama.cpp: $($llamaRelease.tag_name)" -ForegroundColor DarkGray

    $llamaDir = Join-Path $BundledRoot 'llama'

    # Match assets by pattern (prefer CUDA 12.4 for broader compatibility)
    $llamaPatterns = @(
        @{ Pattern = "llama-*-bin-win-cuda-12.4-x64.zip"; Label = "CUDA 12.4" },
        @{ Pattern = "cudart-llama-bin-win-cuda-12.4-x64.zip"; Label = "CUDA Runtime" },
        @{ Pattern = "llama-*-bin-win-vulkan-x64.zip"; Label = "Vulkan" },
        @{ Pattern = "llama-*-bin-win-cpu-x64.zip"; Label = "CPU" }
    )
    $expectedLlama = @()
    foreach ($p in $llamaPatterns) {
        $asset = $llamaRelease.assets | Where-Object { $_.name -like $p.Pattern } | Select-Object -First 1
        if ($asset) {
            Download-IfMissing -Url $asset.browser_download_url -DestDir $llamaDir
            $expectedLlama += $asset.name
        } else {
            Write-Host "  [skip] llama.cpp $($p.Label) asset not found" -ForegroundColor DarkYellow
        }
    }
    Remove-OldVersions -Dir $llamaDir -ExpectedFiles $expectedLlama

    # ── Update classdata.tpk from AssetRipper/Tpk CI ──
    if (Get-Command gh -ErrorAction SilentlyContinue) {
        Write-Host "  Fetching latest classdata.tpk from AssetRipper/Tpk..." -ForegroundColor DarkGray
        $tpkTempDir = Join-Path ([System.IO.Path]::GetTempPath()) "tpk_$(Get-Random)"
        try {
            $ghOutput = & gh run download -R AssetRipper/Tpk -n lz4_file -D $tpkTempDir 2>&1
            if ($LASTEXITCODE -ne 0) { throw "gh run download failed: $ghOutput" }
            $downloadedTpk = Join-Path $tpkTempDir 'lz4.tpk'
            if (-not (Test-Path $downloadedTpk)) { throw "lz4.tpk not found in downloaded artifact" }
            $targetTpk = Join-Path $ProjectRoot 'XUnityToolkit-WebUI\Resources\classdata.tpk'
            $newHash = (Get-FileHash -Path $downloadedTpk -Algorithm SHA256).Hash
            $changed = $true
            if (Test-Path $targetTpk) {
                $oldHash = (Get-FileHash -Path $targetTpk -Algorithm SHA256).Hash
                if ($oldHash -eq $newHash) { $changed = $false }
            }
            if ($changed) {
                Copy-Item -Path $downloadedTpk -Destination $targetTpk -Force
                Write-Host "  [updated] classdata.tpk" -ForegroundColor Green
            } else {
                Write-Host "  [unchanged] classdata.tpk is up to date" -ForegroundColor DarkGray
            }
        } catch {
            Write-Host "  [warn] Failed to update classdata.tpk: $($_.Exception.Message)" -ForegroundColor DarkYellow
            Write-Host "         Artifacts may have expired (90-day retention). Using committed version." -ForegroundColor DarkGray
        } finally {
            if (Test-Path $tpkTempDir) { Remove-Item $tpkTempDir -Recurse -Force -ErrorAction SilentlyContinue }
        }
    } else {
        Write-Host "  [skip] gh CLI not found, using committed classdata.tpk" -ForegroundColor DarkGray
    }

    Write-Host "  Bundled assets ready." -ForegroundColor Green
}

# ── Step: Build frontend ──
$currentStep++
Write-Host ""
Write-Host "[$currentStep/$stepCount] Building frontend..." -ForegroundColor Yellow
Push-Location $FrontendDir
try {
    & npm install --silent 2>&1 | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "npm install failed" }

    & npm run build
    if ($LASTEXITCODE -ne 0) { throw "Frontend build failed" }
} finally {
    Pop-Location
}
Write-Host "  Frontend build complete." -ForegroundColor Green

# ── Step: Build TranslatorEndpoint (LLMTranslate.dll) ──
if ($hasEndpoint) {
    $currentStep++
    Write-Host ""
    Write-Host "[$currentStep/$stepCount] Building TranslatorEndpoint (LLMTranslate.dll)..." -ForegroundColor Yellow

    # Check if XUnity reference DLLs exist
    $libsDir = Join-Path $ProjectRoot 'TranslatorEndpoint\libs'
    $hasLibs = (Test-Path (Join-Path $libsDir 'XUnity.AutoTranslator.Plugin.Core.dll')) -and
               (Test-Path (Join-Path $libsDir 'XUnity.Common.dll'))

    if ($hasLibs) {
        & dotnet build $EndpointProject -c Release --nologo -v quiet
        if ($LASTEXITCODE -ne 0) { throw "TranslatorEndpoint build failed" }
        Write-Host "  LLMTranslate.dll build complete." -ForegroundColor Green
    } else {
        Write-Host "  Skipped: XUnity reference DLLs not found in TranslatorEndpoint/libs/" -ForegroundColor DarkYellow
        Write-Host "  (LLMTranslate.dll will not be embedded)" -ForegroundColor DarkGray
    }
}

# ── Step: Build Updater (AOT) ──
if ($hasUpdater) {
    $currentStep++
    Write-Host ""
    Write-Host "[$currentStep/$stepCount] Building Updater (AOT)..." -ForegroundColor Yellow

    # AOT publish requires its own restore phase — do NOT use --no-restore
    & dotnet publish $UpdaterProject -c Release -r $rid --nologo -v quiet
    if ($LASTEXITCODE -ne 0) { throw "Updater build failed" }
    Write-Host "  Updater.exe build complete." -ForegroundColor Green
}

# ── Step: Prepare Release folder ──
$currentStep++
Write-Host ""
Write-Host "[$currentStep/$stepCount] Preparing Release folder..." -ForegroundColor Yellow

# Stop processes that may lock files in Release folder
$processesToStop = @('XUnityToolkit-WebUI', 'llama-server')
foreach ($procName in $processesToStop) {
    $procs = Get-Process -Name $procName -ErrorAction SilentlyContinue
    if ($procs) {
        Write-Host "  Stopping $procName..." -ForegroundColor DarkYellow
        $procs | Stop-Process -Force
        Start-Sleep -Milliseconds 500
    }
}

if (Test-Path $ReleaseRoot) {
    Remove-Item $ReleaseRoot -Recurse -Force
}
New-Item -ItemType Directory -Path $ReleaseRoot -Force | Out-Null

# ── Step: Publish win-x64 ──
$currentStep++
$OutputDir = Join-Path $ReleaseRoot $rid
Write-Host ""
Write-Host "[$currentStep/$stepCount] Publishing $rid..." -ForegroundColor Yellow

& dotnet publish $ProjectFile `
    -c Release `
    -r $rid `
    --self-contained true `
    -p:DebugType=none `
    -p:SkipFrontendBuild=true `
    -p:InformationalVersion=$BuildVersion `
    -o $OutputDir

if ($LASTEXITCODE -ne 0) { throw "Publishing failed for $rid" }

# Clean up unnecessary files
@('web.config', '*.pdb', '*.staticwebassets.endpoints.json') | ForEach-Object {
    Get-ChildItem -Path $OutputDir -Filter $_ -ErrorAction SilentlyContinue |
        Remove-Item -Force
}

# Copy Updater.exe
if ($hasUpdater) {
    $updaterExe = Join-Path $ProjectRoot "Updater\bin\Release\net10.0\$rid\publish\Updater.exe"
    if (Test-Path $updaterExe) {
        Copy-Item $updaterExe $OutputDir -Force
        Write-Host "  Copied Updater.exe." -ForegroundColor DarkGray
    } else {
        Write-Host "  [warn] Updater.exe not found at expected path" -ForegroundColor DarkYellow
    }
}

# Copy bundled assets (bypass MSBuild — PublishSingleFile drops files with '+' in names)
$bundledSrc = Join-Path $ProjectRoot 'bundled'
if (Test-Path $bundledSrc) {
    $bundledDest = Join-Path $OutputDir 'bundled'
    if (Test-Path $bundledDest) { Remove-Item $bundledDest -Recurse -Force }
    Copy-Item -Path $bundledSrc -Destination $bundledDest -Recurse -Force
    Write-Host "  Copied bundled assets." -ForegroundColor DarkGray
}

$exeFile = Get-Item (Join-Path $OutputDir 'XUnityToolkit-WebUI.exe')
$exeSize = [math]::Round($exeFile.Length / 1MB, 1)
Write-Host "  $rid done (exe: $exeSize MB)" -ForegroundColor Green

# ── Summary ──
Write-Host ""
Write-Host "=== Build Complete ===" -ForegroundColor Cyan
Write-Host ""

Get-ChildItem $OutputDir | ForEach-Object {
    if ($_.PSIsContainer) {
        $folderSize = [math]::Round(((Get-ChildItem $_.FullName -File -Recurse | Measure-Object -Property Length -Sum).Sum / 1MB), 1)
        $size = "<DIR> $folderSize MB"
    } else {
        $size = "$([math]::Round($_.Length / 1MB, 1)) MB"
    }
    Write-Host "  $($_.Name.PadRight(35)) $size"
}
Write-Host ""

Write-Host "Output: $OutputDir" -ForegroundColor White

Wait-Exit 0

} catch {
    Write-Host ""
    Write-Host "=== BUILD FAILED ===" -ForegroundColor Red
    Write-Host "  $($_.Exception.Message)" -ForegroundColor Red
    if ($_.Exception.InnerException) {
        Write-Host "  Detail: $($_.Exception.InnerException.Message)" -ForegroundColor DarkRed
    }
    Write-Host "  Location: $($_.InvocationInfo.PositionMessage)" -ForegroundColor DarkGray
    Write-Host ""
    Wait-Exit 1
}
