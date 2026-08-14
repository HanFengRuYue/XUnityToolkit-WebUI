using System.Diagnostics;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using XUnityToolkit_WebUI.Models;

namespace XUnityToolkit_WebUI.Services;

internal sealed record DiagnosticArtifactDescriptor(
    string Id,
    string Label,
    string Kind,
    string? RelativePath,
    string Summary,
    long Size,
    DateTime? LastModifiedUtc,
    string? FullPath,
    string? SyntheticContent,
    bool Required,
    bool SourceTruncated = false);

internal sealed record DiagnosticArtifactContent(
    DiagnosticArtifactDescriptor Descriptor,
    IReadOnlyList<string> Lines,
    bool Truncated,
    string? SelectionReason)
{
    public string NumberedContent => string.Join('\n', Lines.Select((line, index) => $"{index + 1:D5}: {line}"));
}

internal sealed record DiagnosticArtifactSnapshot(
    string Fingerprint,
    IReadOnlyList<DiagnosticArtifactDescriptor> Artifacts);

internal sealed record PluginAssemblyMetadata(
    string AssemblyName,
    string? AssemblyVersion,
    string? FileVersion,
    IReadOnlyList<string> References);

public sealed partial class PluginDiagnosticArtifactCollector(
    ILogger<PluginDiagnosticArtifactCollector> logger)
{
    private const int MaxPluginAssemblies = 300;
    private const int MaxConfigArtifacts = 256;
    private const int MaxSelectedArtifacts = 12;
    private const int MaxConfigBytes = 128 * 1024;
    private const int MaxLogBytes = 8 * 1024 * 1024;
    private const int MaxLogLines = 4000;

    private static readonly EnumerationOptions RecursiveEnumeration = new()
    {
        RecurseSubdirectories = true,
        IgnoreInaccessible = true,
        AttributesToSkip = FileAttributes.ReparsePoint
    };

    internal async Task<DiagnosticArtifactSnapshot> CollectInventoryAsync(
        Game game,
        IReadOnlyList<HealthCheckItem> objectiveChecks,
        CancellationToken ct = default)
    {
        var gamePath = Path.GetFullPath(game.GamePath);
        var artifacts = new List<DiagnosticArtifactDescriptor>();
        var environment = BuildEnvironmentSummary(game, objectiveChecks);
        artifacts.Add(new(
            "environment",
            "游戏与本地检查摘要",
            "environment",
            null,
            "Unity 环境、安装状态和确定性检查结果",
            Encoding.UTF8.GetByteCount(environment),
            null,
            null,
            environment,
            Required: true));

        var (pluginMetadata, pluginInventoryTruncated) = CollectPluginMetadata(gamePath, ct);
        var pluginInventory = BuildPluginInventory(pluginMetadata, gamePath, pluginInventoryTruncated);
        artifacts.Add(new(
            "plugin-inventory",
            "BepInEx 插件与依赖清单",
            "metadata",
            "BepInEx/plugins",
            $"发现 {pluginMetadata.Count} 个插件程序集" +
            (pluginInventoryTruncated ? $"（清单已按 {MaxPluginAssemblies} 项上限截断）" : string.Empty) +
            "；内容仅来自 PE 元数据，不包含二进制",
            Encoding.UTF8.GetByteCount(pluginInventory),
            null,
            null,
            pluginInventory,
            Required: true,
            SourceTruncated: pluginInventoryTruncated));

        AddKnownTextArtifact(artifacts, gamePath, "doorstop-config", "Doorstop 启动配置",
            "config", "doorstop_config.ini", "Doorstop 启动与目标程序集配置");
        AddKnownTextArtifact(artifacts, gamePath, "bepinex-config", "BepInEx 主配置",
            "config", Path.Combine("BepInEx", "config", "BepInEx.cfg"), "BepInEx 日志、链式加载和运行配置");
        AddKnownTextArtifact(artifacts, gamePath, "xunity-config", "XUnity.AutoTranslator 配置",
            "config", Path.Combine("BepInEx", "config", "AutoTranslatorConfig.ini"), "翻译端点、语言、文本框架与 LLMTranslate 配置");
        AddKnownTextArtifact(artifacts, gamePath, "bepinex-log", "BepInEx 运行日志",
            "log", Path.Combine("BepInEx", "LogOutput.log"), "BepInEx、插件加载、异常与翻译运行记录");

        AddAdditionalConfigArtifacts(artifacts, gamePath, ct);
        AddAdditionalLogArtifacts(artifacts, gamePath, ct);

        var ordered = artifacts
            .GroupBy(a => a.Id, StringComparer.Ordinal)
            .Select(g => g.First())
            .OrderByDescending(a => a.Required)
            .ThenBy(GetArtifactPriority)
            .ThenBy(a => a.Kind, StringComparer.Ordinal)
            .ThenBy(a => a.RelativePath, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var fingerprint = ComputeFingerprint(game, objectiveChecks, ordered);
        await Task.CompletedTask;
        return new DiagnosticArtifactSnapshot(fingerprint, ordered);
    }

    internal async Task<IReadOnlyList<DiagnosticArtifactContent>> ReadSelectedAsync(
        DiagnosticArtifactSnapshot snapshot,
        IReadOnlyDictionary<string, string?> requestedArtifacts,
        string gamePath,
        int totalCharacterBudget,
        CancellationToken ct)
    {
        var selected = snapshot.Artifacts
            .Where(a => a.Required || requestedArtifacts.ContainsKey(a.Id))
            .OrderByDescending(a => a.Required)
            .ThenBy(GetArtifactPriority)
            .ThenBy(a => a.Kind, StringComparer.Ordinal)
            .Take(MaxSelectedArtifacts)
            .ToList();

        var result = new List<DiagnosticArtifactContent>(selected.Count);
        var remaining = Math.Max(1_000, totalCharacterBudget);
        var requiredRemaining = selected.Count(artifact => artifact.Required);

        foreach (var artifact in selected)
        {
            ct.ThrowIfCancellationRequested();
            if (remaining <= 0)
                break;

            string content;
            var truncated = artifact.SourceTruncated;
            if (artifact.SyntheticContent is not null)
            {
                content = artifact.SyntheticContent;
            }
            else if (artifact.FullPath is not null)
            {
                if (!IsSafeRegularFile(gamePath, artifact.FullPath))
                {
                    logger.LogWarning("诊断资料 {ArtifactId} 已不再位于允许的普通文件路径内，已跳过", artifact.Id);
                    continue;
                }

                try
                {
                    (content, truncated) = artifact.Kind == "log"
                        ? await ReadLogTailAsync(artifact.FullPath, ct)
                        : await ReadTextPrefixAsync(artifact.FullPath, MaxConfigBytes, ct);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
                {
                    logger.LogWarning(ex, "诊断资料 {ArtifactId} 无法作为安全文本读取，已跳过", artifact.Id);
                    continue;
                }
            }
            else
            {
                continue;
            }

            var artifactBudget = artifact.Required && requiredRemaining > 0
                ? Math.Max(500, remaining / requiredRemaining)
                : remaining;
            if (content.Length > artifactBudget)
            {
                content = content[..artifactBudget];
                truncated = true;
            }

            var lines = content.Split(["\r\n", "\n", "\r"], StringSplitOptions.None);
            result.Add(new DiagnosticArtifactContent(
                artifact,
                lines,
                truncated,
                requestedArtifacts.GetValueOrDefault(artifact.Id)));
            remaining -= content.Length;
            if (artifact.Required)
                requiredRemaining--;
        }

        return result;
    }

    internal static PluginDiagnosticEvidence? BuildEvidence(
        IReadOnlyDictionary<string, DiagnosticArtifactContent> reviewed,
        string artifactId,
        int startLine,
        int endLine)
    {
        if (!reviewed.TryGetValue(artifactId, out var artifact) || artifact.Lines.Count == 0)
            return null;

        if (startLine < 1 || endLine < startLine || startLine > artifact.Lines.Count)
            return null;

        endLine = Math.Min(endLine, Math.Min(artifact.Lines.Count, startLine + 5));
        var excerpt = string.Join('\n', artifact.Lines.Skip(startLine - 1).Take(endLine - startLine + 1)).Trim();
        if (string.IsNullOrWhiteSpace(excerpt))
            return null;
        if (excerpt.Length > 600)
            excerpt = string.Concat(excerpt.AsSpan(0, 600), "...");

        return new PluginDiagnosticEvidence(
            artifactId,
            artifact.Descriptor.Label,
            artifact.Descriptor.RelativePath,
            startLine,
            endLine,
            excerpt);
    }

    internal static string SanitizeContent(string content, string gamePath)
    {
        if (string.IsNullOrEmpty(content))
            return content;

        var normalizedRoot = Path.GetFullPath(gamePath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var result = content.Replace(normalizedRoot, "[GAME]", StringComparison.OrdinalIgnoreCase);
        result = result.Replace(normalizedRoot.Replace('\\', '/'), "[GAME]", StringComparison.OrdinalIgnoreCase);
        var lines = result.Split(["\r\n", "\n", "\r"], StringSplitOptions.None);

        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            line = SensitiveAssignmentRegex().Replace(line, "$1[REDACTED]");
            line = XmlSecretValueRegex().Replace(line, "$1[REDACTED]");
            line = AuthorizationRegex().Replace(line, "$1[REDACTED]");
            line = SensitiveQueryRegex().Replace(line, "$1[REDACTED]");
            line = JwtRegex().Replace(line, "[REDACTED_TOKEN]");
            line = CommonSecretRegex().Replace(line, "[REDACTED_TOKEN]");
            line = UrlUserInfoRegex().Replace(line, "$1[REDACTED]@");
            line = UncAbsolutePathRegex().Replace(line, "[PATH]");
            line = WindowsAbsolutePathRegex().Replace(line, "[PATH]");
            line = WindowsForwardAbsolutePathRegex().Replace(line, "[PATH]");
            line = UnixAbsolutePathRegex().Replace(line, "[PATH]");
            lines[i] = line;
        }

        return string.Join('\n', lines);
    }

    private static string BuildEnvironmentSummary(Game game, IReadOnlyList<HealthCheckItem> checks)
    {
        var sb = new StringBuilder();
        sb.AppendLine("以下内容是只读收集的客观事实，不是诊断结论：");
        sb.AppendLine($"游戏：{game.Name}");
        sb.AppendLine($"安装状态：{game.InstallState}");
        sb.AppendLine($"Unity 版本：{game.DetectedInfo?.UnityVersion ?? "未知"}");
        sb.AppendLine($"脚本后端：{game.DetectedInfo?.Backend.ToString() ?? "未知"}");
        sb.AppendLine($"架构：{game.DetectedInfo?.Architecture.ToString() ?? "未知"}");
        sb.AppendLine($"BepInEx 记录版本：{game.InstalledBepInExVersion ?? "未知"}");
        sb.AppendLine($"XUnity 记录版本：{game.InstalledXUnityVersion ?? "未知"}");
        sb.AppendLine("关键运行文件：");
        AppendFileFact(sb, game.GamePath, game.ExecutableName ?? game.DetectedInfo?.DetectedExecutable);
        AppendFileFact(sb, game.GamePath, "UnityPlayer.dll");
        AppendFileFact(sb, game.GamePath, "GameAssembly.dll");
        AppendFileFact(sb, game.GamePath, Path.Combine("BepInEx", "core", "BepInEx.dll"));
        AppendFileFact(sb, game.GamePath, Path.Combine("BepInEx", "core", "BepInEx.Core.dll"));

        try
        {
            var dataDir = Directory.EnumerateDirectories(game.GamePath, "*_Data", SearchOption.TopDirectoryOnly)
                .FirstOrDefault(directory => IsSafeDirectory(game.GamePath, directory));
            if (dataDir is not null)
            {
                AppendFileFact(sb, game.GamePath,
                    Path.GetRelativePath(game.GamePath, Path.Combine(dataDir, "il2cpp_data", "Metadata", "global-metadata.dat")));
                var managedDir = Path.Combine(dataDir, "Managed");
                var managedSafe = IsSafeDirectory(game.GamePath, managedDir);
                var managedCount = managedSafe
                    ? Directory.EnumerateFiles(managedDir, "*.dll", SearchOption.TopDirectoryOnly).Take(5001).Count()
                    : 0;
                sb.AppendLine($"- {Path.GetRelativePath(game.GamePath, managedDir).Replace('\\', '/')}: " +
                              (managedSafe ? $"存在，顶层程序集 {managedCount} 个" : "不存在或已拒绝重解析点"));
            }
        }
        catch
        {
            sb.AppendLine("- Unity 数据目录：当前无法枚举");
        }

        sb.AppendLine("本地检查：");
        foreach (var check in checks)
            sb.AppendLine($"- [{check.Id}] {check.Label}: {check.Status}; {check.Detail ?? "已确认存在或配置有效"}");
        return sb.ToString();
    }

    private static void AppendFileFact(StringBuilder sb, string gamePath, string? relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
            return;

        try
        {
            var fullPath = Path.GetFullPath(Path.Combine(gamePath, relativePath));
            var displayPath = Path.GetRelativePath(gamePath, fullPath).Replace('\\', '/');
            if (!File.Exists(fullPath))
            {
                sb.AppendLine($"- {displayPath}: 不存在");
                return;
            }
            if (!IsSafeRegularFile(gamePath, fullPath))
            {
                sb.AppendLine($"- {displayPath}: 已拒绝重解析点或越界路径");
                return;
            }

            var info = new FileInfo(fullPath);
            var version = FileVersionInfo.GetVersionInfo(fullPath).FileVersion;
            sb.AppendLine($"- {displayPath}: 存在，{info.Length} 字节，文件版本 {version ?? "未知"}");
        }
        catch
        {
            sb.AppendLine($"- {relativePath.Replace('\\', '/')}: 无法读取元数据");
        }
    }

    private (List<(string FullPath, string RelativePath, bool Enabled, PluginAssemblyMetadata? Metadata)> Plugins,
        bool Truncated) CollectPluginMetadata(
        string gamePath,
        CancellationToken ct)
    {
        var pluginsDir = Path.Combine(gamePath, "BepInEx", "plugins");
        if (!Directory.Exists(pluginsDir))
            return ([], false);

        var result = new List<(string, string, bool, PluginAssemblyMetadata?)>();
        var truncated = false;
        try
        {
            foreach (var fullPath in Directory.EnumerateFiles(pluginsDir, "*", RecursiveEnumeration)
                         .Where(path => path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)
                                     || path.EndsWith(".dll.disabled", StringComparison.OrdinalIgnoreCase)))
            {
                ct.ThrowIfCancellationRequested();
                if (!IsSafeRegularFile(gamePath, fullPath))
                    continue;
                if (result.Count >= MaxPluginAssemblies)
                {
                    truncated = true;
                    break;
                }

                var relative = Path.GetRelativePath(gamePath, fullPath).Replace('\\', '/');
                result.Add((
                    fullPath,
                    relative,
                    !fullPath.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase),
                    TryReadAssemblyMetadata(fullPath)));
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "读取插件元数据清单失败");
        }

        return (result, truncated);
    }

    private static string BuildPluginInventory(
        IReadOnlyList<(string FullPath, string RelativePath, bool Enabled, PluginAssemblyMetadata? Metadata)> plugins,
        string gamePath,
        bool truncated)
    {
        var availableAssemblies = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var plugin in plugins)
        {
            var name = plugin.Metadata?.AssemblyName ?? Path.GetFileNameWithoutExtension(plugin.FullPath);
            availableAssemblies.Add(name);
        }

        foreach (var directory in GetDependencySearchDirectories(gamePath))
        {
            if (!IsSafeDirectory(gamePath, directory))
                continue;
            try
            {
                foreach (var file in Directory.EnumerateFiles(directory, "*.dll", SearchOption.TopDirectoryOnly))
                    availableAssemblies.Add(Path.GetFileNameWithoutExtension(file));
            }
            catch
            {
                // Inventory is best effort; inaccessible directories remain visible in the environment summary.
            }
        }

        var sb = new StringBuilder();
        sb.AppendLine("插件程序集清单（未解析引用只是观察项，不能单独证明依赖缺失）：");
        if (truncated)
            sb.AppendLine($"- 清单超过安全上限，仅列出前 {MaxPluginAssemblies} 个普通插件文件");
        foreach (var plugin in plugins.OrderBy(p => p.RelativePath, StringComparer.OrdinalIgnoreCase))
        {
            var metadata = plugin.Metadata;
            sb.AppendLine($"- 路径={plugin.RelativePath}; 状态={(plugin.Enabled ? "启用" : "禁用")}; " +
                          $"程序集={metadata?.AssemblyName ?? "无法读取"}; 程序集版本={metadata?.AssemblyVersion ?? "未知"}; " +
                          $"文件版本={metadata?.FileVersion ?? "未知"}");

            if (metadata is { References.Count: > 0 })
            {
                var unresolved = metadata.References
                    .Where(reference => !availableAssemblies.Contains(reference))
                    .Where(reference => !IsFrameworkReference(reference))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Take(20)
                    .ToList();
                sb.AppendLine($"  引用={string.Join(", ", metadata.References.Take(40))}");
                if (unresolved.Count > 0)
                    sb.AppendLine($"  标准搜索目录未发现={string.Join(", ", unresolved)}（仅供结合日志判断）");
            }
        }

        return sb.ToString();
    }

    private static IReadOnlyList<string> GetDependencySearchDirectories(string gamePath)
    {
        var directories = new List<string>
        {
            Path.Combine(gamePath, "BepInEx", "core"),
            Path.Combine(gamePath, "BepInEx", "interop")
        };

        try
        {
            var dataDir = Directory.EnumerateDirectories(gamePath, "*_Data", SearchOption.TopDirectoryOnly)
                .FirstOrDefault(directory => IsSafeDirectory(gamePath, directory));
            if (dataDir is not null)
                directories.Add(Path.Combine(dataDir, "Managed"));
        }
        catch
        {
            // Best effort inventory.
        }

        return directories;
    }

    internal static PluginAssemblyMetadata? TryReadAssemblyMetadata(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            using var peReader = new PEReader(stream, PEStreamOptions.PrefetchMetadata);
            if (!peReader.HasMetadata)
                return null;

            var metadata = peReader.GetMetadataReader();
            if (!metadata.IsAssembly)
                return null;

            var definition = metadata.GetAssemblyDefinition();
            var name = metadata.GetString(definition.Name);
            var references = metadata.AssemblyReferences
                .Select(handle => metadata.GetString(metadata.GetAssemblyReference(handle).Name))
                .Where(reference => !string.IsNullOrWhiteSpace(reference))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(reference => reference, StringComparer.OrdinalIgnoreCase)
                .ToList();
            var version = definition.Version.ToString();
            var fileVersion = FileVersionInfo.GetVersionInfo(path).FileVersion;
            return new PluginAssemblyMetadata(name, version, fileVersion, references);
        }
        catch
        {
            return null;
        }
    }

    private static bool IsFrameworkReference(string name) =>
        name.Equals("mscorlib", StringComparison.OrdinalIgnoreCase)
        || name.Equals("netstandard", StringComparison.OrdinalIgnoreCase)
        || name.StartsWith("System", StringComparison.OrdinalIgnoreCase)
        || name.StartsWith("Microsoft.", StringComparison.OrdinalIgnoreCase)
        || name.StartsWith("UnityEngine", StringComparison.OrdinalIgnoreCase);

    private static void AddKnownTextArtifact(
        List<DiagnosticArtifactDescriptor> artifacts,
        string gamePath,
        string id,
        string label,
        string kind,
        string relativePath,
        string summary)
    {
        var fullPath = Path.GetFullPath(Path.Combine(gamePath, relativePath));
        if (!File.Exists(fullPath) || !IsSafeRegularFile(gamePath, fullPath))
            return;

        var info = new FileInfo(fullPath);
        artifacts.Add(new(
            id,
            label,
            kind,
            Path.GetRelativePath(gamePath, fullPath).Replace('\\', '/'),
            summary,
            info.Length,
            info.LastWriteTimeUtc,
            fullPath,
            null,
            Required: false));
    }

    private static void AddAdditionalConfigArtifacts(
        List<DiagnosticArtifactDescriptor> artifacts,
        string gamePath,
        CancellationToken ct)
    {
        var configDir = Path.Combine(gamePath, "BepInEx", "config");
        if (!Directory.Exists(configDir))
            return;

        try
        {
            foreach (var fullPath in Directory.EnumerateFiles(configDir, "*.cfg", RecursiveEnumeration)
                         .Take(MaxConfigArtifacts))
            {
                ct.ThrowIfCancellationRequested();
                if (!IsSafeRegularFile(gamePath, fullPath))
                    continue;

                var relative = Path.GetRelativePath(gamePath, fullPath).Replace('\\', '/');
                if (artifacts.Any(a => string.Equals(a.RelativePath, relative, StringComparison.OrdinalIgnoreCase)))
                    continue;

                var info = new FileInfo(fullPath);
                artifacts.Add(new(
                    CreateArtifactId("config", relative),
                    Path.GetFileName(fullPath),
                    "config",
                    relative,
                    "第三方 BepInEx 插件配置；仅在 AI 明确选择后读取原文",
                    info.Length,
                    info.LastWriteTimeUtc,
                    fullPath,
                    null,
                    Required: false));
            }
        }
        catch
        {
            // Best effort; inaccessible config folders are omitted.
        }
    }

    private static void AddAdditionalLogArtifacts(
        List<DiagnosticArtifactDescriptor> artifacts,
        string gamePath,
        CancellationToken ct)
    {
        var bepInExDir = Path.Combine(gamePath, "BepInEx");
        if (!Directory.Exists(bepInExDir))
            return;

        try
        {
            foreach (var fullPath in Directory.EnumerateFiles(bepInExDir, "*.log", SearchOption.TopDirectoryOnly).Take(8))
            {
                ct.ThrowIfCancellationRequested();
                if (!IsSafeRegularFile(gamePath, fullPath))
                    continue;

                var relative = Path.GetRelativePath(gamePath, fullPath).Replace('\\', '/');
                if (artifacts.Any(a => string.Equals(a.RelativePath, relative, StringComparison.OrdinalIgnoreCase)))
                    continue;

                var info = new FileInfo(fullPath);
                artifacts.Add(new(
                    CreateArtifactId("log", relative),
                    Path.GetFileName(fullPath),
                    "log",
                    relative,
                    "BepInEx 辅助运行日志",
                    info.Length,
                    info.LastWriteTimeUtc,
                    fullPath,
                    null,
                    Required: false));
            }
        }
        catch
        {
            // Best effort; inaccessible logs are omitted.
        }
    }

    private static string CreateArtifactId(string kind, string relativePath)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(relativePath.ToLowerInvariant()));
        return $"{kind}-{Convert.ToHexString(hash.AsSpan(0, 6)).ToLowerInvariant()}";
    }

    private static int GetArtifactPriority(DiagnosticArtifactDescriptor artifact) => artifact.Id switch
    {
        "environment" => 0,
        "plugin-inventory" => 1,
        "bepinex-log" => 2,
        "xunity-config" => 3,
        "doorstop-config" => 4,
        "bepinex-config" => 5,
        _ when artifact.Kind == "log" => 6,
        _ => 7
    };

    private static string ComputeFingerprint(
        Game game,
        IReadOnlyList<HealthCheckItem> checks,
        IReadOnlyList<DiagnosticArtifactDescriptor> artifacts)
    {
        var sb = new StringBuilder();
        sb.Append(game.Id).Append('|')
            .Append(game.InstallState).Append('|')
            .Append(game.DetectedInfo?.UnityVersion).Append('|')
            .Append(game.DetectedInfo?.Backend).Append('|')
            .Append(game.DetectedInfo?.Architecture).AppendLine();

        foreach (var check in checks
                     .Where(check => !string.Equals(check.Id, "toolboxConnectivity", StringComparison.Ordinal))
                     .OrderBy(c => c.Id, StringComparer.Ordinal))
            sb.Append(check.Id).Append('|').Append(check.Status).Append('|').Append(check.Detail).AppendLine();
        foreach (var artifact in artifacts.OrderBy(a => a.Id, StringComparer.Ordinal))
        {
            sb.Append(artifact.Id).Append('|').Append(artifact.RelativePath).Append('|');
            if (artifact.SyntheticContent is not null)
            {
                var fingerprintContent = artifact.Id == "environment"
                    ? string.Join('\n', artifact.SyntheticContent
                        .Split(["\r\n", "\n", "\r"], StringSplitOptions.None)
                        .Where(line => !line.Contains("[toolboxConnectivity]", StringComparison.Ordinal)))
                    : artifact.SyntheticContent;
                sb.Append(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(fingerprintContent)))).AppendLine();
            }
            else
            {
                sb.Append(artifact.Size).Append('|').Append(artifact.LastModifiedUtc?.Ticks).AppendLine();
            }
        }

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString()))).ToLowerInvariant();
    }

    internal static bool IsSafeRegularFile(string gamePath, string fullPath)
        => File.Exists(fullPath) && IsSafePathWithoutReparse(gamePath, fullPath);

    internal static bool IsSafeRegularDirectory(string gamePath, string fullPath)
        => Directory.Exists(fullPath) && IsSafePathWithoutReparse(gamePath, fullPath);

    private static bool IsSafeDirectory(string gamePath, string fullPath)
        => Directory.Exists(fullPath) && IsSafePathWithoutReparse(gamePath, fullPath);

    private static bool IsSafePathWithoutReparse(string gamePath, string fullPath)
    {
        try
        {
            var rootDirectory = Path.GetFullPath(gamePath)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var root = rootDirectory + Path.DirectorySeparatorChar;
            var normalized = Path.GetFullPath(fullPath);
            if (!string.Equals(normalized, rootDirectory, StringComparison.OrdinalIgnoreCase)
                && !normalized.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                return false;

            var current = normalized;
            while (true)
            {
                if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                    return false;
                if (string.Equals(current, rootDirectory, StringComparison.OrdinalIgnoreCase))
                    return true;
                current = Path.GetDirectoryName(current);
                if (current is null || !current.StartsWith(rootDirectory, StringComparison.OrdinalIgnoreCase))
                    return false;
            }
        }
        catch
        {
            return false;
        }
    }

    private static async Task<(string Content, bool Truncated)> ReadTextPrefixAsync(
        string path,
        int maxBytes,
        CancellationToken ct)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete, 16 * 1024, useAsync: true);
        var toRead = (int)Math.Min(stream.Length, maxBytes);
        var buffer = new byte[toRead];
        var read = await stream.ReadAsync(buffer.AsMemory(0, toRead), ct);
        var content = DecodeText(buffer.AsSpan(0, read));
        return (content, stream.Length > read);
    }

    private static async Task<(string Content, bool Truncated)> ReadLogTailAsync(string path, CancellationToken ct)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete, 64 * 1024, useAsync: true);
        var toRead = (int)Math.Min(stream.Length, MaxLogBytes);
        if (stream.Length > toRead)
            stream.Seek(-toRead, SeekOrigin.End);

        var buffer = new byte[toRead];
        var totalRead = 0;
        while (totalRead < toRead)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(totalRead, toRead - totalRead), ct);
            if (read == 0)
                break;
            totalRead += read;
        }

        var content = DecodeText(buffer.AsSpan(0, totalRead));
        var lines = content.Split(["\r\n", "\n", "\r"], StringSplitOptions.None);
        var truncated = stream.Length > totalRead || lines.Length > MaxLogLines;
        if (lines.Length > MaxLogLines)
            lines = lines[^MaxLogLines..];
        return (string.Join('\n', lines), truncated);
    }

    private static string DecodeText(ReadOnlySpan<byte> bytes)
    {
        string content;
        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
            content = Encoding.Unicode.GetString(bytes[2..]);
        else if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
            content = Encoding.BigEndianUnicode.GetString(bytes[2..]);
        else if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
            content = Encoding.UTF8.GetString(bytes[3..]);
        else
            content = Encoding.UTF8.GetString(bytes);

        if (!IsProbablyText(content))
            throw new InvalidDataException("资料包含非文本内容。");
        return content;
    }

    internal static bool IsProbablyText(string content)
    {
        if (content.IndexOf('\0') >= 0)
            return false;

        var suspicious = content.Count(character =>
            (char.IsControl(character) && character is not ('\r' or '\n' or '\t' or '\u001b'))
            || character == '\uFFFD');
        return suspicious <= Math.Max(2, content.Length / 100);
    }

    [GeneratedRegex(@"(?im)^(\s*[^#;\r\n]*?(?:api[\s_-]*key|access[\s_-]*token|token|secret|password|passwd|authorization|cookie|client[\s_-]*secret|subscription[\s_-]*key|game[\s_-]*id|user(?:[\s_-]*name)?)\s*[\""']?\s*[=:]\s*[\""']?).*$")]
    private static partial Regex SensitiveAssignmentRegex();

    [GeneratedRegex(@"(?i)(\bkey\s*=\s*[\""'](?:api[\s_-]*key|access[\s_-]*token|token|secret|password|passwd|authorization|cookie|client[\s_-]*secret|subscription[\s_-]*key|game[\s_-]*id|user(?:[\s_-]*name)?)[\""'][^>\r\n]*?\bvalue\s*=\s*[\""'])[^\""']+")]
    private static partial Regex XmlSecretValueRegex();

    [GeneratedRegex(@"(?i)(\b(?:Bearer|Basic)\s+)[A-Za-z0-9._~+\-/=]+")]
    private static partial Regex AuthorizationRegex();

    [GeneratedRegex(@"(?i)([?&](?:api[_-]?key|access[_-]?token|token|secret|password|signature|sig|auth|authorization)=)[^&\s]+")]
    private static partial Regex SensitiveQueryRegex();

    [GeneratedRegex(@"\beyJ[A-Za-z0-9_-]{10,}\.[A-Za-z0-9_-]{10,}(?:\.[A-Za-z0-9_-]{10,})?\b")]
    private static partial Regex JwtRegex();

    [GeneratedRegex(@"(?i)\b(?:sk-[A-Za-z0-9_-]{16,}|gh[pousr]_[A-Za-z0-9]{20,}|hf_[A-Za-z0-9]{20,}|AIza[A-Za-z0-9_-]{20,}|AKIA[A-Z0-9]{16})\b")]
    private static partial Regex CommonSecretRegex();

    [GeneratedRegex(@"(?i)(https?:\/\/)[^\s\/@:]+:[^\s\/@]+@")]
    private static partial Regex UrlUserInfoRegex();

    [GeneratedRegex(@"(?i)\b[A-Z]:\\(?:[^\s\\/:*?\""<>|]+\\)*[^\s\\/:*?\""<>|]*")]
    private static partial Regex WindowsAbsolutePathRegex();

    [GeneratedRegex(@"(?i)\b[A-Z]:/(?:[^\s/:*?\""<>|]+/)*[^\s/:*?\""<>|]*")]
    private static partial Regex WindowsForwardAbsolutePathRegex();

    [GeneratedRegex(@"\\\\[^\s\\/:*?\""<>|]+\\[^\s\\/:*?\""<>|]+(?:\\[^\s\\/:*?\""<>|]+)*")]
    private static partial Regex UncAbsolutePathRegex();

    [GeneratedRegex(@"(?<!:)\/(?:home|Users|usr|var|opt|tmp)\/[^\s,;\""']+")]
    private static partial Regex UnixAbsolutePathRegex();
}
