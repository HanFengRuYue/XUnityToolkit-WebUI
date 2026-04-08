using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Collections.Concurrent;
using AssetsTools.NET;
using AssetsTools.NET.Extra;
using XUnityToolkit_WebUI.Models;

namespace XUnityToolkit_WebUI.Services;

public sealed partial class AssetExtractionService(ILogger<AssetExtractionService> logger)
{
    private readonly record struct ExtractionCacheKey(
        string FilePath,
        long Length,
        long LastWriteTicksUtc,
        string UnityVersion,
        bool IsBundle);

    /// <summary>
    /// Common legacy encodings for games. Tried in order when UTF-8 validation fails.
    /// Scoring picks the best match based on decoded Unicode character quality.
    /// </summary>
    private static readonly Encoding[] LegacyEncodings;

    static AssetExtractionService()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        LegacyEncodings =
        [
            Encoding.GetEncoding(932),  // Shift-JIS (Japanese)
            Encoding.GetEncoding(936),  // GBK (Simplified Chinese)
            Encoding.GetEncoding(950),  // Big5 (Traditional Chinese)
            Encoding.GetEncoding(949),  // EUC-KR (Korean)
            Encoding.GetEncoding(51932), // EUC-JP (Japanese)
            Encoding.GetEncoding(1251), // Windows-1251 (Cyrillic/Russian)
            Encoding.GetEncoding(1252), // Windows-1252 (Western European)
        ];
    }

    private static readonly Lazy<byte[]> ClassDataTpk = new(() =>
    {
        using var stream = Assembly.GetExecutingAssembly()
            .GetManifestResourceStream("classdata.tpk")
            ?? throw new InvalidOperationException("Embedded classdata.tpk not found.");
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        return ms.ToArray();
    });

    private readonly ConcurrentDictionary<ExtractionCacheKey, IReadOnlyList<ExtractedText>> _fileExtractionCache = new();

    private static ExtractionCacheKey BuildCacheKey(string filePath, string unityVersion, bool isBundle)
    {
        var info = new FileInfo(filePath);
        return new ExtractionCacheKey(
            filePath,
            info.Length,
            info.LastWriteTimeUtc.Ticks,
            unityVersion,
            isBundle);
    }

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
                    var cacheKey = BuildCacheKey(assetFile, gameInfo.UnityVersion, isBundle: false);
                    progressData.CurrentFile = fileName;
                    progressData.ScannedFiles++;
                    progress?.Report(progressData);

                    if (_fileExtractionCache.TryGetValue(cacheKey, out var cachedTexts))
                    {
                        allTexts.AddRange(cachedTexts);
                        progressData.ExtractedTexts = allTexts.Count;
                        progress?.Report(progressData);
                        continue;
                    }

                    AssetsFileInstance? afileInst = null;
                    try
                    {
                        var texts = ExtractFromAssetFile(manager, assetFile, fileName, gameInfo.UnityVersion, out afileInst);
                        allTexts.AddRange(texts);
                        _fileExtractionCache[cacheKey] = texts;
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
                    var cacheKey = BuildCacheKey(bundleFile, gameInfo.UnityVersion, isBundle: true);
                    progressData.CurrentFile = fileName;
                    progressData.ScannedFiles++;
                    progress?.Report(progressData);

                    if (_fileExtractionCache.TryGetValue(cacheKey, out var cachedBundleTexts))
                    {
                        allTexts.AddRange(cachedBundleTexts);
                        progressData.ExtractedTexts = allTexts.Count;
                        progress?.Report(progressData);
                        continue;
                    }

                    BundleFileInstance? bunInst = null;
                    try
                    {
                        bunInst = manager.LoadBundleFile(bundleFile, true);
                        var dirInfos = bunInst.file.BlockAndDirInfo.DirectoryInfos;
                        var bundleTexts = new List<ExtractedText>();

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
                                bundleTexts.AddRange(texts);
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

                        allTexts.AddRange(bundleTexts);
                        _fileExtractionCache[cacheKey] = bundleTexts;
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

        // Detect template variables (e.g. {PC}, {M}, {D}) — log for user to configure DNT terms
        DetectAndLogTemplateVariables(uniqueTexts);

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

                // Use AsByteArray to get raw bytes (works for both String and ByteArray
                // value types in AssetsTools.NET v3), then detect encoding.
                // Unity stores TextAsset raw file bytes without encoding conversion,
                // so m_Script may be Shift-JIS or other non-UTF-8 encoding.
                var scriptBytes = texBase["m_Script"].AsByteArray;
                var script = scriptBytes.Length > 0
                    ? DecodeTextBytes(scriptBytes)
                    : string.Empty;

                if (!string.IsNullOrWhiteSpace(script))
                {
                    // Check for VIDE Dialogue JSON format first
                    var videTexts = TryExtractVideDialogue(script, name, fileName);
                    if (videTexts != null)
                    {
                        texts.AddRange(videTexts);
                        if (videTexts.Count > 0)
                            logger.LogDebug("检测到 VIDE 对话，提取了 {Count} 条文本: {Name}",
                                videTexts.Count, name);
                    }
                    else
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

                // Check for I2 Localization LanguageSourceAsset — extract terms specifically
                if (IsI2LanguageSource(mbBase))
                {
                    var i2Texts = ExtractI2LocalizationTerms(mbBase, fileName);
                    texts.AddRange(i2Texts);
                    logger.LogInformation("检测到 I2 Localization 资产，提取了 {Count} 条术语翻译", i2Texts.Count);
                    continue;
                }

                var name = mbBase["m_Name"].IsDummy ? "(unnamed)" : mbBase["m_Name"].AsString;

                // Check for GameCreator Dialogue — extract dialogue nodes specifically
                var gcDialogueType = DetectGameCreatorDialogueType(mbBase);
                if (gcDialogueType != null)
                {
                    var gcTexts = ExtractGameCreatorTexts(mbBase, gcDialogueType, name, fileName);
                    texts.AddRange(gcTexts);
                    if (gcTexts.Count > 0)
                        logger.LogDebug("检测到 GameCreator {Type}，提取了 {Count} 条文本: {Name}",
                            gcDialogueType, gcTexts.Count, name);
                    continue;
                }

                // Check for VIDE SOQuotes_UW — extract quote strings
                if (IsVideQuote(mbBase))
                {
                    var quoteTexts = ExtractVideQuotes(mbBase, name, fileName);
                    texts.AddRange(quoteTexts);
                    if (quoteTexts.Count > 0)
                        logger.LogDebug("检测到 VIDE 语录，提取了 {Count} 条文本: {Name}",
                            quoteTexts.Count, name);
                    continue;
                }

                // Check for VIDE SOTraitData_UW — extract trait/achievement text
                if (IsVideTrait(mbBase))
                {
                    var traitTexts = ExtractVideTraits(mbBase, name, fileName);
                    texts.AddRange(traitTexts);
                    if (traitTexts.Count > 0)
                        logger.LogDebug("检测到 VIDE 特征/成就，提取了 {Count} 条文本: {Name}",
                            traitTexts.Count, name);
                    continue;
                }

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
        if (depth > 20 || field.IsDummy) return;

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
    /// Detect whether a MonoBehaviour is an I2 Localization LanguageSourceAsset
    /// by checking for the characteristic mSource.mTerms field structure.
    /// </summary>
    private static bool IsI2LanguageSource(AssetTypeValueField mbBase)
    {
        var mSource = mbBase["mSource"];
        if (mSource.IsDummy) return false;

        var mTerms = mSource["mTerms"];
        if (mTerms.IsDummy) return false;

        var mLanguages = mSource["mLanguages"];
        return !mLanguages.IsDummy;
    }

    /// <summary>
    /// Extract translation terms from an I2 Localization LanguageSourceAsset.
    /// Parses the mSource.mTerms structure to extract per-language text values
    /// with proper categorization (e.g., "I2:en:TALK/MakinaText00").
    /// </summary>
    private List<ExtractedText> ExtractI2LocalizationTerms(
        AssetTypeValueField mbBase, string assetFileName)
    {
        var results = new List<ExtractedText>();
        var mSource = mbBase["mSource"];

        // Read language codes from mLanguages array
        var langCodes = new List<string>();
        foreach (var lang in GetArrayElements(mSource["mLanguages"]))
        {
            var code = lang["Code"];
            if (!code.IsDummy)
                langCodes.Add(code.AsString);
        }

        if (langCodes.Count == 0)
        {
            logger.LogWarning("I2 Localization 资产未包含语言信息，跳过提取");
            return results;
        }

        logger.LogInformation("I2 Localization 语言: {Languages}", string.Join(", ", langCodes));

        // Extract terms
        foreach (var term in GetArrayElements(mSource["mTerms"]))
        {
            var termKeyField = term["Term"];
            if (termKeyField.IsDummy) continue;
            var termKey = termKeyField.AsString;
            if (string.IsNullOrEmpty(termKey)) continue;

            var languages = term["Languages"];
            if (languages.IsDummy) continue;

            int langIdx = 0;
            foreach (var langValue in GetArrayElements(languages))
            {
                if (langIdx >= langCodes.Count) break;

                if (langValue.TemplateField.ValueType == AssetValueType.String)
                {
                    var text = langValue.AsString;
                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        results.Add(new ExtractedText
                        {
                            Text = text,
                            Source = $"I2:{langCodes[langIdx]}:{termKey}",
                            AssetFile = assetFileName
                        });
                    }
                }

                langIdx++;
            }
        }

        return results;
    }

    // ── GameCreator 2 Detection & Extraction ──────────────────────────────

    /// <summary>
    /// Detect whether a MonoBehaviour is a GameCreator 2 type.
    /// Returns the detected type name ("Dialogue", "Quest", "Actor", "Item", "Stat") or null.
    /// GameCreator stores dialogue trees, quest definitions, character data, and inventory items
    /// as ScriptableObjects with characteristic field structures.
    /// </summary>
    private static string? DetectGameCreatorDialogueType(AssetTypeValueField mbBase)
    {
        // Dialogue: has m_Story field containing dialogue node tree
        var mStory = mbBase["m_Story"];
        if (!mStory.IsDummy)
        {
            var mNodes = mStory["m_Nodes"];
            if (!mNodes.IsDummy)
                return "Dialogue";
        }

        // Quest: has m_Tasks array with quest task definitions
        var mTasks = mbBase["m_Tasks"];
        if (!mTasks.IsDummy)
        {
            // Distinguish from other types that might have m_Tasks — check for quest-specific fields
            var mTitle = mbBase["m_Title"];
            var mDescription = mbBase["m_Description"];
            if (!mTitle.IsDummy || !mDescription.IsDummy)
                return "Quest";
        }

        // Actor: has m_ActorName or m_PrimaryActorName (GameCreator Dialogue Actor)
        if (!mbBase["m_ActorName"].IsDummy || !mbBase["m_PrimaryActorName"].IsDummy)
            return "Actor";

        // Stat/Attribute/StatusEffect: has m_Acronym (unique to GC stats)
        if (!mbBase["m_Acronym"].IsDummy && !mbBase["m_Title"].IsDummy)
            return "Stat";

        return null;
    }

    /// <summary>
    /// Extract text from a GameCreator 2 MonoBehaviour with type-specific source tagging.
    /// Falls back to generic CollectStrings but with better source categorization.
    /// </summary>
    private List<ExtractedText> ExtractGameCreatorTexts(
        AssetTypeValueField mbBase, string gcType, string name, string assetFileName)
    {
        var results = new List<ExtractedText>();

        switch (gcType)
        {
            case "Dialogue":
                ExtractGameCreatorDialogue(mbBase, name, assetFileName, results);
                break;
            case "Quest":
                ExtractGameCreatorQuest(mbBase, name, assetFileName, results);
                break;
            case "Actor":
                ExtractGameCreatorActor(mbBase, name, assetFileName, results);
                break;
            default:
                // For Stat and other types, use generic collection with GC source tag
                var strings = new List<string>();
                CollectStrings(mbBase, strings, depth: 0);
                foreach (var str in strings)
                    results.Add(new ExtractedText
                    {
                        Text = str,
                        Source = $"GameCreator:{gcType}:{name}",
                        AssetFile = assetFileName
                    });
                break;
        }

        return results;
    }

    /// <summary>
    /// Extract dialogue text from GameCreator Dialogue ScriptableObject.
    /// Walks m_Story.m_Nodes to extract all dialogue content, choices, and instruction text.
    /// </summary>
    private void ExtractGameCreatorDialogue(
        AssetTypeValueField mbBase, string name, string assetFileName, List<ExtractedText> results)
    {
        var mStory = mbBase["m_Story"];
        if (mStory.IsDummy) return;

        // Extract from m_Nodes array — each node contains dialogue text
        foreach (var node in GetArrayElements(mStory["m_Nodes"]))
        {
            // Direct text content fields
            foreach (var fieldName in new[] { "m_Content", "m_ContentChoice", "m_Text", "m_Message", "m_Tag" })
            {
                var field = node[fieldName];
                if (!field.IsDummy && field.TemplateField.ValueType == AssetValueType.String)
                {
                    var text = field.AsString;
                    if (IsGameText(text))
                        results.Add(new ExtractedText
                        {
                            Text = text,
                            Source = $"GameCreator:Dialogue:{name}",
                            AssetFile = assetFileName
                        });
                }
            }

            // Array text fields (choices, random values)
            foreach (var arrayFieldName in new[] { "m_Values", "m_ValuesChoices", "m_ValuesRandom" })
            {
                var arrayField = node[arrayFieldName];
                if (arrayField.IsDummy) continue;
                foreach (var elem in GetArrayElements(arrayField))
                {
                    if (elem.TemplateField.ValueType == AssetValueType.String)
                    {
                        var text = elem.AsString;
                        if (IsGameText(text))
                            results.Add(new ExtractedText
                            {
                                Text = text,
                                Source = $"GameCreator:Dialogue:{name}",
                                AssetFile = assetFileName
                            });
                    }
                }
            }

            // Recursively collect from nested instructions (m_InstructionsOnStart, m_InstructionsOnEnd, etc.)
            foreach (var instrFieldName in new[] { "m_InstructionsOnStart", "m_InstructionsOnEnd", "m_Instructions", "m_Conditions" })
            {
                var instrField = node[instrFieldName];
                if (!instrField.IsDummy)
                {
                    var instrStrings = new List<string>();
                    CollectStrings(instrField, instrStrings, depth: 0);
                    foreach (var str in instrStrings)
                        results.Add(new ExtractedText
                        {
                            Text = str,
                            Source = $"GameCreator:Dialogue:{name}",
                            AssetFile = assetFileName
                        });
                }
            }
        }

        // Also collect any top-level strings we might have missed
        var topStrings = new List<string>();
        CollectGameCreatorRemainingStrings(mbBase, topStrings, results);
        foreach (var str in topStrings)
            results.Add(new ExtractedText
            {
                Text = str,
                Source = $"GameCreator:Dialogue:{name}",
                AssetFile = assetFileName
            });
    }

    /// <summary>
    /// Extract quest text from GameCreator Quest ScriptableObject.
    /// Extracts title, description, and recursively walks task trees.
    /// </summary>
    private void ExtractGameCreatorQuest(
        AssetTypeValueField mbBase, string name, string assetFileName, List<ExtractedText> results)
    {
        // Extract top-level quest fields
        foreach (var fieldName in new[] { "m_Title", "m_Description", "m_Completion" })
        {
            var field = mbBase[fieldName];
            if (!field.IsDummy && field.TemplateField.ValueType == AssetValueType.String)
            {
                var text = field.AsString;
                if (IsGameText(text))
                    results.Add(new ExtractedText
                    {
                        Text = text,
                        Source = $"GameCreator:Quest:{name}",
                        AssetFile = assetFileName
                    });
            }
        }

        // Recursively extract from tasks
        ExtractGameCreatorTasks(mbBase["m_Tasks"], name, assetFileName, results, depth: 0);

        // Collect any remaining strings (instructions, conditions, etc.)
        var remaining = new List<string>();
        CollectGameCreatorRemainingStrings(mbBase, remaining, results);
        foreach (var str in remaining)
            results.Add(new ExtractedText
            {
                Text = str,
                Source = $"GameCreator:Quest:{name}",
                AssetFile = assetFileName
            });
    }

    /// <summary>
    /// Recursively extract task text from GameCreator quest task trees.
    /// </summary>
    private void ExtractGameCreatorTasks(
        AssetTypeValueField tasksField, string name, string assetFileName,
        List<ExtractedText> results, int depth)
    {
        if (tasksField.IsDummy || depth > 5) return;

        foreach (var task in GetArrayElements(tasksField))
        {
            foreach (var fieldName in new[] { "m_Title", "m_Description", "m_Completion", "m_Text" })
            {
                var field = task[fieldName];
                if (!field.IsDummy && field.TemplateField.ValueType == AssetValueType.String)
                {
                    var text = field.AsString;
                    if (IsGameText(text))
                        results.Add(new ExtractedText
                        {
                            Text = text,
                            Source = $"GameCreator:Quest:{name}",
                            AssetFile = assetFileName
                        });
                }
            }

            // Sub-tasks (recursive)
            ExtractGameCreatorTasks(task["m_Tasks"], name, assetFileName, results, depth + 1);

            // Instructions within tasks
            foreach (var instrFieldName in new[] { "m_InstructionsOnStart", "m_InstructionsOnEnd", "m_InstructionsOnComplete", "m_Instructions" })
            {
                var instrField = task[instrFieldName];
                if (!instrField.IsDummy)
                {
                    var instrStrings = new List<string>();
                    CollectStrings(instrField, instrStrings, depth: 0);
                    foreach (var str in instrStrings)
                        results.Add(new ExtractedText
                        {
                            Text = str,
                            Source = $"GameCreator:Quest:{name}",
                            AssetFile = assetFileName
                        });
                }
            }
        }
    }

    /// <summary>
    /// Extract character data from GameCreator Actor ScriptableObject.
    /// </summary>
    private static void ExtractGameCreatorActor(
        AssetTypeValueField mbBase, string name, string assetFileName, List<ExtractedText> results)
    {
        foreach (var fieldName in new[]
        {
            "m_ActorName", "m_ActorDescription",
            "m_PrimaryActorName", "m_PrimaryActorDescription",
            "m_AlternateActorName", "m_AlternateActorDescription"
        })
        {
            var field = mbBase[fieldName];
            if (!field.IsDummy && field.TemplateField.ValueType == AssetValueType.String)
            {
                var text = field.AsString;
                if (IsGameText(text))
                    results.Add(new ExtractedText
                    {
                        Text = text,
                        Source = $"GameCreator:Actor:{name}",
                        AssetFile = assetFileName
                    });
            }
        }
    }

    /// <summary>
    /// Collect remaining strings from a GameCreator MonoBehaviour that weren't caught
    /// by type-specific extraction. Skips strings already found in results.
    /// </summary>
    private static void CollectGameCreatorRemainingStrings(
        AssetTypeValueField mbBase, List<string> remaining, List<ExtractedText> alreadyFound)
    {
        var existingTexts = new HashSet<string>(alreadyFound.Select(t => t.Text));
        var allStrings = new List<string>();
        CollectStrings(mbBase, allStrings, depth: 0);
        foreach (var str in allStrings)
        {
            if (!existingTexts.Contains(str))
                remaining.Add(str);
        }
    }

    // ── End GameCreator 2 ───────────────────────────────────────────────

    // ── VIDE Dialogues & Unaware in The City Detection & Extraction ──────

    /// <summary>
    /// Try to detect and extract VIDE dialogue text from a TextAsset JSON.
    /// VIDE dialogues use flat JSON with keys like pd_{N}_com_{C}text for dialogue text
    /// and pd_{N}_com_{C}charName for character names.
    /// Returns null if the content is not a VIDE dialogue JSON.
    /// </summary>
    private List<ExtractedText>? TryExtractVideDialogue(string script, string name, string assetFileName)
    {
        // Quick check: must look like JSON with VIDE metadata keys
        if (script.Length < 10 || script[0] != '{' || !script.Contains("\"dID\""))
            return null;

        try
        {
            using var doc = JsonDocument.Parse(script);
            var root = doc.RootElement;

            // Verify VIDE structure: must have dID and playerDiags
            if (!root.TryGetProperty("dID", out _) || !root.TryGetProperty("playerDiags", out _))
                return null;

            var results = new List<ExtractedText>();
            var charNames = new HashSet<string>();

            foreach (var prop in root.EnumerateObject())
            {
                if (prop.Value.ValueKind != JsonValueKind.String)
                    continue;

                // Extract dialogue text: pd_{N}_com_{C}text
                if (VideTextKeyPattern().IsMatch(prop.Name))
                {
                    var text = prop.Value.GetString();
                    if (text != null && IsVideGameText(text))
                    {
                        results.Add(new ExtractedText
                        {
                            Text = text,
                            Source = $"VIDE:{name}",
                            AssetFile = assetFileName
                        });
                    }
                }
                // Collect unique character names: pd_{N}_com_{C}charName
                else if (VideCharNameKeyPattern().IsMatch(prop.Name))
                {
                    var charName = prop.Value.GetString();
                    if (!string.IsNullOrWhiteSpace(charName))
                        charNames.Add(charName);
                }
            }

            // Add unique character names as separate entries
            foreach (var charName in charNames)
            {
                if (IsGameText(charName))
                {
                    results.Add(new ExtractedText
                    {
                        Text = charName,
                        Source = $"VIDE:Character:{name}",
                        AssetFile = assetFileName
                    });
                }
            }

            return results;
        }
        catch (JsonException)
        {
            return null; // Not valid JSON, fall back to generic extraction
        }
    }

    /// <summary>
    /// Filter VIDE dialogue text — skip control markers and non-displayable entries.
    /// </summary>
    private static bool IsVideGameText(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        if (text == "*" || text.StartsWith("* ")) return false; // Branch/control markers
        if (text is "RAND" or "ExtraData") return false; // Internal VIDE markers
        return IsGameText(text);
    }

    /// <summary>
    /// Detect SOQuotes_UW: has quotes array + dialType field (unique to VIDE quote system).
    /// </summary>
    private static bool IsVideQuote(AssetTypeValueField mbBase)
    {
        return !mbBase["quotes"].IsDummy && !mbBase["dialType"].IsDummy;
    }

    /// <summary>
    /// Extract translatable quote strings from SOQuotes_UW MonoBehaviour.
    /// </summary>
    private static List<ExtractedText> ExtractVideQuotes(
        AssetTypeValueField mbBase, string name, string assetFileName)
    {
        var results = new List<ExtractedText>();

        var quotes = mbBase["quotes"];
        if (!quotes.IsDummy)
        {
            foreach (var elem in GetArrayElements(quotes))
            {
                if (elem.TemplateField.ValueType == AssetValueType.String)
                {
                    var text = elem.AsString;
                    if (IsGameText(text))
                        results.Add(new ExtractedText
                        {
                            Text = text,
                            Source = $"Quote:{name}",
                            AssetFile = assetFileName
                        });
                }
            }
        }

        return results;
    }

    /// <summary>
    /// Detect SOTraitData_UW: has traitName + traitType fields (unique to trait/achievement system).
    /// </summary>
    private static bool IsVideTrait(AssetTypeValueField mbBase)
    {
        return !mbBase["traitName"].IsDummy && !mbBase["traitType"].IsDummy;
    }

    /// <summary>
    /// Extract translatable text from SOTraitData_UW MonoBehaviour
    /// (traits, achievements, and background stories).
    /// Extracts traitName, reqsText, effectText, flavorText directly.
    /// Extracts hoverText only if it contains substantial non-template prose
    /// (e.g., background story descriptions beyond {NAME}/{REQS}/{EFFECT}/{FLAVOR} placeholders).
    /// </summary>
    private static List<ExtractedText> ExtractVideTraits(
        AssetTypeValueField mbBase, string name, string assetFileName)
    {
        var results = new List<ExtractedText>();

        // Extract direct text fields
        foreach (var fieldName in new[] { "traitName", "reqsText", "effectText", "flavorText" })
        {
            var field = mbBase[fieldName];
            if (!field.IsDummy && field.TemplateField.ValueType == AssetValueType.String)
            {
                var text = field.AsString;
                if (IsGameText(text))
                    results.Add(new ExtractedText
                    {
                        Text = text,
                        Source = $"Trait:{name}",
                        AssetFile = assetFileName
                    });
            }
        }

        // Extract hoverText only if it contains substantial non-template content
        // (background stories embed unique prose text mixed with template placeholders)
        var hoverText = mbBase["hoverText"];
        if (!hoverText.IsDummy && hoverText.TemplateField.ValueType == AssetValueType.String)
        {
            var text = hoverText.AsString;
            if (!string.IsNullOrWhiteSpace(text))
            {
                // Strip known template placeholders and whitespace, check if real text remains
                var stripped = TraitTemplatePlaceholderPattern().Replace(text, "")
                    .Replace("\n", "").Replace("\r", "").Trim();
                if (stripped.Length > 5 && IsGameText(text))
                    results.Add(new ExtractedText
                    {
                        Text = text,
                        Source = $"Trait:HoverText:{name}",
                        AssetFile = assetFileName
                    });
            }
        }

        return results;
    }

    // ── End VIDE / Unaware in The City ───────────────────────────────────

    /// <summary>
    /// Get array elements from an AssetsTools.NET array field.
    /// Arrays have structure: field -> "Array" child -> actual elements.
    /// </summary>
    private static IReadOnlyList<AssetTypeValueField> GetArrayElements(AssetTypeValueField arrayField)
    {
        if (arrayField.IsDummy) return [];
        var children = arrayField.Children;
        if (children.Count == 1 && children[0].FieldName == "Array")
            return children[0].Children;
        return children;
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

        // Skip file paths — require path-like structure, not just presence of / and .
        // Natural text often contains both (e.g., "Yes/No.", "<b>bold</b> text.", "2.1% bonus")
        if (text.Contains(":\\") || text.StartsWith("\\\\")) return false; // Windows/UNC paths
        if (text.Contains('\\') && !text.Contains(' ')) return false; // Backslash paths without spaces
        if (text.Contains('/') && text.Contains('.') && !text.Contains(' ')) return false; // Unix-like paths

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
        if (texts.Count == 0) return "en"; // Default

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

    /// <summary>
    /// Decode raw bytes from a TextAsset with automatic encoding detection.
    /// Unity stores TextAsset file bytes as-is without encoding conversion,
    /// so the data may be Shift-JIS, GBK, Big5, EUC-KR, or other legacy encodings.
    /// </summary>
    private static string DecodeTextBytes(byte[] bytes)
    {
        // Check BOM
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
            return Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
            return Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2);
        if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
            return Encoding.BigEndianUnicode.GetString(bytes, 2, bytes.Length - 2);

        // Try strict UTF-8 decoding (rejects invalid byte sequences)
        try
        {
            var utf8Strict = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
            return utf8Strict.GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            // Not valid UTF-8, try legacy encodings
        }

        // Try all legacy encodings and pick the one that produces the highest-quality text.
        // Each encoding decodes the same bytes differently; scoring based on recognized
        // Unicode character ranges identifies the correct encoding.
        string? bestResult = null;
        int bestScore = int.MinValue;

        foreach (var encoding in LegacyEncodings)
        {
            var decoded = encoding.GetString(bytes);
            var score = ScoreDecodedText(decoded);
            if (score > bestScore)
            {
                bestScore = score;
                bestResult = decoded;
            }
        }

        return bestResult ?? Encoding.UTF8.GetString(bytes);
    }

    /// <summary>
    /// Score decoded text by counting recognized Unicode characters.
    /// Script-specific characters (kana, hangul, cyrillic) score higher than shared CJK
    /// because they uniquely identify the correct encoding.
    /// </summary>
    private static int ScoreDecodedText(string text)
    {
        int score = 0;
        foreach (var ch in text)
        {
            if (ch == '\uFFFD') { score -= 10; continue; } // Replacement character
            if (char.IsControl(ch) && ch is not '\n' and not '\r' and not '\t')
            {
                score -= 5;
                continue;
            }

            // Script-specific characters — strong encoding indicators
            if (ch is >= '\u3040' and <= '\u309F') score += 3;      // Hiragana (Japanese)
            else if (ch is >= '\u30A0' and <= '\u30FF') score += 3;  // Katakana (Japanese)
            else if (ch is >= '\uAC00' and <= '\uD7AF') score += 3;  // Hangul syllables (Korean)
            else if (ch is >= '\u1100' and <= '\u11FF') score += 3;  // Hangul Jamo (Korean)
            else if (ch is >= '\u0400' and <= '\u04FF') score += 3;  // Cyrillic (Russian etc.)
            else if (ch is >= '\u00C0' and <= '\u024F') score += 2;  // Latin Extended (accented)

            // CJK ideographs — shared by Chinese/Japanese/Korean
            else if (ch is >= '\u4E00' and <= '\u9FFF') score += 2;  // CJK Unified
            else if (ch is >= '\u3400' and <= '\u4DBF') score += 2;  // CJK Extension A

            // CJK punctuation and fullwidth forms
            else if (ch is >= '\u3000' and <= '\u303F') score += 1;  // CJK Symbols
            else if (ch is >= '\uFF00' and <= '\uFFEF') score += 1;  // Fullwidth Forms
        }

        return score;
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

    [GeneratedRegex(@"\{[A-Za-z_]\w{0,15}\}")]
    private static partial Regex TemplateVariablePattern();

    [GeneratedRegex(@"^pd_\d+_com_\d+text$")]
    private static partial Regex VideTextKeyPattern();

    [GeneratedRegex(@"^pd_\d+_com_\d+charName$")]
    private static partial Regex VideCharNameKeyPattern();

    [GeneratedRegex(@"\{(NAME|REQS|EFFECT|FLAVOR|STATS|REPS|TP|ITEMS|CLOTHES)\}")]
    private static partial Regex TraitTemplatePlaceholderPattern();

    /// <summary>
    /// Detect template variable patterns (e.g., {PC}, {M}, {D}) in extracted texts
    /// and log them so the user knows to configure DoNotTranslate terms.
    /// </summary>
    private void DetectAndLogTemplateVariables(List<ExtractedText> texts)
    {
        var variables = new HashSet<string>();

        foreach (var text in texts)
        {
            foreach (Match match in TemplateVariablePattern().Matches(text.Text))
                variables.Add(match.Value);
        }

        if (variables.Count > 0)
        {
            logger.LogInformation(
                "检测到 {Count} 个模板变量: {Variables}。建议为这些变量添加「不翻译」术语以在翻译中保留原文",
                variables.Count, string.Join(", ", variables.Order()));
        }
    }
}
