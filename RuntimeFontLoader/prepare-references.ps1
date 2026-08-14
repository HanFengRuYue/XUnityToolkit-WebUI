param(
    [Parameter(Mandatory = $true)]
    [string]$ProjectRoot
)

$ErrorActionPreference = 'Stop'
$ProjectRoot = [IO.Path]::GetFullPath($ProjectRoot)
$runtimeRoot = Join-Path $ProjectRoot 'RuntimeFontLoader'
$monoDir = Join-Path $runtimeRoot 'libs\mono'
$il2CppDir = Join-Path $runtimeRoot 'libs\il2cpp'
New-Item -ItemType Directory -Force -Path $monoDir, $il2CppDir | Out-Null

Add-Type -AssemblyName System.IO.Compression.FileSystem

function Expand-ReferenceFiles {
    param(
        [Parameter(Mandatory = $true)] [string]$ArchivePath,
        [Parameter(Mandatory = $true)] [string]$Destination,
        [Parameter(Mandatory = $true)] [hashtable]$Entries
    )

    $archive = [IO.Compression.ZipFile]::OpenRead($ArchivePath)
    try {
        foreach ($entryName in $Entries.Keys) {
            $entry = $archive.GetEntry($entryName)
            if ($null -eq $entry) { throw "Missing build reference '$entryName' in '$ArchivePath'." }
            $target = Join-Path $Destination $Entries[$entryName]
            [IO.Compression.ZipFileExtensions]::ExtractToFile($entry, $target, $true)
        }
    }
    finally {
        $archive.Dispose()
    }
}
$bepInEx5 = Get-ChildItem -Path (Join-Path $ProjectRoot 'bundled\bepinex5') -Filter '*x64*.zip' |
    Sort-Object Name -Descending | Select-Object -First 1
$bepInEx6 = Get-ChildItem -Path (Join-Path $ProjectRoot 'bundled\bepinex6') -Filter '*x64*.zip' |
    Sort-Object Name -Descending | Select-Object -First 1
if ($null -eq $bepInEx5 -or $null -eq $bepInEx6) {
    throw 'BepInEx 5/6 bundled archives are required to build RuntimeFontLoader.'
}

Expand-ReferenceFiles $bepInEx5.FullName $monoDir @{
    'BepInEx/core/BepInEx.dll' = 'BepInEx.dll'
    'BepInEx/core/0Harmony.dll' = '0Harmony.dll'
}
Expand-ReferenceFiles $bepInEx6.FullName $il2CppDir @{
    'BepInEx/core/BepInEx.Core.dll' = 'BepInEx.Core.dll'
    'BepInEx/core/BepInEx.Unity.Common.dll' = 'BepInEx.Unity.Common.dll'
    'BepInEx/core/BepInEx.Unity.IL2CPP.dll' = 'BepInEx.Unity.IL2CPP.dll'
    'BepInEx/core/0Harmony.dll' = '0Harmony.dll'
}
