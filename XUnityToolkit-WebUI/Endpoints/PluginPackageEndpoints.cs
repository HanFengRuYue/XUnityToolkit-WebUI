using XUnityToolkit_WebUI.Models;
using XUnityToolkit_WebUI.Services;

namespace XUnityToolkit_WebUI.Endpoints;

public static class PluginPackageEndpoints
{
    public static void MapPluginPackageEndpoints(this WebApplication app)
    {
        // Export plugin package as ZIP download
        app.MapPost("/api/games/{id}/plugin-package/export", async (
            string id,
            GameLibraryService gameLibrary,
            PluginPackageService pkgService,
            CancellationToken ct) =>
        {
            var game = await gameLibrary.GetByIdAsync(id, ct);
            if (game is null)
                return Results.NotFound(ApiResult.Fail("游戏不存在"));
            if (!game.IsUnityGame)
                return Results.BadRequest(ApiResult.Fail("非 Unity 游戏"));
            if (game.InstallState != InstallState.FullyInstalled)
                return Results.BadRequest(ApiResult.Fail("请先完成插件安装"));

            try
            {
                var (stream, fileName) = await pkgService.ExportAsync(game, ct);
                return Results.File(stream, "application/zip", fileName);
            }
            catch (DirectoryNotFoundException)
            {
                return Results.BadRequest(ApiResult.Fail("游戏 BepInEx 目录不存在，请先安装插件"));
            }
            catch (Exception)
            {
                return Results.Json(ApiResult.Fail("生成汉化包失败"), statusCode: 500);
            }
        });

        // Import plugin package from a local ZIP file
        app.MapPost("/api/games/{id}/plugin-package/import", async (
            string id,
            ImportPluginPackageRequest request,
            GameLibraryService gameLibrary,
            PluginPackageService pkgService,
            CancellationToken ct) =>
        {
            var game = await gameLibrary.GetByIdAsync(id, ct);
            if (game is null)
                return Results.NotFound(ApiResult.Fail("游戏不存在"));
            if (!game.IsUnityGame)
                return Results.BadRequest(ApiResult.Fail("非 Unity 游戏"));

            if (string.IsNullOrWhiteSpace(request.ZipPath))
                return Results.BadRequest(ApiResult.Fail("请选择 ZIP 文件"));
            if (!File.Exists(request.ZipPath))
                return Results.BadRequest(ApiResult.Fail("文件不存在"));
            if (!request.ZipPath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                return Results.BadRequest(ApiResult.Fail("仅支持 ZIP 格式"));

            try
            {
                await pkgService.ImportAsync(game, request.ZipPath, ct);
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
                return Results.Json(ApiResult.Fail("导入汉化包失败"), statusCode: 500);
            }
        });
    }
}

public record ImportPluginPackageRequest(string ZipPath);
