using System.IO.Compression;
using UnityLocalizationToolkit_WebUI.Infrastructure;
using UnityLocalizationToolkit_WebUI.Models;

namespace UnityLocalizationToolkit_WebUI.Services;

public sealed class BepInExInstallerService(ILogger<BepInExInstallerService> logger)
{
    /// <summary>
    /// Resolves the bundled BepInEx ZIP for the given game info.
    /// BepInEx 5 for Mono, BepInEx 6 BE for IL2CPP.
    /// </summary>
    public string ResolveBundledZip(UnityGameInfo info, BundledAssetPaths bundled)
    {
        var archStr = info.Architecture == Architecture.X64 ? "x64" : "x86";
        var usesBepInEx6 = info.Backend == UnityBackend.IL2CPP;

        var zipPath = usesBepInEx6
            ? bundled.FindBepInEx6Zip(archStr)
            : bundled.FindBepInEx5Zip(archStr);

        return zipPath ?? throw new InvalidOperationException(
            $"未找到捆绑的 BepInEx ZIP（{(usesBepInEx6 ? "IL2CPP" : "Mono")} {archStr}）。请重新构建发布版本。");
    }

    /// <summary>Parses version string from bundled ZIP filename.</summary>
    public static string ParseVersionFromZip(string zipPath)
    {
        var name = Path.GetFileNameWithoutExtension(zipPath);
        if (name.Contains("IL2CPP"))
        {
            // BepInEx-Unity.IL2CPP-win-x64-6.0.0-be.755+3fab71a
            var beIdx = name.IndexOf("-be.", StringComparison.Ordinal);
            return beIdx >= 0 ? $"v6.0.0-be.{name[(beIdx + 4)..]}" : "v6.0.0-be";
        }
        // BepInEx_win_x64_5.4.23.2
        var parts = name.Split('_');
        return parts.Length >= 4 ? $"v{parts[^1]}" : "v5.x";
    }

    public Task<List<string>> InstallAsync(string gamePath, string zipPath, CancellationToken ct = default)
    {
        logger.LogInformation("正在安装 BepInEx: {Zip}", zipPath);

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

        logger.LogInformation("BepInEx 安装完成，共 {Count} 个文件", installedFiles.Count);
        return Task.FromResult(installedFiles);
    }

    public Task UninstallAsync(string gamePath, CancellationToken ct = default)
    {
        logger.LogInformation("正在卸载 BepInEx: {Game}", gamePath);

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
