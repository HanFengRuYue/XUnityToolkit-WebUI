using System.Runtime.InteropServices;
using System.Text;
using FreeTypeSharp;
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
        });

        group.MapPost("/generate", (FontGenerationStartRequest body,
            TmpFontGeneratorService generator, AppDataPaths appDataPaths) =>
        {
            var fontPath = Path.Combine(appDataPaths.FontGenerationUploadsDirectory, body.FileName);
            if (!File.Exists(fontPath))
                return Results.NotFound(ApiResult.Fail("上传的字体文件不存在"));

            var request = new FontGenerationRequest(
                fontPath,
                body.UnityVersion,
                body.SamplingSize,
                body.AtlasWidth,
                body.AtlasHeight
            );

            // Fire-and-forget
            _ = Task.Run(async () =>
            {
                var result = await generator.GenerateAsync(request);
                if (!result.Success)
                {
                    // Error already broadcast via SignalR in GenerateAsync
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

        group.MapGet("/history", (AppDataPaths appDataPaths) =>
        {
            var dir = appDataPaths.GeneratedFontsDirectory;
            if (!Directory.Exists(dir))
                return Results.Ok(ApiResult<List<GeneratedFontInfo>>.Ok([]));

            var files = Directory.GetFiles(dir, "*.bundle")
                .Select(f =>
                {
                    var fi = new FileInfo(f);
                    var name = Path.GetFileNameWithoutExtension(fi.Name);
                    // Parse font name from filename pattern: {fontName}_U{version}
                    var lastU = name.LastIndexOf("_U", StringComparison.Ordinal);
                    var fontName = lastU > 0 ? name[..lastU] : name;

                    return new GeneratedFontInfo
                    {
                        FileName = fi.Name,
                        FontName = fontName,
                        GlyphCount = 0, // Not stored in filename
                        FileSize = fi.Length,
                        GeneratedAt = fi.CreationTimeUtc,
                    };
                })
                .OrderByDescending(f => f.GeneratedAt)
                .ToList();

            return Results.Ok(ApiResult<List<GeneratedFontInfo>>.Ok(files));
        });

        group.MapDelete("/{fileName}", (string fileName, AppDataPaths appDataPaths) =>
        {
            var safeName = Path.GetFileName(fileName);
            var path = Path.Combine(appDataPaths.GeneratedFontsDirectory, safeName);
            if (!File.Exists(path))
                return Results.NotFound(ApiResult.Fail("文件不存在"));

            File.Delete(path);
            return Results.Ok(ApiResult.Ok());
        });

        group.MapPost("/use-as-custom/{gameId}", (string gameId, UseAsCustomRequest body,
            AppDataPaths appDataPaths, GameLibraryService library) =>
        {
            var safeName = Path.GetFileName(body.FileName);
            var srcPath = Path.Combine(appDataPaths.GeneratedFontsDirectory, safeName);
            if (!File.Exists(srcPath))
                return Results.NotFound(ApiResult.Fail("生成的字体文件不存在"));

            var dstDir = appDataPaths.GetCustomFontDirectory(gameId);
            Directory.CreateDirectory(dstDir);

            // Clear existing custom fonts
            foreach (var existing in Directory.GetFiles(dstDir))
                File.Delete(existing);

            File.Copy(srcPath, Path.Combine(dstDir, safeName));
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
        int AtlasHeight = 4096
    );

    private record UseAsCustomRequest(string FileName);
}
