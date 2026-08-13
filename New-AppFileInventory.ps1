[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$ReleaseDir
)

$ErrorActionPreference = 'Stop'
$inventoryName = 'app-file-inventory-v1.json'
$releaseRoot = (Resolve-Path -LiteralPath $ReleaseDir).Path.TrimEnd(
    [System.IO.Path]::DirectorySeparatorChar,
    [System.IO.Path]::AltDirectorySeparatorChar)
$inventoryPath = Join-Path $releaseRoot $inventoryName

$files = [System.Collections.Generic.List[string]]::new()
Get-ChildItem -LiteralPath $releaseRoot -File -Recurse | ForEach-Object {
    $relativePath = $_.FullName.Substring($releaseRoot.Length + 1).Replace('\', '/')
    if ($relativePath -eq $inventoryName) { return }
    if ($relativePath -match '(?i)^(wwwroot|bundled|data)/') { return }
    if ($relativePath -match '(?i)^appsettings') { return }
    $files.Add($relativePath)
}

$files.Add($inventoryName)
$sortedFiles = @($files | Sort-Object -Unique)
$inventory = [ordered]@{
    protocolVersion = 1
    files = $sortedFiles
}

$inventory | ConvertTo-Json -Depth 3 | Set-Content -LiteralPath $inventoryPath -Encoding utf8
Write-Host "Generated $inventoryName ($($sortedFiles.Count) app files)"
