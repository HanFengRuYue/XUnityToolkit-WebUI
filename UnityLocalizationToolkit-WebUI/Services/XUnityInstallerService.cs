using System.IO.Compression;
using System.Reflection;
using UnityLocalizationToolkit_WebUI.Infrastructure;
using UnityLocalizationToolkit_WebUI.Models;

namespace UnityLocalizationToolkit_WebUI.Services;

public sealed class XUnityInstallerService(ILogger<XUnityInstallerService> logger)
{
    private const string EndpointDllResourceName = "LLMTranslate.dll";

    private static string GetTranslatorEndpointPath(string gamePath) =>
        Path.Combine(gamePath, "BepInEx", "plugins", "XUnity.AutoTranslator", "Translators", EndpointDllResourceName);

    /// <summary>
    /// Deploy the LLMTranslate.dll translator endpoint to the game's Translators directory.
    /// Returns true if the DLL was deployed, false if not available.
    /// Skips if already deployed (preserves user-customized files).
    /// </summary>
    public bool DeployTranslatorEndpoint(string gamePath)
    {
        var destPath = GetTranslatorEndpointPath(gamePath);

        // Skip if already deployed (don't overwrite user-customized files)
        if (File.Exists(destPath))
        {
            logger.LogInformation("翻译端点 DLL 已存在，跳过部署: {Path}", destPath);
            return true;
        }

        return ExtractEndpointDll(destPath);
    }

    /// <summary>
    /// Check if the LLMTranslate.dll is installed for a specific game.
    /// </summary>
    public bool IsTranslatorEndpointInstalled(string gamePath) =>
        File.Exists(GetTranslatorEndpointPath(gamePath));

    /// <summary>
    /// Force-deploy the LLMTranslate.dll (overwrites existing).
    /// Used when user explicitly requests install from game detail page.
    /// </summary>
    public bool ForceDeployTranslatorEndpoint(string gamePath) =>
        ExtractEndpointDll(GetTranslatorEndpointPath(gamePath));

    /// <summary>
    /// Remove the LLMTranslate.dll from a game's Translators directory.
    /// </summary>
    public void RemoveTranslatorEndpoint(string gamePath)
    {
        var path = GetTranslatorEndpointPath(gamePath);
        if (File.Exists(path))
        {
            File.Delete(path);
            logger.LogInformation("已删除 AI 翻译端点 DLL: {Path}", path);
        }
    }

    private bool ExtractEndpointDll(string destPath)
    {
        var assembly = Assembly.GetExecutingAssembly();
        using var stream = assembly.GetManifestResourceStream(EndpointDllResourceName);
        if (stream is null)
        {
            logger.LogInformation("LLMTranslate.dll 嵌入资源不可用，跳过翻译端点部署");
            return false;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(destPath)!);
        using var fs = new FileStream(destPath, FileMode.Create, FileAccess.Write);
        stream.CopyTo(fs);

        logger.LogInformation("已部署翻译端点 DLL: {Path}", destPath);
        return true;
    }

    /// <summary>
    /// Resolves the bundled XUnity ZIP for the given game info.
    /// </summary>
    public string ResolveBundledZip(UnityGameInfo info, BundledAssetPaths bundled)
    {
        var isIL2CPP = info.Backend == UnityBackend.IL2CPP;
        var zips = bundled.GetXUnityZips();
        var zip = zips.FirstOrDefault(z =>
        {
            var name = Path.GetFileName(z);
            if (!name.StartsWith("XUnity.AutoTranslator-BepInEx", StringComparison.OrdinalIgnoreCase))
                return false;
            var hasIL2CPP = name.Contains("IL2CPP", StringComparison.OrdinalIgnoreCase);
            return isIL2CPP == hasIL2CPP;
        });

        return zip ?? throw new InvalidOperationException(
            $"未找到捆绑的 XUnity ZIP（{(isIL2CPP ? "IL2CPP" : "Mono")}）。请重新构建发布版本。");
    }

    /// <summary>Parses version string from bundled ZIP filename.</summary>
    public static string ParseVersionFromZip(string zipPath)
    {
        // XUnity.AutoTranslator-BepInEx-5.3.0.zip or XUnity.AutoTranslator-BepInEx-IL2CPP-5.3.0.zip
        var name = Path.GetFileNameWithoutExtension(zipPath);
        var lastDash = name.LastIndexOf('-');
        return lastDash >= 0 ? $"v{name[(lastDash + 1)..]}" : "v?.?";
    }

    public Task<List<string>> InstallAsync(string gamePath, string zipPath, CancellationToken ct = default)
    {
        logger.LogInformation("正在安装 XUnity.AutoTranslator: {Zip}", zipPath);

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

        logger.LogInformation("XUnity.AutoTranslator 安装完成，共 {Count} 个文件", installedFiles.Count);
        return Task.FromResult(installedFiles);
    }

    public Task UninstallAsync(string gamePath, CancellationToken ct = default)
    {
        logger.LogInformation("正在卸载 XUnity.AutoTranslator: {Game}", gamePath);

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

        // Remove XUnity config (actual file name is AutoTranslatorConfig.ini, not the BepInEx GUID style)
        var configDir = Path.Combine(gamePath, "BepInEx", "config");
        foreach (var cfgName in new[] { "AutoTranslatorConfig.ini", "gravydevsupreme.xunity.autotranslator.cfg" })
        {
            var cfgPath = Path.Combine(configDir, cfgName);
            if (File.Exists(cfgPath))
                File.Delete(cfgPath);
        }

        // Remove translation output
        var translationDir = Path.Combine(gamePath, "BepInEx", "Translation");
        if (Directory.Exists(translationDir))
            Directory.Delete(translationDir, recursive: true);

        return Task.CompletedTask;
    }
}
