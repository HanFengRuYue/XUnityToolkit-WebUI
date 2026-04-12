using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using UnityLocalizationToolkit_WebUI.Infrastructure;
using UnityLocalizationToolkit_WebUI.Models;

namespace UnityLocalizationToolkit_WebUI.Services;

public sealed class ManualTranslationService(
    AppDataPaths paths,
    UnityAssetCatalogService catalogService,
    UnityAssetWriteService writeService,
    ILogger<ManualTranslationService> logger)
{
    public async Task<ManualTranslationProjectIndex> ScanAsync(Game game, CancellationToken ct = default)
    {
        var index = await catalogService.ScanAsync(game, ct);
        var projectDir = paths.ManualTranslationProjectDirectory(game.Id);
        Directory.CreateDirectory(projectDir);
        Directory.CreateDirectory(paths.ManualTranslationOverridesDirectory(game.Id));
        Directory.CreateDirectory(paths.ManualTranslationExportsDirectory(game.Id));
        Directory.CreateDirectory(paths.ManualTranslationBuildsDirectory(game.Id));
        Directory.CreateDirectory(paths.ManualTranslationCodePatchesDirectory(game.Id));

        var manifest = new ManualTranslationProjectManifest
        {
            GameId = game.Id,
            ScannedAt = DateTime.UtcNow,
            TotalAssets = index.Assets.Count,
            EditableAssets = index.Assets.Count(asset => asset.Editable),
            State = "ready"
        };

        await FileHelper.WriteJsonAtomicAsync(paths.ManualTranslationAssetIndexFile(game.Id), index, ct: ct);
        await FileHelper.WriteJsonAtomicAsync(paths.ManualTranslationManifestFile(game.Id), manifest, ct: ct);
        logger.LogInformation("Scanned manual translation assets for {GameId}: {AssetCount} assets", game.Id, index.Assets.Count);
        return index;
    }

    public async Task<ManualTranslationStatus> GetStatusAsync(string gameId, CancellationToken ct = default)
    {
        var manifest = await ReadManifestAsync(gameId, ct);
        var overrides = await ReadOverridesAsync(gameId, ct);
        var buildDir = paths.ManualTranslationBuildsDirectory(gameId);
        var lastPackage = Directory.Exists(buildDir)
            ? Directory.GetFiles(buildDir, "*.zip", SearchOption.TopDirectoryOnly)
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .FirstOrDefault()
            : null;

        if (manifest is null)
        {
            return new ManualTranslationStatus
            {
                GameId = gameId,
                HasScan = false,
                OverrideCount = overrides.Count
            };
        }

        return new ManualTranslationStatus
        {
            GameId = gameId,
            HasScan = true,
            State = overrides.Count > manifest.OverridesApplied ? "dirty" : manifest.State,
            AssetCount = manifest.TotalAssets,
            EditableAssetCount = manifest.EditableAssets,
            OverrideCount = overrides.Count,
            HasBackups = Directory.Exists(paths.ManualTranslationBackupDirectory(gameId)),
            ScannedAt = manifest.ScannedAt,
            AppliedAt = manifest.AppliedAt,
            LastPackagePath = lastPackage
        };
    }

    public async Task<GameTranslationWorkspaceSummary> GetWorkspaceSummaryAsync(string gameId, CancellationToken ct = default)
    {
        var status = await GetStatusAsync(gameId, ct);
        return new GameTranslationWorkspaceSummary
        {
            Workspace = GameTranslationWorkspace.Manual,
            Available = status.HasScan,
            Active = status.HasScan && (status.OverrideCount > 0 || status.AssetCount > 0),
            State = status.State,
            AssetCount = status.AssetCount,
            EditableAssetCount = status.EditableAssetCount,
            OverrideCount = status.OverrideCount,
            UpdatedAt = status.AppliedAt ?? status.ScannedAt,
            Summary = status.HasScan
                ? $"Indexed {status.AssetCount} assets, {status.OverrideCount} overrides"
                : "Manual translation workspace not scanned yet"
        };
    }

    public async Task<ManualTranslationAssetListResponse> GetAssetsAsync(string gameId, ManualTranslationAssetsQuery query, CancellationToken ct = default)
    {
        var index = await ReadIndexAsync(gameId, ct) ?? new ManualTranslationProjectIndex { GameId = gameId };
        var overrides = await ReadOverridesAsync(gameId, ct);
        var overrideMap = overrides.ToDictionary(item => item.AssetId, StringComparer.Ordinal);

        IEnumerable<ManualTranslationAssetEntry> assets = index.Assets.Select(asset =>
        {
            if (overrideMap.TryGetValue(asset.AssetId, out var entry))
            {
                asset.Overridden = true;
                asset.OverrideUpdatedAt = entry.UpdatedAt;
            }
            else
            {
                asset.Overridden = false;
                asset.OverrideUpdatedAt = null;
            }

            return asset;
        });

        if (!string.IsNullOrWhiteSpace(query.Scope))
        {
            assets = assets.Where(asset =>
                asset.StorageKind.ToString().Contains(query.Scope, StringComparison.OrdinalIgnoreCase) ||
                asset.ObjectType.Contains(query.Scope, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            assets = assets.Where(asset =>
                (asset.DisplayName?.Contains(query.Search, StringComparison.OrdinalIgnoreCase) ?? false) ||
                (asset.Preview?.Contains(query.Search, StringComparison.OrdinalIgnoreCase) ?? false) ||
                (asset.FieldPath?.Contains(query.Search, StringComparison.OrdinalIgnoreCase) ?? false));
        }

        if (query.EditableOnly)
            assets = assets.Where(asset => asset.Editable);
        if (query.OverriddenOnly)
            assets = assets.Where(asset => asset.Overridden);

        return new ManualTranslationAssetListResponse
        {
            GameId = gameId,
            Assets = assets.ToList()
        };
    }

    public async Task<ManualTranslationAssetEntry?> GetAssetDetailAsync(string gameId, string assetId, CancellationToken ct = default)
    {
        var index = await ReadIndexAsync(gameId, ct);
        return index?.Assets.FirstOrDefault(asset => asset.AssetId == assetId);
    }

    public async Task<ManualTranslationAssetContent?> GetAssetContentAsync(string gameId, string assetId, CancellationToken ct = default)
    {
        var asset = await GetAssetDetailAsync(gameId, assetId, ct);
        if (asset is null)
            return null;

        var overrides = await ReadOverridesAsync(gameId, ct);
        var overrideValue = overrides.FirstOrDefault(item => item.AssetId == assetId)?.Value;

        return new ManualTranslationAssetContent
        {
            AssetId = assetId,
            Asset = asset,
            OriginalText = asset.OriginalText,
            OverrideText = overrideValue,
            EffectiveText = overrideValue ?? asset.OriginalText
        };
    }

    public async Task SaveOverrideAsync(string gameId, string assetId, string value, string? source = null, CancellationToken ct = default)
    {
        var index = await ReadIndexAsync(gameId, ct)
            ?? throw new InvalidOperationException("Manual translation index does not exist. Scan first.");
        if (!index.Assets.Any(asset => asset.AssetId == assetId))
            throw new InvalidOperationException("Target asset was not found.");

        Directory.CreateDirectory(paths.ManualTranslationOverridesDirectory(gameId));
        var entry = new ManualTranslationOverrideEntry
        {
            AssetId = assetId,
            Value = value,
            Source = string.IsNullOrWhiteSpace(source) ? "manual" : source,
            UpdatedAt = DateTime.UtcNow
        };
        await FileHelper.WriteJsonAtomicAsync(GetOverrideFilePath(gameId, assetId), entry, ct: ct);
    }

    public Task DeleteOverrideAsync(string gameId, string assetId, CancellationToken ct = default)
    {
        var overridePath = GetOverrideFilePath(gameId, assetId);
        if (File.Exists(overridePath))
            File.Delete(overridePath);
        return Task.CompletedTask;
    }

    public async Task<ManualTranslationApplyResult> ApplyAsync(Game game, CancellationToken ct = default)
    {
        var index = await ReadIndexAsync(game.Id, ct)
            ?? throw new InvalidOperationException("Manual translation index does not exist. Scan first.");
        var overrides = await ReadOverridesAsync(game.Id, ct);
        var result = await writeService.ApplyAsync(
            game,
            index,
            overrides,
            paths.ManualTranslationBackupDirectory(game.Id),
            ct);

        var manifest = await ReadManifestAsync(game.Id, ct) ?? new ManualTranslationProjectManifest { GameId = game.Id };
        manifest.AppliedAt = DateTime.UtcNow;
        manifest.OverridesApplied = overrides.Count;
        manifest.State = "applied";
        manifest.TotalAssets = index.Assets.Count;
        manifest.EditableAssets = index.Assets.Count(asset => asset.Editable);
        await FileHelper.WriteJsonAtomicAsync(paths.ManualTranslationManifestFile(game.Id), manifest, ct: ct);
        return result;
    }

    public async Task RestoreAsync(Game game, CancellationToken ct = default)
    {
        await writeService.RestoreAsync(game, paths.ManualTranslationBackupDirectory(game.Id), ct);
        var manifest = await ReadManifestAsync(game.Id, ct);
        if (manifest is null)
            return;

        manifest.State = "ready";
        manifest.AppliedAt = null;
        manifest.OverridesApplied = 0;
        await FileHelper.WriteJsonAtomicAsync(paths.ManualTranslationManifestFile(game.Id), manifest, ct: ct);
    }

    public async Task<(MemoryStream Stream, string FileName)> BuildPackageAsync(Game game, CancellationToken ct = default)
    {
        var overrides = await ReadOverridesAsync(game.Id, ct);
        var index = await ReadIndexAsync(game.Id, ct) ?? new ManualTranslationProjectIndex { GameId = game.Id };
        var packageManifest = new ManualTranslationPackageManifest
        {
            GameId = game.Id,
            OverrideCount = overrides.Count
        };

        var memory = new MemoryStream();
        using (var archive = new ZipArchive(memory, ZipArchiveMode.Create, leaveOpen: true))
        {
            WriteZipJson(archive, "package-manifest.json", packageManifest);
            WriteZipJson(archive, "asset-index.json", index);
            foreach (var entry in overrides)
            {
                WriteZipJson(archive, $"overrides/{GetOverrideFileName(entry.AssetId)}.json", entry);
            }
        }

        memory.Position = 0;
        Directory.CreateDirectory(paths.ManualTranslationBuildsDirectory(game.Id));
        var fileName = $"{SanitizeFileName(game.Name)}_manual_translation_{DateTime.Now:yyyy-MM-dd}.zip";
        var filePath = Path.Combine(paths.ManualTranslationBuildsDirectory(game.Id), fileName);
        await using (var fs = File.Create(filePath))
        {
            memory.Position = 0;
            await memory.CopyToAsync(fs, ct);
        }

        memory.Position = 0;
        return (memory, fileName);
    }

    public async Task ImportPackageAsync(string gameId, string zipPath, CancellationToken ct = default)
    {
        if (!File.Exists(zipPath))
            throw new FileNotFoundException("Patch package was not found.", zipPath);

        Directory.CreateDirectory(paths.ManualTranslationProjectDirectory(gameId));
        Directory.CreateDirectory(paths.ManualTranslationOverridesDirectory(gameId));

        using var archive = ZipFile.OpenRead(zipPath);
        foreach (var entry in archive.Entries)
        {
            ct.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(entry.Name))
                continue;

            if (entry.FullName.Equals("asset-index.json", StringComparison.OrdinalIgnoreCase))
            {
                var targetPath = paths.ManualTranslationAssetIndexFile(gameId);
                Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
                await using var indexStream = entry.Open();
                await using var indexFileStream = File.Create(targetPath);
                await indexStream.CopyToAsync(indexFileStream, ct);
                continue;
            }

            if (!entry.FullName.StartsWith("overrides/", StringComparison.OrdinalIgnoreCase))
                continue;

            var targetOverride = Path.Combine(paths.ManualTranslationOverridesDirectory(gameId), entry.Name);
            await using var overrideStream = entry.Open();
            await using var overrideFileStream = File.Create(targetOverride);
            await overrideStream.CopyToAsync(overrideFileStream, ct);
        }

        var manifest = await ReadManifestAsync(gameId, ct);
        if (manifest is null && File.Exists(paths.ManualTranslationAssetIndexFile(gameId)))
        {
            var index = await ReadIndexAsync(gameId, ct);
            manifest = new ManualTranslationProjectManifest
            {
                GameId = gameId,
                ScannedAt = DateTime.UtcNow,
                TotalAssets = index?.Assets.Count ?? 0,
                EditableAssets = index?.Assets.Count(asset => asset.Editable) ?? 0,
                State = "ready"
            };
            await FileHelper.WriteJsonAtomicAsync(paths.ManualTranslationManifestFile(gameId), manifest, ct: ct);
        }
    }

    public async Task<(Stream Stream, string ContentType, string FileName)> ExportAssetAsync(Game game, string assetId, CancellationToken ct = default)
    {
        var content = await GetAssetContentAsync(game.Id, assetId, ct)
            ?? throw new InvalidOperationException("Target asset was not found.");
        var asset = content.Asset;
        var exportName = $"{SanitizeFileName(asset.DisplayName ?? asset.ObjectType)}_{DateTime.Now:yyyyMMddHHmmss}.txt";
        var text = content.EffectiveText ?? content.OriginalText;
        if (string.IsNullOrWhiteSpace(text))
        {
            exportName = $"{SanitizeFileName(asset.DisplayName ?? asset.ObjectType)}_metadata.json";
            text = JsonSerializer.Serialize(asset, FileHelper.DataJsonOptions);
        }

        var bytes = Encoding.UTF8.GetBytes(text);
        return (new MemoryStream(bytes), "text/plain; charset=utf-8", exportName);
    }

    public void DeleteGameData(string gameId)
    {
        DeleteDirectoryIfExists(paths.ManualTranslationProjectDirectory(gameId));
        DeleteDirectoryIfExists(paths.ManualTranslationBackupDirectory(gameId));
    }

    private async Task<ManualTranslationProjectManifest?> ReadManifestAsync(string gameId, CancellationToken ct)
    {
        var manifestPath = paths.ManualTranslationManifestFile(gameId);
        if (!File.Exists(manifestPath))
            return null;

        var json = await File.ReadAllTextAsync(manifestPath, ct);
        return JsonSerializer.Deserialize<ManualTranslationProjectManifest>(json, FileHelper.DataJsonOptions);
    }

    private async Task<ManualTranslationProjectIndex?> ReadIndexAsync(string gameId, CancellationToken ct)
    {
        var indexPath = paths.ManualTranslationAssetIndexFile(gameId);
        if (!File.Exists(indexPath))
            return null;

        var json = await File.ReadAllTextAsync(indexPath, ct);
        return JsonSerializer.Deserialize<ManualTranslationProjectIndex>(json, FileHelper.DataJsonOptions);
    }

    private async Task<List<ManualTranslationOverrideEntry>> ReadOverridesAsync(string gameId, CancellationToken ct)
    {
        var overrideDir = paths.ManualTranslationOverridesDirectory(gameId);
        if (!Directory.Exists(overrideDir))
            return [];

        var result = new List<ManualTranslationOverrideEntry>();
        foreach (var file in Directory.GetFiles(overrideDir, "*.json", SearchOption.TopDirectoryOnly))
        {
            ct.ThrowIfCancellationRequested();
            var json = await File.ReadAllTextAsync(file, ct);
            var entry = JsonSerializer.Deserialize<ManualTranslationOverrideEntry>(json, FileHelper.DataJsonOptions);
            if (entry is not null)
                result.Add(entry);
        }

        return result;
    }

    private string GetOverrideFilePath(string gameId, string assetId)
    {
        return Path.Combine(paths.ManualTranslationOverridesDirectory(gameId), $"{GetOverrideFileName(assetId)}.json");
    }

    private static string GetOverrideFileName(string assetId)
    {
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(assetId)));
    }

    private static void WriteZipJson<T>(ZipArchive archive, string entryName, T payload)
    {
        var entry = archive.CreateEntry(entryName, CompressionLevel.SmallestSize);
        using var writer = new StreamWriter(entry.Open(), Encoding.UTF8);
        writer.Write(JsonSerializer.Serialize(payload, FileHelper.DataJsonOptions));
    }

    private static string SanitizeFileName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return new string(value.Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray());
    }

    private static void DeleteDirectoryIfExists(string path)
    {
        if (Directory.Exists(path))
            Directory.Delete(path, recursive: true);
    }
}
