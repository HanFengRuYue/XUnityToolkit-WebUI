using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using UnityLocalizationToolkit_WebUI.Infrastructure;
using UnityLocalizationToolkit_WebUI.Models;
using UnityLocalizationToolkit_WebUI.Services;

namespace UnityLocalizationToolkit_WebUI.Endpoints;

public static class GameEndpoints
{
    public static void MapGameEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/games");

        group.MapGet("/", async (
            GameLibraryService library,
            GameImageService imageService,
            ManualTranslationService manualTranslation,
            CancellationToken ct) =>
        {
            var games = await library.GetAllAsync();
            await EnrichGamesAsync(games, manualTranslation, imageService, ct);
            return Results.Ok(ApiResult<List<Game>>.Ok(games));
        });

        group.MapGet("/{id}", async (
            string id,
            GameLibraryService library,
            GameImageService imageService,
            ManualTranslationService manualTranslation,
            CancellationToken ct) =>
        {
            var game = await library.GetByIdAsync(id);
            return game is not null
                ? Results.Ok(ApiResult<Game>.Ok(await EnrichGameAsync(game, manualTranslation, imageService, ct)))
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

        group.MapPost("/", async (AddGameRequest request, GameLibraryService library, GameImageService imageService) =>
        {
            if (string.IsNullOrWhiteSpace(request.GamePath))
                return Results.BadRequest(ApiResult<Game>.Fail("Game path is required."));

            if (!Directory.Exists(request.GamePath))
                return Results.BadRequest(ApiResult<Game>.Fail("Game path does not exist."));

            var gamePath = Path.GetFullPath(request.GamePath);
            var name = request.Name ?? Path.GetFileName(gamePath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));

            if (request.ExecutableName is not null &&
                request.ExecutableName.IndexOfAny([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar, '/']) >= 0)
                return Results.BadRequest(ApiResult<Game>.Fail("可执行文件名不能包含路径分隔符。"));

            try
            {
                var game = await library.AddAsync(name, gamePath, request.ExecutableName);
                return Results.Created($"/api/games/{game.Id}", ApiResult<Game>.Ok(game.WithImageFlags(imageService)));
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
            XUnityInstallerService xUnityInstaller,
            ConfigurationService configService,
            AppSettingsService appSettingsService,
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

            var (game, skipReason) = await DetectAndAddAsync(
                folderPath, exePath, library, detection, pluginDetection,
                imageService, xUnityInstaller, configService, appSettingsService, ct);

            if (game is null)
                return Results.Conflict(ApiResult<AddGameResponse>.Fail(skipReason ?? "添加游戏失败。"));

            return Results.Created($"/api/games/{game.Id}",
                ApiResult<AddGameResponse>.Ok(new AddGameResponse
                {
                    NeedsExeSelection = false,
                    Game = game.WithImageFlags(imageService)
                }));
        });

        // Batch add games from subdirectories of a parent folder
        group.MapPost("/batch-add", async (
            BatchAddRequest request,
            GameLibraryService library,
            UnityDetectionService detection,
            PluginDetectionService pluginDetection,
            GameImageService imageService,
            XUnityInstallerService xUnityInstaller,
            ConfigurationService configService,
            AppSettingsService appSettingsService,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(request.ParentFolderPath))
                return Results.BadRequest(ApiResult<BatchAddResult>.Fail("文件夹路径不能为空。"));

            var parentPath = Path.GetFullPath(request.ParentFolderPath);
            if (!Directory.Exists(parentPath))
                return Results.BadRequest(ApiResult<BatchAddResult>.Fail("文件夹不存在。"));

            string[] subDirs;
            try
            {
                subDirs = Directory.GetDirectories(parentPath, "*", SearchOption.TopDirectoryOnly);
            }
            catch (UnauthorizedAccessException)
            {
                return Results.BadRequest(ApiResult<BatchAddResult>.Fail("没有访问该文件夹的权限。"));
            }

            if (subDirs.Length == 0)
                return Results.Ok(ApiResult<BatchAddResult>.Ok(new BatchAddResult()));

            // Snapshot existing paths for fast duplicate check
            var existingPaths = (await library.GetAllAsync())
                .Select(g => g.GamePath)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var added = new List<Game>();
            var skipped = new List<BatchSkippedItem>();

            foreach (var subDir in subDirs)
            {
                var folderPath = Path.GetFullPath(subDir);
                var folderName = Path.GetFileName(folderPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));

                // Skip duplicates
                if (existingPaths.Contains(folderPath))
                {
                    skipped.Add(new BatchSkippedItem { FolderName = folderName, Reason = "已在库中" });
                    continue;
                }

                // Try to find exe
                var exePath = detection.TryFindExecutable(folderPath);
                if (exePath is null)
                {
                    skipped.Add(new BatchSkippedItem { FolderName = folderName, Reason = "未找到可执行文件" });
                    continue;
                }

                try
                {
                    var (game, skipReason) = await DetectAndAddAsync(
                        folderPath, exePath, library, detection, pluginDetection,
                        imageService, xUnityInstaller, configService, appSettingsService, ct);

                    if (game is not null)
                    {
                        added.Add(game.WithImageFlags(imageService));
                        existingPaths.Add(folderPath);
                    }
                    else
                    {
                        skipped.Add(new BatchSkippedItem { FolderName = folderName, Reason = skipReason ?? "添加失败" });
                    }
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    throw; // propagate cancellation
                }
                catch (Exception)
                {
                    skipped.Add(new BatchSkippedItem { FolderName = folderName, Reason = "添加时发生错误" });
                }
            }

            return Results.Ok(ApiResult<BatchAddResult>.Ok(new BatchAddResult
            {
                Added = added,
                Skipped = skipped
            }));
        });

        group.MapPut("/{id}", async (string id, UpdateGameRequest request, GameLibraryService library, GameImageService imageService) =>
        {
            var game = await library.GetByIdAsync(id);
            if (game is null) return Results.NotFound(ApiResult<Game>.Fail("Game not found."));

            if (request.Name is not null) game.Name = request.Name;
            if (request.ExecutableName is not null)
            {
                // Validate ExecutableName is a simple filename (no path separators)
                if (request.ExecutableName.IndexOfAny([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar, '/']) >= 0)
                    return Results.BadRequest(ApiResult<Game>.Fail("可执行文件名不能包含路径分隔符。"));
                game.ExecutableName = request.ExecutableName;
            }
            var updated = await library.UpdateAsync(game);
            return Results.Ok(ApiResult<Game>.Ok(updated.WithImageFlags(imageService)));
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

            try
            {
                using var stream = file.OpenReadStream();
                await imageService.SaveCustomIconFromUploadAsync(id, stream, ct);
            }
            catch (InvalidDataException ex)
            {
                return Results.BadRequest(ApiResult.Fail(ex.Message));
            }

            // Update game timestamp for cache busting
            await library.UpdateAsync(game);
            return Results.Ok(ApiResult.Ok());
        }).DisableAntiforgery();

        // Upload icon from local path
        group.MapPost("/{id}/icon/upload-from-path", async (
            UploadFromPathRequest request,
            string id,
            GameLibraryService library,
            GameImageService imageService,
            CancellationToken ct) =>
        {
            var game = await library.GetByIdAsync(id);
            if (game is null)
                return Results.NotFound(ApiResult.Fail("Game not found."));

            if (string.IsNullOrWhiteSpace(request.FilePath))
                return Results.BadRequest(ApiResult.Fail("请选择文件"));
            if (!File.Exists(request.FilePath))
                return Results.BadRequest(ApiResult.Fail("文件不存在"));

            var info = new FileInfo(request.FilePath);
            if (info.Length > 5 * 1024 * 1024)
                return Results.BadRequest(ApiResult.Fail("图片文件不能超过 5 MB。"));

            var ext = Path.GetExtension(request.FilePath).ToLowerInvariant();
            if (ext is not ".jpg" and not ".jpeg" and not ".png" and not ".webp")
                return Results.BadRequest(ApiResult.Fail("仅支持 JPEG、PNG 或 WebP 格式。"));

            try
            {
                await using var stream = File.OpenRead(request.FilePath);
                await imageService.SaveCustomIconFromUploadAsync(id, stream, ct);
            }
            catch (InvalidDataException ex)
            {
                return Results.BadRequest(ApiResult.Fail(ex.Message));
            }

            await library.UpdateAsync(game);
            return Results.Ok(ApiResult.Ok());
        });

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

        // Web image search for icon
        group.MapPost("/{id}/icon/web-search", async (
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
                    request.SizeFilter ?? "auto", "icon", ct);
                return Results.Ok(ApiResult<List<WebImageResult>>.Ok(results));
            }
            catch (HttpRequestException ex)
            {
                return Results.Json(
                    ApiResult.Fail($"图片搜索失败: {ex.Message}"),
                    statusCode: 502);
            }
        });

        // Select web image as icon
        group.MapPost("/{id}/icon/web-select", async (
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
                await webSearch.SelectAsIconAsync(id, request.ImageUrl, ct);
                await library.UpdateAsync(game);
                return Results.Ok(ApiResult.Ok());
            }
            catch (InvalidDataException ex)
            {
                return Results.BadRequest(ApiResult.Fail(ex.Message));
            }
            catch (HttpRequestException ex)
            {
                return Results.Json(
                    ApiResult.Fail($"下载图片失败: {ex.Message}"),
                    statusCode: 502);
            }
        });

        // Search SteamGridDB games for icon
        group.MapPost("/{id}/icon/search", async (
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

        // Get available icon images for a SteamGridDB game
        group.MapPost("/{id}/icon/grids", async (
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
                var icons = await imageService.GetIconsAsync(request.SteamGridDbGameId, ct);
                return Results.Ok(ApiResult<List<SteamGridDbImage>>.Ok(icons));
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

        // Download a selected SteamGridDB icon and set as game icon
        group.MapPost("/{id}/icon/select", async (
            IconSelectRequest request,
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
                await imageService.SaveCustomIconFromUrlAsync(id, request.ImageUrl, ct);
                game.SteamGridDbGameId = request.SteamGridDbGameId;
                await library.UpdateAsync(game);
                return Results.Ok(ApiResult.Ok());
            }
            catch (InvalidDataException ex)
            {
                return Results.BadRequest(ApiResult.Fail(ex.Message));
            }
            catch (HttpRequestException ex)
            {
                return Results.Json(
                    ApiResult.Fail($"下载图标失败: {ex.Message}"),
                    statusCode: 502);
            }
        });

        group.MapDelete("/{id}", async (string id, GameLibraryService library, GameImageService imageService, AppDataPaths appDataPaths, TermService termService, ScriptTagService scriptTagService, TranslationMemoryService tmService, DynamicPatternService dynamicPatternService, TermExtractionService extractionService, PreTranslationService preTranslationService, PreTranslationCacheMonitor cacheMonitor, LlmTranslationService translationService, GlossaryExtractionService glossaryExtractionService, ManualTranslationService manualTranslationService, CancellationToken ct) =>
        {
            var removed = await library.RemoveAsync(id);
            if (!removed)
                return Results.NotFound(ApiResult.Fail("Game not found."));

            // Clean up cover image and custom icon
            await imageService.DeleteCoverAsync(id, ct);
            await imageService.DeleteCustomIconAsync(id, ct);

            // Clean up font replacement data
            var fontBackupDir = appDataPaths.GetFontBackupDirectory(id);
            if (Directory.Exists(fontBackupDir))
                Directory.Delete(fontBackupDir, recursive: true);
            var customFontDir = appDataPaths.GetCustomFontDirectory(id);
            if (Directory.Exists(customFontDir))
                Directory.Delete(customFontDir, recursive: true);

            // Clean up glossary file
            var glossaryPath = Path.Combine(appDataPaths.GlossariesDirectory, $"{id}.json");
            if (File.Exists(glossaryPath))
                File.Delete(glossaryPath);

            // Clean up old do-not-translate files (transition period)
#pragma warning disable CS0612 // Obsolete DoNotTranslateDirectory needed for cleanup
            var dntFile = appDataPaths.DoNotTranslateFile(id);
#pragma warning restore CS0612
            if (File.Exists(dntFile))
                File.Delete(dntFile);
            var migratedDntFile = dntFile + ".migrated";
            if (File.Exists(migratedDntFile))
                File.Delete(migratedDntFile);
            // Clean up unified term cache
            termService.RemoveCache(id);

            // Clean up script tag rules
            var scriptTagFile = appDataPaths.ScriptTagFile(id);
            if (File.Exists(scriptTagFile))
                File.Delete(scriptTagFile);
            scriptTagService.RemoveCache(id);

            // Clean up translation memory, dynamic patterns, term candidates, and pre-translation cache
            tmService.RemoveCache(id);
            dynamicPatternService.RemoveCache(id);
            extractionService.RemoveCache(id);
            cacheMonitor.UnloadCache();
            translationService.RemoveGameRuntimeState(id);
            glossaryExtractionService.RemoveGameState(id);

            var tmFile = appDataPaths.TranslationMemoryFile(id);
            if (File.Exists(tmFile)) File.Delete(tmFile);
            var dpFile = appDataPaths.DynamicPatternsFile(id);
            if (File.Exists(dpFile)) File.Delete(dpFile);
            var tcFile = appDataPaths.TermCandidatesFile(id);
            if (File.Exists(tcFile)) File.Delete(tcFile);
            await preTranslationService.DeleteCheckpointAsync(id, clearInactiveStatus: true, ct);
            manualTranslationService.DeleteGameData(id);

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
                FileName = game.GamePath,
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

            var exePath = Path.GetFullPath(Path.Combine(game.GamePath, exeName));
            var normalizedGamePath = Path.GetFullPath(game.GamePath).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!exePath.StartsWith(normalizedGamePath, StringComparison.OrdinalIgnoreCase))
                return Results.BadRequest(ApiResult.Fail("可执行文件必须位于游戏目录内。"));
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

        // TMP font management
        group.MapGet("/{id}/tmp-font", async (
            string id,
            GameLibraryService library,
            TmpFontService tmpFontService) =>
        {
            var game = await library.GetByIdAsync(id);
            if (game is null)
                return Results.NotFound(ApiResult.Fail("Game not found."));

            var installed = tmpFontService.IsFontInstalled(game.GamePath);
            return Results.Ok(ApiResult<TmpFontStatus>.Ok(new TmpFontStatus(installed)));
        });

        group.MapPost("/{id}/tmp-font", async (
            string id,
            GameLibraryService library,
            TmpFontService tmpFontService,
            ConfigurationService configService,
            CancellationToken ct) =>
        {
            var game = await library.GetByIdAsync(id);
            if (game is null)
                return Results.NotFound(ApiResult.Fail("Game not found."));

            if (game.InstallState != InstallState.FullyInstalled)
                return Results.BadRequest(ApiResult.Fail("请先安装 BepInEx 和 XUnity.AutoTranslator。"));

            if (game.DetectedInfo is null)
                return Results.BadRequest(ApiResult.Fail("未检测到 Unity 版本信息。"));

            var configValue = tmpFontService.InstallFont(game.GamePath, game.DetectedInfo);
            if (configValue is null)
                return Results.BadRequest(ApiResult.Fail("未找到可用的 TMP 字体文件。"));

            // Patch config to set FallbackFontTextMeshPro
            await configService.PatchSectionAsync(game.GamePath, "Behaviour",
                new Dictionary<string, string>
                {
                    ["FallbackFontTextMeshPro"] = configValue
                }, ct);

            return Results.Ok(ApiResult<TmpFontStatus>.Ok(new TmpFontStatus(true)));
        });

        group.MapDelete("/{id}/tmp-font", async (
            string id,
            GameLibraryService library,
            TmpFontService tmpFontService) =>
        {
            var game = await library.GetByIdAsync(id);
            if (game is null)
                return Results.NotFound(ApiResult.Fail("Game not found."));

            tmpFontService.RemoveFont(game.GamePath);
            return Results.Ok(ApiResult<TmpFontStatus>.Ok(new TmpFontStatus(false)));
        });

        // Unified term management
        group.MapGet("/{id}/terms", async (
            string id,
            TermService termService) =>
        {
            if (!Guid.TryParse(id, out _))
                return Results.BadRequest(ApiResult.Fail("Invalid game ID"));

            var entries = await termService.GetAsync(id);
            return Results.Ok(ApiResult<List<TermEntry>>.Ok(entries));
        });

        group.MapPut("/{id}/terms", async (
            string id,
            List<TermEntry> entries,
            TermService termService) =>
        {
            if (!Guid.TryParse(id, out _))
                return Results.BadRequest(ApiResult.Fail("Invalid game ID"));

            if (entries.Count > 10000)
                return Results.BadRequest(ApiResult.Fail("术语条目不能超过 10000 条。"));

            // Validate regex entries
            foreach (var entry in entries.Where(e => e.IsRegex))
            {
                try { _ = new System.Text.RegularExpressions.Regex(entry.Original, System.Text.RegularExpressions.RegexOptions.None, TimeSpan.FromSeconds(1)); }
                catch (System.Text.RegularExpressions.RegexParseException)
                { return Results.BadRequest(ApiResult.Fail($"无效的正则表达式: {entry.Original}")); }
            }

            await termService.SaveAsync(id, entries);
            return Results.Ok(ApiResult.Ok());
        });

        group.MapPost("/{id}/terms/import-from-game", async (
            string id,
            ImportFromGameRequest request,
            TermService termService) =>
        {
            if (!Guid.TryParse(id, out _))
                return Results.BadRequest(ApiResult.Fail("Invalid game ID"));

            var (added, skipped) = await termService.ImportFromGameAsync(id, request.SourceGameId);
            return Results.Ok(ApiResult<object>.Ok(new { added, skipped }));
        });

        // Glossary management (compat proxy → TermService)
        group.MapGet("/{id}/glossary", async (
            string id,
            GameLibraryService library,
            TermService termService) =>
        {
            if (!Guid.TryParse(id, out _))
                return Results.BadRequest(ApiResult.Fail("Invalid game ID"));

            var game = await library.GetByIdAsync(id);
            if (game is null)
                return Results.NotFound(ApiResult.Fail("Game not found."));

            var allTerms = await termService.GetAsync(id);
            var glossaryEntries = allTerms
                .Where(t => t.Type == TermType.Translate)
                .Select(t => new { original = t.Original, translation = t.Translation ?? "", isRegex = t.IsRegex, description = t.Description })
                .ToList();
            return Results.Ok(ApiResult<object>.Ok(glossaryEntries));
        });

        group.MapPut("/{id}/glossary", async (
            string id,
            List<GlossaryCompatEntry> entries,
            GameLibraryService library,
            TermService termService) =>
        {
            if (!Guid.TryParse(id, out _))
                return Results.BadRequest(ApiResult.Fail("Invalid game ID"));

            var game = await library.GetByIdAsync(id);
            if (game is null)
                return Results.NotFound(ApiResult.Fail("Game not found."));

            if (entries.Count > 5000)
                return Results.BadRequest(ApiResult.Fail("术语表条目不能超过 5000 条。"));

            // Validate regex entries
            foreach (var entry in entries.Where(e => e.IsRegex))
            {
                try { _ = new System.Text.RegularExpressions.Regex(entry.Original, System.Text.RegularExpressions.RegexOptions.None, TimeSpan.FromSeconds(1)); }
                catch (System.Text.RegularExpressions.RegexParseException)
                { return Results.BadRequest(ApiResult.Fail($"无效的正则表达式: {entry.Original}")); }
            }

            var newTerms = entries.Select(e => new TermEntry
            {
                Type = TermType.Translate,
                Original = e.Original,
                Translation = e.Translation,
                IsRegex = e.IsRegex,
                Description = e.Description
            }).ToList();

            await termService.ReplaceByTypeAsync(id, TermType.Translate, newTerms);
            return Results.Ok(ApiResult<List<GlossaryCompatEntry>>.Ok(entries));
        });

        // Do-not-translate list management (compat proxy → TermService)
        group.MapGet("/{id}/do-not-translate", async (
            string id,
            GameLibraryService library,
            TermService termService) =>
        {
            if (!Guid.TryParse(id, out _))
                return Results.BadRequest(ApiResult.Fail("Invalid game ID"));

            var game = await library.GetByIdAsync(id);
            if (game is null)
                return Results.NotFound(ApiResult.Fail("Game not found."));

            var allTerms = await termService.GetAsync(id);
            var dntEntries = allTerms
                .Where(t => t.Type == TermType.DoNotTranslate)
                .Select(t => new { original = t.Original, caseSensitive = t.CaseSensitive })
                .ToList();
            return Results.Ok(ApiResult<object>.Ok(dntEntries));
        });

        group.MapPut("/{id}/do-not-translate", async (
            string id,
            List<DntCompatEntry> entries,
            GameLibraryService library,
            TermService termService) =>
        {
            if (!Guid.TryParse(id, out _))
                return Results.BadRequest(ApiResult.Fail("Invalid game ID"));

            var game = await library.GetByIdAsync(id);
            if (game is null)
                return Results.NotFound(ApiResult.Fail("Game not found."));

            if (entries.Count > 10000)
                return Results.BadRequest(ApiResult.Fail("免翻译列表条目不能超过 10000 条。"));

            var newTerms = entries.Select(e => new TermEntry
            {
                Type = TermType.DoNotTranslate,
                Original = e.Original,
                CaseSensitive = e.CaseSensitive
            }).ToList();

            await termService.ReplaceByTypeAsync(id, TermType.DoNotTranslate, newTerms);
            return Results.Ok(ApiResult<List<DntCompatEntry>>.Ok(entries));
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
            GameImageService imageService,
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

            return Results.Ok(ApiResult<Game>.Ok(game.WithImageFlags(imageService)));
        });
    }

    /// <summary>
    /// Shared detection + add logic used by both add-with-detection and batch-add endpoints.
    /// Returns (game, null) on success, or (null, reason) on skip/failure.
    /// </summary>
    private static async Task<(Game? game, string? skipReason)> DetectAndAddAsync(
        string folderPath,
        string exePath,
        GameLibraryService library,
        UnityDetectionService detection,
        PluginDetectionService pluginDetection,
        GameImageService imageService,
        XUnityInstallerService xUnityInstaller,
        ConfigurationService configService,
        AppSettingsService appSettingsService,
        CancellationToken ct)
    {
        var name = Path.GetFileName(folderPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        var exeName = Path.GetFileName(exePath);

        // Detect Unity
        var isUnity = detection.CheckIsUnityGame(folderPath, exePath);

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

            // Sync GameId in INI when re-adding a game with existing plugins
            if (installState == InstallState.FullyInstalled &&
                xUnityInstaller.IsTranslatorEndpointInstalled(folderPath))
            {
                var configPath = configService.GetConfigPath(folderPath);
                if (File.Exists(configPath))
                {
                    var settings = await appSettingsService.GetAsync(ct);
                    var port = settings.AiTranslation.Port;
                    await configService.PatchSectionAsync(folderPath, "LLMTranslate",
                        new Dictionary<string, string>
                        {
                            ["ToolkitUrl"] = $"http://127.0.0.1:{port}",
                            ["GameId"] = game.Id
                        }, ct);
                }
            }

            // Auto-fetch cover from Steam CDN (best effort)
            if (steamAppId.HasValue && !imageService.HasCover(game.Id))
            {
                try { await imageService.SaveCoverFromSteamAsync(game.Id, steamAppId.Value, ct); }
                catch { /* cover can be set manually later */ }
            }

            return (game, null);
        }
        catch (InvalidOperationException ex)
        {
            return (null, ex.Message);
        }
    }

    private static async Task EnrichGamesAsync(
        IEnumerable<Game> games,
        ManualTranslationService manualTranslation,
        GameImageService imageService,
        CancellationToken ct)
    {
        foreach (var game in games)
        {
            await EnrichGameAsync(game, manualTranslation, imageService, ct);
        }
    }

    private static async Task<Game> EnrichGameAsync(
        Game game,
        ManualTranslationService manualTranslation,
        GameImageService imageService,
        CancellationToken ct)
    {
        game.WithImageFlags(imageService);
        game.XUnityStatus = CreateXUnitySummary(game);
        game.ManualTranslationStatus = await manualTranslation.GetWorkspaceSummaryAsync(game.Id, ct);
        return game;
    }

    private static GameTranslationWorkspaceSummary CreateXUnitySummary(Game game)
    {
        var installed = game.InstallState is InstallState.BepInExOnly or InstallState.FullyInstalled;
        return new GameTranslationWorkspaceSummary
        {
            Workspace = GameTranslationWorkspace.XUnity,
            State = game.InstallState switch
            {
                InstallState.FullyInstalled => "installed",
                InstallState.BepInExOnly => "bepinexOnly",
                InstallState.PartiallyInstalled => "partial",
                _ => "notInstalled"
            },
            Available = installed,
            Active = game.InstallState == InstallState.FullyInstalled,
            Summary = game.InstallState switch
            {
                InstallState.FullyInstalled => "XUnity translator is installed",
                InstallState.BepInExOnly => "BepInEx is installed, XUnity translator is not",
                InstallState.PartiallyInstalled => "Existing plugin files were detected",
                _ => "XUnity translator is not installed"
            }
        };
    }
}

public static class GameImageExtensions
{
    public static Game WithImageFlags(this Game game, GameImageService imageService)
    {
        game.HasCover = imageService.HasCover(game.Id);
        game.HasBackground = imageService.HasBackground(game.Id);
        return game;
    }

    public static List<Game> WithImageFlags(this List<Game> games, GameImageService imageService)
    {
        foreach (var game in games)
            game.WithImageFlags(imageService);
        return games;
    }
}

public record AddGameRequest(string GamePath, string? Name = null, string? ExecutableName = null);
public record AddWithDetectionRequest(string FolderPath, string? ExePath = null);
public record BatchAddRequest(string ParentFolderPath);
public record UpdateGameRequest(string? Name = null, string? ExecutableName = null);
public record AiEndpointStatus(bool Installed);
public record TmpFontStatus(bool Installed);
public record UpdateDescriptionRequest(string? Description);
public record IconSelectRequest(string ImageUrl, int SteamGridDbGameId);
public record ImportFromGameRequest(string SourceGameId);

// Compat request models for legacy glossary/DNT API shape
public sealed class GlossaryCompatEntry
{
    public string Original { get; set; } = "";
    public string Translation { get; set; } = "";
    public bool IsRegex { get; set; }
    public string? Description { get; set; }
}

public sealed class DntCompatEntry
{
    public string Original { get; set; } = "";
    public bool CaseSensitive { get; set; } = true;
}
