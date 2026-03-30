using System.Net;

namespace XUnityToolkit_WebUI.Infrastructure;

public static class PathSecurity
{
    public static string SafeJoin(string root, string relativePath)
    {
        var normalizedRoot = Path.GetFullPath(root);
        // Ensure root ends with separator to prevent prefix-match bypass
        // (e.g., root="C:\data" must not match "C:\data-sibling\file")
        if (!normalizedRoot.EndsWith(Path.DirectorySeparatorChar))
            normalizedRoot += Path.DirectorySeparatorChar;
        var full = Path.GetFullPath(Path.Combine(normalizedRoot, relativePath));
        if (!full.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase))
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
            // IPv4-mapped IPv6 addresses (e.g. ::ffff:127.0.0.1) bypass IPv4 checks — unwrap first
            if (ip.IsIPv4MappedToIPv6)
                ip = ip.MapToIPv4();

            var bytes = ip.GetAddressBytes();
            var isPrivate = ip.AddressFamily switch
            {
                System.Net.Sockets.AddressFamily.InterNetwork => IsPrivateIPv4(bytes),
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

    private static bool IsPrivateIPv4(byte[] bytes) =>
        bytes[0] == 10 ||                                        // 10.0.0.0/8
        (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31) || // 172.16.0.0/12
        (bytes[0] == 192 && bytes[1] == 168) ||                  // 192.168.0.0/16
        (bytes[0] == 169 && bytes[1] == 254) ||                  // 169.254.0.0/16 (link-local)
        bytes[0] == 127;                                          // 127.0.0.0/8
}
