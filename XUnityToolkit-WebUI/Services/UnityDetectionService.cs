using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using XUnityToolkit_WebUI.Models;

namespace XUnityToolkit_WebUI.Services;

public sealed partial class UnityDetectionService(ILogger<UnityDetectionService> logger)
{
    /// <summary>
    /// Full Unity detection — throws if no exe or not a Unity game.
    /// Used by InstallOrchestrator.
    /// </summary>
    public Task<UnityGameInfo> DetectAsync(string gamePath, CancellationToken ct = default)
    {
        var exePath = FindGameExecutable(gamePath);
        if (exePath is null)
            throw new FileNotFoundException("No game executable found in the specified directory.");

        if (!CheckIsUnityGame(gamePath, exePath))
            throw new InvalidOperationException("指定目录中未找到 Unity 游戏标识。");

        return Task.FromResult(BuildUnityGameInfo(gamePath, exePath));
    }

    /// <summary>
    /// Try to find an exe in the directory. Returns null if none found.
    /// </summary>
    public string? TryFindExecutable(string gamePath) => FindGameExecutable(gamePath);

    /// <summary>
    /// Check if a game directory + exe is a Unity game using multiple heuristics.
    /// </summary>
    public bool CheckIsUnityGame(string gamePath, string exePath)
    {
        var gameName = Path.GetFileNameWithoutExtension(exePath);
        var exeDir = Path.GetDirectoryName(exePath) ?? gamePath;

        // Heuristic 1: {Name}_Data/ folder (canonical Unity layout)
        if (Directory.Exists(Path.Combine(gamePath, $"{gameName}_Data")))
            return true;
        if (exeDir != gamePath && Directory.Exists(Path.Combine(exeDir, $"{gameName}_Data")))
            return true;

        // Heuristic 2: UnityPlayer.dll
        if (File.Exists(Path.Combine(gamePath, "UnityPlayer.dll")))
            return true;

        // Heuristic 3: GameAssembly.dll (IL2CPP)
        if (File.Exists(Path.Combine(gamePath, "GameAssembly.dll")))
            return true;

        // Heuristic 4: globalgamemanagers or data.unity3d in any *_Data subfolder
        try
        {
            foreach (var dir in Directory.GetDirectories(gamePath, "*_Data", SearchOption.TopDirectoryOnly))
            {
                if (File.Exists(Path.Combine(dir, "globalgamemanagers")) ||
                    File.Exists(Path.Combine(dir, "data.unity3d")))
                    return true;
            }
        }
        catch { /* access denied etc. */ }

        // Heuristic 5: MonoBleedingEdge folder (Mono runtime)
        if (Directory.Exists(Path.Combine(gamePath, "MonoBleedingEdge")))
            return true;

        return false;
    }

    /// <summary>
    /// Build full UnityGameInfo for a confirmed Unity game.
    /// </summary>
    public UnityGameInfo BuildUnityGameInfo(string gamePath, string exePath)
    {
        var architecture = DetectArchitecture(exePath);
        var backend = DetectBackend(gamePath, exePath);
        var unityVersion = DetectUnityVersion(gamePath, exePath);
        var hasTextMeshPro = DetectTextMeshPro(gamePath, exePath, backend);

        var info = new UnityGameInfo
        {
            UnityVersion = unityVersion ?? "Unknown",
            Backend = backend,
            Architecture = architecture,
            DetectedExecutable = Path.GetFileName(exePath),
            HasTextMeshPro = hasTextMeshPro
        };

        logger.LogInformation(
            "检测结果: {Exe} | Unity {Version} | {Backend} | {Arch} | TMP={HasTMP}",
            info.DetectedExecutable, info.UnityVersion, info.Backend, info.Architecture,
            hasTextMeshPro?.ToString() ?? "Unknown");

        return info;
    }

    private static bool? DetectTextMeshPro(string gamePath, string exePath, UnityBackend backend)
    {
        if (backend == UnityBackend.IL2CPP)
            return null;

        var gameName = Path.GetFileNameWithoutExtension(exePath);
        var managedDir = Path.Combine(gamePath, $"{gameName}_Data", "Managed");

        if (!Directory.Exists(managedDir))
            return null;

        try
        {
            var files = Directory.GetFiles(managedDir, "*.dll", SearchOption.TopDirectoryOnly);
            return files.Any(f =>
                Path.GetFileName(f).Contains("TextMeshPro", StringComparison.OrdinalIgnoreCase));
        }
        catch
        {
            return null;
        }
    }

    private static string? FindGameExecutable(string gamePath)
    {
        var exeFiles = Directory.GetFiles(gamePath, "*.exe", SearchOption.TopDirectoryOnly);

        // Prefer exe with matching _Data folder
        foreach (var exe in exeFiles)
        {
            var name = Path.GetFileNameWithoutExtension(exe);
            var dataDir = Path.Combine(gamePath, $"{name}_Data");
            if (Directory.Exists(dataDir))
                return exe;
        }

        // Exclude common non-game executables
        string[] excludes = ["UnityCrashHandler", "UnityPlayer", "unins", "setup", "launcher"];
        var candidates = exeFiles
            .Where(e => !excludes.Any(x =>
                Path.GetFileName(e).Contains(x, StringComparison.OrdinalIgnoreCase)))
            .ToArray();

        return candidates.Length > 0
            ? candidates.OrderByDescending(f => new FileInfo(f).Length).First()
            : exeFiles.FirstOrDefault();
    }

    private static Architecture DetectArchitecture(string exePath)
    {
        using var stream = new FileStream(exePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var reader = new BinaryReader(stream);

        // DOS header: e_lfanew at offset 0x3C
        stream.Seek(0x3C, SeekOrigin.Begin);
        var peOffset = reader.ReadInt32();

        // PE signature (4 bytes) + Machine type (2 bytes)
        stream.Seek(peOffset + 4, SeekOrigin.Begin);
        var machineType = reader.ReadUInt16();

        // 0x014C = x86, 0x8664 = x64
        return machineType == 0x8664 ? Models.Architecture.X64 : Models.Architecture.X86;
    }

    private static UnityBackend DetectBackend(string gamePath, string exePath)
    {
        // Check for GameAssembly.dll (definitive IL2CPP indicator)
        if (File.Exists(Path.Combine(gamePath, "GameAssembly.dll")))
            return UnityBackend.IL2CPP;

        // Check for il2cpp_data directory
        var gameName = Path.GetFileNameWithoutExtension(exePath);
        var il2cppDataDir = Path.Combine(gamePath, $"{gameName}_Data", "il2cpp_data");
        if (Directory.Exists(il2cppDataDir))
            return UnityBackend.IL2CPP;

        return UnityBackend.Mono;
    }

    private string? DetectUnityVersion(string gamePath, string exePath)
    {
        var gameName = Path.GetFileNameWithoutExtension(exePath);

        // Method 1: Read from globalgamemanagers
        var ggmPath = Path.Combine(gamePath, $"{gameName}_Data", "globalgamemanagers");
        if (File.Exists(ggmPath))
        {
            var version = ReadVersionFromGlobalGameManagers(ggmPath);
            if (version is not null) return version;
        }

        // Method 2: Read from data.unity3d (some games use this)
        var dataUnityPath = Path.Combine(gamePath, $"{gameName}_Data", "data.unity3d");
        if (File.Exists(dataUnityPath))
        {
            var version = ReadVersionFromBinaryFile(dataUnityPath);
            if (version is not null) return version;
        }

        // Method 3: UnityPlayer.dll file version
        var unityPlayerPath = Path.Combine(gamePath, "UnityPlayer.dll");
        if (File.Exists(unityPlayerPath))
        {
            var versionInfo = FileVersionInfo.GetVersionInfo(unityPlayerPath);
            if (versionInfo.FileVersion is not null)
                return versionInfo.FileVersion;
        }

        return null;
    }

    private string? ReadVersionFromGlobalGameManagers(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            // Read first 128 bytes and search for version pattern
            var buffer = new byte[128];
            var bytesRead = stream.Read(buffer, 0, buffer.Length);
            var text = Encoding.UTF8.GetString(buffer, 0, bytesRead);

            var match = UnityVersionRegex().Match(text);
            return match.Success ? match.Value : null;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "读取版本信息失败: {Path}", path);
            return null;
        }
    }

    private string? ReadVersionFromBinaryFile(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            var buffer = new byte[256];
            var bytesRead = stream.Read(buffer, 0, buffer.Length);
            var text = Encoding.UTF8.GetString(buffer, 0, bytesRead);

            var match = UnityVersionRegex().Match(text);
            return match.Success ? match.Value : null;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "读取版本信息失败: {Path}", path);
            return null;
        }
    }

    [GeneratedRegex(@"\d{4}\.\d+\.\d+[a-z]\d+")]
    private static partial Regex UnityVersionRegex();
}
