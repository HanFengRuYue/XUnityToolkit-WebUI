using XUnityToolkit_WebUI.Models;
using XUnityToolkit_WebUI.Services;

namespace XUnityToolkit_WebUI.Endpoints;

public static class BepInExPluginEndpoints
{
    private static readonly HashSet<InstallState> BepInExStates =
    [
        InstallState.BepInExOnly,
        InstallState.FullyInstalled,
        InstallState.PartiallyInstalled,
    ];

    public static void MapBepInExPluginEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/games/{id}/plugins");

        // List all plugins
        group.MapGet("/", async (
            string id,
            GameLibraryService gameLibrary,
            BepInExPluginService pluginService,
            CancellationToken ct) =>
        {
            var game = await gameLibrary.GetByIdAsync(id, ct);
            if (game is null)
                return Results.NotFound(ApiResult.Fail("游戏不存在"));
            if (!BepInExStates.Contains(game.InstallState))
                return Results.BadRequest(ApiResult.Fail("请先安装 BepInEx"));

            var plugins = await pluginService.ListPluginsAsync(game);
            return Results.Ok(ApiResult<List<BepInExPlugin>>.Ok(plugins));
        });

        // Install from local path
        group.MapPost("/install", async (
            string id,
            InstallPluginRequest request,
            GameLibraryService gameLibrary,
            BepInExPluginService pluginService,
            CancellationToken ct) =>
        {
            var game = await gameLibrary.GetByIdAsync(id, ct);
            if (game is null)
                return Results.NotFound(ApiResult.Fail("游戏不存在"));
            if (!BepInExStates.Contains(game.InstallState))
                return Results.BadRequest(ApiResult.Fail("请先安装 BepInEx"));

            if (string.IsNullOrWhiteSpace(request.FilePath))
                return Results.BadRequest(ApiResult.Fail("请选择文件"));
            if (!File.Exists(request.FilePath))
                return Results.BadRequest(ApiResult.Fail("文件不存在"));

            var ext = Path.GetExtension(request.FilePath);
            if (!ext.Equals(".dll", StringComparison.OrdinalIgnoreCase)
                && !ext.Equals(".zip", StringComparison.OrdinalIgnoreCase))
                return Results.BadRequest(ApiResult.Fail("仅支持 .dll 和 .zip 格式"));

            try
            {
                await pluginService.InstallPluginAsync(game, request.FilePath);
                return Results.Ok(ApiResult.Ok());
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(ApiResult.Fail(ex.Message));
            }
            catch (InvalidDataException ex)
            {
                return Results.BadRequest(ApiResult.Fail(ex.Message));
            }
            catch (Exception)
            {
                return Results.Json(ApiResult.Fail("安装插件失败"), statusCode: 500);
            }
        });

        // Upload and install
        group.MapPost("/upload", async (
            string id,
            IFormFile file,
            GameLibraryService gameLibrary,
            BepInExPluginService pluginService,
            CancellationToken ct) =>
        {
            var game = await gameLibrary.GetByIdAsync(id, ct);
            if (game is null)
                return Results.NotFound(ApiResult.Fail("游戏不存在"));
            if (!BepInExStates.Contains(game.InstallState))
                return Results.BadRequest(ApiResult.Fail("请先安装 BepInEx"));

            if (file.Length > 50 * 1024 * 1024)
                return Results.BadRequest(ApiResult.Fail("文件大小不能超过 50MB"));

            var fileName = Path.GetFileName(file.FileName);
            var ext = Path.GetExtension(fileName);
            if (!ext.Equals(".dll", StringComparison.OrdinalIgnoreCase)
                && !ext.Equals(".zip", StringComparison.OrdinalIgnoreCase))
                return Results.BadRequest(ApiResult.Fail("仅支持 .dll 和 .zip 格式"));

            try
            {
                using var stream = file.OpenReadStream();
                await pluginService.UploadPluginAsync(game, stream, fileName, file.Length);
                return Results.Ok(ApiResult.Ok());
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(ApiResult.Fail(ex.Message));
            }
            catch (InvalidDataException ex)
            {
                return Results.BadRequest(ApiResult.Fail(ex.Message));
            }
            catch (Exception)
            {
                return Results.Json(ApiResult.Fail("上传插件失败"), statusCode: 500);
            }
        }).DisableAntiforgery();

        // Uninstall
        group.MapDelete("/", async (
            string id,
            string relativePath,
            GameLibraryService gameLibrary,
            BepInExPluginService pluginService,
            CancellationToken ct) =>
        {
            var game = await gameLibrary.GetByIdAsync(id, ct);
            if (game is null)
                return Results.NotFound(ApiResult.Fail("游戏不存在"));

            if (string.IsNullOrWhiteSpace(relativePath))
                return Results.BadRequest(ApiResult.Fail("请指定插件路径"));

            try
            {
                await pluginService.UninstallPluginAsync(game, relativePath);
                return Results.Ok(ApiResult.Ok());
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(ApiResult.Fail(ex.Message));
            }
            catch (FileNotFoundException)
            {
                return Results.NotFound(ApiResult.Fail("插件文件不存在"));
            }
            catch (Exception)
            {
                return Results.Json(ApiResult.Fail("卸载插件失败"), statusCode: 500);
            }
        });

        // Toggle enable/disable
        group.MapPost("/toggle", async (
            string id,
            TogglePluginRequest request,
            GameLibraryService gameLibrary,
            BepInExPluginService pluginService,
            CancellationToken ct) =>
        {
            var game = await gameLibrary.GetByIdAsync(id, ct);
            if (game is null)
                return Results.NotFound(ApiResult.Fail("游戏不存在"));

            if (string.IsNullOrWhiteSpace(request.RelativePath))
                return Results.BadRequest(ApiResult.Fail("请指定插件路径"));

            try
            {
                var updatedPlugin = await pluginService.TogglePluginAsync(game, request.RelativePath);
                return Results.Ok(ApiResult<BepInExPlugin>.Ok(updatedPlugin));
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(ApiResult.Fail(ex.Message));
            }
            catch (FileNotFoundException)
            {
                return Results.NotFound(ApiResult.Fail("插件文件不存在"));
            }
            catch (Exception)
            {
                return Results.Json(ApiResult.Fail("切换插件状态失败"), statusCode: 500);
            }
        });

        // Read config file
        group.MapGet("/config", async (
            string id,
            string configFile,
            GameLibraryService gameLibrary,
            BepInExPluginService pluginService,
            CancellationToken ct) =>
        {
            var game = await gameLibrary.GetByIdAsync(id, ct);
            if (game is null)
                return Results.NotFound(ApiResult.Fail("游戏不存在"));

            if (string.IsNullOrWhiteSpace(configFile))
                return Results.BadRequest(ApiResult.Fail("请指定配置文件名"));

            var content = await pluginService.GetConfigAsync(game, configFile);
            if (content is null)
                return Results.NotFound(ApiResult.Fail("配置文件不存在"));

            return Results.Ok(ApiResult<string>.Ok(content));
        });
    }
}

public record InstallPluginRequest(string FilePath);

public record TogglePluginRequest(string RelativePath);
