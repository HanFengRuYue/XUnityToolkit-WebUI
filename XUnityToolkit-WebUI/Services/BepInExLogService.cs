using System.Text;
using XUnityToolkit_WebUI.Infrastructure;
using XUnityToolkit_WebUI.Models;

namespace XUnityToolkit_WebUI.Services;

public class BepInExLogService
{
    private readonly AppSettingsService _settingsService;
    private readonly LlmTranslationService _translationService;
    private readonly ILogger<BepInExLogService> _logger;

    private const int MaxAnalysisLines = 4000;

    private const string DiagnosticPrompt = """
        你是一个 BepInEx/XUnity.AutoTranslator 日志分析专家。请分析以下 BepInEx 日志，使用 Markdown 格式输出诊断报告，包含以下部分：

        ## 总体状态
        判断插件是否正常工作（正常 / 部分异常 / 严重错误）

        ## 插件加载状态
        - BepInEx 框架是否成功初始化
        - XUnity.AutoTranslator 是否成功加载
        - LLMTranslate 翻译端点是否已注册
        - 其他已加载的插件列表

        ## 错误与警告摘要
        列出所有 Error 和 Warning 级别的日志条目，按严重程度排序，说明可能的原因

        ## 翻译功能状态
        - 翻译端点是否连接成功
        - 是否有翻译请求和响应记录
        - 翻译是否正常工作

        ## 建议修复措施
        针对发现的问题，给出具体的修复建议

        请直接输出 Markdown 格式的分析报告，不要输出其他内容。
        """;

    public BepInExLogService(
        AppSettingsService settingsService,
        LlmTranslationService translationService,
        ILogger<BepInExLogService> logger)
    {
        _settingsService = settingsService;
        _translationService = translationService;
        _logger = logger;
    }

    public static string GetLogPath(Game game) =>
        Path.Combine(game.GamePath, "BepInEx", "LogOutput.log");

    public async Task<BepInExLogResponse> ReadLogAsync(Game game)
    {
        var logPath = GetLogPath(game);
        if (!File.Exists(logPath))
            throw new FileNotFoundException("BepInEx 日志文件不存在，请确认 BepInEx 已安装且游戏已运行过。", logPath);

        var fi = new FileInfo(logPath);
        var lastModified = fi.LastWriteTimeUtc;
        var fileSize = fi.Length;

        // Read with FileShare.ReadWrite — game may be running
        string content;
        using (var fs = new FileStream(logPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
        using (var reader = new StreamReader(fs, Encoding.UTF8))
        {
            content = await reader.ReadToEndAsync();
        }

        return new BepInExLogResponse(content, fileSize, lastModified);
    }

    public async Task<BepInExLogAnalysis> AnalyzeLogAsync(string logContent, CancellationToken ct)
    {
        var settings = await _settingsService.GetAsync();
        var endpoint = ResolveEndpoint(settings.AiTranslation);

        if (endpoint is null)
            throw new InvalidOperationException("没有可用的 AI 翻译提供商。请先在 AI 翻译设置中配置至少一个启用的端点。");

        // Truncate to last N lines if too long
        var lines = logContent.Split('\n');
        var truncated = lines.Length > MaxAnalysisLines
            ? string.Join('\n', lines[^MaxAnalysisLines..])
            : logContent;

        _logger.LogInformation("开始 AI 分析 BepInEx 日志，使用端点: {Endpoint}，日志行数: {Lines}",
            endpoint.Name, lines.Length);

        var (report, _) = await _translationService.CallLlmRawAsync(
            endpoint, DiagnosticPrompt, truncated, 0.3, ct);

        return new BepInExLogAnalysis(report, endpoint.Name, DateTime.UtcNow);
    }

    private static ApiEndpointConfig? ResolveEndpoint(AiTranslationSettings ai)
        => EndpointSelector.SelectBestEndpoint(ai.Endpoints);
}
