namespace XUnityToolkit_WebUI.Endpoints;

using XUnityToolkit_WebUI.Models;
using XUnityToolkit_WebUI.Services;

public static class UpdateEndpoints
{
    public static void MapUpdateEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/update");

        group.MapGet("/check", async (UpdateService updateService, CancellationToken ct) =>
        {
            try
            {
                var result = await updateService.CheckForUpdateAsync(ct);
                return Results.Ok(ApiResult<UpdateCheckResult>.Ok(result));
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(ApiResult.Fail(ex.Message));
            }
        });

        group.MapGet("/status", (UpdateService updateService) =>
        {
            var status = updateService.GetUpdateStatus();
            return Results.Ok(ApiResult<UpdateStatusInfo>.Ok(status));
        });

        group.MapPost("/download", async (UpdateService updateService, CancellationToken ct) =>
        {
            try
            {
                await updateService.DownloadUpdateAsync(ct);
                return Results.Ok(ApiResult.Ok());
            }
            catch (OperationCanceledException)
            {
                return Results.BadRequest(ApiResult.Fail("下载已取消"));
            }
            catch (Exception)
            {
                return Results.Json(ApiResult.Fail("下载更新失败"), statusCode: 500);
            }
        });

        group.MapPost("/cancel", (UpdateService updateService) =>
        {
            updateService.CancelDownload();
            return Results.Ok(ApiResult.Ok());
        });

        group.MapPost("/apply", async (
            UpdateService updateService,
            InstallOrchestrator installOrchestrator,
            LlmTranslationService translationService,
            LocalLlmService localLlmService,
            TmpFontGeneratorService fontGenService,
            PreTranslationService preTranslationService,
            CancellationToken ct) =>
        {
            // Pre-apply safety checks
            if (installOrchestrator.HasActiveOperation)
                return Results.Json(ApiResult.Fail("有正在进行的安装/卸载操作，请先完成或取消"), statusCode: 409);
            if (translationService.HasPendingTranslations)
                return Results.Json(ApiResult.Fail("有正在进行的 AI 翻译任务，请先停止"), statusCode: 409);
            if (localLlmService.IsRunning)
                return Results.Json(ApiResult.Fail("本地 LLM 服务正在运行，请先停止"), statusCode: 409);
            if (fontGenService.IsGenerating)
                return Results.Json(ApiResult.Fail("字体生成正在进行中，请先取消"), statusCode: 409);
            if (preTranslationService.IsPreTranslating)
                return Results.Json(ApiResult.Fail("预翻译正在进行中，请先取消"), statusCode: 409);

            try
            {
                var message = await updateService.ApplyUpdateAsync(ct);
                return Results.Ok(ApiResult<string>.Ok(message));
            }
            catch (Exception)
            {
                return Results.Json(ApiResult.Fail("应用更新失败"), statusCode: 500);
            }
        });

        group.MapPost("/dismiss", async (UpdateService updateService, CancellationToken ct) =>
        {
            await updateService.DismissUpdateAsync(ct);
            return Results.Ok(ApiResult.Ok());
        });
    }
}
