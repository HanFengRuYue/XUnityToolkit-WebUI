# Unity TMP SDF Generation Replication Design

## Summary

Replace the current FreeTypeSharp `FT_RENDER_MODE_SDF` pipeline with a faithful replication of Unity's FontEngine SDF generation approach: rasterize glyphs as antialiased bitmaps, compute Euclidean Distance Transform (Felzenszwalb algorithm), and encode as SDF. Support SDFAA/SDF8/SDF16/SDF32 render modes with upsampling, dynamic padding calculation, auto-sizing point size, and correct GradientScale material properties.

## Motivation

The current font generation uses FreeType's built-in SDF mode (`FT_RENDER_MODE_SDF`), which computes distances from outline vectors. While mathematically sound, the output may differ from Unity's approach (bitmap-to-distance-transform), leading to subtle rendering differences in games. By replicating Unity's exact algorithm, we guarantee output consistency with Unity editor-generated TMP fonts.

## Architecture

### New File

- `XUnityToolkit-WebUI/Services/DistanceFieldGenerator.cs` — Felzenszwalb EDT + SDF encoding

### Modified Files

- `TmpFontGeneratorService.cs` — Pipeline restructure (Phase 2 SDF generation)
- `Models/FontGeneration.cs` — New configuration fields
- `src/api/types.ts` — TypeScript type sync
- `FontGeneratorView.vue` — UI for render mode, auto-sizing, padding mode
- `FontGenerationEndpoints.cs` — Accept new parameters
- AssetBundle serialization — GradientScale, padding, render mode metadata

## Design

### 1. SDF Generation Pipeline

#### SDFAA Mode (default, no upsampling)

```
FT_Set_Pixel_Sizes(samplingSize)
-> FT_Render_Glyph(FT_RENDER_MODE_NORMAL)     // 8-bit antialiased bitmap
-> Pad bitmap by `padding` pixels on all sides
-> Initialize EDT from antialiased values (sub-pixel distance seeding, see Section 2)
-> Compute EDT (Felzenszwalb) on inside and outside channels
-> Combine: SDF = sqrt(outside) - sqrt(inside)
-> Normalize: value = round(clamp(distance / padding, -1, 1) * 127.5 + 128)
```

#### SDF8/16/32 Modes (with upsampling)

```
FT_Set_Pixel_Sizes(samplingSize * upsampling)  // e.g., 64*8 = 512
-> FT_Render_Glyph(FT_RENDER_MODE_NORMAL)     // high-resolution AA bitmap
-> Binarize at threshold 128
-> Pad bitmap by (padding * upsampling) pixels
-> Compute EDT (Felzenszwalb) with binary initialization
-> Combine: SDF = sqrt(outside) - sqrt(inside)
-> Bilinear downsample to target resolution
-> Normalize to 0-255 (128 = edge), using round()
```

#### Constraints (matching Unity)

- `samplingSize * upsampling <= 16384` pixels
- SDF32: max samplingSize = 512
- SDF16: max samplingSize = 1024
- SDF8: max samplingSize = 2048

### 2. Felzenszwalb Distance Transform

New class `DistanceFieldGenerator` with public API:

```csharp
public static byte[] GenerateSdf(
    byte[] bitmap,
    int width, int height,
    int padding,
    int upsampling,
    int targetWidth, int targetHeight
)
```

Internal implementation:

1. **EDT initialization (mode-dependent)**:
   - **SDFAA (upsampling=1)**: Sub-pixel distance seeding from antialiased values. Inside channel: `d_inside[i] = (value/255.0)^2` when value > 0, else `INF`. Outside channel: `d_outside[i] = ((255-value)/255.0)^2` when value < 255, else `INF`. This preserves the antialiased edge information rather than applying a hard binary threshold, producing smoother results at the glyph boundary.
   - **SDF8/16/32 (upsampling>1)**: Binary initialization after binarization at threshold 128. Inside channel: `d[i] = 0` when value >= 128, else `INF`. Outside channel: `d[i] = 0` when value < 128, else `INF`.

2. **1D -> 2D decomposition (Felzenszwalb parabola envelope)**:
   - For each row, compute 1D squared distance transform using lower envelope of parabolas
   - For each column, apply same 1D transform to the row-transformed result
   - O(n) per row/column, O(n*m) total
   - Uses three working arrays per 1D pass: `f[]` (source), `v[]` (parabola locations), `z[]` (boundaries)
   - Intersection formula: `s = ((f[q] + q*q) - (f[v[k]] + v[k]*v[k])) / (2*q - 2*v[k])`

3. **Merge**: `signedDistance = sqrt(outside[i]) - sqrt(inside[i])`

4. **Normalize**: `round(clamp(signedDistance / padding, -1, 1) * 127.5 + 128)` — uses rounding (not truncation) to match Unity behavior. Output range: [0, 255], where 128 = edge.

5. **Downsample** (SDF8/16/32 only): Bilinear interpolation in SDF domain to target dimensions.

### 3. Padding Calculation

Two modes (replicating Unity):

- **Percentage mode** (default): `padding = (int)(samplingSize * paddingPercent / 100f)`, default 10%
- **Pixel mode**: `padding = paddingPixels`, user-specified

### 4. GradientScale

```
GradientScale = padding + 1
```

Written to AssetBundle Material's `_GradientScale` float property. This is the critical shader parameter that tells TMP how to interpret the SDF distance field. Must be kept consistent with `m_AtlasPadding` in the MonoBehaviour: `_GradientScale = m_AtlasPadding + 1`.

### 5. Auto-Sizing Sampling Size

Binary search algorithm (replicating Unity, max 15 iterations):

```
Initial estimate: maxSamplingSize = sqrt(atlasW * atlasH / glyphCount) * 3
Binary search:
  samplingSize = (min + max) / 2
  Try pack all glyphs at this size
  All fit -> min = samplingSize (increase)
  Overflow -> max = samplingSize (decrease)
  max - min <= 1 -> found optimum
```

User can choose "auto" or "manual" sampling size mode.

### 6. Render Mode Support

| Mode | Upsampling | AtlasRenderMode | Max SamplingSize |
|------|-----------|-----------------|------------------|
| SDFAA | 1x | 4165 | No special limit |
| SDF8 | 8x | 4168 | 2048 |
| SDF16 | 16x | 4169 | 1024 |
| SDF32 | 32x | 4170 | 512 |

AtlasRenderMode values to be verified from Unity project during implementation; listed values are best estimates from `GlyphRenderMode` enum structure.

### 7. Glyph Metrics and Bitmap Dimension Handling

#### Bitmap includes padding

The SDF bitmap stored to disk **includes padding** on all sides (matching Unity's approach). This means:
- Raw glyph bitmap: `glyphWidth x glyphHeight` (from FreeType, at render resolution)
- After padding: `(glyphWidth + 2*padding) x (glyphHeight + 2*padding)` (at target resolution after downsampling if applicable)
- The stored `BitmapWidth`/`BitmapHeight` in `GlyphData` reflect the **padded** dimensions

#### Packing changes (no double-padding)

Since the SDF bitmap already includes padding, the packing code must NOT add additional padding:
- **Current**: `CreatePackingRects` adds `DefaultPadding * 2` to bitmap dimensions → **remove this**
- **New**: Pack using `BitmapWidth x BitmapHeight` directly (already includes padding)

#### Coordinate flow: packing → compositing → serialization

The SDF bitmap includes padding, but `GlyphRect` in `m_GlyphTable` must reference the **inner glyph region** (unpadded), matching Unity's convention. The TMP shader expands outward from `GlyphRect` by `m_AtlasPadding` to sample the surrounding SDF distance field.

Coordinate flow pseudocode:

```
// 1. SDF bitmap dimensions (stored to disk)
paddedBitmapW = glyphW + 2 * padding
paddedBitmapH = glyphH + 2 * padding

// 2. Packing: use full padded dimensions
packRect.width  = paddedBitmapW
packRect.height = paddedBitmapH
→ RectpackSharp assigns: packRect.x, packRect.y

// 3. Compositing: blit full padded bitmap at packed position
compositX = packRect.x
compositY = packRect.y
Buffer.BlockCopy(sdfBitmap, ..., atlas, compositX, compositY, paddedBitmapW, paddedBitmapH)

// 4. GlyphRect serialization: inner glyph region (offset inward by padding)
glyphRect.x      = packRect.x + padding
glyphRect.y      = packRect.y + padding
glyphRect.width  = glyphW   // unpadded
glyphRect.height = glyphH   // unpadded

// 5. UsedGlyphRects: full padded region
usedRect.x      = packRect.x
usedRect.y      = packRect.y
usedRect.width  = paddedBitmapW
usedRect.height = paddedBitmapH
```

**`ApplyPackedPositions` change**: Current code adds `DefaultPadding` to packed X/Y to offset inward. Under the new design, `GlyphData.AtlasX/AtlasY` should store the **inner glyph position** (packRect.x + padding, packRect.y + padding) for use in `GlyphRect` serialization. A separate `CompositX/CompositY` (= packRect.x, packRect.y) is used for atlas compositing. Alternatively, store the raw packed position and add padding only at serialization time.

#### BearingX / BearingY semantics

BearingX and BearingY remain **raw FreeType values** (unpadded glyph metrics) regardless of padding or upsampling mode. The TMP shader uses `m_AtlasPadding` + `GlyphRect` to correctly locate the glyph in the atlas; bearing values describe the glyph's typographic positioning relative to the cursor, not its atlas location.

#### Metric adjustments for upsampling

When using upsampling (SDF8/16/32):
- Render at `samplingSize * upsampling`
- All metrics (Width, Height, BearingX, BearingY, Advance) divided by `upsampling`
- Padded bitmap dimensions: `glyphWidth/upsampling + 2*padding` x `glyphHeight/upsampling + 2*padding`
- SDF bitmap stored at target (downsampled) resolution

#### UsedGlyphRects

`UsedGlyphRects` use the full **padded** bitmap dimensions at the raw packed position (see coordinate flow above). Current code's `+ DefaultPadding` offset is removed.

#### Zero-bitmap glyphs (space, etc.)

Glyphs with zero bitmap dimensions (e.g., space U+0020) are excluded from SDF generation and atlas packing but included in `m_CharacterTable` and `m_GlyphTable` with correct metrics (advance, etc.). No change from current behavior.

#### Minimum padding

Padding must be >= 1 to avoid division by zero in SDF normalization (`signedDistance / padding`). Enforce `padding = Math.Max(1, calculatedPadding)` after calculation.

### 8. Memory Control

High upsampling produces large intermediate bitmaps. Peak memory estimate for SDF32 at samplingSize=64:
- High-res bitmap: 2048x2048 = 4MB
- Two EDT float grids (inside + outside): 2 * 2048 * 2048 * 4 bytes = 32MB
- 1D working arrays (f, v, z): 3 * 2048 * 4 bytes = 24KB (negligible, reused per row/column)
- **Peak per-glyph: ~36MB**, freed after each glyph

Strategy:
- Process one glyph at a time: render -> EDT -> downsample -> write to disk -> free
- Maintain existing disk-backed temp storage pattern
- EDT working arrays allocated once per glyph, reused across rows/columns

### 9. AssetBundle Metadata

| Field | Current | New |
|-------|---------|-----|
| `m_AtlasPadding` | 9 (hardcoded) | Calculated padding value |
| `m_AtlasRenderMode` | 4165 | Per render mode enum value |
| Material `_GradientScale` | Not set | `padding + 1` (must equal `m_AtlasPadding + 1`) |
| Material `_TextureWidth` | Not set | Atlas width (low priority, most shaders read texture size directly) |
| Material `_TextureHeight` | Not set | Atlas height (low priority) |

Material property injection via AssetsTools.NET: check template Material's `m_SavedProperties.m_Floats` for existing entries, modify or add as needed.

### 10. API Changes

`POST /api/font-generation/generate` new fields:

```typescript
{
  // existing fields (samplingSize, atlasWidth, atlasHeight, charsets, etc.)
  renderMode: 'SDFAA' | 'SDF8' | 'SDF16' | 'SDF32'  // default: 'SDFAA'
  samplingSizeMode: 'auto' | 'manual'                  // default: 'manual'
  paddingMode: 'percentage' | 'pixel'                   // default: 'percentage'
  paddingValue: number                                   // default: 10 (%) or 9 (px)
}
```

Note: The existing API uses `samplingSize` (not `pointSize`). This naming is preserved for consistency. Internally, `samplingSize` is equivalent to Unity's "point size" concept. The new field is `samplingSizeMode` (not `pointSizeMode`) to match.

### 11. Frontend Changes (FontGeneratorView.vue)

New controls:
1. **Render mode dropdown**: SDFAA / SDF8 / SDF16 / SDF32
2. **Sampling size mode toggle**: Auto / Manual (auto hides the sampling size input)
3. **Padding mode toggle**: Percentage / Pixel with numeric input

Phase labels remain unchanged: `parsing`, `sdf`, `packing`, `compositing`, `serializing`. The SDF phase progress message will include render mode info (e.g., "SDF32 (2048px upsampling)") for user awareness of expected duration.

### 12. Model Changes

`Models/FontGeneration.cs` — new fields in generation config:
- `RenderMode` (string enum)
- `SamplingSizeMode` (string enum)
- `PaddingMode` (string enum)
- `PaddingValue` (int)

`FontGenerationReport` — new fields:
- `RenderMode`, `SamplingSizeMode`, `ActualSamplingSize`, `Padding`, `GradientScale`

`FontGenerationEndpoints.cs` — update `FontGenerationStartRequest` record to include new fields and map them to `FontGenerationRequest`.

### 13. Removed Dependencies

- `FT_Property_Set("sdf", "spread", ...)` — no longer using FreeType SDF mode
- `FT_RENDER_MODE_SDF` constant — replaced with `FT_RENDER_MODE_NORMAL`
- `DefaultSpread` constant — removed (spread concept replaced by padding-based EDT)

## Sync Points

- `Models/FontGeneration.cs` <-> `src/api/types.ts`: New config and report fields
- `FontGeneratorView.vue`: New UI controls matching new API fields
- `TmpFontGeneratorService.cs`: Accept and use new parameters, update packing logic
- `FontGenerationEndpoints.cs`: Parse new request fields, update `FontGenerationStartRequest`

## Risks

- **SDF8/16/32 memory**: Peak ~36MB per glyph for SDF32 (mitigated by per-glyph processing + disk temp, freed after each glyph)
- **SDF8/16/32 speed**: Higher upsampling = slower rendering (mitigated by progress reporting showing render resolution)
- **AtlasRenderMode enum values**: SDF8/16/32 values need verification from Unity (mitigated by creating test fonts in Unity editor during implementation)
- **Material property injection**: Template Material may have different `m_SavedProperties` structure across Unity versions (mitigated by checking and adapting at runtime)
- **Double-padding regression**: Packing code currently adds padding to bitmap dimensions; must be removed since new bitmaps include padding (mitigated by explicit documentation in Section 7)
