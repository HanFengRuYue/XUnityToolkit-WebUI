# build.ps1 - XUnityToolkit-WebUI 一键构建脚本
# 用法: .\build.ps1 [-Runtime win-x64|win-arm64|all]

param(
    [ValidateSet('win-x64', 'win-arm64', 'all')]
    [string]$Runtime = 'all'
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

$EndpointProject = Join-Path $ProjectRoot 'TranslatorEndpoint\TranslatorEndpoint.csproj'
$Runtimes = if ($Runtime -eq 'all') { @('win-x64', 'win-arm64') } else { @($Runtime) }
$hasEndpoint = Test-Path $EndpointProject
$totalSteps = 2 + $Runtimes.Count + $(if ($hasEndpoint) { 1 } else { 0 })

Write-Host ""
Write-Host "=== XUnityToolkit-WebUI Build ===" -ForegroundColor Cyan
Write-Host "    Target: $($Runtimes -join ', ')" -ForegroundColor DarkGray
Write-Host "    llama.cpp: downloaded on-demand at runtime" -ForegroundColor DarkGray
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

# Step 2: Build TranslatorEndpoint (LLMTranslate.dll)
$nextStep = 2
if ($hasEndpoint) {
    Write-Host ""
    Write-Host "[$nextStep/$totalSteps] Building TranslatorEndpoint (LLMTranslate.dll)..." -ForegroundColor Yellow

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
    $nextStep++
}

# Clean Release folder
Write-Host ""
Write-Host "[$nextStep/$totalSteps] Preparing Release folder..." -ForegroundColor Yellow

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
$step = $nextStep
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

    if ($LASTEXITCODE -ne 0) { throw "Publishing failed for $rid" }

    # Clean up unnecessary files
    @('web.config', '*.pdb', '*.staticwebassets.endpoints.json') | ForEach-Object {
        Get-ChildItem -Path $OutputDir -Filter $_ -ErrorAction SilentlyContinue |
            Remove-Item -Force
    }

    # TMP fonts are copied automatically by dotnet publish via csproj Content items

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
