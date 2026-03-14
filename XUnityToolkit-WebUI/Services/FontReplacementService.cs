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

    public async Task<List<TmpFontInfo>> ScanFontsAsync(
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

        // Find bundle files (data.unity3d + StreamingAssets bundles)
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

        if (assetFiles.Count == 0 && bundleFiles.Count == 0)
            throw new FileNotFoundException("No asset files found in game data directory.");

        logger.LogInformation("字体扫描: 找到 {AssetCount} 个资产文件, {BundleCount} 个 Bundle 文件: {Path}",
            assetFiles.Count, bundleFiles.Count, dataPath);

        return await Task.Run(() =>
        {
            var results = new List<TmpFontInfo>();
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

            logger.LogInformation("字体扫描完成: 找到 {Count} 个 TMP 字体", results.Count);
            return results;
        }, ct);
    }

    private List<TmpFontInfo> ScanAssetsInstance(
        AssetsManager manager, AssetsFileInstance afileInst, string fileName,
        string unityVersion, bool isBundle)
    {
        var results = new List<TmpFontInfo>();
        var afile = afileInst.file;

        // Load class database for this Unity version
        try
        {
            manager.LoadClassDatabaseFromPackage(afile.Metadata.UnityVersion);
        }
        catch
        {
            try { manager.LoadClassDatabaseFromPackage(unityVersion); }
            catch
            {
                // If type tree is embedded, we can still read without class database
                if (!afile.Metadata.TypeTreeEnabled)
                    return results;
            }
        }

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

                    results.Add(new TmpFontInfo
                    {
                        Name = name,
                        PathId = mbInfo.PathId,
                        AssetFile = fileName,
                        IsInBundle = isBundle,
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
                    results.Add(new TmpFontInfo
                    {
                        Name = name,
                        PathId = mbInfo.PathId,
                        AssetFile = fileName,
                        IsInBundle = isBundle,
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

    public async Task ReplaceFontsAsync(
        string gamePath, string gameId, UnityGameInfo gameInfo,
        FontTarget[] fonts, string? customFontPath,
        IProgress<FontReplacementProgress>? progress, CancellationToken ct = default)
    {
        var fontFile = customFontPath ?? tmpFontService.ResolveFontFile(gameInfo.UnityVersion);
        if (fontFile is null || !File.Exists(fontFile))
            throw new FileNotFoundException($"Font file not found: {fontFile ?? "(none)"}");

        var gameName = Path.GetFileNameWithoutExtension(gameInfo.DetectedExecutable);
        var dataPath = Path.Combine(gamePath, $"{gameName}_Data");
        var backupDir = appDataPaths.GetFontBackupDirectory(gameId);
        Directory.CreateDirectory(backupDir);

        var replacedFiles = new List<ReplacedFileEntry>();
        var catalogFiles = new List<CatalogFileEntry>();

        logger.LogInformation("字体替换开始: {Count} 个字体, 源字体: {Font}", fonts.Length, Path.GetFileName(fontFile));

        await Task.Run(() =>
        {
            // === Step 1: Load source font from bundled AssetBundle ===
            var srcManager = new AssetsManager();
            AssetTypeValueField srcFontBase;
            AssetTypeValueField srcTexBase;
            AssetsFileInstance srcAfileInst;

            try
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
                srcAfileInst = foundSrcAfileInst;

                // Find source atlas Texture2D via m_AtlasTextures[0] PPtr
                var srcAtlasTextures = srcFontBase["m_AtlasTextures"];
                if (srcAtlasTextures.IsDummy || srcAtlasTextures["Array"].Children.Count == 0)
                    throw new InvalidOperationException("Source font has no atlas textures.");

                var srcAtlasPPtr = srcAtlasTextures["Array"][0];
                var srcAtlasPathId = srcAtlasPPtr["m_PathID"].AsLong;

                AssetTypeValueField? foundSrcTex = null;
                foreach (var texInfo in srcAfileInst.file.GetAssetsOfType(AssetClassID.Texture2D))
                {
                    if (texInfo.PathId == srcAtlasPathId)
                    {
                        foundSrcTex = srcManager.GetBaseField(srcAfileInst, texInfo);
                        break;
                    }
                }

                if (foundSrcTex is null)
                    throw new InvalidOperationException($"Source atlas Texture2D not found (PathId={srcAtlasPathId}).");

                srcTexBase = foundSrcTex;

                // Read source texture raw data
                var srcTexFile = TextureFile.ReadTextureFile(srcTexBase);
                // FillPictureData loads encoded texture bytes (from inline or streaming)
                var srcEncodedData = srcTexFile.FillPictureData(srcAfileInst)
                    ?? throw new InvalidOperationException("Failed to read source atlas texture data.");
                var srcWidth = srcTexFile.m_Width;
                var srcHeight = srcTexFile.m_Height;
                var srcTextureFormat = srcTexFile.m_TextureFormat;

                logger.LogInformation("源字体已加载: {Glyphs} 个字形, 图集 {W}x{H}, 格式 {Fmt}",
                    srcFontBase["m_GlyphTable"]["Array"].Children.Count, srcWidth, srcHeight, srcTextureFormat);

                // === Step 2: Group target fonts by asset file ===
                var fontsByFile = fonts.GroupBy(f => f.AssetFile).ToList();
                int totalFonts = fonts.Length;
                int processedFonts = 0;

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

                    foreach (var fileGroup in fontsByFile)
                    {
                        ct.ThrowIfCancellationRequested();

                        var assetFileName = fileGroup.Key;
                        var fontsInFile = fileGroup.ToList();

                        progress?.Report(new FontReplacementProgress
                        {
                            Phase = "replacing",
                            Current = processedFonts,
                            Total = totalFonts,
                            CurrentFile = assetFileName
                        });

                        // Determine if this is a bundle entry (contains '/')
                        var slashIndex = assetFileName.IndexOf('/');
                        bool isBundle = slashIndex >= 0;

                        if (isBundle)
                        {
                            ProcessBundleFile(dstManager, srcFontBase, srcEncodedData, srcWidth, srcHeight, srcTextureFormat,
                                gamePath, gameId, dataPath, assetFileName, fontsInFile,
                                gameInfo.UnityVersion, replacedFiles, catalogFiles, ref processedFonts, totalFonts,
                                progress, ct);
                        }
                        else
                        {
                            ProcessLooseFile(dstManager, srcFontBase, srcEncodedData, srcWidth, srcHeight, srcTextureFormat,
                                gamePath, gameId, dataPath, assetFileName, fontsInFile,
                                gameInfo.UnityVersion, replacedFiles, ref processedFonts, totalFonts,
                                progress, ct);
                        }
                    }
                }
                catch (Exception)
                {
                    // Rollback: restore all backed-up files
                    foreach (var entry in replacedFiles)
                    {
                        var backupPath = Path.Combine(backupDir, entry.BackupFileName);
                        if (File.Exists(backupPath) && !File.Exists(entry.OriginalPath + ".bak"))
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
            FontSource = Path.GetFileName(fontFile),
            ReplacedFiles = replacedFiles,
            CatalogFiles = catalogFiles
        };
        var json = JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(Path.Combine(backupDir, "manifest.json"), json, ct);

        logger.LogInformation("字体替换完成: 替换了 {Count} 个字体, 修改了 {Files} 个文件",
            fonts.Length, replacedFiles.Count);

        progress?.Report(new FontReplacementProgress
        {
            Phase = "completed",
            Current = fonts.Length,
            Total = fonts.Length
        });
    }

    private void ProcessLooseFile(
        AssetsManager manager, AssetTypeValueField srcFontBase,
        byte[] srcEncodedData, int srcWidth, int srcHeight, int srcTextureFormat,
        string gamePath, string gameId, string dataPath, string assetFileName,
        List<FontTarget> fontsInFile, string unityVersion,
        List<ReplacedFileEntry> replacedFiles,
        ref int processedFonts, int totalFonts,
        IProgress<FontReplacementProgress>? progress, CancellationToken ct)
    {
        var originalPath = Path.Combine(dataPath, assetFileName);
        if (!File.Exists(originalPath))
        {
            logger.LogWarning("资产文件不存在: {File}", originalPath);
            return;
        }

        // Backup before modifying
        var backupFileName = BackupFile(gamePath, gameId, originalPath);

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
                    ReplaceSingleFont(manager, afileInst, srcFontBase,
                        srcEncodedData, srcWidth, srcHeight, srcTextureFormat, font.PathId, out var fontName);

                    replacedFonts.Add(new ReplacedFontEntry { Name = fontName, PathId = font.PathId });

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
                }
            }

            if (replacedFonts.Count > 0)
            {
                // Write to temp file then replace original
                var tmpPath = originalPath + ".tmp";
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
        }
        finally
        {
            if (afileInst != null)
                manager.UnloadAssetsFile(afileInst);
        }
    }

    private void ProcessBundleFile(
        AssetsManager manager, AssetTypeValueField srcFontBase,
        byte[] srcEncodedData, int srcWidth, int srcHeight, int srcTextureFormat,
        string gamePath, string gameId, string dataPath, string assetFileName,
        List<FontTarget> fontsInFile, string unityVersion,
        List<ReplacedFileEntry> replacedFiles, List<CatalogFileEntry> catalogFiles,
        ref int processedFonts, int totalFonts,
        IProgress<FontReplacementProgress>? progress, CancellationToken ct)
    {
        // AssetFile for bundle entries looks like "data.unity3d/CAB-xxx"
        var slashIndex = assetFileName.IndexOf('/');
        var bundleFileName = assetFileName[..slashIndex];
        var entryName = assetFileName[(slashIndex + 1)..];

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

        // Backup before modifying
        var backupFileName = BackupFile(gamePath, gameId, originalPath);

        var replacedFonts = new List<ReplacedFontEntry>();
        BundleFileInstance? bunInst = null;

        try
        {
            bunInst = manager.LoadBundleFile(originalPath, true);
            var dirInfos = bunInst.file.BlockAndDirInfo.DirectoryInfos;

            // Find the entry index matching the target entry name
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
                return;
            }

            AssetsFileInstance? afileInst = null;
            try
            {
                afileInst = manager.LoadAssetsFileFromBundle(bunInst, entryIndex, false);
                var afile = afileInst.file;

                LoadClassDatabase(manager, afile, unityVersion);

                foreach (var font in fontsInFile)
                {
                    ct.ThrowIfCancellationRequested();

                    try
                    {
                        ReplaceSingleFont(manager, afileInst, srcFontBase,
                            srcEncodedData, srcWidth, srcHeight, srcTextureFormat, font.PathId, out var fontName);

                        replacedFonts.Add(new ReplacedFontEntry { Name = fontName, PathId = font.PathId });

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
                    }
                }

                if (replacedFonts.Count > 0)
                {
                    // Update the bundle directory entry with modified assets
                    dirInfos[entryIndex].SetNewData(afile);

                    // Write uncompressed bundle to temp file
                    var tmpPath = originalPath + ".tmp";
                    using (var writer = new AssetsFileWriter(tmpPath))
                        bunInst.file.Write(writer);

                    manager.UnloadAssetsFile(afileInst);
                    afileInst = null;
                    manager.UnloadBundleFile(bunInst);
                    bunInst = null;

                    // Recompress with LZ4
                    using var reReader = new AssetsFileReader(File.OpenRead(tmpPath));
                    var newBun = new AssetBundleFile();
                    newBun.Read(reReader);
                    var compressedPath = originalPath + ".lz4.tmp";
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
                        ReplacedFonts = replacedFonts
                    });

                    logger.LogInformation("已替换 {Count} 个字体 (Bundle): {File}", replacedFonts.Count, assetFileName);
                }
            }
            finally
            {
                if (afileInst != null)
                    manager.UnloadAssetsFile(afileInst);
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
        byte[] srcEncodedData, int srcWidth, int srcHeight, int srcTextureFormat,
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

        // Find the target's atlas Texture2D via m_AtlasTextures[0] PPtr
        var dstAtlasTextures = dstFontBase["m_AtlasTextures"];
        if (dstAtlasTextures.IsDummy || dstAtlasTextures["Array"].Children.Count == 0)
            throw new InvalidOperationException($"Target font '{fontName}' has no atlas textures.");

        var dstAtlasPPtr = dstAtlasTextures["Array"][0];
        var dstAtlasPathId = dstAtlasPPtr["m_PathID"].AsLong;

        AssetFileInfo? dstTexInfo = null;
        foreach (var texInfo in afile.GetAssetsOfType(AssetClassID.Texture2D))
        {
            if (texInfo.PathId == dstAtlasPathId)
            {
                dstTexInfo = texInfo;
                break;
            }
        }

        if (dstTexInfo is null)
            throw new InvalidOperationException($"Target atlas Texture2D not found (PathId={dstAtlasPathId}).");

        // Replace texture data using raw encoded bytes
        var dstTexBase = manager.GetBaseField(afileInst, dstTexInfo);
        var dstTexFile = TextureFile.ReadTextureFile(dstTexBase);
        dstTexFile.m_TextureFormat = srcTextureFormat;
        dstTexFile.SetPictureData(srcEncodedData, srcWidth, srcHeight);
        dstTexFile.WriteTo(dstTexBase);

        // Clear streaming data to force inline texture
        dstTexBase["m_StreamData"]["path"].AsString = "";
        dstTexBase["m_StreamData"]["offset"].AsULong = 0;
        dstTexBase["m_StreamData"]["size"].AsULong = 0;

        // Commit modified data
        dstTexInfo.SetNewData(dstTexBase);
        targetMbInfo.SetNewData(dstFontBase);
    }

    private static void CopyFontFields(AssetTypeValueField src, AssetTypeValueField dst)
    {
        // Array fields: swap children
        SwapArrayField(src, dst, "m_GlyphTable");
        SwapArrayField(src, dst, "m_CharacterTable");
        SwapArrayField(src, dst, "m_UsedGlyphRects");
        SwapArrayField(src, dst, "m_FreeGlyphRects");

        // Struct fields: swap children
        SwapStructField(src, dst, "m_FaceInfo");
        SwapStructField(src, dst, "m_FontFeatureTable");
        SwapStructField(src, dst, "m_CreationSettings");

        // Scalar fields
        dst["m_AtlasWidth"].AsInt = src["m_AtlasWidth"].AsInt;
        dst["m_AtlasHeight"].AsInt = src["m_AtlasHeight"].AsInt;
        dst["m_AtlasPadding"].AsInt = src["m_AtlasPadding"].AsInt;
        dst["m_AtlasRenderMode"].AsInt = src["m_AtlasRenderMode"].AsInt;
        dst["m_AtlasTextureIndex"].AsInt = src["m_AtlasTextureIndex"].AsInt;
        dst["m_Version"].AsString = src["m_Version"].AsString;

        // DO NOT touch: m_AtlasTextures, material, m_SourceFontFile, m_FallbackFontAssetTable
    }

    private static void SwapArrayField(AssetTypeValueField src, AssetTypeValueField dst, string name)
    {
        var srcField = src[name];
        var dstField = dst[name];
        if (srcField.IsDummy || dstField.IsDummy) return;
        dstField["Array"].Children = new List<AssetTypeValueField>(srcField["Array"].Children);
    }

    private static void SwapStructField(AssetTypeValueField src, AssetTypeValueField dst, string name)
    {
        var srcField = src[name];
        var dstField = dst[name];
        if (srcField.IsDummy || dstField.IsDummy) return;
        dstField.Children = new List<AssetTypeValueField>(srcField.Children);
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

    private string BackupFile(string gamePath, string gameId, string filePath)
    {
        var backupDir = appDataPaths.GetFontBackupDirectory(gameId);
        Directory.CreateDirectory(backupDir);
        var relativePath = Path.GetRelativePath(gamePath, filePath);
        var backupFileName = relativePath.Replace(Path.DirectorySeparatorChar, '_')
                                         .Replace(Path.AltDirectorySeparatorChar, '_');
        var backupPath = Path.Combine(backupDir, backupFileName);
        if (!File.Exists(backupPath))
            File.Copy(filePath, backupPath);
        return backupFileName;
    }

    private static string ComputeFileHash(string filePath)
    {
        using var sha = SHA256.Create();
        using var stream = File.OpenRead(filePath);
        return Convert.ToHexString(sha.ComputeHash(stream));
    }

    public async Task<List<CatalogFileEntry>> ClearAddressablesCrcAsync(
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

    public async Task<FontReplacementStatus> GetStatusAsync(string gamePath, string gameId)
    {
        var backupDir = appDataPaths.GetFontBackupDirectory(gameId);
        var manifestPath = Path.Combine(backupDir, "manifest.json");

        if (!File.Exists(manifestPath))
            return new FontReplacementStatus();

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
                .Select(rf => new TmpFontInfo
                {
                    Name = rf.Name,
                    PathId = rf.PathId,
                    AssetFile = "",
                    IsInBundle = false,
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
                IsExternallyRestored = externallyRestored
            };
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "读取字体替换状态失败: {GameId}", gameId);
            return new FontReplacementStatus();
        }
    }
}
