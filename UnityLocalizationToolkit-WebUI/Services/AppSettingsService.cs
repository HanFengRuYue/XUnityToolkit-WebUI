using System.Text.Json;
using UnityLocalizationToolkit_WebUI.Infrastructure;
using UnityLocalizationToolkit_WebUI.Models;
using static UnityLocalizationToolkit_WebUI.Infrastructure.DpapiProtector;

namespace UnityLocalizationToolkit_WebUI.Services;

public sealed class AppSettingsService(AppDataPaths paths, ILogger<AppSettingsService> logger)
{
    private readonly SemaphoreSlim _lock = new(1, 1);
    private volatile AppSettings? _cached;

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
            var json = JsonSerializer.Serialize(current, FileHelper.DataJsonOptions);
            var copy = JsonSerializer.Deserialize<AppSettings>(json, FileHelper.DataJsonOptions)!;
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
        var settings = JsonSerializer.Deserialize<AppSettings>(json, FileHelper.DataJsonOptions) ?? new AppSettings();
        var hasFailures = DecryptSettings(settings);
        if (hasFailures)
        {
            // Back up the original file so ciphertext is not lost on subsequent saves
            var backupPath = paths.SettingsFile + ".bak";
            if (!File.Exists(backupPath))
            {
                await File.WriteAllTextAsync(backupPath, json, ct);
                logger.LogWarning("已备份加密的设置文件到 {Path}", backupPath);
            }
        }
        return settings;
    }

    private async Task WriteAsync(AppSettings settings, CancellationToken ct)
    {
        // Deep-copy then encrypt — never mutate the in-memory cached object
        var json = JsonSerializer.Serialize(settings, FileHelper.DataJsonOptions);
        var encryptedCopy = JsonSerializer.Deserialize<AppSettings>(json, FileHelper.DataJsonOptions)!;
        EncryptSettings(encryptedCopy);
        var encryptedJson = JsonSerializer.Serialize(encryptedCopy, FileHelper.DataJsonOptions);

        var tmpPath = paths.SettingsFile + ".tmp";
        await File.WriteAllTextAsync(tmpPath, encryptedJson, ct);
        File.Move(tmpPath, paths.SettingsFile, overwrite: true);
    }

    private static void EncryptSettings(AppSettings s)
    {
        s.SteamGridDbApiKey = Protect(s.SteamGridDbApiKey);
        foreach (var ep in s.AiTranslation.Endpoints)
            ep.ApiKey = Protect(ep.ApiKey) ?? "";
    }

    /// <summary>Returns true if any field failed to decrypt.</summary>
    private bool DecryptSettings(AppSettings s)
    {
        var hasFailures = false;

        var steamKey = Unprotect(s.SteamGridDbApiKey, logger);
        if (steamKey != s.SteamGridDbApiKey) s.SteamGridDbApiKey = steamKey;
        else if (IsEncrypted(s.SteamGridDbApiKey)) hasFailures = true;

        foreach (var ep in s.AiTranslation.Endpoints)
        {
            var decrypted = Unprotect(ep.ApiKey, logger);
            if (decrypted != ep.ApiKey) ep.ApiKey = decrypted ?? "";
            else if (IsEncrypted(ep.ApiKey)) hasFailures = true;
        }

        return hasFailures;
    }

    private static bool IsEncrypted(string? value) =>
        value is not null && value.StartsWith("ENC:DPAPI:", StringComparison.Ordinal);
}
