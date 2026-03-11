using System.Text.Json;
using XUnityToolkit_WebUI.Infrastructure;
using XUnityToolkit_WebUI.Models;
using XUnityToolkit_WebUI.Services;

namespace XUnityToolkit_WebUI.Endpoints;

public static class AssetEndpoints
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    public static void MapAssetEndpoints(this WebApplication app)
    {
        // Extract game assets (MonoBehaviour strings + TextAsset)
        app.MapPost("/api/games/{id}/extract-assets", async (
            string id,
            GameLibraryService gameLibrary,
            AssetExtractionService extractionService,
            AppDataPaths paths,
            ILogger<AssetExtractionService> logger,
            CancellationToken ct) =>
        {
            var game = await gameLibrary.GetByIdAsync(id, ct);
            if (game is null) return Results.NotFound(ApiResult.Fail("游戏不存在"));
            if (!game.IsUnityGame) return Results.BadRequest(ApiResult.Fail("非 Unity 游戏"));
            if (game.DetectedInfo is null) return Results.BadRequest(ApiResult.Fail("游戏未检测"));

            try
            {
                var result = await extractionService.ExtractTextsAsync(
                    game.GamePath, game.ExecutableName, game.DetectedInfo, ct: ct);

                result.GameId = id;

                // Cache the result
                var cachePath = paths.ExtractedTextsFile(id);
                Directory.CreateDirectory(Path.GetDirectoryName(cachePath)!);
                var json = JsonSerializer.Serialize(result, JsonOptions);
                await File.WriteAllTextAsync(cachePath, json, ct);

                logger.LogInformation("资产提取完成并缓存: {Count} 条文本, 语言={Lang}",
                    result.TotalTextsExtracted, result.DetectedLanguage);

                return Results.Ok(ApiResult<AssetExtractionResult>.Ok(result));
            }
            catch (DirectoryNotFoundException ex)
            {
                return Results.BadRequest(ApiResult.Fail(ex.Message));
            }
            catch (FileNotFoundException ex)
            {
                return Results.BadRequest(ApiResult.Fail(ex.Message));
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "资产提取失败: 游戏 {GameId}", id);
                return Results.Json(ApiResult.Fail($"提取失败: {ex.Message}"), statusCode: 500);
            }
        });

        // Get cached extracted texts
        app.MapGet("/api/games/{id}/extracted-texts", async (
            string id,
            AppDataPaths paths,
            CancellationToken ct) =>
        {
            var cachePath = paths.ExtractedTextsFile(id);
            if (!File.Exists(cachePath))
                return Results.Ok(ApiResult<AssetExtractionResult?>.Ok(null));

            var json = await File.ReadAllTextAsync(cachePath, ct);
            var result = JsonSerializer.Deserialize<AssetExtractionResult>(json, JsonOptions);
            return Results.Ok(ApiResult<AssetExtractionResult?>.Ok(result));
        });

        // Delete cached extracted texts
        app.MapDelete("/api/games/{id}/extracted-texts", (
            string id,
            AppDataPaths paths) =>
        {
            var cachePath = paths.ExtractedTextsFile(id);
            if (File.Exists(cachePath))
                File.Delete(cachePath);
            return Results.Ok(ApiResult.Ok());
        });

        // Start pre-translation
        app.MapPost("/api/games/{id}/pre-translate", async (
            string id,
            PreTranslateRequest request,
            PreTranslationService preTranslation,
            AppDataPaths paths,
            CancellationToken ct) =>
        {
            var cachePath = paths.ExtractedTextsFile(id);
            if (!File.Exists(cachePath))
                return Results.BadRequest(ApiResult.Fail("请先提取游戏资产"));

            var json = await File.ReadAllTextAsync(cachePath, ct);
            var extractResult = JsonSerializer.Deserialize<AssetExtractionResult>(json, JsonOptions);
            if (extractResult is null || extractResult.Texts.Count == 0)
                return Results.BadRequest(ApiResult.Fail("没有可翻译的文本"));

            try
            {
                var status = await preTranslation.StartPreTranslationAsync(
                    id, extractResult.Texts,
                    request.FromLang ?? extractResult.DetectedLanguage ?? "ja",
                    request.ToLang ?? "zh");

                return Results.Ok(ApiResult<PreTranslationStatus>.Ok(status));
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(ApiResult.Fail(ex.Message));
            }
        });

        // Get pre-translation status
        app.MapGet("/api/games/{id}/pre-translate/status", (
            string id,
            PreTranslationService preTranslation) =>
        {
            var status = preTranslation.GetStatus(id);
            return Results.Ok(ApiResult<PreTranslationStatus>.Ok(status));
        });

        // Cancel pre-translation
        app.MapPost("/api/games/{id}/pre-translate/cancel", (
            string id,
            PreTranslationService preTranslation) =>
        {
            preTranslation.Cancel(id);
            return Results.Ok(ApiResult.Ok());
        });
    }
}

public record PreTranslateRequest(string? FromLang, string? ToLang);
