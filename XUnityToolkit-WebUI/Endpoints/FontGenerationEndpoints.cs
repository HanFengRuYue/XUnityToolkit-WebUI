using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using FreeTypeSharp;
using Microsoft.AspNetCore.SignalR;
using XUnityToolkit_WebUI.Hubs;
using XUnityToolkit_WebUI.Infrastructure;
using XUnityToolkit_WebUI.Models;
using XUnityToolkit_WebUI.Services;
using static FreeTypeSharp.FT;

namespace XUnityToolkit_WebUI.Endpoints;

public static class FontGenerationEndpoints
{
    public static void MapFontGenerationEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/font-generation");

        group.MapPost("/upload", async (HttpRequest request, AppDataPaths appDataPaths) =>
        {
            if (!request.HasFormContentType)
                return Results.BadRequest(ApiResult.Fail("请使用 multipart/form-data 格式上传"));

            var form = await request.ReadFormAsync();
            var file = form.Files.FirstOrDefault();
            if (file is null || file.Length == 0)
                return Results.BadRequest(ApiResult.Fail("未找到上传文件"));

            if (file.Length > 50 * 1024 * 1024)
                return Results.BadRequest(ApiResult.Fail("文件大小不能超过 50MB"));

            var originalName = Path.GetFileName(file.FileName);
            var ext = Path.GetExtension(originalName).ToLowerInvariant();
            if (ext is not ".ttf" and not ".otf")
                return Results.BadRequest(ApiResult.Fail("仅支持 .ttf 和 .otf 格式"));

            var savedName = $"{Guid.NewGuid():N}{ext}";
            var savedPath = Path.Combine(appDataPaths.FontGenerationUploadsDirectory, savedName);

            await using (var stream = File.Create(savedPath))
                await file.CopyToAsync(stream);

            // Extract font family name
            var fontName = ExtractFontName(savedPath);

            return Results.Ok(ApiResult<FontUploadInfo>.Ok(new FontUploadInfo
            {
                FileName = savedName,
                FontName = fontName,
                FileSize = file.Length,
            }));
        }).DisableAntiforgery();

        group.MapPost("/generate", (FontGenerationStartRequest body,
            TmpFontGeneratorService generator, AppDataPaths appDataPaths,
            IHubContext<InstallProgressHub> hubContext,
            ILogger<TmpFontGeneratorService> logger) =>
        {
            var fontPath = Path.Combine(appDataPaths.FontGenerationUploadsDirectory, Path.GetFileName(body.FileName));
            if (!File.Exists(fontPath))
                return Results.NotFound(ApiResult.Fail("上传的字体文件不存在"));

            var request = new FontGenerationRequest(
                fontPath,
                body.UnityVersion,
                body.SamplingSize,
                body.AtlasWidth,
                body.AtlasHeight,
                body.CharacterSet,
                body.RenderMode,
                body.SamplingSizeMode,
                body.PaddingMode,
                body.PaddingValue
            );

            // Fire-and-forget
            _ = Task.Run(async () =>
            {
                try
                {
                    var result = await generator.GenerateAsync(request);
                    if (!result.Success)
                    {
                        await hubContext.Clients.Group("font-generation")
                            .SendAsync("FontGenerationComplete",
                                new FontGenerationComplete(false, null, 0, result.Error));
                    }
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "字体生成失败");
                    await hubContext.Clients.Group("font-generation")
                        .SendAsync("FontGenerationComplete",
                            new FontGenerationComplete(false, null, 0, "字体生成失败，请检查字体文件格式"));
                }
            }, CancellationToken.None);

            return Results.Ok(ApiResult.Ok());
        });

        group.MapGet("/status", (TmpFontGeneratorService generator) =>
            Results.Ok(ApiResult<FontGenerationStatus>.Ok(generator.GetStatus())));

        group.MapPost("/cancel", (TmpFontGeneratorService generator) =>
        {
            generator.Cancel();
            return Results.Ok(ApiResult.Ok());
        });

        group.MapGet("/download/{fileName}", (string fileName, AppDataPaths appDataPaths) =>
        {
            var safeName = Path.GetFileName(fileName);
            var path = Path.Combine(appDataPaths.GeneratedFontsDirectory, safeName);
            if (!File.Exists(path))
                return Results.NotFound();
            return Results.File(path, "application/octet-stream", safeName);
        });

        group.MapGet("/history", async (AppDataPaths appDataPaths) =>
        {
            var dir = appDataPaths.GeneratedFontsDirectory;
            if (!Directory.Exists(dir))
                return Results.Ok(ApiResult<List<GeneratedFontInfo>>.Ok([]));

            var bundleFiles = Directory.GetFiles(dir, "*.bundle");
            var infos = new List<GeneratedFontInfo>(bundleFiles.Length);

            foreach (var f in bundleFiles)
            {
                var fi = new FileInfo(f);
                var name = Path.GetFileNameWithoutExtension(fi.Name);
                // Parse font name from filename pattern: {fontName}_U{version}
                var lastU = name.LastIndexOf("_U", StringComparison.Ordinal);
                var fontName = lastU > 0 ? name[..lastU] : name;

                var glyphCount = 0;
                var hasReport = false;
                var reportPath = Path.ChangeExtension(f, ".report.json");
                if (File.Exists(reportPath))
                {
                    hasReport = true;
                    try
                    {
                        var reportJson = await File.ReadAllTextAsync(reportPath);
                        var report = JsonSerializer.Deserialize<FontGenerationReport>(reportJson);
                        if (report != null)
                            glyphCount = report.SuccessfulGlyphs;
                    }
                    catch
                    {
                        // Ignore malformed report files
                    }
                }

                infos.Add(new GeneratedFontInfo
                {
                    FileName = fi.Name,
                    FontName = fontName,
                    GlyphCount = glyphCount,
                    FileSize = fi.Length,
                    GeneratedAt = fi.CreationTimeUtc,
                    HasReport = hasReport,
                });
            }

            infos.Sort((a, b) => b.GeneratedAt.CompareTo(a.GeneratedAt));
            return Results.Ok(ApiResult<List<GeneratedFontInfo>>.Ok(infos));
        });

        group.MapDelete("/{fileName}", (string fileName, AppDataPaths appDataPaths) =>
        {
            var safeName = Path.GetFileName(fileName);
            var fullPath = Path.Combine(appDataPaths.GeneratedFontsDirectory, safeName);
            if (!File.Exists(fullPath))
                return Results.NotFound(ApiResult.Fail("文件不存在"));

            try
            {
                File.Delete(fullPath);

                var reportPath = Path.ChangeExtension(fullPath, ".report.json");
                if (File.Exists(reportPath))
                    File.Delete(reportPath);
            }
            catch (IOException)
            {
                return Results.Json(ApiResult.Fail("文件正在使用中，无法删除"), statusCode: 409);
            }

            return Results.Ok(ApiResult.Ok());
        });

        // GET /charsets — list available built-in charsets
        group.MapGet("/charsets", (CharacterSetService charsetService) =>
            Results.Ok(ApiResult<List<CharsetInfo>>.Ok(charsetService.GetBuiltinCharsets())));

        // POST /charset/preview — preview merged charset stats
        group.MapPost("/charset/preview", async (CharacterSetPreviewRequest body, CharacterSetService charsetService) =>
        {
            var (_, preview) = await charsetService.ResolveCharactersAsync(
                body.CharacterSet, body.AtlasWidth, body.AtlasHeight, body.SamplingSize);
            return Results.Ok(ApiResult<CharacterSetPreview>.Ok(preview));
        });

        // POST /charset/upload-custom — upload custom charset TXT file
        group.MapPost("/charset/upload-custom", async (HttpRequest request, AppDataPaths appDataPaths) =>
        {
            if (!request.HasFormContentType)
                return Results.BadRequest(ApiResult.Fail("请求必须是 multipart/form-data 格式"));

            var form = await request.ReadFormAsync();
            var file = form.Files.FirstOrDefault();
            if (file == null || file.Length == 0)
                return Results.BadRequest(ApiResult.Fail("请上传字符集文件"));

            if (file.Length > 10 * 1024 * 1024)
                return Results.BadRequest(ApiResult.Fail("字符集文件大小不能超过 10MB"));

            var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
            if (ext != ".txt")
                return Results.BadRequest(ApiResult.Fail("仅支持 .txt 格式"));

            var safeName = $"{Guid.NewGuid():N}{ext}";
            var savePath = Path.Combine(appDataPaths.FontGenerationCharsetUploadsDirectory, safeName);
            await using (var fs = new FileStream(savePath, FileMode.Create))
                await file.CopyToAsync(fs);

            // Count unique characters
            var text = await File.ReadAllTextAsync(savePath);
            var charCount = new HashSet<int>();
            var enumerator = System.Globalization.StringInfo.GetTextElementEnumerator(text);
            while (enumerator.MoveNext())
            {
                var cp = char.ConvertToUtf32(enumerator.GetTextElement(), 0);
                if (cp > 0xFFFF || !char.IsWhiteSpace((char)cp))
                    charCount.Add(cp);
            }

            return Results.Ok(ApiResult<object>.Ok(new { fileName = safeName, characterCount = charCount.Count }));
        }).DisableAntiforgery();

        // POST /charset/upload-translation — upload translation file
        group.MapPost("/charset/upload-translation", async (HttpRequest request, AppDataPaths appDataPaths) =>
        {
            if (!request.HasFormContentType)
                return Results.BadRequest(ApiResult.Fail("请求必须是 multipart/form-data 格式"));

            var form = await request.ReadFormAsync();
            var file = form.Files.FirstOrDefault();
            if (file == null || file.Length == 0)
                return Results.BadRequest(ApiResult.Fail("请上传翻译文件"));

            if (file.Length > 10 * 1024 * 1024)
                return Results.BadRequest(ApiResult.Fail("翻译文件大小不能超过 10MB"));

            var safeName = $"{Guid.NewGuid():N}.txt";
            var savePath = Path.Combine(appDataPaths.FontGenerationTranslationUploadsDirectory, safeName);
            await using (var fs = new FileStream(savePath, FileMode.Create))
                await file.CopyToAsync(fs);

            var charsetService = request.HttpContext.RequestServices.GetRequiredService<CharacterSetService>();
            var chars = charsetService.ExtractFromTranslationFile(savePath);

            return Results.Ok(ApiResult<object>.Ok(new { fileName = safeName, characterCount = chars.Count }));
        }).DisableAntiforgery();

        // GET /report/{fileName} — get historical generation report
        group.MapGet("/report/{fileName}", async (string fileName, AppDataPaths appDataPaths) =>
        {
            var safeName = Path.GetFileName(fileName);
            var reportPath = Path.Combine(appDataPaths.GeneratedFontsDirectory,
                Path.ChangeExtension(safeName, ".report.json"));

            if (!File.Exists(reportPath))
                return Results.NotFound(ApiResult.Fail("报告不存在"));

            var json = await File.ReadAllTextAsync(reportPath);
            var report = JsonSerializer.Deserialize<FontGenerationReport>(json);
            return Results.Ok(ApiResult<FontGenerationReport>.Ok(report!));
        });

        group.MapPost("/install-tmp-font/{gameId}", async (string gameId, UseAsCustomRequest body,
            AppDataPaths appDataPaths, GameLibraryService library, ConfigurationService configService,
            CancellationToken ct) =>
        {
            var safeName = Path.GetFileName(body.FileName);
            var srcPath = Path.Combine(appDataPaths.GeneratedFontsDirectory, safeName);
            if (!File.Exists(srcPath))
                return Results.NotFound(ApiResult.Fail("生成的字体文件不存在"));

            var game = await library.GetByIdAsync(gameId);
            if (game is null)
                return Results.NotFound(ApiResult.Fail("游戏不存在"));

            if (game.InstallState != InstallState.FullyInstalled)
                return Results.BadRequest(ApiResult.Fail("请先安装 BepInEx 和 XUnity.AutoTranslator。"));

            // Install to BepInEx/Font/ using the generated font's actual name (without .bundle extension)
            var destFileName = Path.GetFileNameWithoutExtension(safeName);
            var configValue = TmpFontService.InstallCustomFont(game.GamePath, srcPath, destFileName);

            // Patch config
            await configService.PatchSectionAsync(game.GamePath, "Behaviour",
                new Dictionary<string, string>
                {
                    ["FallbackFontTextMeshPro"] = configValue
                }, ct);

            return Results.Ok(ApiResult.Ok());
        });
    }

    private static unsafe string ExtractFontName(string fontPath)
    {
        FT_LibraryRec_* lib;
        FT_FaceRec_* face;

        if (FT_Init_FreeType(&lib) != FT_Error.FT_Err_Ok)
            return "Unknown";

        try
        {
            var pathBytes = Encoding.UTF8.GetBytes(fontPath + '\0');
            FT_Error error;
            fixed (byte* pathPtr = pathBytes)
            {
                error = FT_New_Face(lib, pathPtr, 0, &face);
            }
            if (error != FT_Error.FT_Err_Ok)
                return "Unknown";

            try
            {
                return Marshal.PtrToStringAnsi((IntPtr)face->family_name) ?? "Unknown";
            }
            finally
            {
                FT_Done_Face(face);
            }
        }
        finally
        {
            FT_Done_FreeType(lib);
        }
    }

    private record FontGenerationStartRequest(
        string FileName,
        string UnityVersion,
        int SamplingSize = 64,
        int AtlasWidth = 4096,
        int AtlasHeight = 4096,
        CharacterSetConfig? CharacterSet = null,
        string RenderMode = "SDFAA",
        string SamplingSizeMode = "manual",
        string PaddingMode = "percentage",
        int PaddingValue = 10
    );

    private record UseAsCustomRequest(string FileName);

    private record CharacterSetPreviewRequest(
        CharacterSetConfig CharacterSet,
        int AtlasWidth = 4096,
        int AtlasHeight = 4096,
        int SamplingSize = 64
    );
}
