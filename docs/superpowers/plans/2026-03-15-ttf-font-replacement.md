# TTF Font Replacement Implementation Plan

> **For agentic workers:** REQUIRED: Use superpowers:subagent-driven-development (if subagents available) or superpowers:executing-plans to implement this plan. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Extend the font replacement feature to detect and replace Unity `Font` assets (TTF/OTF binary data) alongside existing TMP_FontAsset replacement.

**Architecture:** Extend `FontReplacementService` to scan `AssetClassID.Font` assets and replace their `m_FontData` byte arrays. Unify the data model (`TmpFontInfo` → `FontInfo`) across backend, frontend types, and UI. Shared backup/restore mechanism, no new API endpoints.

**Tech Stack:** C# / ASP.NET Core / AssetsTools.NET (backend), Vue 3 / TypeScript / Naive UI (frontend)

---

## File Structure

| File | Action | Responsibility |
|------|--------|----------------|
| `XUnityToolkit-WebUI/Models/FontReplacement.cs` | Modify | Rename `TmpFontInfo` → `FontInfo`, add `FontType`/`FontDataSize`; add `FontType` to `ReplacedFontEntry` |
| `XUnityToolkit-WebUI/Services/FontReplacementService.cs` | Modify | Add TTF scanning in `ScanAssetsInstance`, add `ReplaceSingleTtfFont`, update `ReplaceFontsAsync` for dual-source, update `ValidateCustomFont`, update `GetStatusAsync` |
| `XUnityToolkit-WebUI/Endpoints/FontReplacementEndpoints.cs` | Modify | Update return types, extend upload validation |
| `XUnityToolkit-Vue/src/api/types.ts` | Modify | Rename `TmpFontInfo` → `FontInfo`, add `fontType`/`fontDataSize` |
| `XUnityToolkit-Vue/src/views/FontReplacementView.vue` | Modify | Update table columns, type refs, user-facing text |

---

## Chunk 1: Backend Data Model & Scanning

### Task 1: Update data models

**Files:**
- Modify: `XUnityToolkit-WebUI/Models/FontReplacement.cs`

- [ ] **Step 1: Rename `TmpFontInfo` to `FontInfo` and add new fields**

```csharp
public record FontInfo
{
    public required string Name { get; init; }
    public required long PathId { get; init; }
    public required string AssetFile { get; init; }
    public required bool IsInBundle { get; init; }
    public required string FontType { get; init; }    // "TMP" | "TTF"
    public bool IsSupported { get; init; }
    // TMP-specific (0 for TTF)
    public int AtlasCount { get; init; }
    public int GlyphCount { get; init; }
    public int CharacterCount { get; init; }
    public int AtlasWidth { get; init; }
    public int AtlasHeight { get; init; }
    // TTF-specific (0 for TMP)
    public long FontDataSize { get; init; }
}
```

- [ ] **Step 2: Add `FontType` to `ReplacedFontEntry`**

```csharp
public record ReplacedFontEntry
{
    public required string Name { get; init; }
    public required long PathId { get; init; }
    public string FontType { get; init; } = "TMP";
}
```

- [ ] **Step 3: Update `FontReplacementStatus.ReplacedFonts` type**

Change `List<TmpFontInfo>` → `List<FontInfo>` in the `FontReplacementStatus` record.

- [ ] **Step 4: Build to verify compilation**

Run: `dotnet build XUnityToolkit-WebUI/XUnityToolkit-WebUI.csproj -p:SkipFrontendBuild=true`

This will produce errors in `FontReplacementService.cs` and `FontReplacementEndpoints.cs` where `TmpFontInfo` is referenced — that's expected, we fix them in subsequent tasks.

- [ ] **Step 5: Commit**

```bash
git add XUnityToolkit-WebUI/Models/FontReplacement.cs
git commit -m "refactor: 重命名 TmpFontInfo 为 FontInfo，添加 FontType 和 FontDataSize 字段"
```

### Task 2: Update FontReplacementService — rename references and add TTF scanning

**Files:**
- Modify: `XUnityToolkit-WebUI/Services/FontReplacementService.cs`

- [ ] **Step 1: Update `ScanFontsAsync` return type and log message**

Change line 28: `Task<List<TmpFontInfo>>` → `Task<List<FontInfo>>`
Change line 65: `new List<TmpFontInfo>()` → `new List<FontInfo>()`
Change line 161: `"找到 {Count} 个 TMP 字体"` → `"找到 {Count} 个字体"`

- [ ] **Step 2: Update `ScanAssetsInstance` return type and variable type**

Change line 166: `private List<TmpFontInfo> ScanAssetsInstance(` → `private List<FontInfo> ScanAssetsInstance(`
Change line 170: `var results = new List<TmpFontInfo>();` → `var results = new List<FontInfo>();`

- [ ] **Step 3: Update TMP font `TmpFontInfo` construction to `FontInfo` with `FontType = "TMP"`**

Change lines 206-218 — the `results.Add(new TmpFontInfo { ... })` block for v1.1.0:
```csharp
results.Add(new FontInfo
{
    Name = name,
    PathId = mbInfo.PathId,
    AssetFile = fileName,
    IsInBundle = isBundle,
    FontType = "TMP",
    AtlasCount = atlasTextures.IsDummy ? 0 : atlasTextures["Array"].Children.Count,
    GlyphCount = glyphTableField["Array"].Children.Count,
    CharacterCount = charTable.IsDummy ? 0 : charTable["Array"].Children.Count,
    AtlasWidth = mbBase["m_AtlasWidth"].IsDummy ? 0 : mbBase["m_AtlasWidth"].AsInt,
    AtlasHeight = mbBase["m_AtlasHeight"].IsDummy ? 0 : mbBase["m_AtlasHeight"].AsInt,
    IsSupported = version == "1.1.0"
});
```

Change lines 228-236 — the `results.Add(new TmpFontInfo { ... })` block for v1.0.0:
```csharp
results.Add(new FontInfo
{
    Name = name,
    PathId = mbInfo.PathId,
    AssetFile = fileName,
    IsInBundle = isBundle,
    FontType = "TMP",
    GlyphCount = glyphInfoList["Array"].Children.Count,
    IsSupported = false
});
```

- [ ] **Step 4: Add TTF font scanning after the MonoBehaviour loop in `ScanAssetsInstance`**

After the `foreach (var mbInfo in afile.GetAssetsOfType(AssetClassID.MonoBehaviour))` loop (after line 243), add:

```csharp
// Scan for Unity Font assets (TTF/OTF)
foreach (var fontAssetInfo in afile.GetAssetsOfType(AssetClassID.Font))
{
    try
    {
        var fontBase = manager.GetBaseField(afileInst, fontAssetInfo);
        if (fontBase.IsDummy) continue;

        var fontData = fontBase["m_FontData"];
        if (fontData.IsDummy) continue;

        var dataBytes = fontData.AsByteArray;
        if (dataBytes.Length < 1024) continue; // Skip placeholder/stub fonts

        var name = fontBase["m_Name"].IsDummy ? "(unnamed)" : fontBase["m_Name"].AsString;

        results.Add(new FontInfo
        {
            Name = name,
            PathId = fontAssetInfo.PathId,
            AssetFile = fileName,
            IsInBundle = isBundle,
            FontType = "TTF",
            IsSupported = true,
            FontDataSize = dataBytes.Length
        });
    }
    catch
    {
        // Font deserialization can fail — skip silently
    }
}
```

- [ ] **Step 5: Update `GetStatusAsync` to use `FontInfo` and populate `FontType` from manifest**

In `GetStatusAsync` (around line 1390-1400), change:
```csharp
var replacedFonts = manifest.ReplacedFiles
    .SelectMany(f => f.ReplacedFonts)
    .Select(rf => new FontInfo
    {
        Name = rf.Name,
        PathId = rf.PathId,
        AssetFile = "",
        IsInBundle = false,
        FontType = rf.FontType ?? "TMP",
        IsSupported = true
    })
    .ToList();
```

- [ ] **Step 6: Build to verify**

Run: `dotnet build XUnityToolkit-WebUI/XUnityToolkit-WebUI.csproj -p:SkipFrontendBuild=true`

Expected: may still have errors in `FontReplacementEndpoints.cs` — fixed in Task 3.

- [ ] **Step 7: Commit**

```bash
git add XUnityToolkit-WebUI/Services/FontReplacementService.cs
git commit -m "feat: 添加 TTF 字体扫描支持，统一 FontInfo 数据模型"
```

### Task 3: Update endpoints — return types and upload validation

**Files:**
- Modify: `XUnityToolkit-WebUI/Endpoints/FontReplacementEndpoints.cs`

- [ ] **Step 1: Update scan endpoint return type**

Change line 28: `ApiResult<List<TmpFontInfo>>.Ok(fonts)` → `ApiResult<List<FontInfo>>.Ok(fonts)`

- [ ] **Step 2: Update upload endpoint to accept TTF/OTF files**

Replace the upload validation logic (lines 98-113). Currently it deletes all existing files then validates as AssetBundle. New logic:

```csharp
var customDir = appDataPaths.GetCustomFontDirectory(id);
Directory.CreateDirectory(customDir);

var safeFileName = Path.GetFileName(file.FileName);
var extension = Path.GetExtension(safeFileName).ToLowerInvariant();
var destPath = Path.Combine(customDir, safeFileName);

// Read first 4 bytes for magic detection
byte[] magic = new byte[4];
using (var peekStream = file.OpenReadStream())
    await peekStream.ReadExactlyAsync(magic);

bool isTtfOrOtf = (magic[0] == 0x00 && magic[1] == 0x01 && magic[2] == 0x00 && magic[3] == 0x00) // TTF
                || (magic[0] == 0x4F && magic[1] == 0x54 && magic[2] == 0x54 && magic[3] == 0x4F); // OTF "OTTO"

if (isTtfOrOtf)
{
    // Delete existing TTF/OTF custom fonts only (preserve AssetBundle custom fonts)
    foreach (var existing in Directory.GetFiles(customDir))
    {
        var ext = Path.GetExtension(existing).ToLowerInvariant();
        if (ext is ".ttf" or ".otf")
            File.Delete(existing);
    }

    await using var stream = File.Create(destPath);
    await file.CopyToAsync(stream);
    // TTF validated by magic bytes + minimum size (already checked > 0 above)
}
else
{
    // AssetBundle: delete existing AssetBundle custom fonts only
    foreach (var existing in Directory.GetFiles(customDir))
    {
        var ext = Path.GetExtension(existing).ToLowerInvariant();
        if (ext is not ".ttf" and not ".otf")
            File.Delete(existing);
    }

    await using var stream = File.Create(destPath);
    await file.CopyToAsync(stream);
    stream.Close();

    if (!fontReplacementService.ValidateCustomFont(destPath))
    {
        File.Delete(destPath);
        return Results.BadRequest(ApiResult.Fail("无效的字体文件：未找到 TMP_FontAsset。"));
    }
}

return Results.Ok(ApiResult.Ok());
```

- [ ] **Step 3: Build backend**

Run: `dotnet build XUnityToolkit-WebUI/XUnityToolkit-WebUI.csproj -p:SkipFrontendBuild=true`

Expected: SUCCESS — all `TmpFontInfo` references now updated.

- [ ] **Step 4: Commit**

```bash
git add XUnityToolkit-WebUI/Endpoints/FontReplacementEndpoints.cs
git commit -m "feat: 更新字体替换端点支持 TTF/OTF 上传和统一返回类型"
```

---

## Chunk 2: Backend Replacement Logic

### Task 4: Add TTF replacement logic to FontReplacementService

**Files:**
- Modify: `XUnityToolkit-WebUI/Services/FontReplacementService.cs`

- [ ] **Step 1: Restructure `ReplaceFontsAsync` source resolution for dual TMP+TTF support**

The current method (line 295-549) always resolves a TMP AssetBundle source font. We need to restructure this into two independent source resolution paths. **Replace the entire font source resolution block (lines 300-314)** with:

```csharp
// === Resolve TMP source (AssetBundle) ===
// Filter custom dir to only pick AssetBundle files (not TTF/OTF)
string? fontFile = customFontPath;
if (fontFile is null)
{
    var customDir = appDataPaths.GetCustomFontDirectory(gameId);
    if (Directory.Exists(customDir))
    {
        var customBundle = Directory.GetFiles(customDir)
            .FirstOrDefault(f =>
            {
                var ext = Path.GetExtension(f).ToLowerInvariant();
                return ext is not ".ttf" and not ".otf";
            });
        if (customBundle != null)
            fontFile = customBundle;
    }
}
fontFile ??= tmpFontService.ResolveFontFile(gameInfo.UnityVersion);
// fontFile may be null if no TMP bundles available — OK if only TTF fonts selected

// === Resolve TTF source (raw bytes) ===
byte[]? ttfSourceBytes = null;
string? ttfSourceName = null;
{
    // Priority 1: custom TTF/OTF in custom-fonts directory
    var customDir = appDataPaths.GetCustomFontDirectory(gameId);
    if (Directory.Exists(customDir))
    {
        var customTtf = Directory.GetFiles(customDir)
            .FirstOrDefault(f =>
            {
                var ext = Path.GetExtension(f).ToLowerInvariant();
                return ext is ".ttf" or ".otf";
            });
        if (customTtf != null)
        {
            ttfSourceBytes = File.ReadAllBytes(customTtf);
            ttfSourceName = Path.GetFileName(customTtf);
        }
    }

    // Priority 2: bundled SourceHanSansCN-Regular.ttf
    if (ttfSourceBytes is null)
    {
        var bundledTtf = Path.Combine(bundledPaths.FontsDirectory, "SourceHanSansCN-Regular.ttf");
        if (File.Exists(bundledTtf))
        {
            ttfSourceBytes = File.ReadAllBytes(bundledTtf);
            ttfSourceName = "SourceHanSansCN-Regular.ttf";
        }
    }
}
```

**Important:** This also fixes the existing bug where `customFiles[0]` could pick a TTF file and try to load it as an AssetBundle. The TMP resolution now explicitly filters by extension.

**Note:** `bundledPaths` requires adding `BundledAssetPaths` to the constructor — the service already has `TmpFontService` injected which depends on `BundledAssetPaths`, but we need direct access. Add `BundledAssetPaths bundledPaths` to the primary constructor parameters (line 13-16).

- [ ] **Step 2: Update the font source in manifest to reflect both sources**

In the manifest saving block (line 519-528), update `FontSource` to include TTF source when applicable:

```csharp
FontSource = ttfSourceName != null && fontFile != null
    ? $"{Path.GetFileName(fontFile)} + {ttfSourceName}"
    : ttfSourceName ?? Path.GetFileName(fontFile!),
```

- [ ] **Step 3: Make TMP source loading conditional inside `Task.Run`**

The TMP source loading block (lines 329-419 inside `Task.Run`) currently uses non-nullable locals. Make them nullable and wrap the loading in a conditional:

```csharp
AssetTypeValueField? srcFontBase = null;
List<SourceAtlasPage>? srcAtlasPages = null;

if (fontFile != null && File.Exists(fontFile))
{
    // ... existing TMP source loading logic (lines 330-419) stays here ...
    // Assign to srcFontBase and srcAtlasPages
}
```

Remove the old `throw new FileNotFoundException` for missing `fontFile` — it's no longer an error if only TTF fonts are selected.

- [ ] **Step 4: Update `ProcessLooseFile` signature to accept nullable TMP source and TTF bytes**

Change the method signature (line 551-558):
- `AssetTypeValueField srcFontBase` → `AssetTypeValueField? srcFontBase`
- `List<SourceAtlasPage> srcAtlasPages` → `List<SourceAtlasPage>? srcAtlasPages`
- Add `byte[]? ttfSourceBytes` parameter after `srcAtlasPages`

In the `foreach (var font in fontsInFile)` loop (around line 580-617), replace the `ReplaceSingleFont` call with type routing:

```csharp
// Determine font type by checking AssetClassID
var assetInfo = afile.GetAssetInfo(font.PathId);
if (assetInfo is null)
{
    failedFonts.Add(new FailedFontEntry
    {
        PathId = font.PathId, AssetFile = assetFileName,
        Error = "Asset not found"
    });
    processedFonts++;
    continue;
}

string fontName;
string fontType;

if (assetInfo.TypeId == (int)AssetClassID.Font)
{
    if (ttfSourceBytes is null)
        throw new InvalidOperationException("No TTF source font available for TTF replacement.");

    ReplaceSingleTtfFont(manager, afileInst, ttfSourceBytes,
        font.PathId, out fontName);
    fontType = "TTF";
}
else
{
    if (srcFontBase is null || srcAtlasPages is null)
        throw new InvalidOperationException("No TMP source font available for TMP replacement.");

    ReplaceSingleFont(manager, afileInst, srcFontBase,
        srcAtlasPages, font.PathId, out fontName);
    fontType = "TMP";
}

replacedFonts.Add(new ReplacedFontEntry { Name = fontName, PathId = font.PathId, FontType = fontType });
```

- [ ] **Step 5: Apply the same changes to `ProcessBundleFile`**

Same signature changes:
- `AssetTypeValueField srcFontBase` → `AssetTypeValueField? srcFontBase`
- `List<SourceAtlasPage> srcAtlasPages` → `List<SourceAtlasPage>? srcAtlasPages`
- Add `byte[]? ttfSourceBytes` parameter

Apply identical type routing logic in the `foreach (var font in fontsInFile)` loop (around line 718-755).

- [ ] **Step 6: Update all call sites of `ProcessLooseFile` and `ProcessBundleFile`**

In `ReplaceFontsAsync`, update the two call sites (around lines 459-470) to pass the nullable TMP source and TTF bytes:

```csharp
ProcessBundleFile(dstManager, srcFontBase, srcAtlasPages, ttfSourceBytes,
    gamePath, gameId, dataPath, assetFileName, fontsInFile,
    gameInfo.UnityVersion, replacedFiles, catalogFiles, failedFonts,
    ref processedFonts, totalFonts, progress, ct);
```

```csharp
ProcessLooseFile(dstManager, srcFontBase, srcAtlasPages, ttfSourceBytes,
    gamePath, gameId, dataPath, assetFileName, fontsInFile,
    gameInfo.UnityVersion, replacedFiles, failedFonts,
    ref processedFonts, totalFonts, progress, ct);
```

- [ ] **Step 7: Implement `ReplaceSingleTtfFont`**

Add this new method after `ReplaceSingleFont` (after line 935):

```csharp
private static void ReplaceSingleTtfFont(
    AssetsManager manager, AssetsFileInstance afileInst,
    byte[] ttfSourceBytes, long targetPathId, out string fontName)
{
    var afile = afileInst.file;

    // Find the target Font asset by PathId
    AssetFileInfo? targetFontInfo = null;
    foreach (var fontInfo in afile.GetAssetsOfType(AssetClassID.Font))
    {
        if (fontInfo.PathId == targetPathId)
        {
            targetFontInfo = fontInfo;
            break;
        }
    }

    if (targetFontInfo is null)
        throw new InvalidOperationException($"Target Font asset not found (PathId={targetPathId}).");

    var fontBase = manager.GetBaseField(afileInst, targetFontInfo);
    if (fontBase.IsDummy)
        throw new InvalidOperationException($"Cannot read target Font asset (PathId={targetPathId}).");

    fontName = fontBase["m_Name"].IsDummy ? "(unnamed)" : fontBase["m_Name"].AsString;

    // Replace m_FontData with new TTF bytes — preserve all other fields
    var fontData = fontBase["m_FontData"];
    if (fontData.IsDummy)
        throw new InvalidOperationException($"Font '{fontName}' has no m_FontData field.");

    fontData.AsByteArray = ttfSourceBytes;

    // Commit modified data
    targetFontInfo.SetNewData(fontBase);
}
```

- [ ] **Step 8: Build and verify**

Run: `dotnet build XUnityToolkit-WebUI/XUnityToolkit-WebUI.csproj -p:SkipFrontendBuild=true`

Expected: SUCCESS

- [ ] **Step 9: Commit**

```bash
git add XUnityToolkit-WebUI/Services/FontReplacementService.cs
git commit -m "feat: 实现 TTF 字体替换逻辑，支持双源解析和类型路由"
```

---

## Chunk 3: Frontend Updates

### Task 5: Update TypeScript types

**Files:**
- Modify: `XUnityToolkit-Vue/src/api/types.ts`

- [ ] **Step 1: Rename `TmpFontInfo` to `FontInfo` and add new fields**

Replace the `TmpFontInfo` interface (lines 229-240) with:

```typescript
export interface FontInfo {
  name: string
  pathId: number
  assetFile: string
  isInBundle: boolean
  fontType: 'TMP' | 'TTF'
  isSupported: boolean
  // TMP-specific
  atlasCount: number
  glyphCount: number
  characterCount: number
  atlasWidth: number
  atlasHeight: number
  // TTF-specific
  fontDataSize: number
}
```

- [ ] **Step 2: Update `FontReplacementStatus.replacedFonts` type**

Change line 254: `replacedFonts: TmpFontInfo[]` → `replacedFonts: FontInfo[]`

- [ ] **Step 3: Do NOT commit yet** — `types.ts` and `FontReplacementView.vue` must be committed together since the type rename breaks the view until it is also updated.

### Task 6: Update FontReplacementView.vue (commit together with Task 5)

**Files:**
- Modify: `XUnityToolkit-Vue/src/views/FontReplacementView.vue`

- [ ] **Step 1: Update imports**

Change line 11: `TmpFontInfo` → `FontInfo` in the import statement.

- [ ] **Step 2: Update refs and type annotations**

Change line 23: `const fonts = ref<TmpFontInfo[]>([])` → `const fonts = ref<FontInfo[]>([])`

- [ ] **Step 3: Add `h` import and `formatSize` helper (BEFORE updating columns)**

Add `h` to the vue import: `import { ref, computed, onMounted, onBeforeUnmount, h } from 'vue'`

Add helper function after the refs:

- [ ] **Step 4: Update table columns**

Replace the `columns` computed (lines 37-54) with:

```typescript
const columns = computed<DataTableColumns<FontInfo>>(() => [
  { type: 'selection', disabled: (row: FontInfo) => !row.isSupported },
  { title: '字体名称', key: 'name', ellipsis: { tooltip: true } },
  {
    title: '类型', key: 'fontType', width: 80,
    render: (row) => h(NTag, { size: 'small', bordered: false, type: row.fontType === 'TMP' ? 'info' : 'success' }, { default: () => row.fontType })
  },
  {
    title: '所在文件', key: 'assetFile', ellipsis: { tooltip: true }, width: 220,
    render: (row) => row.assetFile
  },
  {
    title: '图集', key: 'atlas', width: 120,
    render: (row) => row.fontType === 'TMP' && row.atlasWidth > 0 ? `${row.atlasWidth}×${row.atlasHeight}` : '—'
  },
  {
    title: '字形数', key: 'glyphCount', width: 80,
    render: (row) => row.fontType === 'TMP' ? row.glyphCount : '—'
  },
  {
    title: '字符数', key: 'characterCount', width: 80,
    render: (row) => row.fontType === 'TMP' ? row.characterCount : '—'
  },
  {
    title: '大小', key: 'fontDataSize', width: 100,
    render: (row) => row.fontType === 'TTF' ? formatSize(row.fontDataSize) : '—'
  },
  {
    title: '状态', key: 'isSupported', width: 100,
    render: (row) => row.isSupported ? '支持' : '不支持'
  }
])
```

Add the `formatSize` function:

```typescript
function formatSize(bytes: number): string {
  if (bytes < 1024) return `${bytes} B`
  if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`
  return `${(bytes / (1024 * 1024)).toFixed(1)} MB`
}
```

- [ ] **Step 5: Update `rowClassName` and `row-key` type annotations (and `scanFonts` type)**

Change line 56: `const rowClassName = (row: TmpFontInfo) => ...` → `const rowClassName = (row: FontInfo) => ...`
Change line 359: `:row-key="(row: TmpFontInfo) => ..."` → `:row-key="(row: FontInfo) => ..."`

- [ ] **Step 6: Update `scanFonts` to use `FontInfo` and update messages**

Change line 79: `api.post<TmpFontInfo[]>` → `api.post<FontInfo[]>`
Change line 85: `'未在游戏资产中找到 TMP 字体'` → `'未在游戏资产中找到字体'`
Change line 87: `` `找到 ${fonts.value.length} 个 TMP 字体` `` → `` `找到 ${fonts.value.length} 个字体` ``

- [ ] **Step 7: Update empty state text**

Change line 319: `点击"扫描字体"按钮检测游戏中的 TMP 字体资源` → `点击"扫描字体"按钮检测游戏中的字体资源`

- [ ] **Step 8: Type-check and build frontend**

Run: `cd XUnityToolkit-Vue && npx vue-tsc --noEmit && npm run build`

Expected: SUCCESS

- [ ] **Step 9: Commit**

```bash
git add XUnityToolkit-Vue/src/api/types.ts XUnityToolkit-Vue/src/views/FontReplacementView.vue
git commit -m "feat: 前端支持 TTF 字体显示，统一 FontInfo 类型"
```

---

## Chunk 4: Full Build Verification & CLAUDE.md

### Task 7: Full build verification

- [ ] **Step 1: Build entire project**

Run: `dotnet build XUnityToolkit-WebUI/XUnityToolkit-WebUI.csproj`

This builds both backend AND frontend (MSBuild target). Expected: SUCCESS.

- [ ] **Step 2: Search for any remaining `TmpFontInfo` references**

Run grep across the entire project for `TmpFontInfo` — should only appear in `TmpFontStatus` (which is a different type for TMP font installation, NOT font replacement) and NOT in font replacement code.

- [ ] **Step 3: Fix any remaining references**

If any `TmpFontInfo` references remain in font replacement code paths, update them to `FontInfo`.

### Task 8: Update CLAUDE.md

**Files:**
- Modify: `CLAUDE.md`

- [ ] **Step 1: Update Font Replacement section (MUST be written in English per project convention)**

In the "Font Replacement (AssetsTools.NET)" section, add TTF font documentation:
- Add: `**TTF replacement:** `ReplaceSingleTtfFont` replaces `m_FontData` byte array in `AssetClassID.Font` assets; preserves all layout metadata (`m_FontSize`, `m_LineSpacing`, etc.)`
- Add: `**Bundled TTF:** `bundled/fonts/SourceHanSansCN-Regular.ttf` (~10MB) used as default replacement source for TTF fonts`
- Add: `**Custom font coexistence:** `data/custom-fonts/{gameId}/` can hold both an AssetBundle (for TMP) and a TTF/OTF (for TTF) simultaneously; files distinguished by extension; upload endpoint uses magic bytes (`00 01 00 00` TTF, `4F 54 54 4F` OTF) for format detection`
- Add: `**TMP source filtering:** `ReplaceFontsAsync` custom font auto-resolve filters by extension (excludes `.ttf`/`.otf` from TMP source, excludes non-TTF from TTF source)`

- [ ] **Step 2: Update sync points**

Add to the "Font replacement models" sync point:
- `FontInfo` (was `TmpFontInfo`) between `Models/FontReplacement.cs` ↔ `src/api/types.ts`

- [ ] **Step 3: Commit**

```bash
git add CLAUDE.md
git commit -m "docs: 更新 CLAUDE.md 添加 TTF 字体替换文档"
```
