using XUnityToolkit_WebUI.Models;
using XUnityToolkit_WebUI.Services;

namespace XUnityToolkit_WebUI.Endpoints;

public static class BepInExLogEndpoints
{
    public static void MapBepInExLogEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/games/{id}/bepinex-log");

        // GET / — read the recent log tail + metadata
        group.MapGet("/", async (string id, int? lines, GameLibraryService library, BepInExLogService logService) =>
        {
            var game = await library.GetByIdAsync(id);
            if (game is null)
                return Results.NotFound(ApiResult.Fail("游戏不存在"));

            var logPath = BepInExLogService.GetLogPath(game);
            if (!File.Exists(logPath))
                return Results.NotFound(ApiResult.Fail("BepInEx 日志文件不存在，请确认 BepInEx 已安装且游戏已运行过。"));

            try
            {
                var response = await logService.ReadLogAsync(game, Math.Clamp(lines ?? 5000, 100, 20000));
                return Results.Ok(ApiResult<BepInExLogResponse>.Ok(response));
            }
            catch (Exception)
            {
                return Results.BadRequest(ApiResult.Fail("读取日志失败"));
            }
        });

        // GET /download — download raw log file
        group.MapGet("/download", async (string id, GameLibraryService library) =>
        {
            var game = await library.GetByIdAsync(id);
            if (game is null)
                return Results.NotFound(ApiResult.Fail("游戏不存在"));

            var logPath = BepInExLogService.GetLogPath(game);
            if (!File.Exists(logPath))
                return Results.NotFound(ApiResult.Fail("BepInEx 日志文件不存在"));

            // Keep downloads available while BepInEx is writing or rotating the log.
            var fs = new FileStream(logPath, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            return Results.File(fs, "text/plain", "LogOutput.log");
        });

        // POST /analyze — legacy Markdown adapter over the unified structured diagnostic report
        group.MapPost("/analyze", async (string id, GameLibraryService library,
            PluginHealthCheckService healthService, CancellationToken ct) =>
        {
            var game = await library.GetByIdAsync(id);
            if (game is null)
                return Results.NotFound(ApiResult.Fail("游戏不存在"));

            var logPath = BepInExLogService.GetLogPath(game);
            if (!File.Exists(logPath))
                return Results.NotFound(ApiResult.Fail("BepInEx 日志文件不存在"));

            try
            {
                var report = await healthService.AnalyzeAsync(game, ct: ct);
                var analysis = BepInExLogService.FormatCompatibilityAnalysis(report);
                return Results.Ok(ApiResult<BepInExLogAnalysis>.Ok(analysis));
            }
            catch (PluginDiagnosticAlreadyRunningException ex)
            {
                return Results.Conflict(ApiResult.Fail(ex.Message));
            }
            catch (OperationCanceledException)
            {
                return Results.Json(ApiResult.Fail("智能诊断已取消"), statusCode: 499);
            }
            catch (Exception)
            {
                return Results.BadRequest(ApiResult.Fail("AI 分析失败"));
            }
        });
    }
}
