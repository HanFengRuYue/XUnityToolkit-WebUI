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

        group.MapGet("/{id}/icon", async (HttpContext context, string id, GameLibraryService library, GameImageService imageService, AppDataPaths paths, CancellationToken ct) =>
        {
            var game = await library.GetByIdAsync(id);
            if (game is null)
                return Results.NotFound();

            context.Response.Headers.CacheControl = "public, max-age=31536000, immutable";

            // Check for custom icon first (from Steam/SteamGridDB/upload)
            var customIcon = await imageService.GetCustomIconAsync(id, ct);
            if (customIcon is not null)
                return Results.File(customIcon, "image/png");

            // Fall back to exe icon extraction
            var exeName = game.ExecutableName ?? game.DetectedInfo?.DetectedExecutable;
            if (string.IsNullOrEmpty(exeName))
                return Results.NotFound();

            var exePath = Path.Combine(game.GamePath, exeName);
            if (!File.Exists(exePath))
                return Results.NotFound();

            // Check exe icon cache
            var iconsDir = paths.IconsDirectory;
            Directory.CreateDirectory(iconsDir);
            var cachePath = Path.Combine(iconsDir, $"{game.Id}.png");

            if (File.Exists(cachePath))
            {
                var exeTime = File.GetLastWriteTimeUtc(exePath);
                var cacheTime = File.GetLastWriteTimeUtc(cachePath);
                if (cacheTime > exeTime)
                {
                    var bytes = await File.ReadAllBytesAsync(cachePath, ct);
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

            await File.WriteAllBytesAsync(cachePath, pngBytes, ct);

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
            PluginDetectionService pluginDetection,
            GameImageService imageService,
            CancellationToken ct) =>
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

            // Detect Steam AppID from steam_appid.txt
            int? steamAppId = null;
            var steamAppIdFile = Path.Combine(folderPath, "steam_appid.txt");
            if (File.Exists(steamAppIdFile))
            {
                try
                {
                    var content = (await File.ReadAllTextAsync(steamAppIdFile, ct)).Trim();
                    if (int.TryParse(content, out var parsedId) && parsedId > 0)
                        steamAppId = parsedId;
                }
                catch { /* ignore read errors */ }
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
                game.SteamAppId = steamAppId;
                await library.UpdateAsync(game);

                // Auto-fetch cover from Steam CDN (best effort)
                if (steamAppId.HasValue && !imageService.HasCover(game.Id))
                {
                    try { await imageService.SaveCoverFromSteamAsync(game.Id, steamAppId.Value, ct); }
                    catch { /* cover can be set manually later */ }
                }

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

        // Upload custom icon
        group.MapPost("/{id}/icon/upload", async (
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
            var file = form.Files.GetFile("icon");
            if (file is null || file.Length == 0)
                return Results.BadRequest(ApiResult.Fail("请选择一个图片文件。"));

            if (file.Length > 5 * 1024 * 1024)
                return Results.BadRequest(ApiResult.Fail("图片文件不能超过 5 MB。"));

            if (!GameImageService.IsAllowedContentType(file.ContentType))
                return Results.BadRequest(ApiResult.Fail("仅支持 JPEG、PNG 或 WebP 格式。"));

            using var stream = file.OpenReadStream();
            await imageService.SaveCustomIconFromUploadAsync(id, stream, ct);

            // Update game timestamp for cache busting
            await library.UpdateAsync(game);
            return Results.Ok(ApiResult.Ok());
        }).DisableAntiforgery();

        // Delete custom icon
        group.MapDelete("/{id}/icon/custom", async (
            string id,
            GameLibraryService library,
            GameImageService imageService,
            CancellationToken ct) =>
        {
            var game = await library.GetByIdAsync(id);
            if (game is null)
                return Results.NotFound(ApiResult.Fail("Game not found."));

            await imageService.DeleteCustomIconAsync(id, ct);
            await library.UpdateAsync(game);
            return Results.Ok(ApiResult.Ok());
        });

        group.MapDelete("/{id}", async (string id, GameLibraryService library, GameImageService imageService, CancellationToken ct) =>
        {
            var removed = await library.RemoveAsync(id);
            if (!removed)
                return Results.NotFound(ApiResult.Fail("Game not found."));

            // Clean up cover image and custom icon
            await imageService.DeleteCoverAsync(id, ct);
            await imageService.DeleteCustomIconAsync(id, ct);
            return Results.Ok(ApiResult.Ok());
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

            // Track last played time
            game.LastPlayedAt = DateTime.UtcNow;
            await library.UpdateAsync(game);

            return Results.Ok(ApiResult.Ok());
        });

        // AI translation endpoint management
        group.MapGet("/{id}/ai-endpoint", async (
            string id,
            GameLibraryService library,
            XUnityInstallerService xUnityInstaller) =>
        {
            var game = await library.GetByIdAsync(id);
            if (game is null)
                return Results.NotFound(ApiResult.Fail("Game not found."));

            var installed = xUnityInstaller.IsTranslatorEndpointInstalled(game.GamePath);
            return Results.Ok(ApiResult<AiEndpointStatus>.Ok(new AiEndpointStatus(installed)));
        });

        group.MapPost("/{id}/ai-endpoint", async (
            string id,
            GameLibraryService library,
            XUnityInstallerService xUnityInstaller,
            ConfigurationService configService,
            AppSettingsService appSettingsService,
            CancellationToken ct) =>
        {
            var game = await library.GetByIdAsync(id);
            if (game is null)
                return Results.NotFound(ApiResult.Fail("Game not found."));

            if (game.InstallState != InstallState.FullyInstalled)
                return Results.BadRequest(ApiResult.Fail("请先安装 BepInEx 和 XUnity.AutoTranslator。"));

            var deployed = xUnityInstaller.ForceDeployTranslatorEndpoint(game.GamePath);
            if (!deployed)
                return Results.BadRequest(ApiResult.Fail("AI 翻译端点 DLL 不可用（未嵌入构建）。"));

            // Patch config to ensure LLMTranslate uses 127.0.0.1 (avoids localhost IPv6 issues)
            var configPath = configService.GetConfigPath(game.GamePath);
            if (File.Exists(configPath))
            {
                var settings = await appSettingsService.GetAsync(ct);
                var port = settings.AiTranslation.Port;
                await configService.PatchSectionAsync(game.GamePath, "LLMTranslate",
                    new Dictionary<string, string>
                    {
                        ["ToolkitUrl"] = $"http://127.0.0.1:{port}",
                        ["MaxConcurrency"] = "10",
                        ["MaxTranslationsPerRequest"] = "10",
                        ["GameId"] = id
                    }, ct);
            }

            return Results.Ok(ApiResult<AiEndpointStatus>.Ok(new AiEndpointStatus(true)));
        });

        group.MapDelete("/{id}/ai-endpoint", async (
            string id,
            GameLibraryService library,
            XUnityInstallerService xUnityInstaller) =>
        {
            var game = await library.GetByIdAsync(id);
            if (game is null)
                return Results.NotFound(ApiResult.Fail("Game not found."));

            xUnityInstaller.RemoveTranslatorEndpoint(game.GamePath);
            return Results.Ok(ApiResult<AiEndpointStatus>.Ok(new AiEndpointStatus(false)));
        });

        // Glossary management
        group.MapGet("/{id}/glossary", async (
            string id,
            GameLibraryService library,
            GlossaryService glossaryService,
            CancellationToken ct) =>
        {
            var game = await library.GetByIdAsync(id);
            if (game is null)
                return Results.NotFound(ApiResult.Fail("Game not found."));

            var entries = await glossaryService.GetAsync(id, ct);
            return Results.Ok(ApiResult<List<GlossaryEntry>>.Ok(entries));
        });

        group.MapPut("/{id}/glossary", async (
            string id,
            List<GlossaryEntry> entries,
            GameLibraryService library,
            GlossaryService glossaryService,
            CancellationToken ct) =>
        {
            var game = await library.GetByIdAsync(id);
            if (game is null)
                return Results.NotFound(ApiResult.Fail("Game not found."));

            await glossaryService.SaveAsync(id, entries, ct);
            return Results.Ok(ApiResult<List<GlossaryEntry>>.Ok(entries));
        });

        // Game AI description (per-game context for translation)
        group.MapGet("/{id}/description", async (
            string id,
            GameLibraryService library,
            CancellationToken ct) =>
        {
            var game = await library.GetByIdAsync(id, ct);
            if (game is null)
                return Results.NotFound(ApiResult.Fail("Game not found."));

            return Results.Ok(ApiResult<string?>.Ok(game.AiDescription));
        });

        group.MapPut("/{id}/description", async (
            string id,
            UpdateDescriptionRequest request,
            GameLibraryService library,
            LlmTranslationService translationService,
            CancellationToken ct) =>
        {
            var game = await library.GetByIdAsync(id, ct);
            if (game is null)
                return Results.NotFound(ApiResult.Fail("Game not found."));

            game.AiDescription = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim();
            await library.UpdateAsync(game, ct);

            // Invalidate in-memory cache so hot-path picks up the new description
            translationService.InvalidateDescriptionCache(id, game.AiDescription);

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
public record AiEndpointStatus(bool Installed);
public record UpdateDescriptionRequest(string? Description);
