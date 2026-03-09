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
    }
}
