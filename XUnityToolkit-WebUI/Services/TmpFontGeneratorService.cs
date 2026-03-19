using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using AssetsTools.NET;
using AssetsTools.NET.Extra;
using AssetsTools.NET.Texture;
using FreeTypeSharp;
using Microsoft.AspNetCore.SignalR;
using RectpackSharp;
using XUnityToolkit_WebUI.Hubs;
using XUnityToolkit_WebUI.Infrastructure;
using XUnityToolkit_WebUI.Models;
using XUnityToolkit_WebUI.Services.CharacterSets;
using static FreeTypeSharp.FT;
using static FreeTypeSharp.FT_Render_Mode_;

namespace XUnityToolkit_WebUI.Services;

public sealed class TmpFontGeneratorService(
    TmpFontService tmpFontService,
    CharacterSetService characterSetService,
    AppDataPaths appDataPaths,
    SystemTrayService trayService,
    IHubContext<InstallProgressHub> hubContext,
    ILogger<TmpFontGeneratorService> logger)
{
    private static readonly Lazy<byte[]> ClassDataTpk = new(() =>
    {
        using var stream = Assembly.GetExecutingAssembly()
            .GetManifestResourceStream("classdata.tpk")
            ?? throw new InvalidOperationException("Embedded classdata.tpk not found.");
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        return ms.ToArray();
    });

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

    private readonly SemaphoreSlim _generationSemaphore = new(1, 1);
    private CancellationTokenSource? _cts;
    private long _lastBroadcastTicks;

    // Progress state
    private volatile bool _isGenerating;
    public bool IsGenerating => _isGenerating;
    private volatile string? _phase;
    private volatile int _current;
    private volatile int _total;

    public FontGenerationStatus GetStatus() => new()
    {
        IsGenerating = _isGenerating,
        Phase = _phase,
        Current = _current,
        Total = _total,
    };

    public void Cancel()
    {
        _cts?.Cancel();
        logger.LogInformation("字体生成已取消");
    }

    public async Task<FontGenerationResult> GenerateAsync(FontGenerationRequest request)
    {
        if (!await _generationSemaphore.WaitAsync(0))
            return new FontGenerationResult(false, null, null, 0, 0, 0, "已有生成任务正在进行中");

        _cts = new CancellationTokenSource();
        var ct = _cts.Token;
        _isGenerating = true;
        _phase = "parsing";
        _current = 0;
        _total = 0;

        // Resolve characters BEFORE Task.Run (async, not unsafe)
        List<int> chars;
        Dictionary<string, int>? sourceBreakdown = null;
        try
        {
            if (request.CharacterSet != null)
            {
                var (resolved, preview) = await characterSetService.ResolveCharactersAsync(
                    request.CharacterSet, request.AtlasWidth, request.AtlasHeight, request.SamplingSize);
                chars = resolved.OrderBy(c => c).ToList();
                sourceBreakdown = preview.SourceBreakdown;
            }
            else
            {
                var set = BuiltinCharsets.GB2312();
                set.UnionWith(BuiltinCharsets.Ascii());
                set.UnionWith(BuiltinCharsets.CommonPunctuation());
                chars = set.OrderBy(c => c).ToList();
            }
        }
        catch (Exception ex)
        {
            // Clean up on charset resolution failure
            logger.LogError(ex, "字符集解析失败");
            _isGenerating = false;
            _phase = null;
            _cts?.Dispose();
            _cts = null;
            _generationSemaphore.Release();
            return new FontGenerationResult(false, null, null, 0, 0, 0, "字符集解析失败，请检查字符集配置");
        }

        try
        {
            return await Task.Run(() => GenerateCore(request, chars, sourceBreakdown, ct), CancellationToken.None);
        }
        catch (OperationCanceledException)
        {
            logger.LogInformation("字体生成已被用户取消");
            return new FontGenerationResult(false, null, null, 0, 0, 0, "已取消");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "字体生成失败");
            return new FontGenerationResult(false, null, null, 0, 0, 0, "字体生成失败，请检查字体文件格式");
        }
        finally
        {
            _isGenerating = false;
            _phase = null;
            _current = 0;
            _total = 0;
            _cts?.Dispose();
            _cts = null;
            _generationSemaphore.Release();
        }
    }

    private unsafe FontGenerationResult GenerateCore(
        FontGenerationRequest request, List<int> chars, Dictionary<string, int>? sourceBreakdown,
        CancellationToken ct)
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        // Create temp directory for SDF bitmaps (disk-backed to reduce memory pressure)
        var sessionId = Guid.NewGuid().ToString("N");
        var tempDir = Path.Combine(appDataPaths.FontGenerationTempDirectory, sessionId);
        Directory.CreateDirectory(tempDir);

        // === Phase 1: Parse font ===
        BroadcastProgress("parsing", 0, 1, "正在解析字体...", force: true);

        FT_LibraryRec_* lib;
        FT_FaceRec_* face;

        var error = FT_Init_FreeType(&lib);
        if (error != FT_Error.FT_Err_Ok)
            throw new InvalidOperationException($"FreeType 初始化失败: {error}");

        try
        {
            var fontPathBytes = Encoding.UTF8.GetBytes(request.FontFilePath + '\0');
            fixed (byte* pathPtr = fontPathBytes)
            {
                error = FT_New_Face(lib, pathPtr, 0, &face);
            }
            if (error != FT_Error.FT_Err_Ok)
                throw new InvalidOperationException($"无法加载字体文件: {error}");

            try
            {
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

                // Extract face metrics (26.6 fixed-point → float, scaled by upsampling)
                var sizeMetrics = face->size->metrics;
                float ascender = (int)sizeMetrics.ascender / (64f * upsampling);
                float descender = (int)sizeMetrics.descender / (64f * upsampling);
                float lineHeight = (int)sizeMetrics.height / (64f * upsampling);

                // Auto-sizing: binary search for optimal sampling size
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

                // === Phase 2: Generate SDF bitmaps ===
                _total = chars.Count;
                var modeLabel = upsampling > 1 ? $"{request.RenderMode} ({renderSize}px)" : "SDFAA";
                BroadcastProgress("sdf", 0, chars.Count, $"正在生成 SDF 位图 [{modeLabel}]...", force: true);

                var glyphs = new List<GlyphData>();
                var missingChars = new List<int>();
                float metricScale = 1f / upsampling;

                for (int i = 0; i < chars.Count; i++)
                {
                    ct.ThrowIfCancellationRequested();

                    var charCode = (uint)chars[i];
                    var glyphIndex = FT_Get_Char_Index(face, (UIntPtr)charCode);
                    if (glyphIndex == 0)
                    {
                        missingChars.Add(chars[i]);
                        _current = i + 1;
                        BroadcastProgress("sdf", i + 1, chars.Count,
                            $"正在生成 SDF 位图 [{modeLabel}] ({i + 1}/{chars.Count})...");
                        continue;
                    }

                    error = FT_Load_Glyph(face, glyphIndex, FT_LOAD.FT_LOAD_NO_BITMAP | FT_LOAD.FT_LOAD_NO_HINTING);
                    if (error != FT_Error.FT_Err_Ok)
                    {
                        missingChars.Add(chars[i]);
                        _current = i + 1;
                        BroadcastProgress("sdf", i + 1, chars.Count,
                            $"正在生成 SDF 位图 [{modeLabel}] ({i + 1}/{chars.Count})...");
                        continue;
                    }

                    error = FT_Render_Glyph(face->glyph, FT_RENDER_MODE_NORMAL);
                    if (error != FT_Error.FT_Err_Ok)
                    {
                        missingChars.Add(chars[i]);
                        _current = i + 1;
                        BroadcastProgress("sdf", i + 1, chars.Count,
                            $"正在生成 SDF 位图 [{modeLabel}] ({i + 1}/{chars.Count})...");
                        continue;
                    }

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
                        BitmapHeight = sdfHeight,  // Padded dimensions
                    });

                    _current = i + 1;
                    BroadcastProgress("sdf", i + 1, chars.Count,
                        $"正在生成 SDF 位图 [{modeLabel}] ({i + 1}/{chars.Count})...");
                }

                logger.LogInformation("SDF 生成完成: {Count}/{Total} 个字形, {Missing} 个缺失",
                    glyphs.Count, chars.Count, missingChars.Count);

                // === Phase 3: Pack atlas (multi-page) ===
                _phase = "packing";
                BroadcastProgress("packing", 0, 1, "正在排列字形...", force: true);

                var glyphsToLayout = glyphs.Where(g => g.BitmapWidth > 0 && g.BitmapHeight > 0).ToList();
                var pages = PackGlyphs(glyphsToLayout, request.AtlasWidth, request.AtlasHeight);

                for (int pageIdx = 0; pageIdx < pages.Count; pageIdx++)
                    foreach (var g in pages[pageIdx])
                        g.AtlasIndex = pageIdx;

                BroadcastProgress("packing", 1, 1, $"排列完成，共 {pages.Count} 页", force: true);

                // === Phase 4: Composite atlas textures ===
                _phase = "compositing";
                var atlasPages = new List<byte[]>();

                for (int pageIdx = 0; pageIdx < pages.Count; pageIdx++)
                {
                    ct.ThrowIfCancellationRequested();
                    BroadcastProgress("compositing", pageIdx + 1, pages.Count,
                        $"正在合成纹理 {pageIdx + 1}/{pages.Count}...", force: pageIdx == 0);

                    var atlasBytes = new byte[request.AtlasWidth * request.AtlasHeight];
                    foreach (var g in pages[pageIdx])
                    {
                        var bitmapTempPath = Path.Combine(tempDir, $"{g.Unicode}.bin");
                        if (!File.Exists(bitmapTempPath)) continue;
                        var bitmapData = File.ReadAllBytes(bitmapTempPath);
                        if (bitmapData.Length != g.BitmapWidth * g.BitmapHeight) continue;

                        for (int row = 0; row < g.BitmapHeight; row++)
                        {
                            var srcOffset = row * g.BitmapWidth;
                            var dstOffset = (g.AtlasY + row) * request.AtlasWidth + g.AtlasX;
                            Buffer.BlockCopy(bitmapData, srcOffset, atlasBytes, dstOffset, g.BitmapWidth);
                        }
                    }

                    FlipAtlasY(atlasBytes, request.AtlasWidth, request.AtlasHeight);

                    // Convert glyph AtlasY from top-down to bottom-up for this page
                    foreach (var g in pages[pageIdx])
                        g.AtlasY = request.AtlasHeight - g.AtlasY - g.BitmapHeight;

                    atlasPages.Add(atlasBytes);
                }

                // === Phase 5: Serialize to AssetBundle ===
                BroadcastProgress("serializing", 0, 1, "正在序列化 AssetBundle...", force: true);

                var outputFileName = $"{SanitizeFileName(fontName)}_U{request.UnityVersion}.bundle";
                var outputPath = Path.Combine(appDataPaths.GeneratedFontsDirectory, outputFileName);

                // Collect all glyphs across all pages (for injection)
                var allGlyphs = pages.SelectMany(p => p).ToList();
                // Also include zero-size glyphs (no bitmap but valid metrics)
                var zeroSizeGlyphs = glyphs.Where(g => g.BitmapWidth == 0 || g.BitmapHeight == 0).ToList();
                allGlyphs.AddRange(zeroSizeGlyphs);

                InjectIntoTemplate(request, fontName, allGlyphs, atlasPages,
                    request.AtlasWidth, request.AtlasHeight, ascender, descender, lineHeight,
                    padding, actualSamplingSize, outputPath);

                // Clean up uploaded TTF
                try { File.Delete(request.FontFilePath); }
                catch { /* ignore cleanup errors */ }

                // Generate report
                stopwatch.Stop();
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

                var reportPath = Path.ChangeExtension(outputPath, ".report.json");
                File.WriteAllText(reportPath, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));

                var result = new FontGenerationResult(true, outputPath, fontName,
                    allGlyphs.Count, request.AtlasWidth, request.AtlasHeight, null, report);

                // Broadcast completion
                hubContext.Clients.Group("font-generation")
                    .SendAsync("FontGenerationComplete",
                        new FontGenerationComplete(true, fontName, allGlyphs.Count, null, report)).Wait();

                logger.LogInformation("字体生成完成: {FontName}, {Count} 个字形, {Pages} 页 Atlas, 耗时 {Ms}ms, 输出: {Path}",
                    fontName, allGlyphs.Count, atlasPages.Count, stopwatch.ElapsedMilliseconds, outputPath);

                trayService.ShowNotification("字体生成完成",
                    $"「{fontName}」已生成, {allGlyphs.Count} 个字形");

                return result;
            }
            finally
            {
                // Clean up temp directory
                try { if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true); }
                catch (Exception ex) { logger.LogWarning(ex, "Failed to clean up temp dir: {Dir}", tempDir); }

                FT_Done_Face(face);
            }
        }
        finally
        {
            FT_Done_FreeType(lib);
        }
    }

    private void InjectIntoTemplate(
        FontGenerationRequest request, string fontName,
        List<GlyphData> allGlyphs, List<byte[]> atlasPages,
        int atlasWidth, int atlasHeight,
        float ascender, float descender, float lineHeight,
        int padding, int actualSamplingSize, string outputPath)
    {
        var templatePath = tmpFontService.ResolveFontFile(request.UnityVersion)
            ?? throw new InvalidOperationException($"未找到 Unity {request.UnityVersion} 的模板字体");

        var manager = new AssetsManager();
        using var tpkStream = new MemoryStream(ClassDataTpk.Value);
        manager.LoadClassPackage(tpkStream);
        manager.UseTemplateFieldCache = true;

        var bunInst = manager.LoadBundleFile(templatePath, true);
        var dirInfos = bunInst.file.BlockAndDirInfo.DirectoryInfos;

        AssetsFileInstance? afileInst = null;
        AssetTypeValueField? fontBase = null;
        AssetFileInfo? fontMbInfo = null;
        int entryIndex = -1;

        // Find the TMP_FontAsset MonoBehaviour in the template
        for (int i = 0; i < dirInfos.Count; i++)
        {
            var entryName = dirInfos[i].Name;
            if (entryName.EndsWith(".resource", StringComparison.OrdinalIgnoreCase) ||
                entryName.EndsWith(".resS", StringComparison.OrdinalIgnoreCase))
                continue;

            var inst = manager.LoadAssetsFileFromBundle(bunInst, i, false);
            try { manager.LoadClassDatabaseFromPackage(inst.file.Metadata.UnityVersion); }
            catch { /* type tree embedded */ }

            foreach (var mbInfo in inst.file.GetAssetsOfType(AssetClassID.MonoBehaviour))
            {
                try
                {
                    var mbBase = manager.GetBaseField(inst, mbInfo);
                    if (mbBase.IsDummy) continue;
                    if (!mbBase["m_GlyphTable"].IsDummy && !mbBase["m_Version"].IsDummy)
                    {
                        afileInst = inst;
                        fontBase = mbBase;
                        fontMbInfo = mbInfo;
                        entryIndex = i;
                        break;
                    }
                }
                catch { /* skip */ }
            }
            if (fontBase != null) break;
        }

        if (fontBase == null || afileInst == null || fontMbInfo == null || entryIndex < 0)
            throw new InvalidOperationException("模板字体中未找到有效的 TMP_FontAsset");

        // Build a glyph index map for character table
        var glyphIndexMap = new Dictionary<uint, uint>(); // ftGlyphIndex → sequential index
        for (int i = 0; i < allGlyphs.Count; i++)
            glyphIndexMap[allGlyphs[i].Index] = (uint)i;

        // === Inject GlyphTable ===
        var glyphArray = fontBase["m_GlyphTable"]["Array"];
        if (glyphArray.Children.Count > 0)
        {
            var prototype = glyphArray.Children[0];
            var newGlyphs = new List<AssetTypeValueField>(allGlyphs.Count);

            for (int i = 0; i < allGlyphs.Count; i++)
            {
                var g = allGlyphs[i];

                var entry = ValueBuilder.DefaultValueFieldFromTemplate(prototype.TemplateField);
                entry["m_Index"].AsUInt = (uint)i;

                var m = entry["m_Metrics"];
                m["m_Width"].AsFloat = g.Width;
                m["m_Height"].AsFloat = g.Height;
                m["m_HorizontalBearingX"].AsFloat = g.BearingX;
                m["m_HorizontalBearingY"].AsFloat = g.BearingY;
                m["m_HorizontalAdvance"].AsFloat = g.Advance;

                var gr = entry["m_GlyphRect"];
                if (g.BitmapWidth > 0 && g.BitmapHeight > 0)
                {
                    gr["m_X"].AsInt = g.AtlasX + padding;                  // Inner glyph position
                    gr["m_Y"].AsInt = g.AtlasY + padding;                  // Inner glyph position
                    gr["m_Width"].AsInt = g.BitmapWidth - 2 * padding;     // Unpadded glyph width
                    gr["m_Height"].AsInt = g.BitmapHeight - 2 * padding;   // Unpadded glyph height
                }
                else
                {
                    // Zero-size glyphs (space, control chars): no visual region
                    gr["m_X"].AsInt = 0;
                    gr["m_Y"].AsInt = 0;
                    gr["m_Width"].AsInt = 0;
                    gr["m_Height"].AsInt = 0;
                }

                entry["m_Scale"].AsFloat = 1.0f;
                entry["m_AtlasIndex"].AsInt = g.AtlasIndex;

                newGlyphs.Add(entry);
            }
            glyphArray.Children = newGlyphs;
        }

        // === Inject CharacterTable ===
        var charArray = fontBase["m_CharacterTable"]["Array"];
        if (charArray.Children.Count > 0)
        {
            var charProto = charArray.Children[0];
            var newChars = new List<AssetTypeValueField>(allGlyphs.Count);

            foreach (var g in allGlyphs)
            {
                var entry = ValueBuilder.DefaultValueFieldFromTemplate(charProto.TemplateField);
                entry["m_ElementType"].AsInt = 1;
                entry["m_Unicode"].AsUInt = g.Unicode;
                entry["m_GlyphIndex"].AsUInt = glyphIndexMap[g.Index];
                entry["m_Scale"].AsFloat = 1.0f;
                newChars.Add(entry);
            }
            charArray.Children = newChars;
        }

        // === Inject UsedGlyphRects (all pages combined) ===
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

        // === Clear FreeGlyphRects ===
        var freeRectsArray = fontBase["m_FreeGlyphRects"];
        if (!freeRectsArray.IsDummy)
            freeRectsArray["Array"].Children = [];

        // === Set FaceInfo ===
        var faceInfo = fontBase["m_FaceInfo"];
        if (!faceInfo.IsDummy)
        {
            faceInfo["m_PointSize"].AsInt = actualSamplingSize;
            faceInfo["m_LineHeight"].AsFloat = lineHeight;
            faceInfo["m_AscentLine"].AsFloat = ascender;
            faceInfo["m_DescentLine"].AsFloat = descender;
            faceInfo["m_Baseline"].AsFloat = 0;
            faceInfo["m_UnderlineOffset"].AsFloat = descender * 0.5f;
            faceInfo["m_StrikethroughOffset"].AsFloat = ascender * 0.4f;
            faceInfo["m_TabWidth"].AsFloat = allGlyphs.Count > 0 ? allGlyphs[0].Advance * 4 : actualSamplingSize * 2;
        }

        // === Set scalar fields ===
        fontBase["m_AtlasWidth"].AsInt = atlasWidth;
        fontBase["m_AtlasHeight"].AsInt = atlasHeight;
        fontBase["m_AtlasPadding"].AsInt = padding;
        fontBase["m_AtlasRenderMode"].AsInt = GetAtlasRenderMode(request.RenderMode);
        fontBase["m_AtlasTextureIndex"].AsInt = 0;
        fontBase["m_Version"].AsString = "1.1.0";
        fontBase["m_Name"].AsString = fontName;

        // === Replace atlas Texture2D(s) ===
        var atlasTextures = fontBase["m_AtlasTextures"];
        if (!atlasTextures.IsDummy && atlasTextures["Array"].Children.Count > 0)
        {
            var existingTexCount = atlasTextures["Array"].Children.Count;

            // Replace page 0 texture (always exists in template)
            var page0PathId = atlasTextures["Array"][0]["m_PathID"].AsLong;
            foreach (var texInfo in afileInst.file.GetAssetsOfType(AssetClassID.Texture2D))
            {
                if (texInfo.PathId == page0PathId)
                {
                    ReplaceTexture(manager, afileInst, texInfo, atlasPages[0],
                        atlasWidth, atlasHeight, $"{fontName} Atlas");
                    break;
                }
            }

            // Handle additional pages
            if (atlasPages.Count > 1)
            {
                if (existingTexCount >= atlasPages.Count)
                {
                    // Template has enough texture slots — replace them
                    for (int pageIdx = 1; pageIdx < atlasPages.Count; pageIdx++)
                    {
                        var pagePathId = atlasTextures["Array"][pageIdx]["m_PathID"].AsLong;
                        foreach (var texInfo in afileInst.file.GetAssetsOfType(AssetClassID.Texture2D))
                        {
                            if (texInfo.PathId == pagePathId)
                            {
                                ReplaceTexture(manager, afileInst, texInfo, atlasPages[pageIdx],
                                    atlasWidth, atlasHeight, $"{fontName} Atlas {pageIdx}");
                                break;
                            }
                        }
                    }
                }
                else
                {
                    // Create additional Texture2D assets for pages beyond what the template has
                    // Find existing texture to use as a structural template
                    AssetTypeValueField? texTemplate = null;
                    var templateTexInfo = afileInst.file.GetAssetsOfType(AssetClassID.Texture2D).FirstOrDefault();
                    if (templateTexInfo != null)
                        texTemplate = manager.GetBaseField(afileInst, templateTexInfo);

                    if (texTemplate != null)
                    {
                        // Replace existing texture slots (pages 1..existingTexCount-1)
                        for (int pageIdx = 1; pageIdx < existingTexCount && pageIdx < atlasPages.Count; pageIdx++)
                        {
                            var pagePathId = atlasTextures["Array"][pageIdx]["m_PathID"].AsLong;
                            foreach (var texInfo in afileInst.file.GetAssetsOfType(AssetClassID.Texture2D))
                            {
                                if (texInfo.PathId == pagePathId)
                                {
                                    ReplaceTexture(manager, afileInst, texInfo, atlasPages[pageIdx],
                                        atlasWidth, atlasHeight, $"{fontName} Atlas {pageIdx}");
                                    break;
                                }
                            }
                        }

                        // Create new Texture2D assets for remaining pages
                        var ptrProto = atlasTextures["Array"].Children[0];
                        var newPtrList = new List<AssetTypeValueField>(atlasTextures["Array"].Children);

                        for (int pageIdx = existingTexCount; pageIdx < atlasPages.Count; pageIdx++)
                        {
                            var newTexBase = ValueBuilder.DefaultValueFieldFromTemplate(texTemplate.TemplateField);
                            // Read metadata from existing texture (m_MipCount, m_TextureSettings, etc.)
                            // NOT from newTexBase which has all zeros — Unity won't render mip=0 textures
                            var texFile = TextureFile.ReadTextureFile(texTemplate);
                            texFile.m_TextureFormat = 1; // Alpha8
                            texFile.m_MipCount = 1;
                            texFile.m_MipMap = false;
                            texFile.m_StreamingMipmaps = false;
                            texFile.m_IsReadable = true;
                            texFile.SetPictureData(atlasPages[pageIdx], atlasWidth, atlasHeight);
                            texFile.WriteTo(newTexBase);

                            newTexBase["m_StreamData"]["path"].AsString = "";
                            newTexBase["m_StreamData"]["offset"].AsULong = 0;
                            newTexBase["m_StreamData"]["size"].AsULong = 0;
                            newTexBase["m_Name"].AsString = $"{fontName} Atlas {pageIdx}";

                            // Add as new asset (global max PathId to avoid collisions across types)
                            var newInfo = AssetFileInfo.Create(
                                afileInst.file,
                                afileInst.file.Metadata.AssetInfos.Max(a => a.PathId) + 1,
                                (int)AssetClassID.Texture2D, null);
                            newInfo.SetNewData(newTexBase);
                            afileInst.file.Metadata.AssetInfos.Add(newInfo);

                            // Add PPtr to atlas textures array
                            var newPtr = ValueBuilder.DefaultValueFieldFromTemplate(ptrProto.TemplateField);
                            newPtr["m_FileID"].AsInt = 0;
                            newPtr["m_PathID"].AsLong = newInfo.PathId;
                            newPtrList.Add(newPtr);
                        }

                        atlasTextures["Array"].Children = newPtrList;
                    }
                    else
                    {
                        // Cannot create additional textures — graceful degradation
                        logger.LogWarning(
                            "模板中只有 {Existing} 个纹理槽，但需要 {Needed} 页 Atlas。多余的字形将被忽略",
                            existingTexCount, atlasPages.Count);
                    }
                }
            }
        }

        // Commit font MonoBehaviour changes
        fontMbInfo.SetNewData(fontBase);

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

        // === Write bundle ===
        dirInfos[entryIndex].SetNewData(afileInst.file);

        var tmpPath = outputPath + ".tmp";
        try
        {
            // Write uncompressed
            using (var writer = new AssetsFileWriter(tmpPath))
                bunInst.file.Write(writer);

            // Unload and re-read for compression
            manager.UnloadBundleFile(bunInst);

            using var tmpReader = new AssetsFileReader(tmpPath);
            var tmpBundle = new AssetBundleFile();
            tmpBundle.Read(tmpReader);

            // Compress with LZ4
            using (var finalWriter = new AssetsFileWriter(outputPath))
                tmpBundle.Pack(finalWriter, AssetBundleCompressionType.LZ4);

            tmpBundle.Close();
        }
        finally
        {
            if (File.Exists(tmpPath))
                File.Delete(tmpPath);
        }

        logger.LogInformation("AssetBundle 已写入: {Path}", outputPath);
    }

    private static void ReplaceTexture(
        AssetsManager manager, AssetsFileInstance afileInst, AssetFileInfo texInfo,
        byte[] atlasData, int atlasWidth, int atlasHeight, string textureName)
    {
        var texBase = manager.GetBaseField(afileInst, texInfo);
        var texFile = TextureFile.ReadTextureFile(texBase);
        texFile.m_TextureFormat = 1; // Alpha8
        texFile.m_MipCount = 1;
        texFile.m_MipMap = false;
        texFile.m_StreamingMipmaps = false;
        texFile.m_IsReadable = true;
        texFile.SetPictureData(atlasData, atlasWidth, atlasHeight);
        texFile.WriteTo(texBase);

        texBase["m_StreamData"]["path"].AsString = "";
        texBase["m_StreamData"]["offset"].AsULong = 0;
        texBase["m_StreamData"]["size"].AsULong = 0;
        texBase["m_Name"].AsString = textureName;
        texInfo.SetNewData(texBase);
    }

    private List<List<GlyphData>> PackGlyphs(List<GlyphData> glyphs, int atlasWidth, int atlasHeight)
    {
        if (glyphs.Count == 0)
            return [[]];

        var rects = CreatePackingRects(glyphs);
        RectanglePacker.Pack(rects, out var bounds, PackingHints.FindBest, (uint)atlasWidth, (uint)atlasHeight);

        if (bounds.Width <= (uint)atlasWidth && bounds.Height <= (uint)atlasHeight)
        {
            ApplyPackedPositions(glyphs, rects);
            return [glyphs];
        }

        // Binary search for max glyphs per page
        int lo = 1, hi = glyphs.Count, maxPerPage = 1;
        while (lo <= hi)
        {
            int mid = (lo + hi) / 2;
            var testRects = CreatePackingRects(glyphs.Take(mid).ToList());
            RectanglePacker.Pack(testRects, out var testBounds, PackingHints.FindBest, (uint)atlasWidth, (uint)atlasHeight);
            if (testBounds.Width <= (uint)atlasWidth && testBounds.Height <= (uint)atlasHeight)
            { maxPerPage = mid; lo = mid + 1; }
            else
            { hi = mid - 1; }
        }

        maxPerPage = Math.Max(1, (int)(maxPerPage * 0.9)); // Safety margin

        var pages = new List<List<GlyphData>>();
        for (int i = 0; i < glyphs.Count;)
        {
            var batchSize = Math.Min(maxPerPage, glyphs.Count - i);
            var page = glyphs.Skip(i).Take(batchSize).ToList();
            var pageRects = CreatePackingRects(page);
            RectanglePacker.Pack(pageRects, out var pageBounds, PackingHints.FindBest, (uint)atlasWidth, (uint)atlasHeight);

            while (batchSize > 1 && (pageBounds.Width > (uint)atlasWidth || pageBounds.Height > (uint)atlasHeight))
            {
                batchSize = (int)(batchSize * 0.9);
                page = glyphs.Skip(i).Take(batchSize).ToList();
                pageRects = CreatePackingRects(page);
                RectanglePacker.Pack(pageRects, out pageBounds, PackingHints.FindBest, (uint)atlasWidth, (uint)atlasHeight);
            }

            ApplyPackedPositions(page, pageRects);
            pages.Add(page);
            i += batchSize;
        }
        return pages;
    }

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

    private static void ApplyPackedPositions(IList<GlyphData> glyphs, PackingRectangle[] rects)
    {
        foreach (var r in rects)
        {
            glyphs[(int)r.Id].AtlasX = (int)r.X;  // Raw packed position (top-left of padded bitmap)
            glyphs[(int)r.Id].AtlasY = (int)r.Y;
        }
    }

    private static void FlipAtlasY(byte[] atlas, int width, int height)
    {
        var rowBuf = new byte[width];
        for (int y = 0; y < height / 2; y++)
        {
            var topOff = y * width;
            var botOff = (height - 1 - y) * width;
            Buffer.BlockCopy(atlas, topOff, rowBuf, 0, width);
            Buffer.BlockCopy(atlas, botOff, atlas, topOff, width);
            Buffer.BlockCopy(rowBuf, 0, atlas, botOff, width);
        }
    }

    private void BroadcastProgress(string phase, int current, int total, string message, bool force = false)
    {
        _phase = phase;
        _current = current;
        _total = total;

        var now = DateTime.UtcNow.Ticks;
        var last = Volatile.Read(ref _lastBroadcastTicks);
        if (!force && now - last < TimeSpan.TicksPerMillisecond * 200)
            return;
        if (Interlocked.CompareExchange(ref _lastBroadcastTicks, now, last) != last && !force)
            return;

        var progress = new FontGenerationProgress(phase, current, total, message);
        hubContext.Clients.Group("font-generation")
            .SendAsync("FontGenerationProgress", progress).Wait();
    }

    private static string SanitizeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sb = new StringBuilder(name.Length);
        foreach (var c in name)
            sb.Append(invalid.Contains(c) ? '_' : c);
        return sb.ToString().Trim();
    }

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

    private class GlyphData
    {
        public uint Index;
        public uint Unicode;
        public float Width;
        public float Height;
        public float BearingX;
        public float BearingY;
        public float Advance;
        public byte[]? BitmapData;
        public int BitmapWidth;
        public int BitmapHeight;
        public int AtlasX;
        public int AtlasY;
        public int AtlasIndex;
    }
}
