using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using AssetsTools.NET;
using AssetsTools.NET.Extra;
using AssetsTools.NET.Texture;
using XUnityToolkit_WebUI.Infrastructure;
using XUnityToolkit_WebUI.Models;

namespace XUnityToolkit_WebUI.Services;

public sealed class FontReplacementService(
    TmpFontService tmpFontService,
    BundledAssetPaths bundledPaths,
    AppDataPaths appDataPaths,
    ILogger<FontReplacementService> logger)
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

    public async Task<List<FontInfo>> ScanFontsAsync(
        string gamePath, UnityGameInfo gameInfo, CancellationToken ct = default)
    {
        var gameName = Path.GetFileNameWithoutExtension(gameInfo.DetectedExecutable);
        var dataPath = Path.Combine(gamePath, $"{gameName}_Data");

        if (!Directory.Exists(dataPath))
            throw new DirectoryNotFoundException($"Game data directory not found: {dataPath}");

        // Find all .assets files
        var assetFiles = Directory.GetFiles(dataPath, "*.assets", SearchOption.TopDirectoryOnly)
            .Concat(Directory.GetFiles(dataPath, "level*", SearchOption.TopDirectoryOnly)
                .Where(f => !Path.GetExtension(f).Equals(".resS", StringComparison.OrdinalIgnoreCase)))
            .Distinct()
            .ToList();

        // Find bundle files (data.unity3d + StreamingAssets bundles + other bundles)
        var bundleFiles = new List<string>();
        var dataUnity3d = Path.Combine(dataPath, "data.unity3d");
        if (File.Exists(dataUnity3d))
            bundleFiles.Add(dataUnity3d);

        var streamingAssetsPath = Path.Combine(dataPath, "StreamingAssets");
        if (Directory.Exists(streamingAssetsPath))
        {
            bundleFiles.AddRange(
                Directory.GetFiles(streamingAssetsPath, "*.bundle", SearchOption.AllDirectories));
        }

        // Also scan for bundle files directly in data path (some games store fonts here)
        bundleFiles.AddRange(
            Directory.GetFiles(dataPath, "*.bundle", SearchOption.TopDirectoryOnly)
                .Where(f => !bundleFiles.Contains(f, StringComparer.OrdinalIgnoreCase)));

        if (assetFiles.Count == 0 && bundleFiles.Count == 0)
            throw new FileNotFoundException("No asset files found in game data directory.");

        logger.LogInformation("字体扫描: 找到 {AssetCount} 个资产文件, {BundleCount} 个 Bundle 文件: {Path}",
            assetFiles.Count, bundleFiles.Count, dataPath);

        return await Task.Run(() =>
        {
            var results = new List<FontInfo>();
            var manager = new AssetsManager();

            try
            {
                // Load class database from embedded resource
                using var tpkStream = new MemoryStream(ClassDataTpk.Value);
                manager.LoadClassPackage(tpkStream);

                // Set up MonoBehaviour template generator based on backend
                SetupTemplateGenerator(manager, gamePath, gameName, dataPath, gameInfo);

                manager.UseTemplateFieldCache = true;
                manager.UseMonoTemplateFieldCache = true;

                // Process loose asset files
                foreach (var assetFile in assetFiles)
                {
                    ct.ThrowIfCancellationRequested();

                    var fileName = Path.GetFileName(assetFile);
                    AssetsFileInstance? afileInst = null;
                    try
                    {
                        afileInst = manager.LoadAssetsFile(assetFile, loadDeps: false);
                        var fonts = ScanAssetsInstance(manager, afileInst, fileName,
                            gameInfo.UnityVersion, isBundle: false);
                        results.AddRange(fonts);
                    }
                    catch (Exception ex)
                    {
                        logger.LogWarning(ex, "字体扫描资产文件失败: {File}", fileName);
                    }
                    finally
                    {
                        if (afileInst != null)
                            manager.UnloadAssetsFile(afileInst);
                    }
                }

                // Process bundle files
                foreach (var bundleFile in bundleFiles)
                {
                    ct.ThrowIfCancellationRequested();

                    var fileName = Path.GetFileName(bundleFile);
                    BundleFileInstance? bunInst = null;
                    try
                    {
                        bunInst = manager.LoadBundleFile(bundleFile, true);
                        var dirInfos = bunInst.file.BlockAndDirInfo.DirectoryInfos;

                        for (int i = 0; i < dirInfos.Count; i++)
                        {
                            var entryName = dirInfos[i].Name;
                            if (entryName.EndsWith(".resource", StringComparison.OrdinalIgnoreCase) ||
                                entryName.EndsWith(".resS", StringComparison.OrdinalIgnoreCase))
                                continue;

                            AssetsFileInstance? bundleAfileInst = null;
                            try
                            {
                                bundleAfileInst = manager.LoadAssetsFileFromBundle(bunInst, i, false);
                                var fonts = ScanAssetsInstance(manager, bundleAfileInst,
                                    $"{fileName}/{entryName}", gameInfo.UnityVersion, isBundle: true);
                                results.AddRange(fonts);
                            }
                            catch (Exception ex)
                            {
                                logger.LogDebug(ex, "Bundle 字体扫描失败: {Bundle}/{Entry}", fileName, entryName);
                            }
                            finally
                            {
                                if (bundleAfileInst != null)
                                    manager.UnloadAssetsFile(bundleAfileInst);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        logger.LogWarning(ex, "加载 Bundle 文件失败: {File}", fileName);
                    }
                    finally
                    {
                        if (bunInst != null)
                            manager.UnloadBundleFile(bunInst);
                    }
                }
            }
            finally
            {
                manager.UnloadAll();
                if (manager.MonoTempGenerator is IDisposable disposable)
                    disposable.Dispose();
            }

            logger.LogInformation("字体扫描完成: 找到 {Count} 个字体", results.Count);
            return results;
        }, ct);
    }

    private List<FontInfo> ScanAssetsInstance(
        AssetsManager manager, AssetsFileInstance afileInst, string fileName,
        string unityVersion, bool isBundle)
    {
        var results = new List<FontInfo>();
        var afile = afileInst.file;

        // Load class database for this Unity version
        var hasClassDb = true;
        try
        {
            manager.LoadClassDatabaseFromPackage(afile.Metadata.UnityVersion);
        }
        catch
        {
            try { manager.LoadClassDatabaseFromPackage(unityVersion); }
            catch
            {
                hasClassDb = false;
                // If type tree is embedded, we can still read without class database
                if (!afile.Metadata.TypeTreeEnabled)
                {
                    logger.LogDebug("无法加载类数据库且无内嵌类型树, 跳过 MonoBehaviour 扫描: {File}", fileName);
                    // Don't return — still try to scan Font assets below
                }
            }
        }

        // Skip MonoBehaviour scan if we have no class DB and no type tree
        if (hasClassDb || afile.Metadata.TypeTreeEnabled)
        {
            foreach (var mbInfo in afile.GetAssetsOfType(AssetClassID.MonoBehaviour))
            {
                try
                {
                    var mbBase = manager.GetBaseField(afileInst, mbInfo);
                    if (mbBase.IsDummy) continue;

                    // Check for TMP_FontAsset v1.1.0 signature
                    var versionField = mbBase["m_Version"];
                    var glyphTableField = mbBase["m_GlyphTable"];
                    if (!versionField.IsDummy && !glyphTableField.IsDummy)
                    {
                        var name = mbBase["m_Name"].IsDummy ? "(unnamed)" : mbBase["m_Name"].AsString;
                        var version = versionField.AsString;
                        var atlasTextures = mbBase["m_AtlasTextures"];
                        var charTable = mbBase["m_CharacterTable"];

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
                        continue;
                    }

                    // Check for legacy TMP_FontAsset v1.0.0
                    var fontInfoField = mbBase["m_fontInfo"];
                    var glyphInfoList = mbBase["m_glyphInfoList"];
                    if (!fontInfoField.IsDummy && !glyphInfoList.IsDummy)
                    {
                        var name = mbBase["m_Name"].IsDummy ? "(unnamed)" : mbBase["m_Name"].AsString;
                        results.Add(new FontInfo
                        {
                            Name = name,
                            PathId = mbInfo.PathId,
                            AssetFile = fileName,
                            IsInBundle = isBundle,
                            FontType = "TMP",
                            GlyphCount = glyphInfoList["Array"].Children.Count,
                            IsSupported = false // v1.0.0 not supported
                        });
                    }
                }
                catch
                {
                    // MonoBehaviour deserialization can fail — skip silently
                }
            }
        }

        // Scan for Unity Font assets (TTF/OTF)
        foreach (var fontAssetInfo in afile.GetAssetsOfType(AssetClassID.Font))
        {
            try
            {
                var fontBase = manager.GetBaseField(afileInst, fontAssetInfo);
                if (fontBase.IsDummy) continue;

                var name = fontBase["m_Name"].IsDummy ? "(unnamed)" : fontBase["m_Name"].AsString;

                var fontData = fontBase["m_FontData"];
                long fontDataSize = 0;
                if (!fontData.IsDummy)
                {
                    try { fontDataSize = fontData.AsByteArray.Length; }
                    catch { /* TypelessData read failed — report font with size=0 */ }
                }

                results.Add(new FontInfo
                {
                    Name = name,
                    PathId = fontAssetInfo.PathId,
                    AssetFile = fileName,
                    IsInBundle = isBundle,
                    FontType = "TTF",
                    IsSupported = !fontData.IsDummy,
                    FontDataSize = fontDataSize
                });
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "TTF 字体资产反序列化失败: {File} PathId={PathId}",
                    fileName, fontAssetInfo.PathId);
            }
        }

        return results;
    }

    private void SetupTemplateGenerator(
        AssetsManager manager, string gamePath, string gameName,
        string dataPath, UnityGameInfo gameInfo)
    {
        if (gameInfo.Backend == UnityBackend.IL2CPP)
        {
            var metaPath = Path.Combine(dataPath, "il2cpp_data", "Metadata", "global-metadata.dat");
            var asmPath = Path.Combine(gamePath, "GameAssembly.dll");

            if (File.Exists(metaPath) && File.Exists(asmPath))
            {
                try
                {
                    manager.MonoTempGenerator = new AssetsTools.NET.Cpp2IL.Cpp2IlTempGenerator(metaPath, asmPath);
                    logger.LogInformation("已设置 IL2CPP 模板生成器");
                    return;
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "IL2CPP 模板生成器初始化失败，将跳过 MonoBehaviour 自定义字段");
                }
            }
            else
            {
                logger.LogWarning("IL2CPP 元数据文件不完整: meta={MetaExists}, asm={AsmExists}",
                    File.Exists(metaPath), File.Exists(asmPath));
            }
        }
        else
        {
            var managedPath = Path.Combine(dataPath, "Managed");
            if (Directory.Exists(managedPath))
            {
                try
                {
                    manager.MonoTempGenerator = new MonoCecilTempGenerator(managedPath);
                    logger.LogInformation("已设置 Mono 模板生成器: {Path}", managedPath);
                    return;
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Mono 模板生成器初始化失败，将跳过 MonoBehaviour 自定义字段");
                }
            }
        }
    }

    public async Task<FontReplacementResult> ReplaceFontsAsync(
        string gamePath, string gameId, UnityGameInfo gameInfo,
        FontTarget[] fonts, string? customFontPath,
        IProgress<FontReplacementProgress>? progress, CancellationToken ct = default)
    {
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

        var gameName = Path.GetFileNameWithoutExtension(gameInfo.DetectedExecutable);
        var dataPath = Path.Combine(gamePath, $"{gameName}_Data");
        var backupDir = appDataPaths.GetFontBackupDirectory(gameId);
        Directory.CreateDirectory(backupDir);

        var replacedFiles = new List<ReplacedFileEntry>();
        var catalogFiles = new List<CatalogFileEntry>();
        var failedFonts = new List<FailedFontEntry>();

        var fontSourceDisplay = fontFile != null ? Path.GetFileName(fontFile) : "";
        if (ttfSourceName != null)
            fontSourceDisplay = string.IsNullOrEmpty(fontSourceDisplay)
                ? ttfSourceName
                : $"{fontSourceDisplay} + {ttfSourceName}";
        logger.LogInformation("字体替换开始: {Count} 个字体, 源字体: {Font}", fonts.Length, fontSourceDisplay);

        await Task.Run(() =>
        {
            // === Step 1: Load TMP source font from bundled AssetBundle (if available) ===
            var srcManager = new AssetsManager();
            AssetTypeValueField? srcFontBase = null;
            List<SourceAtlasPage>? srcAtlasPages = null;

            try
            {
                if (fontFile != null && File.Exists(fontFile))
                {
                    using var tpkStream = new MemoryStream(ClassDataTpk.Value);
                    srcManager.LoadClassPackage(tpkStream);
                    srcManager.UseTemplateFieldCache = true;

                    var srcBunInst = srcManager.LoadBundleFile(fontFile, true);
                    var srcDirInfos = srcBunInst.file.BlockAndDirInfo.DirectoryInfos;

                    // Find the assets entry in the source bundle (skip .resource/.resS)
                    AssetsFileInstance? foundSrcAfileInst = null;
                    AssetTypeValueField? foundSrcFontBase = null;

                    for (int i = 0; i < srcDirInfos.Count; i++)
                    {
                        var entryName = srcDirInfos[i].Name;
                        if (entryName.EndsWith(".resource", StringComparison.OrdinalIgnoreCase) ||
                            entryName.EndsWith(".resS", StringComparison.OrdinalIgnoreCase))
                            continue;

                        var afileInst = srcManager.LoadAssetsFileFromBundle(srcBunInst, i, false);
                        var afile = afileInst.file;

                        try { srcManager.LoadClassDatabaseFromPackage(afile.Metadata.UnityVersion); }
                        catch { /* type tree embedded */ }

                        foreach (var mbInfo in afile.GetAssetsOfType(AssetClassID.MonoBehaviour))
                        {
                            try
                            {
                                var mbBase = srcManager.GetBaseField(afileInst, mbInfo);
                                if (mbBase.IsDummy) continue;
                                if (!mbBase["m_GlyphTable"].IsDummy && !mbBase["m_Version"].IsDummy)
                                {
                                    foundSrcAfileInst = afileInst;
                                    foundSrcFontBase = mbBase;
                                    break;
                                }
                            }
                            catch { /* skip */ }
                        }

                        if (foundSrcFontBase != null) break;
                    }

                    if (foundSrcFontBase is null || foundSrcAfileInst is null)
                        throw new InvalidOperationException("Source font bundle does not contain a valid TMP_FontAsset.");

                    srcFontBase = foundSrcFontBase;

                    // Find ALL source atlas Texture2D via m_AtlasTextures PPtrs
                    var srcAtlasTexturesField = srcFontBase["m_AtlasTextures"];
                    if (srcAtlasTexturesField.IsDummy || srcAtlasTexturesField["Array"].Children.Count == 0)
                        throw new InvalidOperationException("Source font has no atlas textures.");

                    srcAtlasPages = new List<SourceAtlasPage>();
                    for (int pageIdx = 0; pageIdx < srcAtlasTexturesField["Array"].Children.Count; pageIdx++)
                    {
                        var srcAtlasPPtr = srcAtlasTexturesField["Array"][pageIdx];
                        var srcAtlasPathId = srcAtlasPPtr["m_PathID"].AsLong;

                        AssetTypeValueField? foundSrcTex = null;
                        foreach (var texInfo in foundSrcAfileInst.file.GetAssetsOfType(AssetClassID.Texture2D))
                        {
                            if (texInfo.PathId == srcAtlasPathId)
                            {
                                foundSrcTex = srcManager.GetBaseField(foundSrcAfileInst, texInfo);
                                break;
                            }
                        }

                        if (foundSrcTex is null)
                            throw new InvalidOperationException($"Source atlas Texture2D not found for page {pageIdx} (PathId={srcAtlasPathId}).");

                        var srcTexFile = TextureFile.ReadTextureFile(foundSrcTex);
                        var srcEncodedData = srcTexFile.FillPictureData(foundSrcAfileInst)
                            ?? throw new InvalidOperationException($"Failed to read source atlas texture data for page {pageIdx}.");

                        srcAtlasPages.Add(new SourceAtlasPage(srcEncodedData, srcTexFile.m_Width, srcTexFile.m_Height, srcTexFile.m_TextureFormat));
                    }

                    logger.LogInformation("TMP 源字体已加载: {Glyphs} 个字形, {Pages} 页图集, 图集 {W}x{H}",
                        srcFontBase["m_GlyphTable"]["Array"].Children.Count, srcAtlasPages.Count,
                        srcAtlasPages[0].Width, srcAtlasPages[0].Height);
                }

                // === Step 2: Group target fonts by asset file ===
                var fontsByFile = fonts.GroupBy(f => f.AssetFile).ToList();
                int totalFonts = fonts.Length;
                int processedFonts = 0;

                // Separate bundle entries from loose files, and group bundle entries by bundle file
                // so each bundle is loaded/compressed only once
                var looseFileGroups = fontsByFile.Where(g => !g.Key.Contains('/')).ToList();
                var bundleFileGroups = fontsByFile
                    .Where(g => g.Key.Contains('/'))
                    .GroupBy(g => g.Key[..g.Key.IndexOf('/')])
                    .ToList();

                // === Step 3: Process each file group ===
                var dstManager = new AssetsManager();
                try
                {
                    using var dstTpkStream = new MemoryStream(ClassDataTpk.Value);
                    dstManager.LoadClassPackage(dstTpkStream);

                    // Set up template generator for the game
                    SetupTemplateGenerator(dstManager, gamePath, gameName, dataPath, gameInfo);
                    dstManager.UseTemplateFieldCache = true;
                    dstManager.UseMonoTemplateFieldCache = true;

                    // Process bundles — all entries within one bundle in a single load/compress cycle
                    foreach (var bundleGroup in bundleFileGroups)
                    {
                        ct.ThrowIfCancellationRequested();

                        var bundleFileName = bundleGroup.Key;
                        var entriesWithFonts = new Dictionary<string, List<FontTarget>>();
                        foreach (var entryGroup in bundleGroup)
                        {
                            var entryName = entryGroup.Key[(entryGroup.Key.IndexOf('/') + 1)..];
                            entriesWithFonts[entryName] = entryGroup.ToList();
                        }

                        ProcessBundleFile(dstManager, srcFontBase, srcAtlasPages, ttfSourceBytes,
                            gamePath, gameId, dataPath, bundleFileName, entriesWithFonts,
                            gameInfo.UnityVersion, replacedFiles, catalogFiles, failedFonts,
                            ref processedFonts, totalFonts, progress, ct);
                    }

                    // Process loose asset files
                    foreach (var looseGroup in looseFileGroups)
                    {
                        ct.ThrowIfCancellationRequested();

                        ProcessLooseFile(dstManager, srcFontBase, srcAtlasPages, ttfSourceBytes,
                            gamePath, gameId, dataPath, looseGroup.Key, looseGroup.ToList(),
                            gameInfo.UnityVersion, replacedFiles, failedFonts,
                            ref processedFonts, totalFonts, progress, ct);
                    }
                }
                catch (Exception)
                {
                    // Rollback: restore all backed-up files
                    foreach (var entry in replacedFiles)
                    {
                        var backupPath = Path.Combine(backupDir, entry.BackupFileName);
                        if (File.Exists(backupPath))
                        {
                            try { File.Copy(backupPath, entry.OriginalPath, overwrite: true); }
                            catch (Exception rollbackEx)
                            {
                                logger.LogError(rollbackEx, "回滚失败: {Path}", entry.OriginalPath);
                            }
                        }
                    }

                    throw;
                }
                finally
                {
                    dstManager.UnloadAll();
                    if (dstManager.MonoTempGenerator is IDisposable disposable2)
                        disposable2.Dispose();
                }
            }
            finally
            {
                srcManager.UnloadAll();
            }
        }, ct);

        // === Step 4: Clear Addressables CRC checksums ===
        if (replacedFiles.Count > 0)
        {
            var gameName2 = Path.GetFileNameWithoutExtension(gameInfo.DetectedExecutable);
            var modifiedBundles = replacedFiles
                .Where(f => f.OriginalPath.EndsWith(".bundle", StringComparison.OrdinalIgnoreCase))
                .Select(f => Path.GetFileName(f.OriginalPath))
                .ToList();

            var clearedCatalogs = await ClearAddressablesCrcAsync(
                gamePath, gameName2, modifiedBundles, gameId, ct);
            catalogFiles.AddRange(clearedCatalogs);
        }

        // === Step 5: Save manifest ===
        var manifest = new FontBackupManifest
        {
            GameId = gameId,
            ReplacedAt = DateTime.UtcNow,
            FontSource = fontSourceDisplay,
            ReplacedFiles = replacedFiles,
            CatalogFiles = catalogFiles
        };
        await FileHelper.WriteJsonAtomicAsync(Path.Combine(backupDir, "manifest.json"), manifest, ct: ct);

        var successCount = fonts.Length - failedFonts.Count;
        logger.LogInformation("字体替换完成: 成功 {Success}/{Total} 个字体, 修改了 {Files} 个文件",
            successCount, fonts.Length, replacedFiles.Count);

        if (failedFonts.Count > 0)
            logger.LogWarning("字体替换部分失败: {Count} 个字体替换失败", failedFonts.Count);

        progress?.Report(new FontReplacementProgress
        {
            Phase = "completed",
            Current = fonts.Length,
            Total = fonts.Length
        });

        return new FontReplacementResult
        {
            SuccessCount = successCount,
            FailedFonts = failedFonts
        };
    }

    private void ProcessLooseFile(
        AssetsManager manager, AssetTypeValueField? srcFontBase,
        List<SourceAtlasPage>? srcAtlasPages, byte[]? ttfSourceBytes,
        string gamePath, string gameId, string dataPath, string assetFileName,
        List<FontTarget> fontsInFile, string unityVersion,
        List<ReplacedFileEntry> replacedFiles, List<FailedFontEntry> failedFonts,
        ref int processedFonts, int totalFonts,
        IProgress<FontReplacementProgress>? progress, CancellationToken ct)
    {
        var originalPath = Path.Combine(dataPath, assetFileName);
        if (!File.Exists(originalPath))
        {
            logger.LogWarning("资产文件不存在: {File}", originalPath);
            return;
        }

        var backupFileName = ComputeBackupFileName(gamePath, originalPath);
        // Pre-copy backup while file handle is not yet open
        EnsureBackupCopy(gameId, originalPath, backupFileName);

        var replacedFonts = new List<ReplacedFontEntry>();
        AssetsFileInstance? afileInst = null;

        try
        {
            afileInst = manager.LoadAssetsFile(originalPath, loadDeps: false);
            var afile = afileInst.file;

            LoadClassDatabase(manager, afile, unityVersion);

            foreach (var font in fontsInFile)
            {
                ct.ThrowIfCancellationRequested();

                try
                {
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

                    processedFonts++;
                    progress?.Report(new FontReplacementProgress
                    {
                        Phase = "replacing",
                        Current = processedFonts,
                        Total = totalFonts,
                        CurrentFile = $"{assetFileName} - {fontName}"
                    });
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "替换字体失败: PathId={PathId} in {File}", font.PathId, assetFileName);
                    failedFonts.Add(new FailedFontEntry
                    {
                        PathId = font.PathId,
                        AssetFile = assetFileName,
                        Error = ex.Message
                    });
                    processedFonts++;
                    progress?.Report(new FontReplacementProgress
                    {
                        Phase = "replacing",
                        Current = processedFonts,
                        Total = totalFonts,
                        CurrentFile = assetFileName
                    });
                }
            }

            if (replacedFonts.Count > 0)
            {
                progress?.Report(new FontReplacementProgress
                {
                    Phase = "saving",
                    Current = processedFonts,
                    Total = totalFonts,
                    CurrentFile = assetFileName
                });

                var tmpPath = originalPath + ".tmp";
                try
                {
                    using (var writer = new AssetsFileWriter(tmpPath))
                        afile.Write(writer);
                    manager.UnloadAssetsFile(afileInst);
                    afileInst = null;
                    File.Move(tmpPath, originalPath, overwrite: true);

                    replacedFiles.Add(new ReplacedFileEntry
                    {
                        OriginalPath = originalPath,
                        BackupFileName = backupFileName,
                        ModifiedFileHash = ComputeFileHash(originalPath),
                        ReplacedFonts = replacedFonts
                    });

                    logger.LogInformation("已替换 {Count} 个字体: {File}", replacedFonts.Count, assetFileName);
                }
                catch
                {
                    try { if (File.Exists(tmpPath)) File.Delete(tmpPath); } catch { }
                    throw;
                }
            }
        }
        finally
        {
            if (afileInst != null)
                manager.UnloadAssetsFile(afileInst);
        }
    }

    private void ProcessBundleFile(
        AssetsManager manager, AssetTypeValueField? srcFontBase,
        List<SourceAtlasPage>? srcAtlasPages, byte[]? ttfSourceBytes,
        string gamePath, string gameId, string dataPath, string bundleFileName,
        Dictionary<string, List<FontTarget>> entriesWithFonts, string unityVersion,
        List<ReplacedFileEntry> replacedFiles, List<CatalogFileEntry> catalogFiles,
        List<FailedFontEntry> failedFonts,
        ref int processedFonts, int totalFonts,
        IProgress<FontReplacementProgress>? progress, CancellationToken ct)
    {
        var originalPath = Path.Combine(dataPath, bundleFileName);
        if (!File.Exists(originalPath))
        {
            // Try StreamingAssets
            var streamingPath = Path.Combine(dataPath, "StreamingAssets");
            if (Directory.Exists(streamingPath))
            {
                var candidates = Directory.GetFiles(streamingPath, bundleFileName, SearchOption.AllDirectories);
                if (candidates.Length > 0)
                    originalPath = candidates[0];
            }

            if (!File.Exists(originalPath))
            {
                logger.LogWarning("Bundle 文件不存在: {File}", originalPath);
                return;
            }
        }

        var backupFileName = ComputeBackupFileName(gamePath, originalPath);

        var allReplacedFonts = new List<ReplacedFontEntry>();
        BundleFileInstance? bunInst = null;

        try
        {
            progress?.Report(new FontReplacementProgress
            {
                Phase = "loading",
                Current = processedFonts,
                Total = totalFonts,
                CurrentFile = bundleFileName
            });

            bunInst = manager.LoadBundleFile(originalPath, true);
            var dirInfos = bunInst.file.BlockAndDirInfo.DirectoryInfos;

            bool anyModified = false;

            // Process all entries within this bundle in one pass
            foreach (var (entryName, fontsInEntry) in entriesWithFonts)
            {
                ct.ThrowIfCancellationRequested();

                var fullAssetFileName = $"{bundleFileName}/{entryName}";

                // Find the entry index
                int entryIndex = -1;
                for (int i = 0; i < dirInfos.Count; i++)
                {
                    if (dirInfos[i].Name == entryName)
                    {
                        entryIndex = i;
                        break;
                    }
                }

                if (entryIndex < 0)
                {
                    logger.LogWarning("Bundle entry 不存在: {Entry} in {Bundle}", entryName, bundleFileName);
                    foreach (var font in fontsInEntry)
                    {
                        failedFonts.Add(new FailedFontEntry
                        {
                            PathId = font.PathId, AssetFile = fullAssetFileName,
                            Error = "Bundle entry not found"
                        });
                        processedFonts++;
                    }
                    continue;
                }

                AssetsFileInstance? afileInst = null;
                try
                {
                    afileInst = manager.LoadAssetsFileFromBundle(bunInst, entryIndex, false);
                    var afile = afileInst.file;

                    LoadClassDatabase(manager, afile, unityVersion);

                    var entryReplacedFonts = new List<ReplacedFontEntry>();

                    foreach (var font in fontsInEntry)
                    {
                        ct.ThrowIfCancellationRequested();

                        try
                        {
                            var assetInfo = afile.GetAssetInfo(font.PathId);
                            if (assetInfo is null)
                            {
                                failedFonts.Add(new FailedFontEntry
                                {
                                    PathId = font.PathId, AssetFile = fullAssetFileName,
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

                            entryReplacedFonts.Add(new ReplacedFontEntry { Name = fontName, PathId = font.PathId, FontType = fontType });

                            processedFonts++;
                            progress?.Report(new FontReplacementProgress
                            {
                                Phase = "replacing",
                                Current = processedFonts,
                                Total = totalFonts,
                                CurrentFile = $"{fullAssetFileName} - {fontName}"
                            });
                        }
                        catch (Exception ex)
                        {
                            logger.LogWarning(ex, "替换字体失败: PathId={PathId} in {File}", font.PathId, fullAssetFileName);
                            failedFonts.Add(new FailedFontEntry
                            {
                                PathId = font.PathId,
                                AssetFile = fullAssetFileName,
                                Error = ex.Message
                            });
                            processedFonts++;
                            progress?.Report(new FontReplacementProgress
                            {
                                Phase = "replacing",
                                Current = processedFonts,
                                Total = totalFonts,
                                CurrentFile = fullAssetFileName
                            });
                        }
                    }

                    if (entryReplacedFonts.Count > 0)
                    {
                        dirInfos[entryIndex].SetNewData(afile);
                        anyModified = true;
                        allReplacedFonts.AddRange(entryReplacedFonts);
                    }
                }
                finally
                {
                    if (afileInst != null)
                        manager.UnloadAssetsFile(afileInst);
                }
            }

            if (anyModified)
            {
                progress?.Report(new FontReplacementProgress
                {
                    Phase = "saving",
                    Current = processedFonts,
                    Total = totalFonts,
                    CurrentFile = bundleFileName
                });

                var tmpPath = originalPath + ".tmp";
                var compressedPath = originalPath + ".lz4.tmp";
                try
                {
                    // Write uncompressed bundle to temp file — ONCE for all entries
                    using (var writer = new AssetsFileWriter(tmpPath))
                        bunInst.file.Write(writer);

                    manager.UnloadBundleFile(bunInst);
                    bunInst = null;

                    // Move original to backup (instant on same volume) — after file handle released
                    EnsureBackup(gameId, originalPath, backupFileName);

                    // Recompress with LZ4 — ONCE
                    using var reReader = new AssetsFileReader(File.OpenRead(tmpPath));
                    var newBun = new AssetBundleFile();
                    newBun.Read(reReader);
                    using (var writer = new AssetsFileWriter(compressedPath))
                        newBun.Pack(writer, AssetBundleCompressionType.LZ4);
                    newBun.Close();
                    reReader.Close();
                    File.Delete(tmpPath);
                    File.Move(compressedPath, originalPath, overwrite: true);

                    replacedFiles.Add(new ReplacedFileEntry
                    {
                        OriginalPath = originalPath,
                        BackupFileName = backupFileName,
                        ModifiedFileHash = ComputeFileHash(originalPath),
                        ReplacedFonts = allReplacedFonts
                    });

                    logger.LogInformation("已替换 {Count} 个字体 (Bundle): {File}", allReplacedFonts.Count, bundleFileName);
                }
                catch
                {
                    // Clean up temp files on failure; restore backup if needed
                    try { if (File.Exists(tmpPath)) File.Delete(tmpPath); } catch { }
                    try { if (File.Exists(compressedPath)) File.Delete(compressedPath); } catch { }
                    // If original was moved to backup but write failed, restore it
                    var backupPath = Path.Combine(appDataPaths.GetFontBackupDirectory(gameId), backupFileName);
                    if (!File.Exists(originalPath) && File.Exists(backupPath))
                    {
                        try { File.Move(backupPath, originalPath); }
                        catch (Exception restoreEx)
                        {
                            logger.LogError(restoreEx, "备份恢复失败: {Backup} → {Original}", backupPath, originalPath);
                        }
                    }
                    throw;
                }
            }
        }
        finally
        {
            if (bunInst != null)
                manager.UnloadBundleFile(bunInst);
        }
    }

    private static void ReplaceSingleFont(
        AssetsManager manager, AssetsFileInstance afileInst,
        AssetTypeValueField srcFontBase,
        List<SourceAtlasPage> srcAtlasPages,
        long targetPathId, out string fontName)
    {
        var afile = afileInst.file;

        // Find the target MonoBehaviour by PathId
        AssetFileInfo? targetMbInfo = null;
        foreach (var mbInfo in afile.GetAssetsOfType(AssetClassID.MonoBehaviour))
        {
            if (mbInfo.PathId == targetPathId)
            {
                targetMbInfo = mbInfo;
                break;
            }
        }

        if (targetMbInfo is null)
            throw new InvalidOperationException($"Target MonoBehaviour not found (PathId={targetPathId}).");

        var dstFontBase = manager.GetBaseField(afileInst, targetMbInfo);
        if (dstFontBase.IsDummy)
            throw new InvalidOperationException($"Cannot read target MonoBehaviour (PathId={targetPathId}).");

        fontName = dstFontBase["m_Name"].IsDummy ? "(unnamed)" : dstFontBase["m_Name"].AsString;

        // Copy font data fields from source to destination
        CopyFontFields(srcFontBase, dstFontBase);

        // === Multi-atlas texture replacement ===
        var dstAtlasTextures = dstFontBase["m_AtlasTextures"];
        if (dstAtlasTextures.IsDummy || dstAtlasTextures["Array"].Children.Count == 0)
            throw new InvalidOperationException($"Target font '{fontName}' has no atlas textures.");

        var existingDstTexCount = dstAtlasTextures["Array"].Children.Count;

        // Replace existing texture slots (min of source and destination count)
        var replaceCount = Math.Min(srcAtlasPages.Count, existingDstTexCount);
        for (int pageIdx = 0; pageIdx < replaceCount; pageIdx++)
        {
            var dstAtlasPathId = dstAtlasTextures["Array"][pageIdx]["m_PathID"].AsLong;

            AssetFileInfo? dstTexInfo = null;
            foreach (var texInfo in afile.GetAssetsOfType(AssetClassID.Texture2D))
            {
                if (texInfo.PathId == dstAtlasPathId)
                {
                    dstTexInfo = texInfo;
                    break;
                }
            }

            if (dstTexInfo is null) continue;

            var page = srcAtlasPages[pageIdx];
            var dstTexBase = manager.GetBaseField(afileInst, dstTexInfo);
            var dstTexFile = TextureFile.ReadTextureFile(dstTexBase);
            dstTexFile.m_TextureFormat = page.TextureFormat;
            dstTexFile.SetPictureData(page.EncodedData, page.Width, page.Height);
            dstTexFile.WriteTo(dstTexBase);

            dstTexBase["m_StreamData"]["path"].AsString = "";
            dstTexBase["m_StreamData"]["offset"].AsULong = 0;
            dstTexBase["m_StreamData"]["size"].AsULong = 0;

            dstTexInfo.SetNewData(dstTexBase);
        }

        // Create new Texture2D assets for extra source pages
        if (srcAtlasPages.Count > existingDstTexCount)
        {
            // Use existing texture as structural template
            var templatePathId = dstAtlasTextures["Array"][0]["m_PathID"].AsLong;
            AssetTypeValueField? texTemplate = null;
            foreach (var texInfo in afile.GetAssetsOfType(AssetClassID.Texture2D))
            {
                if (texInfo.PathId == templatePathId)
                {
                    texTemplate = manager.GetBaseField(afileInst, texInfo);
                    break;
                }
            }

            if (texTemplate != null)
            {
                var ptrProto = dstAtlasTextures["Array"].Children[0];
                var newPtrList = new List<AssetTypeValueField>(dstAtlasTextures["Array"].Children);

                for (int pageIdx = existingDstTexCount; pageIdx < srcAtlasPages.Count; pageIdx++)
                {
                    var page = srcAtlasPages[pageIdx];

                    var newTexBase = ValueBuilder.DefaultValueFieldFromTemplate(texTemplate.TemplateField);
                    // Read metadata from existing texture (m_MipCount, m_TextureSettings, etc.)
                    // NOT from newTexBase which has all zeros — Unity won't render mip=0 textures
                    var texFile = TextureFile.ReadTextureFile(texTemplate);
                    texFile.m_TextureFormat = page.TextureFormat;
                    texFile.SetPictureData(page.EncodedData, page.Width, page.Height);
                    texFile.WriteTo(newTexBase);

                    newTexBase["m_StreamData"]["path"].AsString = "";
                    newTexBase["m_StreamData"]["offset"].AsULong = 0;
                    newTexBase["m_StreamData"]["size"].AsULong = 0;
                    newTexBase["m_Name"].AsString = $"{fontName} Atlas {pageIdx}";

                    // Generate globally unique PathId
                    var newPathId = afile.Metadata.AssetInfos.Max(a => a.PathId) + 1;
                    var newInfo = AssetFileInfo.Create(afile, newPathId, (int)AssetClassID.Texture2D, null);
                    newInfo.SetNewData(newTexBase);
                    afile.Metadata.AssetInfos.Add(newInfo);

                    // Add PPtr to atlas textures array
                    var newPtr = ValueBuilder.DefaultValueFieldFromTemplate(ptrProto.TemplateField);
                    newPtr["m_FileID"].AsInt = 0;
                    newPtr["m_PathID"].AsLong = newInfo.PathId;
                    newPtrList.Add(newPtr);
                }

                dstAtlasTextures["Array"].Children = newPtrList;
            }
        }

        // Commit modified data
        targetMbInfo.SetNewData(dstFontBase);
    }

    private record SourceAtlasPage(byte[] EncodedData, int Width, int Height, int TextureFormat);

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

        // Value may be null for fonts with empty embedded data — create new AssetTypeValue
        fontData.Value = new AssetTypeValue(ttfSourceBytes, false);

        // Commit modified data
        targetFontInfo.SetNewData(fontBase);
    }

    private static void CopyFontFields(AssetTypeValueField src, AssetTypeValueField dst)
    {
        // All data fields: safe deep copy preserving destination type structure
        string[] fieldsToCopy =
        [
            // Array fields
            "m_GlyphTable", "m_CharacterTable", "m_UsedGlyphRects", "m_FreeGlyphRects",
            // Struct fields
            "m_FaceInfo", "m_FontFeatureTable", "m_CreationSettings",
            // Scalar fields
            "m_AtlasWidth", "m_AtlasHeight", "m_AtlasPadding",
            "m_AtlasRenderMode", "m_AtlasTextureIndex",
            "m_AtlasPopulationMode", "m_IsMultiAtlasTexturesEnabled",
            "m_Version"
        ];

        foreach (var name in fieldsToCopy)
        {
            var srcField = src[name];
            var dstField = dst[name];
            if (!srcField.IsDummy && !dstField.IsDummy)
                DeepCopyFieldValues(srcField, dstField);
        }

        // DO NOT touch: m_AtlasTextures, material, m_SourceFontFile, m_FallbackFontAssetTable
    }

    /// <summary>
    /// Recursively copies values from src to dst, matching fields by name.
    /// Preserves dst's type tree structure to avoid serialization mismatches across TMP versions.
    /// </summary>
    private static void DeepCopyFieldValues(AssetTypeValueField src, AssetTypeValueField dst)
    {
        if (src.IsDummy || dst.IsDummy) return;

        // Copy scalar value if both have values
        if (dst.Value != null && src.Value != null)
            dst.Value = src.Value;

        // Check for array (field has an "Array" child)
        var srcArray = src["Array"];
        var dstArray = dst["Array"];
        if (!srcArray.IsDummy && !dstArray.IsDummy)
        {
            RebuildArrayEntries(srcArray, dstArray);
            return;
        }

        // Struct: match children by field name
        if (dst.Children is not { Count: > 0 }) return;
        if (src.Children is not { Count: > 0 }) return;

        foreach (var dstChild in dst.Children)
        {
            if (string.IsNullOrEmpty(dstChild.FieldName)) continue;
            var srcChild = src[dstChild.FieldName];
            if (!srcChild.IsDummy)
                DeepCopyFieldValues(srcChild, dstChild);
        }
    }

    /// <summary>
    /// Rebuilds array entries from source data using destination's element template structure.
    /// </summary>
    private static void RebuildArrayEntries(
        AssetTypeValueField srcArrayNode, AssetTypeValueField dstArrayNode)
    {
        if (srcArrayNode.Children is not { Count: > 0 })
        {
            dstArrayNode.Children = [];
            return;
        }

        // Get element template from destination
        AssetTypeTemplateField? elementTemplate = null;
        if (dstArrayNode.Children is { Count: > 0 })
            elementTemplate = dstArrayNode.Children[0].TemplateField;
        else if (dstArrayNode.TemplateField?.Children is { Count: >= 2 })
            elementTemplate = dstArrayNode.TemplateField.Children[^1];

        if (elementTemplate is null)
        {
            // No dst template available — use source entries directly as last resort.
            // This preserves the old behavior and may cause issues if TMP versions differ,
            // but is better than an empty array (which would mean no glyphs at all).
            dstArrayNode.Children = new List<AssetTypeValueField>(srcArrayNode.Children);
            return;
        }

        var newEntries = new List<AssetTypeValueField>(srcArrayNode.Children.Count);
        foreach (var srcEntry in srcArrayNode.Children)
        {
            var newEntry = ValueBuilder.DefaultValueFieldFromTemplate(elementTemplate);
            DeepCopyFieldValues(srcEntry, newEntry);
            newEntries.Add(newEntry);
        }
        dstArrayNode.Children = newEntries;
    }

    private static void LoadClassDatabase(AssetsManager manager, AssetsFile afile, string unityVersion)
    {
        try
        {
            manager.LoadClassDatabaseFromPackage(afile.Metadata.UnityVersion);
        }
        catch
        {
            try { manager.LoadClassDatabaseFromPackage(unityVersion); }
            catch
            {
                if (!afile.Metadata.TypeTreeEnabled)
                    throw new InvalidOperationException("No class database and no embedded type tree.");
            }
        }
    }

    private string ComputeBackupFileName(string gamePath, string filePath)
    {
        var relativePath = Path.GetRelativePath(gamePath, filePath);
        return relativePath.Replace(Path.DirectorySeparatorChar, '_')
                           .Replace(Path.AltDirectorySeparatorChar, '_');
    }

    /// <summary>
    /// Ensures backup exists. Uses File.Move (instant on same volume) when file handle is released.
    /// Falls back to File.Copy for cross-volume scenarios.
    /// Call AFTER unloading the file to avoid handle conflicts.
    /// </summary>
    private void EnsureBackup(string gameId, string filePath, string backupFileName)
    {
        var backupDir = appDataPaths.GetFontBackupDirectory(gameId);
        Directory.CreateDirectory(backupDir);
        var backupPath = Path.Combine(backupDir, backupFileName);
        if (File.Exists(backupPath)) return;

        // Same volume: Move is instant (rename). Cross-volume: fall back to Copy.
        if (string.Equals(Path.GetPathRoot(filePath), Path.GetPathRoot(backupPath),
                StringComparison.OrdinalIgnoreCase))
        {
            File.Move(filePath, backupPath);
        }
        else
        {
            File.Copy(filePath, backupPath);
        }
    }

    /// <summary>
    /// Convenience: compute name + copy backup in one call (for small files like catalogs).
    /// </summary>
    private string BackupFile(string gamePath, string gameId, string filePath)
    {
        var backupFileName = ComputeBackupFileName(gamePath, filePath);
        EnsureBackupCopy(gameId, filePath, backupFileName);
        return backupFileName;
    }

    /// <summary>
    /// Backup via copy — used when file handle is still open.
    /// </summary>
    private void EnsureBackupCopy(string gameId, string filePath, string backupFileName)
    {
        var backupDir = appDataPaths.GetFontBackupDirectory(gameId);
        Directory.CreateDirectory(backupDir);
        var backupPath = Path.Combine(backupDir, backupFileName);
        if (!File.Exists(backupPath))
            File.Copy(filePath, backupPath);
    }

    private static string ComputeFileHash(string filePath)
    {
        using var sha = SHA256.Create();
        using var stream = File.OpenRead(filePath);
        return Convert.ToHexString(sha.ComputeHash(stream));
    }

    private async Task<List<CatalogFileEntry>> ClearAddressablesCrcAsync(
        string gamePath, string gameName, List<string> modifiedBundles,
        string gameId, CancellationToken ct = default)
    {
        var catalogFiles = new List<CatalogFileEntry>();
        var aaPath = Path.Combine(gamePath, $"{gameName}_Data", "StreamingAssets", "aa");

        if (!Directory.Exists(aaPath))
        {
            logger.LogDebug("Addressables 目录不存在，跳过 CRC 清除: {Path}", aaPath);
            return catalogFiles;
        }

        // Handle catalog.json
        var catalogJson = Path.Combine(aaPath, "catalog.json");
        if (File.Exists(catalogJson))
        {
            try
            {
                var backupFileName = BackupFile(gamePath, gameId, catalogJson);
                var content = await File.ReadAllTextAsync(catalogJson, ct);

                // Zero out all CRC entries — safer approach since we're modifying bundles
                var modified = Regex.Replace(content, "\"Crc\"\\s*:\\s*\\d+", "\"Crc\":0");

                await File.WriteAllTextAsync(catalogJson, modified, ct);
                catalogFiles.Add(new CatalogFileEntry
                {
                    OriginalPath = catalogJson,
                    BackupFileName = backupFileName
                });
                logger.LogInformation("已清除 catalog.json 中的 CRC 校验值");
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "清除 catalog.json CRC 失败");
            }

            // Also handle catalog.hash if it exists
            var catalogHash = Path.Combine(aaPath, "catalog.hash");
            if (File.Exists(catalogHash))
            {
                try
                {
                    var hashBackup = BackupFile(gamePath, gameId, catalogHash);
                    File.Delete(catalogHash);
                    catalogFiles.Add(new CatalogFileEntry
                    {
                        OriginalPath = catalogHash,
                        BackupFileName = hashBackup
                    });
                    logger.LogInformation("已备份并删除 catalog.hash");
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "处理 catalog.hash 失败");
                }
            }
        }

        // Handle catalog.bundle (AssetBundle containing TextAsset with catalog JSON)
        var catalogBundle = Path.Combine(aaPath, "catalog.bundle");
        if (File.Exists(catalogBundle))
        {
            try
            {
                var backupFileName = BackupFile(gamePath, gameId, catalogBundle);

                await Task.Run(() =>
                {
                    var manager = new AssetsManager();
                    try
                    {
                        using var tpkStream = new MemoryStream(ClassDataTpk.Value);
                        manager.LoadClassPackage(tpkStream);

                        var bunInst = manager.LoadBundleFile(catalogBundle, true);
                        var dirInfos = bunInst.file.BlockAndDirInfo.DirectoryInfos;

                        bool modified = false;

                        for (int i = 0; i < dirInfos.Count; i++)
                        {
                            var entryName = dirInfos[i].Name;
                            if (entryName.EndsWith(".resource", StringComparison.OrdinalIgnoreCase) ||
                                entryName.EndsWith(".resS", StringComparison.OrdinalIgnoreCase))
                                continue;

                            AssetsFileInstance? afileInst = null;
                            try
                            {
                                afileInst = manager.LoadAssetsFileFromBundle(bunInst, i, false);
                                var afile = afileInst.file;

                                try { manager.LoadClassDatabaseFromPackage(afile.Metadata.UnityVersion); }
                                catch { /* type tree embedded */ }

                                foreach (var textInfo in afile.GetAssetsOfType(AssetClassID.TextAsset))
                                {
                                    var textBase = manager.GetBaseField(afileInst, textInfo);
                                    if (textBase.IsDummy) continue;

                                    var scriptField = textBase["m_Script"];
                                    if (scriptField.IsDummy) continue;

                                    var script = scriptField.AsString;
                                    if (!script.Contains("\"Crc\"")) continue;

                                    var newScript = Regex.Replace(script, "\"Crc\"\\s*:\\s*\\d+", "\"Crc\":0");
                                    scriptField.AsString = newScript;
                                    textInfo.SetNewData(textBase);
                                    modified = true;
                                }

                                if (modified)
                                    dirInfos[i].SetNewData(afile);
                            }
                            finally
                            {
                                if (afileInst != null)
                                    manager.UnloadAssetsFile(afileInst);
                            }
                        }

                        if (modified)
                        {
                            var tmpPath = catalogBundle + ".tmp";
                            using (var writer = new AssetsFileWriter(tmpPath))
                                bunInst.file.Write(writer);

                            manager.UnloadBundleFile(bunInst);

                            // Recompress with LZ4
                            using var reReader = new AssetsFileReader(File.OpenRead(tmpPath));
                            var newBun = new AssetBundleFile();
                            newBun.Read(reReader);
                            var compressedPath = catalogBundle + ".lz4.tmp";
                            using (var writer = new AssetsFileWriter(compressedPath))
                                newBun.Pack(writer, AssetBundleCompressionType.LZ4);
                            newBun.Close();
                            reReader.Close();
                            File.Delete(tmpPath);
                            File.Move(compressedPath, catalogBundle, overwrite: true);
                        }
                        else
                        {
                            manager.UnloadBundleFile(bunInst);
                        }
                    }
                    finally
                    {
                        manager.UnloadAll();
                    }
                }, ct);

                catalogFiles.Add(new CatalogFileEntry
                {
                    OriginalPath = catalogBundle,
                    BackupFileName = backupFileName
                });
                logger.LogInformation("已清除 catalog.bundle 中的 CRC 校验值");
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "清除 catalog.bundle CRC 失败，不影响字体替换");
            }
        }

        if (catalogFiles.Count == 0)
            logger.LogDebug("未找到 Addressables catalog 文件: {Path}", aaPath);

        return catalogFiles;
    }

    public bool ValidateCustomFont(string fontPath)
    {
        try
        {
            var manager = new AssetsManager();
            try
            {
                using var tpkStream = new MemoryStream(ClassDataTpk.Value);
                manager.LoadClassPackage(tpkStream);

                var bunInst = manager.LoadBundleFile(fontPath, true);
                var dirInfos = bunInst.file.BlockAndDirInfo.DirectoryInfos;

                for (int i = 0; i < dirInfos.Count; i++)
                {
                    var entryName = dirInfos[i].Name;
                    if (entryName.EndsWith(".resource", StringComparison.OrdinalIgnoreCase) ||
                        entryName.EndsWith(".resS", StringComparison.OrdinalIgnoreCase))
                        continue;

                    AssetsFileInstance? afileInst = null;
                    try
                    {
                        afileInst = manager.LoadAssetsFileFromBundle(bunInst, i, false);
                        var afile = afileInst.file;

                        try { manager.LoadClassDatabaseFromPackage(afile.Metadata.UnityVersion); }
                        catch { /* type tree embedded */ }

                        foreach (var mbInfo in afile.GetAssetsOfType(AssetClassID.MonoBehaviour))
                        {
                            try
                            {
                                var mbBase = manager.GetBaseField(afileInst, mbInfo);
                                if (mbBase.IsDummy) continue;

                                if (!mbBase["m_GlyphTable"].IsDummy && !mbBase["m_Version"].IsDummy)
                                    return true;
                            }
                            catch
                            {
                                // MonoBehaviour deserialization can fail — skip
                            }
                        }
                    }
                    finally
                    {
                        if (afileInst != null)
                            manager.UnloadAssetsFile(afileInst);
                    }
                }

                return false;
            }
            finally
            {
                manager.UnloadAll();
            }
        }
        catch
        {
            return false;
        }
    }

    public async Task RestoreFontsAsync(string gamePath, string gameId)
    {
        var backupDir = appDataPaths.GetFontBackupDirectory(gameId);
        var manifestPath = Path.Combine(backupDir, "manifest.json");

        if (!File.Exists(manifestPath))
        {
            logger.LogWarning("字体备份清单不存在: {Path}", manifestPath);
            return;
        }

        var json = await File.ReadAllTextAsync(manifestPath);
        var manifest = JsonSerializer.Deserialize<FontBackupManifest>(json,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        if (manifest is null)
        {
            logger.LogWarning("字体备份清单解析失败: {Path}", manifestPath);
            return;
        }

        // Restore replaced files
        foreach (var entry in manifest.ReplacedFiles)
        {
            var backupPath = Path.Combine(backupDir, entry.BackupFileName);
            if (File.Exists(backupPath))
            {
                try
                {
                    File.Copy(backupPath, entry.OriginalPath, overwrite: true);
                    logger.LogInformation("已还原: {Path}", entry.OriginalPath);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "还原文件失败: {Path}", entry.OriginalPath);
                }
            }
            else
            {
                logger.LogWarning("备份文件不存在: {Path}", backupPath);
            }
        }

        // Restore catalog files
        foreach (var entry in manifest.CatalogFiles)
        {
            var backupPath = Path.Combine(backupDir, entry.BackupFileName);
            if (File.Exists(backupPath))
            {
                try
                {
                    File.Copy(backupPath, entry.OriginalPath, overwrite: true);
                    logger.LogInformation("已还原: {Path}", entry.OriginalPath);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "还原 catalog 文件失败: {Path}", entry.OriginalPath);
                }
            }
            else
            {
                logger.LogWarning("catalog 备份文件不存在: {Path}", backupPath);
            }
        }

        // Delete backup directory
        try
        {
            Directory.Delete(backupDir, recursive: true);
            logger.LogInformation("已删除备份目录: {Path}", backupDir);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "删除备份目录失败: {Path}", backupDir);
        }

        logger.LogInformation("字体还原完成: GameId={GameId}", gameId);
    }

    private string? GetCustomTtfFileName(string gameId)
    {
        var customDir = appDataPaths.GetCustomFontDirectory(gameId);
        if (!Directory.Exists(customDir)) return null;
        var file = Directory.GetFiles(customDir)
            .FirstOrDefault(f => Path.GetExtension(f).ToLowerInvariant() is ".ttf" or ".otf");
        return file != null ? Path.GetFileName(file) : null;
    }

    private string? GetCustomTmpFileName(string gameId)
    {
        var customDir = appDataPaths.GetCustomFontDirectory(gameId);
        if (!Directory.Exists(customDir)) return null;
        var file = Directory.GetFiles(customDir)
            .FirstOrDefault(f => Path.GetExtension(f).ToLowerInvariant() is not ".ttf" and not ".otf");
        return file != null ? Path.GetFileName(file) : null;
    }

    public async Task<FontReplacementStatus> GetStatusAsync(string gamePath, string gameId)
    {
        var backupDir = appDataPaths.GetFontBackupDirectory(gameId);
        var manifestPath = Path.Combine(backupDir, "manifest.json");
        var customTtfFileName = GetCustomTtfFileName(gameId);
        var customTmpFileName = GetCustomTmpFileName(gameId);

        if (!File.Exists(manifestPath))
            return new FontReplacementStatus
            {
                CustomTtfFileName = customTtfFileName,
                CustomTmpFileName = customTmpFileName
            };

        try
        {
            var json = await File.ReadAllTextAsync(manifestPath);
            var manifest = JsonSerializer.Deserialize<FontBackupManifest>(json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            if (manifest is null)
                return new FontReplacementStatus();

            // Quick external-restore detection: check if backup dir exists but files differ
            var externallyRestored = false;
            await Task.Run(() =>
            {
                foreach (var entry in manifest.ReplacedFiles)
                {
                    if (!File.Exists(entry.OriginalPath))
                    {
                        externallyRestored = true;
                        break;
                    }
                    try
                    {
                        var currentHash = ComputeFileHash(entry.OriginalPath);
                        if (currentHash != entry.ModifiedFileHash)
                        {
                            externallyRestored = true;
                            break;
                        }
                    }
                    catch
                    {
                        // File access error — treat as potentially restored
                        externallyRestored = true;
                        break;
                    }
                }
            });

            // Collect replaced font info
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

            return new FontReplacementStatus
            {
                IsReplaced = true,
                BackupExists = Directory.Exists(backupDir),
                ReplacedAt = manifest.ReplacedAt,
                FontSource = manifest.FontSource,
                ReplacedFonts = replacedFonts,
                IsExternallyRestored = externallyRestored,
                CustomTtfFileName = customTtfFileName,
                CustomTmpFileName = customTmpFileName
            };
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "读取字体替换状态失败: {GameId}", gameId);
            return new FontReplacementStatus();
        }
    }
}
