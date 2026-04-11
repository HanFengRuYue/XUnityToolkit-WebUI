using System.IO.Compression;
using System.Net;
using System.Net.Sockets;

namespace XUnityToolkit_WebUI.Infrastructure;

public static class PathSecurity
{
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
                (bytes[0] == 0xFE && (bytes[1] & 0xC0) == 0x80) ||
                ((bytes[0] & 0xFE) == 0xFC),
            _ => false
        };

        if (isPrivate)
            throw new ArgumentException("不允许访问内网地址。");
    }

    private static bool IsPrivateIPv4(byte[] bytes) =>
        bytes[0] == 10 ||
        (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31) ||
        (bytes[0] == 192 && bytes[1] == 168) ||
        (bytes[0] == 169 && bytes[1] == 254) ||
        bytes[0] == 127;
}
