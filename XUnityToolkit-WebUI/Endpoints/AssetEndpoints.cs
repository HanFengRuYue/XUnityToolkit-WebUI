using System.Text.Json;
using System.Text.RegularExpressions;
using XUnityToolkit_WebUI.Infrastructure;
using XUnityToolkit_WebUI.Models;
using XUnityToolkit_WebUI.Services;

namespace XUnityToolkit_WebUI.Endpoints;

public static class AssetEndpoints
{


    public static void MapAssetEndpoints(this WebApplication app)
    {
        // Extract game assets (MonoBehaviour strings + TextAsset)
        app.MapPost("/api/games/{id}/extract-assets", async (
            string id,
            GameLibraryService gameLibrary,
            AssetExtractionService extractionService,
            SystemTrayService trayService,
            AppDataPaths paths,
            ILogger<AssetExtractionService> logger,
            CancellationToken ct) =>
        {
            var game = await gameLibrary.GetByIdAsync(id, ct);
            if (game is null) return Results.NotFound(ApiResult.Fail("游戏不存在"));
            if (!game.IsUnityGame) return Results.BadRequest(ApiResult.Fail("非 Unity 游戏"));
            if (game.DetectedInfo is null) return Results.BadRequest(ApiResult.Fail("游戏未检测"));

            try
            {
                var result = await extractionService.ExtractTextsAsync(
                    game.GamePath, game.ExecutableName, game.DetectedInfo, ct: ct);

                result.GameId = id;

                // Cache the result (use temp-then-move for atomicity)
                var cachePath = paths.ExtractedTextsFile(id);
                Directory.CreateDirectory(Path.GetDirectoryName(cachePath)!);
                var json = JsonSerializer.Serialize(result, FileHelper.DataJsonOptions);
                var tmpPath = cachePath + ".tmp";
                await File.WriteAllTextAsync(tmpPath, json, CancellationToken.None);
                File.Move(tmpPath, cachePath, overwrite: true);

                logger.LogInformation("资产提取完成并缓存: {Count} 条文本, 语言={Lang}",
                    result.TotalTextsExtracted, result.DetectedLanguage);

                trayService.ShowNotification("资产提取完成",
                    $"「{game.Name}」提取到 {result.TotalTextsExtracted} 条文本");

                return Results.Ok(ApiResult<AssetExtractionResult>.Ok(result));
            }
            catch (DirectoryNotFoundException)
            {
                return Results.BadRequest(ApiResult.Fail("游戏资产目录不存在"));
            }
            catch (FileNotFoundException)
            {
                return Results.BadRequest(ApiResult.Fail("游戏资产文件不存在"));
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "资产提取失败: 游戏 {GameId}", id);
                return Results.Json(ApiResult.Fail("资产提取失败"), statusCode: 500);
            }
        });

        // Get cached extracted texts
        app.MapGet("/api/games/{id}/extracted-texts", async (
            string id,
            AppDataPaths paths,
            CancellationToken ct) =>
        {
            if (!Guid.TryParse(id, out _))
                return Results.BadRequest(ApiResult.Fail("Invalid game ID"));
            var cachePath = paths.ExtractedTextsFile(id);
            if (!File.Exists(cachePath))
                return Results.Ok(ApiResult<AssetExtractionResult?>.Ok(null));

            var json = await File.ReadAllTextAsync(cachePath, ct);
            var result = JsonSerializer.Deserialize<AssetExtractionResult>(json, FileHelper.DataJsonOptions);
            return Results.Ok(ApiResult<AssetExtractionResult?>.Ok(result));
        });

        // Delete cached extracted texts
        app.MapDelete("/api/games/{id}/extracted-texts", async (
            string id,
            AppDataPaths paths,
            PreTranslationService preTranslation) =>
        {
            if (!Guid.TryParse(id, out _))
                return Results.BadRequest(ApiResult.Fail("Invalid game ID"));
            var cachePath = paths.ExtractedTextsFile(id);
            if (File.Exists(cachePath))
                File.Delete(cachePath);
            await preTranslation.DeleteCheckpointAsync(id, clearInactiveStatus: true);
            return Results.Ok(ApiResult.Ok());
        });

        // Start pre-translation
        app.MapPost("/api/games/{id}/pre-translate", async (
            string id,
            PreTranslateRequest request,
            PreTranslationService preTranslation,
            AppSettingsService settingsService,
            AppDataPaths paths,
            ILogger<PreTranslationService> logger,
            CancellationToken ct) =>
        {
            if (!Guid.TryParse(id, out _))
                return Results.BadRequest(ApiResult.Fail("Invalid game ID"));
            // Pre-flight: check AI provider configuration
            var settings = await settingsService.GetAsync(ct);
            var ai = settings.AiTranslation;
            var hasProvider = ai.Endpoints.Any(e => e.Enabled && !string.IsNullOrWhiteSpace(e.ApiKey));
            if (!hasProvider)
            {
                logger.LogWarning("预翻译检查失败: 总端点数={Total}, 各端点状态=[{Details}]",
                    ai.Endpoints.Count,
                    string.Join(", ", ai.Endpoints.Select(e => $"{e.Name}(Enabled={e.Enabled}, HasKey={!string.IsNullOrWhiteSpace(e.ApiKey)})")));
                return Results.BadRequest(ApiResult.Fail("请先在 AI 翻译页面配置至少一个 AI 提供商"));
            }

            var cachePath = paths.ExtractedTextsFile(id);
            if (!File.Exists(cachePath))
                return Results.BadRequest(ApiResult.Fail("请先提取游戏资产"));

            var json = await File.ReadAllTextAsync(cachePath, ct);
            var extractResult = JsonSerializer.Deserialize<AssetExtractionResult>(json, FileHelper.DataJsonOptions);
            if (extractResult is null || extractResult.Texts.Count == 0)
                return Results.BadRequest(ApiResult.Fail("没有可翻译的文本"));

            var fromLang = request.FromLang ?? extractResult.DetectedLanguage ?? "ja";
            var toLang = request.ToLang ?? "zh";
            if (!Regex.IsMatch(fromLang, @"^[a-zA-Z0-9_\-]{1,20}$"))
                return Results.BadRequest(ApiResult.Fail("无效的源语言代码"));
            if (!Regex.IsMatch(toLang, @"^[a-zA-Z0-9_\-]{1,20}$"))
                return Results.BadRequest(ApiResult.Fail("无效的目标语言代码"));

            try
            {
                var status = await preTranslation.StartPreTranslationAsync(
                    id, extractResult.Texts, fromLang, toLang, request.Restart, ct);

                return Results.Ok(ApiResult<PreTranslationStatus>.Ok(status));
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(ApiResult.Fail(ex.Message));
            }
        });

        app.MapPost("/api/games/{id}/pre-translate/resume", async (
            string id,
            PreTranslationService preTranslation,
            AppDataPaths paths,
            CancellationToken ct) =>
        {
            if (!Guid.TryParse(id, out _))
                return Results.BadRequest(ApiResult.Fail("Invalid game ID"));

            var cachePath = paths.ExtractedTextsFile(id);
            if (!File.Exists(cachePath))
                return Results.BadRequest(ApiResult.Fail("请先提取游戏资产"));

            var json = await File.ReadAllTextAsync(cachePath, ct);
            var extractResult = JsonSerializer.Deserialize<AssetExtractionResult>(json, FileHelper.DataJsonOptions);
            if (extractResult is null || extractResult.Texts.Count == 0)
                return Results.BadRequest(ApiResult.Fail("没有可翻译的文本"));

            try
            {
                var status = await preTranslation.ResumePreTranslationAsync(id, extractResult.Texts, ct);
                return Results.Ok(ApiResult<PreTranslationStatus>.Ok(status));
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(ApiResult.Fail(ex.Message));
            }
        });

        // Get pre-translation status
        app.MapGet("/api/games/{id}/pre-translate/status", async (
            string id,
            PreTranslationService preTranslation,
            CancellationToken ct) =>
        {
            var status = await preTranslation.GetStatusAsync(id, ct);
            return Results.Ok(ApiResult<PreTranslationStatus>.Ok(status));
        });

        // Cancel pre-translation
        app.MapPost("/api/games/{id}/pre-translate/cancel", (
            string id,
            PreTranslationService preTranslation) =>
        {
            preTranslation.Cancel(id);
            return Results.Ok(ApiResult.Ok());
        });

        // Get custom regex patterns for pre-translation
        app.MapGet("/api/games/{id}/pre-translate/regex", async (
            string id,
            AppDataPaths paths,
            GameLibraryService library,
            ConfigurationService configService,
            CancellationToken ct) =>
        {
            if (!Guid.TryParse(id, out _))
                return Results.BadRequest(ApiResult.Fail("Invalid game ID"));

            var game = await library.GetByIdAsync(id, ct);
            if (game is null)
                return Results.NotFound(ApiResult.Fail("Game not found."));

            var config = await configService.GetAsync(game.GamePath, ct);
            var lang = TranslationEditorPathResolver.ResolvePreTranslationLanguage(
                game.GamePath,
                requestedLang: null,
                config.TargetLanguage,
                "_PreTranslated_Regex.txt");
            var finalFile = TranslationEditorPathResolver.ResolvePreTranslatedRegexFilePath(game.GamePath, lang);
            if (File.Exists(finalFile))
            {
                var managedContent = await File.ReadAllTextAsync(finalFile, ct);
                return Results.Ok(ApiResult<string>.Ok(PreTranslationRegexFormat.ExtractCustomContent(managedContent)));
            }

            var file = paths.PreTranslationRegexFile(id);
            if (!File.Exists(file))
                return Results.Ok(ApiResult<string>.Ok(""));
            var content = await File.ReadAllTextAsync(file);
            return Results.Ok(ApiResult<string>.Ok(content));
        });

        // Save custom regex patterns for pre-translation
        app.MapPut("/api/games/{id}/pre-translate/regex", async (
            string id,
            RegexPatternsRequest request,
            AppDataPaths paths,
            GameLibraryService library,
            ConfigurationService configService,
            CancellationToken ct) =>
        {
            // Legacy compatibility endpoint: only custom rules are editable here.
            if (!Guid.TryParse(id, out _))
                return Results.BadRequest(ApiResult.Fail("Invalid game ID"));
            var patternContent = request.Patterns ?? string.Empty;
            if (patternContent.Length > 512 * 1024)
                return Results.BadRequest(ApiResult.Fail("Regex pattern content cannot exceed 512 KB."));

            List<PreTranslationRegexRule> customRules;
            try
            {
                customRules = PreTranslationRegexFormat.ParseRulesStrict(
                    patternContent,
                    PreTranslationRegexFormat.CustomSection);
                foreach (var rule in customRules)
                    _ = new Regex(rule.Pattern, RegexOptions.None, TimeSpan.FromSeconds(1));
            }
            catch (FormatException ex)
            {
                return Results.BadRequest(ApiResult.Fail(ex.Message));
            }
            catch (RegexParseException ex)
            {
                return Results.BadRequest(ApiResult.Fail($"Invalid regex pattern: {ex.Message}"));
            }

            var game = await library.GetByIdAsync(id, ct);
            if (game is null)
                return Results.NotFound(ApiResult.Fail("Game not found."));

            var config = await configService.GetAsync(game.GamePath, ct);
            var lang = TranslationEditorPathResolver.ResolvePreTranslationLanguage(
                game.GamePath,
                requestedLang: null,
                config.TargetLanguage,
                "_PreTranslated_Regex.txt");
            var finalFile = TranslationEditorPathResolver.ResolvePreTranslatedRegexFilePath(game.GamePath, lang);
            var existingManagedContent = File.Exists(finalFile)
                ? await File.ReadAllTextAsync(finalFile, ct)
                : null;
            var mergedRules = PreTranslationRegexFormat.Parse(existingManagedContent).Rules
                .Where(static rule => !string.Equals(rule.Section, PreTranslationRegexFormat.CustomSection, StringComparison.Ordinal))
                .Concat(customRules)
                .ToList();

            await WriteTextAtomicAsync(finalFile, PreTranslationRegexFormat.SerializeManaged(mergedRules), ct);
            await WriteTextAtomicAsync(
                paths.PreTranslationRegexFile(id),
                PreTranslationRegexFormat.SerializeRules(customRules),
                ct);
            return Results.Ok(ApiResult.Ok());
            if ((request.Patterns ?? "").Length > 512 * 1024)
                return Results.BadRequest(ApiResult.Fail("正则模式内容不能超过 512 KB"));

            foreach (var line in (request.Patterns ?? "").Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                var trimmed = line.Trim();
                if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith("//")) continue;
                if (!trimmed.StartsWith("sr:") && !trimmed.StartsWith("r:"))
                    return Results.BadRequest(ApiResult.Fail("每行必须以 sr: 或 r: 开头，或为 // 注释"));
                if (!trimmed.Contains('='))
                    return Results.BadRequest(ApiResult.Fail($"模式缺少 = 分隔符: {trimmed}"));

                var patternStart = trimmed.IndexOf('"') + 1;
                var patternEnd = trimmed.IndexOf('"', patternStart);
                if (patternStart > 0 && patternEnd > patternStart)
                {
                    var pattern = trimmed[patternStart..patternEnd];
                    try { _ = new System.Text.RegularExpressions.Regex(pattern, default, TimeSpan.FromSeconds(1)); }
                    catch (System.Text.RegularExpressions.RegexParseException ex) { return Results.BadRequest(ApiResult.Fail($"无效的正则表达式: {ex.Message}")); }
                }
            }

            var file = paths.PreTranslationRegexFile(id);
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            var tmpFile = file + ".tmp";
            await File.WriteAllTextAsync(tmpFile, request.Patterns ?? "");
            File.Move(tmpFile, file, overwrite: true);
            return Results.Ok(ApiResult.Ok());
        });
    }

    private static async Task WriteTextAtomicAsync(string filePath, string content, CancellationToken ct)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
        var tmpFile = filePath + ".tmp";
        await File.WriteAllTextAsync(tmpFile, content, ct);
        File.Move(tmpFile, filePath, overwrite: true);
    }
}

public record PreTranslateRequest(string? FromLang, string? ToLang, bool Restart = false);
public record RegexPatternsRequest(string? Patterns);
