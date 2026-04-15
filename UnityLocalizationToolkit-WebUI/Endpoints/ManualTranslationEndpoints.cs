using UnityLocalizationToolkit_WebUI.Models;
using UnityLocalizationToolkit_WebUI.Services;

namespace UnityLocalizationToolkit_WebUI.Endpoints;

public static class ManualTranslationEndpoints
{
    public static void MapManualTranslationEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/games/{id}/manual-translation");

        group.MapPost("/scan", async (
            string id,
            GameLibraryService library,
            ManualTranslationService manualTranslation,
            CancellationToken ct) =>
        {
            var game = await library.GetByIdAsync(id, ct);
            if (game is null)
                return Results.NotFound(ApiResult.Fail("Game not found."));
            if (!game.IsUnityGame || game.DetectedInfo is null)
                return Results.BadRequest(ApiResult.Fail("Only detected Unity games support manual translation."));

            var index = await manualTranslation.ScanAsync(game, ct);
            return Results.Ok(ApiResult<ManualTranslationProjectIndex>.Ok(index));
        });

        group.MapGet("/status", async (
            string id,
            ManualTranslationService manualTranslation,
            CancellationToken ct) =>
        {
            var status = await manualTranslation.GetStatusAsync(id, ct);
            return Results.Ok(ApiResult<ManualTranslationStatus>.Ok(status));
        });

        group.MapGet("/assets", async (
            string id,
            [AsParameters] ManualTranslationAssetsQuery query,
            ManualTranslationService manualTranslation,
            CancellationToken ct) =>
        {
            var assets = await manualTranslation.GetAssetsAsync(id, query, ct);
            return Results.Ok(ApiResult<ManualTranslationAssetListResponse>.Ok(assets));
        });

        group.MapGet("/asset-detail", async (
            string id,
            string assetId,
            ManualTranslationService manualTranslation,
            CancellationToken ct) =>
        {
            var asset = await manualTranslation.GetAssetDetailAsync(id, assetId, ct);
            return asset is null
                ? Results.NotFound(ApiResult.Fail("Asset not found."))
                : Results.Ok(ApiResult<ManualTranslationAssetEntry>.Ok(asset));
        });

        group.MapGet("/asset-content", async (
            string id,
            string assetId,
            ManualTranslationService manualTranslation,
            CancellationToken ct) =>
        {
            var content = await manualTranslation.GetAssetContentAsync(id, assetId, ct);
            return content is null
                ? Results.NotFound(ApiResult.Fail("Asset not found."))
                : Results.Ok(ApiResult<ManualTranslationAssetContent>.Ok(content));
        });

        group.MapGet("/image-preview", async (
            string id,
            string assetId,
            ManualTranslationImagePreviewSource source,
            ManualTranslationImagePreviewSize size,
            GameLibraryService library,
            ManualTranslationService manualTranslation,
            CancellationToken ct) =>
        {
            var game = await library.GetByIdAsync(id, ct);
            if (game is null)
                return Results.NotFound(ApiResult.Fail("Game not found."));

            try
            {
                var (stream, contentType, fileName) = await manualTranslation.GetImagePreviewAsync(game, assetId, source, size, ct);
                return Results.File(stream, contentType, fileName);
            }
            catch (InvalidOperationException ex)
            {
                return Results.NotFound(ApiResult.Fail(ex.Message));
            }
            catch (FileNotFoundException ex)
            {
                return Results.NotFound(ApiResult.Fail(ex.Message));
            }
        });

        group.MapPut("/save-override", async (
            string id,
            ManualTranslationSaveOverridePayload payload,
            ManualTranslationService manualTranslation,
            CancellationToken ct) =>
        {
            await manualTranslation.SaveOverrideAsync(id, payload.AssetId, payload.Value, payload.Source, ct);
            return Results.Ok(ApiResult.Ok());
        });

        group.MapDelete("/delete-override", async (
            string id,
            string assetId,
            ManualTranslationService manualTranslation,
            CancellationToken ct) =>
        {
            await manualTranslation.DeleteOverrideAsync(id, assetId, ct);
            return Results.Ok(ApiResult.Ok());
        });

        group.MapPost("/image-override", async (
            HttpRequest httpRequest,
            string id,
            ManualTranslationService manualTranslation,
            CancellationToken ct) =>
        {
            var form = await httpRequest.ReadFormAsync(ct);
            var assetId = form["assetId"].ToString();
            var source = form["source"].ToString();
            var file = form.Files.GetFile("image");
            if (string.IsNullOrWhiteSpace(assetId))
                return Results.BadRequest(ApiResult.Fail("AssetId is required."));
            if (file is null || file.Length == 0)
                return Results.BadRequest(ApiResult.Fail("Please choose an image file."));
            if (file.Length > 10 * 1024 * 1024)
                return Results.BadRequest(ApiResult.Fail("Image files must be 10 MB or smaller."));

            try
            {
                await using var stream = file.OpenReadStream();
                await manualTranslation.SaveImageOverrideAsync(id, assetId, stream, source, ct);
                return Results.Ok(ApiResult.Ok());
            }
            catch (InvalidDataException ex)
            {
                return Results.BadRequest(ApiResult.Fail(ex.Message));
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(ApiResult.Fail(ex.Message));
            }
        }).DisableAntiforgery();

        group.MapPost("/image-override-from-path", async (
            string id,
            ManualTranslationImageOverrideFromPathRequest request,
            ManualTranslationService manualTranslation,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(request.AssetId))
                return Results.BadRequest(ApiResult.Fail("AssetId is required."));
            if (string.IsNullOrWhiteSpace(request.FilePath))
                return Results.BadRequest(ApiResult.Fail("File path is required."));

            try
            {
                await manualTranslation.SaveImageOverrideFromPathAsync(id, request.AssetId, request.FilePath, request.Source, ct);
                return Results.Ok(ApiResult.Ok());
            }
            catch (InvalidDataException ex)
            {
                return Results.BadRequest(ApiResult.Fail(ex.Message));
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(ApiResult.Fail(ex.Message));
            }
            catch (FileNotFoundException ex)
            {
                return Results.BadRequest(ApiResult.Fail(ex.Message));
            }
        });

        group.MapDelete("/image-override", async (
            string id,
            string assetId,
            ManualTranslationService manualTranslation,
            CancellationToken ct) =>
        {
            await manualTranslation.DeleteOverrideAsync(id, assetId, ct);
            return Results.Ok(ApiResult.Ok());
        });

        group.MapPost("/apply", async (
            string id,
            GameLibraryService library,
            ManualTranslationService manualTranslation,
            CancellationToken ct) =>
        {
            var game = await library.GetByIdAsync(id, ct);
            if (game is null)
                return Results.NotFound(ApiResult.Fail("Game not found."));

            var result = await manualTranslation.ApplyAsync(game, ct);
            return Results.Ok(ApiResult<ManualTranslationApplyResult>.Ok(result));
        });

        group.MapPost("/restore", async (
            string id,
            GameLibraryService library,
            ManualTranslationService manualTranslation,
            CancellationToken ct) =>
        {
            var game = await library.GetByIdAsync(id, ct);
            if (game is null)
                return Results.NotFound(ApiResult.Fail("Game not found."));

            await manualTranslation.RestoreAsync(game, ct);
            return Results.Ok(ApiResult.Ok());
        });

        group.MapPost("/build-package", async (
            string id,
            GameLibraryService library,
            ManualTranslationService manualTranslation,
            CancellationToken ct) =>
        {
            var game = await library.GetByIdAsync(id, ct);
            if (game is null)
                return Results.NotFound(ApiResult.Fail("Game not found."));

            var (stream, fileName) = await manualTranslation.BuildPackageAsync(game, ct);
            return Results.File(stream, "application/zip", fileName);
        });

        group.MapPost("/import-package", async (
            string id,
            ManualTranslationImportPackageRequest request,
            ManualTranslationService manualTranslation,
            CancellationToken ct) =>
        {
            await manualTranslation.ImportPackageAsync(id, request.ZipPath, ct);
            return Results.Ok(ApiResult.Ok());
        });

        group.MapGet("/export-asset", async (
            string id,
            string assetId,
            GameLibraryService library,
            ManualTranslationService manualTranslation,
            CancellationToken ct) =>
        {
            var game = await library.GetByIdAsync(id, ct);
            if (game is null)
                return Results.NotFound(ApiResult.Fail("Game not found."));

            var (stream, contentType, fileName) = await manualTranslation.ExportAssetAsync(game, assetId, ct);
            return Results.File(stream, contentType, fileName);
        });
    }
}

public sealed record ManualTranslationSaveOverridePayload(string AssetId, string Value, string? Source);
public sealed record ManualTranslationImageOverrideFromPathRequest(string AssetId, string FilePath, string? Source);
