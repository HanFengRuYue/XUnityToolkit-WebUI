using System.Collections.Concurrent;
using Microsoft.AspNetCore.SignalR;
using UnityLocalizationToolkit_WebUI.Hubs;
using UnityLocalizationToolkit_WebUI.Infrastructure;
using UnityLocalizationToolkit_WebUI.Models;
using UnityLocalizationToolkit_WebUI.Services;

namespace UnityLocalizationToolkit_WebUI.Endpoints;

public static class FontReplacementEndpoints
{
    private static readonly ConcurrentDictionary<string, CancellationTokenSource> _cancellationTokens = new();
    private sealed record FontUploadFromPathRequest(string FilePath, string Kind);

    private static Task BroadcastFontReplacementProgressAsync(
        IHubContext<InstallProgressHub> hubContext,
        string gameId,
        FontReplacementProgress progress) =>
        hubContext.Clients.Group($"font-replacement-{gameId}")
            .SendAsync("fontReplacementProgress", progress, CancellationToken.None);

    private static bool IsTerminalPhase(string? phase) =>
        string.Equals(phase, "completed", StringComparison.OrdinalIgnoreCase)
        || string.Equals(phase, "failed", StringComparison.OrdinalIgnoreCase)
        || string.Equals(phase, "cancelled", StringComparison.OrdinalIgnoreCase);

    private static FontReplacementProgress BuildTerminalProgress(
        FontReplacementProgress? lastProgress,
        string phase,
        int fallbackTotal,
        string? message = null) =>
        new()
        {
            Phase = phase,
            Current = string.Equals(phase, "completed", StringComparison.OrdinalIgnoreCase)
                ? fallbackTotal
                : lastProgress?.Current ?? 0,
            Total = lastProgress?.Total is > 0 ? lastProgress.Total : fallbackTotal,
            CurrentFile = lastProgress?.CurrentFile,
            Message = message
        };

    private static string GetUserFacingErrorMessage(Exception ex) => ex switch
    {
        OperationCanceledException => "字体替换已取消",
        InvalidOperationException => ex.Message,
        FileNotFoundException => "字体替换失败，所需资源文件不存在。",
        DirectoryNotFoundException => "字体替换失败，游戏数据目录不存在。",
        _ => "字体替换失败，请查看日志。"
    };

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
            AppDataPaths paths, IHubContext<InstallProgressHub> hubContext,
            ILoggerFactory loggerFactory, CancellationToken ct) =>
        {
            var logger = loggerFactory.CreateLogger("FontReplacementEndpoints");
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

            FontReplacementProgress? lastProgress = null;
            try
            {
                var progress = new Progress<FontReplacementProgress>(p =>
                {
                    lastProgress = p;
                    _ = BroadcastFontReplacementProgressAsync(hubContext, id, p);
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

                if (!IsTerminalPhase(lastProgress?.Phase))
                {
                    await BroadcastFontReplacementProgressAsync(
                        hubContext,
                        id,
                        BuildTerminalProgress(lastProgress, "completed", request.Fonts.Length));
                }

                return Results.Ok(ApiResult<FontReplacementResult>.Ok(result));
            }
            catch (OperationCanceledException)
            {
                await BroadcastFontReplacementProgressAsync(
                    hubContext,
                    id,
                    BuildTerminalProgress(lastProgress, phase: "cancelled", fallbackTotal: request.Fonts.Length,
                        message: "字体替换已取消"));
                return Results.BadRequest(ApiResult.Fail("字体替换已取消"));
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "字体替换失败: GameId={GameId}", id);
                var message = GetUserFacingErrorMessage(ex);
                await BroadcastFontReplacementProgressAsync(
                    hubContext,
                    id,
                    BuildTerminalProgress(lastProgress, phase: "failed", fallbackTotal: request.Fonts.Length,
                        message: message));
                return Results.BadRequest(ApiResult.Fail(message));
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
