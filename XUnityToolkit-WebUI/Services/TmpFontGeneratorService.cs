using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using AssetsTools.NET;
using AssetsTools.NET.Extra;
using AssetsTools.NET.Texture;
using FreeTypeSharp;
using Microsoft.AspNetCore.SignalR;
using RectpackSharp;
using XUnityToolkit_WebUI.Hubs;
using XUnityToolkit_WebUI.Infrastructure;
using XUnityToolkit_WebUI.Models;
using static FreeTypeSharp.FT;
using static FreeTypeSharp.FT_Render_Mode_;

namespace XUnityToolkit_WebUI.Services;

public sealed class TmpFontGeneratorService(
    TmpFontService tmpFontService,
    AppDataPaths appDataPaths,
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

    private const int DefaultPadding = 9;
    private const int DefaultSpread = 4; // padding / 2
    private const int SdfAtlasRenderMode = 4165; // GlyphRenderMode.SDF

    private readonly SemaphoreSlim _generationSemaphore = new(1, 1);
    private CancellationTokenSource? _cts;
    private long _lastBroadcastTicks;

    // Progress state
    private volatile bool _isGenerating;
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

        try
        {
            return await Task.Run(() => GenerateCore(request, ct), CancellationToken.None);
        }
        catch (OperationCanceledException)
        {
            logger.LogInformation("字体生成已被用户取消");
            return new FontGenerationResult(false, null, null, 0, 0, 0, "已取消");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "字体生成失败");
            return new FontGenerationResult(false, null, null, 0, 0, 0, ex.Message);
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

    private unsafe FontGenerationResult GenerateCore(FontGenerationRequest request, CancellationToken ct)
    {
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
                // Set pixel size
                FT_Set_Pixel_Sizes(face, 0, (uint)request.SamplingSize);

                // Set SDF spread
                int spread = DefaultSpread;
                var sdfBytes = Encoding.ASCII.GetBytes("sdf\0");
                var spreadBytes = Encoding.ASCII.GetBytes("spread\0");
                fixed (byte* sdfPtr = sdfBytes)
                fixed (byte* spreadNamePtr = spreadBytes)
                {
                    FT_Property_Set(lib, sdfPtr, spreadNamePtr, &spread);
                }

                // Extract font name
                var fontName = Marshal.PtrToStringAnsi((IntPtr)face->family_name) ?? "Unknown";
                logger.LogInformation("字体已加载: {FontName}, 采样大小: {Size}px", fontName, request.SamplingSize);

                // Extract face metrics (26.6 fixed-point → float)
                var sizeMetrics = face->size->metrics;
                float ascender = (int)sizeMetrics.ascender / 64f;
                float descender = (int)sizeMetrics.descender / 64f;
                float lineHeight = (int)sizeMetrics.height / 64f;

                // === Phase 2: Generate SDF bitmaps ===
                var chars = EnumerateCharacterSet();
                _total = chars.Count;
                BroadcastProgress("sdf", 0, chars.Count, "正在生成 SDF 位图...", force: true);

                var glyphs = new List<GlyphData>();
                for (int i = 0; i < chars.Count; i++)
                {
                    ct.ThrowIfCancellationRequested();

                    var charCode = (uint)chars[i];
                    var glyphIndex = FT_Get_Char_Index(face, (UIntPtr)charCode);
                    if (glyphIndex == 0) continue; // Missing glyph

                    error = FT_Load_Glyph(face, glyphIndex, FT_LOAD.FT_LOAD_NO_BITMAP | FT_LOAD.FT_LOAD_NO_HINTING);
                    if (error != FT_Error.FT_Err_Ok) continue;

                    error = FT_Render_Glyph(face->glyph, FT_RENDER_MODE_SDF);
                    if (error != FT_Error.FT_Err_Ok) continue;

                    ref var bitmap = ref face->glyph->bitmap;
                    ref var metrics = ref face->glyph->metrics;

                    // Copy bitmap data
                    byte[]? bitmapData = null;
                    int bmpWidth = (int)bitmap.width;
                    int bmpHeight = (int)bitmap.rows;

                    if (bmpWidth > 0 && bmpHeight > 0 && bitmap.buffer != null)
                    {
                        bitmapData = new byte[bmpWidth * bmpHeight];
                        int pitch = bitmap.pitch;
                        for (int row = 0; row < bmpHeight; row++)
                        {
                            var src = new ReadOnlySpan<byte>(bitmap.buffer + row * pitch, bmpWidth);
                            src.CopyTo(bitmapData.AsSpan(row * bmpWidth));
                        }
                    }

                    glyphs.Add(new GlyphData
                    {
                        Index = glyphIndex,
                        Unicode = charCode,
                        Width = (int)metrics.width / 64f,
                        Height = (int)metrics.height / 64f,
                        BearingX = (int)metrics.horiBearingX / 64f,
                        BearingY = (int)metrics.horiBearingY / 64f,
                        Advance = (int)metrics.horiAdvance / 64f,
                        BitmapData = bitmapData,
                        BitmapWidth = bmpWidth,
                        BitmapHeight = bmpHeight,
                    });

                    _current = i + 1;
                    BroadcastProgress("sdf", i + 1, chars.Count, $"正在生成 SDF 位图 ({i + 1}/{chars.Count})...");
                }

                logger.LogInformation("SDF 生成完成: {Count}/{Total} 个字形", glyphs.Count, chars.Count);

                // === Phase 3: Pack atlas ===
                BroadcastProgress("packing", 0, 1, "正在打包 Atlas...", force: true);

                var rects = new PackingRectangle[glyphs.Count];
                for (int i = 0; i < glyphs.Count; i++)
                {
                    // Include padding around each glyph
                    rects[i] = new PackingRectangle(0, 0,
                        (uint)(glyphs[i].BitmapWidth + DefaultPadding * 2),
                        (uint)(glyphs[i].BitmapHeight + DefaultPadding * 2),
                        i);
                }

                RectanglePacker.Pack(rects, out var bounds,
                    PackingHints.FindBest,
                    (uint)request.AtlasWidth,
                    (uint)request.AtlasHeight);

                // Check if everything fits
                if (bounds.Width > (uint)request.AtlasWidth || bounds.Height > (uint)request.AtlasHeight)
                {
                    return new FontGenerationResult(false, null, fontName, 0,
                        request.AtlasWidth, request.AtlasHeight,
                        $"Atlas 尺寸 {request.AtlasWidth}x{request.AtlasHeight} 不足以容纳所有字形。" +
                        $"需要至少 {bounds.Width}x{bounds.Height}，请增大 Atlas 尺寸。");
                }

                // === Phase 4: Composite atlas texture ===
                int atlasWidth = request.AtlasWidth;
                int atlasHeight = request.AtlasHeight;
                var atlasBytes = new byte[atlasWidth * atlasHeight];

                for (int i = 0; i < rects.Length; i++)
                {
                    var rect = rects[i];
                    var glyph = glyphs[(int)rect.Id];

                    if (glyph.BitmapData == null) continue;

                    // Blit glyph bitmap into atlas (offset by padding)
                    int dstX = (int)rect.X + DefaultPadding;
                    int dstY = (int)rect.Y + DefaultPadding;

                    for (int row = 0; row < glyph.BitmapHeight; row++)
                    {
                        int srcOffset = row * glyph.BitmapWidth;
                        int dstOffset = (dstY + row) * atlasWidth + dstX;

                        if (dstOffset + glyph.BitmapWidth <= atlasBytes.Length)
                        {
                            Array.Copy(glyph.BitmapData, srcOffset, atlasBytes, dstOffset, glyph.BitmapWidth);
                        }
                    }

                    // Record atlas coordinates for this glyph (glyph rect without padding)
                    glyph.AtlasX = dstX;
                    glyph.AtlasY = dstY;
                }

                // === Phase 5: Serialize to AssetBundle ===
                BroadcastProgress("serializing", 0, 1, "正在序列化 AssetBundle...", force: true);

                var outputFileName = $"{SanitizeFileName(fontName)}_U{request.UnityVersion}.bundle";
                var outputPath = Path.Combine(appDataPaths.GeneratedFontsDirectory, outputFileName);

                InjectIntoTemplate(request, fontName, glyphs, rects, atlasBytes,
                    atlasWidth, atlasHeight, ascender, descender, lineHeight, outputPath);

                // Clean up uploaded TTF
                try { File.Delete(request.FontFilePath); }
                catch { /* ignore cleanup errors */ }

                var result = new FontGenerationResult(true, outputPath, fontName,
                    glyphs.Count, atlasWidth, atlasHeight, null);

                // Broadcast completion
                var complete = new FontGenerationComplete(true, fontName, glyphs.Count, null);
                hubContext.Clients.Group("font-generation")
                    .SendAsync("FontGenerationComplete", complete).Wait();

                logger.LogInformation("字体生成完成: {FontName}, {Count} 个字形, 输出: {Path}",
                    fontName, glyphs.Count, outputPath);

                return result;
            }
            finally
            {
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
        List<GlyphData> glyphs, PackingRectangle[] rects,
        byte[] atlasBytes, int atlasWidth, int atlasHeight,
        float ascender, float descender, float lineHeight,
        string outputPath)
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
        for (int i = 0; i < glyphs.Count; i++)
            glyphIndexMap[glyphs[i].Index] = (uint)i;

        // === Inject GlyphTable ===
        var glyphArray = fontBase["m_GlyphTable"]["Array"];
        if (glyphArray.Children.Count > 0)
        {
            var prototype = glyphArray.Children[0];
            var newGlyphs = new List<AssetTypeValueField>(glyphs.Count);

            for (int i = 0; i < glyphs.Count; i++)
            {
                var g = glyphs[i];
                var rect = FindRectForGlyph(rects, i);

                var entry = ValueBuilder.DefaultValueFieldFromTemplate(prototype.TemplateField);
                entry["m_Index"].AsUInt = (uint)i;

                var m = entry["m_Metrics"];
                m["m_Width"].AsFloat = g.Width;
                m["m_Height"].AsFloat = g.Height;
                m["m_HorizontalBearingX"].AsFloat = g.BearingX;
                m["m_HorizontalBearingY"].AsFloat = g.BearingY;
                m["m_HorizontalAdvance"].AsFloat = g.Advance;

                var gr = entry["m_GlyphRect"];
                gr["m_X"].AsInt = g.AtlasX;
                gr["m_Y"].AsInt = g.AtlasY;
                gr["m_Width"].AsInt = g.BitmapWidth;
                gr["m_Height"].AsInt = g.BitmapHeight;

                entry["m_Scale"].AsFloat = 1.0f;
                entry["m_AtlasIndex"].AsInt = 0;

                newGlyphs.Add(entry);
            }
            glyphArray.Children = newGlyphs;
        }

        // === Inject CharacterTable ===
        var charArray = fontBase["m_CharacterTable"]["Array"];
        if (charArray.Children.Count > 0)
        {
            var charProto = charArray.Children[0];
            var newChars = new List<AssetTypeValueField>(glyphs.Count);

            foreach (var g in glyphs)
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

        // === Inject UsedGlyphRects ===
        var usedRectsArray = fontBase["m_UsedGlyphRects"];
        if (!usedRectsArray.IsDummy && usedRectsArray["Array"].Children.Count > 0)
        {
            var rectProto = usedRectsArray["Array"].Children[0];
            var newRects = new List<AssetTypeValueField>(rects.Length);
            foreach (var r in rects)
            {
                var entry = ValueBuilder.DefaultValueFieldFromTemplate(rectProto.TemplateField);
                entry["m_X"].AsInt = (int)r.X;
                entry["m_Y"].AsInt = (int)r.Y;
                entry["m_Width"].AsInt = (int)r.Width;
                entry["m_Height"].AsInt = (int)r.Height;
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
            faceInfo["m_PointSize"].AsInt = request.SamplingSize;
            faceInfo["m_LineHeight"].AsFloat = lineHeight;
            faceInfo["m_AscentLine"].AsFloat = ascender;
            faceInfo["m_DescentLine"].AsFloat = descender;
            faceInfo["m_Baseline"].AsFloat = 0;
            faceInfo["m_UnderlineOffset"].AsFloat = descender * 0.5f;
            faceInfo["m_StrikethroughOffset"].AsFloat = ascender * 0.4f;
            faceInfo["m_TabWidth"].AsFloat = glyphs.Count > 0 ? glyphs[0].Advance * 4 : request.SamplingSize * 2;
        }

        // === Set scalar fields ===
        fontBase["m_AtlasWidth"].AsInt = atlasWidth;
        fontBase["m_AtlasHeight"].AsInt = atlasHeight;
        fontBase["m_AtlasPadding"].AsInt = DefaultPadding;
        fontBase["m_AtlasRenderMode"].AsInt = SdfAtlasRenderMode;
        fontBase["m_AtlasTextureIndex"].AsInt = 0;
        fontBase["m_Version"].AsString = "1.1.0";
        fontBase["m_Name"].AsString = fontName;

        // === Replace atlas Texture2D ===
        var atlasTextures = fontBase["m_AtlasTextures"];
        if (!atlasTextures.IsDummy && atlasTextures["Array"].Children.Count > 0)
        {
            var atlasPathId = atlasTextures["Array"][0]["m_PathID"].AsLong;

            foreach (var texInfo in afileInst.file.GetAssetsOfType(AssetClassID.Texture2D))
            {
                if (texInfo.PathId == atlasPathId)
                {
                    var texBase = manager.GetBaseField(afileInst, texInfo);
                    var texFile = TextureFile.ReadTextureFile(texBase);
                    texFile.m_TextureFormat = 1; // Alpha8
                    texFile.SetPictureData(atlasBytes, atlasWidth, atlasHeight);
                    texFile.WriteTo(texBase);

                    // Clear m_StreamData
                    texBase["m_StreamData"]["path"].AsString = "";
                    texBase["m_StreamData"]["offset"].AsULong = 0;
                    texBase["m_StreamData"]["size"].AsULong = 0;

                    texBase["m_Name"].AsString = $"{fontName} Atlas";
                    texInfo.SetNewData(texBase);
                    break;
                }
            }
        }

        // Commit font MonoBehaviour changes
        fontMbInfo.SetNewData(fontBase);

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

    private static PackingRectangle FindRectForGlyph(PackingRectangle[] rects, int glyphIndex)
    {
        foreach (var r in rects)
        {
            if (r.Id == (uint)glyphIndex)
                return r;
        }
        return rects[0]; // fallback
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

    internal static List<char> EnumerateCharacterSet()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var gb2312 = Encoding.GetEncoding("gb2312");
        var chars = new List<char>(8000);

        // GB2312 characters
        for (int hi = 0xA1; hi <= 0xF7; hi++)
        {
            for (int lo = 0xA1; lo <= 0xFE; lo++)
            {
                byte[] bytes = [(byte)hi, (byte)lo];
                var decoded = gb2312.GetString(bytes);
                if (decoded.Length == 1 && decoded[0] != '\uFFFD')
                    chars.Add(decoded[0]);
            }
        }

        // ASCII printable (0x20-0x7E)
        for (char c = ' '; c <= '~'; c++)
            chars.Add(c);

        return chars;
    }

    private static string SanitizeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sb = new StringBuilder(name.Length);
        foreach (var c in name)
            sb.Append(invalid.Contains(c) ? '_' : c);
        return sb.ToString().Trim();
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
    }
}
