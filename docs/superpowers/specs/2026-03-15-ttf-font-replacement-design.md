# TTF Font Replacement Design

**Date:** 2026-03-15
**Status:** Approved

## Overview

Extend the existing font replacement feature to support replacing Unity `Font` assets (AssetClassID.Font) — the TTF/OTF binary fonts used by legacy Unity UI (UGUI Text, OnGUI). Currently, only TMP_FontAsset replacement is supported.

## Goals

- Detect Unity Font assets containing TTF/OTF binary data (`m_FontData` field)
- Replace font data with a bundled default (SourceHanSansCN-Regular.ttf) or user-uploaded TTF/OTF
- Unified UI: TMP and TTF fonts appear in the same scan results table
- Shared backup/restore mechanism with TMP replacements

## Non-Goals

- Replacing TMP_FontAsset v1.0.0 (remains unsupported)
- Converting between font types (TTF → TMP or vice versa)
- Font subsetting or optimization

## Architecture Decision

**Approach: Extend existing FontReplacementService** (chosen over creating a separate service or abstract base class). TTF replacement logic is simple (byte array swap), and the existing service already handles file traversal, bundle read/write, and backup — no need to duplicate.

## Data Model

### Backend: `FontInfo` (replaces `TmpFontInfo`)

```csharp
public record FontInfo
{
    public string Name { get; init; }
    public long PathId { get; init; }
    public string AssetFile { get; init; }
    public bool IsInBundle { get; init; }
    public string FontType { get; init; }       // "TMP" | "TTF"
    public bool IsSupported { get; init; }
    // TMP-specific (0/null for TTF)
    public int AtlasCount { get; init; }
    public int GlyphCount { get; init; }
    public int CharacterCount { get; init; }
    public int AtlasWidth { get; init; }
    public int AtlasHeight { get; init; }
    // TTF-specific
    public long FontDataSize { get; init; }     // m_FontData byte count
}
```

### Frontend: `FontInfo` TypeScript interface (replaces `TmpFontInfo`)

```typescript
interface FontInfo {
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

## Scanning

In `FontReplacementService.ScanFontsAsync`, within the existing per-file loop:

1. **Existing:** Scan `AssetClassID.MonoBehaviour` for TMP_FontAsset (unchanged)
2. **New:** Scan `AssetClassID.Font` for Unity Font assets:
   - Read `m_FontData` field
   - Skip if `IsDummy`, byte array is empty, or size < 1KB (placeholder/stub fonts)
   - Create `FontInfo` with `FontType = "TTF"`, `FontDataSize = m_FontData.Length`
   - `IsSupported = true` for all detected TTF fonts with non-empty data

No new file traversal loops — piggyback on the existing iteration over `.assets` files and bundle entries.

## Replacement

### Dual-Source Resolution

A single replacement operation may include both TMP and TTF fonts. These require different source files:
- **TMP source:** AssetBundle containing TMP_FontAsset (resolved by `TmpFontService`, or user-uploaded `.bundle`)
- **TTF source:** Raw TTF/OTF file bytes (bundled `SourceHanSansCN-Regular.ttf`, or user-uploaded `.ttf`/`.otf`)

Both sources are resolved at the start of `ReplaceFontsAsync`, before iterating fonts.

### Font Type Routing

The replacement method determines font type at runtime by checking `AssetClassID` of the asset at the given PathId — no need to pass font type from frontend:

```csharp
var assetInfo = afile.GetAssetInfo(pathId);
if (assetInfo.TypeId == (int)AssetClassID.Font)
    ReplaceSingleTtfFont(...)
else // AssetClassID.MonoBehaviour (TMP)
    ReplaceSingleFont(...)
```

### Flow

```
ReplaceFontsAsync receives List<long> selectedFontPathIds
  ├── Resolve TMP source (AssetBundle) — if any TMP fonts selected
  ├── Resolve TTF source (raw bytes) — if any TTF fonts selected
  ├── Group by asset file (existing logic)
  ├── For each font in file:
  │   ├── AssetClassID.Font → ReplaceSingleTtfFont (new)
  │   └── AssetClassID.MonoBehaviour → ReplaceSingleFont (existing)
  ├── Write asset file (existing logic)
  └── Backup (existing logic, shared manifest)
```

### ReplaceSingleTtfFont

1. Read target Font asset base field via `manager.GetBaseField(afileInst, fontAssetInfo)`
2. Read replacement TTF bytes from source file
3. Overwrite `m_FontData` byte array with new bytes
4. **Preserve unchanged:** `m_Name`, `m_FontSize`, `m_LineSpacing`, `m_CharacterSpacing`, `m_CharacterPadding`, and all other layout metadata — prevents breaking game UI layout
5. Mark asset as modified

### Replacement Source Priority (TTF)

1. User-uploaded custom TTF/OTF in `data/custom-fonts/{gameId}/` (`.ttf` or `.otf` extension)
2. Bundled `bundled/fonts/SourceHanSansCN-Regular.ttf` (~10MB)

### Custom Font Upload Extension

The custom font directory can hold **both types simultaneously**: one AssetBundle (for TMP) and one TTF/OTF (for TTF). They are distinguished by file extension. Uploading a TTF does not overwrite an existing AssetBundle custom font, and vice versa.

Extend the existing `/upload` endpoint validation:
- Current: accepts AssetBundle files only (validates TMP_FontAsset presence)
- New: also accepts `.ttf`/`.otf` files
- Detection flow:
  1. Read first 4 bytes of uploaded file
  2. If TTF magic (`00 01 00 00`) or OTF magic (`4F 54 54 4F`): validate as font (minimum size 1KB threshold)
  3. Otherwise: fall through to existing `ValidateCustomFont` (AssetBundle check)
  4. Error messages are format-aware (not hardcoded "未找到 TMP_FontAsset")
- Storage: same location `data/custom-fonts/{gameId}/`, filename preserved

## Backup & Restore

**Restore logic: no changes needed.** The existing `BackupFile` mechanism backs up entire asset files before modification. If a file contains both TMP and TTF fonts, one backup covers both. Restore simply copies backup files back.

**Manifest update:** Add `FontType` field to `ReplacedFontEntry` so that `GetStatusAsync` can construct `FontInfo` objects with correct type. The project is pre-stable, so no backward-compat migration is needed for existing manifests.

## API Changes

No new endpoints. Existing endpoints are extended:

| Endpoint | Change |
|----------|--------|
| `POST .../scan` | Return type: `List<TmpFontInfo>` → `List<FontInfo>` |
| `POST .../replace` | No change — `selectedFontPathIds` already type-agnostic; backend routes by FontType |
| `POST .../upload` | Accept `.ttf`/`.otf` in addition to AssetBundle |
| `GET .../status` | Return type uses `FontInfo` instead of `TmpFontInfo` |
| Others | No change |

## Frontend Changes

### FontReplacementView.vue — Scan Results Table

Add "Type" column and "Size" column:

| Name | Type | Asset File | Atlas | Glyphs | Characters | Size | Status |
|------|------|-----------|-------|--------|------------|------|--------|
| Arial | TMP | sharedassets0 | 2048×2048 | 1200 | 1100 | — | Supported |
| SimSun | TTF | sharedassets1 | — | — | — | 3.2 MB | Supported |
| OldFont | TMP(v1.0) | level0 | — | — | — | — | Unsupported |

- "Type" column: TMP/TTF tag (distinct visual style)
- "Atlas", "Glyphs", "Characters" columns: show `—` for TTF fonts
- "Size" column: show formatted `fontDataSize` for TTF, `—` for TMP
- Checkbox logic unchanged: controlled by `isSupported`

### Upload Area

- Label: "Upload custom font (AssetBundle / TTF / OTF)"
- Display uploaded file type tag after upload

### User-Facing Text Updates

All hardcoded "TMP" references in user-facing text must be updated:
- Scan empty state: "未在游戏资产中找到 TMP 字体" → "未在游戏资产中找到字体"
- Scan success: "找到 X 个 TMP 字体" → "找到 X 个字体"
- Other occurrences of "TMP 字体" in labels/tooltips → "字体"

## Sync Points

Files that must be updated together:

1. `Models/FontReplacement.cs` — `TmpFontInfo` → `FontInfo`, add `FontType`, `FontDataSize`; add `FontType` to `ReplacedFontEntry`
2. `src/api/types.ts` — sync TypeScript interface
3. `FontReplacementService.cs` — scan + replace logic
4. `FontReplacementEndpoints.cs` — return type update, upload validation
5. `FontReplacementView.vue` — table columns, user-facing text updates
6. `CLAUDE.md` — update Font Replacement section, API docs, sync points

## Risks & Mitigations

| Risk | Mitigation |
|------|-----------|
| `AssetClassID.Font` not in classdata.tpk for some Unity versions | Graceful skip: if `GetBaseField` throws, log warning and skip the font |
| `m_FontData` replacement changes file size, breaking bundle offsets | Existing bundle write logic (uncompressed tmp → recompress LZ4) handles size changes correctly |
| Some Font assets may reference external font files (m_FontData empty) | Filter: skip fonts with empty `m_FontData` during scan |
| Custom font upload now accepts two formats | Magic byte detection is robust; validate before saving |
