# build-local.ps1 - 本地快速构建（跳过下载）
# 用法: .\build-local.ps1 [-Runtime win-x64|win-arm64|all]

param(
    [ValidateSet('win-x64', 'win-arm64', 'all')]
    [string]$Runtime = 'all'
)

& "$PSScriptRoot\build.ps1" -Runtime $Runtime -SkipDownload
