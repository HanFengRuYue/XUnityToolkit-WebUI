using XUnityToolkit_WebUI.Models;
using XUnityToolkit_WebUI.Services;

namespace XUnityToolkit_WebUI.Endpoints;

public static class BepInExLogEndpoints
{
    public static void MapBepInExLogEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/games/{id}/bepinex-log");

        // GET / — read full log content + metadata
        group.MapGet("/", async (string id, GameLibraryService library, BepInExLogService logService) =>
        {
            var game = await library.GetByIdAsync(id);
            if (game is null)
                return Results.NotFound(ApiResult.Fail("游戏不存在"));

            var logPath = BepInExLogService.GetLogPath(game);
            if (!File.Exists(logPath))
                return Results.NotFound(ApiResult.Fail("BepInEx 日志文件不存在，请确认 BepInEx 已安装且游戏已运行过。"));

            try
            {
                var response = await logService.ReadLogAsync(game);
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

            // Stream with FileShare.ReadWrite
            var fs = new FileStream(logPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            return Results.File(fs, "text/plain", "LogOutput.log");
        });

        // POST /analyze — AI analysis of log content
        group.MapPost("/analyze", async (string id, GameLibraryService library,
            BepInExLogService logService, CancellationToken ct) =>
        {
            var game = await library.GetByIdAsync(id);
            if (game is null)
                return Results.NotFound(ApiResult.Fail("游戏不存在"));

            var logPath = BepInExLogService.GetLogPath(game);
            if (!File.Exists(logPath))
                return Results.NotFound(ApiResult.Fail("BepInEx 日志文件不存在"));

            try
            {
                var logResponse = await logService.ReadLogAsync(game);
                var analysis = await logService.AnalyzeLogAsync(logResponse.Content, ct);
                return Results.Ok(ApiResult<BepInExLogAnalysis>.Ok(analysis));
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(ApiResult.Fail(ex.Message));
            }
            catch (Exception)
            {
                return Results.BadRequest(ApiResult.Fail("AI 分析失败"));
            }
        });
    }
}
