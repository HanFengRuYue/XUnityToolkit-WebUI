using System.Text.Json;
using System.Text.Json.Serialization;
using XUnityToolkit_WebUI.Infrastructure;
using XUnityToolkit_WebUI.Models;

namespace XUnityToolkit_WebUI.Services;

public sealed class AppSettingsService(AppDataPaths paths, ILogger<AppSettingsService> logger)
{
    private readonly SemaphoreSlim _lock = new(1, 1);
    private volatile AppSettings? _cached;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() }
    };

    public async Task<AppSettings> GetAsync(CancellationToken ct = default)
    {
        // Fast path: return cached settings without lock or disk I/O
        var cached = _cached;
        if (cached is not null) return cached;

        await _lock.WaitAsync(ct);
        try
        {
            // Double-check after acquiring lock
            cached = _cached;
            if (cached is not null) return cached;

            cached = await ReadAsync(ct);
            _cached = cached;
            return cached;
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
            _cached = settings; // Update cache immediately
            logger.LogInformation("已保存应用设置");
            return settings;
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    /// Read-modify-write: loads a fresh copy from cache, applies the mutation, and saves.
    /// This avoids mutating the shared cached object directly.
    /// </summary>
    public async Task UpdateAsync(Action<AppSettings> mutate, CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            // Round-trip through JSON to get a deep copy, avoiding mutation of the cached instance
            var current = _cached ?? await ReadAsync(ct);
            var json = JsonSerializer.Serialize(current, JsonOptions);
            var copy = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions)!;
            mutate(copy);
            await WriteAsync(copy, ct);
            _cached = copy;
            logger.LogInformation("已保存应用设置");
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    /// Invalidate the in-memory cache, forcing next GetAsync to reload from disk.
    /// </summary>
    public void InvalidateCache() => _cached = null;

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
