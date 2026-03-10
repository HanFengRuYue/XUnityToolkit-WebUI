using System.Reflection;
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
            // Server-side validation: clamp MaxConcurrency to [1, 100]
            settings.AiTranslation.MaxConcurrency = Math.Clamp(settings.AiTranslation.MaxConcurrency, 1, 100);

            var saved = await settingsService.SaveAsync(settings);
            return Results.Ok(ApiResult<AppSettings>.Ok(saved));
        });

        group.MapPost("/reset", (AppDataPaths paths, AppSettingsService settingsService, ILogger<AppSettingsService> logger) =>
        {
            settingsService.InvalidateCache();
            var errors = new List<string>();

            // Delete settings.json
            TryDelete(() =>
            {
                if (File.Exists(paths.SettingsFile)) File.Delete(paths.SettingsFile);
            }, "settings.json", errors);

            // Delete library.json
            TryDelete(() =>
            {
                if (File.Exists(paths.LibraryFile)) File.Delete(paths.LibraryFile);
            }, "library.json", errors);

            // Delete entire cache directory (includes download cache + icon cache)
            TryDelete(() =>
            {
                if (Directory.Exists(paths.CacheDirectory))
                    Directory.Delete(paths.CacheDirectory, recursive: true);
            }, "cache", errors);

            // Delete backups directory
            TryDelete(() =>
            {
                if (Directory.Exists(paths.BackupsDirectory))
                    Directory.Delete(paths.BackupsDirectory, recursive: true);
            }, "backups", errors);

            // Recreate required directories
            paths.EnsureDirectoriesExist();

            if (errors.Count > 0)
            {
                logger.LogWarning("重置配置时部分操作失败: {Errors}", string.Join(", ", errors));
                return Results.Ok(ApiResult<object>.Ok(new { partial = true, errors }));
            }

            logger.LogInformation("已重置所有配置和缓存");
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
    }
    private static void TryDelete(Action action, string name, List<string> errors)
    {
        try { action(); }
        catch (Exception ex) { errors.Add($"{name}: {ex.Message}"); }
    }
}

public record VersionInfo(string Version);
