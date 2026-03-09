using XUnityToolkit_WebUI.Models;
using XUnityToolkit_WebUI.Services;

namespace XUnityToolkit_WebUI.Endpoints;

public static class GameEndpoints
{
    public static void MapGameEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/games");

        group.MapGet("/", async (GameLibraryService library) =>
        {
            var games = await library.GetAllAsync();
            return Results.Ok(ApiResult<List<Game>>.Ok(games));
        });

        group.MapGet("/{id}", async (string id, GameLibraryService library) =>
        {
            var game = await library.GetByIdAsync(id);
            return game is not null
                ? Results.Ok(ApiResult<Game>.Ok(game))
                : Results.NotFound(ApiResult<Game>.Fail("Game not found."));
        });

        group.MapPost("/", async (AddGameRequest request, GameLibraryService library) =>
        {
            if (string.IsNullOrWhiteSpace(request.GamePath))
                return Results.BadRequest(ApiResult<Game>.Fail("Game path is required."));

            if (!Directory.Exists(request.GamePath))
                return Results.BadRequest(ApiResult<Game>.Fail("Game path does not exist."));

            var name = request.Name ?? Path.GetFileName(request.GamePath);

            try
            {
                var game = await library.AddAsync(name, request.GamePath, request.ExecutableName);
                return Results.Created($"/api/games/{game.Id}", ApiResult<Game>.Ok(game));
            }
            catch (InvalidOperationException ex)
            {
                return Results.Conflict(ApiResult<Game>.Fail(ex.Message));
            }
        });

        group.MapPut("/{id}", async (string id, UpdateGameRequest request, GameLibraryService library) =>
        {
            var game = await library.GetByIdAsync(id);
            if (game is null) return Results.NotFound(ApiResult<Game>.Fail("Game not found."));

            if (request.Name is not null) game.Name = request.Name;
            var updated = await library.UpdateAsync(game);
            return Results.Ok(ApiResult<Game>.Ok(updated));
        });

        group.MapDelete("/{id}", async (string id, GameLibraryService library) =>
        {
            var removed = await library.RemoveAsync(id);
            return removed
                ? Results.Ok(ApiResult.Ok())
                : Results.NotFound(ApiResult.Fail("Game not found."));
        });
    }
}

public record AddGameRequest(string GamePath, string? Name = null, string? ExecutableName = null);
public record UpdateGameRequest(string? Name = null);
