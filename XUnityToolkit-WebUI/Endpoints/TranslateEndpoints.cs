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
            ILogger<LlmTranslationService> logger,
            CancellationToken ct) =>
        {
            if (request.Texts is not { Count: > 0 })
                return Results.Ok(new TranslateResponse([]));

            try
            {
                var translations = await translationService.TranslateAsync(
                    request.Texts, request.From ?? "ja", request.To ?? "zh", ct);
                logger.LogInformation("AI 翻译完成: {Count} 条文本", request.Texts.Count);
                return Results.Ok(new TranslateResponse(translations));
            }
            catch (InvalidOperationException ex)
            {
                logger.LogWarning("AI 翻译配置错误: {Message}", ex.Message);
                return Results.BadRequest(ApiResult.Fail(ex.Message));
            }
            catch (HttpRequestException ex)
            {
                logger.LogError(ex, "AI 翻译 API 调用失败");
                return Results.Json(ApiResult.Fail($"LLM API 调用失败: {ex.Message}"), statusCode: 502);
            }
        });

        app.MapGet("/api/translate/stats", (LlmTranslationService translationService) =>
            Results.Ok(ApiResult<TranslationStats>.Ok(translationService.GetStats())));
    }
}

public record TranslateRequest(
    [property: JsonPropertyName("texts")] IList<string> Texts,
    [property: JsonPropertyName("from")] string? From,
    [property: JsonPropertyName("to")] string? To);

public record TranslateResponse(
    [property: JsonPropertyName("translations")] IList<string> Translations);
