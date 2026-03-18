using XUnityToolkit_WebUI.Models;
using XUnityToolkit_WebUI.Services;

namespace XUnityToolkit_WebUI.Endpoints;

public static class TranslationEditorEndpoints
{
    public static void MapTranslationEditorEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/games/{id}/translation-editor");

        // Get all translation entries
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
            string filePath;
            try
            {
                filePath = ResolveTranslationFilePath(game.GamePath, config.OutputFile, config.TargetLanguage);
            }
            catch (InvalidOperationException)
            {
                return Results.BadRequest(ApiResult.Fail("翻译文件路径不安全"));
            }
            var exists = File.Exists(filePath);

            var entries = new List<TranslationEntryDto>();
            if (exists)
            {
                var lines = await File.ReadAllLinesAsync(filePath, ct);
                entries = XUnityTranslationFormat.ParseLines(lines);
            }

            // Return relative path (from game directory) to avoid exposing server filesystem
            var relativePath = Path.GetRelativePath(game.GamePath, filePath);
            return Results.Ok(ApiResult<TranslationEditorData>.Ok(
                new TranslationEditorData(relativePath, exists, entries.Count,
                    entries.Select(e => new TranslationEntryResponse(e.Original, e.Translation)).ToList())));
        });

        // Save all translation entries (full replace)
        group.MapPut("/", async (
            string id,
            SaveTranslationsRequest request,
            GameLibraryService library,
            ConfigurationService configService,
            ILogger<GameLibraryService> logger,
            CancellationToken ct) =>
        {
            var game = await library.GetByIdAsync(id, ct);
            if (game is null)
                return Results.NotFound(ApiResult.Fail("游戏不存在"));

            // Client-side should validate, but double-check for duplicate originals
            var duplicates = request.Entries
                .GroupBy(e => e.Original, StringComparer.Ordinal)
                .Where(g => g.Count() > 1)
                .Select(g => g.Key)
                .Take(3)
                .ToList();
            if (duplicates.Count > 0)
                return Results.BadRequest(ApiResult.Fail($"重复的原文条目: {string.Join(", ", duplicates)}"));

            var config = await configService.GetAsync(game.GamePath, ct);
            string filePath;
            try
            {
                filePath = ResolveTranslationFilePath(game.GamePath, config.OutputFile, config.TargetLanguage);
            }
            catch (InvalidOperationException)
            {
                return Results.BadRequest(ApiResult.Fail("翻译文件路径不安全"));
            }

            var dtos = request.Entries
                .Select(e => new TranslationEntryDto(e.Original, e.Translation))
                .ToList();

            var content = XUnityTranslationFormat.SerializeEntries(dtos);

            // Atomic write: .tmp → rename
            var dir = Path.GetDirectoryName(filePath)!;
            Directory.CreateDirectory(dir);
            var tmpPath = filePath + ".tmp";
            await File.WriteAllTextAsync(tmpPath, content, ct);
            File.Move(tmpPath, filePath, overwrite: true);

            logger.LogInformation("译文编辑器保存: {Count} 条, 游戏 {GameId}", dtos.Count, id);
            return Results.Ok(ApiResult.Ok());
        });

        // Parse imported file content and return entries (frontend merges with its state)
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
                entries.Select(e => new TranslationEntryResponse(e.Original, e.Translation)).ToList()));
        });

        // Export translation file as download
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
            string filePath;
            try
            {
                filePath = ResolveTranslationFilePath(game.GamePath, config.OutputFile, config.TargetLanguage);
            }
            catch (InvalidOperationException)
            {
                return Results.BadRequest(ApiResult.Fail("翻译文件路径不安全"));
            }

            if (!File.Exists(filePath))
                return Results.NotFound(ApiResult.Fail("翻译文件不存在"));

            var date = DateTime.Now.ToString("yyyy-MM-dd");
            var safeName = new string(game.Name.Where(c => !Path.GetInvalidFileNameChars().Contains(c)).ToArray());
            var fileName = $"{safeName}_Translations_{date}.txt";

            var bytes = await File.ReadAllBytesAsync(filePath, ct);
            return Results.File(bytes, "text/plain; charset=utf-8", fileDownloadName: fileName);
        });
    }

    private static string ResolveTranslationFilePath(string gamePath, string? outputFile, string targetLang)
    {
        var template = string.IsNullOrWhiteSpace(outputFile)
            ? @"Translation\{Lang}\Text\_AutoGeneratedTranslations.txt"
            : outputFile;
        var relative = template.Replace("{Lang}", targetLang);
        var fullPath = Path.GetFullPath(Path.Combine(gamePath, "BepInEx", relative));

        // Guard against path traversal via crafted OutputFile or TargetLanguage in INI
        var allowedBase = Path.GetFullPath(Path.Combine(gamePath, "BepInEx")) + Path.DirectorySeparatorChar;
        if (!fullPath.StartsWith(allowedBase, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("翻译文件路径超出游戏目录范围");

        return fullPath;
    }
}

// Request/response records
public record TranslationEditorData(
    string FilePath,
    bool FileExists,
    int EntryCount,
    List<TranslationEntryResponse> Entries);

public record TranslationEntryResponse(string Original, string Translation);

public record SaveTranslationsRequest(List<TranslationEntryResponse> Entries);

public record ImportTranslationRequest(string Content);
