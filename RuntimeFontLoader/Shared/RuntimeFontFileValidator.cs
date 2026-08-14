using System;
using System.IO;
using System.Security.Cryptography;

namespace XUnityToolkit.RuntimeFontLoader
{
    internal static class RuntimeFontFileValidator
    {
        internal static bool TryResolveManagedFontPath(
            string gameRoot,
            string configuredPath,
            string expectedSha256,
            out string resolvedPath,
            out string error)
        {
            resolvedPath = null;
            error = null;
            if (string.IsNullOrEmpty(configuredPath) || configuredPath.Trim().Length == 0)
            {
                error = "未配置运行时字体路径。";
                return false;
            }

            var fontRoot = Path.GetFullPath(Path.Combine(Path.Combine(gameRoot, "BepInEx"), "Font"));
            var candidate = Path.GetFullPath(Path.Combine(gameRoot, configuredPath.Replace('/', Path.DirectorySeparatorChar)));
            var rootPrefix = fontRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                + Path.DirectorySeparatorChar;
            if (!candidate.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase))
            {
                error = "字体路径不在 BepInEx/Font 目录内。";
                return false;
            }

            var extension = Path.GetExtension(candidate);
            if (!string.Equals(extension, ".ttf", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(extension, ".otf", StringComparison.OrdinalIgnoreCase))
            {
                error = "运行时字体只允许 TTF/OTF 文件。";
                return false;
            }
            if (!File.Exists(candidate))
            {
                error = "运行时字体文件不存在。";
                return false;
            }
            if (ContainsReparsePoint(fontRoot, candidate))
            {
                error = "字体路径包含重解析点。";
                return false;
            }
            if (!HasSupportedFontMagic(candidate))
            {
                error = "字体文件头无效。";
                return false;
            }

            if (!string.IsNullOrEmpty(expectedSha256) && expectedSha256.Trim().Length > 0)
            {
                var actualHash = ComputeSha256(candidate);
                if (!string.Equals(actualHash, expectedSha256.Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    error = "字体文件 SHA-256 与安装清单不一致。";
                    return false;
                }
            }

            resolvedPath = candidate;
            return true;
        }

        internal static bool ContainsReparsePoint(string root, string file)
        {
            var current = new FileInfo(file);
            if ((current.Attributes & FileAttributes.ReparsePoint) != 0)
                return true;

            var directory = current.Directory;
            var normalizedRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            while (directory != null)
            {
                if ((directory.Attributes & FileAttributes.ReparsePoint) != 0)
                    return true;
                if (string.Equals(directory.FullName.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                    normalizedRoot, StringComparison.OrdinalIgnoreCase))
                    break;
                directory = directory.Parent;
            }
            return false;
        }

        internal static bool HasSupportedFontMagic(string path)
        {
            var magic = new byte[4];
            using (var stream = File.OpenRead(path))
            {
                if (stream.Read(magic, 0, magic.Length) != magic.Length)
                    return false;
            }
            return (magic[0] == 0x00 && magic[1] == 0x01 && magic[2] == 0x00 && magic[3] == 0x00)
                || (magic[0] == 0x4F && magic[1] == 0x54 && magic[2] == 0x54 && magic[3] == 0x4F);
        }

        internal static string ComputeSha256(string path)
        {
            using (var sha = SHA256.Create())
            using (var stream = File.OpenRead(path))
                return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", string.Empty);
        }
    }
}
