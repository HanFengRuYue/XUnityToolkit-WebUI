# MSI Installer Packaging Design

## Overview

Add MSI installer packaging to XUnityToolkit-WebUI using WiX v5 as a standalone project. The MSI provides a standard Windows installation experience with Add/Remove Programs integration, while coexisting with the current portable ZIP distribution and Updater.exe delta update system.

## Goals

- Standard "Next → Next → Install" user experience
- Appear in Windows "Add or Remove Programs" with accurate version info
- Start menu + desktop shortcuts
- Clean uninstall (including runtime data)
- Coexist with portable ZIP mode and Updater.exe updates

## Non-Goals

- Enterprise deployment (GPO/SCCM silent install)
- MSIX/Store distribution
- First-run download of bundled assets (all bundled into MSI)

## Project Structure

```
Installer/
├── Installer.wixproj          # WiX v5 project file
├── Package.wxs                # MSI main definition (Product, MajorUpgrade, Feature)
├── Directories.wxs            # Installation directory structure
├── Components.wxs             # Core components (EXE, shortcuts, registry)
├── Shortcuts.wxs              # Start menu + desktop shortcuts
├── CleanupActions.wxs         # Custom action to remove %AppData% on uninstall
└── HarvestTransform.xslt      # Optional: XSLT to filter harvested files
```

- `HarvestedFiles.wxs` is generated at build time by `wix heat` and gitignored.
- `.config/dotnet-tools.json` registers `wix` as a local dotnet tool.

## Installation Directory

```
[ProgramFiles]\XUnityToolkit-WebUI\        ← INSTALLDIR
├── XUnityToolkit-WebUI.exe
├── Updater.exe
├── appsettings.json
├── *.dll (runtime + dependencies)
├── wwwroot\                               ← Frontend assets
└── bundled\                               ← BepInEx/XUnity/llama
    ├── bepinex5\
    ├── bepinex6\
    ├── xunity\
    ├── llama\
    └── fonts\
```

## Data Directory Redirection

Runtime data (`library.json`, `settings.json`, logs, backups, etc.) is stored outside the install directory:

- **Installed mode:** `%AppData%\XUnityToolkit\` (written to registry by MSI)
- **Portable mode:** `{programDir}/data/` (existing behavior, no registry key)

### Detection Logic

At startup, the application checks `HKCU\Software\XUnityToolkit\DataPath`:

```csharp
public static string GetDataDirectory()
{
    using var key = Registry.CurrentUser.OpenSubKey(@"Software\XUnityToolkit");
    var dataPath = key?.GetValue("DataPath") as string;
    if (!string.IsNullOrEmpty(dataPath))
        return dataPath;
    return Path.Combine(AppContext.BaseDirectory, "data");
}
```

All existing code referencing the `data/` directory must be refactored to use this method.

### Registry Keys (written by MSI)

| Key | Value | Purpose |
|-----|-------|---------|
| `HKCU\Software\XUnityToolkit\DataPath` | `%AppData%\XUnityToolkit` | Data directory for installed mode |
| `HKCU\Software\XUnityToolkit\MsiProductCode` | `{GUID}` | For Updater.exe to locate Uninstall registry entry |

## File Harvesting

The publish output contains thousands of files (DLLs + bundled assets). Manual declaration is impractical.

1. `build.ps1` runs `wix heat dir Release/{rid}/ -o Installer/HarvestedFiles.wxs` after publish
2. `HarvestedFiles.wxs` is gitignored (regenerated each build)
3. Optional XSLT transform excludes unwanted files (e.g., `data/` directory)
4. `Package.wxs` references the harvested `ComponentGroup`

## Versioning

### Problem

MSI `ProductVersion` requires `major.minor.build` (three segments, each ≤ 65535). The existing timestamp format `1.0.{YYYYMMDDHHmm}` overflows the build segment.

### Solution

Map the timestamp to a three-segment version: `{YYYY-2024}.{MMDD}.{HHmm}`

Examples:
- `2026-03-15 12:00` → `2.0315.1200`
- `2026-12-31 23:59` → `2.1231.2359`
- `2028-06-01 08:30` → `4.0601.0830`

All segments stay within 0–65535. `build.ps1` generates this version and passes it to WiX via `-p:MsiVersion=`.

## Upgrade Strategy

### MajorUpgrade

- Fixed `UpgradeCode` GUID (never changes, identifies "same product")
- New `ProductCode` GUID generated per release
- `MajorUpgrade` with `AllowSameVersionUpgrades="yes"` + `DowngradeErrorMessage`
- Running a new MSI automatically uninstalls the old version, then installs the new one

### Updater.exe Registry Sync

After replacing files, Updater.exe updates the MSI registration so Add/Remove Programs shows the correct version:

1. Read `HKCU\Software\XUnityToolkit\MsiProductCode` to get the ProductCode
2. Write to `HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\{ProductCode}`:
   - `DisplayVersion` → new version string
   - `InstallDate` → current date (YYYYMMDD)
3. Failure is non-critical: log only, do not abort the update

### Behavior Matrix

| Scenario | Program Files | AppData Data | Registry |
|----------|--------------|-------------|----------|
| MSI upgrade (new MSI) | Replaced | Preserved | Updated by MSI |
| MSI uninstall | Deleted | Deleted | Deleted |
| Updater.exe update | Replaced | Preserved | DisplayVersion updated |

## Shortcuts

### Start Menu

```
Start Menu\Programs\XUnityToolkit-WebUI\
├── XUnityToolkit-WebUI.lnk          → Main executable
└── Uninstall XUnityToolkit-WebUI.lnk → msiexec /x {ProductCode}
```

### Desktop

- `%PUBLIC%\Desktop\XUnityToolkit-WebUI.lnk` → Main executable
- Created on install, removed on uninstall

## Uninstall Cleanup

MSI only removes files it installed. Runtime-generated data in `%AppData%\XUnityToolkit\` requires a Custom Action:

- Type: Quiet execution of `cmd /c rmdir /s /q "%AppData%\XUnityToolkit"`
- Schedule: After `InstallFinalize`
- Condition: `REMOVE="ALL" AND NOT UPGRADINGPRODUCTCODE`
  - Full uninstall → cleanup runs
  - MajorUpgrade → `UPGRADINGPRODUCTCODE` is set → cleanup skipped, data preserved
- Also removes `HKCU\Software\XUnityToolkit` registry key

## Build Integration

### build.ps1

Append two steps after the existing publish flow:

```
Existing: Download → Frontend → TranslatorEndpoint → Updater → Publish
New:      → Heat harvest → WiX build MSI
```

1. `wix heat dir Release/{rid}/ ...` → generates `HarvestedFiles.wxs`
2. `dotnet build Installer/Installer.wixproj -p:RuntimeId={rid} -p:MsiVersion={version}`
3. Output: `Release/XUnityToolkit-WebUI-{rid}.msi`

### CI (build.yml)

- Add MSI build step after publish
- Upload MSI as additional release asset alongside existing ZIPs
- Release assets become: ZIP (portable) + MSI (installer) per architecture

### dotnet-tools.json

```json
{
  "version": 1,
  "isRoot": true,
  "tools": {
    "wix": {
      "version": "5.0.2",
      "commands": ["wix"]
    }
  }
}
```

## Code Changes Summary

### Main Application

- Add `GetDataDirectory()` helper (registry check → fallback to portable path)
- Refactor all `data/` path references to use `GetDataDirectory()`
- No other behavioral changes

### Updater.exe

- After file replacement, read `MsiProductCode` from registry
- Update `DisplayVersion` and `InstallDate` in Uninstall registry key
- Non-critical: log failures, do not abort

### build.ps1

- Generate MSI version from timestamp
- Call `wix heat` to harvest publish output
- Call `dotnet build Installer/Installer.wixproj` to produce MSI
- Copy MSI to `Release/` output

### CI (build.yml)

- Add `dotnet tool restore` step
- Add MSI build step after publish
- Upload MSI as release asset

## Distribution Model

After implementation, each release provides:

| Asset | Description |
|-------|-------------|
| `XUnityToolkit-WebUI-win-x64.zip` | Portable version (existing) |
| `XUnityToolkit-WebUI-win-arm64.zip` | Portable version (existing) |
| `XUnityToolkit-WebUI-win-x64.msi` | Installer version (new) |
| `XUnityToolkit-WebUI-win-arm64.msi` | Installer version (new) |
| `manifest-win-x64.json` | Differential update manifest (existing) |
| `manifest-win-arm64.json` | Differential update manifest (existing) |
| Component ZIPs | For differential updates (existing) |
