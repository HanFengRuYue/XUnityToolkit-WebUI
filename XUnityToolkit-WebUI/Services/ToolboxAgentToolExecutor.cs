using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using XUnityToolkit_WebUI.Infrastructure;
using XUnityToolkit_WebUI.Models;

namespace XUnityToolkit_WebUI.Services;

internal sealed record ToolboxAgentToolResult(
    bool Success,
    string Description,
    string ModelContent,
    string UserMessage,
    bool RequiresConfirmation = false,
    bool TerminatesTurn = false,
    bool ReloadRequired = false);

public sealed partial class ToolboxAgentToolExecutor(
    GameLibraryService gameLibrary,
    PluginHealthCheckService healthService,
    ToolboxAgentAttachmentStore attachmentStore,
    ToolboxAgentHostAccessService hostAccessService,
    ToolboxDataResetService dataResetService,
    FontReplacementService fontReplacementService,
    TmpFontGeneratorService fontGenerator,
    ConfigurationService configurationService,
    BepInExPluginService pluginService,
    InstallOrchestrator installOrchestrator,
    AppSettingsService settingsService,
    AppDataPaths appDataPaths,
    ToolkitRuntimeEndpointState runtimeEndpoint,
    IHttpClientFactory httpClientFactory,
    ILogger<ToolboxAgentToolExecutor> logger)
{
    private const int MaxModelToolOutputCharacters = 40_000;
    private const int MaxReadableTextBytes = 512 * 1024;

    private static readonly HashSet<string> ReadableExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".txt", ".ini", ".cfg", ".json", ".xml", ".yaml", ".yml", ".log", ".md", ".csv"
    };

    private static readonly HashSet<string> WritableExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".txt", ".ini", ".cfg", ".json", ".xml", ".yaml", ".yml"
    };

    private static readonly HashSet<string> SupportedCharsets = new(StringComparer.OrdinalIgnoreCase)
    {
        "GB2312", "GBK", "CJK_Common", "CJK_Full", "Japanese"
    };

    internal static readonly string ToolCatalog = """
        可用工具：
        1. describe_capabilities {}：取得全部可调用 API 类别、路径和常用请求体提示；不确定接口时先调用。
        2. list_games {}：列出游戏及安装状态。
        3. inspect_game {"gameId":"..."}：读取指定游戏的基本状态。
        4. check_plugin_health {"gameId":"..."}：执行只读本地插件检查。
        5. auto_repair_plugins {"gameId":"..."}：运行云端插件诊断、备份、自动修复并复检。
        6. list_game_files {"gameId":"...","relativeDirectory":"BepInEx/config"}：列出游戏目录内文件。
        7. read_game_file {"gameId":"...","relativePath":"BepInEx/config/example.cfg"}：读取允许的文本文件；返回内容会脱敏。
        8. patch_game_file {"gameId":"...","relativePath":"...","searchText":"精确旧文本","replacementText":"新文本"}：精确替换一处文本，先备份再原子写入。
        9. apply_custom_font {"gameId":"...","attachmentId":"...","characterSets":["GB2312"]}：将上传的 TTF/OTF 生成 TMP 字体，替换可支持的 TMP/Legacy 字体并配置 XUnity fallback。
        10. update_toolbox_setting {"path":"aiTranslation.maxConcurrency","value":6}：补丁式更新安全设置，不读取或覆盖 API Key；支持的路径通过 describe_capabilities 查询。
        11. use_attachment {"gameId":"...","attachmentId":"...","purpose":"install_plugin|plugin_package_import|icon|cover|background|settings_import|font_generation_upload|font_replacement_ttf|font_replacement_tmp|charset|translation_charset"}：把上传文件交给对应工具箱功能。
        12. list_path {"scope":"game|toolbox|external","gameId":"可信游戏范围可选","path":"相对路径或外部绝对路径","purpose":"读取用途"}：列出目录；game/toolbox 属于可信根，external 每次都需要用户确认。
        13. read_file {"scope":"game|toolbox|external","gameId":"可信游戏范围可选","path":"相对路径或外部绝对路径","mode":"auto|text|hex|metadata","startLine":1,"maxLines":200,"offset":0,"length":4096,"purpose":"读取用途"}：读取任意文件原文、有限十六进制块或被动元数据；不会加载用户程序集。external 每次都需要用户确认。
        14. manage_files {"purpose":"修改用途","operations":[{"kind":"create_directory|write_text|copy|move|delete|copy_attachment","scope":"game|toolbox","gameId":"...","path":"相对路径","content":"...","source":{"scope":"game|toolbox","gameId":"...","path":"..."},"attachmentId":"...","overwrite":false,"recursive":false}]}：在已添加游戏目录或完整工具箱数据目录中批量修改文件。整个批次只确认一次，不自动备份。
        15. run_script {"shell":"powershell|cmd","script":"完整脚本","purpose":"具体诊断用途","timeoutSeconds":30}：以当前用户运行诊断脚本。每个脚本都必须单独确认并展示全文；提示词只允许读取和诊断，但后端无法从技术上保证脚本只读。
        16. reset_toolbox_data {"purpose":"为什么要清空"}：删除完整工具箱数据目录并重启工具箱；不会删除游戏目录。必须单独确认，并且必须作为本轮最后且唯一的终止操作。
        17. call_toolbox_api {"method":"GET|POST|PUT|DELETE","path":"/api/...","body":{}}：调用其余 JSON 工具箱接口。可覆盖游戏管理、检测/安装、配置、术语、脚本标签、译文编辑、翻译记忆、字体、插件、日志、更新、本地模型等现有功能；文件上传优先使用 use_attachment/apply_custom_font。

        call_toolbox_api 禁止访问智能体自身、任意主机文件浏览、完整设置密钥、数据重置、更新应用和二进制下载。DELETE、卸载、导入、启动游戏等高影响操作会先要求用户确认。
        """;

    private const string CapabilityCatalog = """
        游戏：GET /api/games；GET/PUT/DELETE /api/games/{id}；POST /api/games/{id}/detect、/launch、/open-folder；POST /api/games/add-with-detection {folderPath,exePath?}；POST /api/games/batch-add {parentFolderPath}。
        安装：GET /api/games/{id}/status；POST /api/games/{id}/install {installBepInEx,installXUnity,autoInstallTmpFont,autoDeployAiEndpoint,applyOptimalConfig}；DELETE /api/games/{id}/install；POST /api/games/{id}/cancel。
        XUnity 配置：GET/PUT /api/games/{id}/config；GET/PUT /api/games/{id}/config/raw，PUT raw 请求体必须是 {"content":"完整 INI 文本"}；GET/POST/DELETE /api/games/{id}/ai-endpoint；GET/POST/DELETE /api/games/{id}/tmp-font。
        术语与描述：GET/PUT /api/games/{id}/terms，PUT 请求体必须直接是术语数组，例如 [{"type":"translate","original":"Captain","translation":"船长","category":"character","isRegex":false,"caseSensitive":true,"exactMatch":true}]，不要再包 entries；POST /api/games/{id}/terms/import-from-game {"sourceGameId":"..."}；GET/PUT /api/games/{id}/description，PUT 请求体是 {"description":"..."}；兼容 glossary/do-not-translate；GET/PUT /api/games/{id}/script-tags，PUT 请求体是 {"presetVersion":1,"rules":[{"pattern":"^NPC:(.+)$","action":"Extract","description":"提取 NPC 文本","isBuiltin":false}]}，action 只能是 Extract 或 Exclude；GET /api/script-tag-presets。
        译文：GET/PUT /api/games/{id}/translation-editor，PUT 请求体是 {"entries":[{"original":"Hello","translation":"你好"}]}；POST .../translation-editor/import 请求体是 {"content":"Hello=你好"}；GET .../translation-editor/export；GET .../translation-memory/stats；DELETE .../translation-memory。
        插件：GET /api/games/{id}/plugins；POST .../plugins/toggle {relativePath}；DELETE .../plugins?relativePath=；GET .../plugins/config?configFile=；POST .../plugin-package/import {zipPath}；插件附件请用 use_attachment。
        诊断：GET /api/games/{id}/health-check；POST .../health-check/analyze、/repair、/verify；GET /api/games/{id}/bepinex-log?lines=；POST .../bepinex-log/analyze。
        字体替换：POST /api/games/{id}/font-replacement/scan、/replace {fonts:[{pathId,assetFile,sourceId}]}、/restore、/cancel；GET .../status；DELETE .../custom-fonts/{sourceId}。上传与全自动应用优先 use_attachment/apply_custom_font。
        字体生成：GET /api/font-generation/status、/history、/charsets、/report/{fileName}；POST .../generate {fileName,unityVersion,samplingSize,atlasWidth,atlasHeight,characterSet,renderMode,samplingSizeMode,paddingMode,paddingValue}、/cancel、/charset/preview、/install-tmp-font/{gameId} {fileName}；DELETE /api/font-generation/{fileName}。
        图片：封面、图标、背景的 search/grids/select/web-search/web-select/steam-search/steam-select 端点均位于 /api/games/{id}/；本地附件请用 use_attachment。
        AI 与日志：GET /api/translate/stats、/api/ai/extraction/stats、/api/logs、/api/logs/history；POST /api/ai/toggle {enabled}、/api/translate/test。/api/ai/models 需要在查询串传递 API Key，出于凭据隔离不能由智能体调用。
        本地模型管理：GET /api/local-llm/status、/gpus、/settings、/catalog、/downloads、/llama-status、/models；PUT /settings；POST /gpus/refresh、/test、/start、/stop、/llama-download、/llama-download/cancel、/download、/download/pause、/download/cancel、/models/add；DELETE /models/{id}。智能体自身仍只由云端 AI 驱动，但可以按用户要求管理本地模型。
        更新：GET /api/update/check、/status；POST /api/update/download、/cancel、/dismiss。为防止对话连接中途销毁进程，/api/update/apply 只能由用户在更新页面点击。
        设置：GET /api/settings/connection、/version；设置导入附件用 use_attachment。完整设置（含密钥）、全量重置和任意主机文件浏览不会暴露给云端智能体。
        安全设置补丁：update_toolbox_setting 支持 theme、modelDownloadSource、hfMirrorUrl、libraryViewMode、librarySortBy、accentColor、libraryCardSize、libraryGap、libraryShowLabels、receivePreReleaseUpdates、pageZoom；installOptions.autoInstallTmpFont/autoDeployAiEndpoint/autoGenerateConfig/autoApplyOptimalConfig/autoVerifyHealth；aiTranslation.enabled/activeMode/maxConcurrency/port/systemPrompt/temperature/contextSize/localContextSize/localMinP/localRepeatPenalty/termAuditEnabled/naturalTranslationMode/enableTranslationMemory/fuzzyMatchThreshold/glossaryExtractionEnabled；以及 aiTranslation.endpoints.{id}.name/modelName/apiFormat/reasoningEffort/priority/enabled。API Key、SteamGridDB Key 与端点 URL 必须由用户在设置页填写，绝不交给模型。
        """;

    internal async Task<ToolboxAgentToolResult> ExecuteAsync(
        string sessionId,
        ToolboxAgentToolCall call,
        string? selectedGameId,
        bool confirmed,
        CancellationToken ct)
    {
        var name = call.Name?.Trim().ToLowerInvariant()
                   ?? throw new InvalidDataException("智能体返回了没有名称的工具调用。");
        return name switch
        {
            "describe_capabilities" => Success("读取工具箱能力目录", CapabilityCatalog, "已读取工具箱能力目录。"),
            "list_games" => await ListGamesAsync(ct),
            "inspect_game" => await InspectGameAsync(call.Arguments, selectedGameId, ct),
            "check_plugin_health" => await CheckPluginHealthAsync(call.Arguments, selectedGameId, ct),
            "auto_repair_plugins" => await AutoRepairPluginsAsync(call.Arguments, selectedGameId, ct),
            "list_game_files" => await ListGameFilesAsync(call.Arguments, selectedGameId, ct),
            "read_game_file" => await ReadGameFileAsync(call.Arguments, selectedGameId, ct),
            "patch_game_file" => await PatchGameFileAsync(call.Arguments, selectedGameId, ct),
            "apply_custom_font" => await ApplyCustomFontAsync(sessionId, call.Arguments, selectedGameId, ct),
            "update_toolbox_setting" => await UpdateToolboxSettingAsync(call.Arguments, ct),
            "use_attachment" => await UseAttachmentAsync(sessionId, call.Arguments, selectedGameId, confirmed, ct),
            "list_path" => await hostAccessService.ListPathAsync(call.Arguments, selectedGameId, confirmed, ct),
            "read_file" => await hostAccessService.ReadFileAsync(call.Arguments, selectedGameId, confirmed, ct),
            "manage_files" => await hostAccessService.ManageFilesAsync(sessionId, call.Arguments, selectedGameId, confirmed, ct),
            "run_script" => await hostAccessService.RunScriptAsync(call.Arguments, confirmed, ct),
            "reset_toolbox_data" => await ResetToolboxDataAsync(call.Arguments, confirmed, ct),
            "call_toolbox_api" => await CallToolboxApiAsync(call.Arguments, confirmed, ct),
            _ => new ToolboxAgentToolResult(false, name, "未知工具。", $"智能体请求了未知工具 {name}。")
        };
    }

    internal static bool IsAllowedApiCall(string method, string path, out string? error)
    {
        error = null;
        method = method.ToUpperInvariant();
        if (method is not ("GET" or "POST" or "PUT" or "DELETE"))
        {
            error = "只允许 GET、POST、PUT、DELETE。";
            return false;
        }

        string decodedPath;
        try
        {
            decodedPath = DecodePathForPolicy(path);
        }
        catch (UriFormatException)
        {
            error = "API 路径编码无效。";
            return false;
        }
        if (!decodedPath.StartsWith("/api/", StringComparison.Ordinal)
            || decodedPath.Contains("..", StringComparison.Ordinal)
            || decodedPath.Contains('\\')
            || decodedPath.Contains('#')
            || decodedPath.Any(char.IsControl)
            || Uri.TryCreate(path, UriKind.Absolute, out _))
        {
            error = "API 路径无效。";
            return false;
        }

        var pathOnly = decodedPath.Split('?', 2)[0].TrimEnd('/');
        var containsSensitiveQuery = Regex.IsMatch(
            decodedPath,
            @"[?&](?:api[_-]?key|password|secret|authorization|(?:(?:access|refresh)[_-]?)?token)=",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        var binaryDownload = method.Equals("GET", StringComparison.OrdinalIgnoreCase)
                             && (pathOnly.EndsWith("/download", StringComparison.OrdinalIgnoreCase)
                                 || pathOnly.Contains("/download/", StringComparison.OrdinalIgnoreCase));
        if (pathOnly.StartsWith("/api/toolbox-agent", StringComparison.OrdinalIgnoreCase)
            || pathOnly.StartsWith("/api/filesystem", StringComparison.OrdinalIgnoreCase)
            || pathOnly.Equals("/api/settings", StringComparison.OrdinalIgnoreCase)
            || pathOnly.Equals("/api/settings/data-path", StringComparison.OrdinalIgnoreCase)
            || pathOnly.Equals("/api/settings/reset", StringComparison.OrdinalIgnoreCase)
            || pathOnly.Equals("/api/update/apply", StringComparison.OrdinalIgnoreCase)
            || pathOnly.Contains("from-path", StringComparison.OrdinalIgnoreCase)
            || pathOnly.EndsWith("/plugin-package/import", StringComparison.OrdinalIgnoreCase)
            || pathOnly.EndsWith("/plugins/install", StringComparison.OrdinalIgnoreCase)
            || pathOnly.EndsWith("/plugin-package/export", StringComparison.OrdinalIgnoreCase)
            || pathOnly.Equals("/api/settings/export", StringComparison.OrdinalIgnoreCase)
            || containsSensitiveQuery
            || binaryDownload)
        {
            error = "该接口包含密钥、任意主机路径、不可恢复重置、进程替换或二进制下载，不能由智能体直接调用。";
            return false;
        }

        return true;
    }

    internal static bool RequiresConfirmation(string method, string path)
    {
        string normalized;
        try
        {
            normalized = DecodePathForPolicy(path).Split('?', 2)[0].TrimEnd('/');
        }
        catch (UriFormatException)
        {
            return true;
        }
        if (method.Equals("DELETE", StringComparison.OrdinalIgnoreCase)
            || method.Equals("PUT", StringComparison.OrdinalIgnoreCase))
            return true;

        if (!method.Equals("POST", StringComparison.OrdinalIgnoreCase))
            return false;

        return normalized.Equals("/api/games", StringComparison.OrdinalIgnoreCase)
               || normalized.Equals("/api/games/add-with-detection", StringComparison.OrdinalIgnoreCase)
               || normalized.Equals("/api/games/batch-add", StringComparison.OrdinalIgnoreCase)
               || normalized.Contains("/uninstall", StringComparison.OrdinalIgnoreCase)
               || normalized.EndsWith("/install", StringComparison.OrdinalIgnoreCase)
               || normalized.EndsWith("/launch", StringComparison.OrdinalIgnoreCase)
               || normalized.EndsWith("/open-folder", StringComparison.OrdinalIgnoreCase)
               || normalized.EndsWith("/open-data-folder", StringComparison.OrdinalIgnoreCase)
               || normalized.EndsWith("/models/add", StringComparison.OrdinalIgnoreCase)
               || normalized.Contains("/import", StringComparison.OrdinalIgnoreCase)
               || normalized.EndsWith("/restore", StringComparison.OrdinalIgnoreCase)
               || normalized.EndsWith("/start", StringComparison.OrdinalIgnoreCase)
               || normalized.EndsWith("/stop", StringComparison.OrdinalIgnoreCase)
               || (normalized.Contains("download", StringComparison.OrdinalIgnoreCase)
                   && !normalized.EndsWith("/cancel", StringComparison.OrdinalIgnoreCase)
                   && !normalized.EndsWith("/pause", StringComparison.OrdinalIgnoreCase));
    }

    private async Task<ToolboxAgentToolResult> ListGamesAsync(CancellationToken ct)
    {
        var games = await gameLibrary.GetAllAsync(ct);
        var payload = games.Select(game => new
        {
            game.Id,
            game.Name,
            game.IsUnityGame,
            game.InstallState,
            unityVersion = game.DetectedInfo?.UnityVersion,
            backend = game.DetectedInfo?.Backend.ToString(),
            architecture = game.DetectedInfo?.Architecture.ToString()
        });
        return Success("列出游戏", payload, $"已读取 {games.Count} 个游戏。 ");
    }

    private async Task<ToolboxAgentToolResult> ResetToolboxDataAsync(
        JsonElement arguments,
        bool confirmed,
        CancellationToken ct)
    {
        var purpose = GetRequiredString(arguments, "purpose");
        if (!confirmed)
        {
            return Confirmation(
                "清空全部工具箱数据并重启",
                $"用途：{purpose}\n\n将永久删除完整工具箱数据目录：\n{appDataPaths.Root}\n\n这包括设置、API Key、游戏库记录、模型、缓存、日志、备份、附件和智能体会话；不会删除已添加游戏的目录。该操作不创建备份，工具箱随后会退出并自动重启。是否继续？");
        }

        await dataResetService.ScheduleAsync(ct);
        return new ToolboxAgentToolResult(
            true,
            "清空全部工具箱数据并重启",
            "数据清空助手已启动；当前进程退出后将删除完整工具箱数据目录并重启。",
            "已安排清空全部工具箱数据。工具箱即将退出，完成删除后会自动重启。",
            TerminatesTurn: true,
            ReloadRequired: true);
    }

    private async Task<ToolboxAgentToolResult> InspectGameAsync(
        JsonElement arguments, string? selectedGameId, CancellationToken ct)
    {
        var game = await GetGameAsync(arguments, selectedGameId, ct);
        var payload = new
        {
            game.Id,
            game.Name,
            game.ExecutableName,
            game.IsUnityGame,
            game.InstallState,
            game.InstalledBepInExVersion,
            game.InstalledXUnityVersion,
            game.DetectedInfo,
            game.DetectedFrameworks,
            game.AiDescription
        };
        return Success("读取游戏状态", payload, $"已读取「{game.Name}」的状态。 ");
    }

    private async Task<ToolboxAgentToolResult> CheckPluginHealthAsync(
        JsonElement arguments, string? selectedGameId, CancellationToken ct)
    {
        var game = await GetGameAsync(arguments, selectedGameId, ct);
        var report = await healthService.CheckAsync(game, ct: ct);
        return Success("检查插件状态", report, $"已完成「{game.Name}」的本地插件检查。 ");
    }

    private async Task<ToolboxAgentToolResult> AutoRepairPluginsAsync(
        JsonElement arguments, string? selectedGameId, CancellationToken ct)
    {
        var game = await GetGameAsync(arguments, selectedGameId, ct);
        var installStatus = installOrchestrator.GetStatus(game.Id);
        if (installStatus.Step is not (InstallStep.Idle or InstallStep.Complete or InstallStep.Failed))
            throw new InvalidOperationException("游戏正在安装或卸载中，无法执行插件自动修复。");
        var result = await healthService.RepairAsync(game, ct);
        return Success("插件全自动修复", result,
            $"「{game.Name}」自动修复完成：{result.Actions.Count(action => action.State == PluginRepairActionState.Completed)} 项成功，" +
            $"{result.Actions.Count(action => action.State == PluginRepairActionState.Failed)} 项失败，" +
            $"{result.Actions.Count(action => action.State == PluginRepairActionState.Skipped)} 项跳过。 ");
    }

    private async Task<ToolboxAgentToolResult> ListGameFilesAsync(
        JsonElement arguments, string? selectedGameId, CancellationToken ct)
    {
        var game = await GetGameAsync(arguments, selectedGameId, ct);
        var relativeDirectory = GetOptionalString(arguments, "relativeDirectory") ?? string.Empty;
        var directory = ResolveGamePath(game, relativeDirectory, requireExisting: true);
        if (!Directory.Exists(directory))
            throw new DirectoryNotFoundException("指定的游戏内目录不存在。");
        if (!PluginDiagnosticArtifactCollector.IsSafeRegularDirectory(game.GamePath, directory))
            throw new InvalidOperationException("目录包含重解析点或不在游戏目录内。");

        var entries = Directory.EnumerateFileSystemEntries(directory, "*", SearchOption.TopDirectoryOnly)
            .Take(201)
            .Select(path => new
            {
                relativePath = Path.GetRelativePath(game.GamePath, path).Replace('\\', '/'),
                type = Directory.Exists(path) ? "directory" : "file",
                size = File.Exists(path) ? new FileInfo(path).Length : (long?)null
            })
            .Take(200)
            .ToList();
        return Success("列出游戏文件", entries, $"已列出 {entries.Count} 项游戏目录内容。 ");
    }

    private async Task<ToolboxAgentToolResult> ReadGameFileAsync(
        JsonElement arguments, string? selectedGameId, CancellationToken ct)
    {
        var game = await GetGameAsync(arguments, selectedGameId, ct);
        var relativePath = GetRequiredString(arguments, "relativePath");
        var fullPath = ResolveGamePath(game, relativePath, requireExisting: true);
        if (!File.Exists(fullPath))
            throw new FileNotFoundException("指定的游戏内文件不存在。");
        if (!ReadableExtensions.Contains(Path.GetExtension(fullPath)))
            throw new InvalidOperationException("智能体只允许读取文本配置、译文和日志文件。");
        if (!PluginDiagnosticArtifactCollector.IsSafeRegularFile(game.GamePath, fullPath))
            throw new InvalidOperationException("文件包含重解析点或不在游戏目录内。");

        var info = new FileInfo(fullPath);
        if (info.Length > MaxReadableTextBytes)
            throw new InvalidOperationException("文件超过 512 KB，请缩小范围后再处理。");

        var content = await File.ReadAllTextAsync(fullPath, ct);
        if (!PluginDiagnosticArtifactCollector.IsProbablyText(content))
            throw new InvalidDataException("该文件不是可安全读取的文本。");
        content = PluginDiagnosticArtifactCollector.SanitizeContent(content, game.GamePath);
        var numbered = string.Join('\n', content.Split(["\r\n", "\n", "\r"], StringSplitOptions.None)
            .Select((line, index) => $"{index + 1:D5}: {line}"));
        return Success("读取游戏文件", new
        {
            relativePath = Path.GetRelativePath(game.GamePath, fullPath).Replace('\\', '/'),
            content = Trim(numbered, MaxModelToolOutputCharacters)
        }, $"已安全读取 {relativePath}。 ");
    }

    private async Task<ToolboxAgentToolResult> PatchGameFileAsync(
        JsonElement arguments, string? selectedGameId, CancellationToken ct)
    {
        var game = await GetGameAsync(arguments, selectedGameId, ct);
        var relativePath = GetRequiredString(arguments, "relativePath");
        var searchText = GetRequiredString(arguments, "searchText");
        var replacementText = GetOptionalString(arguments, "replacementText") ?? string.Empty;
        if (searchText.Length > 16_000 || replacementText.Length > 16_000)
            throw new InvalidDataException("单次文本补丁不能超过 16,000 个字符。");

        var fullPath = ResolveGamePath(game, relativePath, requireExisting: true);
        EnsureWritableGameTextPath(game, fullPath);
        var content = await File.ReadAllTextAsync(fullPath, ct);
        var occurrenceCount = CountOccurrences(content, searchText);
        if (occurrenceCount != 1)
            throw new InvalidOperationException($"精确旧文本匹配到 {occurrenceCount} 处；为避免误改，本次没有写入。");

        var backup = BackupFile(game, fullPath);
        var updated = content.Replace(searchText, replacementText, StringComparison.Ordinal);
        await WriteTextAtomicAsync(fullPath, updated, ct);
        logger.LogInformation("工具箱智能体已补丁游戏配置 {GameId} {RelativePath}，备份 {Backup}",
            game.Id, relativePath, backup);
        return Success("修改游戏配置", new { relativePath, backupCreated = true },
            $"已修改 {relativePath}，并在工具箱数据目录中创建可恢复备份。 ");
    }

    private async Task<ToolboxAgentToolResult> ApplyCustomFontAsync(
        string sessionId, JsonElement arguments, string? selectedGameId, CancellationToken ct)
    {
        var game = await GetGameAsync(arguments, selectedGameId, ct);
        if (game.DetectedInfo is null)
            throw new InvalidOperationException("尚未检测到游戏的 Unity 版本，无法生成匹配字体。");
        if (game.InstallState != InstallState.FullyInstalled)
            throw new InvalidOperationException("请先完整安装 BepInEx 与 XUnity.AutoTranslator。");
        if (GameProcessHelper.IsGameRunning(game))
            throw new InvalidOperationException("游戏正在运行。请退出游戏后再自动生成并应用字体。");

        var configPath = Path.Combine(game.GamePath, "BepInEx", "config", "AutoTranslatorConfig.ini");
        if (!File.Exists(configPath))
            throw new InvalidOperationException("AutoTranslatorConfig.ini 不存在，无法自动应用字体。请先修复插件安装。");

        var attachmentId = GetRequiredString(arguments, "attachmentId");
        var attachment = attachmentStore.GetRequired(sessionId, attachmentId);
        if (Path.GetExtension(attachment.FullPath) is not (".ttf" or ".otf"))
            throw new InvalidDataException("apply_custom_font 只接受 TTF/OTF 附件。");
        await ValidateFontMagicAsync(attachment.FullPath, ct);

        var characterSets = GetStringArray(arguments, "characterSets")
            .Where(SupportedCharsets.Contains)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (characterSets.Count == 0)
            characterSets.Add("GB2312");

        var generationInputPath = CreateFontGenerationInputCopy(
            attachment.FullPath,
            appDataPaths.FontGenerationUploadsDirectory);
        FontGenerationResult generation;
        try
        {
            generation = await fontGenerator.GenerateAsync(new FontGenerationRequest(
                generationInputPath,
                game.DetectedInfo.UnityVersion,
                CharacterSet: new CharacterSetConfig
                {
                    BuiltinSets = characterSets,
                    TranslationGameId = game.Id
                }));
        }
        finally
        {
            try
            {
                if (File.Exists(generationInputPath))
                    File.Delete(generationInputPath);
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "清理智能体字体生成输入副本失败: {Path}", generationInputPath);
            }
        }
        if (!generation.Success || string.IsNullOrWhiteSpace(generation.OutputPath))
            throw new InvalidOperationException(generation.Error ?? "TMP 字体生成失败。");

        var customTtfPath = fontReplacementService.GetUniqueCustomSourcePath(game.Id, "TTF", attachment.FileName);
        File.Copy(attachment.FullPath, customTtfPath, overwrite: false);
        var customTmpPath = fontReplacementService.GetUniqueCustomSourcePath(
            game.Id, "TMP", Path.GetFileName(generation.OutputPath));
        File.Copy(generation.OutputPath, customTmpPath, overwrite: false);
        var ttfSourceId = $"ttf__{Path.GetFileName(customTtfPath)}";
        var tmpSourceId = $"tmp__{Path.GetFileName(customTmpPath)}";

        var scannedFonts = await fontReplacementService.ScanFontsAsync(game.GamePath, game.DetectedInfo, ct);
        var targets = scannedFonts
            .Where(font => font.ReplacementSupported)
            .Select(font => new FontReplacementTarget
            {
                PathId = font.PathId,
                AssetFile = font.AssetFile,
                SourceId = string.Equals(font.FontType, "TTF", StringComparison.OrdinalIgnoreCase)
                    ? ttfSourceId
                    : tmpSourceId
            })
            .ToArray();

        FontReplacementResult replacement = new() { SuccessCount = 0, FailedFonts = [] };
        if (targets.Length > 0)
        {
            replacement = await fontReplacementService.ReplaceFontsAsync(
                game.GamePath, game.Id, game.DetectedInfo, targets, progress: null, ct);
        }

        BackupFile(game, configPath);

        var installedName = Path.GetFileNameWithoutExtension(generation.OutputPath);
        var installedPath = Path.Combine(game.GamePath, "BepInEx", "Font", installedName);
        if (File.Exists(installedPath))
            BackupFile(game, installedPath);
        var configValue = TmpFontService.InstallCustomFont(game.GamePath, generation.OutputPath, installedName);
        await configurationService.PatchSectionAsync(game.GamePath, "Behaviour",
            new Dictionary<string, string> { ["FallbackFontTextMeshPro"] = configValue }, ct);

        var userMessage = $"已用「{attachment.FileName}」生成并应用 TMP 字体（{generation.GlyphCount} 个字形），" +
                          $"资源内字体替换成功 {replacement.SuccessCount}/{targets.Length} 项" +
                          (replacement.FailedFonts.Count > 0 ? $"，失败 {replacement.FailedFonts.Count} 项" : "") + "。";
        return Success("生成并应用自定义字体", new
        {
            generation.FontName,
            generation.GlyphCount,
            characterSets,
            scanned = scannedFonts.Count,
            requestedReplacements = targets.Length,
            replacement.SuccessCount,
            failed = replacement.FailedFonts.Select(item => new { item.AssetFile, item.PathId, item.Error }),
            fallbackConfigured = true
        }, userMessage);
    }

    internal static string CreateFontGenerationInputCopy(string sourcePath, string uploadDirectory)
    {
        var extension = Path.GetExtension(sourcePath).ToLowerInvariant();
        if (extension is not (".ttf" or ".otf"))
            throw new InvalidDataException("字体生成副本只接受 TTF/OTF 文件。");

        Directory.CreateDirectory(uploadDirectory);
        var targetPath = Path.Combine(uploadDirectory, $"{Guid.NewGuid():N}{extension}");
        File.Copy(sourcePath, targetPath, overwrite: false);
        return targetPath;
    }

    private async Task<ToolboxAgentToolResult> UpdateToolboxSettingAsync(
        JsonElement arguments,
        CancellationToken ct)
    {
        var path = GetRequiredString(arguments, "path");
        if (!arguments.TryGetProperty("value", out var value))
            throw new InvalidDataException("工具参数缺少 value。");

        await settingsService.UpdateAsync(settings => ApplySafeSetting(settings, path, value), ct);
        return Success("更新工具箱设置", new { path, updated = true }, $"已更新设置 {path}。 ");
    }

    internal static void ApplySafeSetting(AppSettings settings, string path, JsonElement value)
    {
        var normalized = path.Trim();
        switch (normalized.ToLowerInvariant())
        {
            case "theme": settings.Theme = RequiredSettingString(value, 32); return;
            case "modeldownloadsource":
                settings.ModelDownloadSource = RequiredSettingEnum<ModelDownloadSource>(value);
                return;
            case "hfmirrorurl":
            {
                var url = RequiredSettingString(value, 500);
                if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
                    throw new InvalidDataException("镜像地址必须是 HTTP(S) URL。");
                try
                {
                    PathSecurity.ValidateExternalUrl(url);
                }
                catch (ArgumentException ex)
                {
                    throw new InvalidDataException(ex.Message, ex);
                }
                settings.HfMirrorUrl = url;
                return;
            }
            case "libraryviewmode": settings.LibraryViewMode = RequiredSettingString(value, 32); return;
            case "librarysortby": settings.LibrarySortBy = RequiredSettingString(value, 32); return;
            case "accentcolor":
            {
                var color = RequiredSettingString(value, 16);
                if (!Regex.IsMatch(color, "^#[0-9a-fA-F]{6}$"))
                    throw new InvalidDataException("强调色必须是 #RRGGBB 格式。");
                settings.AccentColor = color;
                return;
            }
            case "librarycardsize": settings.LibraryCardSize = RequiredSettingString(value, 32); return;
            case "librarygap": settings.LibraryGap = RequiredSettingString(value, 32); return;
            case "libraryshowlabels": settings.LibraryShowLabels = RequiredSettingBool(value); return;
            case "receiveprereleaseupdates": settings.ReceivePreReleaseUpdates = RequiredSettingBool(value); return;
            case "pagezoom":
            {
                var zoom = RequiredSettingInt(value);
                settings.PageZoom = zoom == 0 ? 0 : Math.Clamp(zoom, 50, 200);
                return;
            }
            case "installoptions.autoinstalltmpfont": settings.InstallOptions.AutoInstallTmpFont = RequiredSettingBool(value); return;
            case "installoptions.autodeployaiendpoint": settings.InstallOptions.AutoDeployAiEndpoint = RequiredSettingBool(value); return;
            case "installoptions.autogenerateconfig": settings.InstallOptions.AutoGenerateConfig = RequiredSettingBool(value); return;
            case "installoptions.autoapplyoptimalconfig": settings.InstallOptions.AutoApplyOptimalConfig = RequiredSettingBool(value); return;
            case "installoptions.autoverifyhealth": settings.InstallOptions.AutoVerifyHealth = RequiredSettingBool(value); return;
            case "aitranslation.enabled": settings.AiTranslation.Enabled = RequiredSettingBool(value); return;
            case "aitranslation.activemode":
            {
                var mode = RequiredSettingString(value, 16).ToLowerInvariant();
                if (mode is not ("cloud" or "local"))
                    throw new InvalidDataException("AI 模式只能是 cloud 或 local。");
                settings.AiTranslation.ActiveMode = mode;
                return;
            }
            case "aitranslation.maxconcurrency": settings.AiTranslation.MaxConcurrency = Math.Clamp(RequiredSettingInt(value), 1, 100); return;
            case "aitranslation.port": settings.AiTranslation.Port = Math.Clamp(RequiredSettingInt(value), 1024, 65535); return;
            case "aitranslation.systemprompt": settings.AiTranslation.SystemPrompt = RequiredSettingString(value, 20_000); return;
            case "aitranslation.temperature": settings.AiTranslation.Temperature = Math.Clamp(RequiredSettingDouble(value), 0, 2); return;
            case "aitranslation.contextsize": settings.AiTranslation.ContextSize = Math.Clamp(RequiredSettingInt(value), 0, 100); return;
            case "aitranslation.localcontextsize": settings.AiTranslation.LocalContextSize = Math.Clamp(RequiredSettingInt(value), 0, 10); return;
            case "aitranslation.localminp": settings.AiTranslation.LocalMinP = Math.Clamp(RequiredSettingDouble(value), 0, 1); return;
            case "aitranslation.localrepeatpenalty": settings.AiTranslation.LocalRepeatPenalty = Math.Clamp(RequiredSettingDouble(value), 0.5, 2); return;
            case "aitranslation.termauditenabled": settings.AiTranslation.TermAuditEnabled = RequiredSettingBool(value); return;
            case "aitranslation.naturaltranslationmode": settings.AiTranslation.NaturalTranslationMode = RequiredSettingBool(value); return;
            case "aitranslation.enabletranslationmemory": settings.AiTranslation.EnableTranslationMemory = RequiredSettingBool(value); return;
            case "aitranslation.fuzzymatchthreshold": settings.AiTranslation.FuzzyMatchThreshold = Math.Clamp(RequiredSettingInt(value), 0, 100); return;
            case "aitranslation.glossaryextractionenabled": settings.AiTranslation.GlossaryExtractionEnabled = RequiredSettingBool(value); return;
        }

        const string endpointPrefix = "aitranslation.endpoints.";
        if (normalized.StartsWith(endpointPrefix, StringComparison.OrdinalIgnoreCase))
        {
            var remainder = normalized[endpointPrefix.Length..];
            var separator = remainder.LastIndexOf('.');
            if (separator <= 0 || separator == remainder.Length - 1)
                throw new InvalidDataException("端点设置路径应为 aiTranslation.endpoints.{id}.{field}。");
            var endpointId = remainder[..separator];
            var field = remainder[(separator + 1)..];
            var endpoint = settings.AiTranslation.Endpoints.FirstOrDefault(item => item.Id == endpointId)
                           ?? throw new InvalidOperationException("指定 AI 端点不存在。");
            switch (field.ToLowerInvariant())
            {
                case "name": endpoint.Name = RequiredSettingString(value, 100); return;
                case "modelname": endpoint.ModelName = RequiredSettingString(value, 200); return;
                case "apiformat": endpoint.ApiFormat = RequiredSettingEnum<LlmApiFormat>(value); return;
                case "reasoningeffort": endpoint.ReasoningEffort = RequiredSettingEnum<LlmReasoningEffort>(value); return;
                case "priority": endpoint.Priority = Math.Clamp(RequiredSettingInt(value), 0, 100); return;
                case "enabled": endpoint.Enabled = RequiredSettingBool(value); return;
                default: throw new InvalidOperationException("该端点字段可能包含凭据或网络目标，不能由智能体修改。");
            }
        }

        throw new InvalidOperationException("该设置字段不在智能体安全白名单中。");
    }

    private async Task<ToolboxAgentToolResult> UseAttachmentAsync(
        string sessionId,
        JsonElement arguments,
        string? selectedGameId,
        bool confirmed,
        CancellationToken ct)
    {
        var purpose = GetRequiredString(arguments, "purpose").ToLowerInvariant();
        var attachment = attachmentStore.GetRequired(sessionId, GetRequiredString(arguments, "attachmentId"));
        if (purpose == "settings_import")
        {
            if (!confirmed)
                return Confirmation("导入工具箱设置", "导入设置会覆盖当前配置与部分数据，是否确认继续？");
            return await SendApiAsync("POST", "/api/settings/import-from-path",
                JsonSerializer.SerializeToElement(new { filePath = attachment.FullPath }), "导入工具箱设置", ct);
        }

        var game = await GetGameAsync(arguments, selectedGameId, ct);
        switch (purpose)
        {
            case "install_plugin":
                if (!confirmed)
                    return Confirmation("安装插件附件", "安装 DLL/ZIP 插件会向游戏目录写入可执行代码，是否确认继续？");
                await pluginService.InstallPluginAsync(game, attachment.FullPath);
                return Success("安装插件附件", new { attachment.FileName }, $"已将 {attachment.FileName} 安装到「{game.Name}」。");
            case "icon":
                return await SendApiAsync("POST", $"/api/games/{game.Id}/icon/upload-from-path",
                    JsonSerializer.SerializeToElement(new { filePath = attachment.FullPath }), "设置游戏图标", ct);
            case "cover":
                return await SendApiAsync("POST", $"/api/games/{game.Id}/cover/upload-from-path",
                    JsonSerializer.SerializeToElement(new { filePath = attachment.FullPath }), "设置游戏封面", ct);
            case "background":
                return await SendApiAsync("POST", $"/api/games/{game.Id}/background/upload-from-path",
                    JsonSerializer.SerializeToElement(new { filePath = attachment.FullPath }), "设置游戏背景", ct);
            case "plugin_package_import":
                if (!confirmed)
                    return Confirmation("导入插件包", "导入插件包会覆盖游戏内的插件、配置或翻译资料，是否确认继续？");
                return await SendApiAsync("POST", $"/api/games/{game.Id}/plugin-package/import",
                    JsonSerializer.SerializeToElement(new { zipPath = attachment.FullPath }), "导入插件包", ct);
            case "font_generation_upload":
                return await SendApiAsync("POST", "/api/font-generation/upload-from-path",
                    JsonSerializer.SerializeToElement(new { filePath = attachment.FullPath }), "导入字体生成源", ct);
            case "font_replacement_ttf":
                return await SendApiAsync("POST", $"/api/games/{game.Id}/font-replacement/upload-from-path",
                    JsonSerializer.SerializeToElement(new { filePath = attachment.FullPath, kind = "ttf" }),
                    "导入 Legacy 字体替换源", ct);
            case "font_replacement_tmp":
                return await SendApiAsync("POST", $"/api/games/{game.Id}/font-replacement/upload-from-path",
                    JsonSerializer.SerializeToElement(new { filePath = attachment.FullPath, kind = "tmp" }),
                    "导入 TMP 字体替换源", ct);
            case "charset":
                return await SendApiAsync("POST", "/api/font-generation/charset/upload-custom-from-path",
                    JsonSerializer.SerializeToElement(new { filePath = attachment.FullPath }), "导入自定义字符集", ct);
            case "translation_charset":
                return await SendApiAsync("POST", "/api/font-generation/charset/upload-translation-from-path",
                    JsonSerializer.SerializeToElement(new { filePath = attachment.FullPath }), "导入译文字符集来源", ct);
            default:
                throw new InvalidDataException("不支持的附件用途。");
        }
    }

    private async Task<ToolboxAgentToolResult> CallToolboxApiAsync(
        JsonElement arguments, bool confirmed, CancellationToken ct)
    {
        var method = GetRequiredString(arguments, "method").ToUpperInvariant();
        var path = GetRequiredString(arguments, "path");
        if (!IsAllowedApiCall(method, path, out var error))
            throw new InvalidOperationException(error);
        if (RequiresConfirmation(method, path) && !confirmed)
            return Confirmation($"调用 {method} {path}", $"该操作可能启动进程、覆盖、导入、卸载或删除数据。是否确认执行 {method} {path}？");

        JsonElement? body = null;
        if (arguments.ValueKind == JsonValueKind.Object
            && arguments.TryGetProperty("body", out var bodyElement)
            && bodyElement.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined))
        {
            body = bodyElement.Clone();
        }

        return await SendApiAsync(method, path, body, $"调用工具箱接口 {method} {path}", ct);
    }

    private async Task<ToolboxAgentToolResult> SendApiAsync(
        string method,
        string path,
        JsonElement? body,
        string description,
        CancellationToken ct)
    {
        if (!runtimeEndpoint.IsStarted || string.IsNullOrWhiteSpace(runtimeEndpoint.BaseUrl))
            throw new InvalidOperationException("工具箱本机 API 尚未就绪。");

        var requestUri = new Uri(new Uri(runtimeEndpoint.BaseUrl.TrimEnd('/') + "/"), path.TrimStart('/'));
        using var request = new HttpRequestMessage(new HttpMethod(method), requestUri);
        if (body is not null && method is not "GET")
            request.Content = JsonContent.Create(body.Value);

        var client = httpClientFactory.CreateClient("ToolboxAgentLoopback");
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        var bytes = await PathSecurity.ReadBytesWithLimitAsync(response.Content, 256 * 1024, ct);
        var text = Encoding.UTF8.GetString(bytes);
        var safeText = RedactToolOutput(text);
        if (!response.IsSuccessStatusCode)
        {
            return new ToolboxAgentToolResult(false, description,
                Trim(safeText, MaxModelToolOutputCharacters),
                $"{description}失败（HTTP {(int)response.StatusCode}）。");
        }

        return new ToolboxAgentToolResult(true, description,
            Trim(safeText, MaxModelToolOutputCharacters), $"{description}已完成。 ");
    }

    private async Task<Game> GetGameAsync(JsonElement arguments, string? selectedGameId, CancellationToken ct)
    {
        var gameId = GetOptionalString(arguments, "gameId") ?? selectedGameId;
        if (string.IsNullOrWhiteSpace(gameId))
            throw new InvalidDataException("请先选择游戏，或在工具参数中提供 gameId。");
        return await gameLibrary.GetByIdAsync(gameId, ct)
               ?? throw new InvalidOperationException("指定游戏不存在。");
    }

    private static string ResolveGamePath(Game game, string relativePath, bool requireExisting)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
            return Path.GetFullPath(game.GamePath);
        if (Path.IsPathFullyQualified(relativePath))
            throw new InvalidOperationException("只允许使用游戏目录内的相对路径。");
        var path = PathSecurity.SafeJoin(game.GamePath, relativePath.Replace('/', Path.DirectorySeparatorChar));
        if (requireExisting && !File.Exists(path) && !Directory.Exists(path))
            throw new FileNotFoundException("游戏内路径不存在。");
        return path;
    }

    private static void EnsureWritableGameTextPath(Game game, string fullPath)
    {
        if (!File.Exists(fullPath)
            || !WritableExtensions.Contains(Path.GetExtension(fullPath))
            || !PluginDiagnosticArtifactCollector.IsSafeRegularFile(game.GamePath, fullPath))
        {
            throw new InvalidOperationException("只能修改游戏目录内既有的安全文本配置或译文文件。");
        }

        var relative = Path.GetRelativePath(game.GamePath, fullPath).Replace('\\', '/');
        if (!relative.Equals("doorstop_config.ini", StringComparison.OrdinalIgnoreCase)
            && !relative.StartsWith("BepInEx/config/", StringComparison.OrdinalIgnoreCase)
            && !relative.StartsWith("BepInEx/Translation/", StringComparison.OrdinalIgnoreCase)
            && !relative.StartsWith("BepInEx/Localization/", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("写入仅限 Doorstop、BepInEx 配置、译文和本地化目录。");
        }
    }

    private string BackupFile(Game game, string fullPath)
    {
        var relative = Path.GetRelativePath(game.GamePath, fullPath);
        var stamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fffffff");
        var backupRoot = Path.Combine(appDataPaths.BackupDirectory(game.Id), "agent", stamp);
        var backupPath = PathSecurity.SafeJoin(backupRoot, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(backupPath)!);
        File.Copy(fullPath, backupPath, overwrite: false);
        return backupPath;
    }

    private static async Task WriteTextAtomicAsync(string path, string content, CancellationToken ct)
    {
        var tempPath = path + $".agent-{Guid.NewGuid():N}.tmp";
        try
        {
            await File.WriteAllTextAsync(tempPath, content, new UTF8Encoding(false), ct);
            File.Move(tempPath, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(tempPath))
                File.Delete(tempPath);
        }
    }

    private static async Task ValidateFontMagicAsync(string path, CancellationToken ct)
    {
        var magic = new byte[4];
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
            4096, useAsync: true);
        await stream.ReadExactlyAsync(magic, ct);
        var valid = magic is [0x00, 0x01, 0x00, 0x00]
                    || magic is [0x4F, 0x54, 0x54, 0x4F];
        if (!valid)
            throw new InvalidDataException("附件扩展名是字体，但文件头不是有效的 TTF/OTF。");
    }

    private static int CountOccurrences(string content, string value)
    {
        var count = 0;
        var index = 0;
        while ((index = content.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += value.Length;
        }
        return count;
    }

    private static ToolboxAgentToolResult Success(string description, object payload, string userMessage) =>
        new(true, description, Trim(JsonSerializer.Serialize(payload, FileHelper.DataJsonOptions),
            MaxModelToolOutputCharacters), userMessage);

    private static ToolboxAgentToolResult Confirmation(string description, string message) =>
        new(false, description, message, message, RequiresConfirmation: true);

    private static string GetRequiredString(JsonElement element, string propertyName) =>
        GetOptionalString(element, propertyName)
        ?? throw new InvalidDataException($"工具参数缺少 {propertyName}。");

    private static string? GetOptionalString(JsonElement element, string propertyName)
    {
        if (element.ValueKind != JsonValueKind.Object
            || !element.TryGetProperty(propertyName, out var value)
            || value.ValueKind != JsonValueKind.String)
            return null;
        return string.IsNullOrWhiteSpace(value.GetString()) ? null : value.GetString()!.Trim();
    }

    private static IEnumerable<string> GetStringArray(JsonElement element, string propertyName)
    {
        if (element.ValueKind != JsonValueKind.Object
            || !element.TryGetProperty(propertyName, out var value)
            || value.ValueKind != JsonValueKind.Array)
            return [];
        return value.EnumerateArray()
            .Where(item => item.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(item.GetString()))
            .Select(item => item.GetString()!.Trim());
    }

    private static string RequiredSettingString(JsonElement value, int maxLength)
    {
        if (value.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(value.GetString()))
            throw new InvalidDataException("设置值必须是非空字符串。");
        var result = value.GetString()!.Trim();
        if (result.Length > maxLength)
            throw new InvalidDataException($"设置值不能超过 {maxLength} 个字符。");
        return result;
    }

    private static bool RequiredSettingBool(JsonElement value)
    {
        if (value.ValueKind is JsonValueKind.True or JsonValueKind.False)
            return value.GetBoolean();
        throw new InvalidDataException("设置值必须是布尔值。");
    }

    private static int RequiredSettingInt(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var result))
            return result;
        throw new InvalidDataException("设置值必须是整数。");
    }

    private static double RequiredSettingDouble(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var result) && double.IsFinite(result))
            return result;
        throw new InvalidDataException("设置值必须是有限数值。");
    }

    private static T RequiredSettingEnum<T>(JsonElement value) where T : struct, Enum
    {
        var text = RequiredSettingString(value, 40);
        if (Enum.TryParse<T>(text, ignoreCase: true, out var result) && Enum.IsDefined(result))
            return result;
        throw new InvalidDataException($"设置值不是有效的 {typeof(T).Name}。 ");
    }

    private static string RedactToolOutput(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return "接口没有返回正文。";
        try
        {
            using var document = JsonDocument.Parse(raw);
            return JsonSerializer.Serialize(RedactJson(document.RootElement), FileHelper.DataJsonOptions);
        }
        catch (JsonException)
        {
            return SecretAssignmentRegex().Replace(raw, "$1=[已脱敏]");
        }
    }

    private static object? RedactJson(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.Object => element.EnumerateObject().ToDictionary(
            property => property.Name,
            property => IsSensitiveProperty(property.Name) ? (object?)"[已脱敏]" : RedactJson(property.Value),
            StringComparer.OrdinalIgnoreCase),
        JsonValueKind.Array => element.EnumerateArray().Select(RedactJson).ToList(),
        JsonValueKind.String => PluginDiagnosticArtifactCollector.SanitizeContent(
            element.GetString() ?? string.Empty, Environment.CurrentDirectory),
        JsonValueKind.Number when element.TryGetInt64(out var integer) => integer,
        JsonValueKind.Number when element.TryGetDouble(out var number) => number,
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        _ => null
    };

    private static bool IsSensitiveProperty(string name) =>
        name.Contains("apiKey", StringComparison.OrdinalIgnoreCase)
        || name.Contains("password", StringComparison.OrdinalIgnoreCase)
        || name.Contains("secret", StringComparison.OrdinalIgnoreCase)
        || name.Contains("authorization", StringComparison.OrdinalIgnoreCase)
        || name.Equals("token", StringComparison.OrdinalIgnoreCase)
        || name.Equals("gamePath", StringComparison.OrdinalIgnoreCase)
        || name.Equals("filePath", StringComparison.OrdinalIgnoreCase)
        || name.Equals("rootPath", StringComparison.OrdinalIgnoreCase)
        || (!name.Equals("relativePath", StringComparison.OrdinalIgnoreCase)
            && name.EndsWith("Path", StringComparison.OrdinalIgnoreCase))
        || name.EndsWith("Directory", StringComparison.OrdinalIgnoreCase);

    private static string DecodePathForPolicy(string path)
    {
        var decoded = path;
        for (var pass = 0; pass < 3; pass++)
        {
            var next = Uri.UnescapeDataString(decoded);
            if (string.Equals(next, decoded, StringComparison.Ordinal))
                return decoded;
            decoded = next;
        }

        // More encoding layers are never needed by a toolbox route and make policy review ambiguous.
        if (decoded.Contains('%'))
            throw new UriFormatException("API 路径包含过多编码层级。");
        return decoded;
    }

    private static string Trim(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..maxLength] + "...";

    [GeneratedRegex(@"(?im)(api[_-]?key|token|secret|password|authorization)\s*[:=]\s*[^\s,;]+")]
    private static partial Regex SecretAssignmentRegex();
}
