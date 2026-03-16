using System.Reflection;
using System.Text.RegularExpressions;
using AssetsTools.NET;
using AssetsTools.NET.Extra;
using XUnityToolkit_WebUI.Models;

namespace XUnityToolkit_WebUI.Services;

public sealed partial class AssetExtractionService(ILogger<AssetExtractionService> logger)
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

    public async Task<AssetExtractionResult> ExtractTextsAsync(
        string gamePath, string? exeName, UnityGameInfo gameInfo,
        IProgress<AssetExtractionProgress>? progress = null,
        CancellationToken ct = default)
    {
        var gameExe = exeName ?? gameInfo.DetectedExecutable;
        var gameName = Path.GetFileNameWithoutExtension(gameExe);
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

        logger.LogInformation("找到 {AssetCount} 个资产文件, {BundleCount} 个 Bundle 文件: {Path}",
            assetFiles.Count, bundleFiles.Count, dataPath);

        var allTexts = new List<ExtractedText>();
        var progressData = new AssetExtractionProgress
        {
            TotalFiles = assetFiles.Count + bundleFiles.Count,
            Phase = "extracting"
        };

        // Run extraction on a thread pool thread to avoid blocking
        await Task.Run(() =>
        {
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

                foreach (var assetFile in assetFiles)
                {
                    ct.ThrowIfCancellationRequested();

                    var fileName = Path.GetFileName(assetFile);
                    progressData.CurrentFile = fileName;
                    progressData.ScannedFiles++;
                    progress?.Report(progressData);

                    AssetsFileInstance? afileInst = null;
                    try
                    {
                        var texts = ExtractFromAssetFile(manager, assetFile, fileName, gameInfo.UnityVersion, out afileInst);
                        allTexts.AddRange(texts);
                        progressData.ExtractedTexts = allTexts.Count;
                        progress?.Report(progressData);
                    }
                    catch (Exception ex)
                    {
                        logger.LogWarning(ex, "提取资产文件失败: {File}", fileName);
                    }
                    finally
                    {
                        if (afileInst != null)
                            manager.UnloadAssetsFile(afileInst);
                    }
                }

                // Process bundle files (data.unity3d + StreamingAssets .bundle)
                foreach (var bundleFile in bundleFiles)
                {
                    ct.ThrowIfCancellationRequested();

                    var fileName = Path.GetFileName(bundleFile);
                    progressData.CurrentFile = fileName;
                    progressData.ScannedFiles++;
                    progress?.Report(progressData);

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
                                var texts = ExtractFromAssetsInstance(manager, bundleAfileInst,
                                    $"{fileName}/{entryName}", gameInfo.UnityVersion);
                                allTexts.AddRange(texts);
                            }
                            catch (Exception ex)
                            {
                                logger.LogDebug(ex, "Bundle 资产读取失败: {Bundle}/{Entry}", fileName, entryName);
                            }
                            finally
                            {
                                if (bundleAfileInst != null)
                                    manager.UnloadAssetsFile(bundleAfileInst);
                            }
                        }

                        progressData.ExtractedTexts = allTexts.Count;
                        progress?.Report(progressData);
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
        }, ct);

        // Deduplicate
        var uniqueTexts = allTexts
            .DistinctBy(t => t.Text)
            .ToList();

        var totalFilesScanned = assetFiles.Count + bundleFiles.Count;
        logger.LogInformation("提取完成: {Total} 条文本 (去重后 {Unique} 条), 来自 {Files} 个文件",
            allTexts.Count, uniqueTexts.Count, totalFilesScanned);

        progressData.Phase = "detecting";
        progress?.Report(progressData);

        var detectedLang = DetectLanguage(uniqueTexts.Select(t => t.Text).ToList());

        return new AssetExtractionResult
        {
            GameId = string.Empty, // Caller sets this
            Texts = uniqueTexts,
            DetectedLanguage = detectedLang,
            TotalAssetsScanned = totalFilesScanned,
            TotalTextsExtracted = uniqueTexts.Count,
        };
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

    private List<ExtractedText> ExtractFromAssetFile(
        AssetsManager manager, string assetFilePath, string fileName, string unityVersion,
        out AssetsFileInstance? loadedInstance)
    {
        loadedInstance = manager.LoadAssetsFile(assetFilePath, loadDeps: false);
        return ExtractFromAssetsInstance(manager, loadedInstance, fileName, unityVersion);
    }

    private List<ExtractedText> ExtractFromAssetsInstance(
        AssetsManager manager, AssetsFileInstance afileInst, string fileName, string unityVersion)
    {
        var texts = new List<ExtractedText>();
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
                    return texts;
            }
        }

        // Extract TextAsset
        foreach (var texInfo in afile.GetAssetsOfType(AssetClassID.TextAsset))
        {
            try
            {
                var texBase = manager.GetBaseField(afileInst, texInfo);
                var name = texBase["m_Name"].AsString;
                var script = texBase["m_Script"].AsString;

                if (!string.IsNullOrWhiteSpace(script))
                {
                    var extractedLines = ExtractTextLines(script);
                    foreach (var line in extractedLines)
                    {
                        texts.Add(new ExtractedText
                        {
                            Text = line,
                            Source = $"TextAsset:{name}",
                            AssetFile = fileName
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "TextAsset 读取失败: {File}", fileName);
            }
        }

        // Extract MonoBehaviour string fields
        foreach (var mbInfo in afile.GetAssetsOfType(AssetClassID.MonoBehaviour))
        {
            try
            {
                var mbBase = manager.GetBaseField(afileInst, mbInfo);
                if (mbBase.IsDummy) continue;

                var name = mbBase["m_Name"].IsDummy ? "(unnamed)" : mbBase["m_Name"].AsString;
                var strings = new List<string>();
                CollectStrings(mbBase, strings, depth: 0);

                foreach (var str in strings)
                {
                    texts.Add(new ExtractedText
                    {
                        Text = str,
                        Source = $"MonoBehaviour:{name}",
                        AssetFile = fileName
                    });
                }
            }
            catch
            {
                // MonoBehaviour deserialization can fail if template generator isn't available
            }
        }

        return texts;
    }

    private static void CollectStrings(AssetTypeValueField field, List<string> results, int depth)
    {
        if (depth > 10 || field.IsDummy) return;

        if (field.TemplateField.ValueType == AssetValueType.String)
        {
            var val = field.AsString;
            if (IsGameText(val))
                results.Add(val);
            return;
        }

        foreach (var child in field.Children)
            CollectStrings(child, results, depth + 1);
    }

    /// <summary>
    /// Extract meaningful text lines from a TextAsset content string.
    /// </summary>
    private static List<string> ExtractTextLines(string content)
    {
        var lines = new List<string>();

        foreach (var rawLine in content.Split('\n'))
        {
            var line = rawLine.Trim();
            if (IsGameText(line))
                lines.Add(line);
        }

        return lines;
    }

    /// <summary>
    /// Filter out non-game-text strings (paths, GUIDs, code, etc.)
    /// </summary>
    private static bool IsGameText(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        if (text.Length < 2 || text.Length > 2000) return false;

        // Skip paths
        if (text.Contains('/') && (text.Contains('.') || text.Contains('\\'))) return false;
        if (text.Contains(":\\")) return false;

        // Skip GUIDs
        if (GuidPattern().IsMatch(text)) return false;

        // Skip purely numeric or hex strings
        if (NumericPattern().IsMatch(text)) return false;
        if (HexPattern().IsMatch(text)) return false;

        // Skip code-like strings
        if (text.Contains('{') && text.Contains('}') && text.Contains(';')) return false;
        if (text.StartsWith("using ") || text.StartsWith("namespace ") || text.StartsWith("#include")) return false;
        if (text.StartsWith("//") || text.StartsWith("/*")) return false;

        // Skip known Unity internal strings
        if (text.StartsWith("Assets/") || text.StartsWith("Packages/")) return false;
        if ((text.StartsWith("m_") || text.StartsWith("_")) && text.All(c => char.IsLetterOrDigit(c) || c == '_')) return false;

        // Skip single-word camelCase/PascalCase identifiers (likely variable/class names)
        if (!text.Contains(' ') && CamelCasePattern().IsMatch(text) && text.Length > 20) return false;

        // Must contain at least one letter
        if (!text.Any(char.IsLetter)) return false;

        // Skip base64-like strings
        if (text.Length > 40 && Base64LikePattern().IsMatch(text)) return false;

        return true;
    }

    /// <summary>
    /// Detect game language using Unicode range heuristics.
    /// Unity engine internals always contribute English text, so non-Latin scripts
    /// are prioritized as more reliable indicators of the game's true language.
    /// </summary>
    private static string DetectLanguage(IList<string> texts)
    {
        if (texts.Count == 0) return "ja"; // Default

        int japanese = 0, chinese = 0, korean = 0, latin = 0, cyrillic = 0;
        int totalChars = 0;

        // Sample up to 500 texts for performance
        var sample = texts.Count > 500
            ? texts.OrderBy(_ => Random.Shared.Next()).Take(500).ToList()
            : texts;

        foreach (var text in sample)
        {
            foreach (var ch in text)
            {
                totalChars++;

                // Hiragana (3040-309F) or Katakana (30A0-30FF)
                if (ch is >= '\u3040' and <= '\u309F' or >= '\u30A0' and <= '\u30FF')
                    japanese++;
                // CJK Unified Ideographs (4E00-9FFF) — shared by Chinese/Japanese/Korean
                else if (ch is >= '\u4E00' and <= '\u9FFF')
                    chinese++;
                // Hangul (AC00-D7AF) or Hangul Jamo (1100-11FF)
                else if (ch is >= '\uAC00' and <= '\uD7AF' or >= '\u1100' and <= '\u11FF')
                    korean++;
                // Latin with diacritics or basic Latin letters
                else if (ch is >= 'A' and <= 'Z' or >= 'a' and <= 'z' or >= '\u00C0' and <= '\u024F')
                    latin++;
                // Cyrillic
                else if (ch is >= '\u0400' and <= '\u04FF')
                    cyrillic++;
            }
        }

        if (totalChars == 0) return "en";

        // Non-Latin characters are strong indicators of the game's actual language,
        // since Unity engine internals (component names, event names, animation states, etc.)
        // always contribute English/Latin text that can outnumber real game text.
        int nonLatin = japanese + chinese + korean + cyrillic;

        // If Latin text strongly dominates (>80%), classify as English regardless of
        // small amounts of non-Latin characters (e.g. stray asset names, plugin metadata).
        if (latin > totalChars * 0.8)
            return "en";

        // Require meaningful non-Latin presence: at least 50 chars AND ≥2% of total.
        // This avoids misclassifying English games that have a few stray CJK/kana chars.
        if (nonLatin >= 50 && nonLatin >= totalChars * 0.02)
        {
            // For Japanese: kana must be a meaningful portion of CJK+kana characters.
            // A few stray kana in an otherwise English game should not trigger Japanese.
            int cjkPlusKana = japanese + chinese;
            if (japanese > 0 && cjkPlusKana > 0 && (double)japanese / cjkPlusKana >= 0.05)
                return "ja";

            // Hangul is exclusive to Korean
            if (korean > 0)
                return "ko";

            // CJK ideographs without meaningful kana → Chinese
            if (chinese > 0)
                return "zh";

            // Cyrillic → Russian
            if (cyrillic > 0)
                return "ru";
        }

        // Moderate non-Latin presence (lower threshold) — still needs proportion check
        if (nonLatin >= 10 && nonLatin >= totalChars * 0.005)
        {
            int cjkPlusKana = japanese + chinese;
            if (japanese > 0 && cjkPlusKana > 0 && (double)japanese / cjkPlusKana >= 0.1)
                return "ja";
            if (korean > 0) return "ko";
            if (chinese > 0) return "zh";
            if (cyrillic > 0) return "ru";
        }

        // Default to English
        if (latin >= totalChars * 0.3)
            return "en";

        return "en";
    }

    [GeneratedRegex(@"^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$", RegexOptions.IgnoreCase)]
    private static partial Regex GuidPattern();

    [GeneratedRegex(@"^[\d\s.,+\-*/=<>()]+$")]
    private static partial Regex NumericPattern();

    [GeneratedRegex(@"^(0x)?[0-9a-fA-F]{8,}$")]
    private static partial Regex HexPattern();

    [GeneratedRegex(@"^[a-z][a-zA-Z0-9]*[A-Z][a-zA-Z0-9]*$")]
    private static partial Regex CamelCasePattern();

    [GeneratedRegex(@"^[A-Za-z0-9+/=]{40,}$")]
    private static partial Regex Base64LikePattern();
}
