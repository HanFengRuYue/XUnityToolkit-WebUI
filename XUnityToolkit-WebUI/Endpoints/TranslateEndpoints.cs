using System.Text.Json.Serialization;
using XUnityToolkit_WebUI.Models;
using XUnityToolkit_WebUI.Services;

namespace XUnityToolkit_WebUI.Endpoints;

public static class TranslateEndpoints
{
    public static void MapTranslateEndpoints(this WebApplication app)
    {
        app.MapPost("/api/translate", async (
            TranslateRequest request,
            LlmTranslationService translationService,
            GlossaryExtractionService extractionService,
            PreTranslationCacheMonitor cacheMonitor,
            AppSettingsService settingsService,
            ILogger<LlmTranslationService> logger,
            CancellationToken ct) =>
        {
            if (request.Texts is not { Count: > 0 })
                return Results.Ok(new TranslateResponse([]));
            if (request.Texts.Count > 500)
                return Results.BadRequest(ApiResult.Fail("单次请求最多 500 条文本"));

            // Record texts for pre-translation cache monitoring
            if (!string.IsNullOrEmpty(request.GameId))
            {
                await cacheMonitor.EnsureCacheAsync(request.GameId, request.To ?? "zh", ct);
                cacheMonitor.RecordTexts(request.GameId, request.Texts);
            }

            try
            {
                var translations = await translationService.TranslateAsync(
                    request.Texts, request.From ?? "ja", request.To ?? "zh",
                    request.GameId, ct);
                logger.LogInformation("AI 翻译完成: {Count} 条文本", request.Texts.Count);

                // Buffer for glossary extraction (fire-and-forget, non-blocking)
                // Disabled in local mode — local models can't handle extra inference
                var appSettings = await settingsService.GetAsync(ct);
                var isLocalMode = string.Equals(appSettings.AiTranslation.ActiveMode, "local", StringComparison.OrdinalIgnoreCase);
                if (!isLocalMode && !string.IsNullOrEmpty(request.GameId))
                {
                    for (int i = 0; i < request.Texts.Count; i++)
                        extractionService.BufferTranslation(request.GameId, request.Texts[i], translations[i]);
                    extractionService.TryTriggerExtraction(request.GameId);
                }

                return Results.Ok(new TranslateResponse(translations));
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("已停用"))
            {
                logger.LogWarning("AI 翻译已停用");
                return Results.Json(ApiResult.Fail(ex.Message), statusCode: 503);
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("队列已满"))
            {
                logger.LogWarning("翻译队列已满，请求被拒绝");
                return Results.Json(ApiResult.Fail(ex.Message), statusCode: 503);
            }
            catch (InvalidOperationException ex)
            {
                logger.LogWarning("AI 翻译配置错误: {Message}", ex.Message);
                return Results.BadRequest(ApiResult.Fail(ex.Message));
            }
            catch (HttpRequestException ex)
            {
                logger.LogError(ex, "AI 翻译 API 调用失败");
                translationService.RecordError($"LLM API 调用失败: {ex.Message}", gameId: request.GameId);
                return Results.Json(ApiResult.Fail($"LLM API 调用失败: {ex.Message}"), statusCode: 502);
            }
            catch (OperationCanceledException)
            {
                // 客户端断开或 API 超时 — 不算翻译错误，仅记录日志
                logger.LogWarning("翻译请求被取消（客户端断开或 API 超时）");
                return Results.Json(ApiResult.Fail("请求已取消"), statusCode: 499);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "翻译时发生未知错误");
                translationService.RecordError("翻译服务内部错误", gameId: request.GameId);
                return Results.Json(ApiResult.Fail("翻译服务内部错误"), statusCode: 500);
            }
        });

        app.MapGet("/api/translate/stats", (LlmTranslationService translationService) =>
            Results.Ok(ApiResult<TranslationStats>.Ok(translationService.GetStats())));

        app.MapGet("/api/translate/cache-stats", (PreTranslationCacheMonitor cacheMonitor) =>
            Results.Ok(ApiResult<PreTranslationCacheStats>.Ok(cacheMonitor.GetStats())));

        app.MapGet("/api/ai/extraction/stats", (GlossaryExtractionService extractionService) =>
            Results.Ok(ApiResult<GlossaryExtractionStats>.Ok(extractionService.GetStats())));

        // Toggle AI translation on/off
        app.MapPost("/api/ai/toggle", async (
            AiToggleRequest request,
            LlmTranslationService translationService,
            AppSettingsService settingsService,
            ILogger<LlmTranslationService> logger,
            CancellationToken ct) =>
        {
            translationService.Enabled = request.Enabled;

            // Persist to settings — use UpdateAsync to avoid mutating the cached object
            await settingsService.UpdateAsync(s => s.AiTranslation.Enabled = request.Enabled, ct);

            logger.LogInformation("AI 翻译已{State}", request.Enabled ? "启用" : "停用");
            return Results.Ok(ApiResult<bool>.Ok(request.Enabled));
        });

        // Test translation with specific endpoint(s)
        app.MapPost("/api/translate/test", async (
            TestTranslateRequest request,
            LlmTranslationService translationService,
            ILogger<LlmTranslationService> logger,
            CancellationToken ct) =>
        {
            try
            {
                var results = await translationService.TestTranslateAsync(
                    request.Endpoints, request.SystemPrompt, request.Temperature, ct);
                return Results.Ok(ApiResult<IList<EndpointTestResult>>.Ok(results));
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(ApiResult.Fail(ex.Message));
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "测试翻译失败");
                return Results.Json(ApiResult.Fail($"测试翻译失败: {ex.Message}"), statusCode: 500);
            }
        });

        // Fetch available models from provider
        app.MapGet("/api/ai/models", async (
            LlmProvider provider,
            string? apiBaseUrl,
            string apiKey,
            LlmTranslationService translationService,
            CancellationToken ct) =>
        {
            try
            {
                var models = await translationService.FetchModelsAsync(provider, apiBaseUrl ?? "", apiKey, ct);
                return Results.Ok(ApiResult<IList<string>>.Ok(models));
            }
            catch
            {
                return Results.Ok(ApiResult<IList<string>>.Ok(Array.Empty<string>()));
            }
        });
    }
}

public record TranslateRequest(
    [property: JsonPropertyName("texts")] IList<string> Texts,
    [property: JsonPropertyName("from")] string? From,
    [property: JsonPropertyName("to")] string? To,
    [property: JsonPropertyName("gameId")] string? GameId);

public record TranslateResponse(
    [property: JsonPropertyName("translations")] IList<string> Translations);

public record AiToggleRequest(bool Enabled);

public record TestTranslateRequest(
    [property: JsonPropertyName("endpoints")] IList<ApiEndpointConfig> Endpoints,
    [property: JsonPropertyName("systemPrompt")] string SystemPrompt,
    [property: JsonPropertyName("temperature")] double Temperature);
