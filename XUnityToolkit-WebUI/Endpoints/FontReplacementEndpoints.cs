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
    private sealed record FontUploadFromPathRequest(string FilePath, string Kind);

    private static string? NormalizeUploadKind(string? kind) => kind?.Trim().ToLowerInvariant() switch
    {
        "ttf" => "TTF",
        "tmp" => "TMP",
        _ => null
    };

    private static bool IsTtfOrOtf(byte[] magic) =>
        (magic[0] == 0x00 && magic[1] == 0x01 && magic[2] == 0x00 && magic[3] == 0x00)
        || (magic[0] == 0x4F && magic[1] == 0x54 && magic[2] == 0x54 && magic[3] == 0x4F);

    public static void MapFontReplacementEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/games/{id}/font-replacement");

        group.MapPost("/scan", async (string id, GameLibraryService library,
            FontReplacementService fontReplacementService, CancellationToken ct) =>
        {
            var game = await library.GetByIdAsync(id);
            if (game is null) return Results.NotFound(ApiResult.Fail("Game not found."));
            if (game.DetectedInfo is null)
                return Results.BadRequest(ApiResult.Fail("Unity version information was not detected."));

            var fonts = await fontReplacementService.ScanFontsAsync(game.GamePath, game.DetectedInfo, ct);
            return Results.Ok(ApiResult<List<FontInfo>>.Ok(fonts));
        });

        group.MapPost("/replace", async (string id, FontReplacementRequest request,
            GameLibraryService library, FontReplacementService fontReplacementService,
            AppDataPaths paths, IHubContext<InstallProgressHub> hubContext, CancellationToken ct) =>
        {
            var game = await library.GetByIdAsync(id);
            if (game is null) return Results.NotFound(ApiResult.Fail("Game not found."));
            if (game.DetectedInfo is null)
                return Results.BadRequest(ApiResult.Fail("Unity version information was not detected."));

            var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            if (!_cancellationTokens.TryAdd(id, cts))
            {
                cts.Dispose();
                return Results.Conflict(ApiResult.Fail("A font replacement task is already running for this game."));
            }

            try
            {
                var progress = new Progress<FontReplacementProgress>(async p =>
                {
                    await hubContext.Clients.Group($"font-replacement-{id}")
                        .SendAsync("fontReplacementProgress", p, CancellationToken.None);
                });

                if (request.CustomFontPath is not null)
                {
                    var customDir = Path.GetFullPath(paths.GetCustomFontDirectory(id));
                    var resolved = Path.GetFullPath(request.CustomFontPath);
                    if (!resolved.StartsWith(customDir + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                        && !resolved.Equals(customDir, StringComparison.OrdinalIgnoreCase))
                    {
                        return Results.BadRequest(ApiResult.Fail("The custom font path is not allowed."));
                    }
                }

                var result = await fontReplacementService.ReplaceFontsAsync(
                    game.GamePath,
                    id,
                    game.DetectedInfo,
                    request.Fonts,
                    progress,
                    cts.Token);
                return Results.Ok(ApiResult<FontReplacementResult>.Ok(result));
            }
            finally
            {
                _cancellationTokens.TryRemove(id, out _);
                cts.Dispose();
            }
        });

        group.MapPost("/restore", async (string id, GameLibraryService library,
            FontReplacementService fontReplacementService) =>
        {
            var game = await library.GetByIdAsync(id);
            if (game is null) return Results.NotFound(ApiResult.Fail("Game not found."));
            await fontReplacementService.RestoreFontsAsync(game.GamePath, id);
            return Results.Ok(ApiResult.Ok());
        });

        group.MapGet("/status", async (string id, GameLibraryService library,
            FontReplacementService fontReplacementService) =>
        {
            var game = await library.GetByIdAsync(id);
            if (game is null) return Results.NotFound(ApiResult.Fail("Game not found."));
            var status = await fontReplacementService.GetStatusAsync(
                game.GamePath,
                id,
                game.DetectedInfo?.UnityVersion);
            return Results.Ok(ApiResult<FontReplacementStatus>.Ok(status));
        });

        group.MapPost("/upload", async (string id, HttpRequest request,
            GameLibraryService library, FontReplacementService fontReplacementService) =>
        {
            var game = await library.GetByIdAsync(id);
            if (game is null) return Results.NotFound(ApiResult.Fail("Game not found."));
            if (!request.HasFormContentType)
                return Results.BadRequest(ApiResult.Fail("Please upload the file with multipart/form-data."));

            var form = await request.ReadFormAsync();
            var file = form.Files.FirstOrDefault();
            if (file is null || file.Length == 0)
                return Results.BadRequest(ApiResult.Fail("No file was selected."));
            if (file.Length > 50 * 1024 * 1024)
                return Results.BadRequest(ApiResult.Fail("File size exceeds the 50 MB limit."));

            var requestedKind = NormalizeUploadKind(form["kind"].FirstOrDefault());
            if (requestedKind is null)
                return Results.BadRequest(ApiResult.Fail("Specify the upload kind with kind=ttf or kind=tmp."));

            byte[] magic = new byte[4];
            using (var peekStream = file.OpenReadStream())
                await peekStream.ReadExactlyAsync(magic);

            var detectedKind = IsTtfOrOtf(magic) ? "TTF" : "TMP";
            if (!string.Equals(requestedKind, detectedKind, StringComparison.Ordinal))
                return Results.BadRequest(ApiResult.Fail("The uploaded file type does not match the selected kind."));

            var destinationPath = fontReplacementService.GetUniqueCustomSourcePath(
                id,
                requestedKind,
                Path.GetFileName(file.FileName));

            await using (var stream = File.Create(destinationPath))
                await file.CopyToAsync(stream);

            if (detectedKind == "TMP" && !fontReplacementService.ValidateCustomFont(destinationPath))
            {
                File.Delete(destinationPath);
                return Results.BadRequest(ApiResult.Fail("Invalid TMP font bundle: TMP_FontAsset was not found."));
            }

            return Results.Ok(ApiResult.Ok());
        }).DisableAntiforgery();

        group.MapPost("/upload-from-path", async (string id, FontUploadFromPathRequest request,
            GameLibraryService library, FontReplacementService fontReplacementService) =>
        {
            var game = await library.GetByIdAsync(id);
            if (game is null) return Results.NotFound(ApiResult.Fail("Game not found."));

            if (string.IsNullOrWhiteSpace(request.FilePath))
                return Results.BadRequest(ApiResult.Fail("Please choose a file."));
            if (!File.Exists(request.FilePath))
                return Results.BadRequest(ApiResult.Fail("The selected file does not exist."));

            var requestedKind = NormalizeUploadKind(request.Kind);
            if (requestedKind is null)
                return Results.BadRequest(ApiResult.Fail("Specify the upload kind with kind=ttf or kind=tmp."));

            var info = new FileInfo(request.FilePath);
            if (info.Length > 50 * 1024 * 1024)
                return Results.BadRequest(ApiResult.Fail("File size exceeds the 50 MB limit."));

            byte[] magic = new byte[4];
            await using (var peekStream = File.OpenRead(request.FilePath))
                await peekStream.ReadExactlyAsync(magic);

            var detectedKind = IsTtfOrOtf(magic) ? "TTF" : "TMP";
            if (!string.Equals(requestedKind, detectedKind, StringComparison.Ordinal))
                return Results.BadRequest(ApiResult.Fail("The uploaded file type does not match the selected kind."));

            var destinationPath = fontReplacementService.GetUniqueCustomSourcePath(
                id,
                requestedKind,
                Path.GetFileName(request.FilePath));
            File.Copy(request.FilePath, destinationPath, overwrite: false);

            if (detectedKind == "TMP" && !fontReplacementService.ValidateCustomFont(destinationPath))
            {
                File.Delete(destinationPath);
                return Results.BadRequest(ApiResult.Fail("Invalid TMP font bundle: TMP_FontAsset was not found."));
            }

            return Results.Ok(ApiResult.Ok());
        });

        group.MapDelete("/custom-fonts/{sourceId}", (string id, string sourceId,
            FontReplacementService fontReplacementService) =>
        {
            if (!Guid.TryParse(id, out _))
                return Results.BadRequest(ApiResult.Fail("Invalid game ID"));

            if (sourceId.Equals("tmp-default", StringComparison.OrdinalIgnoreCase)
                || sourceId.Equals("ttf-default", StringComparison.OrdinalIgnoreCase))
            {
                return Results.BadRequest(ApiResult.Fail("Default font sources cannot be deleted."));
            }

            if (!fontReplacementService.TryResolveCustomSourcePath(id, sourceId, out var path, out var error))
                return Results.BadRequest(ApiResult.Fail(error ?? "Custom font source was not found."));

            File.Delete(path!);
            return Results.Ok(ApiResult.Ok());
        });

        group.MapPost("/cancel", (string id) =>
        {
            if (_cancellationTokens.TryGetValue(id, out var cts))
                cts.Cancel();
            return Results.Ok(ApiResult.Ok());
        });
    }
}
