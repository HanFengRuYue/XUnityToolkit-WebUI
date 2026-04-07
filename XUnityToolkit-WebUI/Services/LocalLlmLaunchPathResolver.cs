using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace XUnityToolkit_WebUI.Services;

internal enum LocalLlmLaunchPathStrategy
{
    Relative,
    AbsolutePath,
    ShortPath,
    AliasHardLink,
    AliasSymbolicLink
}

internal sealed record LocalLlmLaunchPathResolution(
    bool Success,
    string LaunchPath,
    string ResolvedFilePath,
    LocalLlmLaunchPathStrategy? Strategy,
    string? Error)
{
    public static LocalLlmLaunchPathResolution FromSuccess(
        string launchPath,
        string resolvedFilePath,
        LocalLlmLaunchPathStrategy strategy) =>
        new(true, launchPath, resolvedFilePath, strategy, null);

    public static LocalLlmLaunchPathResolution FromFailure(string error) =>
        new(false, "", "", null, error);
}

internal static class LocalLlmLaunchPathResolver
{
    private const int InitialPathBufferSize = 260;

    public static LocalLlmLaunchPathResolution Resolve(
        string serverPath,
        string modelPath,
        string launchCacheDirectory,
        Func<string, string?>? shortPathResolver = null,
        Func<string, string, bool>? hardLinkCreator = null,
        Func<string, string, bool>? symbolicLinkCreator = null,
        Func<string, string, bool>? sameFilePredicate = null)
    {
        if (string.IsNullOrWhiteSpace(serverPath))
            return LocalLlmLaunchPathResolution.FromFailure("llama-server 路径无效，无法计算模型启动参数。");

        if (string.IsNullOrWhiteSpace(modelPath))
            return LocalLlmLaunchPathResolution.FromFailure("模型路径为空，无法启动本地 AI。");

        var workingDirectory = Path.GetDirectoryName(serverPath);
        if (string.IsNullOrWhiteSpace(workingDirectory))
            return LocalLlmLaunchPathResolution.FromFailure("llama-server 工作目录无效，无法计算模型启动参数。");

        var fullWorkingDirectory = Path.GetFullPath(workingDirectory);
        var fullModelPath = Path.GetFullPath(modelPath);
        sameFilePredicate ??= PathsReferToSameFile;

        var directPath = NormalizeLaunchPath(Path.GetRelativePath(fullWorkingDirectory, fullModelPath));
        if (IsAsciiSafe(directPath) && CandidateMatches(fullWorkingDirectory, directPath, fullModelPath, sameFilePredicate))
        {
            var strategy = Path.IsPathRooted(directPath)
                ? LocalLlmLaunchPathStrategy.AbsolutePath
                : LocalLlmLaunchPathStrategy.Relative;
            return LocalLlmLaunchPathResolution.FromSuccess(directPath, fullModelPath, strategy);
        }

        shortPathResolver ??= TryGetShortPath;
        var shortPath = shortPathResolver(fullModelPath);
        if (!string.IsNullOrWhiteSpace(shortPath))
        {
            var normalizedShortPath = NormalizeLaunchPath(shortPath);
            if (IsAsciiSafe(normalizedShortPath)
                && File.Exists(normalizedShortPath)
                && CandidateMatches(fullWorkingDirectory, normalizedShortPath, fullModelPath, sameFilePredicate))
            {
                return LocalLlmLaunchPathResolution.FromSuccess(
                    normalizedShortPath,
                    Path.GetFullPath(normalizedShortPath),
                    LocalLlmLaunchPathStrategy.ShortPath);
            }
        }

        if (string.IsNullOrWhiteSpace(launchCacheDirectory))
            return LocalLlmLaunchPathResolution.FromFailure("模型路径包含非 ASCII 字符，且未配置 launch-cache 目录。");

        var fullLaunchCacheDirectory = Path.GetFullPath(launchCacheDirectory);
        try
        {
            Directory.CreateDirectory(fullLaunchCacheDirectory);
        }
        catch
        {
            return LocalLlmLaunchPathResolution.FromFailure("模型路径包含非 ASCII 字符，且无法创建 launch-cache 目录。");
        }

        var aliasPath = GetAliasPath(fullLaunchCacheDirectory, fullModelPath);
        hardLinkCreator ??= TryCreateHardLink;
        symbolicLinkCreator ??= TryCreateSymbolicLink;

        TryDeleteFile(aliasPath);
        if (hardLinkCreator(aliasPath, fullModelPath)
            && TryBuildAliasLaunchPath(fullWorkingDirectory, aliasPath, out var aliasLaunchPath))
        {
            return LocalLlmLaunchPathResolution.FromSuccess(
                aliasLaunchPath,
                aliasPath,
                LocalLlmLaunchPathStrategy.AliasHardLink);
        }

        TryDeleteFile(aliasPath);
        if (symbolicLinkCreator(aliasPath, fullModelPath)
            && TryBuildAliasLaunchPath(fullWorkingDirectory, aliasPath, out aliasLaunchPath))
        {
            return LocalLlmLaunchPathResolution.FromSuccess(
                aliasLaunchPath,
                aliasPath,
                LocalLlmLaunchPathStrategy.AliasSymbolicLink);
        }

        TryDeleteFile(aliasPath);
        return LocalLlmLaunchPathResolution.FromFailure(
            "模型路径包含非 ASCII 字符，且无法创建 ASCII 启动别名。请改用英文文件名，或启用 8.3 短路径后重试。");
    }

    private static bool TryBuildAliasLaunchPath(string workingDirectory, string aliasPath, out string launchPath)
    {
        launchPath = NormalizeLaunchPath(Path.GetRelativePath(workingDirectory, aliasPath));
        return IsAsciiSafe(launchPath) && File.Exists(aliasPath);
    }

    private static bool CandidateMatches(
        string workingDirectory,
        string candidatePath,
        string expectedPath,
        Func<string, string, bool> sameFilePredicate)
    {
        try
        {
            var resolvedCandidate = Path.GetFullPath(candidatePath, workingDirectory);
            return sameFilePredicate(resolvedCandidate, expectedPath);
        }
        catch
        {
            return false;
        }
    }

    private static bool PathsReferToSameFile(string left, string right)
    {
        var canonicalLeft = GetCanonicalPath(left);
        var canonicalRight = GetCanonicalPath(right);
        return StringComparer.OrdinalIgnoreCase.Equals(canonicalLeft, canonicalRight);
    }

    private static string GetCanonicalPath(string path)
    {
        var fullPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        return Path.TrimEndingDirectorySeparator(TryGetLongPath(fullPath) ?? fullPath);
    }

    private static string NormalizeLaunchPath(string path) =>
        path.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);

    private static bool IsAsciiSafe(string value) =>
        !string.IsNullOrWhiteSpace(value) && value.All(ch => ch <= sbyte.MaxValue);

    private static string GetAliasPath(string launchCacheDirectory, string modelPath)
    {
        var normalizedPath = Path.GetFullPath(modelPath).ToUpperInvariant();
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalizedPath))).ToLowerInvariant();
        var extension = Path.GetExtension(modelPath);
        var safeExtension = IsAsciiSafe(extension)
            && extension.All(ch => char.IsLetterOrDigit(ch) || ch is '.' or '_' or '-')
                ? extension.ToLowerInvariant()
                : ".bin";

        return Path.Combine(launchCacheDirectory, $"model-{hash[..24]}{safeExtension}");
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
            // Best effort. Caller will surface a clear error if alias creation still fails.
        }
    }

    private static string? TryGetShortPath(string path)
    {
        if (!File.Exists(path))
            return null;

        var bufferSize = InitialPathBufferSize;
        while (true)
        {
            var buffer = new StringBuilder(bufferSize);
            var written = GetShortPathName(path, buffer, (uint)buffer.Capacity);
            if (written == 0)
                return null;

            if (written < buffer.Capacity)
                return buffer.ToString();

            bufferSize = checked((int)written + 1);
        }
    }

    private static string? TryGetLongPath(string path)
    {
        if (!File.Exists(path) && !Directory.Exists(path))
            return null;

        var bufferSize = InitialPathBufferSize;
        while (true)
        {
            var buffer = new StringBuilder(bufferSize);
            var written = GetLongPathName(path, buffer, (uint)buffer.Capacity);
            if (written == 0)
                return null;

            if (written < buffer.Capacity)
                return buffer.ToString();

            bufferSize = checked((int)written + 1);
        }
    }

    private static bool TryCreateHardLink(string linkPath, string targetPath)
    {
        try
        {
            return CreateHardLink(linkPath, targetPath, IntPtr.Zero);
        }
        catch
        {
            return false;
        }
    }

    private static bool TryCreateSymbolicLink(string linkPath, string targetPath)
    {
        try
        {
            File.CreateSymbolicLink(linkPath, targetPath);
            return true;
        }
        catch
        {
            return false;
        }
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "GetShortPathNameW")]
    private static extern uint GetShortPathName(
        string lpszLongPath,
        StringBuilder lpszShortPath,
        uint cchBuffer);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "GetLongPathNameW")]
    private static extern uint GetLongPathName(
        string lpszShortPath,
        StringBuilder lpszLongPath,
        uint cchBuffer);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "CreateHardLinkW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreateHardLink(
        string lpFileName,
        string lpExistingFileName,
        IntPtr lpSecurityAttributes);
}
