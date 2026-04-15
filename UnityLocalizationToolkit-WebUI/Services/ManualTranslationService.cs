using System.Collections.Concurrent;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO.Compression;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AssetsTools.NET;
using AssetsTools.NET.Extra;
using AssetsTools.NET.Texture;
using UnityLocalizationToolkit_WebUI.Infrastructure;
using UnityLocalizationToolkit_WebUI.Models;

namespace UnityLocalizationToolkit_WebUI.Services;

public sealed class ManualTranslationService(
    AppDataPaths paths,
    UnityAssetCatalogService catalogService,
    UnityAssetWriteService writeService,
    ILogger<ManualTranslationService> logger)
{
    private const int DefaultPageSize = 50;
    private const int MaxPageSize = 200;
    private const int ThumbMaxEdge = 480;

    private static readonly Lazy<byte[]> ClassDataTpk = new(() =>
    {
        using var stream = Assembly.GetExecutingAssembly()
            .GetManifestResourceStream("classdata.tpk")
            ?? throw new InvalidOperationException("Embedded classdata.tpk not found.");
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        return ms.ToArray();
    });

    private static readonly IReadOnlyDictionary<ManualTranslationAssetValueKind, int> ValueKindSortOrder =
        new Dictionary<ManualTranslationAssetValueKind, int>
        {
            [ManualTranslationAssetValueKind.Text] = 0,
            [ManualTranslationAssetValueKind.Code] = 1,
            [ManualTranslationAssetValueKind.Image] = 2,
            [ManualTranslationAssetValueKind.Font] = 3,
            [ManualTranslationAssetValueKind.Binary] = 4,
        };

    private readonly ConcurrentDictionary<string, CachedIndexEntry> _indexCache = new(StringComparer.Ordinal);

    private sealed record CachedIndexEntry(ManualTranslationProjectIndex Index, DateTime LastWriteUtc);
    private sealed record NormalizedAssetsQuery(
        string? Search,
        bool EditableOnly,
        bool OverriddenOnly,
        int Page,
        int PageSize,
        ManualTranslationAssetFilterValueKind ValueKind);

    public async Task<ManualTranslationProjectIndex> ScanAsync(Game game, CancellationToken ct = default)
    {
        var index = await catalogService.ScanAsync(game, ct);
        var projectDir = paths.ManualTranslationProjectDirectory(game.Id);
        Directory.CreateDirectory(projectDir);
        Directory.CreateDirectory(paths.ManualTranslationOverridesDirectory(game.Id));
        Directory.CreateDirectory(paths.ManualTranslationOverrideMediaDirectory(game.Id));
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
        CacheIndex(game.Id, index);
        logger.LogInformation("Scanned manual translation assets for {GameId}: {AssetCount} assets", game.Id, index.Assets.Count);
        return index;
    }

    public async Task<ManualTranslationStatus> GetStatusAsync(string gameId, CancellationToken ct = default)
    {
        var manifest = await ReadManifestAsync(gameId, ct);
        var index = await ReadIndexAsync(gameId, ct);
        var overrideCount = CountOverrides(gameId);
        var buildDir = paths.ManualTranslationBuildsDirectory(gameId);
        var lastPackage = Directory.Exists(buildDir)
            ? Directory.GetFiles(buildDir, "*.zip", SearchOption.TopDirectoryOnly)
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .FirstOrDefault()
            : null;

        var kindCounts = ManualTranslationKindCounts.FromAssets(index?.Assets ?? []);
        var hasScan = manifest is not null || index is not null;

        if (!hasScan)
        {
            return new ManualTranslationStatus
            {
                GameId = gameId,
                HasScan = false,
                OverrideCount = overrideCount,
                KindCounts = kindCounts
            };
        }

        return new ManualTranslationStatus
        {
            GameId = gameId,
            HasScan = true,
            State = overrideCount > (manifest?.OverridesApplied ?? 0) ? "dirty" : manifest?.State ?? "ready",
            AssetCount = manifest?.TotalAssets ?? index?.Assets.Count ?? 0,
            EditableAssetCount = manifest?.EditableAssets ?? index?.Assets.Count(asset => asset.Editable) ?? 0,
            OverrideCount = overrideCount,
            HasBackups = Directory.Exists(paths.ManualTranslationBackupDirectory(gameId)),
            ScannedAt = manifest?.ScannedAt ?? index?.ScannedAt,
            AppliedAt = manifest?.AppliedAt,
            LastPackagePath = lastPackage,
            KindCounts = kindCounts
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
        var normalizedQuery = NormalizeQuery(query);

        var filteredAssets = index.Assets
            .Where(asset => MatchesValueKind(asset, normalizedQuery.ValueKind))
            .Where(asset => !normalizedQuery.EditableOnly || asset.Editable)
            .Where(asset => !normalizedQuery.OverriddenOnly || overrideMap.ContainsKey(asset.AssetId))
            .Where(asset => MatchesSearch(asset, normalizedQuery.Search))
            .OrderByDescending(asset => overrideMap.ContainsKey(asset.AssetId))
            .ThenBy(asset => GetValueKindSortOrder(asset.ValueKind))
            .ThenBy(asset => GetSortText(asset), StringComparer.OrdinalIgnoreCase)
            .ThenBy(asset => asset.AssetFile, StringComparer.OrdinalIgnoreCase)
            .ThenBy(asset => asset.PathId ?? long.MaxValue)
            .ThenBy(asset => asset.AssetId, StringComparer.Ordinal)
            .ToList();

        var total = filteredAssets.Count;
        var totalPages = total == 0 ? 1 : (int)Math.Ceiling(total / (double)normalizedQuery.PageSize);
        var page = Math.Clamp(normalizedQuery.Page, 1, totalPages);
        var pageAssets = filteredAssets
            .Skip((page - 1) * normalizedQuery.PageSize)
            .Take(normalizedQuery.PageSize)
            .Select(asset => DecorateAsset(asset, overrideMap.GetValueOrDefault(asset.AssetId)))
            .ToList();

        return new ManualTranslationAssetListResponse
        {
            GameId = gameId,
            Total = total,
            Page = page,
            PageSize = normalizedQuery.PageSize,
            Assets = pageAssets
        };
    }

    public async Task<ManualTranslationAssetEntry?> GetAssetDetailAsync(string gameId, string assetId, CancellationToken ct = default)
    {
        var index = await ReadIndexAsync(gameId, ct);
        var asset = index?.Assets.FirstOrDefault(entry => entry.AssetId == assetId);
        if (asset is null)
            return null;

        var overrideEntry = await ReadOverrideAsync(gameId, assetId, ct);
        return DecorateAsset(asset, overrideEntry);
    }

    public async Task<ManualTranslationAssetContent?> GetAssetContentAsync(string gameId, string assetId, CancellationToken ct = default)
    {
        var index = await ReadIndexAsync(gameId, ct);
        var asset = index?.Assets.FirstOrDefault(entry => entry.AssetId == assetId);
        if (asset is null)
            return null;

        var overrideEntry = await ReadOverrideAsync(gameId, assetId, ct);
        var overrideText = overrideEntry?.Kind == ManualTranslationOverrideKind.Text ? overrideEntry.Value : null;
        return new ManualTranslationAssetContent
        {
            AssetId = assetId,
            Asset = DecorateAsset(asset, overrideEntry),
            OriginalText = asset.OriginalText,
            OverrideText = overrideText,
            EffectiveText = overrideText ?? asset.OriginalText
        };
    }

    public async Task SaveOverrideAsync(string gameId, string assetId, string value, string? source = null, CancellationToken ct = default)
    {
        var index = await ReadIndexAsync(gameId, ct)
            ?? throw new InvalidOperationException("Manual translation index does not exist. Scan first.");
        var asset = index.Assets.FirstOrDefault(entry => entry.AssetId == assetId)
            ?? throw new InvalidOperationException("Target asset was not found.");

        if (!asset.Editable || asset.ValueKind is not (ManualTranslationAssetValueKind.Text or ManualTranslationAssetValueKind.Code))
            throw new InvalidOperationException("This asset does not support text editing.");

        var existingOverride = await ReadOverrideAsync(gameId, assetId, ct);
        DeleteOverrideMediaIfExists(gameId, existingOverride);

        Directory.CreateDirectory(paths.ManualTranslationOverridesDirectory(gameId));
        var entry = new ManualTranslationOverrideEntry
        {
            AssetId = assetId,
            Kind = ManualTranslationOverrideKind.Text,
            Value = value,
            Source = string.IsNullOrWhiteSpace(source) ? "manual" : source,
            UpdatedAt = DateTime.UtcNow
        };
        await FileHelper.WriteJsonAtomicAsync(GetOverrideFilePath(gameId, assetId), entry, ct: ct);
    }

    public async Task SaveImageOverrideAsync(
        string gameId,
        string assetId,
        Stream imageStream,
        string? source = null,
        CancellationToken ct = default)
    {
        var asset = await RequireAssetAsync(gameId, assetId, ManualTranslationAssetValueKind.Image, ct);
        var existingOverride = await ReadOverrideAsync(gameId, assetId, ct);
        var (pngBytes, width, height) = await NormalizeImageToPngAsync(imageStream, ct);

        Directory.CreateDirectory(paths.ManualTranslationOverridesDirectory(gameId));
        Directory.CreateDirectory(paths.ManualTranslationOverrideMediaDirectory(gameId));

        var imageFileName = GetOverrideMediaFileName(asset.AssetId);
        var imagePath = paths.ManualTranslationOverrideMediaFile(gameId, imageFileName);
        await WriteBytesAtomicAsync(imagePath, pngBytes, ct);

        var entry = new ManualTranslationOverrideEntry
        {
            AssetId = asset.AssetId,
            Kind = ManualTranslationOverrideKind.Image,
            ImageFileName = imageFileName,
            ContentType = "image/png",
            Width = width,
            Height = height,
            Source = string.IsNullOrWhiteSpace(source) ? "manual-image" : source,
            UpdatedAt = DateTime.UtcNow
        };

        DeleteOverrideMediaIfExists(gameId, existingOverride, preserveFileName: imageFileName);
        await FileHelper.WriteJsonAtomicAsync(GetOverrideFilePath(gameId, asset.AssetId), entry, ct: ct);
    }

    public async Task SaveImageOverrideFromPathAsync(
        string gameId,
        string assetId,
        string filePath,
        string? source = null,
        CancellationToken ct = default)
    {
        if (!File.Exists(filePath))
            throw new FileNotFoundException("Selected image file was not found.", filePath);

        await using var stream = File.OpenRead(filePath);
        await SaveImageOverrideAsync(gameId, assetId, stream, source, ct);
    }

    public async Task DeleteOverrideAsync(string gameId, string assetId, CancellationToken ct = default)
    {
        var existingOverride = await ReadOverrideAsync(gameId, assetId, ct);
        DeleteOverrideMediaIfExists(gameId, existingOverride);

        var overridePath = GetOverrideFilePath(gameId, assetId);
        if (File.Exists(overridePath))
            File.Delete(overridePath);
    }

    public async Task<(Stream Stream, string ContentType, string FileName)> GetImagePreviewAsync(
        Game game,
        string assetId,
        ManualTranslationImagePreviewSource source,
        ManualTranslationImagePreviewSize size,
        CancellationToken ct = default)
    {
        var asset = await RequireAssetAsync(game.Id, assetId, ManualTranslationAssetValueKind.Image, ct);
        byte[] imageBytes;

        try
        {
            if (source == ManualTranslationImagePreviewSource.Override)
            {
                var overrideEntry = await ReadOverrideAsync(game.Id, asset.AssetId, ct);
                if (overrideEntry?.Kind != ManualTranslationOverrideKind.Image || string.IsNullOrWhiteSpace(overrideEntry.ImageFileName))
                    throw new InvalidOperationException("Image override was not found.");

                var imagePath = paths.ManualTranslationOverrideMediaFile(game.Id, overrideEntry.ImageFileName);
                if (!File.Exists(imagePath))
                    throw new InvalidOperationException("Image override file was not found.");

                imageBytes = await File.ReadAllBytesAsync(imagePath, ct);
            }
            else
            {
                imageBytes = await DecodeTextureAssetToPngAsync(game, asset, ct);
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or FileNotFoundException)
        {
            logger.LogWarning(
                ex,
                "Falling back to generated preview for {GameId} {AssetId} ({Source})",
                game.Id,
                asset.AssetId,
                source);
            imageBytes = CreateUnavailablePreviewPng(asset, source);
        }

        imageBytes = ResizePngIfNeeded(imageBytes, size);
        var fileName = $"{SanitizeFileName(asset.DisplayName ?? asset.ObjectType)}_{source.ToString().ToLowerInvariant()}.png";
        return (new MemoryStream(imageBytes), "image/png", fileName);
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
                if (entry.Kind == ManualTranslationOverrideKind.Image && !string.IsNullOrWhiteSpace(entry.ImageFileName))
                {
                    var mediaPath = paths.ManualTranslationOverrideMediaFile(game.Id, entry.ImageFileName);
                    if (File.Exists(mediaPath))
                        await WriteZipFileAsync(archive, $"override-media/{entry.ImageFileName}", mediaPath, ct);
                }
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
        DeleteDirectoryIfExists(paths.ManualTranslationOverridesDirectory(gameId));
        Directory.CreateDirectory(paths.ManualTranslationOverridesDirectory(gameId));
        Directory.CreateDirectory(paths.ManualTranslationOverrideMediaDirectory(gameId));

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

            if (entry.FullName.StartsWith("overrides/", StringComparison.OrdinalIgnoreCase))
            {
                var targetOverride = Path.Combine(paths.ManualTranslationOverridesDirectory(gameId), entry.Name);
                await using var overrideStream = entry.Open();
                await using var overrideFileStream = File.Create(targetOverride);
                await overrideStream.CopyToAsync(overrideFileStream, ct);
                continue;
            }

            if (!entry.FullName.StartsWith("override-media/", StringComparison.OrdinalIgnoreCase))
                continue;

            var targetMedia = paths.ManualTranslationOverrideMediaFile(gameId, entry.Name);
            Directory.CreateDirectory(Path.GetDirectoryName(targetMedia)!);
            await using var mediaStream = entry.Open();
            await using var mediaFileStream = File.Create(targetMedia);
            await mediaStream.CopyToAsync(mediaFileStream, ct);
        }

        var index = await ReadIndexAsync(gameId, ct);
        if (index is not null)
        {
            var manifest = await ReadManifestAsync(gameId, ct) ?? new ManualTranslationProjectManifest { GameId = gameId };
            manifest.ScannedAt = index.ScannedAt;
            manifest.AppliedAt = null;
            manifest.TotalAssets = index.Assets.Count;
            manifest.EditableAssets = index.Assets.Count(asset => asset.Editable);
            manifest.OverridesApplied = 0;
            manifest.State = "ready";
            await FileHelper.WriteJsonAtomicAsync(paths.ManualTranslationManifestFile(gameId), manifest, ct: ct);
        }

        InvalidateIndexCache(gameId);
    }

    public async Task<(Stream Stream, string ContentType, string FileName)> ExportAssetAsync(Game game, string assetId, CancellationToken ct = default)
    {
        var index = await ReadIndexAsync(game.Id, ct);
        var asset = index?.Assets.FirstOrDefault(entry => entry.AssetId == assetId)
            ?? throw new InvalidOperationException("Target asset was not found.");
        var overrideEntry = await ReadOverrideAsync(game.Id, assetId, ct);

        if (asset.ValueKind == ManualTranslationAssetValueKind.Image)
        {
            var source = overrideEntry?.Kind == ManualTranslationOverrideKind.Image
                ? ManualTranslationImagePreviewSource.Override
                : ManualTranslationImagePreviewSource.Original;
            return await GetImagePreviewAsync(game, assetId, source, ManualTranslationImagePreviewSize.Full, ct);
        }

        var content = await GetAssetContentAsync(game.Id, assetId, ct)
            ?? throw new InvalidOperationException("Target asset was not found.");
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
        InvalidateIndexCache(gameId);
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
        {
            InvalidateIndexCache(gameId);
            return null;
        }

        var lastWriteUtc = File.GetLastWriteTimeUtc(indexPath);
        if (_indexCache.TryGetValue(gameId, out var cached) && cached.LastWriteUtc == lastWriteUtc)
            return cached.Index;

        var json = await File.ReadAllTextAsync(indexPath, ct);
        var index = JsonSerializer.Deserialize<ManualTranslationProjectIndex>(json, FileHelper.DataJsonOptions);
        if (index is not null)
            _indexCache[gameId] = new CachedIndexEntry(index, lastWriteUtc);
        return index;
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

    private async Task<ManualTranslationOverrideEntry?> ReadOverrideAsync(string gameId, string assetId, CancellationToken ct)
    {
        var overridePath = GetOverrideFilePath(gameId, assetId);
        if (!File.Exists(overridePath))
            return null;

        var json = await File.ReadAllTextAsync(overridePath, ct);
        return JsonSerializer.Deserialize<ManualTranslationOverrideEntry>(json, FileHelper.DataJsonOptions);
    }

    private async Task<ManualTranslationAssetEntry> RequireAssetAsync(
        string gameId,
        string assetId,
        ManualTranslationAssetValueKind? expectedKind,
        CancellationToken ct)
    {
        var index = await ReadIndexAsync(gameId, ct)
            ?? throw new InvalidOperationException("Manual translation index does not exist. Scan first.");
        var asset = index.Assets.FirstOrDefault(entry => entry.AssetId == assetId)
            ?? throw new InvalidOperationException("Target asset was not found.");

        if (expectedKind.HasValue && asset.ValueKind != expectedKind.Value)
            throw new InvalidOperationException($"Target asset is not a {expectedKind.Value} asset.");

        return asset;
    }

    private string GetOverrideFilePath(string gameId, string assetId)
    {
        return Path.Combine(paths.ManualTranslationOverridesDirectory(gameId), $"{GetOverrideFileName(assetId)}.json");
    }

    private static string GetOverrideFileName(string assetId)
    {
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(assetId)));
    }

    private static string GetOverrideMediaFileName(string assetId)
    {
        return $"{GetOverrideFileName(assetId)}.png";
    }

    private void DeleteOverrideMediaIfExists(string gameId, ManualTranslationOverrideEntry? entry, string? preserveFileName = null)
    {
        if (entry?.Kind != ManualTranslationOverrideKind.Image || string.IsNullOrWhiteSpace(entry.ImageFileName))
            return;

        if (string.Equals(entry.ImageFileName, preserveFileName, StringComparison.OrdinalIgnoreCase))
            return;

        var mediaPath = paths.ManualTranslationOverrideMediaFile(gameId, entry.ImageFileName);
        if (File.Exists(mediaPath))
            File.Delete(mediaPath);
    }

    private static void WriteZipJson<T>(ZipArchive archive, string entryName, T payload)
    {
        var entry = archive.CreateEntry(entryName, CompressionLevel.SmallestSize);
        using var writer = new StreamWriter(entry.Open(), Encoding.UTF8);
        writer.Write(JsonSerializer.Serialize(payload, FileHelper.DataJsonOptions));
    }

    private static async Task WriteZipFileAsync(ZipArchive archive, string entryName, string filePath, CancellationToken ct)
    {
        var entry = archive.CreateEntry(entryName, CompressionLevel.SmallestSize);
        await using var input = File.OpenRead(filePath);
        await using var output = entry.Open();
        await input.CopyToAsync(output, ct);
    }

    private void CacheIndex(string gameId, ManualTranslationProjectIndex index)
    {
        var indexPath = paths.ManualTranslationAssetIndexFile(gameId);
        if (!File.Exists(indexPath))
            return;

        _indexCache[gameId] = new CachedIndexEntry(index, File.GetLastWriteTimeUtc(indexPath));
    }

    private void InvalidateIndexCache(string gameId)
    {
        _indexCache.TryRemove(gameId, out _);
    }

    private static NormalizedAssetsQuery NormalizeQuery(ManualTranslationAssetsQuery query)
    {
        var page = query.Page.GetValueOrDefault(1);
        if (page <= 0)
            page = 1;

        var pageSize = query.PageSize.GetValueOrDefault(DefaultPageSize);
        if (pageSize <= 0)
            pageSize = DefaultPageSize;

        return new NormalizedAssetsQuery(
            string.IsNullOrWhiteSpace(query.Search) ? null : query.Search.Trim(),
            query.EditableOnly ?? false,
            query.OverriddenOnly ?? false,
            page,
            Math.Clamp(pageSize, 1, MaxPageSize),
            query.ValueKind ?? ManualTranslationAssetFilterValueKind.All);
    }

    private static bool MatchesValueKind(ManualTranslationAssetEntry asset, ManualTranslationAssetFilterValueKind valueKind)
    {
        return valueKind switch
        {
            ManualTranslationAssetFilterValueKind.All => true,
            ManualTranslationAssetFilterValueKind.Text => asset.ValueKind == ManualTranslationAssetValueKind.Text,
            ManualTranslationAssetFilterValueKind.Code => asset.ValueKind == ManualTranslationAssetValueKind.Code,
            ManualTranslationAssetFilterValueKind.Image => asset.ValueKind == ManualTranslationAssetValueKind.Image,
            ManualTranslationAssetFilterValueKind.Font => asset.ValueKind == ManualTranslationAssetValueKind.Font,
            ManualTranslationAssetFilterValueKind.Binary => asset.ValueKind == ManualTranslationAssetValueKind.Binary,
            _ => true
        };
    }

    private static bool MatchesSearch(ManualTranslationAssetEntry asset, string? search)
    {
        if (string.IsNullOrWhiteSpace(search))
            return true;

        return (asset.DisplayName?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false)
            || asset.ObjectType.Contains(search, StringComparison.OrdinalIgnoreCase)
            || asset.AssetFile.Contains(search, StringComparison.OrdinalIgnoreCase)
            || (asset.ListTitle?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false)
            || (asset.ListSubtitle?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false)
            || (asset.ListMeta?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false)
            || (asset.Preview?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false)
            || (asset.FieldPath?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false)
            || (asset.CodeLocation?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false)
            || (asset.OriginalText?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false);
    }

    private static int GetValueKindSortOrder(ManualTranslationAssetValueKind valueKind)
    {
        return ValueKindSortOrder.TryGetValue(valueKind, out var order) ? order : int.MaxValue;
    }

    private static string GetSortText(ManualTranslationAssetEntry asset)
    {
        return asset.ListTitle
            ?? asset.Preview
            ?? asset.DisplayName
            ?? asset.FieldPath
            ?? asset.CodeLocation
            ?? asset.ObjectType;
    }

    private static ManualTranslationAssetEntry DecorateAsset(ManualTranslationAssetEntry asset, ManualTranslationOverrideEntry? overrideEntry)
    {
        var listTitle = asset.ListTitle ?? BuildListTitle(asset);
        var listSubtitle = asset.ListSubtitle ?? BuildListSubtitle(asset);
        var listMeta = asset.ListMeta ?? BuildListMeta(asset);

        return new ManualTranslationAssetEntry
        {
            AssetId = asset.AssetId,
            StorageKind = asset.StorageKind,
            ValueKind = asset.ValueKind,
            ObjectType = asset.ObjectType,
            AssetFile = asset.AssetFile,
            BundleEntry = asset.BundleEntry,
            RelativePath = asset.RelativePath,
            PathId = asset.PathId,
            DisplayName = asset.DisplayName,
            FieldPath = asset.FieldPath,
            CodeLocation = asset.CodeLocation,
            Preview = asset.Preview,
            OriginalText = asset.OriginalText,
            ListTitle = listTitle,
            ListSubtitle = listSubtitle,
            ListMeta = listMeta,
            IconKey = asset.IconKey ?? GetIconKey(asset.ValueKind),
            Width = asset.Width,
            Height = asset.Height,
            Editable = asset.Editable,
            Exportable = asset.Exportable,
            Overridden = overrideEntry is not null,
            OverrideUpdatedAt = overrideEntry?.UpdatedAt,
            EditHint = asset.EditHint
        };
    }

    private static string BuildListTitle(ManualTranslationAssetEntry asset)
    {
        if (asset.ValueKind is ManualTranslationAssetValueKind.Text or ManualTranslationAssetValueKind.Code)
            return FirstNonEmpty(asset.Preview, asset.DisplayName, asset.ObjectType);

        return FirstNonEmpty(asset.DisplayName, asset.Preview, asset.ObjectType);
    }

    private static string? BuildListSubtitle(ManualTranslationAssetEntry asset)
    {
        return asset.ValueKind switch
        {
            ManualTranslationAssetValueKind.Text => JoinDisplayParts(
                asset.FieldPath,
                GetMeaningfulDisplayName(asset.DisplayName, asset.ObjectType)),
            ManualTranslationAssetValueKind.Code => JoinDisplayParts(
                asset.FieldPath,
                GetMeaningfulDisplayName(asset.DisplayName, asset.ObjectType),
                asset.CodeLocation),
            ManualTranslationAssetValueKind.Image => asset.Width > 0 && asset.Height > 0
                ? $"{asset.Width} × {asset.Height}"
                : asset.ObjectType,
            ManualTranslationAssetValueKind.Font => FirstNonEmpty(
                GetMeaningfulDisplayName(asset.DisplayName, asset.ObjectType),
                BuildPreviewText(asset.EditHint),
                asset.ObjectType),
            ManualTranslationAssetValueKind.Binary => FirstNonEmpty(
                BuildPreviewText(asset.EditHint),
                GetMeaningfulDisplayName(asset.DisplayName, asset.ObjectType),
                asset.ObjectType),
            _ => asset.ObjectType
        };
    }

    private static string BuildListMeta(ManualTranslationAssetEntry asset)
    {
        return JoinDisplayParts(
                   asset.AssetFile,
                   asset.BundleEntry,
                   asset.RelativePath,
                   asset.PathId.HasValue ? $"PathID {asset.PathId.Value}" : null)
               ?? asset.AssetFile;
    }

    private static string GetIconKey(ManualTranslationAssetValueKind valueKind)
    {
        return valueKind switch
        {
            ManualTranslationAssetValueKind.Text => "text",
            ManualTranslationAssetValueKind.Code => "code",
            ManualTranslationAssetValueKind.Image => "image",
            ManualTranslationAssetValueKind.Font => "font",
            ManualTranslationAssetValueKind.Binary => "binary",
            _ => "asset"
        };
    }

    private static string? GetMeaningfulDisplayName(string? displayName, string objectType)
    {
        if (string.IsNullOrWhiteSpace(displayName))
            return null;

        if (displayName.Equals(objectType, StringComparison.OrdinalIgnoreCase))
            return null;

        if (displayName.StartsWith("(unnamed", StringComparison.OrdinalIgnoreCase))
            return null;

        return displayName;
    }

    private static string FirstNonEmpty(params string?[] values)
    {
        return values.First(value => !string.IsNullOrWhiteSpace(value))!;
    }

    private static string? JoinDisplayParts(params string?[] values)
    {
        var parts = values
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return parts.Length == 0 ? null : string.Join(" · ", parts);
    }

    private int CountOverrides(string gameId)
    {
        var overrideDir = paths.ManualTranslationOverridesDirectory(gameId);
        return Directory.Exists(overrideDir)
            ? Directory.GetFiles(overrideDir, "*.json", SearchOption.TopDirectoryOnly).Length
            : 0;
    }

    private static string SanitizeFileName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return new string(value.Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray());
    }

    private static string? BuildPreviewText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var singleLine = value.Replace("\r", " ").Replace("\n", " ").Trim();
        return singleLine.Length <= 96 ? singleLine : singleLine[..96] + "...";
    }

    private static void DeleteDirectoryIfExists(string path)
    {
        if (Directory.Exists(path))
            Directory.Delete(path, recursive: true);
    }

    private static async Task WriteBytesAtomicAsync(string filePath, byte[] bytes, CancellationToken ct)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
        var tmpPath = filePath + ".tmp";
        await File.WriteAllBytesAsync(tmpPath, bytes, ct);
        File.Move(tmpPath, filePath, overwrite: true);
    }

    private static async Task<(byte[] PngBytes, int Width, int Height)> NormalizeImageToPngAsync(Stream imageStream, CancellationToken ct)
    {
        using var input = new MemoryStream();
        await imageStream.CopyToAsync(input, ct);
        input.Position = 0;

        try
        {
            using var image = Image.FromStream(input, useEmbeddedColorManagement: false, validateImageData: true);
            using var bitmap = new Bitmap(image);
            using var output = new MemoryStream();
            bitmap.Save(output, ImageFormat.Png);
            return (output.ToArray(), bitmap.Width, bitmap.Height);
        }
        catch (Exception ex) when (ex is ArgumentException or System.Runtime.InteropServices.ExternalException)
        {
            throw new InvalidDataException("The uploaded image could not be decoded. Use a valid PNG or JPEG image.", ex);
        }
    }

    private static byte[] ResizePngIfNeeded(byte[] pngBytes, ManualTranslationImagePreviewSize size)
    {
        if (size != ManualTranslationImagePreviewSize.Thumb)
            return pngBytes;

        using var input = new MemoryStream(pngBytes);
        using var image = Image.FromStream(input, useEmbeddedColorManagement: false, validateImageData: true);
        var maxEdge = Math.Max(image.Width, image.Height);
        if (maxEdge <= ThumbMaxEdge)
            return pngBytes;

        var scale = ThumbMaxEdge / (double)maxEdge;
        var width = Math.Max(1, (int)Math.Round(image.Width * scale));
        var height = Math.Max(1, (int)Math.Round(image.Height * scale));
        using var bitmap = new Bitmap(width, height);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.CompositingQuality = CompositingQuality.HighQuality;
        graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
        graphics.SmoothingMode = SmoothingMode.HighQuality;
        graphics.DrawImage(image, 0, 0, width, height);
        using var output = new MemoryStream();
        bitmap.Save(output, ImageFormat.Png);
        return output.ToArray();
    }

    private static byte[] CreateUnavailablePreviewPng(
        ManualTranslationAssetEntry asset,
        ManualTranslationImagePreviewSource source)
    {
        var (width, height) = GetPreviewCanvasSize(asset);
        using var bitmap = new Bitmap(width, height);
        using var graphics = Graphics.FromImage(bitmap);

        graphics.SmoothingMode = SmoothingMode.HighQuality;
        graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
        graphics.Clear(Color.FromArgb(23, 23, 31));

        using var panelBrush = new SolidBrush(Color.FromArgb(36, 36, 48));
        using var panelRect = new GraphicsPath();
        panelRect.AddRoundedRectangle(new Rectangle(18, 18, width - 36, height - 36), new Size(18, 18));
        graphics.FillPath(panelBrush, panelRect);

        using var borderPen = new Pen(Color.FromArgb(72, 255, 255, 255), 1f);
        graphics.DrawPath(borderPen, panelRect);

        var title = source == ManualTranslationImagePreviewSource.Override
            ? "替换图预览暂不可用"
            : "原图预览暂不可用";
        var subtitle = asset.DisplayName ?? asset.ObjectType;
        var meta = asset.Width.HasValue && asset.Height.HasValue
            ? $"{asset.ObjectType} · {asset.Width} × {asset.Height}"
            : asset.ObjectType;

        var fontFamily = SystemFonts.MessageBoxFont?.FontFamily ?? FontFamily.GenericSansSerif;
        using var titleFont = new Font(fontFamily, 16f, FontStyle.Bold, GraphicsUnit.Pixel);
        using var bodyFont = new Font(fontFamily, 12f, FontStyle.Regular, GraphicsUnit.Pixel);
        using var titleBrush = new SolidBrush(Color.FromArgb(235, 235, 240));
        using var bodyBrush = new SolidBrush(Color.FromArgb(160, 160, 175));
        using var accentBrush = new SolidBrush(Color.FromArgb(251, 191, 36));

        var centerRect = new RectangleF(36, 42, width - 72, height - 84);
        var format = new StringFormat
        {
            Alignment = StringAlignment.Center,
            LineAlignment = StringAlignment.Center,
            Trimming = StringTrimming.EllipsisWord,
        };

        var accentRect = new RectangleF(centerRect.X, centerRect.Y - 30, centerRect.Width, 20);
        graphics.DrawString("Texture2D", bodyFont, accentBrush, accentRect, format);
        graphics.DrawString(title, titleFont, titleBrush, centerRect, format);

        var subtitleRect = new RectangleF(centerRect.X, centerRect.Y + 30, centerRect.Width, 20);
        var metaRect = new RectangleF(centerRect.X, centerRect.Y + 54, centerRect.Width, 20);
        graphics.DrawString(subtitle, bodyFont, bodyBrush, subtitleRect, format);
        graphics.DrawString(meta, bodyFont, bodyBrush, metaRect, format);

        using var output = new MemoryStream();
        bitmap.Save(output, ImageFormat.Png);
        return output.ToArray();
    }

    private static (int Width, int Height) GetPreviewCanvasSize(ManualTranslationAssetEntry asset)
    {
        var width = asset.Width.GetValueOrDefault(512);
        var height = asset.Height.GetValueOrDefault(512);

        if (width <= 0 || height <= 0)
            return (512, 512);

        var longestEdge = Math.Max(width, height);
        if (longestEdge <= 768)
            return (width, height);

        var scale = 768d / longestEdge;
        return (
            Math.Max(192, (int)Math.Round(width * scale)),
            Math.Max(192, (int)Math.Round(height * scale)));
    }

    private async Task<byte[]> DecodeTextureAssetToPngAsync(Game game, ManualTranslationAssetEntry asset, CancellationToken ct)
    {
        return await Task.Run(() =>
        {
            var manager = new AssetsManager();
            try
            {
                using var tpkStream = new MemoryStream(ClassDataTpk.Value);
                manager.LoadClassPackage(tpkStream);
                manager.UseTemplateFieldCache = true;
                manager.UseMonoTemplateFieldCache = true;

                return asset.StorageKind switch
                {
                    ManualTranslationAssetStorageKind.LooseAssetFile => DecodeLooseTextureAssetToPng(manager, game, asset),
                    ManualTranslationAssetStorageKind.BundleEntry => DecodeBundleTextureAssetToPng(manager, game, asset),
                    _ => throw new InvalidOperationException($"Unsupported image storage kind: {asset.StorageKind}")
                };
            }
            finally
            {
                manager.UnloadAll();
            }
        }, ct);
    }

    private static byte[] DecodeLooseTextureAssetToPng(AssetsManager manager, Game game, ManualTranslationAssetEntry asset)
    {
        var assetPath = PathSecurity.SafeJoin(game.GamePath, asset.AssetFile);
        if (!File.Exists(assetPath))
            throw new FileNotFoundException("Texture asset file was not found.", asset.AssetFile);

        AssetsFileInstance? fileInstance = null;
        try
        {
            fileInstance = manager.LoadAssetsFile(assetPath, false);
            TryLoadClassDatabase(manager, fileInstance.file.Metadata.UnityVersion, game.DetectedInfo!.UnityVersion, fileInstance.file.Metadata.TypeTreeEnabled);
            return DecodeTexturePreviewFromInstance(manager, fileInstance, asset);
        }
        finally
        {
            if (fileInstance is not null)
                manager.UnloadAssetsFile(fileInstance);
        }
    }

    private static byte[] DecodeBundleTextureAssetToPng(AssetsManager manager, Game game, ManualTranslationAssetEntry asset)
    {
        if (string.IsNullOrWhiteSpace(asset.BundleEntry))
            throw new InvalidOperationException("Texture bundle entry is missing.");

        var bundlePath = PathSecurity.SafeJoin(game.GamePath, asset.AssetFile);
        if (!File.Exists(bundlePath))
            throw new FileNotFoundException("Texture bundle file was not found.", asset.AssetFile);

        BundleFileInstance? bundleInstance = null;
        AssetsFileInstance? fileInstance = null;
        try
        {
            bundleInstance = manager.LoadBundleFile(bundlePath, true);
            var entryIndex = bundleInstance.file.BlockAndDirInfo.DirectoryInfos
                .FindIndex(info => info.Name.Equals(asset.BundleEntry, StringComparison.OrdinalIgnoreCase));
            if (entryIndex < 0)
                throw new InvalidOperationException($"Bundle entry was not found: {asset.BundleEntry}");

            fileInstance = manager.LoadAssetsFileFromBundle(bundleInstance, entryIndex, false);
            TryLoadClassDatabase(manager, fileInstance.file.Metadata.UnityVersion, game.DetectedInfo!.UnityVersion, fileInstance.file.Metadata.TypeTreeEnabled);
            return DecodeTexturePreviewFromInstance(manager, fileInstance, asset);
        }
        finally
        {
            if (fileInstance is not null)
                manager.UnloadAssetsFile(fileInstance);
            if (bundleInstance is not null)
                manager.UnloadBundleFile(bundleInstance);
        }
    }

    private static byte[] DecodeTexturePreviewFromInstance(
        AssetsManager manager,
        AssetsFileInstance fileInstance,
        ManualTranslationAssetEntry asset)
    {
        var assetInfo = fileInstance.file.Metadata.AssetInfos.FirstOrDefault(info => info.PathId == asset.PathId);
        if (assetInfo is null)
            throw new InvalidOperationException("Texture asset entry was not found in the assets file.");

        var baseField = manager.GetBaseField(fileInstance, assetInfo);
        if (baseField.IsDummy)
            throw new InvalidOperationException("Texture asset could not be deserialized.");

        var texture = TextureFile.ReadTextureFile(baseField);
        var pictureData = texture.FillPictureData(fileInstance);
        if (pictureData is null || pictureData.Length == 0)
            throw new InvalidOperationException("Texture image data could not be read.");

        using var output = new MemoryStream();
        if (!texture.DecodeTextureImage(pictureData, output, ImageExportType.Png))
            throw new InvalidOperationException("Texture format is not supported for preview export.");
        return output.ToArray();
    }

    private static void TryLoadClassDatabase(AssetsManager manager, string unityVersion, string fallbackUnityVersion, bool hasTypeTree)
    {
        try
        {
            manager.LoadClassDatabaseFromPackage(unityVersion);
        }
        catch
        {
            if (!hasTypeTree)
                manager.LoadClassDatabaseFromPackage(fallbackUnityVersion);
        }
    }
}
