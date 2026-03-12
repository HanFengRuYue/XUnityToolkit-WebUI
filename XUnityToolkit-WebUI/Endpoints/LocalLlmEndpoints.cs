using XUnityToolkit_WebUI.Models;
using XUnityToolkit_WebUI.Services;

namespace XUnityToolkit_WebUI.Endpoints;

public static class LocalLlmEndpoints
{
    public static void MapLocalLlmEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/local-llm");

        group.MapGet("/status", async (LocalLlmService svc) =>
            ApiResult<LocalLlmStatus>.Ok(svc.GetStatus()));

        group.MapGet("/gpus", async (LocalLlmService svc) =>
            ApiResult<List<GpuInfo>>.Ok(await svc.DetectGpusAsync()));

        group.MapPost("/gpus/refresh", async (LocalLlmService svc) =>
        {
            svc.ClearGpuCache();
            return ApiResult<List<GpuInfo>>.Ok(await svc.DetectGpusAsync());
        });

        group.MapGet("/settings", async (LocalLlmService svc, CancellationToken ct) =>
            ApiResult<LocalLlmSettings>.Ok(await svc.LoadSettingsAsync(ct)));

        group.MapPut("/settings", async (LocalLlmService svc, UpdateLocalLlmSettingsRequest req, CancellationToken ct) =>
        {
            await svc.UpdateUserSettingsAsync(req, ct);
            return ApiResult.Ok();
        });

        group.MapGet("/catalog", () =>
            ApiResult<IReadOnlyList<BuiltInModelInfo>>.Ok(BuiltInModelCatalog.Models));

        // ── llama.cpp binary management ──

        group.MapGet("/llama-status", async (LocalLlmService svc, CancellationToken ct) =>
            ApiResult<LlamaStatus>.Ok(await svc.GetLlamaStatusAsync(ct)));

        group.MapPost("/download-llama", (LocalLlmService svc) =>
        {
            _ = Task.Run(async () =>
            {
                try { await svc.DownloadRequiredLlamaAsync(CancellationToken.None); }
                catch { /* errors broadcast via SignalR */ }
            });
            return Results.Accepted(value: ApiResult.Ok());
        });

        group.MapPost("/download-llama/pause", (LocalLlmService svc, PauseLlamaRequest req) =>
        {
            svc.PauseLlamaDownload(req.DownloadId);
            return ApiResult.Ok();
        });

        group.MapPost("/download-llama/cancel", async (LocalLlmService svc, CancelLlamaRequest req) =>
        {
            if (!Enum.TryParse<GpuBackend>(req.Backend, true, out var backend))
                return Results.BadRequest(ApiResult.Fail("无效的后端名称"));
            await svc.CancelLlamaDownloadAsync(req.DownloadId, backend);
            return Results.Ok(ApiResult.Ok());
        });

        // ── Test ──

        group.MapPost("/test", async (
            LocalLlmService localSvc,
            LlmTranslationService translateSvc,
            AppSettingsService settingsSvc,
            CancellationToken ct) =>
        {
            var status = localSvc.GetStatus();
            if (status.State != LocalLlmServerState.Running)
                return Results.BadRequest(ApiResult.Fail("本地 AI 未运行"));

            // Find the auto-registered local endpoint in AiTranslationSettings
            var llmSettings = await localSvc.LoadSettingsAsync(ct);
            var appSettings = await settingsSvc.GetAsync(ct);
            var localEndpoint = appSettings.AiTranslation.Endpoints
                .FirstOrDefault(e => e.Id == llmSettings.EndpointId);
            if (localEndpoint is null)
                return Results.BadRequest(ApiResult.Fail("未找到本地 AI 端点配置"));

            try
            {
                var results = await translateSvc.TestTranslateAsync(
                    [localEndpoint], appSettings.AiTranslation.SystemPrompt, appSettings.AiTranslation.Temperature, ct);
                var r = results[0];
                return Results.Ok(ApiResult<LocalLlmTestResult>.Ok(
                    new LocalLlmTestResult(r.Success, r.Translations?.ToArray(), r.Error, r.ResponseTimeMs)));
            }
            catch (Exception ex)
            {
                return Results.Ok(ApiResult<LocalLlmTestResult>.Ok(
                    new LocalLlmTestResult(false, null, ex.Message, 0)));
            }
        });

        // ── Server lifecycle ──

        group.MapPost("/start", async (LocalLlmService svc, StartLocalLlmRequest req, CancellationToken ct) =>
        {
            var status = await svc.StartAsync(req.ModelPath, req.GpuLayers, req.ContextLength, ct);
            return status.State == LocalLlmServerState.Failed
                ? Results.Json(ApiResult<LocalLlmStatus>.Fail(status.Error ?? "启动失败"), statusCode: 500)
                : Results.Ok(ApiResult<LocalLlmStatus>.Ok(status));
        });

        group.MapPost("/stop", async (LocalLlmService svc, CancellationToken ct) =>
            ApiResult<LocalLlmStatus>.Ok(await svc.StopAsync(ct)));

        // ── Model download ──

        group.MapPost("/download", (LocalLlmService svc, DownloadModelRequest req) =>
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    await svc.DownloadModelAsync(req.CatalogId, CancellationToken.None);
                }
                catch (Exception)
                {
                    // Errors already broadcast via SignalR in DownloadModelAsync
                }
            });
            return Results.Accepted(value: ApiResult.Ok());
        });

        group.MapPost("/download/pause", (LocalLlmService svc, PauseDownloadRequest req) =>
        {
            svc.PauseDownload(req.CatalogId);
            return ApiResult.Ok();
        });

        group.MapPost("/download/cancel", async (LocalLlmService svc, CancelDownloadRequest req) =>
        {
            await svc.CancelDownloadAsync(req.CatalogId);
            return ApiResult.Ok();
        });

        // ── Model inventory ──

        group.MapGet("/models", async (LocalLlmService svc, CancellationToken ct) =>
        {
            var settings = await svc.LoadSettingsAsync(ct);
            return ApiResult<List<LocalModelEntry>>.Ok(settings.Models);
        });

        group.MapPost("/models/add", async (LocalLlmService svc, AddModelRequest req, CancellationToken ct) =>
            ApiResult<LocalModelEntry>.Ok(await svc.AddModelAsync(req.FilePath, req.Name, ct)));

        group.MapDelete("/models/{id}", async (LocalLlmService svc, string id, CancellationToken ct) =>
        {
            await svc.RemoveModelAsync(id, ct);
            return ApiResult.Ok();
        });
    }
}

public record UpdateLocalLlmSettingsRequest(int GpuLayers, int ContextLength);
public record StartLocalLlmRequest(string ModelPath, int GpuLayers = -1, int ContextLength = 4096);
public record DownloadModelRequest(string CatalogId);
public record PauseDownloadRequest(string CatalogId);
public record CancelDownloadRequest(string CatalogId);
public record AddModelRequest(string FilePath, string Name);
public record PauseLlamaRequest(string DownloadId);
public record CancelLlamaRequest(string DownloadId, string Backend);
public record LocalLlmTestResult(bool Success, string[]? Translations, string? Error, double ResponseTimeMs);
