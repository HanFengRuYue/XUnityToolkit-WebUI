using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using XUnityToolkit_WebUI.Infrastructure;
using XUnityToolkit_WebUI.Models;

namespace XUnityToolkit_WebUI.Services;

public sealed class RuntimeFontConfigurationConflictException(string message) : InvalidOperationException(message);

public sealed class RuntimeTmpFontService(
    BundledAssetPaths bundledPaths,
    BundledFontCatalog catalog,
    TmpFontService legacyFonts,
    AppDataPaths appDataPaths,
    ConfigurationService configuration,
    ILogger<RuntimeTmpFontService> logger)
{
    public const string PluginId = "com.xunitytoolkit.runtimefontloader";
    public const string OverrideSentinel = "XUnityToolkit.RuntimeFont";
    private const string ConfigFileName = PluginId + ".cfg";
    private const string ManifestFileName = PluginId + ".manifest.json";
    private const string StatusFileName = PluginId + ".status.json";
    private const string MonoPluginFileName = "XUnityToolkit.RuntimeFontLoader.dll";
    private const string Il2CppPluginFileName = "XUnityToolkit.RuntimeFontLoader.IL2CPP.dll";

    public async Task<TmpFontStatus> InstallAsync(
        Game game,
        TmpFontInstallRequest request,
        CancellationToken ct = default)
    {
        if (game.DetectedInfo is null)
            throw new InvalidOperationException("未检测到 Unity 版本信息。");

        var applicationMode = NormalizeApplicationMode(request.ApplicationMode);
        var source = ResolveSource(game.Id, request.SourceId);
        var sourceHash = await ComputeSha256Async(source.Path, ct);
        var isDefaultSource = string.Equals(source.Id, BundledFontCatalog.DefaultSourceId, StringComparison.Ordinal);
        if (isDefaultSource
            && (!string.Equals(sourceHash, BundledFontCatalog.RuntimeSha256, StringComparison.OrdinalIgnoreCase)
                || new FileInfo(source.Path).Length != 17_749_860))
        {
            throw new InvalidDataException("内置思源黑体 TTF 的大小或 SHA-256 校验失败。");
        }

        var pluginSource = bundledPaths.GetRuntimeFontLoaderPlugin(game.DetectedInfo.Backend == UnityBackend.IL2CPP);
        if (!File.Exists(pluginSource))
            throw new FileNotFoundException("运行时字体插件未包含在当前发布包中，请重新构建完整版本。", pluginSource);

        var fontDirectory = GetFontDirectory(game.GamePath);
        var pluginDirectory = GetPluginDirectory(game.GamePath);
        var configDirectory = GetConfigDirectory(game.GamePath);
        var manifestPath = Path.Combine(configDirectory, ManifestFileName);
        var existingManifest = await ReadManifestAsync(manifestPath, ct);
        var currentConfig = await configuration.GetAsync(game.GamePath, ct);
        EnsureConfigCanBeManaged(currentConfig, existingManifest, request.ReplaceExistingConfig);

        Directory.CreateDirectory(fontDirectory);
        Directory.CreateDirectory(pluginDirectory);
        Directory.CreateDirectory(configDirectory);

        var installedFontName = isDefaultSource
            ? BundledFontCatalog.RuntimeFileName
            : $"runtime-{sourceHash[..12].ToLowerInvariant()}{Path.GetExtension(source.Path).ToLowerInvariant()}";
        var installedFontPath = Path.Combine(fontDirectory, installedFontName);
        File.Copy(source.Path, installedFontPath, overwrite: true);

        string? legacyFileName = null;
        if (isDefaultSource)
        {
            var legacyPath = legacyFonts.ResolveFontFile(game.DetectedInfo.UnityVersion);
            if (!string.IsNullOrWhiteSpace(legacyPath) && File.Exists(legacyPath))
            {
                legacyFileName = Path.GetFileName(legacyPath);
                File.Copy(legacyPath, Path.Combine(fontDirectory, legacyFileName), overwrite: true);
            }
        }

        var pluginFileName = game.DetectedInfo.Backend == UnityBackend.IL2CPP
            ? Il2CppPluginFileName
            : MonoPluginFileName;
        File.Copy(pluginSource, Path.Combine(pluginDirectory, pluginFileName), overwrite: true);

        var manifest = new RuntimeFontManifest
        {
            ProtocolVersion = 1,
            ConfiguredAtUtc = DateTime.UtcNow,
            SourceId = source.Id,
            SourceDisplayName = source.DisplayName,
            SourceFileName = installedFontName,
            SourceSha256 = sourceHash,
            Enabled = request.Enabled,
            LegacyFallbackFileName = legacyFileName,
            PluginFileName = pluginFileName,
            ApplicationMode = applicationMode,
            PreviousOverrideFontTextMeshPro = existingManifest?.PreviousOverrideFontTextMeshPro
                ?? currentConfig.OverrideFontTextMeshPro,
            PreviousFallbackFontTextMeshPro = existingManifest?.PreviousFallbackFontTextMeshPro
                ?? currentConfig.FallbackFontTextMeshPro,
        };

        await WritePluginConfigAsync(
            Path.Combine(configDirectory, ConfigFileName),
            request.Enabled,
            manifest,
            ct);
        await FileHelper.WriteJsonAtomicAsync(manifestPath, manifest, ct: ct);
        await ConfigureXUnityAsync(game.GamePath, manifest, request.ReplaceExistingConfig, ct);

        var alternatePlugin = pluginFileName == MonoPluginFileName ? Il2CppPluginFileName : MonoPluginFileName;
        DeleteIfExists(Path.Combine(pluginDirectory, alternatePlugin));
        if (existingManifest is not null)
        {
            if (!string.Equals(existingManifest.SourceFileName, manifest.SourceFileName, StringComparison.OrdinalIgnoreCase))
                DeleteManagedChildIfExists(fontDirectory, existingManifest.SourceFileName);
            if (!string.IsNullOrWhiteSpace(existingManifest.LegacyFallbackFileName)
                && !string.Equals(existingManifest.LegacyFallbackFileName, manifest.LegacyFallbackFileName, StringComparison.OrdinalIgnoreCase))
                DeleteManagedChildIfExists(fontDirectory, existingManifest.LegacyFallbackFileName);
        }

        logger.LogInformation(
            "已配置运行时 TMP 字体: Game={Game}, Source={Source}, Mode={Mode}, Legacy={Legacy}",
            game.Id, source.DisplayName, applicationMode, legacyFileName ?? "none");
        return await GetStatusAsync(game, ct);
    }

    public async Task ConfigureInstalledFontAsync(string gamePath, CancellationToken ct = default)
    {
        var manifest = await ReadManifestAsync(Path.Combine(GetConfigDirectory(gamePath), ManifestFileName), ct);
        if (manifest is not null)
            await ConfigureXUnityAsync(gamePath, manifest, replaceExistingConfig: false, ct);
    }

    public async Task<TmpFontStatus> RemoveAsync(Game game, CancellationToken ct = default)
    {
        var configDirectory = GetConfigDirectory(game.GamePath);
        var manifestPath = Path.Combine(configDirectory, ManifestFileName);
        var manifest = await ReadManifestAsync(manifestPath, ct);

        if (manifest is not null)
        {
            var current = await configuration.GetAsync(game.GamePath, ct);
            var restore = new Dictionary<string, string>();
            if (IsToolkitManagedValue(current.OverrideFontTextMeshPro, manifest))
                restore["OverrideFontTextMeshPro"] = manifest.PreviousOverrideFontTextMeshPro ?? string.Empty;
            if (IsToolkitManagedValue(current.FallbackFontTextMeshPro, manifest))
                restore["FallbackFontTextMeshPro"] = manifest.PreviousFallbackFontTextMeshPro ?? string.Empty;
            if (restore.Count > 0)
            {
                await configuration.PatchSectionAsync(game.GamePath, "Behaviour", restore, ct);
            }

            DeleteManagedChildIfExists(GetFontDirectory(game.GamePath), manifest.SourceFileName);
            if (!string.IsNullOrWhiteSpace(manifest.LegacyFallbackFileName))
                DeleteManagedChildIfExists(GetFontDirectory(game.GamePath), manifest.LegacyFallbackFileName);
            DeleteManagedChildIfExists(GetPluginDirectory(game.GamePath), manifest.PluginFileName);
        }
        DeleteIfExists(Path.Combine(GetPluginDirectory(game.GamePath), MonoPluginFileName));
        DeleteIfExists(Path.Combine(GetPluginDirectory(game.GamePath), Il2CppPluginFileName));

        DeleteIfExists(Path.Combine(configDirectory, ConfigFileName));
        DeleteIfExists(Path.Combine(configDirectory, StatusFileName));
        DeleteIfExists(manifestPath);
        return await GetStatusAsync(game, ct);
    }

    public async Task<TmpFontStatus> GetStatusAsync(Game game, CancellationToken ct = default)
    {
        var availableSources = await GetAvailableSourcesAsync(game.Id, ct);
        var configDirectory = GetConfigDirectory(game.GamePath);
        var manifest = await ReadManifestAsync(Path.Combine(configDirectory, ManifestFileName), ct);
        if (manifest is null)
        {
            return new TmpFontStatus
            {
                AvailableSources = availableSources,
            };
        }

        var pluginExists = IsManagedFileName(manifest.PluginFileName)
            && File.Exists(PathSecurity.SafeJoin(GetPluginDirectory(game.GamePath), manifest.PluginFileName));
        var configExists = File.Exists(Path.Combine(configDirectory, ConfigFileName));
        var runtimeStatus = await ReadRuntimeStatusAsync(Path.Combine(configDirectory, StatusFileName), ct);
        var runtimeCheckedAt = runtimeStatus?.GeneratedAtUtc;
        var requiresRestart = runtimeCheckedAt is null || runtimeCheckedAt < manifest.ConfiguredAtUtc;

        return new TmpFontStatus
        {
            Installed = pluginExists && configExists,
            Enabled = pluginExists && configExists && manifest.Enabled,
            AvailableSources = availableSources,
            SourceId = manifest.SourceId,
            SourceDisplayName = manifest.SourceDisplayName,
            ApplicationMode = manifest.ApplicationMode,
            ActiveLoader = requiresRestart ? "pending" : runtimeStatus?.ActiveLoader ?? "none",
            DirectTtfSupported = runtimeStatus?.DirectTtfSupported ?? false,
            LegacyFallbackUsed = runtimeStatus?.LegacyFallbackUsed ?? false,
            OverrideAdapterAvailable = runtimeStatus?.OverrideAdapterAvailable ?? false,
            RequiresRestart = requiresRestart,
            LastRuntimeCheckUtc = runtimeCheckedAt,
            Message = requiresRestart
                ? "运行时字体已配置，等待下次启动游戏验证。"
                : runtimeStatus?.Message ?? "尚未收到运行时字体状态。",
            Error = runtimeStatus?.Error,
        };
    }

    public async Task<List<ReplacementSource>> GetAvailableSourcesAsync(string gameId, CancellationToken ct = default)
    {
        var result = new List<ReplacementSource> { catalog.CreateDefaultSource() };
        var customDirectory = appDataPaths.GetCustomTtfFontDirectory(gameId);
        if (!Directory.Exists(customDirectory))
            return result;

        foreach (var path in Directory.GetFiles(customDirectory)
                     .Where(IsTtfOrOtf)
                     .OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase))
        {
            ct.ThrowIfCancellationRequested();
            var info = new FileInfo(path);
            result.Add(new ReplacementSource
            {
                Id = "ttf__" + info.Name,
                Kind = "TTF",
                DisplayName = info.Name,
                FileName = info.Name,
                Origin = "custom",
                IsDefault = false,
                FileSize = info.Length,
                UploadedAt = info.LastWriteTimeUtc,
                Sha256 = await ComputeSha256Async(path, ct),
            });
        }
        return result;
    }

    private RuntimeFontSource ResolveSource(string gameId, string? sourceId)
    {
        sourceId = string.IsNullOrWhiteSpace(sourceId) ? BundledFontCatalog.DefaultSourceId : sourceId.Trim();
        if (string.Equals(sourceId, BundledFontCatalog.DefaultSourceId, StringComparison.Ordinal))
        {
            if (!File.Exists(catalog.RuntimeFontPath))
                throw new FileNotFoundException("内置思源黑体运行时 TTF 不存在。", catalog.RuntimeFontPath);
            return new RuntimeFontSource(sourceId, BundledFontCatalog.DisplayName, catalog.RuntimeFontPath);
        }

        const string prefix = "ttf__";
        if (!sourceId.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("无效的运行时字体来源。" );
        var fileName = sourceId[prefix.Length..];
        if (string.IsNullOrWhiteSpace(fileName) || !string.Equals(fileName, Path.GetFileName(fileName), StringComparison.Ordinal))
            throw new InvalidOperationException("无效的自定义字体文件名。" );
        var path = PathSecurity.SafeJoin(appDataPaths.GetCustomTtfFontDirectory(gameId), fileName);
        if (!File.Exists(path) || !IsTtfOrOtf(path))
            throw new FileNotFoundException("自定义 TTF/OTF 字体不存在。", path);
        return new RuntimeFontSource(sourceId, fileName, path);
    }

    private async Task ConfigureXUnityAsync(
        string gamePath,
        RuntimeFontManifest manifest,
        bool replaceExistingConfig,
        CancellationToken ct)
    {
        if (!File.Exists(configuration.GetConfigPath(gamePath)))
            return;
        var current = await configuration.GetAsync(gamePath, ct);
        EnsureConfigCanBeManaged(current, manifest, replaceExistingConfig);
        await configuration.PatchSectionAsync(gamePath, "Behaviour",
            new Dictionary<string, string>
            {
                ["OverrideFontTextMeshPro"] = manifest.ApplicationMode == "override" ? OverrideSentinel : string.Empty,
                ["FallbackFontTextMeshPro"] = string.Empty,
            }, ct);
    }

    private static void EnsureConfigCanBeManaged(
        XUnityConfig config,
        RuntimeFontManifest? manifest,
        bool replaceExistingConfig)
    {
        if (replaceExistingConfig)
            return;
        if (!IsToolkitManagedValue(config.OverrideFontTextMeshPro, manifest)
            || !IsToolkitManagedValue(config.FallbackFontTextMeshPro, manifest))
        {
            throw new RuntimeFontConfigurationConflictException(
                "检测到非工具箱管理的 XUnity TMP 字体配置；确认替换后才能继续。" );
        }
    }

    private static bool IsToolkitManagedValue(string? value, RuntimeFontManifest? manifest)
    {
        if (string.IsNullOrWhiteSpace(value) || string.Equals(value, OverrideSentinel, StringComparison.Ordinal))
            return true;
        var normalized = value.Replace('\\', '/');
        if (normalized.StartsWith("BepInEx/Font/SourceHanSans_U", StringComparison.OrdinalIgnoreCase))
            return true;
        if (manifest is null)
            return false;
        return normalized.EndsWith('/' + manifest.SourceFileName, StringComparison.OrdinalIgnoreCase)
            || (!string.IsNullOrWhiteSpace(manifest.LegacyFallbackFileName)
                && normalized.EndsWith('/' + manifest.LegacyFallbackFileName, StringComparison.OrdinalIgnoreCase));
    }

    private static async Task WritePluginConfigAsync(
        string path,
        bool enabled,
        RuntimeFontManifest manifest,
        CancellationToken ct)
    {
        var builder = new StringBuilder();
        builder.AppendLine("[RuntimeFont]");
        builder.AppendLine($"Enabled = {enabled.ToString().ToLowerInvariant()}");
        builder.AppendLine($"SourceId = {manifest.SourceId}");
        builder.AppendLine($"FontPath = BepInEx/Font/{manifest.SourceFileName}");
        builder.AppendLine($"SourceSha256 = {manifest.SourceSha256}");
        builder.AppendLine($"LegacyFallbackPath = {(manifest.LegacyFallbackFileName is null ? string.Empty : "BepInEx/Font/" + manifest.LegacyFallbackFileName)}");
        builder.AppendLine($"ApplicationMode = {manifest.ApplicationMode}");
        builder.AppendLine($"AllowLegacyFallback = {(manifest.SourceId == BundledFontCatalog.DefaultSourceId).ToString().ToLowerInvariant()}");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var tempPath = path + ".tmp";
        await File.WriteAllTextAsync(tempPath, builder.ToString(), new UTF8Encoding(false), ct);
        File.Move(tempPath, path, overwrite: true);
    }

    private static async Task<RuntimeFontManifest?> ReadManifestAsync(string path, CancellationToken ct)
    {
        if (!File.Exists(path)) return null;
        try
        {
            await using var stream = File.OpenRead(path);
            var manifest = await JsonSerializer.DeserializeAsync<RuntimeFontManifest>(stream, FileHelper.DataJsonOptions, ct);
            return manifest is not null
                   && IsManagedFileName(manifest.SourceFileName)
                   && (string.IsNullOrWhiteSpace(manifest.LegacyFallbackFileName)
                       || IsManagedFileName(manifest.LegacyFallbackFileName))
                   && manifest.PluginFileName is MonoPluginFileName or Il2CppPluginFileName
                ? manifest
                : null;
        }
        catch
        {
            return null;
        }
    }

    private static async Task<RuntimeFontPluginStatus?> ReadRuntimeStatusAsync(string path, CancellationToken ct)
    {
        if (!File.Exists(path)) return null;
        try
        {
            await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            return await JsonSerializer.DeserializeAsync<RuntimeFontPluginStatus>(stream, FileHelper.DataJsonOptions, ct);
        }
        catch
        {
            return null;
        }
    }

    private static string NormalizeApplicationMode(string? mode) => mode?.Trim().ToLowerInvariant() switch
    {
        "fallback" => "fallback",
        "override" => "override",
        _ => throw new InvalidOperationException("应用模式必须是 fallback 或 override。"),
    };

    private static bool IsTtfOrOtf(string path) => Path.GetExtension(path).ToLowerInvariant() is ".ttf" or ".otf";

    private static async Task<string> ComputeSha256Async(string path, CancellationToken ct)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        var hash = await SHA256.HashDataAsync(stream, ct);
        return Convert.ToHexString(hash);
    }

    private static string GetFontDirectory(string gamePath) => Path.Combine(gamePath, "BepInEx", "Font");
    private static string GetPluginDirectory(string gamePath) => Path.Combine(gamePath, "BepInEx", "plugins", "XUnityToolkit");
    private static string GetConfigDirectory(string gamePath) => Path.Combine(gamePath, "BepInEx", "config");

    private static void DeleteIfExists(string path)
    {
        if (File.Exists(path)) File.Delete(path);
    }

    private static bool IsManagedFileName(string? fileName) =>
        !string.IsNullOrWhiteSpace(fileName)
        && string.Equals(fileName, Path.GetFileName(fileName), StringComparison.Ordinal);

    private static void DeleteManagedChildIfExists(string directory, string? fileName)
    {
        if (IsManagedFileName(fileName))
            DeleteIfExists(PathSecurity.SafeJoin(directory, fileName!));
    }

    private sealed record RuntimeFontSource(string Id, string DisplayName, string Path);

    private sealed record RuntimeFontManifest
    {
        public int ProtocolVersion { get; init; }
        public DateTime ConfiguredAtUtc { get; init; }
        public required string SourceId { get; init; }
        public required string SourceDisplayName { get; init; }
        public required string SourceFileName { get; init; }
        public required string SourceSha256 { get; init; }
        public bool Enabled { get; init; } = true;
        public string? LegacyFallbackFileName { get; init; }
        public required string PluginFileName { get; init; }
        public required string ApplicationMode { get; init; }
        public string? PreviousOverrideFontTextMeshPro { get; init; }
        public string? PreviousFallbackFontTextMeshPro { get; init; }
    }

    private sealed record RuntimeFontPluginStatus
    {
        public DateTime GeneratedAtUtc { get; init; }
        public string ActiveLoader { get; init; } = "none";
        public bool DirectTtfSupported { get; init; }
        public bool LegacyFallbackUsed { get; init; }
        public bool OverrideAdapterAvailable { get; init; }
        public string Message { get; init; } = string.Empty;
        public string? Error { get; init; }
    }
}
