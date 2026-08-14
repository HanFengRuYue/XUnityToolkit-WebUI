using System.IO.Compression;
using System.Text;
using XUnityToolkit_WebUI.Infrastructure;
using XUnityToolkit_WebUI.Models;

namespace XUnityToolkit_WebUI.Services;

public sealed class PluginAutoRepairService(
    BepInExInstallerService bepInExInstaller,
    XUnityInstallerService xUnityInstaller,
    BundledAssetPaths bundledAssets,
    ConfigurationService configurationService,
    BepInExPluginService pluginService,
    AppDataPaths paths,
    ILogger<PluginAutoRepairService> logger)
{
    internal async Task<List<PluginRepairActionResult>> ExecuteAsync(
        Game game,
        PluginHealthReport report,
        DiagnosticArtifactSnapshot snapshot,
        IReadOnlyList<PluginRepairPlanAction> aiActions,
        CancellationToken ct)
    {
        var actions = BuildCombinedPlan(game, report, aiActions);
        if (actions.Count == 0)
            return [];

        if (GameProcessHelper.IsGameRunning(game))
        {
            return actions.Select((action, index) => new PluginRepairActionResult(
                $"repair-{index + 1}",
                action.Tool ?? "unknown",
                action.Description ?? "修复操作",
                PluginRepairActionState.Skipped,
                "游戏正在运行；为避免损坏正在使用的插件或配置，本次未修改文件。请退出游戏后重试。",
                GetTarget(action))).ToList();
        }

        var repairRoot = Path.Combine(
            paths.BackupDirectory(game.Id),
            "agent-repair",
            DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fffffff"));
        var results = new List<PluginRepairActionResult>();
        for (var index = 0; index < actions.Count; index++)
        {
            ct.ThrowIfCancellationRequested();
            var action = actions[index];
            var id = $"repair-{index + 1}";
            try
            {
                var message = await ExecuteOneAsync(game, snapshot, action, repairRoot, ct);
                results.Add(new PluginRepairActionResult(
                    id,
                    action.Tool ?? "unknown",
                    action.Description ?? "修复操作",
                    message.Skipped ? PluginRepairActionState.Skipped : PluginRepairActionState.Completed,
                    message.Message,
                    GetTarget(action)));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "插件自动修复操作失败: {GameId} {Tool}", game.Id, action.Tool);
                results.Add(new PluginRepairActionResult(
                    id,
                    action.Tool ?? "unknown",
                    action.Description ?? "修复操作",
                    PluginRepairActionState.Failed,
                    UserFacingError(ex),
                    GetTarget(action)));
            }
        }

        return results;
    }

    private List<PluginRepairPlanAction> BuildCombinedPlan(
        Game game,
        PluginHealthReport report,
        IReadOnlyList<PluginRepairPlanAction> aiActions)
    {
        var actions = new List<PluginRepairPlanAction>();
        var unhealthyIds = report.Checks
            .Where(check => check.Status == HealthStatus.Error)
            .Select(check => check.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (unhealthyIds.Overlaps(["doorstopProxy", "doorstopConfig", "bepinexCore"]))
        {
            actions.Add(ComponentAction("bepinex", "恢复缺失或损坏的 BepInEx / Doorstop 核心文件。"));
        }

        if (unhealthyIds.Contains("xunityPlugin") || unhealthyIds.Contains("translatorConfig"))
        {
            actions.Add(ComponentAction("xunity", "恢复 XUnity.AutoTranslator 插件与基础配置。"));
        }

        if (unhealthyIds.Contains("translatorEndpoint") || unhealthyIds.Contains("translatorEndpointVersion"))
        {
            var endpoint = xUnityInstaller.GetTranslatorEndpointStatus(game);
            if (endpoint.Origin is TranslatorEndpointOrigin.Missing or TranslatorEndpointOrigin.OfficialOutdated)
                actions.Add(ComponentAction("translator_endpoint", "安装或升级工具箱官方 AI 翻译端点。"));
        }

        if (unhealthyIds.Contains("translatorRouting") || unhealthyIds.Contains("translatorConfig"))
        {
            actions.Add(ComponentAction("translator_routing", "恢复 XUnity 到当前工具箱实例的端点路由配置。"));
        }

        actions.AddRange(aiActions);
        return actions
            .Where(action => !string.IsNullOrWhiteSpace(action.Tool))
            .DistinctBy(ActionKey, StringComparer.OrdinalIgnoreCase)
            .Take(12)
            .ToList();
    }

    private async Task<(bool Skipped, string Message)> ExecuteOneAsync(
        Game game,
        DiagnosticArtifactSnapshot snapshot,
        PluginRepairPlanAction action,
        string repairRoot,
        CancellationToken ct)
    {
        switch (action.Tool?.Trim().ToLowerInvariant())
        {
            case "reinstall_component":
                return await ReinstallComponentAsync(game, action.Component, repairRoot, ct);
            case "disable_plugin":
                return await DisablePluginAsync(game, action.RelativePath, repairRoot);
            case "set_ini_value":
                return await SetIniValueAsync(game, snapshot, action, repairRoot, ct);
            default:
                throw new InvalidDataException("AI 返回了不受支持的修复工具。");
        }
    }

    private async Task<(bool Skipped, string Message)> ReinstallComponentAsync(
        Game game,
        string? component,
        string repairRoot,
        CancellationToken ct)
    {
        if (game.DetectedInfo is null)
            throw new InvalidOperationException("缺少 Unity 检测信息，无法选择匹配的安装包。");

        switch (component?.ToLowerInvariant())
        {
            case "bepinex":
            {
                var zip = bepInExInstaller.ResolveBundledZip(game.DetectedInfo, bundledAssets);
                BackupZipTargets(game, zip, repairRoot);
                var installed = await bepInExInstaller.InstallAsync(game.GamePath, zip, ct);
                return (false, $"已从内置包恢复 BepInEx，共写入 {installed.Count} 个文件；覆盖前文件已备份。");
            }
            case "xunity":
            {
                var zip = xUnityInstaller.ResolveBundledZip(game.DetectedInfo, bundledAssets);
                BackupZipTargets(game, zip, repairRoot);
                BackupIfExists(game, TranslatorEndpointPath(game), repairRoot);
                var installed = await xUnityInstaller.InstallAsync(game.GamePath, zip, ct);
                xUnityInstaller.EnsureTranslatorEndpoint(game);
                return (false, $"已从内置包恢复 XUnity.AutoTranslator，共写入 {installed.Count} 个文件；覆盖前文件已备份。");
            }
            case "translator_endpoint":
            {
                var status = xUnityInstaller.GetTranslatorEndpointStatus(game);
                if (status.Origin is TranslatorEndpointOrigin.OfficialCurrent or TranslatorEndpointOrigin.CompatibleCurrent)
                    return (true, "AI 翻译端点已经是当前官方版或同版兼容构建，无需替换。");
                if (status.Origin == TranslatorEndpointOrigin.UnknownOrCustom)
                    return (true, "检测到未知或自定义端点；自动修复不会在没有用户单独确认时覆盖它。");

                var endpointPath = TranslatorEndpointPath(game);
                BackupIfExists(game, endpointPath, repairRoot);
                var updated = xUnityInstaller.EnsureTranslatorEndpoint(game);
                return (false, updated.Message);
            }
            case "translator_routing":
            {
                var configPath = Path.Combine(game.GamePath, "BepInEx", "config", "AutoTranslatorConfig.ini");
                if (!File.Exists(configPath))
                    throw new FileNotFoundException("AutoTranslatorConfig.ini 不存在，无法修复端点路由。");
                BackupIfExists(game, configPath, repairRoot);
                await configurationService.PatchSectionAsync(game.GamePath, "Service",
                    new Dictionary<string, string> { ["Endpoint"] = "LLMTranslate" }, ct);
                await configurationService.PatchTranslatorEndpointAsync(game.GamePath, game.Id, ct);
                return (false, "已恢复 Service.Endpoint、ToolkitUrl、DiscoveryFile、GameId 与并发设置。");
            }
            default:
                throw new InvalidDataException("未知的工具箱组件修复类型。");
        }
    }

    private async Task<(bool Skipped, string Message)> DisablePluginAsync(
        Game game,
        string? relativePath,
        string repairRoot)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
            throw new InvalidDataException("缺少第三方插件相对路径。");
        var plugins = await pluginService.ListPluginsAsync(game);
        var plugin = plugins.FirstOrDefault(item =>
            string.Equals(item.RelativePath.Replace('/', '\\'), relativePath.Replace('/', '\\'),
                StringComparison.OrdinalIgnoreCase));
        if (plugin is null)
            throw new FileNotFoundException("修复计划中的第三方插件已不存在。");
        if (plugin.IsToolkitManaged)
            return (true, "该插件由工具箱管理，自动修复不会将其禁用。");
        if (!plugin.Enabled)
            return (true, "该第三方插件已经处于禁用状态。");

        var pluginPath = PathSecurity.SafeJoin(game.GamePath, plugin.RelativePath);
        if (!PluginDiagnosticArtifactCollector.IsSafeRegularFile(game.GamePath, pluginPath))
            throw new InvalidOperationException("第三方插件已移出安全游戏路径或包含重解析点。");
        BackupIfExists(game, pluginPath, repairRoot);
        var updated = await pluginService.TogglePluginAsync(game, plugin.RelativePath);
        return (false, $"已备份并将第三方插件 {updated.FileName} 重命名为禁用状态；可在插件管理页重新启用。");
    }

    private async Task<(bool Skipped, string Message)> SetIniValueAsync(
        Game game,
        DiagnosticArtifactSnapshot snapshot,
        PluginRepairPlanAction action,
        string repairRoot,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(action.ArtifactId)
            || string.IsNullOrWhiteSpace(action.Section)
            || string.IsNullOrWhiteSpace(action.Key)
            || action.Value is null)
            throw new InvalidDataException("INI 修复参数不完整。");

        var artifact = snapshot.Artifacts.FirstOrDefault(item => item.Id == action.ArtifactId)
                       ?? throw new InvalidDataException("修复计划引用了未知资料。");
        if (string.IsNullOrWhiteSpace(artifact.RelativePath) || string.IsNullOrWhiteSpace(artifact.FullPath))
            throw new InvalidDataException("该诊断资料不是可修改的配置文件。");
        if (!Path.GetExtension(artifact.RelativePath).Equals(".ini", StringComparison.OrdinalIgnoreCase)
            && !Path.GetExtension(artifact.RelativePath).Equals(".cfg", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("自动配置修复只允许 .ini 和 .cfg 文件。");
        if (!PluginDiagnosticArtifactCollector.IsSafeRegularFile(game.GamePath, artifact.FullPath))
            throw new InvalidOperationException("目标配置已移出安全游戏路径或包含重解析点。");

        var relative = artifact.RelativePath.Replace('\\', '/');
        if (!relative.Equals("doorstop_config.ini", StringComparison.OrdinalIgnoreCase)
            && !relative.StartsWith("BepInEx/config/", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("自动 INI 修复仅限 Doorstop 与 BepInEx 配置目录。");

        BackupIfExists(game, artifact.FullPath, repairRoot);
        var content = await File.ReadAllTextAsync(artifact.FullPath, ct);
        var updated = SetIniValue(content, action.Section, action.Key, action.Value);
        await WriteTextAtomicAsync(artifact.FullPath, updated, ct);
        return (false, $"已备份并更新 {relative} 的 [{action.Section}] {action.Key}。 ");
    }

    internal static string SetIniValue(string content, string section, string key, string value)
    {
        var lines = content.Split(["\r\n", "\n", "\r"], StringSplitOptions.None).ToList();
        var sectionStart = -1;
        var sectionEnd = lines.Count;
        for (var index = 0; index < lines.Count; index++)
        {
            var trimmed = lines[index].Trim();
            if (!trimmed.StartsWith('[') || !trimmed.EndsWith(']'))
                continue;
            var name = trimmed[1..^1].Trim();
            if (sectionStart >= 0)
            {
                sectionEnd = index;
                break;
            }
            if (name.Equals(section, StringComparison.OrdinalIgnoreCase))
                sectionStart = index;
        }

        if (sectionStart < 0)
        {
            if (lines.Count > 0 && !string.IsNullOrEmpty(lines[^1]))
                lines.Add(string.Empty);
            lines.Add($"[{section}]");
            lines.Add($"{key}={value}");
        }
        else
        {
            var replaced = false;
            for (var index = sectionStart + 1; index < sectionEnd; index++)
            {
                var trimmed = lines[index].Trim();
                if (trimmed.StartsWith('#') || trimmed.StartsWith(';'))
                    continue;
                var separator = trimmed.IndexOf('=');
                if (separator <= 0 || !trimmed[..separator].Trim().Equals(key, StringComparison.OrdinalIgnoreCase))
                    continue;
                lines[index] = $"{key}={value}";
                replaced = true;
                break;
            }
            if (!replaced)
                lines.Insert(sectionEnd, $"{key}={value}");
        }

        while (lines.Count > 1 && string.IsNullOrEmpty(lines[^1]) && string.IsNullOrEmpty(lines[^2]))
            lines.RemoveAt(lines.Count - 1);
        return string.Join(Environment.NewLine, lines).TrimEnd() + Environment.NewLine;
    }

    private void BackupZipTargets(Game game, string zipPath, string repairRoot)
    {
        using var archive = ZipFile.OpenRead(zipPath);
        foreach (var entry in archive.Entries)
        {
            if (string.IsNullOrEmpty(entry.Name))
                continue;
            var target = PathSecurity.SafeJoin(game.GamePath, entry.FullName);
            BackupIfExists(game, target, repairRoot);
        }
    }

    private void BackupIfExists(Game game, string fullPath, string repairRoot)
    {
        if (!File.Exists(fullPath))
            return;
        var relative = Path.GetRelativePath(game.GamePath, fullPath);
        var backup = PathSecurity.SafeJoin(repairRoot, relative);
        if (File.Exists(backup))
            return;
        Directory.CreateDirectory(Path.GetDirectoryName(backup)!);
        File.Copy(fullPath, backup, overwrite: false);
    }

    private static async Task WriteTextAtomicAsync(string path, string content, CancellationToken ct)
    {
        var temp = path + $".repair-{Guid.NewGuid():N}.tmp";
        try
        {
            await File.WriteAllTextAsync(temp, content, new UTF8Encoding(false), ct);
            File.Move(temp, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temp))
                File.Delete(temp);
        }
    }

    private static PluginRepairPlanAction ComponentAction(string component, string description) => new()
    {
        Tool = "reinstall_component",
        Component = component,
        Description = description
    };

    private static string ActionKey(PluginRepairPlanAction action) =>
        string.Join('|', action.Tool, action.Component, action.ArtifactId, action.RelativePath,
            action.Section, action.Key, action.Value);

    private static string? GetTarget(PluginRepairPlanAction action) =>
        action.RelativePath ?? action.Component ?? action.ArtifactId;

    private static string TranslatorEndpointPath(Game game) =>
        Path.Combine(game.GamePath, "BepInEx", "plugins", "XUnity.AutoTranslator",
            "Translators", "LLMTranslate.dll");

    private static string UserFacingError(Exception exception) => exception switch
    {
        FileNotFoundException => exception.Message,
        InvalidDataException => exception.Message,
        InvalidOperationException => exception.Message,
        UnauthorizedAccessException => "没有权限修改目标文件；已保留原文件。",
        IOException => "目标文件正在使用或无法写入；已保留原文件。",
        _ => "修复执行失败，请查看工具箱日志。"
    };
}
