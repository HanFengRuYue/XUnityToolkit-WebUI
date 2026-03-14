using System.Reflection;
using AssetsTools.NET;
using AssetsTools.NET.Extra;
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
}
