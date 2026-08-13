using System.Diagnostics;
using System.IO.Compression;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using XUnityToolkit_WebUI.Infrastructure;
using XUnityToolkit_WebUI.Models;

namespace XUnityToolkit_WebUI.Services;

public enum TranslatorEndpointOrigin
{
    Missing,
    OfficialCurrent,
    OfficialOutdated,
    UnknownOrCustom
}

public sealed record TranslatorEndpointStatus(
    bool Installed,
    TranslatorEndpointOrigin Origin,
    string? Version,
    string? Sha256,
    bool UpdatePending,
    bool AutoDiscoverySupported,
    bool DirectConnectionMode,
    string Message);

public sealed class XUnityInstallerService
{
    private const string EndpointDllResourceName = "LLMTranslate.dll";
    private const string EndpointMetadataResourceName = "translator-endpoint-metadata.json";

    private readonly ILogger<XUnityInstallerService> _logger;
    private readonly Lazy<EndpointPackage> _endpointPackage;

    public XUnityInstallerService(ILogger<XUnityInstallerService> logger)
    {
        _logger = logger;
        _endpointPackage = new Lazy<EndpointPackage>(LoadEndpointPackage, true);
    }

    private static string GetTranslatorEndpointPath(string gamePath) =>
        Path.Combine(gamePath, "BepInEx", "plugins", "XUnity.AutoTranslator", "Translators", EndpointDllResourceName);

    public TranslatorEndpointStatus GetTranslatorEndpointStatus(Game game)
    {
        var destination = GetTranslatorEndpointPath(game.GamePath);
        if (!File.Exists(destination))
        {
            return new TranslatorEndpointStatus(
                false,
                TranslatorEndpointOrigin.Missing,
                null,
                null,
                false,
                false,
                false,
                "AI 翻译端点尚未安装。");
        }

        var hash = ComputeSha256(destination);
        var package = _endpointPackage.Value;
        var version = TryReadFileVersion(destination);
        var running = GameProcessHelper.IsGameRunning(game);

        if (hash.Equals(package.CurrentSha256, StringComparison.OrdinalIgnoreCase))
        {
            return new TranslatorEndpointStatus(
                true,
                TranslatorEndpointOrigin.OfficialCurrent,
                package.Metadata.CurrentVersion,
                hash,
                false,
                true,
                true,
                "已安装当前官方端点，支持自动发现、持续心跳和本机直连。");
        }

        var legacy = package.Metadata.LegacyOfficialBuilds.FirstOrDefault(item =>
            hash.Equals(item.Sha256, StringComparison.OrdinalIgnoreCase));
        if (legacy is not null)
        {
            return new TranslatorEndpointStatus(
                true,
                TranslatorEndpointOrigin.OfficialOutdated,
                version ?? legacy.Version,
                hash,
                running,
                false,
                false,
                running
                    ? "检测到旧官方端点；游戏正在运行，已标记为待升级。"
                    : "检测到旧官方端点，可安全自动升级。");
        }

        return new TranslatorEndpointStatus(
            true,
            TranslatorEndpointOrigin.UnknownOrCustom,
            version,
            hash,
            false,
            false,
            false,
            "检测到未知或自定义端点，为避免覆盖用户文件，工具箱不会自动替换。");
    }

    internal TranslatorEndpointOrigin ClassifyHash(string sha256)
    {
        var package = _endpointPackage.Value;
        if (sha256.Equals(package.CurrentSha256, StringComparison.OrdinalIgnoreCase))
            return TranslatorEndpointOrigin.OfficialCurrent;
        return package.Metadata.LegacyOfficialBuilds.Any(item =>
            sha256.Equals(item.Sha256, StringComparison.OrdinalIgnoreCase))
            ? TranslatorEndpointOrigin.OfficialOutdated
            : TranslatorEndpointOrigin.UnknownOrCustom;
    }

    /// <summary>
    /// Installs a missing endpoint or upgrades a hash-confirmed old official endpoint.
    /// Unknown/custom files are preserved unless the caller explicitly confirms replacement.
    /// </summary>
    public TranslatorEndpointStatus EnsureTranslatorEndpoint(Game game, bool forceReplaceUnknown = false)
    {
        var current = GetTranslatorEndpointStatus(game);
        if (current.Origin == TranslatorEndpointOrigin.OfficialCurrent)
            return current;

        if (current.Origin == TranslatorEndpointOrigin.UnknownOrCustom && !forceReplaceUnknown)
            return current;

        if (GameProcessHelper.IsGameRunning(game))
        {
            return current with
            {
                UpdatePending = current.Origin == TranslatorEndpointOrigin.OfficialOutdated,
                Message = "游戏正在运行，已延后端点文件替换；请退出游戏后重试。"
            };
        }

        if (!TryExtractEndpointDll(GetTranslatorEndpointPath(game.GamePath)))
            return current with { Message = "官方 AI 翻译端点未嵌入当前工具箱构建。" };

        return GetTranslatorEndpointStatus(game) with
        {
            Message = current.Origin switch
            {
                TranslatorEndpointOrigin.Missing => "已安装当前官方 AI 翻译端点。",
                TranslatorEndpointOrigin.OfficialOutdated => "已将旧官方 AI 翻译端点安全升级到当前版本。",
                _ => "已按用户确认将未知或自定义端点替换为当前官方版本。"
            }
        };
    }

    // Compatibility overload for installation flows that only need to know whether an endpoint exists.
    public bool DeployTranslatorEndpoint(Game game) => EnsureTranslatorEndpoint(game).Installed;

    public bool IsTranslatorEndpointInstalled(string gamePath) => File.Exists(GetTranslatorEndpointPath(gamePath));

    public void RemoveTranslatorEndpoint(string gamePath)
    {
        var path = GetTranslatorEndpointPath(gamePath);
        if (!File.Exists(path))
            return;

        File.Delete(path);
        _logger.LogInformation("已删除 AI 翻译端点 DLL: {Path}", path);
    }

    private bool TryExtractEndpointDll(string destination)
    {
        var package = _endpointPackage.Value;
        if (package.Bytes.Length == 0)
            return false;

        var directory = Path.GetDirectoryName(destination)!;
        Directory.CreateDirectory(directory);
        var temporary = Path.Combine(directory, $".{EndpointDllResourceName}.{Guid.NewGuid():N}.tmp");

        try
        {
            File.WriteAllBytes(temporary, package.Bytes);
            var writtenHash = ComputeSha256(temporary);
            if (!writtenHash.Equals(package.CurrentSha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("写入后的 AI 翻译端点 SHA-256 校验失败。");

            File.Move(temporary, destination, overwrite: true);
            _logger.LogInformation(
                "已原子部署官方翻译端点 DLL {Version} ({Sha256}): {Path}",
                package.Metadata.CurrentVersion,
                package.CurrentSha256,
                destination);
            return true;
        }
        finally
        {
            if (File.Exists(temporary))
                File.Delete(temporary);
        }
    }

    private EndpointPackage LoadEndpointPackage()
    {
        var assembly = Assembly.GetExecutingAssembly();
        using var dllStream = assembly.GetManifestResourceStream(EndpointDllResourceName);
        using var metadataStream = assembly.GetManifestResourceStream(EndpointMetadataResourceName);
        if (metadataStream is null)
            throw new InvalidDataException("AI 翻译端点官方元数据未嵌入当前构建。");

        var metadata = JsonSerializer.Deserialize<EndpointMetadata>(metadataStream, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        }) ?? new EndpointMetadata();
        if (metadata.SchemaVersion != 1
            || !metadata.Product.Equals("XUnityToolkit.LLMTranslate", StringComparison.Ordinal))
        {
            throw new InvalidDataException("嵌入的 AI 翻译端点官方元数据无效。");
        }

        if (dllStream is null)
        {
            _logger.LogWarning("AI 翻译端点 DLL 未嵌入当前构建");
            return new EndpointPackage([], metadata, string.Empty);
        }

        using var buffer = new MemoryStream();
        dllStream.CopyTo(buffer);
        var bytes = buffer.ToArray();
        return new EndpointPackage(bytes, metadata, Convert.ToHexString(SHA256.HashData(bytes)));
    }

    private static string ComputeSha256(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    private static string? TryReadFileVersion(string path)
    {
        try
        {
            return FileVersionInfo.GetVersionInfo(path).FileVersion;
        }
        catch
        {
            return null;
        }
    }

    public string ResolveBundledZip(UnityGameInfo info, BundledAssetPaths bundled)
    {
        var isIL2CPP = info.Backend == UnityBackend.IL2CPP;
        var zip = bundled.GetXUnityZips().FirstOrDefault(z =>
        {
            var name = Path.GetFileName(z);
            return name.StartsWith("XUnity.AutoTranslator-BepInEx", StringComparison.OrdinalIgnoreCase)
                   && isIL2CPP == name.Contains("IL2CPP", StringComparison.OrdinalIgnoreCase);
        });

        return zip ?? throw new InvalidOperationException(
            $"未找到捆绑的 XUnity ZIP（{(isIL2CPP ? "IL2CPP" : "Mono")}）。请重新构建发布版本。");
    }

    public static string ParseVersionFromZip(string zipPath)
    {
        var name = Path.GetFileNameWithoutExtension(zipPath);
        var lastDash = name.LastIndexOf('-');
        return lastDash >= 0 ? $"v{name[(lastDash + 1)..]}" : "v?.?";
    }

    public Task<List<string>> InstallAsync(string gamePath, string zipPath, CancellationToken ct = default)
    {
        _logger.LogInformation("正在安装 XUnity.AutoTranslator: {Zip}", zipPath);
        var installedFiles = new List<string>();
        using var archive = ZipFile.OpenRead(zipPath);
        foreach (var entry in archive.Entries)
        {
            ct.ThrowIfCancellationRequested();
            if (string.IsNullOrEmpty(entry.Name))
                continue;

            var destPath = PathSecurity.SafeJoin(gamePath, entry.FullName);
            Directory.CreateDirectory(Path.GetDirectoryName(destPath)!);
            entry.ExtractToFile(destPath, overwrite: true);
            installedFiles.Add(entry.FullName);
        }

        _logger.LogInformation("XUnity.AutoTranslator 安装完成，共 {Count} 个文件", installedFiles.Count);
        return Task.FromResult(installedFiles);
    }

    public Task UninstallAsync(string gamePath, CancellationToken ct = default)
    {
        _logger.LogInformation("正在卸载 XUnity.AutoTranslator: {Game}", gamePath);
        var pluginsDir = Path.Combine(gamePath, "BepInEx", "plugins");
        if (Directory.Exists(pluginsDir))
        {
            foreach (var dir in new[] { "XUnity.AutoTranslator", "XUnity.ResourceRedirector" })
            {
                var path = Path.Combine(pluginsDir, dir);
                if (Directory.Exists(path))
                    Directory.Delete(path, recursive: true);
            }
        }

        var commonDll = Path.Combine(gamePath, "BepInEx", "core", "XUnity.Common.dll");
        if (File.Exists(commonDll)) File.Delete(commonDll);

        var configDir = Path.Combine(gamePath, "BepInEx", "config");
        foreach (var cfgName in new[] { "AutoTranslatorConfig.ini", "gravydevsupreme.xunity.autotranslator.cfg" })
        {
            var cfgPath = Path.Combine(configDir, cfgName);
            if (File.Exists(cfgPath)) File.Delete(cfgPath);
        }

        var translationDir = Path.Combine(gamePath, "BepInEx", "Translation");
        if (Directory.Exists(translationDir)) Directory.Delete(translationDir, recursive: true);
        return Task.CompletedTask;
    }

    private sealed record EndpointPackage(byte[] Bytes, EndpointMetadata Metadata, string CurrentSha256);

    private sealed class EndpointMetadata
    {
        public int SchemaVersion { get; init; }
        public string Product { get; init; } = string.Empty;
        public string CurrentVersion { get; init; } = string.Empty;
        public int ProtocolVersion { get; init; }
        public List<LegacyOfficialBuild> LegacyOfficialBuilds { get; init; } = [];
    }

    private sealed class LegacyOfficialBuild
    {
        public string Version { get; init; } = string.Empty;
        public string Sha256 { get; init; } = string.Empty;
    }
}
