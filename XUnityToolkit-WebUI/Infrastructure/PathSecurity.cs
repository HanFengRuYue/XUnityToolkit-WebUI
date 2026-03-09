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
}
