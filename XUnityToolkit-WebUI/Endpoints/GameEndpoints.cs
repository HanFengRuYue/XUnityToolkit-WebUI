using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using XUnityToolkit_WebUI.Infrastructure;
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

        group.MapGet("/{id}/icon", async (string id, GameLibraryService library, AppDataPaths paths) =>
        {
            var game = await library.GetByIdAsync(id);
            if (game is null)
                return Results.NotFound();

            var exeName = game.ExecutableName ?? game.DetectedInfo?.DetectedExecutable;
            if (string.IsNullOrEmpty(exeName))
                return Results.NotFound();

            var exePath = Path.Combine(game.GamePath, exeName);
            if (!File.Exists(exePath))
                return Results.NotFound();

            // Check cache
            var iconsDir = Path.Combine(paths.CacheDirectory, "icons");
            Directory.CreateDirectory(iconsDir);
            var cachePath = Path.Combine(iconsDir, $"{game.Id}.png");

            if (File.Exists(cachePath))
            {
                // Invalidate cache if exe is newer
                var exeTime = File.GetLastWriteTimeUtc(exePath);
                var cacheTime = File.GetLastWriteTimeUtc(cachePath);
                if (cacheTime > exeTime)
                {
                    var bytes = await File.ReadAllBytesAsync(cachePath);
                    return Results.File(bytes, "image/png");
                }
            }

            // Extract icon from exe
            using var icon = Icon.ExtractAssociatedIcon(exePath);
            if (icon is null)
                return Results.NotFound();

            using var bitmap = icon.ToBitmap();
            using var ms = new MemoryStream();
            bitmap.Save(ms, ImageFormat.Png);
            var pngBytes = ms.ToArray();

            // Write cache
            await File.WriteAllBytesAsync(cachePath, pngBytes);

            return Results.File(pngBytes, "image/png");
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

        group.MapPost("/{id}/open-folder", async (string id, GameLibraryService library) =>
        {
            var game = await library.GetByIdAsync(id);
            if (game is null)
                return Results.NotFound(ApiResult.Fail("Game not found."));
            if (!Directory.Exists(game.GamePath))
                return Results.BadRequest(ApiResult.Fail("游戏目录不存在。"));

            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"\"{game.GamePath}\"",
                UseShellExecute = true
            });
            return Results.Ok(ApiResult.Ok());
        });

        group.MapPost("/{id}/launch", async (string id, GameLibraryService library) =>
        {
            var game = await library.GetByIdAsync(id);
            if (game is null)
                return Results.NotFound(ApiResult.Fail("Game not found."));

            var exeName = game.ExecutableName ?? game.DetectedInfo?.DetectedExecutable;
            if (string.IsNullOrEmpty(exeName))
                return Results.BadRequest(ApiResult.Fail("未检测到可执行文件。"));

            var exePath = Path.Combine(game.GamePath, exeName);
            if (!File.Exists(exePath))
                return Results.BadRequest(ApiResult.Fail($"可执行文件不存在: {exeName}"));

            Process.Start(new ProcessStartInfo
            {
                FileName = exePath,
                WorkingDirectory = game.GamePath,
                UseShellExecute = true
            });
            return Results.Ok(ApiResult.Ok());
        });
    }
}

public record AddGameRequest(string GamePath, string? Name = null, string? ExecutableName = null);
public record UpdateGameRequest(string? Name = null);
