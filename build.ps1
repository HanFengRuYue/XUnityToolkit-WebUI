# build.ps1 - XUnityToolkit-WebUI 一键构建脚本
# 用法: .\build.ps1 [-Runtime win-x64|win-arm64|all] [-SkipDownload]

param(
    [ValidateSet('win-x64', 'win-arm64', 'all')]
    [string]$Runtime = 'all',
    [switch]$SkipDownload
)

$ErrorActionPreference = 'Stop'

function Wait-Exit {
    param([int]$ExitCode = 0)
    Write-Host ""
    Write-Host "Press any key to exit..." -ForegroundColor DarkGray
    $null = $Host.UI.RawUI.ReadKey('NoEcho,IncludeKeyDown')
    exit $ExitCode
}

try {

$ProjectRoot = $PSScriptRoot
$ProjectFile = Join-Path $ProjectRoot 'XUnityToolkit-WebUI\XUnityToolkit-WebUI.csproj'
$FrontendDir = Join-Path $ProjectRoot 'XUnityToolkit-Vue'
$ReleaseRoot = Join-Path $ProjectRoot 'Release'
$BundledRoot = Join-Path $ProjectRoot 'bundled'

$EndpointProject = Join-Path $ProjectRoot 'TranslatorEndpoint\TranslatorEndpoint.csproj'
$Runtimes = if ($Runtime -eq 'all') { @('win-x64', 'win-arm64') } else { @($Runtime) }
$hasEndpoint = Test-Path $EndpointProject

# ── GitHub repo owners ──
$BepInEx5Owner = "BepInEx"
$BepInEx5Repo = "BepInEx"
$XUnityOwner = "bbepis"
$XUnityRepo = "XUnity.AutoTranslator"

$stepCount = 3 + $Runtimes.Count + $(if ($hasEndpoint) { 1 } else { 0 }) + $(if (-not $SkipDownload) { 1 } else { 0 })

Write-Host ""
Write-Host "=== XUnityToolkit-WebUI Build ===" -ForegroundColor Cyan
Write-Host "    Target: $($Runtimes -join ', ')" -ForegroundColor DarkGray
Write-Host ""

$currentStep = 0

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
        Write-Host "  Downloading $FileName ..."
        $ProgressPreference = 'SilentlyContinue'
        Invoke-WebRequest -Uri $Url -OutFile $destPath -UseBasicParsing
        $ProgressPreference = 'Continue'
        Write-Host "  [done] $FileName" -ForegroundColor Green
    }

    # Remove old version ZIPs that are no longer needed
    function Remove-OldVersions {
        param([string]$Dir, [string[]]$ExpectedFiles)
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
    $bepinex5Releases = Invoke-RestMethod -Uri "https://api.github.com/repos/$BepInEx5Owner/$BepInEx5Repo/releases?per_page=20" -Headers @{ 'User-Agent' = 'XUnityToolkit-Build/1.0' }
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
    $buildsPage = Invoke-WebRequest -Uri "https://builds.bepinex.dev/projects/bepinex_be" -UseBasicParsing
    $buildsHtml = $buildsPage.Content

    # Parse latest build number and commit from IL2CPP x64 link
    if ($buildsHtml -match 'href="[^"]*?/(\d+)/BepInEx-Unity\.IL2CPP-win-x64-6\.0\.0-be\.\d+%2B([a-f0-9]+)\.zip"') {
        $be6BuildNo = $Matches[1]
        $be6Commit = $Matches[2]
        Write-Host "  BepInEx 6 BE: build $be6BuildNo ($be6Commit)" -ForegroundColor DarkGray
    } else {
        throw "Failed to parse BepInEx 6 BE build info from builds.bepinex.dev"
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
    $xunityReleases = Invoke-RestMethod -Uri "https://api.github.com/repos/$XUnityOwner/$XUnityRepo/releases?per_page=10" -Headers @{ 'User-Agent' = 'XUnityToolkit-Build/1.0' }
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

    # ── llama.cpp binaries from GitHub Releases ──
    Write-Host "  Fetching llama.cpp latest release..." -ForegroundColor DarkGray
    $llamaReleases = Invoke-RestMethod -Uri "https://api.github.com/repos/ggml-org/llama.cpp/releases?per_page=10" -Headers @{ 'User-Agent' = 'XUnityToolkit-Build/1.0' }
    $llamaRelease = $llamaReleases | Where-Object { -not $_.prerelease } | Select-Object -First 1
    if (-not $llamaRelease) { throw "llama.cpp release not found" }
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

    Write-Host "  Bundled assets ready." -ForegroundColor Green
}

# Step: Build frontend
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

# Step: Build TranslatorEndpoint (LLMTranslate.dll)
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

# Clean Release folder
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

# Publish for each runtime
foreach ($rid in $Runtimes) {
    $currentStep++
    $OutputDir = Join-Path $ReleaseRoot $rid
    Write-Host ""
    Write-Host "[$currentStep/$stepCount] Publishing $rid..." -ForegroundColor Yellow

    & dotnet publish $ProjectFile `
        -c Release `
        -r $rid `
        --self-contained true `
        -p:PublishSingleFile=true `
        -p:IncludeNativeLibrariesForSelfExtract=true `
        -p:DebugType=none `
        -p:SkipFrontendBuild=true `
        -o $OutputDir

    if ($LASTEXITCODE -ne 0) { throw "Publishing failed for $rid" }

    # Clean up unnecessary files
    @('web.config', '*.pdb', '*.staticwebassets.endpoints.json') | ForEach-Object {
        Get-ChildItem -Path $OutputDir -Filter $_ -ErrorAction SilentlyContinue |
            Remove-Item -Force
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
}

# Summary
Write-Host ""
Write-Host "=== Build Complete ===" -ForegroundColor Cyan
Write-Host ""

foreach ($rid in $Runtimes) {
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
}

Write-Host "Output: $ReleaseRoot" -ForegroundColor White

Wait-Exit 0

} catch {
    Write-Host ""
    Write-Host "=== BUILD FAILED ===" -ForegroundColor Red
    Write-Host "  $($_.Exception.Message)" -ForegroundColor Red
    Write-Host ""
    Wait-Exit 1
}
