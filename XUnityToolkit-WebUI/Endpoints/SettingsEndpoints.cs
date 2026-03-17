using System.IO.Compression;
using System.Reflection;
using System.Text.Json;
using XUnityToolkit_WebUI.Infrastructure;
using XUnityToolkit_WebUI.Models;
using XUnityToolkit_WebUI.Services;

namespace XUnityToolkit_WebUI.Endpoints;

public static class SettingsEndpoints
{
    public static void MapSettingsEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/settings");

        group.MapGet("/", async (AppSettingsService settingsService) =>
        {
            var settings = await settingsService.GetAsync();
            return Results.Ok(ApiResult<AppSettings>.Ok(settings));
        });

        group.MapPut("/", async (AppSettings settings, AppSettingsService settingsService) =>
        {
            // Server-side validation: clamp values to valid ranges
            settings.AiTranslation.MaxConcurrency = Math.Clamp(settings.AiTranslation.MaxConcurrency, 1, 100);
            settings.AiTranslation.Port = Math.Clamp(settings.AiTranslation.Port, 1, 65535);
            settings.AiTranslation.ContextSize = Math.Clamp(settings.AiTranslation.ContextSize, 0, 100);
            settings.AiTranslation.LocalContextSize = Math.Clamp(settings.AiTranslation.LocalContextSize, 0, 10);
            settings.AiTranslation.Temperature = Math.Clamp(settings.AiTranslation.Temperature, 0.0, 2.0);

            var saved = await settingsService.SaveAsync(settings);
            return Results.Ok(ApiResult<AppSettings>.Ok(saved));
        });

        group.MapPost("/reset", (
            AppDataPaths paths,
            AppSettingsService settingsService,
            TermService termService,
            ScriptTagService scriptTagService,
            FileLoggerProvider fileLoggerProvider,
            ILogger<AppSettingsService> logger) =>
        {
            // Invalidate all in-memory caches
            settingsService.InvalidateCache();
            termService.ClearAllCache();
            scriptTagService.ClearAllCache();

            var errors = new List<string>();

            // Suspend file logging so the log file handle is released,
            // allowing the entire data directory to be deleted cleanly.
            fileLoggerProvider.SuspendFileLog();
            try
            {
                TryDelete(() =>
                {
                    if (Directory.Exists(paths.Root))
                        Directory.Delete(paths.Root, recursive: true);
                }, paths.Root, errors);
            }
            finally
            {
                // Recreate directories and reopen log file
                paths.EnsureDirectoriesExist();
                fileLoggerProvider.ResumeFileLog();
            }

            if (errors.Count > 0)
            {
                logger.LogWarning("重置配置时部分操作失败: {Errors}", string.Join(", ", errors));
                return Results.Ok(ApiResult<object>.Ok(new { partial = true, errors }));
            }

            logger.LogInformation("已重置所有配置和缓存（已删除数据目录 {Root}）", paths.Root);
            return Results.Ok(ApiResult<object>.Ok(new { partial = false }));
        });

        group.MapGet("/version", () =>
        {
            var asm = Assembly.GetExecutingAssembly();
            var version = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()
                ?.InformationalVersion
                ?? asm.GetName().Version?.ToString()
                ?? "1.0.0";
            return Results.Ok(ApiResult<VersionInfo>.Ok(new VersionInfo(version)));
        });

        group.MapGet("/data-path", (AppDataPaths paths) =>
        {
            return Results.Ok(ApiResult<DataPathInfo>.Ok(new DataPathInfo(paths.Root)));
        });

        group.MapPost("/open-data-folder", (AppDataPaths paths) =>
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = paths.Root,
                    UseShellExecute = true,
                });
                return Results.Ok(ApiResult.Ok());
            }
            catch
            {
                return Results.Ok(ApiResult.Fail("无法打开文件夹"));
            }
        });

        group.MapPost("/export", async (AppDataPaths paths) =>
        {
            var excludedDirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "models", "llama", "generated-fonts", "logs",
                "update-staging", "update-backup", "update-temp",
                "backups", "font-backups", "custom-fonts",
            };
            // font-generation/temp and font-generation/uploads are excluded separately
            var excludedSubDirs = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase)
            {
                ["font-generation"] = new(StringComparer.OrdinalIgnoreCase) { "temp", "uploads" },
            };

            var memoryStream = new MemoryStream();
            using (var archive = new ZipArchive(memoryStream, ZipArchiveMode.Create, leaveOpen: true))
            {
                var rootPath = Path.GetFullPath(paths.Root);
                foreach (var filePath in Directory.EnumerateFiles(rootPath, "*", SearchOption.AllDirectories))
                {
                    var relativePath = Path.GetRelativePath(rootPath, filePath);
                    var parts = relativePath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

                    // Skip top-level excluded directories
                    if (parts.Length > 1 && excludedDirs.Contains(parts[0]))
                        continue;

                    // Skip excluded sub-directories (e.g., font-generation/temp)
                    if (parts.Length > 2 && excludedSubDirs.TryGetValue(parts[0], out var subExclusions)
                        && subExclusions.Contains(parts[1]))
                        continue;

                    var entry = archive.CreateEntry(relativePath.Replace('\\', '/'), CompressionLevel.Optimal);
                    await using var entryStream = entry.Open();
                    await using var fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                    await fileStream.CopyToAsync(entryStream);
                }
            }

            memoryStream.Position = 0;
            var fileName = $"XUnityToolkit_data_{DateTime.Now:yyyy-MM-dd}.zip";
            return Results.File(memoryStream, "application/zip", fileName);
        });

        group.MapPost("/import", async (HttpRequest request, AppDataPaths paths, ILogger<AppSettingsService> logger) =>
        {
            if (!request.HasFormContentType)
                return Results.BadRequest(ApiResult.Fail("请求必须是 multipart/form-data"));

            var form = await request.ReadFormAsync();
            var file = form.Files.FirstOrDefault();
            if (file is null || file.Length == 0)
                return Results.BadRequest(ApiResult.Fail("未找到上传的文件"));

            var rootPath = Path.GetFullPath(paths.Root);
            try
            {
                await using var stream = file.OpenReadStream();
                using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
                foreach (var entry in archive.Entries)
                {
                    // Skip directories
                    if (string.IsNullOrEmpty(entry.Name))
                        continue;

                    var destPath = PathSecurity.SafeJoin(rootPath, entry.FullName.Replace('/', Path.DirectorySeparatorChar));
                    Directory.CreateDirectory(Path.GetDirectoryName(destPath)!);

                    await using var entryStream = entry.Open();
                    await using var destStream = new FileStream(destPath, FileMode.Create, FileAccess.Write, FileShare.None);
                    await entryStream.CopyToAsync(destStream);
                }

                // Migrate old do-not-translate/ entries into glossaries/ (unified term format)
                var dntDir = Path.Combine(rootPath, "do-not-translate");
                if (Directory.Exists(dntDir))
                {
                    var glossariesDir = Path.Combine(rootPath, "glossaries");
                    Directory.CreateDirectory(glossariesDir);

                    foreach (var dntFile in Directory.EnumerateFiles(dntDir, "*.json"))
                    {
                        try
                        {
                            var gameId = Path.GetFileNameWithoutExtension(dntFile);
                            var dntJson = await File.ReadAllTextAsync(dntFile);
                            using var dntDoc = JsonDocument.Parse(dntJson);

                            var dntTerms = new List<TermEntry>();
                            foreach (var element in dntDoc.RootElement.EnumerateArray())
                            {
                                var original = element.GetProperty("original").GetString();
                                if (string.IsNullOrEmpty(original)) continue;

                                var caseSensitive = element.TryGetProperty("caseSensitive", out var csProp)
                                    && csProp.GetBoolean();

                                dntTerms.Add(new TermEntry
                                {
                                    Type = TermType.DoNotTranslate,
                                    Original = original,
                                    CaseSensitive = caseSensitive,
                                });
                            }

                            if (dntTerms.Count == 0)
                            {
                                File.Delete(dntFile);
                                continue;
                            }

                            // Merge with existing glossary if present
                            var glossaryFile = Path.Combine(glossariesDir, $"{gameId}.json");
                            var existingTerms = new List<TermEntry>();
                            if (File.Exists(glossaryFile))
                            {
                                var glossaryJson = await File.ReadAllTextAsync(glossaryFile);
                                existingTerms = JsonSerializer.Deserialize<List<TermEntry>>(glossaryJson) ?? [];
                            }

                            // Dedup by Original
                            var existingOriginals = new HashSet<string>(
                                existingTerms.Select(t => t.Original),
                                StringComparer.Ordinal);

                            foreach (var term in dntTerms)
                            {
                                if (existingOriginals.Add(term.Original))
                                    existingTerms.Add(term);
                            }

                            var mergedJson = JsonSerializer.Serialize(existingTerms,
                                new JsonSerializerOptions { WriteIndented = true });
                            await File.WriteAllTextAsync(glossaryFile, mergedJson);
                            File.Delete(dntFile);
                        }
                        catch (Exception ex)
                        {
                            logger.LogWarning(ex, "迁移 do-not-translate 文件失败: {File}", Path.GetFileName(dntFile));
                        }
                    }

                    // Remove directory if empty
                    if (!Directory.EnumerateFileSystemEntries(dntDir).Any())
                        Directory.Delete(dntDir);
                }

                logger.LogInformation("已导入配置数据");
                return Results.Ok(ApiResult.Ok());
            }
            catch (InvalidOperationException)
            {
                return Results.BadRequest(ApiResult.Fail("文件路径不安全，导入已中止"));
            }
            catch (InvalidDataException)
            {
                return Results.BadRequest(ApiResult.Fail("无效的 ZIP 文件"));
            }
        }).DisableAntiforgery();
    }
    private static void TryDelete(Action action, string name, List<string> errors)
    {
        try { action(); }
        catch (Exception ex) { errors.Add($"{name}: {ex.Message}"); }
    }
}

public record VersionInfo(string Version);

public record DataPathInfo(string Path);
