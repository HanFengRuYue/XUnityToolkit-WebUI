# Unify Data Path & Config Import/Export Implementation Plan

> **For agentic workers:** REQUIRED: Use superpowers:subagent-driven-development (if subagents available) or superpowers:executing-plans to implement this plan. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Remove broken portable mode, unify data storage to `%AppData%\XUnityToolkit\`, and add data path display + import/export in settings page.

**Architecture:** Simplify `Program.cs` data root to always use `%AppData%`, remove registry-based mode detection, add 4 new API endpoints (data-path, export, import, open-data-folder), and add a "Data Management" section to SettingsView.vue.

**Tech Stack:** ASP.NET Core (.NET 10), Vue 3 + TypeScript + Naive UI, System.IO.Compression for ZIP

---

## Chunk 1: Backend — Simplify Data Path

### Task 1: Remove Portable Mode from Program.cs

**Files:**
- Modify: `XUnityToolkit-WebUI/Program.cs:23-26,289-300`

- [ ] **Step 1: Simplify data root resolution**

Replace lines 23-26:

```csharp
var appDataRoot = builder.Configuration["AppData:Root"]
    ?? GetRegistryDataPath()
    ?? Path.Combine(AppContext.BaseDirectory, "data");
builder.Configuration["AppData:Root"] = appDataRoot;
```

With:

```csharp
var appDataRoot = builder.Configuration["AppData:Root"]
    ?? Path.Combine(Environment.GetFolderPath(
           Environment.SpecialFolder.ApplicationData), "XUnityToolkit");
builder.Configuration["AppData:Root"] = appDataRoot;
```

- [ ] **Step 2: Delete `GetRegistryDataPath()` method**

Delete lines 289-300 (the entire `static string? GetRegistryDataPath()` method).

- [ ] **Step 3: Remove unused `Microsoft.Win32` usage**

Check if any other code in `Program.cs` uses `Microsoft.Win32.Registry`. If `GetRegistryDataPath` was the only consumer, no import to remove (it used fully qualified name).

- [ ] **Step 4: Verify build**

Run: `dotnet build XUnityToolkit-WebUI/XUnityToolkit-WebUI.csproj -p:SkipFrontendBuild=true`
Expected: Build succeeds

- [ ] **Step 5: Commit**

```bash
git add XUnityToolkit-WebUI/Program.cs
git commit -m "refactor: 移除便携模式，统一数据路径到 %AppData%"
```

### Task 2: Update AppDataPaths Fallback

**Files:**
- Modify: `XUnityToolkit-WebUI/Infrastructure/AppDataPaths.cs:5-6`

- [ ] **Step 1: Update fallback to match Program.cs**

Replace lines 5-6:

```csharp
private readonly string _root = config["AppData:Root"]
    ?? Path.Combine(AppContext.BaseDirectory, "data");
```

With:

```csharp
private readonly string _root = config["AppData:Root"]
    ?? Path.Combine(Environment.GetFolderPath(
           Environment.SpecialFolder.ApplicationData), "XUnityToolkit");
```

- [ ] **Step 2: Verify build**

Run: `dotnet build XUnityToolkit-WebUI/XUnityToolkit-WebUI.csproj -p:SkipFrontendBuild=true`
Expected: Build succeeds

- [ ] **Step 3: Commit**

```bash
git add XUnityToolkit-WebUI/Infrastructure/AppDataPaths.cs
git commit -m "refactor: 更新 AppDataPaths 回退路径，与 Program.cs 一致"
```

### Task 3: Update Updater Portable Mode

**Files:**
- Modify: `Updater/Program.cs:36,354`

- [ ] **Step 1: Update default data directory fallback**

Replace line 36:

```csharp
var effectiveDataDir = dataDir ?? Path.Combine(appDir!, "data");
```

With:

```csharp
var effectiveDataDir = dataDir ?? Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "XUnityToolkit");
```

- [ ] **Step 2: Update portable mode log message**

In `SyncMsiRegistryVersion` (around line 354), replace:

```csharp
log("  No MSI registration found (portable mode). Skipping.");
```

With:

```csharp
log("  No MSI registration found. Skipping.");
```

- [ ] **Step 3: Verify build**

Run: `dotnet build Updater/Updater.csproj`
Expected: Build succeeds

- [ ] **Step 4: Commit**

```bash
git add Updater/Program.cs
git commit -m "refactor: 更新 Updater 默认数据路径，移除便携模式提示"
```

## Chunk 2: Backend — New API Endpoints

### Task 4: Add Data Path, Open Folder, Export, and Import Endpoints

**Files:**
- Modify: `XUnityToolkit-WebUI/Endpoints/SettingsEndpoints.cs`

- [ ] **Step 1: Add required using statements**

At the top of `SettingsEndpoints.cs`, add:

```csharp
using System.IO.Compression;
```

- [ ] **Step 2: Add `GET /api/settings/data-path` endpoint**

Inside `MapSettingsEndpoints`, after the `/version` endpoint (after line 85), add:

```csharp
group.MapGet("/data-path", (AppDataPaths paths) =>
{
    return Results.Ok(ApiResult<DataPathInfo>.Ok(new DataPathInfo(paths.Root)));
});
```

- [ ] **Step 3: Add `POST /api/settings/open-data-folder` endpoint**

```csharp
group.MapPost("/open-data-folder", (AppDataPaths paths) =>
{
    try
    {
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = paths.Root,
            UseShellExecute = true,
        });
        return Results.Ok(ApiResult.Ok());
    }
    catch
    {
        return Results.Ok(ApiResult.Fail("无法打开文件夹"));
    }
});
```

- [ ] **Step 4: Add `POST /api/settings/export` endpoint**

```csharp
group.MapPost("/export", async (AppDataPaths paths) =>
{
    var excludedDirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "models", "llama", "generated-fonts", "logs",
        "update-staging", "update-backup", "update-temp",
        "backups", "font-backups", "custom-fonts",
    };
    // font-generation/temp and font-generation/uploads are excluded separately
    var excludedSubDirs = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase)
    {
        ["font-generation"] = new(StringComparer.OrdinalIgnoreCase) { "temp", "uploads" },
    };

    var memoryStream = new MemoryStream();
    using (var archive = new ZipArchive(memoryStream, ZipArchiveMode.Create, leaveOpen: true))
    {
        var rootPath = Path.GetFullPath(paths.Root);
        foreach (var filePath in Directory.EnumerateFiles(rootPath, "*", SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(rootPath, filePath);
            var parts = relativePath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

            // Skip top-level excluded directories
            if (parts.Length > 1 && excludedDirs.Contains(parts[0]))
                continue;

            // Skip excluded sub-directories (e.g., font-generation/temp)
            if (parts.Length > 2 && excludedSubDirs.TryGetValue(parts[0], out var subExclusions)
                && subExclusions.Contains(parts[1]))
                continue;

            var entry = archive.CreateEntry(relativePath.Replace('\\', '/'), CompressionLevel.Optimal);
            await using var entryStream = entry.Open();
            await using var fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            await fileStream.CopyToAsync(entryStream);
        }
    }

    memoryStream.Position = 0;
    var fileName = $"XUnityToolkit_data_{DateTime.Now:yyyy-MM-dd}.zip";
    return Results.File(memoryStream, "application/zip", fileName);
});
```

- [ ] **Step 5: Add `POST /api/settings/import` endpoint**

```csharp
group.MapPost("/import", async (HttpRequest request, AppDataPaths paths, ILogger<AppSettingsService> logger) =>
{
    if (!request.HasFormContentType)
        return Results.BadRequest(ApiResult.Fail("请求必须是 multipart/form-data"));

    var form = await request.ReadFormAsync();
    var file = form.Files.FirstOrDefault();
    if (file is null || file.Length == 0)
        return Results.BadRequest(ApiResult.Fail("未找到上传的文件"));

    var rootPath = Path.GetFullPath(paths.Root);
    try
    {
        await using var stream = file.OpenReadStream();
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
        foreach (var entry in archive.Entries)
        {
            // Skip directories
            if (string.IsNullOrEmpty(entry.Name))
                continue;

            var destPath = PathSecurity.SafeJoin(rootPath, entry.FullName.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(destPath)!);

            await using var entryStream = entry.Open();
            await using var destStream = new FileStream(destPath, FileMode.Create, FileAccess.Write, FileShare.None);
            await entryStream.CopyToAsync(destStream);
        }

        logger.LogInformation("已导入配置数据");
        return Results.Ok(ApiResult.Ok());
    }
    catch (InvalidOperationException)
    {
        return Results.BadRequest(ApiResult.Fail("文件路径不安全，导入已中止"));
    }
    catch (InvalidDataException)
    {
        return Results.BadRequest(ApiResult.Fail("无效的 ZIP 文件"));
    }
}).DisableAntiforgery();
```

- [ ] **Step 6: Add `DataPathInfo` record at bottom of file**

After the existing `VersionInfo` record (line 94), add:

```csharp
public record DataPathInfo(string Path);
```

- [ ] **Step 7: Verify build**

Run: `dotnet build XUnityToolkit-WebUI/XUnityToolkit-WebUI.csproj -p:SkipFrontendBuild=true`
Expected: Build succeeds

- [ ] **Step 8: Commit**

```bash
git add XUnityToolkit-WebUI/Endpoints/SettingsEndpoints.cs
git commit -m "feat(设置): 添加数据路径查询、打开文件夹、导入导出端点"
```

## Chunk 3: Frontend — API Layer & Types

### Task 5: Add Frontend Types and API Methods

**Files:**
- Modify: `XUnityToolkit-Vue/src/api/types.ts:188`
- Modify: `XUnityToolkit-Vue/src/api/games.ts:186-191`

- [ ] **Step 1: Add `DataPathInfo` type**

In `types.ts`, after `VersionInfo` interface (line 188), add:

```typescript
export interface DataPathInfo {
  path: string
}
```

- [ ] **Step 2: Add settings API methods**

In `games.ts`, expand the `settingsApi` object (lines 186-191). Replace:

```typescript
export const settingsApi = {
  get: () => api.get<AppSettings>('/api/settings'),
  save: (settings: AppSettings) => api.put<AppSettings>('/api/settings', settings),
  getVersion: () => api.get<VersionInfo>('/api/settings/version'),
  reset: () => api.post<{ partial: boolean; errors?: string[] }>('/api/settings/reset'),
}
```

With:

```typescript
export const settingsApi = {
  get: () => api.get<AppSettings>('/api/settings'),
  save: (settings: AppSettings) => api.put<AppSettings>('/api/settings', settings),
  getVersion: () => api.get<VersionInfo>('/api/settings/version'),
  reset: () => api.post<{ partial: boolean; errors?: string[] }>('/api/settings/reset'),
  getDataPath: () => api.get<DataPathInfo>('/api/settings/data-path'),
  openDataFolder: () => api.post('/api/settings/open-data-folder'),
  async exportData(): Promise<void> {
    const res = await fetch('/api/settings/export', { method: 'POST' })
    if (!res.ok) throw new Error('导出失败')
    const blob = await res.blob()
    const url = URL.createObjectURL(blob)
    const a = document.createElement('a')
    a.href = url
    const disposition = res.headers.get('content-disposition')
    a.download = disposition?.match(/filename="?([^";\s]+)"?/)?.[1]
        ?? `XUnityToolkit_data_${new Date().toISOString().slice(0, 10)}.zip`
    document.body.appendChild(a)
    a.click()
    document.body.removeChild(a)
    setTimeout(() => URL.revokeObjectURL(url), 1000)
  },
  async importData(file: File): Promise<void> {
    const formData = new FormData()
    formData.append('file', file)
    const res = await fetch('/api/settings/import', { method: 'POST', body: formData })
    if (!res.ok) {
      const data = await res.json().catch(() => null)
      throw new Error(data?.error ?? '导入失败')
    }
  },
}
```

- [ ] **Step 3: Add `DataPathInfo` import**

In `games.ts`, update the import from `@/api/types` to include `DataPathInfo`:

Find the existing import line that includes `VersionInfo` and add `DataPathInfo` alongside it.

- [ ] **Step 4: Verify type-check**

Run: `cd XUnityToolkit-Vue && npx vue-tsc --noEmit`
Expected: No type errors

- [ ] **Step 5: Commit**

```bash
git add XUnityToolkit-Vue/src/api/types.ts XUnityToolkit-Vue/src/api/games.ts
git commit -m "feat(设置): 添加数据路径、导入导出前端 API 方法"
```

## Chunk 4: Frontend — Settings View UI

### Task 6: Add Data Management Section to SettingsView

**Files:**
- Modify: `XUnityToolkit-Vue/src/views/SettingsView.vue`

- [ ] **Step 1: Add imports**

In the `<script setup>` imports, add:

After the existing material icons import line, add `FolderOpenOutlined`, `FileDownloadOutlined`, `FileUploadOutlined`, `StorageOutlined` to the `@vicons/material` import.

- [ ] **Step 2: Add data management state**

After the `resetLoading` ref (line 149), add:

```typescript
// Data management
const dataPath = ref('')
const exportLoading = ref(false)
const importLoading = ref(false)

async function loadDataPath() {
  try {
    const info = await settingsApi.getDataPath()
    dataPath.value = info.path
  } catch {
    dataPath.value = '(unknown)'
  }
}

async function handleOpenDataFolder() {
  try {
    await settingsApi.openDataFolder()
  } catch {
    message.error('无法打开文件夹')
  }
}

async function handleExport() {
  exportLoading.value = true
  try {
    await settingsApi.exportData()
    message.success('导出成功')
  } catch {
    message.error('导出失败')
  } finally {
    exportLoading.value = false
  }
}

function handleImport() {
  const input = document.createElement('input')
  input.type = 'file'
  input.accept = '.zip'
  input.onchange = async () => {
    const file = input.files?.[0]
    if (!file) return
    dialog.warning({
      title: '导入配置',
      content: '导入将覆盖当前所有配置数据，导入完成后需要重启程序。确定要继续吗？',
      positiveText: '确认导入',
      negativeText: '取消',
      onPositiveClick: async () => {
        importLoading.value = true
        try {
          await settingsApi.importData(file)
          message.success('导入成功，请重启程序以使配置生效')
        } catch (e) {
          message.error(e instanceof Error ? e.message : '导入失败')
        } finally {
          importLoading.value = false
        }
      },
    })
  }
  input.click()
}
```

- [ ] **Step 3: Add `loadDataPath()` to `onMounted`**

Update the `onMounted` callback (line 186-189):

```typescript
onMounted(() => {
  loadSettings()
  loadVersion()
  loadDataPath()
})
```

- [ ] **Step 4: Add Data Management section to template**

In the template, **before** the Danger Zone card (before line 311 `<!-- Danger Zone (full width) -->`), add:

```html
<!-- Data Management -->
<div class="section-card" style="animation-delay: 0.2s">
  <div class="section-header">
    <h2 class="section-title">
      <span class="section-icon">
        <NIcon :size="16"><StorageOutlined /></NIcon>
      </span>
      数据管理
    </h2>
  </div>
  <div class="settings-form">
    <div class="form-row">
      <label class="form-label">
        <NIcon :size="14" color="var(--text-3)"><FolderOpenOutlined /></NIcon>
        配置文件夹路径
      </label>
      <div class="data-path-row">
        <span class="data-path-text">{{ dataPath }}</span>
        <NButton size="small" @click="handleOpenDataFolder">打开文件夹</NButton>
      </div>
      <span class="form-hint">所有应用配置、游戏库、术语表等数据存储在此文件夹中</span>
    </div>
    <div class="data-actions">
      <NButton :loading="exportLoading" @click="handleExport">
        <template #icon><NIcon><FileDownloadOutlined /></NIcon></template>
        导出配置
      </NButton>
      <NButton :loading="importLoading" @click="handleImport">
        <template #icon><NIcon><FileUploadOutlined /></NIcon></template>
        导入配置
      </NButton>
    </div>
    <span class="form-hint">导出不包含 AI 模型文件、生成的字体、日志等大文件。导入会覆盖现有配置。</span>
  </div>
</div>
```

- [ ] **Step 5: Update animation delays for subsequent cards**

Shift the `animation-delay` of subsequent cards:
- Danger Zone: `0.2s` → `0.25s`
- Update: `0.25s` → `0.3s`
- About: `0.3s` → `0.35s`

- [ ] **Step 6: Add CSS styles**

In the `<style scoped>` section, before the `/* ===== Danger Zone ===== */` comment, add:

```css
/* ===== Data Management ===== */
.data-path-row {
  display: flex;
  align-items: center;
  gap: 10px;
}

.data-path-text {
  flex: 1;
  font-size: 13px;
  color: var(--text-2);
  padding: 6px 10px;
  background: var(--bg-subtle);
  border: 1px solid var(--border);
  border-radius: var(--radius-sm);
  word-break: break-all;
  user-select: all;
}

.data-actions {
  display: flex;
  gap: 10px;
}
```

- [ ] **Step 7: Verify type-check and build**

Run: `cd XUnityToolkit-Vue && npx vue-tsc --noEmit && npm run build`
Expected: No errors

- [ ] **Step 8: Commit**

```bash
git add XUnityToolkit-Vue/src/views/SettingsView.vue
git commit -m "feat(设置): 添加数据管理区块（路径显示、导入导出）"
```

## Chunk 5: CLAUDE.md Updates

### Task 7: Update CLAUDE.md Files

**Files:**
- Modify: `CLAUDE.md`
- Modify: `XUnityToolkit-WebUI/CLAUDE.md`

- [ ] **Step 1: Update root CLAUDE.md — Persistence description**

In the Architecture section, replace the Persistence line:

```
- **Persistence:** JSON files in `{programDir}/data/` (`library.json`, `settings.json`) — portable app pattern; API keys encrypted with DPAPI
```

With:

```
- **Persistence:** JSON files in `%AppData%\XUnityToolkit\` (`library.json`, `settings.json`); `AppData:Root` config key allows override for dev/test; API keys encrypted with DPAPI
```

- [ ] **Step 2: Update root CLAUDE.md — Settings endpoints**

In the API Endpoints section, update the Settings line:

```
- **Settings:** `GET/PUT /api/settings`, `GET .../version`, `POST .../reset`
```

To:

```
- **Settings:** `GET/PUT /api/settings`, `GET .../version`, `POST .../reset`, `GET .../data-path`, `POST .../export` (ZIP, **not ApiResult**), `POST .../import` (multipart ZIP), `POST .../open-data-folder`
```

- [ ] **Step 3: Update root CLAUDE.md — Remove portable mode doc**

In the Build & Deploy section, remove the line:

```
- **Installed vs portable mode:** `Program.cs` checks `HKCU\Software\XUnityToolkit\DataPath` registry; present → installed mode (`%AppData%\XUnityToolkit\`); absent → portable mode (`{programDir}/data/`)
```

Also replace ALL remaining `{programDir}/data/` references in root CLAUDE.md:

- Backup/Restore line: `manifests at {programDir}/data/backups/{gameId}.json` → `{dataRoot}/backups/{gameId}.json`
- Font Replacement line: `backups at {programDir}/data/font-backups/{gameId}/; custom fonts at {programDir}/data/custom-fonts/{gameId}/` → `{dataRoot}/font-backups/{gameId}/; custom fonts at {dataRoot}/custom-fonts/{gameId}/`
- Font Generation line: `outputs at {programDir}/data/generated-fonts/` → `{dataRoot}/generated-fonts/`

- [ ] **Step 4: Update root CLAUDE.md — MSI registry keys sync point**

Update the MSI registry keys sync point:

```
- **MSI registry keys:** Written by MSI (`Components.wxs`), read by `Program.cs` (DataPath) and `Updater/Program.cs` (MsiProductCode, InstallDir); key path: `HKCU\Software\XUnityToolkit`
```

To:

```
- **MSI registry keys:** Written by MSI (`Components.wxs`), read by `Updater/Program.cs` (MsiProductCode, InstallDir); `DataPath` key written by MSI for `RemoveFolderEx` cleanup only — app no longer reads it; key path: `HKCU\Software\XUnityToolkit`
```

- [ ] **Step 5: Update root CLAUDE.md — Add DataPathInfo sync point**

Add to the Sync Points section:

```
- **DataPathInfo:** Sync 2 places: `Endpoints/SettingsEndpoints.cs` (anonymous type or record), `src/api/types.ts`
```

- [ ] **Step 6: Update root CLAUDE.md — Data path description**

Add to Build & Deploy section:

```
- **Data path:** Always `%AppData%\XUnityToolkit\` (no portable mode); `AppData:Root` config key allows override for dev/test
```

- [ ] **Step 7: Update root CLAUDE.md — Logs path**

Replace:

```
- Logs: `{programDir}/data/logs/XUnityToolkit_YYYY-MM-DD_HH-mm-ss.log`; 500-entry ring buffer + `LogBroadcast`
```

With:

```
- Logs: `{dataRoot}/logs/XUnityToolkit_YYYY-MM-DD_HH-mm-ss.log`; 500-entry ring buffer + `LogBroadcast`
```

- [ ] **Step 8: Update backend CLAUDE.md**

In `XUnityToolkit-WebUI/CLAUDE.md`, update:

Replace:
```
- **Data storage:** `{programDir}/data/` for all runtime data; `AppDataPaths` centralizes paths; shipped assets (`bundled/`, `wwwroot/`) stay at program root
```

With:
```
- **Data storage:** `%AppData%\XUnityToolkit\` for all runtime data; `AppDataPaths` centralizes paths; shipped assets (`bundled/`, `wwwroot/`) stay at program root
```

Replace:
```
- **Pre-DI paths:** `Program.cs` reads `builder.Configuration["AppData:Root"]` fallback to `{baseDir}/data` before DI — must stay in sync with `AppDataPaths._root` formula
```

With:
```
- **Pre-DI paths:** `Program.cs` reads `builder.Configuration["AppData:Root"]` fallback to `%AppData%\XUnityToolkit\` before DI — must stay in sync with `AppDataPaths._root` formula
```

Also replace in backend CLAUDE.md:
- Local LLM line: `binaries at {programDir}/data/llama/{cuda,vulkan,cpu}/` → `{dataRoot}/llama/{cuda,vulkan,cpu}/`

- [ ] **Step 9: Commit**

```bash
git add CLAUDE.md XUnityToolkit-WebUI/CLAUDE.md
git commit -m "docs: 更新 CLAUDE.md 移除便携模式文档，添加新端点"
```
