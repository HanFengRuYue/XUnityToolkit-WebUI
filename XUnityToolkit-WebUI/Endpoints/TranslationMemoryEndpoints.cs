using XUnityToolkit_WebUI.Models;
using XUnityToolkit_WebUI.Services;

namespace XUnityToolkit_WebUI.Endpoints;

public static class TranslationMemoryEndpoints
{
    public static void MapTranslationMemoryEndpoints(this WebApplication app)
    {
        // TM stats
        app.MapGet("/api/games/{id}/translation-memory/stats", (
            string id,
            TranslationMemoryService tmService) =>
        {
            if (!Guid.TryParse(id, out _))
                return Results.BadRequest(ApiResult.Fail("Invalid game ID"));
            var (exact, fuzzy, misses) = tmService.GetHitStats();
            return Results.Ok(ApiResult<object>.Ok(new
            {
                entryCount = tmService.GetEntryCount(id),
                exactHits = exact,
                fuzzyHits = fuzzy,
                misses
            }));
        });

        // Clear TM
        app.MapDelete("/api/games/{id}/translation-memory", async (
            string id,
            TranslationMemoryService tmService) =>
        {
            if (!Guid.TryParse(id, out _))
                return Results.BadRequest(ApiResult.Fail("Invalid game ID"));
            await tmService.DeleteAsync(id);
            return Results.Ok(ApiResult.Ok());
        });
    }
}
