namespace XUnityToolkit_WebUI.Services;

public sealed class MirrorProbeService(
    IHttpClientFactory httpClientFactory,
    ILogger<MirrorProbeService> logger)
{
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(8);

    /// <summary>
    /// Probes GitHub direct connection. Returns true if mirror should be used
    /// (direct is slow or unavailable), false if direct connection is acceptable.
    /// </summary>
    public async Task<bool> ShouldUseMirrorAsync(string directUrl, CancellationToken ct)
    {
        try
        {
            var client = httpClientFactory.CreateClient("GitHub");
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(ProbeTimeout);

            using var request = new HttpRequestMessage(HttpMethod.Head, directUrl);
            using var response = await client.SendAsync(
                request, HttpCompletionOption.ResponseHeadersRead, cts.Token);
            return false; // Direct connection works within timeout
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            logger.LogInformation("GitHub 连接测试超时 ({Seconds}s)，将使用镜像",
                ProbeTimeout.TotalSeconds);
            return true;
        }
        catch (HttpRequestException ex)
        {
            logger.LogInformation(ex, "GitHub 连接测试失败，将使用镜像");
            return true;
        }
    }

    public static string BuildMirrorUrl(string originalUrl, string mirrorBase)
    {
        var normalizedBase = mirrorBase.TrimEnd('/') + "/";
        var mirrorHost = new Uri(normalizedBase).Host;
        if (originalUrl.Contains(mirrorHost, StringComparison.OrdinalIgnoreCase))
            return originalUrl; // Already a mirror URL
        return normalizedBase + originalUrl;
    }
}
