using System.IO.Compression;
using XUnityToolkit_WebUI.Infrastructure;
using XUnityToolkit_WebUI.Models;

namespace XUnityToolkit_WebUI.Services;

public sealed class BepInExInstallerService(ILogger<BepInExInstallerService> logger)
{
    public string? ResolveAssetName(UnityGameInfo info, IReadOnlyList<GitHubAsset> assets, bool useBepInEx6)
    {
        string pattern;

        if (useBepInEx6)
        {
            var runtime = info.Backend == UnityBackend.IL2CPP ? "IL2CPP" : "Mono";
            var arch = info.Architecture == Architecture.X64 ? "x64" : "x86";
            pattern = $"BepInEx-Unity.{runtime}-win-{arch}-";
        }
        else
        {
            if (info.Backend == UnityBackend.IL2CPP)
                return null; // BepInEx 5 does not support IL2CPP

            var arch = info.Architecture == Architecture.X64 ? "x64" : "x86";
            pattern = $"BepInEx_win_{arch}_";
        }

        var asset = assets.FirstOrDefault(a =>
            a.Name.StartsWith(pattern, StringComparison.OrdinalIgnoreCase) &&
            a.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase));

        if (asset is null)
            logger.LogWarning("No matching BepInEx asset found for pattern: {Pattern}", pattern);

        return asset?.Name;
    }

    public GitHubAsset? ResolveAsset(UnityGameInfo info, IReadOnlyList<GitHubAsset> assets, bool useBepInEx6)
    {
        string pattern;

        if (useBepInEx6)
        {
            var runtime = info.Backend == UnityBackend.IL2CPP ? "IL2CPP" : "Mono";
            var arch = info.Architecture == Architecture.X64 ? "x64" : "x86";
            pattern = $"BepInEx-Unity.{runtime}-win-{arch}-";
        }
        else
        {
            if (info.Backend == UnityBackend.IL2CPP)
                return null;

            var arch = info.Architecture == Architecture.X64 ? "x64" : "x86";
            pattern = $"BepInEx_win_{arch}_";
        }

        return assets.FirstOrDefault(a =>
            a.Name.StartsWith(pattern, StringComparison.OrdinalIgnoreCase) &&
            a.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase));
    }

    public Task<List<string>> InstallAsync(string gamePath, string zipPath, CancellationToken ct = default)
    {
        logger.LogInformation("Installing BepInEx from {Zip} to {Game}", zipPath, gamePath);

        var installedFiles = new List<string>();

        using var archive = ZipFile.OpenRead(zipPath);
        foreach (var entry in archive.Entries)
        {
            ct.ThrowIfCancellationRequested();

            if (string.IsNullOrEmpty(entry.Name))
                continue; // Skip directories

            var destPath = PathSecurity.SafeJoin(gamePath, entry.FullName);
            var destDir = Path.GetDirectoryName(destPath)!;
            Directory.CreateDirectory(destDir);

            entry.ExtractToFile(destPath, overwrite: true);
            installedFiles.Add(entry.FullName);
        }

        logger.LogInformation("BepInEx installed: {Count} files", installedFiles.Count);
        return Task.FromResult(installedFiles);
    }

    public Task UninstallAsync(string gamePath, CancellationToken ct = default)
    {
        logger.LogInformation("Uninstalling BepInEx from {Game}", gamePath);

        // Remove BepInEx directory
        var bepinexDir = Path.Combine(gamePath, "BepInEx");
        if (Directory.Exists(bepinexDir))
            Directory.Delete(bepinexDir, recursive: true);

        // Remove doorstop files
        string[] doorstopFiles = ["winhttp.dll", "doorstop_config.ini", ".doorstop_version"];
        foreach (var file in doorstopFiles)
        {
            var path = Path.Combine(gamePath, file);
            if (File.Exists(path))
                File.Delete(path);
        }

        // Remove dotnet runtime directory (BepInEx 6 IL2CPP)
        var dotnetDir = Path.Combine(gamePath, "dotnet");
        if (Directory.Exists(dotnetDir))
            Directory.Delete(dotnetDir, recursive: true);

        // Remove dobby.dll (BepInEx 6 IL2CPP)
        var dobbyPath = Path.Combine(gamePath, "dobby.dll");
        if (File.Exists(dobbyPath))
            File.Delete(dobbyPath);

        return Task.CompletedTask;
    }
}
