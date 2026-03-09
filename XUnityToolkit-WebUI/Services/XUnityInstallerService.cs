using System.IO.Compression;
using XUnityToolkit_WebUI.Infrastructure;
using XUnityToolkit_WebUI.Models;

namespace XUnityToolkit_WebUI.Services;

public sealed class XUnityInstallerService(ILogger<XUnityInstallerService> logger)
{
    public GitHubAsset? ResolveAsset(UnityGameInfo info, IReadOnlyList<GitHubAsset> assets)
    {
        // IL2CPP games need the IL2CPP-specific build
        var isIL2CPP = info.Backend == UnityBackend.IL2CPP;

        return assets.FirstOrDefault(a =>
        {
            var name = a.Name;
            if (!name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                return false;
            if (!name.StartsWith("XUnity.AutoTranslator-BepInEx", StringComparison.OrdinalIgnoreCase))
                return false;

            var hasIL2CPP = name.Contains("IL2CPP", StringComparison.OrdinalIgnoreCase);
            return isIL2CPP == hasIL2CPP;
        });
    }

    public Task<List<string>> InstallAsync(string gamePath, string zipPath, CancellationToken ct = default)
    {
        logger.LogInformation("Installing XUnity.AutoTranslator from {Zip} to {Game}", zipPath, gamePath);

        var installedFiles = new List<string>();

        using var archive = ZipFile.OpenRead(zipPath);
        foreach (var entry in archive.Entries)
        {
            ct.ThrowIfCancellationRequested();

            if (string.IsNullOrEmpty(entry.Name))
                continue;

            var destPath = PathSecurity.SafeJoin(gamePath, entry.FullName);
            var destDir = Path.GetDirectoryName(destPath)!;
            Directory.CreateDirectory(destDir);

            entry.ExtractToFile(destPath, overwrite: true);
            installedFiles.Add(entry.FullName);
        }

        logger.LogInformation("XUnity.AutoTranslator installed: {Count} files", installedFiles.Count);
        return Task.FromResult(installedFiles);
    }

    public Task UninstallAsync(string gamePath, CancellationToken ct = default)
    {
        logger.LogInformation("Uninstalling XUnity.AutoTranslator from {Game}", gamePath);

        // Remove XUnity plugin directories
        var pluginsDir = Path.Combine(gamePath, "BepInEx", "plugins");
        if (Directory.Exists(pluginsDir))
        {
            var xunityDirs = new[] { "XUnity.AutoTranslator", "XUnity.ResourceRedirector" };
            foreach (var dir in xunityDirs)
            {
                var path = Path.Combine(pluginsDir, dir);
                if (Directory.Exists(path))
                    Directory.Delete(path, recursive: true);
            }
        }

        // Remove XUnity.Common from core
        var commonDll = Path.Combine(gamePath, "BepInEx", "core", "XUnity.Common.dll");
        if (File.Exists(commonDll))
            File.Delete(commonDll);

        // Remove XUnity config
        var configFile = Path.Combine(gamePath, "BepInEx", "config",
            "gravydevsupreme.xunity.autotranslator.cfg");
        if (File.Exists(configFile))
            File.Delete(configFile);

        // Remove translation output
        var translationDir = Path.Combine(gamePath, "BepInEx", "Translation");
        if (Directory.Exists(translationDir))
            Directory.Delete(translationDir, recursive: true);

        return Task.CompletedTask;
    }
}
