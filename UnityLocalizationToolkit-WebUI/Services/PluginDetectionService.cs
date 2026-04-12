using System.Diagnostics;
using UnityLocalizationToolkit_WebUI.Models;

namespace UnityLocalizationToolkit_WebUI.Services;

public sealed class PluginDetectionService(ILogger<PluginDetectionService> logger)
{
    public Task<List<DetectedModFramework>> DetectAsync(string gamePath)
    {
        var frameworks = new List<DetectedModFramework>();

        DetectBepInEx(gamePath, frameworks);
        DetectMelonLoader(gamePath, frameworks);
        DetectIPA(gamePath, frameworks);
        DetectReiPatcher(gamePath, frameworks);
        DetectSybaris(gamePath, frameworks);
        DetectUnityInjector(gamePath, frameworks);
        DetectStandalone(gamePath, frameworks);

        if (frameworks.Count > 0)
        {
            logger.LogInformation("检测到 {Count} 个模组框架: {Frameworks}",
                frameworks.Count,
                string.Join(", ", frameworks.Select(f =>
                    f.HasXUnityPlugin ? $"{f.Framework}(含XUnity {f.XUnityVersion})" : f.Framework.ToString())));
        }

        return Task.FromResult(frameworks);
    }

    public Task UninstallFrameworkAsync(string gamePath, ModFrameworkType framework)
    {
        logger.LogInformation("正在卸载框架 {Framework}: {Game}", framework, gamePath);

        switch (framework)
        {
            case ModFrameworkType.MelonLoader:
                UninstallMelonLoader(gamePath);
                break;
            case ModFrameworkType.IPA:
                UninstallIPA(gamePath);
                break;
            case ModFrameworkType.ReiPatcher:
                UninstallReiPatcher(gamePath);
                break;
            case ModFrameworkType.Sybaris:
                UninstallSybaris(gamePath);
                break;
            case ModFrameworkType.UnityInjector:
                UninstallUnityInjector(gamePath);
                break;
            case ModFrameworkType.Standalone:
                UninstallStandalone(gamePath);
                break;
            case ModFrameworkType.BepInEx:
                throw new InvalidOperationException("BepInEx 请使用安装管理中的卸载功能。");
        }

        logger.LogInformation("框架 {Framework} 卸载完成", framework);
        return Task.CompletedTask;
    }

    // ===== Detection Methods =====

    private void DetectBepInEx(string gamePath, List<DetectedModFramework> frameworks)
    {
        var bepInExDir = Path.Combine(gamePath, "BepInEx");
        if (!Directory.Exists(bepInExDir)) return;

        var version = ReadDllVersion(Path.Combine(bepInExDir, "core", "BepInEx.dll"))
                   ?? ReadDllVersion(Path.Combine(bepInExDir, "core", "BepInEx.Core.dll"));

        var (hasXUnity, xunityVersion) = DetectXUnityInBepInEx(gamePath);

        frameworks.Add(new DetectedModFramework
        {
            Framework = ModFrameworkType.BepInEx,
            Version = version,
            HasXUnityPlugin = hasXUnity,
            XUnityVersion = xunityVersion
        });
    }

    private void DetectMelonLoader(string gamePath, List<DetectedModFramework> frameworks)
    {
        var melonDir = Path.Combine(gamePath, "MelonLoader");
        if (!Directory.Exists(melonDir)) return;

        var version = ReadDllVersion(Path.Combine(melonDir, "MelonLoader.dll"))
                   ?? ReadDllVersion(Path.Combine(melonDir, "net6", "MelonLoader.dll"))
                   ?? ReadDllVersion(Path.Combine(melonDir, "net35", "MelonLoader.dll"));

        var hasXUnity = FindXUnityDll(Path.Combine(gamePath, "Mods")) ||
                        FindXUnityDll(Path.Combine(gamePath, "Plugins"));
        var xunityVersion = hasXUnity
            ? ReadXUnityVersion(Path.Combine(gamePath, "Mods")) ?? ReadXUnityVersion(Path.Combine(gamePath, "Plugins"))
            : null;

        frameworks.Add(new DetectedModFramework
        {
            Framework = ModFrameworkType.MelonLoader,
            Version = version,
            HasXUnityPlugin = hasXUnity,
            XUnityVersion = xunityVersion
        });
    }

    private void DetectIPA(string gamePath, List<DetectedModFramework> frameworks)
    {
        var ipaExe = Path.Combine(gamePath, "IPA.exe");
        var ipaDir = Path.Combine(gamePath, "IPA");
        if (!File.Exists(ipaExe) && !Directory.Exists(ipaDir)) return;

        var version = File.Exists(ipaExe) ? ReadDllVersion(ipaExe) : null;

        var pluginsDir = Path.Combine(gamePath, "Plugins");
        var hasXUnity = FindXUnityDll(pluginsDir);
        var xunityVersion = hasXUnity ? ReadXUnityVersion(pluginsDir) : null;

        frameworks.Add(new DetectedModFramework
        {
            Framework = ModFrameworkType.IPA,
            Version = version,
            HasXUnityPlugin = hasXUnity,
            XUnityVersion = xunityVersion
        });
    }

    private void DetectReiPatcher(string gamePath, List<DetectedModFramework> frameworks)
    {
        var reiDir = Path.Combine(gamePath, "ReiPatcher");
        var reiExe = Path.Combine(gamePath, "ReiPatcher.exe");
        if (!Directory.Exists(reiDir) && !File.Exists(reiExe)) return;

        var version = File.Exists(reiExe) ? ReadDllVersion(reiExe) : null;

        var patchesDir = Path.Combine(gamePath, "Patches");
        var hasXUnity = FindXUnityDll(patchesDir) || FindXUnityDll(Path.Combine(gamePath, "Plugins"));
        var xunityVersion = hasXUnity
            ? ReadXUnityVersion(patchesDir) ?? ReadXUnityVersion(Path.Combine(gamePath, "Plugins"))
            : null;

        frameworks.Add(new DetectedModFramework
        {
            Framework = ModFrameworkType.ReiPatcher,
            Version = version,
            HasXUnityPlugin = hasXUnity,
            XUnityVersion = xunityVersion
        });
    }

    private void DetectSybaris(string gamePath, List<DetectedModFramework> frameworks)
    {
        var sybarisDir = Path.Combine(gamePath, "Sybaris");
        if (!Directory.Exists(sybarisDir)) return;

        string? version = null;
        var loaderDll = Path.Combine(sybarisDir, "Sybaris.Loader.dll");
        if (File.Exists(loaderDll))
            version = ReadDllVersion(loaderDll);

        var uiDir = Path.Combine(sybarisDir, "UnityInjector");
        var hasXUnity = FindXUnityDll(uiDir) || FindXUnityDll(sybarisDir);
        var xunityVersion = hasXUnity
            ? ReadXUnityVersion(uiDir) ?? ReadXUnityVersion(sybarisDir)
            : null;

        frameworks.Add(new DetectedModFramework
        {
            Framework = ModFrameworkType.Sybaris,
            Version = version,
            HasXUnityPlugin = hasXUnity,
            XUnityVersion = xunityVersion
        });
    }

    private void DetectUnityInjector(string gamePath, List<DetectedModFramework> frameworks)
    {
        var uiDir = Path.Combine(gamePath, "UnityInjector");
        if (!Directory.Exists(uiDir)) return;

        // Skip if already detected as part of Sybaris
        if (Directory.Exists(Path.Combine(gamePath, "Sybaris"))) return;

        var hasXUnity = FindXUnityDll(uiDir);
        var xunityVersion = hasXUnity ? ReadXUnityVersion(uiDir) : null;

        frameworks.Add(new DetectedModFramework
        {
            Framework = ModFrameworkType.UnityInjector,
            Version = null,
            HasXUnityPlugin = hasXUnity,
            XUnityVersion = xunityVersion
        });
    }

    private void DetectStandalone(string gamePath, List<DetectedModFramework> frameworks)
    {
        var autoTransDir = Path.Combine(gamePath, "AutoTranslator");
        if (!Directory.Exists(autoTransDir)) return;

        var xunityVersion = ReadXUnityVersion(autoTransDir) ?? ReadXUnityVersion(gamePath);

        frameworks.Add(new DetectedModFramework
        {
            Framework = ModFrameworkType.Standalone,
            Version = null,
            HasXUnityPlugin = true,
            XUnityVersion = xunityVersion
        });
    }

    // ===== Uninstall Methods =====

    private static void UninstallMelonLoader(string gamePath)
    {
        DeleteDirectory(Path.Combine(gamePath, "MelonLoader"));
        DeleteFile(Path.Combine(gamePath, "version.dll"));
        DeleteFile(Path.Combine(gamePath, "NOTICE.txt"));
        // Clean XUnity from MelonLoader locations
        CleanXUnityDlls(Path.Combine(gamePath, "Mods"));
        CleanXUnityDlls(Path.Combine(gamePath, "Plugins"));
    }

    private static void UninstallIPA(string gamePath)
    {
        DeleteFile(Path.Combine(gamePath, "IPA.exe"));
        DeleteDirectory(Path.Combine(gamePath, "IPA"));
        // Only delete Libs if it contains IPA marker files
        var libsDir = Path.Combine(gamePath, "Libs");
        if (Directory.Exists(libsDir) &&
            Directory.GetFiles(libsDir, "*.manifest", SearchOption.TopDirectoryOnly).Length > 0)
            DeleteDirectory(libsDir);
        DeleteFile(Path.Combine(gamePath, "winhttp.dll"));
        // Clean XUnity from IPA Plugins
        CleanXUnityDlls(Path.Combine(gamePath, "Plugins"));
    }

    private static void UninstallReiPatcher(string gamePath)
    {
        DeleteFile(Path.Combine(gamePath, "ReiPatcher.exe"));
        DeleteDirectory(Path.Combine(gamePath, "ReiPatcher"));
        CleanXUnityDlls(Path.Combine(gamePath, "Patches"));
        CleanXUnityDlls(Path.Combine(gamePath, "Plugins"));
    }

    private static void UninstallSybaris(string gamePath)
    {
        DeleteDirectory(Path.Combine(gamePath, "Sybaris"));
        DeleteFile(Path.Combine(gamePath, "opengl32.dll"));
    }

    private static void UninstallUnityInjector(string gamePath)
    {
        DeleteDirectory(Path.Combine(gamePath, "UnityInjector"));
    }

    private static void UninstallStandalone(string gamePath)
    {
        DeleteDirectory(Path.Combine(gamePath, "AutoTranslator"));
        // Standalone uses various proxy DLLs
        string[] proxyDlls = ["ExIni.dll"];
        foreach (var dll in proxyDlls)
            DeleteFile(Path.Combine(gamePath, dll));
    }

    // ===== Helpers =====

    private (bool hasXUnity, string? version) DetectXUnityInBepInEx(string gamePath)
    {
        var pluginsDir = Path.Combine(gamePath, "BepInEx", "plugins");
        if (!Directory.Exists(pluginsDir)) return (false, null);

        // Check XUnity.AutoTranslator directory
        var xunityDir = Path.Combine(pluginsDir, "XUnity.AutoTranslator");
        if (Directory.Exists(xunityDir))
        {
            var version = ReadXUnityVersion(xunityDir);
            return (true, version);
        }

        // Also check for flat plugin DLLs
        if (FindXUnityDll(pluginsDir))
        {
            var version = ReadXUnityVersion(pluginsDir);
            return (true, version);
        }

        return (false, null);
    }

    private static bool FindXUnityDll(string directory)
    {
        if (!Directory.Exists(directory)) return false;
        try
        {
            return Directory.GetFiles(directory, "XUnity.AutoTranslator*.dll", SearchOption.AllDirectories).Length > 0;
        }
        catch
        {
            return false;
        }
    }

    private string? ReadXUnityVersion(string directory)
    {
        if (!Directory.Exists(directory)) return null;
        try
        {
            var dlls = Directory.GetFiles(directory, "XUnity.AutoTranslator*.dll", SearchOption.AllDirectories);
            foreach (var dll in dlls)
            {
                var version = ReadDllVersion(dll);
                if (version is not null) return version;
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "读取 XUnity 版本失败: {Dir}", directory);
        }
        return null;
    }

    private string? ReadDllVersion(string dllPath)
    {
        try
        {
            if (!File.Exists(dllPath)) return null;
            var info = FileVersionInfo.GetVersionInfo(dllPath);
            return info.FileVersion ?? info.ProductVersion;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "读取 DLL 版本失败: {Path}", dllPath);
            return null;
        }
    }

    private static void DeleteDirectory(string path)
    {
        if (Directory.Exists(path))
            Directory.Delete(path, recursive: true);
    }

    private static void DeleteFile(string path)
    {
        if (File.Exists(path))
            File.Delete(path);
    }

    private static void CleanXUnityDlls(string directory)
    {
        if (!Directory.Exists(directory)) return;
        try
        {
            foreach (var dll in Directory.GetFiles(directory, "XUnity*.dll", SearchOption.AllDirectories))
                File.Delete(dll);
        }
        catch
        {
            // Best effort
        }
    }
}
