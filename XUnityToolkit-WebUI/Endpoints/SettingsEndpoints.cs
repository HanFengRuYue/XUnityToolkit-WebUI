using System.Reflection;
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
            var saved = await settingsService.SaveAsync(settings);
            return Results.Ok(ApiResult<AppSettings>.Ok(saved));
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
}

public record VersionInfo(string Version);
