using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using XUnityToolkit_WebUI.Infrastructure;
using XUnityToolkit_WebUI.Models;

namespace XUnityToolkit_WebUI.Services;

public sealed class GitHubReleaseService(
    IHttpClientFactory httpClientFactory,
    AppDataPaths paths,
    AppSettingsService settingsService,
    MirrorProbeService mirrorProbeService,
    ILogger<GitHubReleaseService> logger)
{
    private readonly ConcurrentDictionary<string, (DateTime CachedAt, List<GitHubRelease> Releases)> _cache = [];
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(5);

    private const int MaxRetries = 3;

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
            logger.LogWarning("GitHub API 请求频率超限: {Key}", cacheKey);
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
        string url, string fileName,
        IProgress<DownloadProgress>? progress = null,
        CancellationToken ct = default)
    {
        var destPath = Path.Combine(paths.CacheDirectory, fileName);

        // Return cached file if exists
        if (File.Exists(destPath))
        {
            logger.LogInformation("使用缓存文件: {Path}", destPath);
            progress?.Report(new DownloadProgress(100));
            return destPath;
        }

        logger.LogInformation("开始下载: {Url}", url);

        var settings = await settingsService.GetAsync(ct);
        var mirrorBase = string.IsNullOrWhiteSpace(settings.GhMirrorUrl) ? null : settings.GhMirrorUrl;

        var tmpPath = destPath + ".tmp";
        string? mirrorUrl = mirrorBase != null ? MirrorProbeService.BuildMirrorUrl(url, mirrorBase) : null;
        var useMirror = mirrorBase != null && await mirrorProbeService.ShouldUseMirrorAsync(url, ct);

        if (useMirror)
            logger.LogInformation("GitHub 直连较慢，使用镜像加速: {Mirror}", mirrorUrl);

        Exception? lastException = null;

        for (int attempt = 1; attempt <= MaxRetries; attempt++)
        {
            ct.ThrowIfCancellationRequested();

            if (attempt > 1)
            {
                var delaySeconds = (int)Math.Pow(2, attempt - 2); // 1s, 2s
                progress?.Report(new DownloadProgress(0, RetryMessage: $"正在重试 ({attempt}/{MaxRetries})..."));
                await Task.Delay(TimeSpan.FromSeconds(delaySeconds), ct);
            }

            var attemptUrl = (attempt == 1 && !useMirror) ? url : (mirrorUrl ?? url);

            try
            {
                await DownloadStreamAsync(attemptUrl, tmpPath, mirrorBase, progress, ct);
                File.Move(tmpPath, destPath, overwrite: true);
                progress?.Report(new DownloadProgress(100));
                return destPath;
            }
            catch (OperationCanceledException)
            {
                TryDeleteFile(tmpPath);
                throw;
            }
            catch (Exception ex)
            {
                lastException = ex;
                logger.LogWarning(ex, "下载失败 ({Attempt}/{Max}): {Url}",
                    attempt, MaxRetries, attemptUrl);
                TryDeleteFile(tmpPath);

                if (attempt == 1 && !useMirror && mirrorUrl != null)
                {
                    useMirror = true;
                    progress?.Report(new DownloadProgress(0,
                        RetryMessage: "GitHub 连接失败，切换至镜像源..."));
                }
            }
        }

        throw new HttpRequestException(
            $"下载失败（已重试 {MaxRetries} 次）：{lastException?.Message}", lastException);
    }

    private async Task DownloadStreamAsync(
        string url, string tmpPath, string? mirrorBase,
        IProgress<DownloadProgress>? progress,
        CancellationToken ct)
    {
        var client = CreateClientForUrl(url, mirrorBase);

        using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();

        var totalBytes = response.Content.Headers.ContentLength ?? -1;
        await using var contentStream = await response.Content.ReadAsStreamAsync(ct);
        await using var fileStream = new FileStream(tmpPath, FileMode.Create, FileAccess.Write, FileShare.None);

        var buffer = new byte[81920];
        long totalRead = 0;
        int bytesRead;

        var speedWatch = Stopwatch.StartNew();
        long windowBytes = 0;
        double smoothedBps = 0;
        var lastReport = DateTime.UtcNow;

        while ((bytesRead = await contentStream.ReadAsync(buffer, ct)) > 0)
        {
            await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), ct);
            totalRead += bytesRead;
            windowBytes += bytesRead;

            var percent = totalBytes > 0 ? (int)(totalRead * 100 / totalBytes) : 0;

            // Report speed every 500ms
            var now = DateTime.UtcNow;
            if ((now - lastReport).TotalMilliseconds >= 500 && speedWatch.Elapsed.TotalSeconds > 0.1)
            {
                var currentBps = windowBytes / speedWatch.Elapsed.TotalSeconds;
                smoothedBps = smoothedBps < 1 ? currentBps : smoothedBps * 0.7 + currentBps * 0.3;

                progress?.Report(new DownloadProgress(percent, SpeedFormatted: FormatSpeed(smoothedBps)));

                windowBytes = 0;
                speedWatch.Restart();
                lastReport = now;
            }
        }
    }

    private HttpClient CreateClientForUrl(string url, string? mirrorBase)
    {
        if (mirrorBase != null)
        {
            var mirrorHost = new Uri(mirrorBase.TrimEnd('/') + "/").Host;
            if (url.Contains(mirrorHost, StringComparison.OrdinalIgnoreCase))
                return httpClientFactory.CreateClient("Mirror");
        }
        return httpClientFactory.CreateClient("GitHub");
    }

    private static string FormatSpeed(double bytesPerSecond) =>
        bytesPerSecond switch
        {
            >= 1_048_576 => $"{bytesPerSecond / 1_048_576:F1} MB/s",
            >= 1024 => $"{bytesPerSecond / 1024:F0} KB/s",
            _ => $"{bytesPerSecond:F0} B/s"
        };

    private static void TryDeleteFile(string path)
    {
        try { File.Delete(path); } catch { /* best effort */ }
    }
}
