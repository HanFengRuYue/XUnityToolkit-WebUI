namespace XUnityToolkit_WebUI.Services;

using System.Security.Cryptography;
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
    public static bool ShouldManageForManifest(string relativePath)
    {
        var normalized = NormalizeRelativePath(relativePath);
        if (string.IsNullOrEmpty(normalized))
            return false;

        if (normalized.StartsWith("data/", StringComparison.OrdinalIgnoreCase))
            return false;

        if (normalized.StartsWith("appsettings", StringComparison.OrdinalIgnoreCase))
            return false;

        if (normalized.StartsWith("wwwroot/", StringComparison.OrdinalIgnoreCase))
            return true;

        if (normalized.StartsWith("bundled/", StringComparison.OrdinalIgnoreCase))
            return true;

        if (normalized.StartsWith("runtimes/", StringComparison.OrdinalIgnoreCase))
            return true;

        return !normalized.Contains('/');
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

        foreach (var filePath in Directory.EnumerateFiles(appDir, "*", SearchOption.AllDirectories))
        {
            var relative = NormalizeRelativePath(Path.GetRelativePath(appDir, filePath));
            if (!ShouldManageForManifest(relative))
                continue;

            if (ShouldPreserveLocalFile(relative, preserveCustomLlamaFiles, hasBundledLlama))
                continue;

            var info = new FileInfo(filePath);
            files[relative] = new LocalManagedFile(relative, filePath, info.Length);
        }

        return files;
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

    private static string NormalizeRelativePath(string relativePath)
    {
        return relativePath
            .Replace('\\', '/')
            .TrimStart('/');
    }
}
