using XUnityToolkit_WebUI.Models;
using XUnityToolkit_WebUI.Services;

namespace XUnityToolkit_WebUI.Endpoints;

public static class PluginHealthEndpoints
{
    public static void MapPluginHealthEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/games/{id}/health-check");

        // GET / — local objective facts plus the current process's cached AI report; never calls a model
        group.MapGet("/", async (string id, GameLibraryService library,
            PluginHealthCheckService healthService, CancellationToken ct) =>
        {
            var game = await library.GetByIdAsync(id, ct);
            if (game is null)
                return Results.NotFound(ApiResult.Fail("游戏不存在"));

            if (game.InstallState == InstallState.NotInstalled)
                return Results.BadRequest(ApiResult.Fail("游戏未安装插件"));

            var report = await healthService.CheckAsync(game, ct: ct);
            return Results.Ok(ApiResult<PluginHealthReport>.Ok(report));
        });

        // POST /analyze — explicit two-stage AI diagnostic without launching the game
        group.MapPost("/analyze", async (string id, GameLibraryService library,
            PluginHealthCheckService healthService, CancellationToken ct) =>
        {
            var game = await library.GetByIdAsync(id, ct);
            if (game is null)
                return Results.NotFound(ApiResult.Fail("游戏不存在"));

            if (game.InstallState == InstallState.NotInstalled)
                return Results.BadRequest(ApiResult.Fail("游戏未安装 BepInEx 插件"));

            try
            {
                var report = await healthService.AnalyzeAsync(game, ct: ct);
                return Results.Ok(ApiResult<PluginHealthReport>.Ok(report));
            }
            catch (PluginDiagnosticAlreadyRunningException ex)
            {
                return Results.Conflict(ApiResult.Fail(ex.Message));
            }
            catch (OperationCanceledException)
            {
                return Results.Json(ApiResult.Fail("智能诊断已取消"), statusCode: 499);
            }
        });

        // POST /verify — launch game briefly, wait for a fresh log and ping, then run the same AI agent
        group.MapPost("/verify", async (string id, GameLibraryService library,
            PluginHealthCheckService healthService, InstallOrchestrator orchestrator,
            CancellationToken ct) =>
        {
            var game = await library.GetByIdAsync(id, ct);
            if (game is null)
                return Results.NotFound(ApiResult.Fail("游戏不存在"));

            if (game.InstallState is InstallState.NotInstalled or InstallState.BepInExOnly)
                return Results.BadRequest(ApiResult.Fail("需要完整安装 BepInEx 和 XUnity.AutoTranslator 才能执行连通性验证"));

            // Prevent conflict with active install/uninstall
            var installStatus = orchestrator.GetStatus(id);
            if (installStatus.Step is not (InstallStep.Idle or InstallStep.Complete or InstallStep.Failed))
                return Results.Conflict(ApiResult.Fail("游戏正在安装或卸载中，无法执行验证"));

            try
            {
                var report = await healthService.VerifyAsync(game, ct);
                return Results.Ok(ApiResult<PluginHealthReport>.Ok(report));
            }
            catch (PluginDiagnosticAlreadyRunningException ex)
            {
                return Results.Conflict(ApiResult.Fail(ex.Message));
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(ApiResult.Fail(ex.Message));
            }
            catch (OperationCanceledException)
            {
                return Results.BadRequest(ApiResult.Fail("验证已取消"));
            }
            catch (Exception)
            {
                return Results.BadRequest(ApiResult.Fail("验证安装失败"));
            }
        });
    }
}
