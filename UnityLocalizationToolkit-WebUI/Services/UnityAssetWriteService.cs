using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AssetsTools.NET;
using AssetsTools.NET.Extra;
using AssetsTools.NET.Texture;
using dnlib.DotNet;
using dnlib.DotNet.Emit;
using UnityLocalizationToolkit_WebUI.Infrastructure;
using UnityLocalizationToolkit_WebUI.Models;

namespace UnityLocalizationToolkit_WebUI.Services;

public sealed class UnityAssetWriteService(
    AddressablesCatalogService addressablesCatalogService,
    AppDataPaths paths)
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

    public Task<ManualTranslationApplyResult> ApplyAsync(
        Game game,
        ManualTranslationProjectIndex index,
        IReadOnlyList<ManualTranslationOverrideEntry> overrides,
        string backupDirectory,
        CancellationToken ct = default)
    {
        if (game.DetectedInfo is null)
            throw new InvalidOperationException("Unity game metadata is required.");

        return Task.Run(() =>
        {
            Directory.CreateDirectory(backupDirectory);
            var backupManifest = LoadBackupManifest(game.Id, backupDirectory);
            var result = new ManualTranslationApplyResult();
            var overrideMap = overrides.ToDictionary(o => o.AssetId, StringComparer.Ordinal);
            var entries = index.Assets.Where(asset => overrideMap.ContainsKey(asset.AssetId)).ToList();
            if (entries.Count == 0)
                return result;

            var modifiedFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            ApplyLooseAssets(game, entries, overrideMap, backupDirectory, backupManifest, modifiedFiles, result, ct);
            ApplyBundleAssets(game, entries, overrideMap, backupDirectory, backupManifest, modifiedFiles, result, ct);
            ApplyManagedAssemblies(game, entries, overrideMap, backupDirectory, backupManifest, modifiedFiles, result);
            ApplyBinaryStringPatches(game, entries, overrideMap, backupDirectory, backupManifest, modifiedFiles, result, ct);

            foreach (var touchedCatalog in addressablesCatalogService.ClearCrcForModifiedGame(
                         game.GamePath,
                         sourcePath => EnsureBackup(game.GamePath, sourcePath, backupDirectory, backupManifest)))
            {
                modifiedFiles.Add(touchedCatalog);
            }

            SaveBackupManifest(backupDirectory, backupManifest);
            result.ModifiedFiles = modifiedFiles.Count;
            return result;
        }, ct);
    }

    public Task RestoreAsync(Game game, string backupDirectory, CancellationToken ct = default)
    {
        return Task.Run(() =>
        {
            var manifest = LoadBackupManifest(game.Id, backupDirectory);
            foreach (var entry in manifest.BackedUpFiles)
            {
                ct.ThrowIfCancellationRequested();
                var backupPath = Path.Combine(backupDirectory, entry.BackupFileName);
                if (!File.Exists(backupPath))
                    continue;

                var targetPath = PathSecurity.SafeJoin(game.GamePath, entry.OriginalRelativePath);
                Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
                File.Copy(backupPath, targetPath, overwrite: true);
            }
        }, ct);
    }

    private void ApplyLooseAssets(
        Game game,
        IReadOnlyList<ManualTranslationAssetEntry> entries,
        IReadOnlyDictionary<string, ManualTranslationOverrideEntry> overrideMap,
        string backupDirectory,
        ManualTranslationBackupManifest backupManifest,
        HashSet<string> modifiedFiles,
        ManualTranslationApplyResult result,
        CancellationToken ct)
    {
        var groups = entries
            .Where(entry => entry.StorageKind == ManualTranslationAssetStorageKind.LooseAssetFile)
            .GroupBy(entry => entry.AssetFile, StringComparer.OrdinalIgnoreCase);

        if (!groups.Any())
            return;

        var manager = new AssetsManager();
        try
        {
            using var tpkStream = new MemoryStream(ClassDataTpk.Value);
            manager.LoadClassPackage(tpkStream);
            SetupTemplateGenerator(manager, game);
            manager.UseTemplateFieldCache = true;
            manager.UseMonoTemplateFieldCache = true;

            foreach (var group in groups)
            {
                ct.ThrowIfCancellationRequested();
                var fullPath = PathSecurity.SafeJoin(game.GamePath, group.Key);
                if (!File.Exists(fullPath))
                {
                    result.Warnings.Add($"Missing asset file: {group.Key}");
                    continue;
                }

                EnsureBackup(game.GamePath, fullPath, backupDirectory, backupManifest);
                AssetsFileInstance? afileInst = null;
                var modified = false;

                try
                {
                    afileInst = manager.LoadAssetsFile(fullPath, false);
                    var afile = afileInst.file;
                    TryLoadClassDatabase(manager, afile.Metadata.UnityVersion, game.DetectedInfo!.UnityVersion, afile.Metadata.TypeTreeEnabled);

                    foreach (var entry in group)
                    {
                        var assetInfo = afile.Metadata.AssetInfos.FirstOrDefault(info => info.PathId == entry.PathId);
                        if (assetInfo is null)
                        {
                            result.Warnings.Add($"Missing asset PathId={entry.PathId} in {entry.AssetFile}");
                            continue;
                        }

                        if (ApplyAssetOverride(game.Id, manager, afileInst, assetInfo, entry, overrideMap[entry.AssetId], result.Warnings))
                        {
                            modified = true;
                            result.AppliedOverrides++;
                        }
                    }

                    if (!modified)
                        continue;

                    var tmpPath = fullPath + ".tmp";
                    using (var writer = new AssetsFileWriter(tmpPath))
                        afile.Write(writer);
                    manager.UnloadAssetsFile(afileInst);
                    afileInst = null;
                    File.Move(tmpPath, fullPath, overwrite: true);
                    modifiedFiles.Add(group.Key.Replace('\\', '/'));
                }
                finally
                {
                    if (afileInst is not null)
                        manager.UnloadAssetsFile(afileInst);
                }
            }
        }
        finally
        {
            manager.UnloadAll();
            if (manager.MonoTempGenerator is IDisposable disposable)
                disposable.Dispose();
        }
    }

    private void ApplyBundleAssets(
        Game game,
        IReadOnlyList<ManualTranslationAssetEntry> entries,
        IReadOnlyDictionary<string, ManualTranslationOverrideEntry> overrideMap,
        string backupDirectory,
        ManualTranslationBackupManifest backupManifest,
        HashSet<string> modifiedFiles,
        ManualTranslationApplyResult result,
        CancellationToken ct)
    {
        var groups = entries
            .Where(entry => entry.StorageKind == ManualTranslationAssetStorageKind.BundleEntry)
            .GroupBy(entry => entry.AssetFile, StringComparer.OrdinalIgnoreCase);

        if (!groups.Any())
            return;

        var manager = new AssetsManager();
        try
        {
            using var tpkStream = new MemoryStream(ClassDataTpk.Value);
            manager.LoadClassPackage(tpkStream);
            SetupTemplateGenerator(manager, game);
            manager.UseTemplateFieldCache = true;
            manager.UseMonoTemplateFieldCache = true;

            foreach (var bundleGroup in groups)
            {
                ct.ThrowIfCancellationRequested();
                var bundlePath = PathSecurity.SafeJoin(game.GamePath, bundleGroup.Key);
                if (!File.Exists(bundlePath))
                {
                    result.Warnings.Add($"Missing bundle file: {bundleGroup.Key}");
                    continue;
                }

                EnsureBackup(game.GamePath, bundlePath, backupDirectory, backupManifest);
                BundleFileInstance? bunInst = null;
                var anyModified = false;

                try
                {
                    bunInst = manager.LoadBundleFile(bundlePath, true);
                    var dirInfos = bunInst.file.BlockAndDirInfo.DirectoryInfos;

                    foreach (var entryGroup in bundleGroup.GroupBy(entry => entry.BundleEntry, StringComparer.OrdinalIgnoreCase))
                    {
                        if (string.IsNullOrWhiteSpace(entryGroup.Key))
                            continue;

                        var entryIndex = -1;
                        for (var i = 0; i < dirInfos.Count; i++)
                        {
                            if (dirInfos[i].Name.Equals(entryGroup.Key, StringComparison.OrdinalIgnoreCase))
                            {
                                entryIndex = i;
                                break;
                            }
                        }

                        if (entryIndex < 0)
                        {
                            result.Warnings.Add($"Missing bundle entry: {bundleGroup.Key}/{entryGroup.Key}");
                            continue;
                        }

                        AssetsFileInstance? afileInst = null;
                        try
                        {
                            afileInst = manager.LoadAssetsFileFromBundle(bunInst, entryIndex, false);
                            var afile = afileInst.file;
                            TryLoadClassDatabase(manager, afile.Metadata.UnityVersion, game.DetectedInfo!.UnityVersion, afile.Metadata.TypeTreeEnabled);

                            var modified = false;
                            foreach (var entry in entryGroup)
                            {
                                var assetInfo = afile.Metadata.AssetInfos.FirstOrDefault(info => info.PathId == entry.PathId);
                                if (assetInfo is null)
                                {
                                    result.Warnings.Add($"Missing bundle asset PathId={entry.PathId} in {entry.AssetFile}/{entry.BundleEntry}");
                                    continue;
                                }

                                if (ApplyAssetOverride(game.Id, manager, afileInst, assetInfo, entry, overrideMap[entry.AssetId], result.Warnings))
                                {
                                    modified = true;
                                    result.AppliedOverrides++;
                                }
                            }

                            if (!modified)
                                continue;

                            dirInfos[entryIndex].SetNewData(afile);
                            anyModified = true;
                        }
                        finally
                        {
                            if (afileInst is not null)
                                manager.UnloadAssetsFile(afileInst);
                        }
                    }

                    if (!anyModified)
                        continue;

                    var tmpPath = bundlePath + ".tmp";
                    var compressedPath = bundlePath + ".lz4.tmp";
                    using (var writer = new AssetsFileWriter(tmpPath))
                        bunInst.file.Write(writer);
                    manager.UnloadBundleFile(bunInst);
                    bunInst = null;

                    using (var reReader = new AssetsFileReader(File.OpenRead(tmpPath)))
                    {
                        var newBundle = new AssetBundleFile();
                        newBundle.Read(reReader);
                        using var writer = new AssetsFileWriter(compressedPath);
                        newBundle.Pack(writer, AssetBundleCompressionType.LZ4);
                        newBundle.Close();
                    }

                    File.Delete(tmpPath);
                    File.Move(compressedPath, bundlePath, overwrite: true);
                    modifiedFiles.Add(bundleGroup.Key.Replace('\\', '/'));
                }
                finally
                {
                    if (bunInst is not null)
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
    }

    private void ApplyManagedAssemblies(
        Game game,
        IReadOnlyList<ManualTranslationAssetEntry> entries,
        IReadOnlyDictionary<string, ManualTranslationOverrideEntry> overrideMap,
        string backupDirectory,
        ManualTranslationBackupManifest backupManifest,
        HashSet<string> modifiedFiles,
        ManualTranslationApplyResult result)
    {
        var groups = entries
            .Where(entry => entry.StorageKind == ManualTranslationAssetStorageKind.ManagedAssembly)
            .GroupBy(entry => entry.RelativePath ?? entry.AssetFile, StringComparer.OrdinalIgnoreCase);

        foreach (var group in groups)
        {
            var assemblyPath = PathSecurity.SafeJoin(game.GamePath, group.Key);
            if (!File.Exists(assemblyPath))
            {
                result.Warnings.Add($"Missing managed assembly: {group.Key}");
                continue;
            }

            EnsureBackup(game.GamePath, assemblyPath, backupDirectory, backupManifest);
            using var module = ModuleDefMD.Load(assemblyPath);
            var modified = false;

            foreach (var entry in group)
            {
                if (ApplyManagedStringLiteral(module, entry, overrideMap[entry.AssetId].Value ?? string.Empty, result.Warnings))
                {
                    modified = true;
                    result.AppliedOverrides++;
                }
            }

            if (!modified)
                continue;

            var tmpPath = assemblyPath + ".tmp";
            module.Write(tmpPath);
            File.Move(tmpPath, assemblyPath, overwrite: true);
            modifiedFiles.Add(group.Key.Replace('\\', '/'));
        }
    }

    private void ApplyBinaryStringPatches(
        Game game,
        IReadOnlyList<ManualTranslationAssetEntry> entries,
        IReadOnlyDictionary<string, ManualTranslationOverrideEntry> overrideMap,
        string backupDirectory,
        ManualTranslationBackupManifest backupManifest,
        HashSet<string> modifiedFiles,
        ManualTranslationApplyResult result,
        CancellationToken ct)
    {
        var groups = entries
            .Where(entry => entry.StorageKind is ManualTranslationAssetStorageKind.Il2CppMetadata or ManualTranslationAssetStorageKind.NativeBinary)
            .GroupBy(entry => entry.RelativePath ?? entry.AssetFile, StringComparer.OrdinalIgnoreCase);

        foreach (var group in groups)
        {
            ct.ThrowIfCancellationRequested();
            var fullPath = PathSecurity.SafeJoin(game.GamePath, group.Key);
            if (!File.Exists(fullPath))
            {
                result.Warnings.Add($"Missing binary file: {group.Key}");
                continue;
            }

            EnsureBackup(game.GamePath, fullPath, backupDirectory, backupManifest);
            var bytes = File.ReadAllBytes(fullPath);
            var modified = false;

            foreach (var entry in group)
            {
                if (TryPatchBinaryString(bytes, entry, overrideMap[entry.AssetId].Value ?? string.Empty, result.Warnings))
                {
                    modified = true;
                    result.AppliedOverrides++;
                }
            }

            if (!modified)
                continue;

            var tmpPath = fullPath + ".tmp";
            File.WriteAllBytes(tmpPath, bytes);
            File.Move(tmpPath, fullPath, overwrite: true);
            modifiedFiles.Add(group.Key.Replace('\\', '/'));
        }
    }

    private bool ApplyAssetOverride(
        string gameId,
        AssetsManager manager,
        AssetsFileInstance afileInst,
        AssetFileInfo assetInfo,
        ManualTranslationAssetEntry entry,
        ManualTranslationOverrideEntry overrideEntry,
        List<string> warnings)
    {
        var baseField = manager.GetBaseField(afileInst, assetInfo);
        if (baseField.IsDummy)
        {
            warnings.Add($"Failed to deserialize asset: {entry.AssetId}");
            return false;
        }

        if (overrideEntry.Kind == ManualTranslationOverrideKind.Image)
        {
            if (entry.ObjectType != "Texture2D")
            {
                warnings.Add($"Unsupported image override target: {entry.ObjectType}");
                return false;
            }

            if (string.IsNullOrWhiteSpace(overrideEntry.ImageFileName))
            {
                warnings.Add($"Image override payload missing file: {entry.AssetId}");
                return false;
            }

            var imagePath = paths.ManualTranslationOverrideMediaFile(gameId, overrideEntry.ImageFileName);
            return ApplyTextureOverride(baseField, assetInfo, entry, imagePath, warnings);
        }

        if (entry.ObjectType == "TextAsset")
        {
            var scriptField = baseField["m_Script"];
            if (scriptField.IsDummy)
            {
                warnings.Add($"TextAsset field missing: {entry.AssetId}");
                return false;
            }

            scriptField.AsString = overrideEntry.Value ?? string.Empty;
            assetInfo.SetNewData(baseField);
            return true;
        }

        if (entry.ObjectType != "MonoBehaviour" || string.IsNullOrWhiteSpace(entry.FieldPath))
        {
            warnings.Add($"Unsupported asset type for in-place write: {entry.ObjectType}");
            return false;
        }

        var targetField = ResolveFieldPath(baseField, entry.FieldPath);
        if (targetField.IsDummy)
        {
            warnings.Add($"Failed to resolve field path: {entry.FieldPath}");
            return false;
        }

        targetField.AsString = overrideEntry.Value ?? string.Empty;
        assetInfo.SetNewData(baseField);
        return true;
    }

    private static bool ApplyTextureOverride(
        AssetTypeValueField baseField,
        AssetFileInfo assetInfo,
        ManualTranslationAssetEntry entry,
        string imagePath,
        List<string> warnings)
    {
        if (!File.Exists(imagePath))
        {
            warnings.Add($"Image override file missing for {entry.DisplayName ?? entry.AssetId}");
            return false;
        }

        try
        {
            var texture = TextureFile.ReadTextureFile(baseField);
            texture.EncodeTextureImage(imagePath);
            texture.WriteTo(baseField);
            assetInfo.SetNewData(baseField);
            return true;
        }
        catch (Exception ex)
        {
            warnings.Add($"Failed to apply texture override for {entry.DisplayName ?? entry.AssetId}: {ex.Message}");
            return false;
        }
    }

    private static AssetTypeValueField ResolveFieldPath(AssetTypeValueField root, string fieldPath)
    {
        var current = root;
        foreach (var segment in fieldPath.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            var open = segment.IndexOf('[');
            var fieldName = open >= 0 ? segment[..open] : segment;
            var index = open >= 0
                ? int.Parse(segment[(open + 1)..segment.IndexOf(']', open)])
                : -1;

            current = current[fieldName];
            if (index >= 0)
            {
                var array = current["Array"];
                current = array.Children[index];
            }
        }

        return current;
    }

    private static bool ApplyManagedStringLiteral(
        ModuleDefMD module,
        ManualTranslationAssetEntry entry,
        string value,
        List<string> warnings)
    {
        if (string.IsNullOrWhiteSpace(entry.CodeLocation))
            return false;

        var parts = entry.CodeLocation.Split(':', 2);
        if (parts.Length != 2 || !int.TryParse(parts[0], out var token) || !int.TryParse(parts[1], out var instructionIndex))
        {
            warnings.Add($"Invalid managed code location: {entry.CodeLocation}");
            return false;
        }

        var method = module.GetTypes()
            .SelectMany(type => type.Methods)
            .FirstOrDefault(candidate => candidate.MDToken.Raw == token);
        if (method?.Body is null || instructionIndex < 0 || instructionIndex >= method.Body.Instructions.Count)
        {
            warnings.Add($"Managed method not found: {entry.CodeLocation}");
            return false;
        }

        var instruction = method.Body.Instructions[instructionIndex];
        if (instruction.OpCode != OpCodes.Ldstr)
        {
            warnings.Add($"Target IL instruction is not ldstr: {entry.CodeLocation}");
            return false;
        }

        instruction.Operand = value;
        return true;
    }

    private static bool TryPatchBinaryString(byte[] buffer, ManualTranslationAssetEntry entry, string value, List<string> warnings)
    {
        if (string.IsNullOrWhiteSpace(entry.CodeLocation))
            return false;

        var parts = entry.CodeLocation.Split(':');
        if (parts.Length != 3 ||
            !int.TryParse(parts[0], out var offset) ||
            !int.TryParse(parts[1], out var length))
        {
            warnings.Add($"Invalid binary code location: {entry.CodeLocation}");
            return false;
        }

        var encoding = string.Equals(parts[2], "utf16", StringComparison.OrdinalIgnoreCase)
            ? Encoding.Unicode
            : Encoding.UTF8;

        var replacementBytes = encoding.GetBytes(value);
        if (replacementBytes.Length > length)
        {
            warnings.Add($"Skipped overlong binary patch for {entry.DisplayName ?? entry.AssetId}");
            return false;
        }

        Array.Clear(buffer, offset, length);
        Array.Copy(replacementBytes, 0, buffer, offset, replacementBytes.Length);
        return true;
    }

    private static void SetupTemplateGenerator(AssetsManager manager, Game game)
    {
        var executableName = game.ExecutableName ?? game.DetectedInfo?.DetectedExecutable;
        if (game.DetectedInfo is null || string.IsNullOrWhiteSpace(executableName))
            return;

        var dataPath = Path.Combine(game.GamePath, $"{Path.GetFileNameWithoutExtension(executableName)}_Data");

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
                    // Ignore.
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
            // Ignore.
        }
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

    private static ManualTranslationBackupManifest LoadBackupManifest(string gameId, string backupDirectory)
    {
        var manifestPath = Path.Combine(backupDirectory, "manifest.json");
        if (!File.Exists(manifestPath))
            return new ManualTranslationBackupManifest { GameId = gameId };

        var json = File.ReadAllText(manifestPath);
        return JsonSerializer.Deserialize<ManualTranslationBackupManifest>(json, FileHelper.DataJsonOptions)
               ?? new ManualTranslationBackupManifest { GameId = gameId };
    }

    private static void SaveBackupManifest(string backupDirectory, ManualTranslationBackupManifest manifest)
    {
        var manifestPath = Path.Combine(backupDirectory, "manifest.json");
        FileHelper.WriteJsonAtomicAsync(manifestPath, manifest).GetAwaiter().GetResult();
    }

    private static bool EnsureBackup(
        string gamePath,
        string sourcePath,
        string backupDirectory,
        ManualTranslationBackupManifest manifest)
    {
        var relativePath = Path.GetRelativePath(gamePath, sourcePath).Replace('\\', '/');
        if (!File.Exists(sourcePath))
            return false;

        var existingEntry = manifest.BackedUpFiles
            .FirstOrDefault(entry => entry.OriginalRelativePath.Equals(relativePath, StringComparison.OrdinalIgnoreCase));
        var backupName = existingEntry?.BackupFileName
            ?? Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(relativePath))) + Path.GetExtension(sourcePath);
        File.Copy(sourcePath, Path.Combine(backupDirectory, backupName), overwrite: true);

        if (existingEntry is null)
        {
            manifest.BackedUpFiles.Add(new BackupEntry
            {
                OriginalRelativePath = relativePath,
                BackupFileName = backupName
            });
        }

        return true;
    }
}
