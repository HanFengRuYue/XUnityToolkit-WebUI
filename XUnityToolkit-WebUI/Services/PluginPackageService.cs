using System.IO.Compression;
using XUnityToolkit_WebUI.Infrastructure;
using XUnityToolkit_WebUI.Models;

namespace XUnityToolkit_WebUI.Services;

public sealed class PluginPackageService(ILogger<PluginPackageService> logger, AppDataPaths appDataPaths)
{
    private const string ConfigRelativePath = "BepInEx/config/AutoTranslatorConfig.ini";

    private static readonly HashSet<string> SensitiveSections = new(StringComparer.OrdinalIgnoreCase)
    {
        "GoogleLegitimate", "BingLegitimate", "Baidu", "Yandex", "DeepLLegitimate",
        "PapagoTranslate", "LingoCloud", "Watson", "Custom", "LecPowerTranslator15", "ezTrans"
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

                // Add font-replaced asset files if present
                var fontManifestPath = Path.Combine(appDataPaths.GetFontBackupDirectory(game.Id), "manifest.json");
                if (File.Exists(fontManifestPath))
                {
                    var fontManifest = System.Text.Json.JsonSerializer.Deserialize<FontBackupManifest>(
                        File.ReadAllText(fontManifestPath));
                    if (fontManifest is not null)
                    {
                        foreach (var entry in fontManifest.ReplacedFiles)
                        {
                            if (File.Exists(entry.OriginalPath))
                            {
                                var relativePath = Path.GetRelativePath(gamePath, entry.OriginalPath);
                                var entryName = relativePath.Replace('\\', '/');
                                var zipEntry = archive.CreateEntry(entryName, CompressionLevel.SmallestSize);
                                using var src = File.OpenRead(entry.OriginalPath);
                                using var dst = zipEntry.Open();
                                src.CopyTo(dst);
                                fileCount++;
                            }
                        }
                        foreach (var entry in fontManifest.CatalogFiles)
                        {
                            if (File.Exists(entry.OriginalPath))
                            {
                                var relativePath = Path.GetRelativePath(gamePath, entry.OriginalPath);
                                var entryName = relativePath.Replace('\\', '/');
                                var zipEntry = archive.CreateEntry(entryName, CompressionLevel.SmallestSize);
                                using var src = File.OpenRead(entry.OriginalPath);
                                using var dst = zipEntry.Open();
                                src.CopyTo(dst);
                                fileCount++;
                            }
                        }
                        // Add manifest for import identification
                        var manifestEntry = archive.CreateEntry("_font_replacement_manifest.json", CompressionLevel.SmallestSize);
                        using var manifestSrc = File.OpenRead(fontManifestPath);
                        using var manifestDst = manifestEntry.Open();
                        manifestSrc.CopyTo(manifestDst);
                        fileCount++;
                    }
                }
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

        // Phase 1: Check for font replacement manifest and backup originals FIRST
        var fontManifestEntry = archive.GetEntry("_font_replacement_manifest.json");
        if (fontManifestEntry is not null)
        {
            using var manifestStream = fontManifestEntry.Open();
            using var reader = new StreamReader(manifestStream);
            var manifestJson = reader.ReadToEnd();
            var fontManifest = System.Text.Json.JsonSerializer.Deserialize<FontBackupManifest>(manifestJson);

            if (fontManifest is not null)
            {
                var backupDir = appDataPaths.GetFontBackupDirectory(game.Id);
                Directory.CreateDirectory(backupDir);

                // Collect all asset file paths from the manifest that will be overwritten
                var filesToBackup = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var entry in fontManifest.ReplacedFiles)
                    filesToBackup.Add(entry.BackupFileName);
                foreach (var entry in fontManifest.CatalogFiles)
                    filesToBackup.Add(entry.BackupFileName);

                // Backup originals before they get overwritten
                foreach (var zipEntry in archive.Entries)
                {
                    if (string.IsNullOrEmpty(zipEntry.Name)) continue;
                    var destPath = PathSecurity.SafeJoin(game.GamePath, zipEntry.FullName);
                    if (!File.Exists(destPath)) continue;

                    var relativePath = Path.GetRelativePath(game.GamePath, destPath);
                    var backupFileName = relativePath
                        .Replace(Path.DirectorySeparatorChar, '_')
                        .Replace(Path.AltDirectorySeparatorChar, '_');

                    if (filesToBackup.Contains(backupFileName))
                    {
                        var backupPath = Path.Combine(backupDir, backupFileName);
                        if (!File.Exists(backupPath))
                            File.Copy(destPath, backupPath);
                    }
                }

                // Save manifest with updated GameId
                var newManifest = fontManifest with { GameId = game.Id };

                // Update OriginalPath values to point to the importing game's directory
                var updatedReplacedFiles = newManifest.ReplacedFiles.Select(f =>
                {
                    // Reconstruct path from backup filename: replace _ back to directory separator
                    // BackupFileName format: "GameName_Data_sharedassets0.assets"
                    // We need to find the matching ZIP entry to get the correct relative path
                    var relativePath = f.BackupFileName.Replace('_', Path.DirectorySeparatorChar);
                    // Try to find a better path from ZIP entries
                    foreach (var ze in archive.Entries)
                    {
                        if (string.IsNullOrEmpty(ze.Name)) continue;
                        var zePath = ze.FullName.Replace('/', Path.DirectorySeparatorChar);
                        var zeBackupName = zePath.Replace(Path.DirectorySeparatorChar, '_');
                        if (zeBackupName == f.BackupFileName)
                        {
                            relativePath = zePath;
                            break;
                        }
                    }
                    var fullPath = PathSecurity.SafeJoin(game.GamePath, relativePath);
                    return f with { OriginalPath = fullPath };
                }).ToList();

                var updatedCatalogFiles = newManifest.CatalogFiles.Select(c =>
                {
                    var relativePath = c.BackupFileName.Replace('_', Path.DirectorySeparatorChar);
                    foreach (var ze in archive.Entries)
                    {
                        if (string.IsNullOrEmpty(ze.Name)) continue;
                        var zePath = ze.FullName.Replace('/', Path.DirectorySeparatorChar);
                        var zeBackupName = zePath.Replace(Path.DirectorySeparatorChar, '_');
                        if (zeBackupName == c.BackupFileName)
                        {
                            relativePath = zePath;
                            break;
                        }
                    }
                    var fullPath = PathSecurity.SafeJoin(game.GamePath, relativePath);
                    return c with { OriginalPath = fullPath };
                }).ToList();

                newManifest = newManifest with
                {
                    ReplacedFiles = updatedReplacedFiles,
                    CatalogFiles = updatedCatalogFiles
                };

                var newManifestJson = System.Text.Json.JsonSerializer.Serialize(newManifest);
                var manifestPath = Path.Combine(backupDir, "manifest.json");
                var tmpManifest = manifestPath + ".tmp";
                File.WriteAllText(tmpManifest, newManifestJson);
                File.Move(tmpManifest, manifestPath, overwrite: true);
            }
        }

        // Phase 2: Extract all files
        var fileCount = 0;
        foreach (var entry in archive.Entries)
        {
            ct.ThrowIfCancellationRequested();
            if (string.IsNullOrEmpty(entry.Name)) continue;
            if (entry.FullName == "_font_replacement_manifest.json") continue;

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
