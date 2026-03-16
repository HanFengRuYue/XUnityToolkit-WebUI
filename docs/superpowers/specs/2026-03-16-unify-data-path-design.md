# Unify Data Path & Add Config Import/Export

## Problem

Both installed and portable versions save all config files to the user data folder (`%AppData%\XUnityToolkit\`). The portable mode (saving to `{programDir}/data/`) has been broken for an unknown number of versions. Since user data folder avoids permission issues, we unify on it and remove all portable mode code.

## Decisions

- **Data path**: Always `%AppData%\XUnityToolkit\` (via `Environment.SpecialFolder.ApplicationData`)
- **Configuration override**: `AppData:Root` config key preserved for dev/test override
- **Export scope**: Config + cache data — see exclusion list below
- **Import behavior**: Direct overwrite, no merge, no backup, no size limit
- **Post-import**: User prompted to restart the program
- **DPAPI caveat**: API keys encrypted with DPAPI (`CurrentUser` scope) will fail to decrypt on a different machine/user — existing `.bak` fallback handles this gracefully; UI does not warn (acceptable UX)

## Backend Changes

### 1. Program.cs — Simplify Data Path Resolution

Remove `GetRegistryDataPath()` method and the three-level fallback. Replace with:

```csharp
var appDataRoot = builder.Configuration["AppData:Root"]
    ?? Path.Combine(Environment.GetFolderPath(
           Environment.SpecialFolder.ApplicationData), "XUnityToolkit");
builder.Configuration["AppData:Root"] = appDataRoot;
```

### 2. AppDataPaths.cs — Remove Portable Fallback

Update the `_root` fallback to match `Program.cs`:

```csharp
private readonly string _root = config["AppData:Root"]
    ?? Path.Combine(Environment.GetFolderPath(
           Environment.SpecialFolder.ApplicationData), "XUnityToolkit");
```

This keeps them consistent. The fallback is dead code (Program.cs always sets the config key) but ensures safety.

### 3. MSI Registry Key

MSI continues to write `HKCU\Software\XUnityToolkit\DataPath` — needed by `RemoveFolderEx` for uninstall cleanup. The app simply no longer reads it.

### 4. Updater/Program.cs

- Remove portable mode detection log message and the conditional skip of MSI registry sync.
- Update the default `effectiveDataDir` fallback from `Path.Combine(appDir!, "data")` to `Path.Combine(Environment.GetFolderPath(ApplicationData), "XUnityToolkit")`.
- The registry read for `DisplayVersion` update remains (syncing MSI uninstall metadata).

### 5. New API Endpoints

| Method | Path | Description |
|--------|------|-------------|
| `GET` | `/api/settings/data-path` | Returns `ApiResult<DataPathInfo>` |
| `POST` | `/api/settings/export` | ZIP download (**not ApiResult** — raw file response) |
| `POST` | `/api/settings/import` | Multipart ZIP upload, extract to data folder, returns `ApiResult` |
| `POST` | `/api/settings/open-data-folder` | Opens Explorer at data folder, returns `ApiResult` |

#### Export Exclusions

| Directory | Reason |
|-----------|--------|
| `models/` | LLM model files (multi-GB) |
| `llama/` | llama-server binaries (extracted from bundled) |
| `generated-fonts/` | Regeneratable font files |
| `font-generation/temp/` | Temporary SDF bitmaps |
| `font-generation/uploads/` | User-uploaded font files (re-uploadable) |
| `font-backups/` | Original game asset copies (large, machine-specific) |
| `custom-fonts/` | User-uploaded font bundles (re-uploadable) |
| `backups/` | Uninstall manifests with absolute paths (machine-specific) |
| `logs/` | Log files |
| `update-staging/` | Update staging files |
| `update-backup/` | Update backup files |
| `update-temp/` | Update temp files |

#### Export File Name

`XUnityToolkit_data_{yyyy-MM-dd}.zip`

#### Import Behavior

1. Receive multipart ZIP upload
2. Extract to `AppDataPaths.Root`, overwriting existing files
3. Return success response prompting restart

#### Import Security

- `PathSecurity.SafeJoin` for all extracted paths (prevent path traversal)
- No file size limit on ZIP

#### Open Data Folder

Mirrors existing `POST /api/games/{id}/open-folder` pattern: `Process.Start` with `UseShellExecute = true`.

## Frontend Changes

### SettingsView.vue — New "Data Management" Section

Placed **before** the "Danger Zone" section. Contains:

1. **Config folder path**: Read-only text displaying `AppDataPaths.Root` with an "Open Folder" button
2. **Export**: Button that triggers `POST /api/settings/export` and downloads the ZIP
3. **Import**: Button that opens file picker for ZIP, uploads via `POST /api/settings/import`, shows restart prompt on success

### API Types

```typescript
// src/api/types.ts
interface DataPathInfo {
  path: string
}
```

### Settings API

```typescript
// src/api/settings.ts
getDataPath(): Promise<DataPathInfo>
exportData(): Promise<Blob>  // raw fetch, not api helper
importData(file: File): Promise<void>
openDataFolder(): Promise<void>
```

Note: `exportData()` must use raw `fetch` since the response is a `Blob`, not `ApiResult<T>`.

## CLAUDE.md Updates

- Remove all references to portable app pattern
- Remove installed vs portable mode documentation
- Remove `GetRegistryDataPath` and registry-based mode detection docs
- Update data path description to state it always uses `%AppData%\XUnityToolkit\`
- Add new endpoints to API list
- Add sync point for `DataPathInfo` type
- Update `AppDataPaths` documentation
