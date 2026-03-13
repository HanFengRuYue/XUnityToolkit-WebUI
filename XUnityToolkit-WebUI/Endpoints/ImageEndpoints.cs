using XUnityToolkit_WebUI.Models;
using XUnityToolkit_WebUI.Services;

namespace XUnityToolkit_WebUI.Endpoints;

public static class ImageEndpoints
{
    public static void MapImageEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/games/{id}");

        // Serve cover image (raw bytes, no ApiResult wrapper)
        group.MapGet("/cover", async (HttpContext context, string id, GameLibraryService library, GameImageService imageService, CancellationToken ct) =>
        {
            var game = await library.GetByIdAsync(id);
            if (game is null)
                return Results.NotFound();

            var result = await imageService.GetCoverAsync(id, ct);
            if (result is null)
                return Results.NotFound();

            context.Response.Headers.CacheControl = "public, max-age=31536000, immutable";
            var (bytes, contentType) = result.Value;
            return Results.File(bytes, contentType);
        });

        // Upload custom cover image
        group.MapPost("/cover/upload", async (
            HttpRequest httpRequest,
            string id,
            GameLibraryService library,
            GameImageService imageService,
            CancellationToken ct) =>
        {
            var game = await library.GetByIdAsync(id);
            if (game is null)
                return Results.NotFound(ApiResult.Fail("Game not found."));

            var form = await httpRequest.ReadFormAsync(ct);
            var file = form.Files.GetFile("cover");
            if (file is null || file.Length == 0)
                return Results.BadRequest(ApiResult.Fail("请选择一个图片文件。"));

            if (file.Length > 5 * 1024 * 1024)
                return Results.BadRequest(ApiResult.Fail("图片文件不能超过 5 MB。"));

            if (!GameImageService.IsAllowedContentType(file.ContentType))
                return Results.BadRequest(ApiResult.Fail("仅支持 JPEG、PNG 或 WebP 格式。"));

            using var stream = file.OpenReadStream();
            await imageService.SaveCoverFromUploadAsync(id, stream, file.ContentType, ct);

            return Results.Ok(ApiResult<CoverInfo>.Ok(
                new CoverInfo(true, "upload", game.SteamGridDbGameId)));
        }).DisableAntiforgery();

        // Search SteamGridDB for games
        group.MapPost("/cover/search", async (
            CoverSearchRequest request,
            string id,
            GameLibraryService library,
            GameImageService imageService,
            CancellationToken ct) =>
        {
            var game = await library.GetByIdAsync(id);
            if (game is null)
                return Results.NotFound(ApiResult.Fail("Game not found."));

            try
            {
                var results = await imageService.SearchGamesAsync(request.Query, ct);
                return Results.Ok(ApiResult<List<SteamGridDbSearchResult>>.Ok(results));
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(ApiResult.Fail(ex.Message));
            }
            catch (HttpRequestException ex)
            {
                return Results.Json(
                    ApiResult.Fail($"SteamGridDB API 调用失败: {ex.Message}"),
                    statusCode: 502);
            }
        });

        // Get available grid images for a SteamGridDB game
        group.MapPost("/cover/grids", async (
            CoverGridsRequest request,
            string id,
            GameLibraryService library,
            GameImageService imageService,
            CancellationToken ct) =>
        {
            var game = await library.GetByIdAsync(id);
            if (game is null)
                return Results.NotFound(ApiResult.Fail("Game not found."));

            try
            {
                var images = await imageService.GetGridsAsync(request.SteamGridDbGameId, ct);
                return Results.Ok(ApiResult<List<SteamGridDbImage>>.Ok(images));
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(ApiResult.Fail(ex.Message));
            }
            catch (HttpRequestException ex)
            {
                return Results.Json(
                    ApiResult.Fail($"SteamGridDB API 调用失败: {ex.Message}"),
                    statusCode: 502);
            }
        });

        // Download a selected SteamGridDB image and set as cover
        group.MapPost("/cover/select", async (
            CoverSelectRequest request,
            string id,
            GameLibraryService library,
            GameImageService imageService,
            CancellationToken ct) =>
        {
            var game = await library.GetByIdAsync(id);
            if (game is null)
                return Results.NotFound(ApiResult.Fail("Game not found."));

            try
            {
                await imageService.SaveCoverFromUrlAsync(id, request.ImageUrl, request.SteamGridDbGameId, ct);

                // Persist the SteamGridDB game ID for future re-fetching
                game.SteamGridDbGameId = request.SteamGridDbGameId;
                await library.UpdateAsync(game);

                return Results.Ok(ApiResult<CoverInfo>.Ok(
                    new CoverInfo(true, "steamgriddb", request.SteamGridDbGameId)));
            }
            catch (HttpRequestException ex)
            {
                return Results.Json(
                    ApiResult.Fail($"下载封面图失败: {ex.Message}"),
                    statusCode: 502);
            }
        });

        // Search Steam Store for games (no API key needed)
        group.MapPost("/cover/steam-search", async (
            CoverSearchRequest request,
            string id,
            GameLibraryService library,
            GameImageService imageService,
            CancellationToken ct) =>
        {
            var game = await library.GetByIdAsync(id);
            if (game is null)
                return Results.NotFound(ApiResult.Fail("Game not found."));

            try
            {
                var results = await imageService.SearchSteamGamesAsync(request.Query, ct);
                return Results.Ok(ApiResult<List<SteamStoreSearchResult>>.Ok(results));
            }
            catch (HttpRequestException ex)
            {
                return Results.Json(
                    ApiResult.Fail($"Steam Store API 调用失败: {ex.Message}"),
                    statusCode: 502);
            }
        });

        // Download Steam cover and set as game cover
        group.MapPost("/cover/steam-select", async (
            SteamCoverSelectRequest request,
            string id,
            GameLibraryService library,
            GameImageService imageService,
            CancellationToken ct) =>
        {
            var game = await library.GetByIdAsync(id);
            if (game is null)
                return Results.NotFound(ApiResult.Fail("Game not found."));

            try
            {
                var saved = await imageService.SaveCoverFromSteamAsync(id, request.SteamAppId, ct);
                if (!saved)
                    return Results.Json(
                        ApiResult.Fail("未找到该游戏的 Steam 封面图。"),
                        statusCode: 404);

                // Persist the Steam AppID on the game record
                game.SteamAppId = request.SteamAppId;
                await library.UpdateAsync(game);

                return Results.Ok(ApiResult<CoverInfo>.Ok(
                    new CoverInfo(true, "steam", null)));
            }
            catch (HttpRequestException ex)
            {
                return Results.Json(
                    ApiResult.Fail($"下载 Steam 封面图失败: {ex.Message}"),
                    statusCode: 502);
            }
        });

        // Web image search for cover
        group.MapPost("/cover/web-search", async (
            WebImageSearchRequest request,
            string id,
            GameLibraryService library,
            WebImageSearchService webSearch,
            CancellationToken ct) =>
        {
            var game = await library.GetByIdAsync(id);
            if (game is null)
                return Results.NotFound(ApiResult.Fail("Game not found."));

            try
            {
                var results = await webSearch.SearchAsync(
                    request.Query, request.Engine,
                    request.SizeFilter ?? "auto", "cover", ct);
                return Results.Ok(ApiResult<List<WebImageResult>>.Ok(results));
            }
            catch (HttpRequestException ex)
            {
                return Results.Json(
                    ApiResult.Fail($"图片搜索失败: {ex.Message}"),
                    statusCode: 502);
            }
        });

        // Select web image as cover
        group.MapPost("/cover/web-select", async (
            WebImageSelectRequest request,
            string id,
            GameLibraryService library,
            WebImageSearchService webSearch,
            CancellationToken ct) =>
        {
            var game = await library.GetByIdAsync(id);
            if (game is null)
                return Results.NotFound(ApiResult.Fail("Game not found."));

            try
            {
                await webSearch.SelectAsCoverAsync(id, request.ImageUrl, ct);
                await library.UpdateAsync(game);
                return Results.Ok(ApiResult<CoverInfo>.Ok(
                    new CoverInfo(true, "web", null)));
            }
            catch (HttpRequestException ex)
            {
                return Results.Json(
                    ApiResult.Fail($"下载图片失败: {ex.Message}"),
                    statusCode: 502);
            }
        });

        // ===== Background Image Endpoints =====

        // Search SteamGridDB for games (background)
        group.MapPost("/background/search", async (
            CoverSearchRequest request,
            string id,
            GameLibraryService library,
            GameImageService imageService,
            CancellationToken ct) =>
        {
            var game = await library.GetByIdAsync(id);
            if (game is null)
                return Results.NotFound(ApiResult.Fail("Game not found."));

            try
            {
                var results = await imageService.SearchGamesAsync(request.Query, ct);
                return Results.Ok(ApiResult<List<SteamGridDbSearchResult>>.Ok(results));
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(ApiResult.Fail(ex.Message));
            }
            catch (HttpRequestException ex)
            {
                return Results.Json(
                    ApiResult.Fail($"SteamGridDB API 调用失败: {ex.Message}"),
                    statusCode: 502);
            }
        });

        // Get available hero images for a SteamGridDB game (for backgrounds)
        group.MapPost("/background/heroes", async (
            CoverGridsRequest request,
            string id,
            GameLibraryService library,
            GameImageService imageService,
            CancellationToken ct) =>
        {
            var game = await library.GetByIdAsync(id);
            if (game is null)
                return Results.NotFound(ApiResult.Fail("Game not found."));

            try
            {
                var images = await imageService.GetHeroesAsync(request.SteamGridDbGameId, ct);
                return Results.Ok(ApiResult<List<SteamGridDbImage>>.Ok(images));
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(ApiResult.Fail(ex.Message));
            }
            catch (HttpRequestException ex)
            {
                return Results.Json(
                    ApiResult.Fail($"SteamGridDB API 调用失败: {ex.Message}"),
                    statusCode: 502);
            }
        });

        // Download a selected SteamGridDB hero image and set as background
        group.MapPost("/background/select", async (
            BackgroundSelectRequest request,
            string id,
            GameLibraryService library,
            GameImageService imageService,
            CancellationToken ct) =>
        {
            var game = await library.GetByIdAsync(id);
            if (game is null)
                return Results.NotFound(ApiResult.Fail("Game not found."));

            try
            {
                await imageService.SaveBackgroundFromUrlAsync(id, request.ImageUrl, request.SteamGridDbGameId, ct);

                // Persist the SteamGridDB game ID for future re-fetching
                game.SteamGridDbGameId = request.SteamGridDbGameId;
                await library.UpdateAsync(game);

                return Results.Ok(ApiResult.Ok());
            }
            catch (HttpRequestException ex)
            {
                return Results.Json(
                    ApiResult.Fail($"下载背景图失败: {ex.Message}"),
                    statusCode: 502);
            }
        });

        // Search Steam Store for games (background)
        group.MapPost("/background/steam-search", async (
            CoverSearchRequest request,
            string id,
            GameLibraryService library,
            GameImageService imageService,
            CancellationToken ct) =>
        {
            var game = await library.GetByIdAsync(id);
            if (game is null)
                return Results.NotFound(ApiResult.Fail("Game not found."));

            try
            {
                var results = await imageService.SearchSteamGamesAsync(request.Query, ct);
                return Results.Ok(ApiResult<List<SteamStoreSearchResult>>.Ok(results));
            }
            catch (HttpRequestException ex)
            {
                return Results.Json(
                    ApiResult.Fail($"Steam Store API 调用失败: {ex.Message}"),
                    statusCode: 502);
            }
        });

        // Download Steam hero image and set as background
        group.MapPost("/background/steam-select", async (
            SteamCoverSelectRequest request,
            string id,
            GameLibraryService library,
            GameImageService imageService,
            CancellationToken ct) =>
        {
            var game = await library.GetByIdAsync(id);
            if (game is null)
                return Results.NotFound(ApiResult.Fail("Game not found."));

            try
            {
                var saved = await imageService.SaveBackgroundFromSteamAsync(id, request.SteamAppId, ct);
                if (!saved)
                    return Results.Json(
                        ApiResult.Fail("未找到该游戏的 Steam 背景图。"),
                        statusCode: 404);

                // Persist the Steam AppID on the game record
                game.SteamAppId = request.SteamAppId;
                await library.UpdateAsync(game);

                return Results.Ok(ApiResult.Ok());
            }
            catch (HttpRequestException ex)
            {
                return Results.Json(
                    ApiResult.Fail($"下载 Steam 背景图失败: {ex.Message}"),
                    statusCode: 502);
            }
        });

        // Serve background image (raw bytes)
        group.MapGet("/background", async (HttpContext context, string id, GameLibraryService library, GameImageService imageService, CancellationToken ct) =>
        {
            var game = await library.GetByIdAsync(id);
            if (game is null)
                return Results.NotFound();

            var result = await imageService.GetBackgroundAsync(id, ct);
            if (result is null)
                return Results.NotFound();

            context.Response.Headers.CacheControl = "public, max-age=31536000, immutable";
            var (bytes, contentType) = result.Value;
            return Results.File(bytes, contentType);
        });

        // Upload background image
        group.MapPost("/background/upload", async (
            HttpRequest httpRequest,
            string id,
            GameLibraryService library,
            GameImageService imageService,
            CancellationToken ct) =>
        {
            var game = await library.GetByIdAsync(id);
            if (game is null)
                return Results.NotFound(ApiResult.Fail("Game not found."));

            var form = await httpRequest.ReadFormAsync(ct);
            var file = form.Files.GetFile("background");
            if (file is null || file.Length == 0)
                return Results.BadRequest(ApiResult.Fail("请选择一个图片文件。"));

            if (file.Length > 10 * 1024 * 1024)
                return Results.BadRequest(ApiResult.Fail("图片文件不能超过 10 MB。"));

            if (!GameImageService.IsAllowedContentType(file.ContentType))
                return Results.BadRequest(ApiResult.Fail("仅支持 JPEG、PNG 或 WebP 格式。"));

            using var stream = file.OpenReadStream();
            await imageService.SaveBackgroundFromUploadAsync(id, stream, file.ContentType, ct);

            return Results.Ok(ApiResult.Ok());
        }).DisableAntiforgery();

        // Web image search for background
        group.MapPost("/background/web-search", async (
            WebImageSearchRequest request,
            string id,
            GameLibraryService library,
            WebImageSearchService webSearch,
            CancellationToken ct) =>
        {
            var game = await library.GetByIdAsync(id);
            if (game is null)
                return Results.NotFound(ApiResult.Fail("Game not found."));

            try
            {
                var results = await webSearch.SearchAsync(
                    request.Query, request.Engine,
                    request.SizeFilter ?? "auto", "background", ct);
                return Results.Ok(ApiResult<List<WebImageResult>>.Ok(results));
            }
            catch (HttpRequestException ex)
            {
                return Results.Json(
                    ApiResult.Fail($"图片搜索失败: {ex.Message}"),
                    statusCode: 502);
            }
        });

        // Select web image as background
        group.MapPost("/background/web-select", async (
            WebImageSelectRequest request,
            string id,
            GameLibraryService library,
            WebImageSearchService webSearch,
            CancellationToken ct) =>
        {
            var game = await library.GetByIdAsync(id);
            if (game is null)
                return Results.NotFound(ApiResult.Fail("Game not found."));

            try
            {
                await webSearch.SelectAsBackgroundAsync(id, request.ImageUrl, ct);
                return Results.Ok(ApiResult.Ok());
            }
            catch (HttpRequestException ex)
            {
                return Results.Json(
                    ApiResult.Fail($"下载图片失败: {ex.Message}"),
                    statusCode: 502);
            }
        });

        // Delete background image
        group.MapDelete("/background", async (
            string id,
            GameLibraryService library,
            GameImageService imageService,
            CancellationToken ct) =>
        {
            var game = await library.GetByIdAsync(id);
            if (game is null)
                return Results.NotFound(ApiResult.Fail("Game not found."));

            await imageService.DeleteBackgroundAsync(id, ct);
            return Results.Ok(ApiResult.Ok());
        });

        // Delete cover image
        group.MapDelete("/cover", async (
            string id,
            GameLibraryService library,
            GameImageService imageService,
            CancellationToken ct) =>
        {
            var game = await library.GetByIdAsync(id);
            if (game is null)
                return Results.NotFound(ApiResult.Fail("Game not found."));

            await imageService.DeleteCoverAsync(id, ct);
            return Results.Ok(ApiResult.Ok());
        });
    }
}

public record CoverSearchRequest(string Query);
public record CoverGridsRequest(int SteamGridDbGameId);
public record CoverSelectRequest(string ImageUrl, int SteamGridDbGameId);
public record SteamCoverSelectRequest(int SteamAppId);
public record BackgroundSelectRequest(string ImageUrl, int SteamGridDbGameId);
public record WebImageSearchRequest(string Query, string Engine, string? SizeFilter);
public record WebImageSelectRequest(string ImageUrl);
