using XUnityToolkit_WebUI.Models;
using XUnityToolkit_WebUI.Services;

namespace XUnityToolkit_WebUI.Endpoints;

public static class LocalLlmEndpoints
{
    public static void MapLocalLlmEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/local-llm");

        group.MapGet("/status", (LocalLlmService svc) =>
            Results.Ok(ApiResult<LocalLlmStatus>.Ok(svc.GetStatus())));

        group.MapGet("/gpus", async (LocalLlmService svc) =>
            Results.Ok(ApiResult<List<GpuInfo>>.Ok(await svc.DetectGpusAsync())));

        group.MapPost("/gpus/refresh", async (LocalLlmService svc) =>
        {
            svc.ClearGpuCache();
            return Results.Ok(ApiResult<List<GpuInfo>>.Ok(await svc.DetectGpusAsync()));
        });

        group.MapGet("/settings", async (LocalLlmService svc, CancellationToken ct) =>
            Results.Ok(ApiResult<LocalLlmSettings>.Ok(await svc.LoadSettingsAsync(ct))));

        group.MapPut("/settings", async (LocalLlmService svc, UpdateLocalLlmSettingsRequest req, CancellationToken ct) =>
        {
            await svc.UpdateUserSettingsAsync(req, ct);
            return Results.Ok(ApiResult.Ok());
        });

        group.MapGet("/catalog", () =>
            Results.Ok(ApiResult<IReadOnlyList<BuiltInModelInfo>>.Ok(BuiltInModelCatalog.Models)));

        group.MapGet("/downloads", (LocalLlmService svc) =>
            Results.Ok(ApiResult<IReadOnlyList<LocalLlmDownloadProgress>>.Ok(svc.GetActiveDownloads())));

        // ── llama.cpp binary status ──

        group.MapGet("/llama-status", async (LocalLlmService svc, CancellationToken ct) =>
            Results.Ok(ApiResult<LlamaStatus>.Ok(await svc.GetLlamaStatusAsync(ct))));

        // ── Test ──

        group.MapPost("/test", async (
            LocalLlmService localSvc,
            LlmTranslationService translateSvc,
            AppSettingsService settingsSvc,
            ILogger<LocalLlmService> logger,
            CancellationToken ct) =>
        {
            var status = localSvc.GetStatus();
            if (status.State != LocalLlmServerState.Running)
                return Results.BadRequest(ApiResult.Fail("本地 AI 未运行"));

            // Always test against the running server's current runtime endpoint.
            var appSettings = await settingsSvc.GetAsync(ct);
            var localEndpoint = await localSvc.GetRuntimeEndpointAsync(ct);
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
            catch (HttpRequestException ex)
            {
                logger.LogWarning(ex, "本地 LLM 连接测试失败");
                return Results.Ok(ApiResult<LocalLlmTestResult>.Ok(
                    new LocalLlmTestResult(false, null, "连接本地 AI 服务失败", 0)));
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "本地 LLM 测试失败");
                return Results.Ok(ApiResult<LocalLlmTestResult>.Ok(
                    new LocalLlmTestResult(false, null, "测试翻译失败", 0)));
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
            Results.Ok(ApiResult<LocalLlmStatus>.Ok(await svc.StopAsync(ct))));

        // ── llama.cpp binary download ──

        group.MapPost("/llama-download", (LocalLlmService svc) =>
        {
            if (svc.IsDownloadingLlama)
                return Results.BadRequest(ApiResult.Fail("llama 二进制文件正在下载中"));

            _ = Task.Run(async () =>
            {
                try
                {
                    await svc.DownloadLlamaAsync(CancellationToken.None);
                }
                catch (Exception)
                {
                    // Errors broadcast via SignalR in DownloadLlamaAsync
                }
            });
            return Results.Accepted(value: ApiResult.Ok());
        });

        group.MapPost("/llama-download/cancel", (LocalLlmService svc) =>
        {
            svc.CancelLlamaDownload();
            return Results.Ok(ApiResult.Ok());
        });

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
            return Results.Ok(ApiResult.Ok());
        });

        group.MapPost("/download/cancel", async (LocalLlmService svc, CancelDownloadRequest req) =>
        {
            await svc.CancelDownloadAsync(req.CatalogId);
            return Results.Ok(ApiResult.Ok());
        });

        // ── Model inventory ──

        group.MapGet("/models", async (LocalLlmService svc, CancellationToken ct) =>
        {
            var settings = await svc.LoadSettingsAsync(ct);
            return Results.Ok(ApiResult<List<LocalModelEntry>>.Ok(settings.Models));
        });

        group.MapPost("/models/add", async (LocalLlmService svc, AddModelRequest req, CancellationToken ct) =>
            Results.Ok(ApiResult<LocalModelEntry>.Ok(await svc.AddModelAsync(req.FilePath, req.Name, ct))));

        group.MapDelete("/models/{id}", async (LocalLlmService svc, string id, CancellationToken ct) =>
        {
            await svc.RemoveModelAsync(id, ct);
            return Results.Ok(ApiResult.Ok());
        });
    }
}

public record UpdateLocalLlmSettingsRequest(int GpuLayers, int ContextLength, string? KvCacheType = null);
public record StartLocalLlmRequest(string ModelPath, int GpuLayers = -1, int ContextLength = 4096);
public record DownloadModelRequest(string CatalogId);
public record PauseDownloadRequest(string CatalogId);
public record CancelDownloadRequest(string CatalogId);
public record AddModelRequest(string FilePath, string Name);
public record LocalLlmTestResult(bool Success, string[]? Translations, string? Error, double ResponseTimeMs);
