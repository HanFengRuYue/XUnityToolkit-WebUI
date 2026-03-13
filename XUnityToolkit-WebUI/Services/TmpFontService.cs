using System.Text.RegularExpressions;
using XUnityToolkit_WebUI.Infrastructure;
using XUnityToolkit_WebUI.Models;

namespace XUnityToolkit_WebUI.Services;

public sealed partial class TmpFontService(BundledAssetPaths bundledPaths, ILogger<TmpFontService> logger)
{
    private const string FontFileName = "SourceHanSans";
    public const string ConfigValue = "BepInEx/Font/SourceHanSans";

    private static string GetInstalledFontPath(string gamePath) =>
        Path.Combine(gamePath, "BepInEx", "Font", FontFileName);

    public bool IsFontInstalled(string gamePath) =>
        File.Exists(GetInstalledFontPath(gamePath));

    /// <summary>
    /// Install the best-matching TMP font for the given Unity version.
    /// Returns true if the font was installed, false if no font files available.
    /// </summary>
    public bool InstallFont(string gamePath, UnityGameInfo gameInfo)
    {
        var fontFile = ResolveFontFile(gameInfo.UnityVersion);
        if (fontFile is null)
        {
            logger.LogWarning("未找到可用的 TMP 字体文件");
            return false;
        }

        var destPath = GetInstalledFontPath(gamePath);
        Directory.CreateDirectory(Path.GetDirectoryName(destPath)!);
        File.Copy(fontFile, destPath, overwrite: true);

        logger.LogInformation("已安装 TMP 字体: {Source} → {Dest}", Path.GetFileName(fontFile), destPath);
        return true;
    }

    /// <summary>
    /// Remove the installed TMP font from a game directory.
    /// </summary>
    public void RemoveFont(string gamePath)
    {
        var path = GetInstalledFontPath(gamePath);
        if (File.Exists(path))
        {
            File.Delete(path);
            logger.LogInformation("已删除 TMP 字体: {Path}", path);
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
        if (dotIndex <= 0)
            return null;

        return int.TryParse(unityVersion[..dotIndex], out var major) ? major : null;
    }

    [GeneratedRegex(@"_U(\d+)$")]
    private static partial Regex FontVersionRegex();
}
