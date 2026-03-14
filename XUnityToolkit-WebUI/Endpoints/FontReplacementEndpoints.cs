using System.Collections.Concurrent;
using Microsoft.AspNetCore.SignalR;
using XUnityToolkit_WebUI.Hubs;
using XUnityToolkit_WebUI.Infrastructure;
using XUnityToolkit_WebUI.Models;
using XUnityToolkit_WebUI.Services;

namespace XUnityToolkit_WebUI.Endpoints;

public static class FontReplacementEndpoints
{
    private static readonly ConcurrentDictionary<string, CancellationTokenSource> _cancellationTokens = new();

    public static void MapFontReplacementEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/games/{id}/font-replacement");

        // POST .../scan
        group.MapPost("/scan", async (string id, GameLibraryService library,
            FontReplacementService fontReplacementService, CancellationToken ct) =>
        {
            var game = await library.GetByIdAsync(id);
            if (game is null) return Results.NotFound(ApiResult.Fail("Game not found."));
            if (game.DetectedInfo is null)
                return Results.BadRequest(ApiResult.Fail("未检测到 Unity 版本信息。"));

            var fonts = await fontReplacementService.ScanFontsAsync(game.GamePath, game.DetectedInfo, ct);
            return Results.Ok(ApiResult<List<TmpFontInfo>>.Ok(fonts));
        });

        // POST .../replace
        group.MapPost("/replace", async (string id, FontReplacementRequest request,
            GameLibraryService library, FontReplacementService fontReplacementService,
            IHubContext<InstallProgressHub> hubContext, CancellationToken ct) =>
        {
            var game = await library.GetByIdAsync(id);
            if (game is null) return Results.NotFound(ApiResult.Fail("Game not found."));
            if (game.DetectedInfo is null)
                return Results.BadRequest(ApiResult.Fail("未检测到 Unity 版本信息。"));

            var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            _cancellationTokens[id] = cts;
            try
            {
                var progress = new Progress<FontReplacementProgress>(async p =>
                {
                    await hubContext.Clients.Group($"font-replacement-{id}")
                        .SendAsync("fontReplacementProgress", p, CancellationToken.None);
                });
                var result = await fontReplacementService.ReplaceFontsAsync(
                    game.GamePath, id, game.DetectedInfo,
                    request.Fonts, request.CustomFontPath, progress, cts.Token);
                return Results.Ok(ApiResult<FontReplacementResult>.Ok(result));
            }
            finally
            {
                _cancellationTokens.TryRemove(id, out _);
                cts.Dispose();
            }
        });

        // POST .../restore
        group.MapPost("/restore", async (string id, GameLibraryService library,
            FontReplacementService fontReplacementService) =>
        {
            var game = await library.GetByIdAsync(id);
            if (game is null) return Results.NotFound(ApiResult.Fail("Game not found."));
            await fontReplacementService.RestoreFontsAsync(game.GamePath, id);
            return Results.Ok(ApiResult.Ok());
        });

        // GET .../status
        group.MapGet("/status", async (string id, GameLibraryService library,
            FontReplacementService fontReplacementService) =>
        {
            var game = await library.GetByIdAsync(id);
            if (game is null) return Results.NotFound(ApiResult.Fail("Game not found."));
            var status = await fontReplacementService.GetStatusAsync(game.GamePath, id);
            return Results.Ok(ApiResult<FontReplacementStatus>.Ok(status));
        });

        // POST .../upload
        group.MapPost("/upload", async (string id, HttpRequest request,
            GameLibraryService library, FontReplacementService fontReplacementService,
            AppDataPaths appDataPaths) =>
        {
            var game = await library.GetByIdAsync(id);
            if (game is null) return Results.NotFound(ApiResult.Fail("Game not found."));
            if (!request.HasFormContentType)
                return Results.BadRequest(ApiResult.Fail("请使用 multipart/form-data 上传。"));
            var form = await request.ReadFormAsync();
            var file = form.Files.FirstOrDefault();
            if (file is null || file.Length == 0)
                return Results.BadRequest(ApiResult.Fail("未选择文件。"));
            if (file.Length > 50 * 1024 * 1024)
                return Results.BadRequest(ApiResult.Fail("文件大小超过 50MB 限制。"));

            var customDir = appDataPaths.GetCustomFontDirectory(id);
            Directory.CreateDirectory(customDir);
            foreach (var existing in Directory.GetFiles(customDir))
                File.Delete(existing);

            var destPath = Path.Combine(customDir, Path.GetFileName(file.FileName));
            await using var stream = File.Create(destPath);
            await file.CopyToAsync(stream);
            stream.Close();

            if (!fontReplacementService.ValidateCustomFont(destPath))
            {
                File.Delete(destPath);
                return Results.BadRequest(ApiResult.Fail("无效的字体文件：未找到 TMP_FontAsset。"));
            }
            return Results.Ok(ApiResult.Ok());
        });

        // DELETE .../custom-font
        group.MapDelete("/custom-font", (string id, AppDataPaths appDataPaths) =>
        {
            var customDir = appDataPaths.GetCustomFontDirectory(id);
            if (Directory.Exists(customDir))
            {
                foreach (var file in Directory.GetFiles(customDir))
                    File.Delete(file);
            }
            return Results.Ok(ApiResult.Ok());
        });

        // POST .../cancel
        group.MapPost("/cancel", (string id) =>
        {
            if (_cancellationTokens.TryGetValue(id, out var cts))
                cts.Cancel();
            return Results.Ok(ApiResult.Ok());
        });
    }
}
