using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using XUnityToolkit_WebUI.Infrastructure;
using XUnityToolkit_WebUI.Models;

namespace XUnityToolkit_WebUI.Services;

public sealed class GameImageService(
    IHttpClientFactory httpClientFactory,
    AppDataPaths paths,
    AppSettingsService settingsService,
    ILogger<GameImageService> logger)
{
    private readonly SemaphoreSlim _lock = new(1, 1);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() }
    };

    private static readonly string[] AllowedContentTypes = ["image/jpeg", "image/png", "image/webp"];

    public bool HasCover(string gameId) => File.Exists(paths.CoverFile(gameId));

    public async Task<(byte[] Bytes, string ContentType)?> GetCoverAsync(string gameId, CancellationToken ct = default)
    {
        var coverPath = paths.CoverFile(gameId);
        if (!File.Exists(coverPath))
            return null;

        var metaPath = paths.CoverMetaFile(gameId);
        var contentType = "image/jpeg";
        if (File.Exists(metaPath))
        {
            try
            {
                var metaJson = await File.ReadAllTextAsync(metaPath, ct);
                var meta = JsonSerializer.Deserialize<CoverMeta>(metaJson, JsonOptions);
                if (meta is not null)
                    contentType = meta.ContentType;
            }
            catch { /* use default content type */ }
        }

        var bytes = await File.ReadAllBytesAsync(coverPath, ct);
        return (bytes, contentType);
    }

    public async Task<List<SteamGridDbSearchResult>> SearchGamesAsync(string query, CancellationToken ct = default)
    {
        var apiKey = await GetApiKeyAsync(ct);
        var client = httpClientFactory.CreateClient("SteamGridDB");

        using var request = new HttpRequestMessage(HttpMethod.Get,
            $"search/autocomplete/{Uri.EscapeDataString(query)}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

        var response = await client.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();

        return await ParseSteamGridDbResponse<List<SteamGridDbSearchResult>>(response, ct);
    }

    public async Task<List<SteamGridDbImage>> GetGridsAsync(int steamGridDbGameId, CancellationToken ct = default)
    {
        var apiKey = await GetApiKeyAsync(ct);
        var client = httpClientFactory.CreateClient("SteamGridDB");

        using var request = new HttpRequestMessage(HttpMethod.Get,
            $"grids/game/{steamGridDbGameId}?dimensions=600x900&types=static");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

        var response = await client.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();

        return await ParseSteamGridDbResponse<List<SteamGridDbImage>>(response, ct);
    }

    public async Task SaveCoverFromUrlAsync(string gameId, string imageUrl, int? steamGridDbGameId, CancellationToken ct = default)
    {
        var client = httpClientFactory.CreateClient("SteamGridDB");
        var response = await client.GetAsync(imageUrl, ct);
        response.EnsureSuccessStatusCode();

        var contentType = response.Content.Headers.ContentType?.MediaType ?? "image/jpeg";
        var bytes = await response.Content.ReadAsByteArrayAsync(ct);

        await _lock.WaitAsync(ct);
        try
        {
            Directory.CreateDirectory(paths.CoversDirectory);

            // Write cover file
            var coverPath = paths.CoverFile(gameId);
            var tmpPath = coverPath + ".tmp";
            await File.WriteAllBytesAsync(tmpPath, bytes, ct);
            File.Move(tmpPath, coverPath, overwrite: true);

            // Write meta file
            var meta = new CoverMeta
            {
                ContentType = contentType,
                Source = "steamgriddb",
                SourceUrl = imageUrl,
                SteamGridDbGameId = steamGridDbGameId,
                SavedAt = DateTime.UtcNow
            };
            var metaJson = JsonSerializer.Serialize(meta, JsonOptions);
            var metaTmpPath = paths.CoverMetaFile(gameId) + ".tmp";
            await File.WriteAllTextAsync(metaTmpPath, metaJson, ct);
            File.Move(metaTmpPath, paths.CoverMetaFile(gameId), overwrite: true);

            logger.LogInformation("已保存游戏 {GameId} 的封面图 (来源: steamgriddb)", gameId);
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task SaveCoverFromUploadAsync(string gameId, Stream imageStream, string contentType, CancellationToken ct = default)
    {
        using var ms = new MemoryStream();
        await imageStream.CopyToAsync(ms, ct);
        var bytes = ms.ToArray();

        await _lock.WaitAsync(ct);
        try
        {
            Directory.CreateDirectory(paths.CoversDirectory);

            var coverPath = paths.CoverFile(gameId);
            var tmpPath = coverPath + ".tmp";
            await File.WriteAllBytesAsync(tmpPath, bytes, ct);
            File.Move(tmpPath, coverPath, overwrite: true);

            var meta = new CoverMeta
            {
                ContentType = contentType,
                Source = "upload",
                SavedAt = DateTime.UtcNow
            };
            var metaJson = JsonSerializer.Serialize(meta, JsonOptions);
            var metaTmpPath = paths.CoverMetaFile(gameId) + ".tmp";
            await File.WriteAllTextAsync(metaTmpPath, metaJson, ct);
            File.Move(metaTmpPath, paths.CoverMetaFile(gameId), overwrite: true);

            logger.LogInformation("已保存游戏 {GameId} 的封面图 (来源: 上传)", gameId);
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<bool> DeleteCoverAsync(string gameId, CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            var coverPath = paths.CoverFile(gameId);
            var metaPath = paths.CoverMetaFile(gameId);
            var deleted = false;

            if (File.Exists(coverPath))
            {
                File.Delete(coverPath);
                deleted = true;
            }
            if (File.Exists(metaPath))
                File.Delete(metaPath);

            if (deleted)
                logger.LogInformation("已删除游戏 {GameId} 的封面图", gameId);

            return deleted;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<List<SteamStoreSearchResult>> SearchSteamGamesAsync(string query, CancellationToken ct = default)
    {
        var client = httpClientFactory.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(15);
        var url = $"https://store.steampowered.com/api/storesearch/?term={Uri.EscapeDataString(query)}&l=schinese&cc=cn";

        var response = await client.GetAsync(url, ct);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(json);

        var results = new List<SteamStoreSearchResult>();
        if (doc.RootElement.TryGetProperty("items", out var items))
        {
            foreach (var item in items.EnumerateArray())
            {
                results.Add(new SteamStoreSearchResult
                {
                    Id = item.GetProperty("id").GetInt32(),
                    Name = item.GetProperty("name").GetString() ?? "",
                    TinyImage = item.TryGetProperty("tiny_image", out var img) ? img.GetString() ?? "" : ""
                });
            }
        }

        return results;
    }

    public async Task<bool> SaveCoverFromSteamAsync(string gameId, int steamAppId, CancellationToken ct = default)
    {
        var client = httpClientFactory.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(15);

        // Try vertical cover first (600x900, perfect for 2:3 cards)
        var verticalUrl = $"https://cdn.akamai.steamstatic.com/steam/apps/{steamAppId}/library_600x900.jpg";
        var response = await client.GetAsync(verticalUrl, ct);

        string imageUrl;
        if (response.IsSuccessStatusCode)
        {
            imageUrl = verticalUrl;
        }
        else
        {
            // Fall back to header image (460x215, CSS object-fit:cover handles vertical adaptation)
            response.Dispose();
            var headerUrl = $"https://cdn.akamai.steamstatic.com/steam/apps/{steamAppId}/header.jpg";
            response = await client.GetAsync(headerUrl, ct);
            if (!response.IsSuccessStatusCode)
            {
                response.Dispose();
                return false;
            }
            imageUrl = headerUrl;
        }

        using (response)
        {
            var contentType = response.Content.Headers.ContentType?.MediaType ?? "image/jpeg";
            var bytes = await response.Content.ReadAsByteArrayAsync(ct);

            await _lock.WaitAsync(ct);
            try
            {
                Directory.CreateDirectory(paths.CoversDirectory);

                var coverPath = paths.CoverFile(gameId);
                var tmpPath = coverPath + ".tmp";
                await File.WriteAllBytesAsync(tmpPath, bytes, ct);
                File.Move(tmpPath, coverPath, overwrite: true);

                var meta = new CoverMeta
                {
                    ContentType = contentType,
                    Source = "steam",
                    SourceUrl = imageUrl,
                    SavedAt = DateTime.UtcNow
                };
                var metaJson = JsonSerializer.Serialize(meta, JsonOptions);
                var metaTmpPath = paths.CoverMetaFile(gameId) + ".tmp";
                await File.WriteAllTextAsync(metaTmpPath, metaJson, ct);
                File.Move(metaTmpPath, paths.CoverMetaFile(gameId), overwrite: true);

                logger.LogInformation("已保存游戏 {GameId} 的封面图 (来源: Steam CDN, AppID: {SteamAppId})", gameId, steamAppId);
            }
            finally
            {
                _lock.Release();
            }
        }

        return true;
    }

    public static bool IsAllowedContentType(string contentType) =>
        AllowedContentTypes.Contains(contentType, StringComparer.OrdinalIgnoreCase);

    private async Task<string> GetApiKeyAsync(CancellationToken ct)
    {
        var settings = await settingsService.GetAsync(ct);
        var apiKey = settings.SteamGridDbApiKey;
        if (string.IsNullOrEmpty(apiKey))
            throw new InvalidOperationException("请先在设置中配置 SteamGridDB API Key。");
        return apiKey;
    }

    private static async Task<T> ParseSteamGridDbResponse<T>(HttpResponseMessage response, CancellationToken ct)
    {
        var json = await response.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(json);

        if (doc.RootElement.TryGetProperty("data", out var dataElement))
        {
            return JsonSerializer.Deserialize<T>(dataElement.GetRawText(), JsonOptions)
                   ?? throw new InvalidOperationException("SteamGridDB 返回了空数据。");
        }

        return JsonSerializer.Deserialize<T>(json, JsonOptions)
               ?? throw new InvalidOperationException("无法解析 SteamGridDB 响应。");
    }
}
