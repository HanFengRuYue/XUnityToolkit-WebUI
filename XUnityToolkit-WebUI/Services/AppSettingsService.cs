using System.Text.Json;
using System.Text.Json.Serialization;
using XUnityToolkit_WebUI.Infrastructure;
using XUnityToolkit_WebUI.Models;

namespace XUnityToolkit_WebUI.Services;

public sealed class AppSettingsService(AppDataPaths paths, ILogger<AppSettingsService> logger)
{
    private readonly SemaphoreSlim _lock = new(1, 1);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() }
    };

    public async Task<AppSettings> GetAsync(CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            return await ReadAsync(ct);
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<AppSettings> SaveAsync(AppSettings settings, CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            await WriteAsync(settings, ct);
            logger.LogInformation("已保存应用设置");
            return settings;
        }
        finally
        {
            _lock.Release();
        }
    }

    private async Task<AppSettings> ReadAsync(CancellationToken ct)
    {
        if (!File.Exists(paths.SettingsFile))
            return new AppSettings();

        var json = await File.ReadAllTextAsync(paths.SettingsFile, ct);
        return JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();
    }

    private async Task WriteAsync(AppSettings settings, CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(settings, JsonOptions);
        var tmpPath = paths.SettingsFile + ".tmp";
        await File.WriteAllTextAsync(tmpPath, json, ct);
        File.Move(tmpPath, paths.SettingsFile, overwrite: true);
    }
}
