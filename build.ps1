# build.ps1 - XUnityToolkit-WebUI 一键构建脚本
# 用法: .\build.ps1 [-Runtime win-x64|win-arm64|all]

param(
    [ValidateSet('win-x64', 'win-arm64', 'all')]
    [string]$Runtime = 'all'
)

$ErrorActionPreference = 'Stop'

$ProjectRoot = $PSScriptRoot
$ProjectFile = Join-Path $ProjectRoot 'XUnityToolkit-WebUI\XUnityToolkit-WebUI.csproj'
$FrontendDir = Join-Path $ProjectRoot 'xunity-webui'
$ReleaseRoot = Join-Path $ProjectRoot 'Release'

$Runtimes = if ($Runtime -eq 'all') { @('win-x64', 'win-arm64') } else { @($Runtime) }
$totalSteps = 2 + $Runtimes.Count

Write-Host ""
Write-Host "=== XUnityToolkit-WebUI Build ===" -ForegroundColor Cyan
Write-Host "    Target: $($Runtimes -join ', ')" -ForegroundColor DarkGray
Write-Host ""

# Step 1: Build frontend once
Write-Host "[1/$totalSteps] Building frontend..." -ForegroundColor Yellow
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

# Step 2: Clean Release folder
Write-Host ""
Write-Host "[2/$totalSteps] Preparing Release folder..." -ForegroundColor Yellow
if (Test-Path $ReleaseRoot) {
    Remove-Item $ReleaseRoot -Recurse -Force
}
New-Item -ItemType Directory -Path $ReleaseRoot -Force | Out-Null

# Step 3+: Publish for each runtime
$step = 2
foreach ($rid in $Runtimes) {
    $step++
    $OutputDir = Join-Path $ReleaseRoot $rid
    Write-Host ""
    Write-Host "[$step/$totalSteps] Publishing $rid..." -ForegroundColor Yellow

    & dotnet publish $ProjectFile `
        -c Release `
        -r $rid `
        --self-contained true `
        -p:PublishSingleFile=true `
        -p:IncludeNativeLibrariesForSelfExtract=true `
        -p:DebugType=none `
        -p:SkipFrontendBuild=true `
        -o $OutputDir

    if ($LASTEXITCODE -ne 0) {
        Write-Host "Build failed for $rid!" -ForegroundColor Red
        exit 1
    }

    # Clean up unnecessary files
    @('web.config', '*.pdb', '*.staticwebassets.endpoints.json') | ForEach-Object {
        Get-ChildItem -Path $OutputDir -Filter $_ -ErrorAction SilentlyContinue |
            Remove-Item -Force
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
            $size = "<DIR>"
        } else {
            $size = "$([math]::Round($_.Length / 1MB, 1)) MB"
        }
        Write-Host "  $($_.Name.PadRight(35)) $size"
    }
    Write-Host ""
}

Write-Host "Output: $ReleaseRoot" -ForegroundColor White
Write-Host ""
