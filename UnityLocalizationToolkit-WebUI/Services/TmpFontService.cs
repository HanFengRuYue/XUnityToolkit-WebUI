using System.Text.RegularExpressions;
using UnityLocalizationToolkit_WebUI.Infrastructure;
using UnityLocalizationToolkit_WebUI.Models;

namespace UnityLocalizationToolkit_WebUI.Services;

public sealed partial class TmpFontService(BundledAssetPaths bundledPaths, ILogger<TmpFontService> logger)
{
    private static string GetFontDirectory(string gamePath) =>
        Path.Combine(gamePath, "BepInEx", "Font");

    /// <summary>
    /// Check if any TMP font is installed in BepInEx/Font/.
    /// </summary>
    public bool IsFontInstalled(string gamePath)
    {
        var fontDir = GetFontDirectory(gamePath);
        if (!Directory.Exists(fontDir)) return false;
        return Directory.GetFiles(fontDir).Length > 0;
    }

    /// <summary>
    /// Install the best-matching TMP font for the given Unity version.
    /// Returns the config value (e.g. "BepInEx/Font/SourceHanSans_U2022"), or null if no font available.
    /// </summary>
    public string? InstallFont(string gamePath, UnityGameInfo gameInfo)
    {
        var fontFile = ResolveFontFile(gameInfo.UnityVersion);
        if (fontFile is null)
        {
            logger.LogWarning("未找到可用的 TMP 字体文件");
            return null;
        }

        var fileName = Path.GetFileName(fontFile);
        var destPath = Path.Combine(GetFontDirectory(gamePath), fileName);
        Directory.CreateDirectory(Path.GetDirectoryName(destPath)!);
        File.Copy(fontFile, destPath, overwrite: true);

        logger.LogInformation("已安装 TMP 字体: {Source} → {Dest}", fileName, destPath);
        return $"BepInEx/Font/{fileName}";
    }

    /// <summary>
    /// Install a custom font file (e.g. from font generator) to BepInEx/Font/.
    /// Returns the config value.
    /// </summary>
    public static string InstallCustomFont(string gamePath, string srcPath, string destFileName)
    {
        destFileName = Path.GetFileName(destFileName);
        var destPath = Path.Combine(GetFontDirectory(gamePath), destFileName);
        Directory.CreateDirectory(Path.GetDirectoryName(destPath)!);
        File.Copy(srcPath, destPath, overwrite: true);
        return $"BepInEx/Font/{destFileName}";
    }

    /// <summary>
    /// Remove all TMP font files from a game's BepInEx/Font/ directory.
    /// </summary>
    public void RemoveFont(string gamePath)
    {
        var fontDir = GetFontDirectory(gamePath);
        if (!Directory.Exists(fontDir)) return;

        foreach (var file in Directory.GetFiles(fontDir))
        {
            File.Delete(file);
            logger.LogInformation("已删除 TMP 字体: {Path}", file);
        }
    }

    /// <summary>
    /// Find the best font file for a Unity version string (e.g. "2022.3.62f3").
    /// Exact major version match preferred; falls back to nearest boundary.
    /// </summary>
    public string? ResolveFontFile(string unityVersion)
    {
        var available = GetAvailableFonts();
        if (available.Count == 0)
            return null;

        var gameMajor = ParseMajorVersion(unityVersion);
        if (gameMajor is null)
        {
            logger.LogWarning("无法解析 Unity 版本号: {Version}", unityVersion);
            return null;
        }

        // Exact match
        if (available.TryGetValue(gameMajor.Value, out var exactPath))
            return exactPath;

        // Clamp to available range
        var sorted = available.Keys.OrderBy(k => k).ToList();
        var minVersion = sorted[0];
        var maxVersion = sorted[^1];

        if (gameMajor.Value < minVersion)
            return available[minVersion];

        if (gameMajor.Value > maxVersion)
            return available[maxVersion];

        // Between two versions: use the nearest lower version
        var lower = sorted.Where(v => v < gameMajor.Value).Max();
        return available[lower];
    }

    /// <summary>
    /// Get all available font files mapped by Unity major version.
    /// </summary>
    private SortedDictionary<int, string> GetAvailableFonts()
    {
        var fontsDir = bundledPaths.FontsDirectory;
        var result = new SortedDictionary<int, string>();

        if (!Directory.Exists(fontsDir))
            return result;

        foreach (var file in Directory.GetFiles(fontsDir))
        {
            var match = FontVersionRegex().Match(Path.GetFileName(file));
            if (match.Success && int.TryParse(match.Groups[1].Value, out var version))
                result[version] = file;
        }

        return result;
    }

    /// <summary>
    /// Extract major version year from Unity version string.
    /// "2022.3.62f3" → 2022, "6000.2.10f1" → 6000
    /// </summary>
    private static int? ParseMajorVersion(string unityVersion)
    {
        var dotIndex = unityVersion.IndexOf('.');
        var segment = dotIndex > 0 ? unityVersion[..dotIndex] : unityVersion;
        return int.TryParse(segment, out var major) ? major : null;
    }

    [GeneratedRegex(@"_U(\d+)$")]
    private static partial Regex FontVersionRegex();
}
