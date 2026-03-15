# Unity TMP SDF Generation Replication — Implementation Plan

> **For agentic workers:** REQUIRED: Use superpowers:subagent-driven-development (if subagents available) or superpowers:executing-plans to implement this plan. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the FreeTypeSharp `FT_RENDER_MODE_SDF` pipeline with a Unity-faithful bitmap→EDT→SDF pipeline, supporting SDFAA/SDF8/SDF16/SDF32 render modes, dynamic padding, auto-sizing, and correct GradientScale.

**Architecture:** New `DistanceFieldGenerator` class implements Felzenszwalb EDT. `TmpFontGeneratorService` Phase 2 is refactored to render AA bitmaps via FreeType then pass them through EDT. New config fields flow from frontend through API to the generation pipeline. AssetBundle serialization injects GradientScale into Material properties.

**Tech Stack:** C# (.NET 10), FreeTypeSharp 3.1.0, RectpackSharp 1.2.0, AssetsTools.NET 3.0.4, Vue 3, TypeScript, Naive UI

**Spec:** `docs/superpowers/specs/2026-03-15-unity-sdf-replication-design.md`

---

## Chunk 1: Backend — Core Algorithm + Models + Service Refactor

### Task 1: Create DistanceFieldGenerator.cs

**Files:**
- Create: `XUnityToolkit-WebUI/Services/DistanceFieldGenerator.cs`

- [ ] **Step 1: Create the distance field generator**

```csharp
namespace XUnityToolkit_WebUI.Services;

/// <summary>
/// Generates SDF bitmaps from FreeType antialiased bitmaps using the
/// Felzenszwalb squared Euclidean distance transform, replicating Unity's FontEngine approach.
/// </summary>
public static class DistanceFieldGenerator
{
    private const float INF = 1e20f;

    /// <summary>
    /// Generate an SDF bitmap from an antialiased FreeType bitmap.
    /// Output includes padding on all sides.
    /// </summary>
    /// <param name="bitmap">Input AA bitmap (FT_RENDER_MODE_NORMAL output)</param>
    /// <param name="bmpWidth">Input bitmap width</param>
    /// <param name="bmpHeight">Input bitmap height</param>
    /// <param name="padding">Padding in target space (pixels)</param>
    /// <param name="upsampling">1 for SDFAA, 8/16/32 for upsampled modes</param>
    /// <param name="sdfWidth">Output: SDF bitmap width (includes padding)</param>
    /// <param name="sdfHeight">Output: SDF bitmap height (includes padding)</param>
    /// <returns>SDF bitmap bytes (Alpha8, 128 = edge)</returns>
    public static byte[] GenerateSdf(
        byte[] bitmap,
        int bmpWidth, int bmpHeight,
        int padding,
        int upsampling,
        out int sdfWidth, out int sdfHeight)
    {
        int padHigh = padding * upsampling;
        int paddedW = bmpWidth + 2 * padHigh;
        int paddedH = bmpHeight + 2 * padHigh;

        var inside = new float[paddedW * paddedH];
        var outside = new float[paddedW * paddedH];

        if (upsampling == 1)
            InitializeFromAntialiased(bitmap, bmpWidth, bmpHeight, padHigh, paddedW, inside, outside);
        else
            InitializeFromBinary(bitmap, bmpWidth, bmpHeight, padHigh, paddedW, inside, outside);

        ComputeEdt(inside, paddedW, paddedH);
        ComputeEdt(outside, paddedW, paddedH);

        // Merge: signed distance = sqrt(outside) - sqrt(inside)
        var sdfHigh = new float[paddedW * paddedH];
        for (int i = 0; i < sdfHigh.Length; i++)
            sdfHigh[i] = MathF.Sqrt(outside[i]) - MathF.Sqrt(inside[i]);

        float[] sdfFinal;
        if (upsampling > 1)
        {
            sdfWidth = (int)Math.Ceiling(bmpWidth / (double)upsampling) + 2 * padding;
            sdfHeight = (int)Math.Ceiling(bmpHeight / (double)upsampling) + 2 * padding;
            sdfFinal = BilinearDownsample(sdfHigh, paddedW, paddedH, sdfWidth, sdfHeight);
        }
        else
        {
            sdfWidth = paddedW;
            sdfHeight = paddedH;
            sdfFinal = sdfHigh;
        }

        // Normalize to 0-255 (128 = edge)
        var result = new byte[sdfWidth * sdfHeight];
        for (int i = 0; i < result.Length; i++)
        {
            float normalized = Math.Clamp(sdfFinal[i] / padding, -1f, 1f) * 127.5f + 128f;
            result[i] = (byte)Math.Clamp(MathF.Round(normalized), 0f, 255f);
        }

        return result;
    }

    /// <summary>SDFAA: sub-pixel distance seeding from antialiased values.</summary>
    private static void InitializeFromAntialiased(
        byte[] bitmap, int bmpW, int bmpH,
        int pad, int paddedW,
        float[] inside, float[] outside)
    {
        Array.Fill(inside, INF);
        Array.Fill(outside, INF);

        for (int y = 0; y < bmpH; y++)
        {
            for (int x = 0; x < bmpW; x++)
            {
                float v = bitmap[y * bmpW + x] / 255f;
                int pi = (y + pad) * paddedW + (x + pad);
                inside[pi] = v > 0 ? v * v : INF;
                outside[pi] = v < 1f ? (1f - v) * (1f - v) : INF;
            }
        }
    }

    /// <summary>SDF8/16/32: binary initialization from binarized bitmap.</summary>
    private static void InitializeFromBinary(
        byte[] bitmap, int bmpW, int bmpH,
        int pad, int paddedW,
        float[] inside, float[] outside)
    {
        Array.Fill(inside, INF);
        Array.Fill(outside, INF);

        for (int y = 0; y < bmpH; y++)
        {
            for (int x = 0; x < bmpW; x++)
            {
                int pi = (y + pad) * paddedW + (x + pad);
                if (bitmap[y * bmpW + x] >= 128)
                    inside[pi] = 0;
                else
                    outside[pi] = 0;
            }
        }
    }

    /// <summary>2D squared Euclidean distance transform (Felzenszwalb parabola envelope).</summary>
    private static void ComputeEdt(float[] grid, int width, int height)
    {
        int maxDim = Math.Max(width, height);
        var f = new float[maxDim];
        var v = new int[maxDim];
        var z = new float[maxDim + 1];
        var d = new float[maxDim];

        // Rows
        for (int y = 0; y < height; y++)
        {
            int offset = y * width;
            for (int x = 0; x < width; x++)
                f[x] = grid[offset + x];

            Edt1D(f, width, v, z, d);

            for (int x = 0; x < width; x++)
                grid[offset + x] = d[x];
        }

        // Columns
        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
                f[y] = grid[y * width + x];

            Edt1D(f, height, v, z, d);

            for (int y = 0; y < height; y++)
                grid[y * width + x] = d[y];
        }
    }

    /// <summary>1D squared distance transform using lower envelope of parabolas.</summary>
    private static void Edt1D(float[] f, int n, int[] v, float[] z, float[] d)
    {
        int k = 0;
        v[0] = 0;
        z[0] = -INF;
        z[1] = INF;

        for (int q = 1; q < n; q++)
        {
            float s;
            while (true)
            {
                s = ((f[q] + q * q) - (f[v[k]] + v[k] * v[k])) / (2f * q - 2f * v[k]);
                if (s > z[k]) break;
                k--;
            }

            k++;
            v[k] = q;
            z[k] = s;
            z[k + 1] = INF;
        }

        k = 0;
        for (int q = 0; q < n; q++)
        {
            while (z[k + 1] < q)
                k++;
            float dx = q - v[k];
            d[q] = dx * dx + f[v[k]];
        }
    }

    /// <summary>Bilinear downsample in SDF domain.</summary>
    private static float[] BilinearDownsample(float[] src, int srcW, int srcH, int dstW, int dstH)
    {
        var dst = new float[dstW * dstH];
        float scaleX = (float)srcW / dstW;
        float scaleY = (float)srcH / dstH;

        for (int dy = 0; dy < dstH; dy++)
        {
            for (int dx = 0; dx < dstW; dx++)
            {
                float sx = (dx + 0.5f) * scaleX - 0.5f;
                float sy = (dy + 0.5f) * scaleY - 0.5f;

                int x0 = Math.Max(0, (int)MathF.Floor(sx));
                int y0 = Math.Max(0, (int)MathF.Floor(sy));
                int x1 = Math.Min(srcW - 1, x0 + 1);
                int y1 = Math.Min(srcH - 1, y0 + 1);

                float fx = sx - x0;
                float fy = sy - y0;

                dst[dy * dstW + dx] =
                    src[y0 * srcW + x0] * (1 - fx) * (1 - fy) +
                    src[y0 * srcW + x1] * fx * (1 - fy) +
                    src[y1 * srcW + x0] * (1 - fx) * fy +
                    src[y1 * srcW + x1] * fx * fy;
            }
        }

        return dst;
    }
}
```

- [ ] **Step 2: Build to verify compilation**

Run: `dotnet build XUnityToolkit-WebUI/XUnityToolkit-WebUI.csproj -p:SkipFrontendBuild=true`
Expected: Build succeeded

- [ ] **Step 3: Commit**

```bash
git add XUnityToolkit-WebUI/Services/DistanceFieldGenerator.cs
git commit -m "feat: 实现 Felzenszwalb EDT 距离场生成器"
```

---

### Task 2: Update Models and Endpoints

**Files:**
- Modify: `XUnityToolkit-WebUI/Models/FontGeneration.cs`
- Modify: `XUnityToolkit-WebUI/Endpoints/FontGenerationEndpoints.cs`

- [ ] **Step 1: Add new fields to FontGenerationRequest record**

In `Models/FontGeneration.cs`, replace the `FontGenerationRequest` record:

```csharp
public record FontGenerationRequest(
    string FontFilePath,
    string UnityVersion,
    int SamplingSize = 64,
    int AtlasWidth = 4096,
    int AtlasHeight = 4096,
    CharacterSetConfig? CharacterSet = null,
    string RenderMode = "SDFAA",
    string SamplingSizeMode = "manual",
    string PaddingMode = "percentage",
    int PaddingValue = 10
);
```

- [ ] **Step 2: Add new fields to FontGenerationReport**

In `Models/FontGeneration.cs`, add to the `FontGenerationReport` record (after `ElapsedMilliseconds`):

```csharp
public string RenderMode { get; init; } = "SDFAA";
public string SamplingSizeMode { get; init; } = "manual";
public int ActualSamplingSize { get; init; }
public int Padding { get; init; }
public int GradientScale { get; init; }
```

- [ ] **Step 3: Update FontGenerationStartRequest in endpoints**

In `FontGenerationEndpoints.cs`, replace the `FontGenerationStartRequest` record (line 316):

```csharp
private record FontGenerationStartRequest(
    string FileName,
    string UnityVersion,
    int SamplingSize = 64,
    int AtlasWidth = 4096,
    int AtlasHeight = 4096,
    CharacterSetConfig? CharacterSet = null,
    string RenderMode = "SDFAA",
    string SamplingSizeMode = "manual",
    string PaddingMode = "percentage",
    int PaddingValue = 10
);
```

- [ ] **Step 4: Update the /generate endpoint to pass new fields**

In `FontGenerationEndpoints.cs`, update the `FontGenerationRequest` construction in the `/generate` handler (line 60):

```csharp
var request = new FontGenerationRequest(
    fontPath,
    body.UnityVersion,
    body.SamplingSize,
    body.AtlasWidth,
    body.AtlasHeight,
    body.CharacterSet,
    body.RenderMode,
    body.SamplingSizeMode,
    body.PaddingMode,
    body.PaddingValue
);
```

- [ ] **Step 5: Build to verify compilation**

Run: `dotnet build XUnityToolkit-WebUI/XUnityToolkit-WebUI.csproj -p:SkipFrontendBuild=true`
Expected: Build succeeded

- [ ] **Step 6: Commit**

```bash
git add XUnityToolkit-WebUI/Models/FontGeneration.cs XUnityToolkit-WebUI/Endpoints/FontGenerationEndpoints.cs
git commit -m "feat: 添加 SDF 渲染模式、padding 模式、自动采样大小配置字段"
```

---

### Task 3: Refactor TmpFontGeneratorService — Phase 2 SDF Generation

**Files:**
- Modify: `XUnityToolkit-WebUI/Services/TmpFontGeneratorService.cs`

This is the core refactoring. Changes are described as diffs against the current file.

- [ ] **Step 1: Update constants and add helper methods**

Replace the three constants (lines 37-39):

```csharp
// Old:
private const int DefaultPadding = 9;
private const int DefaultSpread = 4;
private const int SdfAtlasRenderMode = 4165;

// New:
private static int GetUpsampling(string renderMode) => renderMode switch
{
    "SDF8" => 8,
    "SDF16" => 16,
    "SDF32" => 32,
    _ => 1, // SDFAA
};

private static int GetAtlasRenderMode(string renderMode) => renderMode switch
{
    "SDF8" => 4168,
    "SDF16" => 4169,
    "SDF32" => 4170,
    _ => 4165, // SDFAA
};

private static int GetMaxSamplingSize(string renderMode) => renderMode switch
{
    "SDF8" => 2048,
    "SDF16" => 1024,
    "SDF32" => 512,
    _ => int.MaxValue,
};

private static int CalculatePadding(int samplingSize, string paddingMode, int paddingValue)
{
    int padding = paddingMode == "pixel"
        ? paddingValue
        : (int)(samplingSize * paddingValue / 100f);
    return Math.Max(1, padding);
}
```

- [ ] **Step 2: Refactor Phase 1 — remove SDF spread, adjust pixel size for upsampling**

In `GenerateCore`, replace lines 166-177 (pixel size setting and SDF spread):

```csharp
// Calculate parameters
int upsampling = GetUpsampling(request.RenderMode);
int actualSamplingSize = request.SamplingSize;
int padding = CalculatePadding(actualSamplingSize, request.PaddingMode, request.PaddingValue);
int renderSize = actualSamplingSize * upsampling;

// Validate constraints
int maxSize = GetMaxSamplingSize(request.RenderMode);
if (request.SamplingSize > maxSize)
    throw new InvalidOperationException(
        $"渲染模式 {request.RenderMode} 最大采样大小为 {maxSize}，当前设置为 {request.SamplingSize}");
if (renderSize > 16384)
    throw new InvalidOperationException(
        $"采样大小 {request.SamplingSize} × 上采样 {upsampling} = {renderSize} 超过 16384 限制");

// Set pixel size (at upsampled resolution)
FT_Set_Pixel_Sizes(face, 0, (uint)renderSize);

// Extract font name
var fontName = Marshal.PtrToStringAnsi((IntPtr)face->family_name) ?? "Unknown";
logger.LogInformation("字体已加载: {FontName}, 采样大小: {Size}px, 上采样: {Up}x, Padding: {Pad}, 字符数: {Count}",
    fontName, request.SamplingSize, upsampling, padding, chars.Count);
```

Note: Remove the entire SDF spread block (`var sdfBytes`, `var spreadBytes`, `FT_Property_Set`).

Also update the SDF phase progress messages (lines 192, 207, 253) to include the render mode:
```csharp
var modeLabel = upsampling > 1 ? $"{request.RenderMode} ({renderSize}px)" : "SDFAA";
BroadcastProgress("sdf", 0, chars.Count, $"正在生成 SDF 位图 [{modeLabel}]...", force: true);
// ... and in the per-glyph progress:
BroadcastProgress("sdf", i + 1, chars.Count, $"正在生成 SDF 位图 [{modeLabel}] ({i + 1}/{chars.Count})...");
```

- [ ] **Step 3: Refactor Phase 2 loop — AA bitmap → EDT → SDF**

Replace the inner glyph rendering loop (lines 211-253):

```csharp
error = FT_Load_Glyph(face, glyphIndex, FT_LOAD.FT_LOAD_NO_BITMAP | FT_LOAD.FT_LOAD_NO_HINTING);
if (error != FT_Error.FT_Err_Ok) continue;

error = FT_Render_Glyph(face->glyph, FT_RENDER_MODE_NORMAL);
if (error != FT_Error.FT_Err_Ok) continue;

ref var bitmap = ref face->glyph->bitmap;
ref var metrics = ref face->glyph->metrics;

int bmpWidth = (int)bitmap.width;
int bmpHeight = (int)bitmap.rows;

int sdfWidth = 0, sdfHeight = 0;
if (bmpWidth > 0 && bmpHeight > 0 && bitmap.buffer != null)
{
    // Copy AA bitmap from FreeType
    var aaBitmap = new byte[bmpWidth * bmpHeight];
    int pitch = bitmap.pitch;
    for (int row = 0; row < bmpHeight; row++)
    {
        var src = new ReadOnlySpan<byte>(bitmap.buffer + row * pitch, bmpWidth);
        src.CopyTo(aaBitmap.AsSpan(row * bmpWidth));
    }

    // Generate SDF via EDT
    var sdfBitmap = DistanceFieldGenerator.GenerateSdf(
        aaBitmap, bmpWidth, bmpHeight,
        padding, upsampling,
        out sdfWidth, out sdfHeight);

    // Save SDF (padded) to disk
    var tempPath = Path.Combine(tempDir, $"{charCode}.bin");
    File.WriteAllBytes(tempPath, sdfBitmap);
}

// Metrics: divide by upsampling to get target-space values
float metricScale = 1f / upsampling;
glyphs.Add(new GlyphData
{
    Index = glyphIndex,
    Unicode = charCode,
    Width = (int)metrics.width / 64f * metricScale,
    Height = (int)metrics.height / 64f * metricScale,
    BearingX = (int)metrics.horiBearingX / 64f * metricScale,
    BearingY = (int)metrics.horiBearingY / 64f * metricScale,
    Advance = (int)metrics.horiAdvance / 64f * metricScale,
    BitmapData = null,
    BitmapWidth = sdfWidth,   // Padded dimensions
    BitmapHeight = sdfHeight, // Padded dimensions
});
```

- [ ] **Step 4: Update face metrics extraction for upsampling**

The face metrics (ascender, descender, lineHeight) at lines 185-188 are at the upsampled resolution. Add scaling:

```csharp
var sizeMetrics = face->size->metrics;
float metricDivisor = 64f * upsampling;
float ascender = (int)sizeMetrics.ascender / metricDivisor;
float descender = (int)sizeMetrics.descender / metricDivisor;
float lineHeight = (int)sizeMetrics.height / metricDivisor;
```

- [ ] **Step 5: Build to verify compilation**

Run: `dotnet build XUnityToolkit-WebUI/XUnityToolkit-WebUI.csproj -p:SkipFrontendBuild=true`
Expected: Build succeeded

- [ ] **Step 6: Commit**

```bash
git add XUnityToolkit-WebUI/Services/TmpFontGeneratorService.cs
git commit -m "feat: 重构 SDF 生成管线，使用 AA 位图 + EDT 替代 FT_RENDER_MODE_SDF"
```

---

### Task 4: Refactor Packing, Compositing, and Serialization

**Files:**
- Modify: `XUnityToolkit-WebUI/Services/TmpFontGeneratorService.cs`

- [ ] **Step 1: Update CreatePackingRects — no double-padding**

Replace `CreatePackingRects` (lines 761-769):

```csharp
private static PackingRectangle[] CreatePackingRects(IList<GlyphData> glyphs)
{
    var rects = new PackingRectangle[glyphs.Count];
    for (int i = 0; i < glyphs.Count; i++)
        rects[i] = new PackingRectangle(0, 0,
            (uint)glyphs[i].BitmapWidth,   // Already includes padding
            (uint)glyphs[i].BitmapHeight,  // Already includes padding
            i);
    return rects;
}
```

- [ ] **Step 2: Update ApplyPackedPositions — raw positions**

Replace `ApplyPackedPositions` (lines 771-778):

```csharp
private static void ApplyPackedPositions(IList<GlyphData> glyphs, PackingRectangle[] rects)
{
    foreach (var r in rects)
    {
        glyphs[(int)r.Id].AtlasX = (int)r.X;  // Raw packed position (top-left of padded bitmap)
        glyphs[(int)r.Id].AtlasY = (int)r.Y;
    }
}
```

- [ ] **Step 3: Update InjectIntoTemplate signature and scalar fields**

Add `padding` and `renderMode` parameters to `InjectIntoTemplate`. Update the call site (line 319) and the method signature (line 375).

Updated call:
```csharp
InjectIntoTemplate(request, fontName, allGlyphs, atlasPages,
    request.AtlasWidth, request.AtlasHeight, ascender, descender, lineHeight,
    padding, actualSamplingSize, outputPath);
```

Updated signature:
```csharp
private void InjectIntoTemplate(
    FontGenerationRequest request, string fontName,
    List<GlyphData> allGlyphs, List<byte[]> atlasPages,
    int atlasWidth, int atlasHeight,
    float ascender, float descender, float lineHeight,
    int padding, int actualSamplingSize, string outputPath)
```

Update scalar fields (lines 530-534):
```csharp
fontBase["m_AtlasWidth"].AsInt = atlasWidth;
fontBase["m_AtlasHeight"].AsInt = atlasHeight;
fontBase["m_AtlasPadding"].AsInt = padding;
fontBase["m_AtlasRenderMode"].AsInt = GetAtlasRenderMode(request.RenderMode);
fontBase["m_AtlasTextureIndex"].AsInt = 0;
```

Also update `m_FaceInfo.m_PointSize` (line 519) to use `actualSamplingSize`:
```csharp
faceInfo["m_PointSize"].AsInt = actualSamplingSize;
```

- [ ] **Step 4: Update GlyphRect serialization — inner glyph region**

Replace the GlyphRect injection (lines 459-463):

```csharp
var gr = entry["m_GlyphRect"];
gr["m_X"].AsInt = g.AtlasX + padding;                      // Inner position
gr["m_Y"].AsInt = g.AtlasY + padding;                      // Inner position
gr["m_Width"].AsInt = g.BitmapWidth - 2 * padding;         // Unpadded glyph width
gr["m_Height"].AsInt = g.BitmapHeight - 2 * padding;       // Unpadded glyph height
```

- [ ] **Step 5: Update UsedGlyphRects — full padded region**

Replace UsedGlyphRects injection (lines 492-508):

```csharp
var usedRectsArray = fontBase["m_UsedGlyphRects"];
if (!usedRectsArray.IsDummy && usedRectsArray["Array"].Children.Count > 0)
{
    var rectProto = usedRectsArray["Array"].Children[0];
    var newRects = new List<AssetTypeValueField>();
    foreach (var g in allGlyphs.Where(g => g.BitmapWidth > 0 && g.BitmapHeight > 0))
    {
        var entry = ValueBuilder.DefaultValueFieldFromTemplate(rectProto.TemplateField);
        entry["m_X"].AsInt = g.AtlasX;              // Full padded region
        entry["m_Y"].AsInt = g.AtlasY;
        entry["m_Width"].AsInt = g.BitmapWidth;      // Already includes padding
        entry["m_Height"].AsInt = g.BitmapHeight;
        newRects.Add(entry);
    }
    usedRectsArray["Array"].Children = newRects;
}
```

- [ ] **Step 6: Add Material _GradientScale injection**

After the existing atlas texture replacement block and before the bundle write, add Material property injection. Insert after `fontMbInfo.SetNewData(fontBase)` (line 654):

```csharp
// === Inject Material _GradientScale ===
var materialPPtr = fontBase["material"];
if (!materialPPtr.IsDummy)
{
    var matPathId = materialPPtr["m_PathID"].AsLong;
    foreach (var matInfo in afileInst.file.GetAssetsOfType(AssetClassID.Material))
    {
        if (matInfo.PathId != matPathId) continue;
        try
        {
            var matBase = manager.GetBaseField(afileInst, matInfo);
            var floats = matBase["m_SavedProperties"]["m_Floats"]["Array"];
            if (floats.IsDummy) break;

            int gradientScale = padding + 1;
            bool foundGS = false, foundTW = false, foundTH = false;
            foreach (var pair in floats.Children)
            {
                var key = pair["first"]["name"].AsString;
                if (key == "_GradientScale")
                    { pair["second"].AsFloat = gradientScale; foundGS = true; }
                else if (key == "_TextureWidth")
                    { pair["second"].AsFloat = atlasWidth; foundTW = true; }
                else if (key == "_TextureHeight")
                    { pair["second"].AsFloat = atlasHeight; foundTH = true; }
            }

            // Add missing properties
            if (floats.Children.Count > 0)
            {
                var proto = floats.Children[0];
                if (!foundGS)
                {
                    var e = ValueBuilder.DefaultValueFieldFromTemplate(proto.TemplateField);
                    e["first"]["name"].AsString = "_GradientScale";
                    e["second"].AsFloat = gradientScale;
                    floats.Children.Add(e);
                }
                if (!foundTW)
                {
                    var e = ValueBuilder.DefaultValueFieldFromTemplate(proto.TemplateField);
                    e["first"]["name"].AsString = "_TextureWidth";
                    e["second"].AsFloat = atlasWidth;
                    floats.Children.Add(e);
                }
                if (!foundTH)
                {
                    var e = ValueBuilder.DefaultValueFieldFromTemplate(proto.TemplateField);
                    e["first"]["name"].AsString = "_TextureHeight";
                    e["second"].AsFloat = atlasHeight;
                    floats.Children.Add(e);
                }
            }

            matInfo.SetNewData(matBase);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "无法注入 Material 属性 _GradientScale");
        }
        break;
    }
}
```

- [ ] **Step 7: Update the report generation**

Update the report creation (around line 328) to include new fields:

```csharp
var report = new FontGenerationReport
{
    FontName = fontName,
    TotalCharacters = chars.Count,
    SuccessfulGlyphs = allGlyphs.Count,
    MissingGlyphs = missingChars.Count,
    MissingCharacters = missingChars.Take(500).Select(c => char.ConvertFromUtf32(c)).ToList(),
    TotalMissingCount = missingChars.Count,
    AtlasCount = atlasPages.Count,
    AtlasWidth = request.AtlasWidth,
    AtlasHeight = request.AtlasHeight,
    SamplingSize = request.SamplingSize,
    SourceBreakdown = sourceBreakdown ?? new(),
    ElapsedMilliseconds = stopwatch.ElapsedMilliseconds,
    RenderMode = request.RenderMode,
    SamplingSizeMode = request.SamplingSizeMode,
    ActualSamplingSize = actualSamplingSize,
    Padding = padding,
    GradientScale = padding + 1,
};
```

- [ ] **Step 8: Remove unused import**

Remove `using static FreeTypeSharp.FT_Render_Mode_;` if `FT_RENDER_MODE_NORMAL` is accessed differently, OR keep it. Check that `FT_RENDER_MODE_NORMAL` compiles. FreeTypeSharp's `FT_Render_Mode_` enum should have this member.

- [ ] **Step 9: Build and verify**

Run: `dotnet build XUnityToolkit-WebUI/XUnityToolkit-WebUI.csproj -p:SkipFrontendBuild=true`
Expected: Build succeeded

- [ ] **Step 10: Commit**

```bash
git add XUnityToolkit-WebUI/Services/TmpFontGeneratorService.cs
git commit -m "feat: 重构打包/合成/序列化管线，注入 GradientScale 和动态 padding"
```

---

### Task 5: Add Auto-Sizing Support

**Files:**
- Modify: `XUnityToolkit-WebUI/Services/TmpFontGeneratorService.cs`

- [ ] **Step 1: Add auto-sizing method**

Add after the `SanitizeFileName` method:

```csharp
private unsafe int AutoSizeSamplingSize(
    FT_FaceRec_* face, List<int> chars,
    int atlasWidth, int atlasHeight, int padding, int upsampling)
{
    int glyphCount = chars.Count;
    if (glyphCount == 0) return 64;

    int maxSize = (int)(MathF.Sqrt((float)atlasWidth * atlasHeight / glyphCount) * 3);
    if (upsampling > 1)
        maxSize = Math.Min(maxSize, 16384 / upsampling);

    int minSize = 4;
    int bestSize = minSize;

    for (int iter = 0; iter < 15; iter++)
    {
        int testSize = (minSize + maxSize) / 2;
        if (testSize <= 0) break;

        FT_Set_Pixel_Sizes(face, 0, (uint)(testSize * upsampling));

        // Estimate glyph dimensions by sampling a few characters
        var sampleGlyphs = new List<(int w, int h)>();
        int sampleCount = Math.Min(chars.Count, 200);
        int step = Math.Max(1, chars.Count / sampleCount);

        for (int i = 0; i < chars.Count && sampleGlyphs.Count < sampleCount; i += step)
        {
            var glyphIndex = FT_Get_Char_Index(face, (UIntPtr)(uint)chars[i]);
            if (glyphIndex == 0) continue;

            if (FT_Load_Glyph(face, glyphIndex, FT_LOAD.FT_LOAD_NO_BITMAP | FT_LOAD.FT_LOAD_NO_HINTING) != FT_Error.FT_Err_Ok)
                continue;

            ref var metrics = ref face->glyph->metrics;
            int w = (int)Math.Ceiling((int)metrics.width / 64.0 / upsampling) + 2 * padding;
            int h = (int)Math.Ceiling((int)metrics.height / 64.0 / upsampling) + 2 * padding;
            if (w > 0 && h > 0)
                sampleGlyphs.Add((w, h));
        }

        if (sampleGlyphs.Count == 0) { maxSize = testSize; continue; }

        // Estimate total area
        double avgArea = sampleGlyphs.Average(g => (double)g.w * g.h);
        double totalArea = avgArea * glyphCount;
        double atlasArea = (double)atlasWidth * atlasHeight * 0.85; // 85% utilization

        if (totalArea <= atlasArea)
        {
            bestSize = testSize;
            minSize = testSize;
        }
        else
        {
            maxSize = testSize;
        }

        if (maxSize - minSize <= 1) break;
    }

    return bestSize;
}
```

- [ ] **Step 2: Integrate auto-sizing into GenerateCore**

After the `FT_Set_Pixel_Sizes` call and before the SDF rendering loop, add auto-sizing check:

```csharp
// Auto-sizing: binary search for optimal sampling size
int actualSamplingSize = request.SamplingSize;
if (request.SamplingSizeMode == "auto")
{
    BroadcastProgress("parsing", 0, 1, "正在自动计算最优采样大小...", force: true);
    actualSamplingSize = AutoSizeSamplingSize(face, chars,
        request.AtlasWidth, request.AtlasHeight, padding, upsampling);
    renderSize = actualSamplingSize * upsampling;
    FT_Set_Pixel_Sizes(face, 0, (uint)renderSize);

    // Recalculate padding if percentage mode
    if (request.PaddingMode == "percentage")
        padding = Math.Max(1, (int)(actualSamplingSize * request.PaddingValue / 100f));

    // Re-extract face metrics at new size
    sizeMetrics = face->size->metrics;
    ascender = (int)sizeMetrics.ascender / (64f * upsampling);
    descender = (int)sizeMetrics.descender / (64f * upsampling);
    lineHeight = (int)sizeMetrics.height / (64f * upsampling);

    logger.LogInformation("自动采样大小: {Size}px", actualSamplingSize);
}
```

Update `renderSize` and the local variables to be `var` (not `int`) so they can be reassigned. Also update the report's `ActualSamplingSize` to use `actualSamplingSize`.

- [ ] **Step 3: Build and verify**

Run: `dotnet build XUnityToolkit-WebUI/XUnityToolkit-WebUI.csproj -p:SkipFrontendBuild=true`
Expected: Build succeeded

- [ ] **Step 4: Commit**

```bash
git add XUnityToolkit-WebUI/Services/TmpFontGeneratorService.cs
git commit -m "feat: 添加自动采样大小二分搜索"
```

---

## Chunk 2: Frontend — TypeScript Types + Vue UI

### Task 6: Update TypeScript Types

**Files:**
- Modify: `XUnityToolkit-Vue/src/api/types.ts`

- [ ] **Step 1: Update FontGenerationReport interface**

Find the `FontGenerationReport` interface and add new fields at the end:

```typescript
interface FontGenerationReport {
  // ... existing fields ...
  elapsedMilliseconds: number
  // New fields:
  renderMode: string
  samplingSizeMode: string
  actualSamplingSize: number
  padding: number
  gradientScale: number
}
```

- [ ] **Step 2: Commit**

```bash
git add XUnityToolkit-Vue/src/api/types.ts
git commit -m "feat: 同步前端 FontGenerationReport 类型定义"
```

---

### Task 7: Update FontGeneratorView.vue

**Files:**
- Modify: `XUnityToolkit-Vue/src/views/FontGeneratorView.vue`

- [ ] **Step 1: Add reactive state for new settings**

In the `<script setup>` section, find the existing settings refs (around the `samplingSize`, `atlasSize` refs) and add:

```typescript
const renderMode = ref<'SDFAA' | 'SDF8' | 'SDF16' | 'SDF32'>('SDFAA')
const samplingSizeMode = ref<'auto' | 'manual'>('manual')
const paddingMode = ref<'percentage' | 'pixel'>('percentage')
const paddingValue = ref(10)

const renderModeOptions = [
  { label: 'SDFAA (默认)', value: 'SDFAA' },
  { label: 'SDF8 (8x 上采样)', value: 'SDF8' },
  { label: 'SDF16 (16x 上采样)', value: 'SDF16' },
  { label: 'SDF32 (32x 上采样，最高质量)', value: 'SDF32' },
]
```

- [ ] **Step 2: Add UI controls in the settings section**

In the template, find the settings section. **Remove** the existing sampling size form-row (the `<div>` or `<n-form-item>` containing the sampling size `<n-select>`), as the new UI below replaces it. Then add after the atlas size selector:

```html
<n-form-item label="渲染模式">
  <n-select v-model:value="renderMode" :options="renderModeOptions" />
</n-form-item>

<n-form-item label="采样大小">
  <n-space vertical>
    <n-radio-group v-model:value="samplingSizeMode">
      <n-radio value="manual">手动</n-radio>
      <n-radio value="auto">自动（最优大小）</n-radio>
    </n-radio-group>
    <n-select
      v-if="samplingSizeMode === 'manual'"
      v-model:value="samplingSize"
      :options="samplingSizeOptions"
    />
  </n-space>
</n-form-item>

<n-form-item label="Padding">
  <n-space>
    <n-radio-group v-model:value="paddingMode">
      <n-radio value="percentage">百分比</n-radio>
      <n-radio value="pixel">像素</n-radio>
    </n-radio-group>
    <n-input-number
      v-model:value="paddingValue"
      :min="1"
      :max="paddingMode === 'percentage' ? 50 : 100"
      size="small"
      style="width: 100px"
    />
    <span>{{ paddingMode === 'percentage' ? '%' : 'px' }}</span>
  </n-space>
</n-form-item>
```

Note: The exact template structure should match the existing Naive UI patterns in the file. Adjust `n-form-item` / `n-space` nesting to fit the current layout. Ensure `NInputNumber`, `NRadioGroup`, and `NRadio` are imported from `naive-ui` if not already present in the component imports.

- [ ] **Step 3: Update the startGeneration function**

Find the `startGeneration` function and update the fetch body to include new fields:

```typescript
// In the request body object, add:
renderMode: renderMode.value,
samplingSizeMode: samplingSizeMode.value,
paddingMode: paddingMode.value,
paddingValue: paddingValue.value,
```

- [ ] **Step 4: Update report display to show new fields**

In the report display section, add after existing report stats:

```html
<n-descriptions-item label="渲染模式">{{ report.renderMode }}</n-descriptions-item>
<n-descriptions-item label="实际采样大小" v-if="report.samplingSizeMode === 'auto'">
  {{ report.actualSamplingSize }}px
</n-descriptions-item>
<n-descriptions-item label="Padding">{{ report.padding }}px</n-descriptions-item>
<n-descriptions-item label="GradientScale">{{ report.gradientScale }}</n-descriptions-item>
```

- [ ] **Step 5: Build frontend**

Run: `cd XUnityToolkit-Vue && npx vue-tsc --noEmit`
Expected: No type errors

- [ ] **Step 6: Commit**

```bash
git add XUnityToolkit-Vue/src/views/FontGeneratorView.vue XUnityToolkit-Vue/src/api/types.ts
git commit -m "feat: 前端添加渲染模式、自动采样、padding 模式 UI 控件"
```

---

### Task 8: Verify AtlasRenderMode Values from Unity

**Files:** None (research task)

- [ ] **Step 1: Create test fonts in Unity editor**

In the Unity project at `D:\Document\GameProject\Unity\TMPFont6000`:
1. Open Unity Editor
2. Go to `Window → TextMeshPro → Font Asset Creator`
3. Load a font (e.g., Arial)
4. Create 4 font assets with render modes: SDFAA, SDF8, SDF16, SDF32
5. Save each as a `.asset` file

- [ ] **Step 2: Inspect m_AtlasRenderMode in each .asset file**

Open each `.asset` file in a text editor (they are YAML) and search for `m_AtlasRenderMode`. Record the actual integer value for each mode.

If the values differ from the estimates (SDFAA=4165, SDF8=4168, SDF16=4169, SDF32=4170), update the `GetAtlasRenderMode` helper in `TmpFontGeneratorService.cs`.

- [ ] **Step 3: Commit if values changed**

```bash
git add XUnityToolkit-WebUI/Services/TmpFontGeneratorService.cs
git commit -m "fix: 修正 AtlasRenderMode 枚举值为 Unity 实际值"
```

---

### Task 9: End-to-End Verification

- [ ] **Step 1: Build the full project**

Run: `dotnet build XUnityToolkit-WebUI/XUnityToolkit-WebUI.csproj`
Expected: Build succeeded (includes frontend)

- [ ] **Step 2: Run the backend**

Run: `dotnet run --project XUnityToolkit-WebUI/XUnityToolkit-WebUI.csproj`
Open: `http://127.0.0.1:51821`

- [ ] **Step 3: Test SDFAA mode**

1. Navigate to Font Generator
2. Upload a TTF font
3. Select render mode: SDFAA, manual sampling size 64
4. Generate with GB2312 charset
5. Verify: generation completes, report shows renderMode=SDFAA, padding > 0, gradientScale > 0
6. Download and install the generated bundle to a game

- [ ] **Step 4: Test SDF16 mode**

1. Same font, select SDF16 render mode
2. Verify: generation completes (slower than SDFAA)
3. Compare SDF quality visually in the game

- [ ] **Step 5: Test auto-sizing**

1. Same font, select auto sampling size mode
2. Verify: generation completes, report shows actualSamplingSize != 0

- [ ] **Step 6: Test percentage padding**

1. Set padding mode to percentage, value 10%
2. Verify report shows correct padding value (samplingSize * 10 / 100)

- [ ] **Step 7: Final commit**

```bash
git add -A
git commit -m "chore: 完成 Unity SDF 复刻管线端到端验证"
```
