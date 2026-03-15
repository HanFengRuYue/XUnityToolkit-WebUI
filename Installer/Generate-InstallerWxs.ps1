function Generate-InstallerWxs {
    param(
        [string]$ReleaseDir,
        [string]$OutputFile
    )

    Write-Host "  Generating $OutputFile from $ReleaseDir..." -ForegroundColor DarkGray

    $outputDir = Split-Path $OutputFile -Parent
    if (-not (Test-Path $outputDir)) {
        New-Item -ItemType Directory -Path $outputDir -Force | Out-Null
    }

    $xml = [System.Xml.XmlDocument]::new()
    $xml.AppendChild($xml.CreateXmlDeclaration("1.0", "UTF-8", $null)) | Out-Null

    $wixNs = "http://wixtoolset.org/schemas/v4/wxs"
    $wix = $xml.CreateElement("Wix", $wixNs)
    $xml.AppendChild($wix) | Out-Null

    $fragment = $xml.CreateElement("Fragment", $wixNs)
    $wix.AppendChild($fragment) | Out-Null

    $compGroup = $xml.CreateElement("ComponentGroup", $wixNs)
    $compGroup.SetAttribute("Id", "HarvestedFiles")
    $compGroup.SetAttribute("Directory", "INSTALLDIR")
    $fragment.AppendChild($compGroup) | Out-Null

    # Scan all files in publish output
    $files = Get-ChildItem -Path $ReleaseDir -File -Recurse
    $counter = 0

    foreach ($file in $files) {
        $relativePath = $file.FullName.Substring($ReleaseDir.TrimEnd('\', '/').Length + 1)

        # Skip data directory if present
        if ($relativePath -like "data\*") { continue }

        $counter++
        $safeId = "f_$counter"

        # Determine directory for subdirectory files
        $dirParts = $relativePath.Split('\')

        if ($dirParts.Length -eq 1) {
            # Root-level file — add directly to component group
            $comp = $xml.CreateElement("Component", $wixNs)
            $comp.SetAttribute("Id", "c_$counter")
            $comp.SetAttribute("Guid", "*")
            $compGroup.AppendChild($comp) | Out-Null

            $fileEl = $xml.CreateElement("File", $wixNs)
            $fileEl.SetAttribute("Id", $safeId)
            $fileEl.SetAttribute("Source", "`$(var.PublishDir)\$relativePath")
            $fileEl.SetAttribute("KeyPath", "yes")
            $comp.AppendChild($fileEl) | Out-Null
        } else {
            # Subdirectory file — create directory ref + component
            $comp = $xml.CreateElement("Component", $wixNs)
            $comp.SetAttribute("Id", "c_$counter")
            $comp.SetAttribute("Guid", "*")
            $comp.SetAttribute("Subdirectory", ($dirParts[0..($dirParts.Length - 2)] -join '\'))
            $compGroup.AppendChild($comp) | Out-Null

            $fileEl = $xml.CreateElement("File", $wixNs)
            $fileEl.SetAttribute("Id", $safeId)
            $fileEl.SetAttribute("Source", "`$(var.PublishDir)\$relativePath")
            $fileEl.SetAttribute("KeyPath", "yes")
            $comp.AppendChild($fileEl) | Out-Null
        }
    }

    $xml.Save($OutputFile)
    Write-Host "  Generated $counter file entries." -ForegroundColor Green
}
