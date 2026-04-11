using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using XUnityToolkit_WebUI.Infrastructure;
using XUnityToolkit_WebUI.Models;

namespace XUnityToolkit_WebUI.Services;

public sealed partial class WebImageSearchService(
    IHttpClientFactory httpClientFactory,
    GameImageService imageService,
    ILogger<WebImageSearchService> logger)
{
    // Bing: extract <a class="iusc" m='JSON'> or m="JSON"
    [GeneratedRegex("<a[^>]+class=\"iusc\"[^>]+m='([^']+)'", RegexOptions.Compiled)]
    private static partial Regex BingIuscSingleQuoteRegex();

    [GeneratedRegex("<a[^>]+class=\"iusc\"[^>]+m=\"([^\"]+)\"", RegexOptions.Compiled)]
    private static partial Regex BingIuscDoubleQuoteRegex();

    // Google: extract ["url", width, height] tuples (skip encrypted-tbn0 thumbnails)
    [GeneratedRegex("\\[\"(https?://(?!encrypted-tbn0)[^\\\\\"]+)\",(\\d+),(\\d+)\\]", RegexOptions.Compiled)]
    private static partial Regex GoogleImageTupleRegex();

    // Google: extract thumbnail URLs
    [GeneratedRegex("\"(https?://encrypted-tbn0\\.gstatic\\.com/images\\?[^\"]+)\"", RegexOptions.Compiled)]
    private static partial Regex GoogleThumbRegex();

    public async Task<List<WebImageResult>> SearchAsync(
        string query, string engine, string sizeFilter, string mode,
        CancellationToken ct = default)
    {
        var suffix = mode switch
        {
            "icon" => " game icon",
            "background" => " game screenshot wallpaper",
            _ => " game cover art"
        };
        var fullQuery = query.Trim() + suffix;

        // Resolve "auto" filter based on mode
        var resolvedFilter = sizeFilter;
        if (string.IsNullOrEmpty(resolvedFilter) || resolvedFilter == "auto")
            resolvedFilter = mode switch
            {
                "icon" => "icon-auto",
                "background" => "bg-auto",
                _ => "cover-auto"
            };

        try
        {
            return engine.Equals("Google", StringComparison.OrdinalIgnoreCase)
                ? await SearchGoogleAsync(fullQuery, resolvedFilter, ct)
                : await SearchBingAsync(fullQuery, resolvedFilter, ct);
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new HttpRequestException("搜索超时，请稍后重试");
        }
    }

    public async Task SelectAsCoverAsync(string gameId, string imageUrl, CancellationToken ct = default)
    {
        var (bytes, contentType) = await DownloadImageAsync(imageUrl, ct);
        await imageService.SaveCoverFromWebSearchAsync(gameId, bytes, contentType, imageUrl, ct);
    }

    public async Task SelectAsBackgroundAsync(string gameId, string imageUrl, CancellationToken ct = default)
    {
        var (bytes, contentType) = await DownloadImageAsync(imageUrl, ct);
        await imageService.SaveBackgroundFromWebSearchAsync(gameId, bytes, contentType, imageUrl, ct);
    }

    public async Task SelectAsIconAsync(string gameId, string imageUrl, CancellationToken ct = default)
    {
        var (bytes, _) = await DownloadImageAsync(imageUrl, ct);
        using var ms = new MemoryStream(bytes);
        await imageService.SaveCustomIconFromUploadAsync(gameId, ms, ct);
    }

    private async Task<(byte[] Bytes, string ContentType)> DownloadImageAsync(string imageUrl, CancellationToken ct)
    {
        try
        {
            PathSecurity.ValidateExternalUrl(imageUrl);
        }
        catch (ArgumentException ex)
        {
            throw new InvalidDataException(ex.Message, ex);
        }

        var client = httpClientFactory.CreateClient("ExternalDownload");
        using var request = new HttpRequestMessage(HttpMethod.Get, imageUrl);
        HttpResponseMessage response;
        try
        {
            response = await PathSecurity.SendWithValidatedRedirectsAsync(
                client,
                request,
                HttpCompletionOption.ResponseHeadersRead,
                ct);
        }
        catch (ArgumentException ex)
        {
            throw new InvalidDataException(ex.Message, ex);
        }

        using (response)
        {
            response.EnsureSuccessStatusCode();

            var contentType = response.Content.Headers.ContentType?.MediaType ?? "image/jpeg";
            if (!GameImageService.IsAllowedContentType(contentType))
                throw new HttpRequestException($"不支持的图片格式: {contentType}");

            try
            {
                var bytes = await PathSecurity.ReadBytesWithLimitAsync(
                    response.Content,
                    GameImageService.MaxDownloadedImageBytes,
                    ct);
                return (bytes, contentType);
            }
            catch (InvalidDataException ex)
            {
                throw new InvalidDataException("图片文件不能超过 10 MB。", ex);
            }
        }
    }

    // ===== Bing =====

    private async Task<List<WebImageResult>> SearchBingAsync(
        string query, string sizeFilter, CancellationToken ct)
    {
        var qft = BuildBingQft(sizeFilter);
        var url = $"https://www.bing.com/images/async?q={Uri.EscapeDataString(query)}&first=1&count=35&qft={Uri.EscapeDataString(qft)}&mmasync=1";

        var client = httpClientFactory.CreateClient("WebImageSearch");
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Add("Cookie", "SRCHHPGUSR=ADLT=OFF");
        request.Headers.Add("Referer", "https://www.bing.com/");

        var response = await client.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();

        var html = await response.Content.ReadAsStringAsync(ct);
        return ParseBingResults(html);
    }

    private static string BuildBingQft(string sizeFilter) => sizeFilter switch
    {
        "cover-auto" => "+filterui:imagesize-large+filterui:aspect-tall",
        "icon-auto" => "+filterui:imagesize-small+filterui:aspect-square",
        "bg-auto" => "+filterui:imagesize-wallpaper+filterui:aspect-wide",
        "large" => "+filterui:imagesize-large",
        "medium" => "+filterui:imagesize-medium",
        "small" => "+filterui:imagesize-small",
        "square" => "+filterui:aspect-square",
        "tall" => "+filterui:aspect-tall",
        "wallpaper" => "+filterui:imagesize-wallpaper",
        _ => ""
    };

    private List<WebImageResult> ParseBingResults(string html)
    {
        var results = new List<WebImageResult>();

        // Try single-quote variant first, then double-quote
        var matches = BingIuscSingleQuoteRegex().Matches(html);
        if (matches.Count == 0)
            matches = BingIuscDoubleQuoteRegex().Matches(html);

        foreach (Match match in matches)
        {
            try
            {
                var mValue = WebUtility.HtmlDecode(match.Groups[1].Value);
                using var doc = JsonDocument.Parse(mValue);
                var root = doc.RootElement;

                var murl = root.TryGetProperty("murl", out var murlProp) ? murlProp.GetString() : null;
                var turl = root.TryGetProperty("turl", out var turlProp) ? turlProp.GetString() : null;
                var title = root.TryGetProperty("t", out var tProp) ? tProp.GetString() : null;

                if (string.IsNullOrEmpty(murl)) continue;

                results.Add(new WebImageResult
                {
                    FullUrl = murl,
                    ThumbUrl = turl ?? murl,
                    Title = title,
                    Width = null,
                    Height = null
                });
            }
            catch (JsonException ex)
            {
                logger.LogDebug(ex, "跳过无法解析的 Bing 图片结果");
            }
        }

        return results;
    }

    // ===== Google =====

    private async Task<List<WebImageResult>> SearchGoogleAsync(
        string query, string sizeFilter, CancellationToken ct)
    {
        var tbs = BuildGoogleTbs(sizeFilter);
        var url = $"https://www.google.com/search?tbm=isch&q={Uri.EscapeDataString(query)}&safe=off";
        if (!string.IsNullOrEmpty(tbs))
            url += $"&tbs={Uri.EscapeDataString(tbs)}";

        var client = httpClientFactory.CreateClient("WebImageSearch");
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Add("Cookie", "CONSENT=YES+cb.20240101-00-p0.en+FX+999");
        request.Headers.Add("Referer", "https://www.google.com/");

        var response = await client.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();

        var html = await response.Content.ReadAsStringAsync(ct);
        return ParseGoogleResults(html);
    }

    private static string BuildGoogleTbs(string sizeFilter) => sizeFilter switch
    {
        "cover-auto" => "isz:l",
        "icon-auto" => "isz:s",
        "bg-auto" => "isz:lt,islt:2mp",
        "large" => "isz:l",
        "medium" => "isz:m",
        "small" => "isz:s",
        "square" => "isz:m",     // Google has no aspect filter; use medium for square-ish
        "tall" => "isz:l",       // No tall filter; use large
        "wallpaper" => "isz:lt,islt:2mp",
        _ => ""
    };

    private List<WebImageResult> ParseGoogleResults(string html)
    {
        var results = new List<WebImageResult>();

        // Collect thumbnail URLs for pairing
        var thumbMatches = GoogleThumbRegex().Matches(html);
        var thumbsSeen = new HashSet<string>();
        var thumbs = new List<string>();
        foreach (Match m in thumbMatches)
        {
            var thumbUrl = m.Groups[1].Value.Replace("\\u003d", "=").Replace("\\u0026", "&");
            if (thumbsSeen.Add(thumbUrl))
                thumbs.Add(thumbUrl);
        }

        // Extract full-size image tuples: ["url", width, height]
        var tupleMatches = GoogleImageTupleRegex().Matches(html);
        var idx = 0;
        foreach (Match match in tupleMatches)
        {
            var fullUrl = match.Groups[1].Value
                .Replace("\\u003d", "=")
                .Replace("\\u0026", "&")
                .Replace("\\/", "/");

            if (!int.TryParse(match.Groups[2].Value, out var width)) continue;
            if (!int.TryParse(match.Groups[3].Value, out var height)) continue;

            // Skip very small images that are likely UI elements
            if (width < 50 || height < 50) continue;

            var thumbUrl = idx < thumbs.Count ? thumbs[idx] : fullUrl;
            idx++;

            results.Add(new WebImageResult
            {
                FullUrl = fullUrl,
                ThumbUrl = thumbUrl,
                Width = width,
                Height = height
            });

            if (results.Count >= 40) break;
        }

        return results;
    }
}
