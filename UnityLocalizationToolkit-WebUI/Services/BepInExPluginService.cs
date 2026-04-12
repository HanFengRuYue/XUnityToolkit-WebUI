using System.IO.Compression;
using System.Reflection;
using System.Runtime.InteropServices;
using UnityLocalizationToolkit_WebUI.Infrastructure;
using UnityLocalizationToolkit_WebUI.Models;

namespace UnityLocalizationToolkit_WebUI.Services;

public sealed class BepInExPluginService(ILogger<BepInExPluginService> logger)
{
    private const long MaxPluginPackageBytes = 50L * 1024 * 1024;
    private const long MaxPluginArchiveEntryBytes = 64L * 1024 * 1024;
    private const long MaxPluginArchiveTotalBytes = 256L * 1024 * 1024;

    private static readonly HashSet<string> ToolkitManagedPatterns = new(StringComparer.OrdinalIgnoreCase)
    {
        "XUnity.AutoTranslator",
        "XUnity.Common",
        "XUnity.ResourceRedirector",
        "LLMTranslate",
    };

    private static readonly Lazy<string[]> RuntimeDlls = new(() =>
        Directory.GetFiles(RuntimeEnvironment.GetRuntimeDirectory(), "*.dll"));

    public Task<List<BepInExPlugin>> ListPluginsAsync(Game game)
    {
        var pluginsDir = Path.Combine(game.GamePath, "BepInEx", "plugins");
        if (!Directory.Exists(pluginsDir))
            return Task.FromResult(new List<BepInExPlugin>());

        var configDir = Path.Combine(game.GamePath, "BepInEx", "config");
        var configFiles = Directory.Exists(configDir)
            ? Directory.GetFiles(configDir, "*.cfg").Select(Path.GetFileName).Where(n => n is not null).ToHashSet(StringComparer.OrdinalIgnoreCase)!
            : new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var plugins = new List<BepInExPlugin>();
        var dllFiles = Directory.EnumerateFiles(pluginsDir, "*.dll", SearchOption.AllDirectories)
            .Concat(Directory.EnumerateFiles(pluginsDir, "*.dll.disabled", SearchOption.AllDirectories));

        foreach (var fullPath in dllFiles)
        {
            var relativePath = Path.GetRelativePath(pluginsDir, fullPath);
            var fileName = Path.GetFileName(fullPath);
            var enabled = !fileName.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase);
            var isToolkitManaged = IsToolkitManaged(fileName);

            var (guid, name, version) = TryReadPluginMetadata(fullPath);

            // Match config file by GUID (BepInEx convention: {GUID}.cfg)
            string? configFileName = null;
            if (guid is not null)
            {
                var cfgName = $"{guid}.cfg";
                if (configFiles.Contains(cfgName))
                    configFileName = cfgName;
            }

            plugins.Add(new BepInExPlugin
            {
                FileName = fileName,
                RelativePath = relativePath.Replace('\\', '/'),
                Enabled = enabled,
                FileSize = new FileInfo(fullPath).Length,
                PluginGuid = guid,
                PluginName = name,
                PluginVersion = version,
                IsToolkitManaged = isToolkitManaged,
                ConfigFileName = configFileName,
            });
        }

        plugins.Sort((a, b) =>
        {
            // Toolkit-managed first, then alphabetical
            var cmp = a.IsToolkitManaged.CompareTo(b.IsToolkitManaged);
            if (cmp != 0) return -cmp; // true first
            return string.Compare(a.PluginName ?? a.FileName, b.PluginName ?? b.FileName, StringComparison.OrdinalIgnoreCase);
        });

        return Task.FromResult(plugins);
    }

    public async Task InstallPluginAsync(Game game, string filePath)
    {
        var pluginsDir = Path.Combine(game.GamePath, "BepInEx", "plugins");
        Directory.CreateDirectory(pluginsDir);

        if (filePath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
        {
            EnsurePackageSizeWithinLimit(new FileInfo(filePath).Length);

            var zipName = Path.GetFileNameWithoutExtension(filePath);
            var targetDir = Path.Combine(pluginsDir, zipName);
            Directory.CreateDirectory(targetDir);

            using var archive = ZipFile.OpenRead(filePath);
            var extractedCount = await ExtractPluginArchiveAsync(archive, targetDir);

            logger.LogInformation("已从 ZIP 安装插件到 {Dir}，共 {Count} 个文件",
                targetDir, extractedCount);
        }
        else if (filePath.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
        {
            var fileName = Path.GetFileName(filePath);
            var destPath = Path.Combine(pluginsDir, fileName);
            File.Copy(filePath, destPath, overwrite: true);

            logger.LogInformation("已安装插件: {FileName}", fileName);
        }
        else
        {
            throw new InvalidOperationException("仅支持 .dll 和 .zip 格式");
        }

    }

    public async Task UploadPluginAsync(Game game, Stream fileStream, string fileName, long fileSize)
    {
        var pluginsDir = Path.Combine(game.GamePath, "BepInEx", "plugins");
        Directory.CreateDirectory(pluginsDir);

        var safeFileName = Path.GetFileName(fileName);

        if (safeFileName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
        {
            EnsurePackageSizeWithinLimit(fileSize);

            // Save to temp, then extract
            var tempPath = Path.Combine(Path.GetTempPath(), $"bepinex_upload_{Guid.NewGuid():N}.zip");
            try
            {
                await using (var fs = File.Create(tempPath))
                    await fileStream.CopyToAsync(fs);

                var zipName = Path.GetFileNameWithoutExtension(safeFileName);
                var targetDir = Path.Combine(pluginsDir, zipName);
                Directory.CreateDirectory(targetDir);

                using var archive = ZipFile.OpenRead(tempPath);
                await ExtractPluginArchiveAsync(archive, targetDir);

                logger.LogInformation("已从上传 ZIP 安装插件到 {Dir}", targetDir);
            }
            finally
            {
                try { File.Delete(tempPath); } catch { /* best effort */ }
            }
        }
        else if (safeFileName.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
        {
            var destPath = Path.Combine(pluginsDir, safeFileName);
            await using var fs = File.Create(destPath);
            await fileStream.CopyToAsync(fs);

            logger.LogInformation("已上传安装插件: {FileName}", safeFileName);
        }
        else
        {
            throw new InvalidOperationException("仅支持 .dll 和 .zip 格式");
        }

    }

    private static void EnsurePackageSizeWithinLimit(long fileSize)
    {
        if (fileSize > MaxPluginPackageBytes)
            throw new InvalidOperationException("插件包大小不能超过 50MB");
    }

    private static async Task<int> ExtractPluginArchiveAsync(ZipArchive archive, string targetDir)
    {
        long totalExtractedBytes = 0;
        var extractedCount = 0;

        foreach (var entry in archive.Entries)
        {
            if (string.IsNullOrEmpty(entry.Name))
                continue;

            var destinationPath = PathSecurity.PrepareZipExtractionPath(
                targetDir,
                entry,
                ref totalExtractedBytes,
                MaxPluginArchiveEntryBytes,
                MaxPluginArchiveTotalBytes);
            await PathSecurity.ExtractZipEntryAsync(entry, destinationPath);
            extractedCount++;
        }

        return extractedCount;
    }

    public Task UninstallPluginAsync(Game game, string relativePath)
    {
        var pluginsDir = Path.Combine(game.GamePath, "BepInEx", "plugins");
        var fullPath = PathSecurity.SafeJoin(pluginsDir, relativePath);

        if (!File.Exists(fullPath))
            throw new FileNotFoundException("插件文件不存在");

        var fileName = Path.GetFileName(fullPath);
        if (IsToolkitManaged(fileName))
            throw new InvalidOperationException("无法卸载工具箱管理的插件");

        // Check if file is in a subdirectory — if so, try to remove the whole folder
        var parentDir = Path.GetDirectoryName(fullPath)!;
        var normalizedPluginsDir = Path.GetFullPath(pluginsDir);
        var normalizedParent = Path.GetFullPath(parentDir);

        if (!string.Equals(normalizedParent, normalizedPluginsDir, StringComparison.OrdinalIgnoreCase))
        {
            // Plugin is in a subfolder — check if it only contains this plugin's files
            var dllCount = Directory.GetFiles(parentDir, "*.dll", SearchOption.AllDirectories).Length
                         + Directory.GetFiles(parentDir, "*.dll.disabled", SearchOption.AllDirectories).Length;

            if (dllCount <= 1)
            {
                // Single-plugin subfolder — remove the whole directory
                Directory.Delete(parentDir, recursive: true);
                logger.LogInformation("已卸载插件目录: {Dir}", Path.GetRelativePath(pluginsDir, parentDir));
                return Task.CompletedTask;
            }
        }

        // Otherwise just delete the single file
        File.Delete(fullPath);
        logger.LogInformation("已卸载插件: {FileName}", fileName);
        return Task.CompletedTask;
    }

    public Task<BepInExPlugin> TogglePluginAsync(Game game, string relativePath)
    {
        var pluginsDir = Path.Combine(game.GamePath, "BepInEx", "plugins");
        var fullPath = PathSecurity.SafeJoin(pluginsDir, relativePath);

        if (!File.Exists(fullPath))
            throw new FileNotFoundException("插件文件不存在");

        var fileName = Path.GetFileName(fullPath);
        if (IsToolkitManaged(fileName))
            throw new InvalidOperationException("无法切换工具箱管理的插件");

        string newPath;
        bool newEnabled;
        if (fullPath.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase))
        {
            // Enable: remove .disabled suffix
            newPath = fullPath[..^".disabled".Length];
            newEnabled = true;
        }
        else
        {
            // Disable: add .disabled suffix
            newPath = fullPath + ".disabled";
            newEnabled = false;
        }

        File.Move(fullPath, newPath, overwrite: false);

        var newRelativePath = Path.GetRelativePath(pluginsDir, newPath).Replace('\\', '/');
        var newFileName = Path.GetFileName(newPath);

        logger.LogInformation("插件 {FileName} 已{Action}", fileName, newEnabled ? "启用" : "禁用");

        var (guid, name, version) = TryReadPluginMetadata(newPath);

        // Resolve config file name from GUID
        string? configFileName = null;
        if (guid is not null)
        {
            var configDir = Path.Combine(game.GamePath, "BepInEx", "config");
            var cfgPath = Path.Combine(configDir, $"{guid}.cfg");
            if (File.Exists(cfgPath))
                configFileName = $"{guid}.cfg";
        }

        return Task.FromResult(new BepInExPlugin
        {
            FileName = newFileName,
            RelativePath = newRelativePath,
            Enabled = newEnabled,
            FileSize = new FileInfo(newPath).Length,
            PluginGuid = guid,
            PluginName = name,
            PluginVersion = version,
            IsToolkitManaged = false,
            ConfigFileName = configFileName,
        });
    }

    public Task<string?> GetConfigAsync(Game game, string configFileName)
    {
        var safeFileName = Path.GetFileName(configFileName);
        var configDir = Path.Combine(game.GamePath, "BepInEx", "config");
        var configPath = PathSecurity.SafeJoin(configDir, safeFileName);

        if (!File.Exists(configPath))
            return Task.FromResult<string?>(null);

        return Task.FromResult<string?>(File.ReadAllText(configPath));
    }

    private static bool IsToolkitManaged(string fileName)
    {
        var nameWithoutExt = fileName;
        if (nameWithoutExt.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase))
            nameWithoutExt = nameWithoutExt[..^".disabled".Length];
        if (nameWithoutExt.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
            nameWithoutExt = nameWithoutExt[..^".dll".Length];

        return ToolkitManagedPatterns.Any(p =>
            nameWithoutExt.StartsWith(p, StringComparison.OrdinalIgnoreCase));
    }

    private static (string? Guid, string? Name, string? Version) TryReadPluginMetadata(string dllPath)
    {
        try
        {
            var actualPath = dllPath;
            // For .disabled files, we still read metadata from the file
            if (!File.Exists(actualPath))
                return (null, null, null);

            // Use MetadataLoadContext for safe read-only reflection
            var resolver = new PathAssemblyResolver(RuntimeDlls.Value.Append(actualPath));

            using var mlc = new MetadataLoadContext(resolver);
            var assembly = mlc.LoadFromAssemblyPath(actualPath);

            foreach (var type in assembly.GetTypes())
            {
                foreach (var attr in type.GetCustomAttributesData())
                {
                    if (attr.AttributeType.Name is "BepInPlugin" or "BepInPluginAttribute"
                        && attr.ConstructorArguments.Count >= 3)
                    {
                        var guid = attr.ConstructorArguments[0].Value as string;
                        var name = attr.ConstructorArguments[1].Value as string;
                        var version = attr.ConstructorArguments[2].Value as string;
                        return (guid, name, version);
                    }
                }
            }
        }
        catch
        {
            // Native DLLs, corrupted files, etc. — graceful degradation
        }

        return (null, null, null);
    }
}
