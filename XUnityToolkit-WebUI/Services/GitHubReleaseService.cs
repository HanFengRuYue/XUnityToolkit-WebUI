using System.Collections.Concurrent;
using System.Text.Json;
using XUnityToolkit_WebUI.Infrastructure;
using XUnityToolkit_WebUI.Models;

namespace XUnityToolkit_WebUI.Services;

public sealed class GitHubReleaseService(
    IHttpClientFactory httpClientFactory,
    AppDataPaths paths,
    ILogger<GitHubReleaseService> logger)
{
    private readonly ConcurrentDictionary<string, (DateTime CachedAt, List<GitHubRelease> Releases)> _cache = [];
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(5);

    public async Task<List<GitHubRelease>> GetReleasesAsync(
        string owner, string repo, CancellationToken ct = default)
    {
        var cacheKey = $"{owner}/{repo}";
        if (_cache.TryGetValue(cacheKey, out var cached) &&
            DateTime.UtcNow - cached.CachedAt < CacheDuration)
        {
            return cached.Releases;
        }

        var client = httpClientFactory.CreateClient("GitHub");
        var response = await client.GetAsync($"repos/{owner}/{repo}/releases?per_page=20", ct);

        if (response.StatusCode == System.Net.HttpStatusCode.Forbidden ||
            response.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
        {
            logger.LogWarning("GitHub API rate limit hit for {Key}", cacheKey);
            if (_cache.TryGetValue(cacheKey, out var stale))
                return stale.Releases;
            throw new HttpRequestException("GitHub API rate limit exceeded. Please try again later.");
        }

        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync(ct);
        var releases = JsonSerializer.Deserialize<List<GitHubRelease>>(json) ?? [];

        _cache[cacheKey] = (DateTime.UtcNow, releases);
        return releases;
    }

    public async Task<GitHubRelease?> GetLatestStableReleaseAsync(
        string owner, string repo, CancellationToken ct = default)
    {
        var releases = await GetReleasesAsync(owner, repo, ct);
        return releases.FirstOrDefault(r => !r.Prerelease);
    }

    public async Task<GitHubRelease?> GetLatestReleaseAsync(
        string owner, string repo, bool includePrerelease = false, CancellationToken ct = default)
    {
        var releases = await GetReleasesAsync(owner, repo, ct);
        return includePrerelease
            ? releases.FirstOrDefault()
            : releases.FirstOrDefault(r => !r.Prerelease);
    }

    public async Task<string> DownloadAssetAsync(
        string url, string fileName, IProgress<int>? progress = null, CancellationToken ct = default)
    {
        var destPath = Path.Combine(paths.CacheDirectory, fileName);

        // Return cached file if exists
        if (File.Exists(destPath))
        {
            logger.LogInformation("Using cached download: {Path}", destPath);
            progress?.Report(100);
            return destPath;
        }

        logger.LogInformation("Downloading {Url} to {Path}", url, destPath);

        // Download to temp file first, then move atomically to avoid corrupt cache
        var tmpPath = destPath + ".tmp";

        try
        {
            var client = httpClientFactory.CreateClient("GitHub");
            using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
            response.EnsureSuccessStatusCode();

            var totalBytes = response.Content.Headers.ContentLength ?? -1;
            await using var contentStream = await response.Content.ReadAsStreamAsync(ct);
            await using var fileStream = new FileStream(tmpPath, FileMode.Create, FileAccess.Write, FileShare.None);

            var buffer = new byte[81920];
            long totalRead = 0;
            int bytesRead;

            while ((bytesRead = await contentStream.ReadAsync(buffer, ct)) > 0)
            {
                await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), ct);
                totalRead += bytesRead;

                if (totalBytes > 0)
                    progress?.Report((int)(totalRead * 100 / totalBytes));
            }
        }
        catch
        {
            // Clean up partial download on failure
            try { File.Delete(tmpPath); } catch { /* best effort */ }
            throw;
        }

        File.Move(tmpPath, destPath, overwrite: true);
        progress?.Report(100);
        return destPath;
    }
}
