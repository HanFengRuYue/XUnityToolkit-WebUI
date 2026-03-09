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

        // Add game with auto-detection (Unity check, plugin scan, exe discovery)
        group.MapPost("/add-with-detection", async (
            AddWithDetectionRequest request,
            GameLibraryService library,
            UnityDetectionService detection,
            PluginDetectionService pluginDetection) =>
        {
            if (string.IsNullOrWhiteSpace(request.FolderPath))
                return Results.BadRequest(ApiResult<AddGameResponse>.Fail("文件夹路径不能为空。"));

            if (!Directory.Exists(request.FolderPath))
                return Results.BadRequest(ApiResult<AddGameResponse>.Fail("文件夹不存在。"));

            // Normalize path
            var folderPath = Path.GetFullPath(request.FolderPath);

            // Duplicate check
            var existing = (await library.GetAllAsync())
                .FirstOrDefault(g => g.GamePath.Equals(folderPath, StringComparison.OrdinalIgnoreCase));
            if (existing is not null)
                return Results.Conflict(ApiResult<AddGameResponse>.Fail("该游戏已在库中。"));

            var name = Path.GetFileName(folderPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));

            // Find exe — use explicit path if provided
            string? exePath;
            if (!string.IsNullOrEmpty(request.ExePath))
            {
                var fullExePath = Path.GetFullPath(request.ExePath);
                // Validate exe is inside game folder
                var normalizedFolder = folderPath.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
                if (!fullExePath.StartsWith(normalizedFolder, StringComparison.OrdinalIgnoreCase))
                    return Results.BadRequest(ApiResult<AddGameResponse>.Fail("可执行文件必须位于游戏目录内。"));
                if (!File.Exists(fullExePath))
                    return Results.BadRequest(ApiResult<AddGameResponse>.Fail("指定的可执行文件不存在。"));
                exePath = fullExePath;
            }
            else
            {
                exePath = detection.TryFindExecutable(folderPath);
            }

            // No exe found — signal frontend to show file picker
            if (exePath is null)
            {
                return Results.Ok(ApiResult<AddGameResponse>.Ok(new AddGameResponse
                {
                    NeedsExeSelection = true,
                    Game = null
                }));
            }

            // Detect Unity
            var isUnity = detection.CheckIsUnityGame(folderPath, exePath);
            var exeName = Path.GetFileName(exePath);

            UnityGameInfo? unityInfo = null;
            if (isUnity)
            {
                try { unityInfo = detection.BuildUnityGameInfo(folderPath, exePath); }
                catch { /* detection partial failure — still add as Unity game */ }
            }

            // Detect installed plugins/frameworks
            var frameworks = await pluginDetection.DetectAsync(folderPath);

            // Determine install state from detected frameworks
            var installState = InstallState.NotInstalled;
            string? bepInExVersion = null;
            string? xUnityVersion = null;

            var bepInEx = frameworks.FirstOrDefault(f => f.Framework == ModFrameworkType.BepInEx);
            if (bepInEx is not null)
            {
                bepInExVersion = bepInEx.Version;
                if (bepInEx.HasXUnityPlugin)
                {
                    installState = InstallState.FullyInstalled;
                    xUnityVersion = bepInEx.XUnityVersion;
                }
                else
                {
                    installState = InstallState.BepInExOnly;
                }
            }

            // Build and save game
            try
            {
                var game = await library.AddAsync(name, folderPath, exeName);
                game.IsUnityGame = isUnity;
                game.DetectedInfo = unityInfo;
                game.InstallState = installState;
                game.InstalledBepInExVersion = bepInExVersion;
                game.InstalledXUnityVersion = xUnityVersion;
                game.DetectedFrameworks = frameworks.Count > 0 ? frameworks : null;
                await library.UpdateAsync(game);

                return Results.Created($"/api/games/{game.Id}",
                    ApiResult<AddGameResponse>.Ok(new AddGameResponse
                    {
                        NeedsExeSelection = false,
                        Game = game
                    }));
            }
            catch (InvalidOperationException ex)
            {
                return Results.Conflict(ApiResult<AddGameResponse>.Fail(ex.Message));
            }
        });

        group.MapPut("/{id}", async (string id, UpdateGameRequest request, GameLibraryService library) =>
        {
            var game = await library.GetByIdAsync(id);
            if (game is null) return Results.NotFound(ApiResult<Game>.Fail("Game not found."));

            if (request.Name is not null) game.Name = request.Name;
            if (request.ExecutableName is not null) game.ExecutableName = request.ExecutableName;
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

        // Uninstall a non-BepInEx mod framework
        group.MapDelete("/{id}/framework/{framework}", async (
            string id,
            ModFrameworkType framework,
            GameLibraryService library,
            PluginDetectionService pluginDetection) =>
        {
            var game = await library.GetByIdAsync(id);
            if (game is null)
                return Results.NotFound(ApiResult.Fail("Game not found."));

            try
            {
                await pluginDetection.UninstallFrameworkAsync(game.GamePath, framework);
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(ApiResult.Fail(ex.Message));
            }

            // Re-detect frameworks after uninstall
            var frameworks = await pluginDetection.DetectAsync(game.GamePath);
            game.DetectedFrameworks = frameworks.Count > 0 ? frameworks : null;
            await library.UpdateAsync(game);

            return Results.Ok(ApiResult<Game>.Ok(game));
        });
    }
}

public record AddGameRequest(string GamePath, string? Name = null, string? ExecutableName = null);
public record AddWithDetectionRequest(string FolderPath, string? ExePath = null);
public record UpdateGameRequest(string? Name = null, string? ExecutableName = null);
