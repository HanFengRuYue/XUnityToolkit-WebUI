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
            return Results.Ok(ApiResult<List<FontInfo>>.Ok(fonts));
        });

        // POST .../replace
        group.MapPost("/replace", async (string id, FontReplacementRequest request,
            GameLibraryService library, FontReplacementService fontReplacementService,
            AppDataPaths paths, IHubContext<InstallProgressHub> hubContext, CancellationToken ct) =>
        {
            var game = await library.GetByIdAsync(id);
            if (game is null) return Results.NotFound(ApiResult.Fail("Game not found."));
            if (game.DetectedInfo is null)
                return Results.BadRequest(ApiResult.Fail("未检测到 Unity 版本信息。"));

            // Prevent concurrent replacements for the same game (atomic guard)
            var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            if (!_cancellationTokens.TryAdd(id, cts))
            {
                cts.Dispose();
                return Results.Conflict(ApiResult.Fail("字体替换正在进行中，请等待完成或先取消。"));
            }
            try
            {
                var progress = new Progress<FontReplacementProgress>(async p =>
                {
                    await hubContext.Clients.Group($"font-replacement-{id}")
                        .SendAsync("fontReplacementProgress", p, CancellationToken.None);
                });
                // Validate CustomFontPath: must be within the game's custom font directory
                string? validatedFontPath = null;
                if (request.CustomFontPath is not null)
                {
                    var customDir = Path.GetFullPath(paths.GetCustomFontDirectory(id));
                    var resolved = Path.GetFullPath(request.CustomFontPath);
                    if (!resolved.StartsWith(customDir + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                        && !resolved.Equals(customDir, StringComparison.OrdinalIgnoreCase))
                        return Results.BadRequest(ApiResult.Fail("自定义字体路径不安全"));
                    validatedFontPath = resolved;
                }
                var result = await fontReplacementService.ReplaceFontsAsync(
                    game.GamePath, id, game.DetectedInfo,
                    request.Fonts, validatedFontPath, progress, cts.Token);
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

            var safeFileName = Path.GetFileName(file.FileName);
            var destPath = Path.Combine(customDir, safeFileName);

            // Read first 4 bytes for magic detection
            byte[] magic = new byte[4];
            using (var peekStream = file.OpenReadStream())
                await peekStream.ReadExactlyAsync(magic);

            bool isTtfOrOtf = (magic[0] == 0x00 && magic[1] == 0x01 && magic[2] == 0x00 && magic[3] == 0x00) // TTF
                            || (magic[0] == 0x4F && magic[1] == 0x54 && magic[2] == 0x54 && magic[3] == 0x4F); // OTF "OTTO"

            if (isTtfOrOtf)
            {
                // Delete existing TTF/OTF custom fonts only (preserve AssetBundle custom fonts)
                foreach (var existing in Directory.GetFiles(customDir))
                {
                    var ext = Path.GetExtension(existing).ToLowerInvariant();
                    if (ext is ".ttf" or ".otf")
                        File.Delete(existing);
                }

                await using var stream = File.Create(destPath);
                await file.CopyToAsync(stream);
            }
            else
            {
                // AssetBundle: delete existing AssetBundle custom fonts only
                foreach (var existing in Directory.GetFiles(customDir))
                {
                    var ext = Path.GetExtension(existing).ToLowerInvariant();
                    if (ext is not ".ttf" and not ".otf")
                        File.Delete(existing);
                }

                {
                    await using var stream = File.Create(destPath);
                    await file.CopyToAsync(stream);
                }

                if (!fontReplacementService.ValidateCustomFont(destPath))
                {
                    File.Delete(destPath);
                    return Results.BadRequest(ApiResult.Fail("无效的字体文件：未找到 TMP_FontAsset。"));
                }
            }

            return Results.Ok(ApiResult.Ok());
        }).DisableAntiforgery();

        // DELETE .../custom-font
        group.MapDelete("/custom-font", (string id, AppDataPaths appDataPaths) =>
        {
            if (!Guid.TryParse(id, out _))
                return Results.BadRequest(ApiResult.Fail("Invalid game ID"));

            var customDir = appDataPaths.GetCustomFontDirectory(id);
            if (Directory.Exists(customDir))
            {
                foreach (var file in Directory.GetFiles(customDir))
                    File.Delete(file);
            }
            return Results.Ok(ApiResult.Ok());
        });

        // POST .../cancel — only cancel, /replace's finally block owns disposal
        group.MapPost("/cancel", (string id) =>
        {
            if (_cancellationTokens.TryGetValue(id, out var cts))
                cts.Cancel();
            return Results.Ok(ApiResult.Ok());
        });
    }
}
