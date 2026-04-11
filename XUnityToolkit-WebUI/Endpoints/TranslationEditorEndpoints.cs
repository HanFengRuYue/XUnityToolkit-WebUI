using System.Text.RegularExpressions;
using XUnityToolkit_WebUI.Infrastructure;
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
            string? source,
            string? lang,
            GameLibraryService library,
            ConfigurationService configService,
            CancellationToken ct) =>
        {
            var game = await library.GetByIdAsync(id, ct);
            if (game is null)
                return Results.NotFound(ApiResult.Fail("游戏不存在"));

            var config = await configService.GetAsync(game.GamePath, ct);
            var normalizedSource = NormalizeTextSource(source);
            if (normalizedSource is null)
                return Results.BadRequest(ApiResult.Fail("不支持的译文来源"));

            TextEditorTarget target;
            try
            {
                target = ResolveTextEditorTarget(game, config, normalizedSource, lang);
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(ApiResult.Fail(ex.Message));
            }

            var entries = new List<TranslationEntryDto>();
            if (File.Exists(target.FullPath))
            {
                var lines = await File.ReadAllLinesAsync(target.FullPath, ct);
                entries = XUnityTranslationFormat.ParseLines(lines);
            }

            return Results.Ok(ApiResult<TranslationEditorData>.Ok(new TranslationEditorData(
                Source: target.Source,
                Language: target.Language,
                FilePath: Path.GetRelativePath(game.GamePath, target.FullPath),
                FileExists: File.Exists(target.FullPath),
                EntryCount: entries.Count,
                AvailablePreTranslationLanguages: target.AvailableLanguages,
                Entries: entries.Select(static entry => new TranslationEntryResponse(entry.Original, entry.Translation)).ToList())));
        });

        group.MapPut("/", async (
            string id,
            string? source,
            string? lang,
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
                .Where(static group => group.Count() > 1)
                .Select(static group => group.Key)
                .Take(3)
                .ToList();
            if (duplicates.Count > 0)
                return Results.BadRequest(ApiResult.Fail($"存在重复原文: {string.Join(", ", duplicates)}"));

            var config = await configService.GetAsync(game.GamePath, ct);
            var normalizedSource = NormalizeTextSource(source);
            if (normalizedSource is null)
                return Results.BadRequest(ApiResult.Fail("不支持的译文来源"));

            TextEditorTarget target;
            try
            {
                target = ResolveTextEditorTarget(game, config, normalizedSource, lang);
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(ApiResult.Fail(ex.Message));
            }

            var content = XUnityTranslationFormat.SerializeEntries(request.Entries
                .Select(static entry => new TranslationEntryDto(entry.Original, entry.Translation))
                .ToList());

            await WriteTextAtomicAsync(target.FullPath, content, ct);

            var logger = loggerFactory.CreateLogger("TranslationEditor");
            logger.LogInformation("译文编辑器保存 {Count} 条, 游戏 {GameId}, Source={Source}, Lang={Lang}",
                request.Entries.Count, id, target.Source, target.Language);
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
                entries.Select(static entry => new TranslationEntryResponse(entry.Original, entry.Translation)).ToList()));
        });

        group.MapGet("/export", async (
            string id,
            string? source,
            string? lang,
            GameLibraryService library,
            ConfigurationService configService,
            CancellationToken ct) =>
        {
            var game = await library.GetByIdAsync(id, ct);
            if (game is null)
                return Results.NotFound(ApiResult.Fail("游戏不存在"));

            var config = await configService.GetAsync(game.GamePath, ct);
            var normalizedSource = NormalizeTextSource(source);
            if (normalizedSource is null)
                return Results.BadRequest(ApiResult.Fail("不支持的译文来源"));

            TextEditorTarget target;
            try
            {
                target = ResolveTextEditorTarget(game, config, normalizedSource, lang);
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(ApiResult.Fail(ex.Message));
            }

            if (!File.Exists(target.FullPath))
                return Results.NotFound(ApiResult.Fail("译文文件不存在"));

            var fileName = normalizedSource == "pretranslated"
                ? $"{SanitizeFileName(game.Name)}_PreTranslated_{target.Language}_{DateTime.Now:yyyy-MM-dd}.txt"
                : $"{SanitizeFileName(game.Name)}_Translations_{DateTime.Now:yyyy-MM-dd}.txt";
            var bytes = await File.ReadAllBytesAsync(target.FullPath, ct);
            return Results.File(bytes, "text/plain; charset=utf-8", fileDownloadName: fileName);
        });

        group.MapGet("/regex", async (
            string id,
            string? lang,
            GameLibraryService library,
            ConfigurationService configService,
            CancellationToken ct) =>
        {
            var game = await library.GetByIdAsync(id, ct);
            if (game is null)
                return Results.NotFound(ApiResult.Fail("游戏不存在"));

            var config = await configService.GetAsync(game.GamePath, ct);
            RegexEditorTarget target;
            try
            {
                target = ResolveRegexEditorTarget(game, config, lang);
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(ApiResult.Fail(ex.Message));
            }

            var content = File.Exists(target.FullPath)
                ? await File.ReadAllTextAsync(target.FullPath, ct)
                : string.Empty;
            var parsed = PreTranslationRegexFormat.Parse(content);

            return Results.Ok(ApiResult<TranslationRegexEditorData>.Ok(new TranslationRegexEditorData(
                Language: target.Language,
                FilePath: Path.GetRelativePath(game.GamePath, target.FullPath),
                FileExists: File.Exists(target.FullPath),
                AvailablePreTranslationLanguages: target.AvailableLanguages,
                Rules: parsed.Rules.Select(static rule => new RegexTranslationRuleResponse(
                    rule.Id,
                    rule.Section,
                    rule.Kind,
                    rule.Pattern,
                    rule.Replacement)).ToList())));
        });

        group.MapPut("/regex", async (
            string id,
            string? lang,
            SaveRegexRulesRequest request,
            GameLibraryService library,
            ConfigurationService configService,
            AppDataPaths appDataPaths,
            ILoggerFactory loggerFactory,
            CancellationToken ct) =>
        {
            var game = await library.GetByIdAsync(id, ct);
            if (game is null)
                return Results.NotFound(ApiResult.Fail("游戏不存在"));
            if (request.Rules is null)
                return Results.BadRequest(ApiResult.Fail("请求缺少 rules 字段"));

            var config = await configService.GetAsync(game.GamePath, ct);
            RegexEditorTarget target;
            try
            {
                target = ResolveRegexEditorTarget(game, config, lang);
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(ApiResult.Fail(ex.Message));
            }

            List<PreTranslationRegexRule> rules;
            try
            {
                rules = ValidateRegexRules(request.Rules);
            }
            catch (FormatException ex)
            {
                return Results.BadRequest(ApiResult.Fail(ex.Message));
            }

            var content = PreTranslationRegexFormat.SerializeManaged(rules);
            await WriteTextAtomicAsync(target.FullPath, content, ct);
            await SyncLegacyCustomRegexFileAsync(appDataPaths, id, rules, ct);

            var logger = loggerFactory.CreateLogger("TranslationRegexEditor");
            logger.LogInformation("预翻译正则保存 {Count} 条, 游戏 {GameId}, Lang={Lang}", rules.Count, id, target.Language);
            return Results.Ok(ApiResult.Ok());
        });

        group.MapPost("/regex/import", async (
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

            List<PreTranslationRegexRule> rules;
            try
            {
                rules = PreTranslationRegexFormat.Parse(request.Content).Rules.ToList();
                if (rules.Count == 0)
                    rules = PreTranslationRegexFormat.ParseRulesStrict(request.Content);

                _ = ValidateRegexRules(rules.Select(static rule => new RegexTranslationRuleRequest(
                    rule.Id,
                    rule.Section,
                    rule.Kind,
                    rule.Pattern,
                    rule.Replacement)).ToList());
            }
            catch (FormatException ex)
            {
                return Results.BadRequest(ApiResult.Fail(ex.Message));
            }

            return Results.Ok(ApiResult<List<RegexTranslationRuleResponse>>.Ok(
                rules.Select(static rule => new RegexTranslationRuleResponse(
                    rule.Id,
                    rule.Section,
                    rule.Kind,
                    rule.Pattern,
                    rule.Replacement)).ToList()));
        });

        group.MapGet("/regex/export", async (
            string id,
            string? lang,
            GameLibraryService library,
            ConfigurationService configService,
            CancellationToken ct) =>
        {
            var game = await library.GetByIdAsync(id, ct);
            if (game is null)
                return Results.NotFound(ApiResult.Fail("游戏不存在"));

            var config = await configService.GetAsync(game.GamePath, ct);
            RegexEditorTarget target;
            try
            {
                target = ResolveRegexEditorTarget(game, config, lang);
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(ApiResult.Fail(ex.Message));
            }

            if (!File.Exists(target.FullPath))
                return Results.NotFound(ApiResult.Fail("预翻译正则文件不存在"));

            var bytes = await File.ReadAllBytesAsync(target.FullPath, ct);
            var fileName = $"{SanitizeFileName(game.Name)}_PreTranslatedRegex_{target.Language}_{DateTime.Now:yyyy-MM-dd}.txt";
            return Results.File(bytes, "text/plain; charset=utf-8", fileDownloadName: fileName);
        });
    }

    private static string? NormalizeTextSource(string? source)
    {
        return source switch
        {
            null or "" => "default",
            "default" => "default",
            "pretranslated" => "pretranslated",
            _ => null
        };
    }

    private static TextEditorTarget ResolveTextEditorTarget(Game game, XUnityConfig config, string source, string? lang)
    {
        if (source == "default")
        {
            var filePath = TranslationEditorPathResolver.ResolveDefaultTranslationFilePath(
                game.GamePath,
                config.OutputFile,
                config.TargetLanguage);

            return new TextEditorTarget(
                Source: source,
                Language: config.TargetLanguage,
                FullPath: filePath,
                AvailableLanguages: TranslationEditorPathResolver.ListAvailablePreTranslationLanguages(
                    game.GamePath,
                    "_PreTranslated.txt",
                    "_PreTranslated_Regex.txt"));
        }

        var language = TranslationEditorPathResolver.ResolvePreTranslationLanguage(
            game.GamePath,
            lang,
            config.TargetLanguage,
            "_PreTranslated.txt");
        var preTranslatedFilePath = TranslationEditorPathResolver.ResolvePreTranslatedTextFilePath(game.GamePath, language);
        return new TextEditorTarget(
            Source: source,
            Language: language,
            FullPath: preTranslatedFilePath,
            AvailableLanguages: TranslationEditorPathResolver.ListAvailablePreTranslationLanguages(
                game.GamePath,
                "_PreTranslated.txt"));
    }

    private static RegexEditorTarget ResolveRegexEditorTarget(Game game, XUnityConfig config, string? lang)
    {
        var language = TranslationEditorPathResolver.ResolvePreTranslationLanguage(
            game.GamePath,
            lang,
            config.TargetLanguage,
            "_PreTranslated_Regex.txt");
        var filePath = TranslationEditorPathResolver.ResolvePreTranslatedRegexFilePath(game.GamePath, language);
        return new RegexEditorTarget(
            Language: language,
            FullPath: filePath,
            AvailableLanguages: TranslationEditorPathResolver.ListAvailablePreTranslationLanguages(
                game.GamePath,
                "_PreTranslated_Regex.txt"));
    }

    private static List<PreTranslationRegexRule> ValidateRegexRules(IReadOnlyList<RegexTranslationRuleRequest> rules)
    {
        var validated = new List<PreTranslationRegexRule>(rules.Count);
        for (var index = 0; index < rules.Count; index++)
        {
            var rule = rules[index];
            if (string.IsNullOrWhiteSpace(rule.Pattern))
                throw new FormatException("正则表达式不能为空");
            if (false && string.IsNullOrWhiteSpace(rule.Replacement))
                throw new FormatException("替换内容不能为空");

            var normalizedSection = rule.Section switch
            {
                PreTranslationRegexFormat.BaseSection => PreTranslationRegexFormat.BaseSection,
                PreTranslationRegexFormat.CustomSection => PreTranslationRegexFormat.CustomSection,
                PreTranslationRegexFormat.DynamicSection => PreTranslationRegexFormat.DynamicSection,
                _ => throw new FormatException($"不支持的正则区块: {rule.Section}")
            };
            var normalizedKind = rule.Kind switch
            {
                "sr" => "sr",
                "r" => "r",
                _ => throw new FormatException($"不支持的正则类型: {rule.Kind}")
            };

            try
            {
                _ = new Regex(rule.Pattern, RegexOptions.None, TimeSpan.FromSeconds(1));
            }
            catch (RegexParseException ex)
            {
                throw new FormatException($"无效的正则表达式: {ex.Message}");
            }

            validated.Add(new PreTranslationRegexRule(
                Id: string.IsNullOrWhiteSpace(rule.Id) ? $"{normalizedSection}-{index}" : rule.Id,
                Section: normalizedSection,
                Kind: normalizedKind,
                Pattern: rule.Pattern,
                Replacement: rule.Replacement ?? string.Empty));
        }

        return validated;
    }

    private static async Task WriteTextAtomicAsync(string filePath, string content, CancellationToken ct)
    {
        var directory = Path.GetDirectoryName(filePath)!;
        Directory.CreateDirectory(directory);
        var tmpPath = filePath + ".tmp";
        await File.WriteAllTextAsync(tmpPath, content, ct);
        File.Move(tmpPath, filePath, overwrite: true);
    }

    private static async Task SyncLegacyCustomRegexFileAsync(
        AppDataPaths appDataPaths,
        string gameId,
        IEnumerable<PreTranslationRegexRule> rules,
        CancellationToken ct)
    {
        var customRules = rules.Where(static rule =>
            string.Equals(rule.Section, PreTranslationRegexFormat.CustomSection, StringComparison.Ordinal));
        var content = PreTranslationRegexFormat.SerializeRules(customRules);
        var filePath = appDataPaths.PreTranslationRegexFile(gameId);
        var directory = Path.GetDirectoryName(filePath)!;
        Directory.CreateDirectory(directory);
        await File.WriteAllTextAsync(filePath, content, ct);
    }

    private static string SanitizeFileName(string value)
    {
        return new string(value.Where(static c => !Path.GetInvalidFileNameChars().Contains(c)).ToArray());
    }

    private sealed record TextEditorTarget(
        string Source,
        string Language,
        string FullPath,
        IReadOnlyList<string> AvailableLanguages);

    private sealed record RegexEditorTarget(
        string Language,
        string FullPath,
        IReadOnlyList<string> AvailableLanguages);
}

public record TranslationEditorData(
    string Source,
    string Language,
    string FilePath,
    bool FileExists,
    int EntryCount,
    IReadOnlyList<string> AvailablePreTranslationLanguages,
    List<TranslationEntryResponse> Entries);

public record TranslationRegexEditorData(
    string Language,
    string FilePath,
    bool FileExists,
    IReadOnlyList<string> AvailablePreTranslationLanguages,
    List<RegexTranslationRuleResponse> Rules);

public record TranslationEntryResponse(string Original, string Translation);

public record RegexTranslationRuleResponse(
    string Id,
    string Section,
    string Kind,
    string Pattern,
    string Replacement);

public record SaveTranslationsRequest(List<TranslationEntryResponse> Entries);

public record SaveRegexRulesRequest(List<RegexTranslationRuleRequest> Rules);

public record RegexTranslationRuleRequest(
    string Id,
    string Section,
    string Kind,
    string Pattern,
    string Replacement);

public record ImportTranslationRequest(string Content);
