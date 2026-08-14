namespace XUnityToolkit_WebUI.Services;

using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using XUnityToolkit_WebUI.Models;

internal readonly record struct LocalManagedFile(string RelativePath, string FullPath, long Size);

internal sealed class UpdateManifestDiffResult
{
    public required Dictionary<string, LocalManagedFile> LocalFiles { get; init; }
    public required List<string> ChangedPackages { get; init; }
    public required int ChangedFileCount { get; init; }
    public required List<string> DeletedFiles { get; init; }
}

internal static class UpdateManifestFileSet
{
    public const string AppFileInventoryName = "app-file-inventory-v1.json";

    private static readonly JsonSerializerOptions InventoryJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    /// <summary>
    /// Validates a path supplied by a signed release manifest. Release manifests may contain
    /// WinUI self-contained resources in arbitrary subdirectories, while user data and local
    /// configuration are always protected.
    /// </summary>
    public static bool ShouldManageForManifest(string relativePath)
    {
        if (!TryNormalizeSafeRelativePath(relativePath, out var normalized))
            return false;

        return !IsProtectedPath(normalized);
    }

    public static bool ShouldPreserveLocalFile(
        string relativePath,
        bool preserveCustomLlamaFiles,
        bool hasBundledLlama)
    {
        if (!preserveCustomLlamaFiles || hasBundledLlama)
            return false;

        var normalized = NormalizeRelativePath(relativePath);
        return normalized.StartsWith("bundled/llama/", StringComparison.OrdinalIgnoreCase);
    }

    public static Dictionary<string, LocalManagedFile> EnumerateManagedLocalFiles(
        string appDir,
        bool preserveCustomLlamaFiles,
        bool hasBundledLlama)
    {
        var files = new Dictionary<string, LocalManagedFile>(StringComparer.OrdinalIgnoreCase);
        if (!Directory.Exists(appDir))
            return files;

        var inventoryFiles = TryReadAppFileInventory(appDir);
        foreach (var filePath in Directory.EnumerateFiles(appDir, "*", SearchOption.AllDirectories))
        {
            var relative = NormalizeRelativePath(Path.GetRelativePath(appDir, filePath));
            var shouldManage = inventoryFiles is null
                ? ShouldManageLegacyLocalPath(relative)
                : IsSharedComponentPath(relative) || inventoryFiles.Contains(relative);
            if (!shouldManage)
                continue;

            if (ShouldPreserveLocalFile(relative, preserveCustomLlamaFiles, hasBundledLlama))
                continue;

            var info = new FileInfo(filePath);
            files[relative] = new LocalManagedFile(relative, filePath, info.Length);
        }

        return files;
    }

    public static HashSet<string>? ReadAppFileInventory(Stream stream)
    {
        try
        {
            var inventory = JsonSerializer.Deserialize<AppFileInventory>(stream, InventoryJsonOptions);
            if (inventory is null || inventory.ProtocolVersion != 1 || inventory.Files.Count == 0)
                return null;

            var files = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var path in inventory.Files)
            {
                if (!TryNormalizeSafeRelativePath(path, out var normalized)
                    || IsProtectedPath(normalized)
                    || IsSharedComponentPath(normalized))
                {
                    return null;
                }

                files.Add(normalized);
            }

            return files.Contains(AppFileInventoryName) ? files : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public static void ValidateAppPackageInventory(
        ZipArchive archive,
        UpdateManifest manifest)
    {
        var remoteHasInventory = manifest.Files.Keys.Any(path =>
            string.Equals(
                path.Replace('\\', '/'),
                AppFileInventoryName,
                StringComparison.OrdinalIgnoreCase));
        if (!remoteHasInventory)
            return; // Legacy release compatibility.

        var inventoryEntry = archive.Entries.FirstOrDefault(entry =>
            string.Equals(
                entry.FullName.Replace('\\', '/'),
                AppFileInventoryName,
                StringComparison.OrdinalIgnoreCase));
        if (inventoryEntry is null)
            throw new InvalidOperationException("应用组件包缺少 app-file-inventory-v1.json");

        HashSet<string>? inventoryFiles;
        using (var stream = inventoryEntry.Open())
            inventoryFiles = ReadAppFileInventory(stream);

        if (inventoryFiles is null)
            throw new InvalidOperationException("应用组件包的文件清单无效");

        var manifestAppFiles = manifest.Files
            .Where(pair =>
                string.Equals(pair.Value.Package, "app", StringComparison.OrdinalIgnoreCase)
                && ShouldManageForManifest(pair.Key))
            .Select(pair => pair.Key.Replace('\\', '/').TrimStart('/'))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (!inventoryFiles.SetEquals(manifestAppFiles))
        {
            var missingFromInventory = manifestAppFiles.Except(inventoryFiles).Take(3);
            var missingFromManifest = inventoryFiles.Except(manifestAppFiles).Take(3);
            throw new InvalidOperationException(
                "应用组件文件清单与更新清单不一致。"
                + $" 未列入应用清单: {string.Join(", ", missingFromInventory)};"
                + $" 未列入更新清单: {string.Join(", ", missingFromManifest)}");
        }

        var archiveFiles = archive.Entries
            .Where(entry => !string.IsNullOrEmpty(entry.Name))
            .Select(entry => entry.FullName.Replace('\\', '/').TrimStart('/'))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var missingFromArchive = inventoryFiles.Except(archiveFiles).Take(3).ToArray();
        if (missingFromArchive.Length > 0)
        {
            throw new InvalidOperationException(
                $"应用组件包缺少清单文件: {string.Join(", ", missingFromArchive)}");
        }
    }

    public static UpdateManifestDiffResult ComputeDiff(
        string appDir,
        UpdateManifest manifest,
        bool preserveCustomLlamaFiles,
        bool hasBundledLlama)
    {
        var localFiles = EnumerateManagedLocalFiles(appDir, preserveCustomLlamaFiles, hasBundledLlama);
        var remoteFiles = manifest.Files
            .Where(pair => ShouldManageForManifest(pair.Key))
            .ToDictionary(
                pair => NormalizeRelativePath(pair.Key),
                pair => pair.Value,
                StringComparer.OrdinalIgnoreCase);

        var changedPackages = new List<string>();
        var changedPackageSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var changedFileCount = 0;

        foreach (var (relativePath, entry) in remoteFiles)
        {
            localFiles.TryGetValue(relativePath, out var localFile);
            if (!IsManifestFileChanged(localFile, entry))
                continue;

            changedFileCount++;
            if (changedPackageSet.Add(entry.Package))
                changedPackages.Add(entry.Package);
        }

        var deletedFiles = localFiles.Keys
            .Where(relativePath => !remoteFiles.ContainsKey(relativePath))
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new UpdateManifestDiffResult
        {
            LocalFiles = localFiles,
            ChangedPackages = changedPackages,
            ChangedFileCount = changedFileCount,
            DeletedFiles = deletedFiles
        };
    }

    public static bool IsManifestFileChanged(LocalManagedFile? localFile, ManifestFileEntry entry)
    {
        if (localFile is null)
            return true;

        if (localFile.Value.Size != entry.Size)
            return true;

        return ComputeFileHash(localFile.Value.FullPath) != entry.Hash;
    }

    public static string ComputeFileHash(string filePath)
    {
        using var stream = File.OpenRead(filePath);
        var hash = SHA256.HashData(stream);
        return "sha256:" + Convert.ToHexStringLower(hash);
    }

    private static HashSet<string>? TryReadAppFileInventory(string appDir)
    {
        var inventoryPath = Path.Combine(appDir, AppFileInventoryName);
        if (!File.Exists(inventoryPath))
            return null;

        try
        {
            using var stream = File.OpenRead(inventoryPath);
            return ReadAppFileInventory(stream);
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static bool ShouldManageLegacyLocalPath(string relativePath)
    {
        if (!ShouldManageForManifest(relativePath))
            return false;

        return IsSharedComponentPath(relativePath)
            || relativePath.StartsWith("runtimes/", StringComparison.OrdinalIgnoreCase)
            || !relativePath.Contains('/');
    }

    private static bool IsSharedComponentPath(string relativePath) =>
        relativePath.StartsWith("wwwroot/", StringComparison.OrdinalIgnoreCase)
        || relativePath.StartsWith("bundled/", StringComparison.OrdinalIgnoreCase);

    private static bool IsProtectedPath(string normalizedPath) =>
        normalizedPath.StartsWith("data/", StringComparison.OrdinalIgnoreCase)
        || normalizedPath.StartsWith("appsettings", StringComparison.OrdinalIgnoreCase);

    private static bool TryNormalizeSafeRelativePath(string relativePath, out string normalized)
    {
        normalized = string.Empty;
        if (string.IsNullOrWhiteSpace(relativePath)
            || Path.IsPathRooted(relativePath)
            || relativePath.IndexOf('\0') >= 0)
        {
            return false;
        }

        normalized = NormalizeRelativePath(relativePath);
        if (string.IsNullOrEmpty(normalized))
            return false;

        var segments = normalized.Split('/');
        return segments.All(segment =>
            !string.IsNullOrEmpty(segment)
            && segment != "."
            && segment != ".."
            && segment.IndexOf(':') < 0);
    }

    private static string NormalizeRelativePath(string relativePath)
    {
        return relativePath
            .Replace('\\', '/')
            .TrimStart('/');
    }

    private sealed class AppFileInventory
    {
        [JsonPropertyName("protocolVersion")]
        public int ProtocolVersion { get; init; }

        [JsonPropertyName("files")]
        public List<string> Files { get; init; } = [];
    }
}
