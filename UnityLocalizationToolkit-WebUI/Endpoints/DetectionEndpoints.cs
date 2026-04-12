using UnityLocalizationToolkit_WebUI.Models;
using UnityLocalizationToolkit_WebUI.Services;

namespace UnityLocalizationToolkit_WebUI.Endpoints;

public static class DetectionEndpoints
{
    public static void MapDetectionEndpoints(this WebApplication app)
    {
        app.MapPost("/api/games/{id}/detect", async (string id,
            GameLibraryService library, UnityDetectionService detection) =>
        {
            var game = await library.GetByIdAsync(id);
            if (game is null) return Results.NotFound(ApiResult<UnityGameInfo>.Fail("Game not found."));

            try
            {
                var info = await detection.DetectAsync(game.GamePath);
                game.DetectedInfo = info;
                game.ExecutableName = info.DetectedExecutable;
                await library.UpdateAsync(game);
                return Results.Ok(ApiResult<UnityGameInfo>.Ok(info));
            }
            catch (FileNotFoundException ex)
            {
                return Results.BadRequest(ApiResult<UnityGameInfo>.Fail(ex.Message));
            }
        });
    }
}
