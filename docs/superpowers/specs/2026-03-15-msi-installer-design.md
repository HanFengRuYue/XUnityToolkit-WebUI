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

## Size Constraint

MSI format has a 2GB file size limit (32-bit offsets). Current publish output is ~991MB uncompressed (bundled ~847MB). With MSI CAB compression, the final `.msi` is expected to be ~500–700MB, well within the limit. If bundled assets grow beyond ~1.5GB uncompressed in the future, a WiX Burn bootstrapper (`.exe` wrapper, no size limit) should be considered.

## Installation Identity: Per-User

The MSI uses **per-user installation** (`InstallScope="perUser"`):

- Installs to `%LocalAppData%\Programs\XUnityToolkit-WebUI\` (no UAC elevation required)
- All registry keys under `HKCU` (Updater.exe can write without elevation)
- Uninstall entry at `HKCU\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\{ProductCode}`
- Data at `%AppData%\XUnityToolkit\`
- No cross-user visibility — each user installs independently

This avoids UAC prompts, Program Files write-protection issues, and the HKLM elevation requirement for Updater.exe registry sync.

## Project Structure

```
Installer/
├── Installer.wixproj          # WiX v5 project file
├── Package.wxs                # MSI main definition (Package, MajorUpgrade, Feature)
├── Directories.wxs            # Installation directory structure
├── Components.wxs             # Core components (EXE, registry keys)
├── Shortcuts.wxs              # Start menu + desktop shortcuts
└── CleanupActions.wxs         # Deferred CA to remove %AppData% on uninstall
```

- File harvesting uses WiX v5 `HeatDirectory` MSBuild item in `.wixproj` (not the legacy `heat.exe` CLI).
- `.config/dotnet-tools.json` registers `wix` as a local dotnet tool.

## Installation Directory

```
%LocalAppData%\Programs\XUnityToolkit-WebUI\   ← INSTALLDIR
├── XUnityToolkit-WebUI.exe
├── Updater.exe
├── appsettings.json
├── *.dll (runtime + dependencies)
├── wwwroot\                                    ← Frontend assets
└── bundled\                                    ← BepInEx/XUnity/llama
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

The existing `AppDataPaths` class already centralizes data paths via `IConfiguration["AppData:Root"]`. The MSI detection logic feeds into this existing mechanism — no need to refactor all path references individually.

### Registry Keys (written by MSI)

| Key | Value | Purpose |
|-----|-------|---------|
| `HKCU\Software\XUnityToolkit\DataPath` | `%AppData%\XUnityToolkit` | Data directory for installed mode |
| `HKCU\Software\XUnityToolkit\MsiProductCode` | `{GUID}` | For Updater.exe to locate Uninstall registry entry |

## File Harvesting

The publish output contains thousands of files (DLLs + bundled assets). Manual declaration is impractical.

WiX v5 approach using `HeatDirectory` MSBuild item in `Installer.wixproj`:

```xml
<ItemGroup>
  <HeatDirectory Include="$(PublishDir)"
                 ComponentGroupName="PublishedFiles"
                 DirectoryRefId="INSTALLDIR"
                 SuppressRootDirectory="true"
                 AutoGenerateGuids="true" />
</ItemGroup>
```

This automatically harvests all files from the publish output at build time. No separate CLI step or XSLT transform needed.

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

Note: MSI `ProductVersion` and application `InformationalVersion` are intentionally different formats. `ProductVersion` is for MSI upgrade logic only; `InformationalVersion` (e.g., `1.3.202603151200`) is displayed in-app and in Updater.exe. Both are set in `build.ps1`.

## Upgrade Strategy

### MajorUpgrade

- Fixed `UpgradeCode` GUID (never changes, identifies "same product")
- New `ProductCode` GUID generated per release
- `MajorUpgrade` with `AllowSameVersionUpgrades="yes"` + `DowngradeErrorMessage`
  - `AllowSameVersionUpgrades` enables repair scenarios (re-running same MSI)
- Running a new MSI automatically uninstalls the old version, then installs the new one

### Updater.exe Registry Sync

After replacing files, Updater.exe updates the MSI registration so Add/Remove Programs shows the correct version:

1. Read `HKCU\Software\XUnityToolkit\MsiProductCode` to get the ProductCode
2. Write to `HKCU\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\{ProductCode}`:
   - `DisplayVersion` → new version string
   - `InstallDate` → current date (YYYYMMDD)
3. Failure is non-critical: log only, do not abort the update

Since the install is per-user, all registry writes are under HKCU — no elevation required.

### Behavior Matrix

| Scenario | Install Dir | AppData Data | Registry |
|----------|------------|-------------|----------|
| MSI upgrade (new MSI) | Replaced | Preserved | Updated by MSI |
| MSI uninstall | Deleted | Deleted | Deleted |
| Updater.exe update | Replaced | Preserved | DisplayVersion updated |

## Shortcuts

### Start Menu

```
Start Menu\Programs\XUnityToolkit-WebUI\
└── XUnityToolkit-WebUI.lnk          → Main executable
```

No uninstall shortcut — users uninstall via "Add or Remove Programs" (Windows 10+ standard).

### Desktop

- `%USERPROFILE%\Desktop\XUnityToolkit-WebUI.lnk` → Main executable (per-user desktop)
- Created on install, removed on uninstall

## Uninstall Cleanup

MSI only removes files it installed. Runtime-generated data in `%AppData%\XUnityToolkit\` requires a Custom Action:

- Type: Deferred, quiet execution custom action
- Data: Actual path passed via `CustomActionData` using MSI `[AppDataFolder]` property (not `%AppData%` environment variable — avoids expansion issues in SYSTEM context)
- Schedule: **Before** `InstallFinalize` (deferred CAs must run within the `InstallInitialize`–`InstallFinalize` transaction boundary)
- Condition: `REMOVE="ALL" AND NOT UPGRADINGPRODUCTCODE`
  - Full uninstall → cleanup runs
  - MajorUpgrade → `UPGRADINGPRODUCTCODE` is set → cleanup skipped, data preserved
- Also removes `HKCU\Software\XUnityToolkit` registry key (via standard WiX `RegistryKey` with `ForceDeleteOnUninstall`)

## ARM64 Considerations

- WiX produces a platform-specific MSI per RID (`win-x64`, `win-arm64`)
- The `cmd /c rmdir` cleanup custom action works on both platforms (shell command, no binary dependency)
- Updater.exe is win-x64 only (runs on ARM64 via x64 emulation) — this is an existing constraint, not introduced by MSI packaging
- WiX `Platform` property must be set explicitly: `x64` for win-x64, `arm64` for win-arm64

## Build Integration

### build.ps1

Append MSI build step after the existing publish flow:

```
Existing: Download → Frontend → TranslatorEndpoint → Updater → Publish
New:      → WiX build MSI
```

1. Generate MSI version: `{YYYY-2024}.{MMDD}.{HHmm}` from timestamp
2. `dotnet build Installer/Installer.wixproj -p:PublishDir=Release/{rid}/ -p:MsiVersion={version} -p:Platform={arch}`
3. WiX `HeatDirectory` auto-harvests files from `PublishDir`
4. Output: `Release/XUnityToolkit-WebUI-{rid}.msi`

### CI (build.yml)

- Add `dotnet tool restore` step (installs `wix` tool)
- Add MSI build step after publish (per RID, in matrix)
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

Version is pinned for build reproducibility. Update intentionally via `dotnet tool update wix`.

## Code Changes Summary

### Main Application

- Modify `Program.cs` pre-DI path resolution: check `HKCU\Software\XUnityToolkit\DataPath` registry key, set `AppData:Root` in configuration if present
- The existing `AppDataPaths` class already reads `IConfiguration["AppData:Root"]` — no further changes needed for data path centralization
- No other behavioral changes

### Updater.exe

- Add `--data-dir` CLI parameter (passed by main app when in installed mode)
- Use `--data-dir` for log/backup/error paths instead of hardcoded `Path.Combine(appDir, "data", ...)`
- Portable mode: main app passes `--data-dir {appDir}/data` (same as current behavior)
- After file replacement, read `MsiProductCode` from registry and update `DisplayVersion`/`InstallDate` in HKCU Uninstall key
- Registry operations use Win32 P/Invoke (AOT-compatible, no `Microsoft.Win32.Registry` dependency)

### build.ps1

- Generate MSI version from timestamp
- Call `dotnet build Installer/Installer.wixproj` with appropriate properties
- Copy MSI to `Release/` output

### CI (build.yml)

- Add `dotnet tool restore` step
- Add MSI build step after publish
- Upload MSI as release asset

## Code Signing

The MSI will not be code-signed initially. Unsigned MSI installers trigger Windows SmartScreen warnings ("Unknown publisher"). This is a known limitation shared with the current portable ZIP distribution. Code signing can be added in the future by passing a certificate to WiX's `SignTool` integration.

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
