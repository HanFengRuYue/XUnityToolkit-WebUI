using System.Buffers;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;

namespace XUnityToolkit_WebUI.Infrastructure;

public static class PathSecurity
{
    private const int DefaultRedirectLimit = 5;

    public static string SafeJoin(string root, string relativePath)
    {
        var normalizedRoot = Path.GetFullPath(root);
        if (!normalizedRoot.EndsWith(Path.DirectorySeparatorChar))
            normalizedRoot += Path.DirectorySeparatorChar;

        var fullPath = Path.GetFullPath(Path.Combine(normalizedRoot, relativePath));
        if (!fullPath.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Path traversal detected: {relativePath}");

        return fullPath;
    }

    /// <summary>
    /// Validate an external HTTP(S) URL and reject loopback/private-network targets.
    /// </summary>
    public static void ValidateExternalUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            throw new ArgumentException("无效的 URL。");
        if (uri.Scheme is not ("https" or "http"))
            throw new ArgumentException("URL 必须使用 HTTP 或 HTTPS 协议。");

        var host = uri.Host;
        if (host is "localhost" or "127.0.0.1" or "::1" or "0.0.0.0" or "[::]")
            throw new ArgumentException("不允许访问回环地址。");

        if (IPAddress.TryParse(host.Trim('[', ']'), out var parsedIp))
        {
            ValidateNonPrivateIp(parsedIp);
            return;
        }

        if (host.EndsWith(".local", StringComparison.OrdinalIgnoreCase) ||
            host.EndsWith(".internal", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("不允许访问内网地址。");

        IPAddress[] addresses;
        try
        {
            addresses = Dns.GetHostAddresses(host);
        }
        catch (SocketException ex)
        {
            throw new ArgumentException("无法解析 URL 中的主机名。", ex);
        }

        if (addresses.Length == 0)
            throw new ArgumentException("无法解析 URL 中的主机名。");

        foreach (var address in addresses)
            ValidateNonPrivateIp(address);
    }

    public static string PrepareZipExtractionPath(
        string root,
        ZipArchiveEntry entry,
        ref long totalExtractedBytes,
        long maxEntryBytes,
        long maxTotalBytes)
    {
        if (string.IsNullOrEmpty(entry.Name))
            throw new InvalidOperationException("Directory entries cannot be extracted as files.");
        if (entry.Length < 0 || entry.Length > maxEntryBytes)
            throw new InvalidDataException($"ZIP entry is too large: {entry.FullName}");

        try
        {
            checked
            {
                totalExtractedBytes += entry.Length;
            }
        }
        catch (OverflowException ex)
        {
            throw new InvalidDataException("ZIP entry sizes overflowed the extraction budget.", ex);
        }

        if (totalExtractedBytes > maxTotalBytes)
            throw new InvalidDataException("ZIP archive exceeds the allowed extracted size.");

        return SafeJoin(root, entry.FullName.Replace('/', Path.DirectorySeparatorChar));
    }

    public static async Task ExtractZipEntryAsync(
        ZipArchiveEntry entry,
        string destinationPath,
        CancellationToken ct = default)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
        await using var entryStream = entry.Open();
        await using var destinationStream = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None);
        await entryStream.CopyToAsync(destinationStream, ct);
    }

    public static async Task<HttpResponseMessage> SendWithValidatedRedirectsAsync(
        HttpClient client,
        HttpRequestMessage request,
        HttpCompletionOption completionOption = HttpCompletionOption.ResponseHeadersRead,
        CancellationToken ct = default,
        int maxRedirects = DefaultRedirectLimit)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.RequestUri);

        if (request.Content is not null)
            throw new NotSupportedException("Validated redirect handling only supports body-less requests.");
        if (request.Method != HttpMethod.Get && request.Method != HttpMethod.Head)
            throw new NotSupportedException("Validated redirect handling only supports GET/HEAD requests.");
        if (maxRedirects < 0)
            throw new ArgumentOutOfRangeException(nameof(maxRedirects));

        var currentUri = request.RequestUri;

        for (var redirectCount = 0; ; redirectCount++)
        {
            ValidateExternalUrl(currentUri.ToString());

            using var currentRequest = CloneRequestWithoutContent(request, currentUri);
            var response = await client.SendAsync(currentRequest, completionOption, ct);
            if (!IsRedirect(response.StatusCode) || response.Headers.Location is null)
                return response;

            response.Dispose();
            if (redirectCount >= maxRedirects)
                throw new HttpRequestException($"Too many redirects while requesting {request.RequestUri}.");

            currentUri = response.Headers.Location.IsAbsoluteUri
                ? response.Headers.Location
                : new Uri(currentUri, response.Headers.Location);
        }
    }

    public static async Task<byte[]> ReadBytesWithLimitAsync(
        HttpContent content,
        long maxBytes,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(content);
        if (maxBytes <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxBytes));

        if (content.Headers.ContentLength is long contentLength && contentLength > maxBytes)
            throw new InvalidDataException($"Response exceeds the allowed size of {maxBytes} bytes.");

        await using var stream = await content.ReadAsStreamAsync(ct);
        using var output = content.Headers.ContentLength is long length and > 0 and <= int.MaxValue
            ? new MemoryStream((int)length)
            : new MemoryStream();

        var buffer = ArrayPool<byte>.Shared.Rent(81920);
        try
        {
            long totalBytes = 0;
            while (true)
            {
                var read = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), ct);
                if (read == 0)
                    return output.ToArray();

                totalBytes += read;
                if (totalBytes > maxBytes)
                    throw new InvalidDataException($"Response exceeds the allowed size of {maxBytes} bytes.");

                await output.WriteAsync(buffer.AsMemory(0, read), ct);
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private static HttpRequestMessage CloneRequestWithoutContent(HttpRequestMessage request, Uri requestUri)
    {
        var clone = new HttpRequestMessage(request.Method, requestUri)
        {
            Version = request.Version,
            VersionPolicy = request.VersionPolicy,
        };

        foreach (var header in request.Headers)
            clone.Headers.TryAddWithoutValidation(header.Key, header.Value);

        return clone;
    }

    private static bool IsRedirect(HttpStatusCode statusCode) =>
        statusCode is HttpStatusCode.Moved
            or HttpStatusCode.Redirect
            or HttpStatusCode.RedirectMethod
            or HttpStatusCode.TemporaryRedirect
            or HttpStatusCode.PermanentRedirect;

    private static void ValidateNonPrivateIp(IPAddress ip)
    {
        if (ip.IsIPv4MappedToIPv6)
            ip = ip.MapToIPv4();

        var bytes = ip.GetAddressBytes();
        var isPrivate = ip.AddressFamily switch
        {
            AddressFamily.InterNetwork => IsPrivateIPv4(bytes),
            AddressFamily.InterNetworkV6 =>
                IPAddress.IsLoopback(ip) ||
                ip.Equals(IPAddress.IPv6Any) ||
                ip.Equals(IPAddress.IPv6None) ||
                (bytes[0] == 0xFE && (bytes[1] & 0xC0) == 0x80) ||
                ((bytes[0] & 0xFE) == 0xFC) ||
                bytes[0] == 0xFF,
            _ => false
        };

        if (isPrivate)
            throw new ArgumentException("不允许访问内网地址。");
    }

    private static bool IsPrivateIPv4(byte[] bytes) =>
        bytes[0] == 0 ||
        bytes[0] == 10 ||
        (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31) ||
        (bytes[0] == 100 && bytes[1] >= 64 && bytes[1] <= 127) ||
        (bytes[0] == 192 && bytes[1] == 168) ||
        (bytes[0] == 198 && bytes[1] is 18 or 19) ||
        (bytes[0] == 169 && bytes[1] == 254) ||
        (bytes[0] & 0xF0) == 0xE0 ||
        (bytes[0] & 0xF0) == 0xF0 ||
        bytes[0] == 127;
}
