using System.IO.Compression;
using XUnityToolkit_WebUI.Infrastructure;
using XUnityToolkit_WebUI.Models;

namespace XUnityToolkit_WebUI.Services;

public sealed class PluginPackageService(ILogger<PluginPackageService> logger)
{
    private const string ConfigRelativePath = "BepInEx/config/AutoTranslatorConfig.ini";

    private static readonly HashSet<string> SensitiveSections = new(StringComparer.OrdinalIgnoreCase)
    {
        "GoogleLegitimate", "GoogleTranslateV2",
        "BingLegitimate", "BingTranslate",
        "Baidu", "BaiduTranslate",
        "Yandex", "YandexTranslate",
        "DeepLLegitimate", "DeepLTranslate",
        "PapagoTranslate", "LingoCloud",
        "Watson", "Custom", "LecPowerTranslator15", "ezTrans"
    };

    private static readonly string[] RootFiles = ["winhttp.dll", "doorstop_config.ini", ".doorstop_version", "dobby.dll"];
    private static readonly string[] RootDirectories = ["dotnet"];

    /// <summary>
    /// Export all BepInEx plugin files and translations as a ZIP package.
    /// </summary>
    public Task<(MemoryStream Stream, string FileName)> ExportAsync(Game game, CancellationToken ct = default)
    {
        var gamePath = game.GamePath;
        var bepInExDir = Path.Combine(gamePath, "BepInEx");
        if (!Directory.Exists(bepInExDir))
            throw new DirectoryNotFoundException("BepInEx 目录不存在，请先安装插件。");

        var memStream = new MemoryStream();
        try
        {
            var fileCount = 0;
            string targetLang = "zh";

            // Read target language from config before creating ZIP
            var configPath = Path.Combine(gamePath, ConfigRelativePath.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(configPath))
            {
                var iniContent = File.ReadAllText(configPath);
                targetLang = ReadTargetLanguage(iniContent) ?? "zh";
            }

            using (var archive = new ZipArchive(memStream, ZipArchiveMode.Create, leaveOpen: true))
            {
                // Add root-level files
                foreach (var rootFile in RootFiles)
                {
                    ct.ThrowIfCancellationRequested();
                    var filePath = Path.Combine(gamePath, rootFile);
                    if (File.Exists(filePath))
                    {
                        AddFileToZip(archive, gamePath, rootFile);
                        fileCount++;
                    }
                }

                // Add root-level directories (e.g. dotnet/)
                foreach (var dirName in RootDirectories)
                {
                    ct.ThrowIfCancellationRequested();
                    var dirPath = Path.Combine(gamePath, dirName);
                    if (Directory.Exists(dirPath))
                        fileCount += AddDirectoryToZip(archive, gamePath, dirName, ct);
                }

                // Add BepInEx directory
                fileCount += AddDirectoryToZip(archive, gamePath, "BepInEx", ct);
            }

            memStream.Position = 0;

            var safeName = SanitizeFileName(game.Name);
            var fileName = $"{safeName}_{targetLang}_{DateTime.Now:yyyy-MM-dd}.zip";

            logger.LogInformation("汉化包生成完成: {FileName}，共 {Count} 个文件", fileName, fileCount);
            return Task.FromResult((memStream, fileName));
        }
        catch
        {
            memStream.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Import a ZIP package into the game directory, extracting all files with overwrite.
    /// </summary>
    public Task ImportAsync(Game game, string zipFilePath, CancellationToken ct = default)
    {
        logger.LogInformation("正在导入汉化包: {ZipPath} → {GamePath}", zipFilePath, game.GamePath);

        using var archive = ZipFile.OpenRead(zipFilePath);
        var fileCount = 0;

        foreach (var entry in archive.Entries)
        {
            ct.ThrowIfCancellationRequested();

            if (string.IsNullOrEmpty(entry.Name))
                continue; // Skip directories

            var destPath = PathSecurity.SafeJoin(game.GamePath, entry.FullName);
            var destDir = Path.GetDirectoryName(destPath)!;
            Directory.CreateDirectory(destDir);

            entry.ExtractToFile(destPath, overwrite: true);
            fileCount++;
        }

        logger.LogInformation("汉化包导入完成，共 {Count} 个文件", fileCount);
        return Task.CompletedTask;
    }

    private void AddFileToZip(ZipArchive archive, string gamePath, string relativePath)
    {
        var entryName = relativePath.Replace('\\', '/');
        var filePath = Path.Combine(gamePath, relativePath);

        var entry = archive.CreateEntry(entryName, CompressionLevel.SmallestSize);

        // Special handling for AutoTranslatorConfig.ini — sanitize sensitive data
        if (string.Equals(entryName, ConfigRelativePath, StringComparison.OrdinalIgnoreCase))
        {
            var content = File.ReadAllText(filePath);
            var sanitized = SanitizeIniContent(content);
            using var writer = new StreamWriter(entry.Open());
            writer.Write(sanitized);
            return;
        }

        using var source = File.OpenRead(filePath);
        using var dest = entry.Open();
        source.CopyTo(dest);
    }

    private int AddDirectoryToZip(ZipArchive archive, string gamePath, string dirRelative, CancellationToken ct)
    {
        var dirPath = Path.Combine(gamePath, dirRelative);
        if (!Directory.Exists(dirPath))
            return 0;

        var count = 0;
        foreach (var filePath in Directory.EnumerateFiles(dirPath, "*", SearchOption.AllDirectories))
        {
            ct.ThrowIfCancellationRequested();

            var relativePath = Path.GetRelativePath(gamePath, filePath);
            var entryName = relativePath.Replace('\\', '/');

            // Exclusion rules
            if (ShouldExclude(entryName))
                continue;

            AddFileToZip(archive, gamePath, relativePath);
            count++;
        }

        return count;
    }

    private static bool ShouldExclude(string entryName)
    {
        // Exclude BepInEx/LogOutput.log
        if (string.Equals(entryName, "BepInEx/LogOutput.log", StringComparison.OrdinalIgnoreCase))
            return true;

        // Exclude BepInEx/cache/ directory
        if (entryName.StartsWith("BepInEx/cache/", StringComparison.OrdinalIgnoreCase))
            return true;

        // Exclude all .log files
        if (entryName.EndsWith(".log", StringComparison.OrdinalIgnoreCase))
            return true;

        return false;
    }

    internal static string SanitizeIniContent(string content)
    {
        var useCrLf = content.Contains("\r\n");
        var lines = content.Split('\n');
        var result = new List<string>(lines.Length);
        string? currentSection = null;

        foreach (var rawLine in lines)
        {
            var line = rawLine.TrimEnd('\r');
            var trimmed = line.Trim();

            // Section header
            if (trimmed.StartsWith('[') && trimmed.EndsWith(']'))
            {
                currentSection = trimmed[1..^1];
                result.Add(line);
                continue;
            }

            // Key=Value line in a sensitive section
            if (currentSection is not null && !string.IsNullOrEmpty(trimmed)
                && !trimmed.StartsWith('#') && !trimmed.StartsWith(';'))
            {
                var eqIndex = trimmed.IndexOf('=');
                if (eqIndex > 0)
                {
                    var key = trimmed[..eqIndex].Trim();

                    // Full section blanking for sensitive sections
                    if (SensitiveSections.Contains(currentSection))
                    {
                        result.Add($"{key}=");
                        continue;
                    }

                    // LLMTranslate: only blank GameId
                    if (string.Equals(currentSection, "LLMTranslate", StringComparison.OrdinalIgnoreCase)
                        && string.Equals(key, "GameId", StringComparison.OrdinalIgnoreCase))
                    {
                        result.Add($"{key}=");
                        continue;
                    }
                }
            }

            result.Add(line);
        }

        return string.Join(useCrLf ? "\r\n" : "\n", result);
    }

    private static string? ReadTargetLanguage(string iniContent)
    {
        string? currentSection = null;
        foreach (var rawLine in iniContent.Split('\n'))
        {
            var line = rawLine.Trim();
            if (line.StartsWith('[') && line.EndsWith(']'))
            {
                currentSection = line[1..^1];
                continue;
            }

            if (string.Equals(currentSection, "General", StringComparison.OrdinalIgnoreCase))
            {
                var eqIndex = line.IndexOf('=');
                if (eqIndex > 0)
                {
                    var key = line[..eqIndex].Trim();
                    if (string.Equals(key, "Language", StringComparison.OrdinalIgnoreCase))
                    {
                        var value = line[(eqIndex + 1)..].Trim();
                        return string.IsNullOrEmpty(value) ? null : value;
                    }
                }
            }
        }
        return null;
    }

    private static string SanitizeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sanitized = new char[name.Length];
        for (var i = 0; i < name.Length; i++)
            sanitized[i] = Array.IndexOf(invalid, name[i]) >= 0 ? '_' : name[i];
        return new string(sanitized);
    }
}
