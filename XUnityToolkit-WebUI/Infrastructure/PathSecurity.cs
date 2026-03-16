using System.Net;

namespace XUnityToolkit_WebUI.Infrastructure;

public static class PathSecurity
{
    public static string SafeJoin(string root, string relativePath)
    {
        var full = Path.GetFullPath(Path.Combine(root, relativePath));
        if (!full.StartsWith(Path.GetFullPath(root), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Path traversal detected: {relativePath}");
        return full;
    }

    /// <summary>
    /// Validates that a URL is a safe external HTTP(S) URL (not pointing to localhost or private networks).
    /// </summary>
    public static void ValidateExternalUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            throw new ArgumentException("无效的 URL");
        if (uri.Scheme is not ("https" or "http"))
            throw new ArgumentException("URL 必须使用 HTTP/HTTPS 协议");

        var host = uri.Host;
        // Block loopback
        if (host is "localhost" or "127.0.0.1" or "::1" or "0.0.0.0" or "[::]")
            throw new ArgumentException("不允许访问回环地址");

        // Block private IP ranges
        if (IPAddress.TryParse(host.Trim('[', ']'), out var ip))
        {
            var bytes = ip.GetAddressBytes();
            var isPrivate = ip.AddressFamily switch
            {
                System.Net.Sockets.AddressFamily.InterNetwork =>
                    bytes[0] == 10 ||                                   // 10.0.0.0/8
                    (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31) || // 172.16.0.0/12
                    (bytes[0] == 192 && bytes[1] == 168) ||             // 192.168.0.0/16
                    (bytes[0] == 169 && bytes[1] == 254) ||             // 169.254.0.0/16 (link-local)
                    bytes[0] == 127,                                     // 127.0.0.0/8
                System.Net.Sockets.AddressFamily.InterNetworkV6 =>
                    IPAddress.IsLoopback(ip) ||
                    (bytes[0] == 0xFE && (bytes[1] & 0xC0) == 0x80) || // fe80::/10 (link-local)
                    ((bytes[0] & 0xFE) == 0xFC),                        // fc00::/7 (ULA)
                _ => false
            };
            if (isPrivate)
                throw new ArgumentException("不允许访问内网地址");
        }
        else
        {
            // Hostname-based checks
            if (host.EndsWith(".local", StringComparison.OrdinalIgnoreCase) ||
                host.EndsWith(".internal", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("不允许访问内网地址");
        }
    }
}
