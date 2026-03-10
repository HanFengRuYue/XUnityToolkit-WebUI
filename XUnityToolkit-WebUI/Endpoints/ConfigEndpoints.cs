using XUnityToolkit_WebUI.Models;
using XUnityToolkit_WebUI.Services;

namespace XUnityToolkit_WebUI.Endpoints;

public static class ConfigEndpoints
{
    public static void MapConfigEndpoints(this WebApplication app)
    {
        app.MapGet("/api/games/{id}/config", async (string id,
            GameLibraryService library, ConfigurationService configService) =>
        {
            var game = await library.GetByIdAsync(id);
            if (game is null) return Results.NotFound(ApiResult<XUnityConfig>.Fail("Game not found."));

            var config = await configService.GetAsync(game.GamePath);
            return Results.Ok(ApiResult<XUnityConfig>.Ok(config));
        });

        app.MapPut("/api/games/{id}/config", async (string id,
            XUnityConfig config,
            GameLibraryService library, ConfigurationService configService) =>
        {
            var game = await library.GetByIdAsync(id);
            if (game is null) return Results.NotFound(ApiResult<XUnityConfig>.Fail("Game not found."));

            await configService.PatchAsync(game.GamePath, config);
            return Results.Ok(ApiResult<XUnityConfig>.Ok(config));
        });

        app.MapGet("/api/games/{id}/config/raw", async (string id,
            GameLibraryService library, ConfigurationService configService) =>
        {
            var game = await library.GetByIdAsync(id);
            if (game is null) return Results.NotFound(ApiResult<string>.Fail("Game not found."));

            var content = await configService.GetRawAsync(game.GamePath);
            if (content is null) return Results.NotFound(ApiResult<string>.Fail("配置文件不存在。"));
            return Results.Ok(ApiResult<string>.Ok(content));
        });

        app.MapPut("/api/games/{id}/config/raw", async (string id,
            RawConfigRequest request,
            GameLibraryService library, ConfigurationService configService) =>
        {
            var game = await library.GetByIdAsync(id);
            if (game is null) return Results.NotFound(ApiResult.Fail("Game not found."));

            await configService.SaveRawAsync(game.GamePath, request.Content);
            return Results.Ok(ApiResult.Ok());
        });
    }
}

public record RawConfigRequest(string Content);
