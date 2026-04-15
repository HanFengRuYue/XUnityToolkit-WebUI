using System.Reflection;
using System.Text;
using AssetsTools.NET;
using AssetsTools.NET.Extra;
using AssetsTools.NET.Texture;
using dnlib.DotNet;
using dnlib.DotNet.Emit;
using UnityLocalizationToolkit_WebUI.Models;

namespace UnityLocalizationToolkit_WebUI.Services;

public sealed class UnityAssetCatalogService(ILogger<UnityAssetCatalogService> logger)
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

    static UnityAssetCatalogService()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    public Task<ManualTranslationProjectIndex> ScanAsync(Game game, CancellationToken ct = default)
    {
        if (!game.IsUnityGame || game.DetectedInfo is null)
            throw new InvalidOperationException("Only detected Unity games can be scanned.");

        return Task.Run(() =>
        {
            var assets = new List<ManualTranslationAssetEntry>();
            var manager = new AssetsManager();

            try
            {
                using var tpkStream = new MemoryStream(ClassDataTpk.Value);
                manager.LoadClassPackage(tpkStream);
                SetupTemplateGenerator(manager, game);
                manager.UseTemplateFieldCache = true;
                manager.UseMonoTemplateFieldCache = true;

                var dataPath = GetGameDataPath(game);
                foreach (var assetFile in DiscoverLooseAssetFiles(dataPath))
                {
                    ct.ThrowIfCancellationRequested();
                    ScanLooseAssetFile(manager, game, assetFile, assets);
                }

                foreach (var bundleFile in DiscoverBundleFiles(dataPath))
                {
                    ct.ThrowIfCancellationRequested();
                    ScanBundleFile(manager, game, bundleFile, assets);
                }

                ScanManagedAssemblies(game, assets, ct);
                ScanIl2CppFiles(game, assets, ct);
            }
            finally
            {
                manager.UnloadAll();
                if (manager.MonoTempGenerator is IDisposable disposable)
                    disposable.Dispose();
            }

            return new ManualTranslationProjectIndex
            {
                GameId = game.Id,
                Assets = assets
                    .OrderBy(a => a.StorageKind)
                    .ThenBy(a => a.AssetFile, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(a => a.DisplayName, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(a => a.AssetId, StringComparer.Ordinal)
                    .ToList()
            };
        }, ct);
    }

    private void ScanLooseAssetFile(AssetsManager manager, Game game, string assetFilePath, List<ManualTranslationAssetEntry> assets)
    {
        AssetsFileInstance? afileInst = null;
        try
        {
            afileInst = manager.LoadAssetsFile(assetFilePath, false);
            ScanAssetsInstance(
                manager,
                afileInst,
                game,
                Path.GetRelativePath(game.GamePath, assetFilePath),
                bundleEntryName: null,
                ManualTranslationAssetStorageKind.LooseAssetFile,
                assets);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to scan loose asset file {File}", assetFilePath);
        }
        finally
        {
            if (afileInst is not null)
                manager.UnloadAssetsFile(afileInst);
        }
    }

    private void ScanBundleFile(AssetsManager manager, Game game, string bundleFilePath, List<ManualTranslationAssetEntry> assets)
    {
        BundleFileInstance? bunInst = null;
        try
        {
            bunInst = manager.LoadBundleFile(bundleFilePath, true);
            var relativeBundlePath = Path.GetRelativePath(game.GamePath, bundleFilePath);
            var dirInfos = bunInst.file.BlockAndDirInfo.DirectoryInfos;
            for (var i = 0; i < dirInfos.Count; i++)
            {
                var entryName = dirInfos[i].Name;
                if (entryName.EndsWith(".resource", StringComparison.OrdinalIgnoreCase) ||
                    entryName.EndsWith(".resS", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                AssetsFileInstance? afileInst = null;
                try
                {
                    afileInst = manager.LoadAssetsFileFromBundle(bunInst, i, false);
                    ScanAssetsInstance(
                        manager,
                        afileInst,
                        game,
                        relativeBundlePath,
                        entryName,
                        ManualTranslationAssetStorageKind.BundleEntry,
                        assets);
                }
                catch (Exception ex)
                {
                    logger.LogDebug(ex, "Failed to scan bundle entry {Bundle}/{Entry}", relativeBundlePath, entryName);
                }
                finally
                {
                    if (afileInst is not null)
                        manager.UnloadAssetsFile(afileInst);
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to scan bundle {Bundle}", bundleFilePath);
        }
        finally
        {
            if (bunInst is not null)
                manager.UnloadBundleFile(bunInst);
        }
    }

    private void ScanAssetsInstance(
        AssetsManager manager,
        AssetsFileInstance afileInst,
        Game game,
        string relativeAssetFile,
        string? bundleEntryName,
        ManualTranslationAssetStorageKind storageKind,
        List<ManualTranslationAssetEntry> assets)
    {
        var afile = afileInst.file;
        TryLoadClassDatabase(manager, afile.Metadata.UnityVersion, game.DetectedInfo!.UnityVersion, afile.Metadata.TypeTreeEnabled);

        foreach (var textInfo in afile.GetAssetsOfType(AssetClassID.TextAsset))
        {
            try
            {
                var textBase = manager.GetBaseField(afileInst, textInfo);
                if (textBase.IsDummy)
                    continue;

                var name = SafeString(textBase["m_Name"].AsString, "(unnamed TextAsset)");
                var scriptField = textBase["m_Script"];
                if (scriptField.IsDummy)
                    continue;

                var content = DecodeText(scriptField.AsByteArray);
                assets.Add(CreateAssetEntry(
                    storageKind,
                    ManualTranslationAssetValueKind.Text,
                    "TextAsset",
                    relativeAssetFile,
                    bundleEntryName,
                    textInfo.PathId,
                    name,
                    "m_Script",
                    relativePath: null,
                    editable: true,
                    originalText: content));
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "Failed to inspect TextAsset in {File}", relativeAssetFile);
            }
        }

        foreach (var fontInfo in afile.GetAssetsOfType(AssetClassID.Font))
        {
            try
            {
                var fontBase = manager.GetBaseField(afileInst, fontInfo);
                if (fontBase.IsDummy)
                    continue;

                var name = SafeString(fontBase["m_Name"].AsString, "(unnamed Font)");
                assets.Add(CreateAssetEntry(
                    storageKind,
                    ManualTranslationAssetValueKind.Font,
                    "Font",
                    relativeAssetFile,
                    bundleEntryName,
                    fontInfo.PathId,
                    name,
                    fieldPath: null,
                    relativePath: null,
                    editable: false,
                    originalText: null,
                    editHint: "Use the dedicated font replacement panel to swap or restore this font."));
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "Failed to inspect Font in {File}", relativeAssetFile);
            }
        }

        foreach (var textureInfo in afile.GetAssetsOfType(AssetClassID.Texture2D))
        {
            try
            {
                var texBase = manager.GetBaseField(afileInst, textureInfo);
                if (texBase.IsDummy)
                    continue;

                var name = SafeString(texBase["m_Name"].AsString, "(unnamed Texture2D)");
                var texture = TextureFile.ReadTextureFile(texBase);
                int? width = texture.m_Width > 0
                    ? texture.m_Width
                    : (texBase["m_Width"].IsDummy ? null : texBase["m_Width"].AsInt);
                int? height = texture.m_Height > 0
                    ? texture.m_Height
                    : (texBase["m_Height"].IsDummy ? null : texBase["m_Height"].AsInt);
                assets.Add(CreateAssetEntry(
                    storageKind,
                    ManualTranslationAssetValueKind.Image,
                    "Texture2D",
                    relativeAssetFile,
                    bundleEntryName,
                    textureInfo.PathId,
                    name,
                    fieldPath: null,
                    relativePath: null,
                    editable: false,
                    originalText: null,
                    editHint: "Preview this texture and upload a replacement image in the manual translation workspace.",
                    width: width,
                    height: height));
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "Failed to inspect Texture2D in {File}", relativeAssetFile);
            }
        }

        foreach (var mbInfo in afile.GetAssetsOfType(AssetClassID.MonoBehaviour))
        {
            try
            {
                var mbBase = manager.GetBaseField(afileInst, mbInfo);
                if (mbBase.IsDummy)
                    continue;

                var objectName = SafeString(mbBase["m_Name"].AsString, "(unnamed MonoBehaviour)");
                CollectMonoBehaviourStringEntries(
                    mbBase,
                    assets,
                    storageKind,
                    relativeAssetFile,
                    bundleEntryName,
                    mbInfo.PathId,
                    objectName,
                    currentPath: string.Empty,
                    depth: 0);
            }
            catch
            {
                // Skip MonoBehaviour entries without templates.
            }
        }
    }

    private void CollectMonoBehaviourStringEntries(
        AssetTypeValueField field,
        List<ManualTranslationAssetEntry> assets,
        ManualTranslationAssetStorageKind storageKind,
        string relativeAssetFile,
        string? bundleEntryName,
        long pathId,
        string objectName,
        string currentPath,
        int depth)
    {
        if (depth > 20 || field.IsDummy)
            return;

        if (field.TemplateField.ValueType == AssetValueType.String)
        {
            var value = field.AsString;
            if (!string.IsNullOrWhiteSpace(value))
            {
                assets.Add(CreateAssetEntry(
                    storageKind,
                    ManualTranslationAssetValueKind.Text,
                    "MonoBehaviour",
                    relativeAssetFile,
                    bundleEntryName,
                    pathId,
                    objectName,
                    currentPath,
                    relativePath: null,
                    editable: true,
                    originalText: value));
            }

            return;
        }

        if (string.Equals(field.FieldName, "Array", StringComparison.Ordinal) && field.Children.Count > 0)
        {
            for (var i = 0; i < field.Children.Count; i++)
            {
                CollectMonoBehaviourStringEntries(
                    field.Children[i],
                    assets,
                    storageKind,
                    relativeAssetFile,
                    bundleEntryName,
                    pathId,
                    objectName,
                    $"{currentPath}[{i}]",
                    depth + 1);
            }

            return;
        }

        foreach (var child in field.Children)
        {
            var nextPath = string.IsNullOrEmpty(currentPath)
                ? child.FieldName
                : $"{currentPath}/{child.FieldName}";
            CollectMonoBehaviourStringEntries(
                child,
                assets,
                storageKind,
                relativeAssetFile,
                bundleEntryName,
                pathId,
                objectName,
                nextPath,
                depth + 1);
        }
    }

    private void ScanManagedAssemblies(Game game, List<ManualTranslationAssetEntry> assets, CancellationToken ct)
    {
        if (game.DetectedInfo?.Backend != UnityBackend.Mono)
            return;

        var managedDir = Path.Combine(GetGameDataPath(game), "Managed");
        if (!Directory.Exists(managedDir))
            return;

        foreach (var assemblyPath in Directory.GetFiles(managedDir, "*.dll", SearchOption.TopDirectoryOnly))
        {
            ct.ThrowIfCancellationRequested();

            try
            {
                using var module = ModuleDefMD.Load(assemblyPath);
                var relativePath = Path.GetRelativePath(game.GamePath, assemblyPath);
                foreach (var type in module.GetTypes())
                {
                    foreach (var method in type.Methods)
                    {
                        if (!method.HasBody)
                            continue;

                        var instructions = method.Body.Instructions;
                        for (var i = 0; i < instructions.Count; i++)
                        {
                            var instruction = instructions[i];
                            if (instruction.OpCode != OpCodes.Ldstr || instruction.Operand is not string value)
                                continue;
                            if (!LooksLikeGameText(value))
                                continue;

                            assets.Add(CreateAssetEntry(
                                ManualTranslationAssetStorageKind.ManagedAssembly,
                                ManualTranslationAssetValueKind.Code,
                                "ManagedStringLiteral",
                                relativePath,
                                bundleEntryName: null,
                                pathId: null,
                                displayName: Path.GetFileName(assemblyPath),
                                fieldPath: $"{type.FullName}::{method.Name}",
                                relativePath: relativePath,
                                editable: true,
                                originalText: value,
                                codeLocation: $"{method.MDToken.Raw}:{i}",
                                editHint: "Managed string patches update ldstr literals in-place."));
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to inspect managed assembly {Assembly}", assemblyPath);
            }
        }
    }

    private void ScanIl2CppFiles(Game game, List<ManualTranslationAssetEntry> assets, CancellationToken ct)
    {
        if (game.DetectedInfo?.Backend != UnityBackend.IL2CPP)
            return;

        var dataPath = GetGameDataPath(game);
        var metadataPath = Path.Combine(dataPath, "il2cpp_data", "Metadata", "global-metadata.dat");
        if (File.Exists(metadataPath))
        {
            foreach (var entry in ScanBinaryStrings(
                         game,
                         metadataPath,
                         ManualTranslationAssetStorageKind.Il2CppMetadata,
                         "Il2CppMetadataString",
                         maxCount: 2500,
                         includeUtf16: false,
                         ct))
            {
                assets.Add(entry);
            }
        }

        var nativePath = Path.Combine(game.GamePath, "GameAssembly.dll");
        if (File.Exists(nativePath))
        {
            foreach (var entry in ScanBinaryStrings(
                         game,
                         nativePath,
                         ManualTranslationAssetStorageKind.NativeBinary,
                         "NativeString",
                         maxCount: 2500,
                         includeUtf16: true,
                         ct))
            {
                assets.Add(entry);
            }
        }
    }

    private IEnumerable<ManualTranslationAssetEntry> ScanBinaryStrings(
        Game game,
        string filePath,
        ManualTranslationAssetStorageKind storageKind,
        string objectType,
        int maxCount,
        bool includeUtf16,
        CancellationToken ct)
    {
        var bytes = File.ReadAllBytes(filePath);
        var relativePath = Path.GetRelativePath(game.GamePath, filePath);
        var count = 0;

        foreach (var candidate in EnumerateAsciiCandidates(bytes))
        {
            ct.ThrowIfCancellationRequested();
            if (!LooksLikeGameText(candidate.Text))
                continue;

            yield return CreateAssetEntry(
                storageKind,
                ManualTranslationAssetValueKind.Code,
                objectType,
                relativePath,
                bundleEntryName: null,
                pathId: null,
                displayName: Path.GetFileName(filePath),
                fieldPath: $"0x{candidate.Offset:X}",
                relativePath: relativePath,
                editable: true,
                originalText: candidate.Text,
                codeLocation: $"{candidate.Offset}:{candidate.Length}:ascii",
                editHint: "Native patches currently use safe in-place replacement and cannot exceed the original byte length.");

            count++;
            if (count >= maxCount)
                yield break;
        }

        if (!includeUtf16)
            yield break;

        foreach (var candidate in EnumerateUtf16Candidates(bytes))
        {
            ct.ThrowIfCancellationRequested();
            if (!LooksLikeGameText(candidate.Text))
                continue;

            yield return CreateAssetEntry(
                storageKind,
                ManualTranslationAssetValueKind.Code,
                objectType,
                relativePath,
                bundleEntryName: null,
                pathId: null,
                displayName: Path.GetFileName(filePath),
                fieldPath: $"0x{candidate.Offset:X}",
                relativePath: relativePath,
                editable: true,
                originalText: candidate.Text,
                codeLocation: $"{candidate.Offset}:{candidate.Length}:utf16",
                editHint: "Native patches currently use safe in-place replacement and cannot exceed the original byte length.");

            count++;
            if (count >= maxCount)
                yield break;
        }
    }

    private static IEnumerable<(int Offset, int Length, string Text)> EnumerateAsciiCandidates(byte[] bytes)
    {
        var start = -1;
        for (var i = 0; i < bytes.Length; i++)
        {
            var value = bytes[i];
            var printable = value is >= 0x20 and <= 0x7E || value >= 0x80;
            if (printable)
            {
                if (start < 0)
                    start = i;
                continue;
            }

            if (value == 0 && start >= 0)
            {
                var length = i - start;
                if (length >= 2 && length <= 256)
                {
                    var text = DecodeText(bytes[start..i]);
                    yield return (start, length, text);
                }
            }

            start = -1;
        }
    }

    private static IEnumerable<(int Offset, int Length, string Text)> EnumerateUtf16Candidates(byte[] bytes)
    {
        for (var i = 0; i < bytes.Length - 4; i += 2)
        {
            var length = 0;
            while (i + length + 1 < bytes.Length)
            {
                if (bytes[i + length] == 0 && bytes[i + length + 1] == 0)
                    break;

                if (bytes[i + length + 1] != 0)
                {
                    length = 0;
                    break;
                }

                length += 2;
            }

            if (length >= 4 && length <= 512)
            {
                var text = Encoding.Unicode.GetString(bytes, i, length);
                yield return (i, length, text);
                i += length;
            }
        }
    }

    private static IEnumerable<string> DiscoverLooseAssetFiles(string dataPath)
    {
        return Directory.GetFiles(dataPath, "*.assets", SearchOption.TopDirectoryOnly)
            .Concat(Directory.GetFiles(dataPath, "level*", SearchOption.TopDirectoryOnly)
                .Where(path => !Path.GetExtension(path).Equals(".resS", StringComparison.OrdinalIgnoreCase)))
            .Distinct(StringComparer.OrdinalIgnoreCase);
    }

    private static IEnumerable<string> DiscoverBundleFiles(string dataPath)
    {
        var result = new List<string>();
        var dataUnity3d = Path.Combine(dataPath, "data.unity3d");
        if (File.Exists(dataUnity3d))
            result.Add(dataUnity3d);

        var streamingAssetsPath = Path.Combine(dataPath, "StreamingAssets");
        if (Directory.Exists(streamingAssetsPath))
        {
            result.AddRange(Directory.GetFiles(streamingAssetsPath, "*.bundle", SearchOption.AllDirectories));
        }

        result.AddRange(Directory.GetFiles(dataPath, "*.bundle", SearchOption.TopDirectoryOnly)
            .Where(path => !result.Contains(path, StringComparer.OrdinalIgnoreCase)));

        return result;
    }

    private static void SetupTemplateGenerator(AssetsManager manager, Game game)
    {
        if (game.DetectedInfo is null)
            return;

        var dataPath = GetGameDataPath(game);

        if (game.DetectedInfo.Backend == UnityBackend.IL2CPP)
        {
            var metaPath = Path.Combine(dataPath, "il2cpp_data", "Metadata", "global-metadata.dat");
            var asmPath = Path.Combine(game.GamePath, "GameAssembly.dll");
            if (File.Exists(metaPath) && File.Exists(asmPath))
            {
                try
                {
                    manager.MonoTempGenerator = new AssetsTools.NET.Cpp2IL.Cpp2IlTempGenerator(metaPath, asmPath);
                }
                catch
                {
                    // Skip custom MonoBehaviour templates when Cpp2IL is unavailable.
                }
            }

            return;
        }

        var managedPath = Path.Combine(dataPath, "Managed");
        if (!Directory.Exists(managedPath))
            return;

        try
        {
            manager.MonoTempGenerator = new MonoCecilTempGenerator(managedPath);
        }
        catch
        {
            // Skip custom MonoBehaviour templates when Mono.Cecil metadata fails to load.
        }
    }

    private static string GetGameDataPath(Game game)
    {
        var executable = game.ExecutableName ?? game.DetectedInfo?.DetectedExecutable
            ?? throw new InvalidOperationException("Executable name is required.");
        var gameName = Path.GetFileNameWithoutExtension(executable);
        var dataPath = Path.Combine(game.GamePath, $"{gameName}_Data");
        if (!Directory.Exists(dataPath))
            throw new DirectoryNotFoundException($"Game data directory not found: {dataPath}");
        return dataPath;
    }

    private static void TryLoadClassDatabase(AssetsManager manager, string unityVersion, string fallbackUnityVersion, bool hasTypeTree)
    {
        try
        {
            manager.LoadClassDatabaseFromPackage(unityVersion);
        }
        catch
        {
            if (!hasTypeTree)
                manager.LoadClassDatabaseFromPackage(fallbackUnityVersion);
        }
    }

    private static ManualTranslationAssetEntry CreateAssetEntry(
        ManualTranslationAssetStorageKind storageKind,
        ManualTranslationAssetValueKind valueKind,
        string objectType,
        string assetFile,
        string? bundleEntryName,
        long? pathId,
        string? displayName,
        string? fieldPath,
        string? relativePath,
        bool editable,
        string? originalText,
        string? codeLocation = null,
        string? editHint = null,
        int? width = null,
        int? height = null)
    {
        var preview = string.IsNullOrWhiteSpace(originalText)
            ? displayName
            : BuildPreview(originalText);
        var normalizedAssetFile = assetFile.Replace('\\', '/');
        var normalizedRelativePath = relativePath?.Replace('\\', '/');
        var listTitle = BuildListTitle(valueKind, preview, displayName, objectType);
        var listSubtitle = BuildListSubtitle(valueKind, displayName, fieldPath, codeLocation, objectType, editHint, width, height);
        var listMeta = BuildListMeta(normalizedAssetFile, bundleEntryName, normalizedRelativePath, pathId);

        return new ManualTranslationAssetEntry
        {
            AssetId = BuildAssetId(storageKind, assetFile, bundleEntryName, pathId, fieldPath, relativePath, codeLocation),
            StorageKind = storageKind,
            ValueKind = valueKind,
            ObjectType = objectType,
            AssetFile = normalizedAssetFile,
            BundleEntry = bundleEntryName,
            RelativePath = normalizedRelativePath,
            PathId = pathId,
            DisplayName = displayName,
            FieldPath = fieldPath,
            CodeLocation = codeLocation,
            Preview = preview,
            OriginalText = originalText,
            ListTitle = listTitle,
            ListSubtitle = listSubtitle,
            ListMeta = listMeta,
            IconKey = GetIconKey(valueKind),
            Width = width,
            Height = height,
            Editable = editable,
            EditHint = editHint
        };
    }

    private static string BuildAssetId(
        ManualTranslationAssetStorageKind storageKind,
        string assetFile,
        string? bundleEntryName,
        long? pathId,
        string? fieldPath,
        string? relativePath,
        string? codeLocation)
    {
        return string.Join("::",
            storageKind,
            assetFile.Replace('\\', '/'),
            bundleEntryName ?? "-",
            relativePath?.Replace('\\', '/') ?? "-",
            pathId?.ToString() ?? "-",
            fieldPath ?? "-",
            codeLocation ?? "-");
    }

    private static string BuildPreview(string text)
    {
        var singleLine = text.Replace("\r", " ").Replace("\n", " ").Trim();
        return singleLine.Length <= 96 ? singleLine : singleLine[..96] + "...";
    }

    private static string BuildListTitle(
        ManualTranslationAssetValueKind valueKind,
        string? preview,
        string? displayName,
        string objectType)
    {
        if (valueKind is ManualTranslationAssetValueKind.Text or ManualTranslationAssetValueKind.Code)
            return FirstNonEmpty(preview, displayName, objectType);

        return FirstNonEmpty(displayName, preview, objectType);
    }

    private static string? BuildListSubtitle(
        ManualTranslationAssetValueKind valueKind,
        string? displayName,
        string? fieldPath,
        string? codeLocation,
        string objectType,
        string? editHint,
        int? width,
        int? height)
    {
        return valueKind switch
        {
            ManualTranslationAssetValueKind.Text => JoinDisplayParts(
                fieldPath,
                GetMeaningfulDisplayName(displayName, objectType)),
            ManualTranslationAssetValueKind.Code => JoinDisplayParts(
                fieldPath,
                GetMeaningfulDisplayName(displayName, objectType),
                codeLocation),
            ManualTranslationAssetValueKind.Image => width > 0 && height > 0
                ? $"{width} × {height}"
                : objectType,
            ManualTranslationAssetValueKind.Font => FirstNonEmpty(GetMeaningfulDisplayName(displayName, objectType), BuildPreview(editHint ?? string.Empty), objectType),
            ManualTranslationAssetValueKind.Binary => FirstNonEmpty(BuildPreview(editHint ?? string.Empty), GetMeaningfulDisplayName(displayName, objectType), objectType),
            _ => objectType
        };
    }

    private static string BuildListMeta(string assetFile, string? bundleEntryName, string? relativePath, long? pathId)
    {
        return JoinDisplayParts(
            assetFile,
            bundleEntryName,
            relativePath,
            pathId.HasValue ? $"PathID {pathId.Value}" : null) ?? assetFile;
    }

    private static string GetIconKey(ManualTranslationAssetValueKind valueKind)
    {
        return valueKind switch
        {
            ManualTranslationAssetValueKind.Text => "text",
            ManualTranslationAssetValueKind.Code => "code",
            ManualTranslationAssetValueKind.Image => "image",
            ManualTranslationAssetValueKind.Font => "font",
            ManualTranslationAssetValueKind.Binary => "binary",
            _ => "asset"
        };
    }

    private static string? GetMeaningfulDisplayName(string? displayName, string objectType)
    {
        if (string.IsNullOrWhiteSpace(displayName))
            return null;

        if (displayName.Equals(objectType, StringComparison.OrdinalIgnoreCase))
            return null;

        if (displayName.StartsWith("(unnamed", StringComparison.OrdinalIgnoreCase))
            return null;

        return displayName;
    }

    private static string FirstNonEmpty(params string?[] values)
    {
        return values.First(value => !string.IsNullOrWhiteSpace(value))!;
    }

    private static string? JoinDisplayParts(params string?[] values)
    {
        var parts = values
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return parts.Length == 0 ? null : string.Join(" · ", parts);
    }

    private static string SafeString(string? value, string fallback)
    {
        return string.IsNullOrWhiteSpace(value) ? fallback : value;
    }

    private static string DecodeText(byte[] bytes)
    {
        if (bytes.Length == 0)
            return string.Empty;

        foreach (var encoding in GetCandidateEncodings())
        {
            try
            {
                var text = encoding.GetString(bytes).TrimEnd('\0');
                if (LooksLikeGameText(text))
                    return text;
            }
            catch
            {
                // Try next encoding.
            }
        }

        return Encoding.UTF8.GetString(bytes).TrimEnd('\0');
    }

    private static IEnumerable<Encoding> GetCandidateEncodings()
    {
        yield return new UTF8Encoding(false, throwOnInvalidBytes: true);
        yield return Encoding.Unicode;
        yield return Encoding.BigEndianUnicode;
        yield return Encoding.GetEncoding(932);
        yield return Encoding.GetEncoding(936);
        yield return Encoding.GetEncoding(950);
        yield return Encoding.GetEncoding(949);
        yield return Encoding.GetEncoding(1252);
    }

    private static bool LooksLikeGameText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return false;

        var trimmed = text.Trim();
        if (trimmed.Length < 2 || trimmed.Length > 4096)
            return false;
        if (trimmed.Contains('\0'))
            return false;

        var letterLike = trimmed.Count(ch =>
            char.IsLetterOrDigit(ch) ||
            ch >= 0x3040 ||
            ch >= 0x4E00);

        return letterLike >= Math.Max(2, trimmed.Length / 6);
    }
}
