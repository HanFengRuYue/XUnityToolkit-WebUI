using XUnityToolkit_WebUI.Models;
using XUnityToolkit_WebUI.Services;

namespace XUnityToolkit_WebUI.Endpoints;

public static class TranslationEditorEndpoints
{
    public static void MapTranslationEditorEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/games/{id}/translation-editor");

        group.MapGet("/", async (
            string id,
            GameLibraryService library,
            ConfigurationService configService,
            CancellationToken ct) =>
        {
            var game = await library.GetByIdAsync(id, ct);
            if (game is null)
                return Results.NotFound(ApiResult.Fail("游戏不存在"));

            var config = await configService.GetAsync(game.GamePath, ct);
            string fullPath;
            try
            {
                fullPath = TranslationEditorPathResolver.ResolveDefaultTranslationFilePath(
                    game.GamePath,
                    config.OutputFile,
                    config.TargetLanguage);
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(ApiResult.Fail(ex.Message));
            }

            var entries = new List<TranslationEntryDto>();
            if (File.Exists(fullPath))
            {
                var lines = await File.ReadAllLinesAsync(fullPath, ct);
                entries = XUnityTranslationFormat.ParseLines(lines);
            }

            return Results.Ok(ApiResult<TranslationEditorData>.Ok(new TranslationEditorData(
                Language: config.TargetLanguage,
                FilePath: Path.GetRelativePath(game.GamePath, fullPath),
                FileExists: File.Exists(fullPath),
                EntryCount: entries.Count,
                Entries: entries.Select(static entry =>
                    new TranslationEntryResponse(entry.Original, entry.Translation)).ToList())));
        });

        group.MapPut("/", async (
            string id,
            SaveTranslationsRequest request,
            GameLibraryService library,
            ConfigurationService configService,
            ILoggerFactory loggerFactory,
            CancellationToken ct) =>
        {
            var game = await library.GetByIdAsync(id, ct);
            if (game is null)
                return Results.NotFound(ApiResult.Fail("游戏不存在"));
            if (request.Entries is null)
                return Results.BadRequest(ApiResult.Fail("请求缺少 entries 字段"));

            var duplicates = request.Entries
                .GroupBy(static entry => entry.Original, StringComparer.Ordinal)
                .Where(static duplicateGroup => duplicateGroup.Count() > 1)
                .Select(static duplicateGroup => duplicateGroup.Key)
                .Take(3)
                .ToList();
            if (duplicates.Count > 0)
                return Results.BadRequest(ApiResult.Fail($"存在重复原文: {string.Join(", ", duplicates)}"));

            var config = await configService.GetAsync(game.GamePath, ct);
            string fullPath;
            try
            {
                fullPath = TranslationEditorPathResolver.ResolveDefaultTranslationFilePath(
                    game.GamePath,
                    config.OutputFile,
                    config.TargetLanguage);
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(ApiResult.Fail(ex.Message));
            }

            var content = XUnityTranslationFormat.SerializeEntries(request.Entries
                .Select(static entry => new TranslationEntryDto(entry.Original, entry.Translation))
                .ToList());
            await WriteTextAtomicAsync(fullPath, content, ct);

            var logger = loggerFactory.CreateLogger("TranslationEditor");
            logger.LogInformation("译文编辑器保存 {Count} 条, 游戏 {GameId}, Lang={Lang}",
                request.Entries.Count, id, config.TargetLanguage);
            return Results.Ok(ApiResult.Ok());
        });

        group.MapPost("/import", async (
            string id,
            ImportTranslationRequest request,
            GameLibraryService library,
            CancellationToken ct) =>
        {
            var game = await library.GetByIdAsync(id, ct);
            if (game is null)
                return Results.NotFound(ApiResult.Fail("游戏不存在"));
            if (string.IsNullOrWhiteSpace(request.Content))
                return Results.BadRequest(ApiResult.Fail("导入内容为空"));

            var lines = request.Content.Split(["\r\n", "\n", "\r"], StringSplitOptions.None);
            var entries = XUnityTranslationFormat.ParseLines(lines);
            return Results.Ok(ApiResult<List<TranslationEntryResponse>>.Ok(
                entries.Select(static entry =>
                    new TranslationEntryResponse(entry.Original, entry.Translation)).ToList()));
        });

        group.MapGet("/export", async (
            string id,
            GameLibraryService library,
            ConfigurationService configService,
            CancellationToken ct) =>
        {
            var game = await library.GetByIdAsync(id, ct);
            if (game is null)
                return Results.NotFound(ApiResult.Fail("游戏不存在"));

            var config = await configService.GetAsync(game.GamePath, ct);
            string fullPath;
            try
            {
                fullPath = TranslationEditorPathResolver.ResolveDefaultTranslationFilePath(
                    game.GamePath,
                    config.OutputFile,
                    config.TargetLanguage);
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(ApiResult.Fail(ex.Message));
            }

            if (!File.Exists(fullPath))
                return Results.NotFound(ApiResult.Fail("译文文件不存在"));

            var bytes = await File.ReadAllBytesAsync(fullPath, ct);
            var fileName = $"{SanitizeFileName(game.Name)}_Translations_{DateTime.Now:yyyy-MM-dd}.txt";
            return Results.File(bytes, "text/plain; charset=utf-8", fileDownloadName: fileName);
        });
    }

    private static async Task WriteTextAtomicAsync(string filePath, string content, CancellationToken ct)
    {
        var directory = Path.GetDirectoryName(filePath)!;
        Directory.CreateDirectory(directory);
        var tempPath = filePath + ".tmp";
        await File.WriteAllTextAsync(tempPath, content, ct);
        File.Move(tempPath, filePath, overwrite: true);
    }

    private static string SanitizeFileName(string value)
        => new(value.Where(static character => !Path.GetInvalidFileNameChars().Contains(character)).ToArray());
}

public record TranslationEditorData(
    string Language,
    string FilePath,
    bool FileExists,
    int EntryCount,
    List<TranslationEntryResponse> Entries);

public record TranslationEntryResponse(string Original, string Translation);

public record SaveTranslationsRequest(List<TranslationEntryResponse> Entries);

public record ImportTranslationRequest(string Content);
