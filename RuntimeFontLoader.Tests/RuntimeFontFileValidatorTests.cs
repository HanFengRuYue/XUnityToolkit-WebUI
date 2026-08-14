using System.Security.Cryptography;
using Xunit;

namespace XUnityToolkit.RuntimeFontLoader;

public sealed class RuntimeFontFileValidatorTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"xut-font-validator-{Guid.NewGuid():N}");
    private readonly string _fontRoot;

    public RuntimeFontFileValidatorTests()
    {
        _fontRoot = Path.Combine(_root, "BepInEx", "Font");
        Directory.CreateDirectory(_fontRoot);
    }

    [Theory]
    [InlineData("valid.ttf", new byte[] { 0x00, 0x01, 0x00, 0x00 })]
    [InlineData("valid.otf", new byte[] { 0x4F, 0x54, 0x54, 0x4F })]
    public void Resolve_AcceptsManagedFontWithMatchingHash(string fileName, byte[] magic)
    {
        var path = Path.Combine(_fontRoot, fileName);
        File.WriteAllBytes(path, [.. magic, 0x01, 0x02, 0x03]);
        var hash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));

        var result = RuntimeFontFileValidator.TryResolveManagedFontPath(
            _root, $"BepInEx/Font/{fileName}", hash, out var resolved, out var error);

        Assert.True(result, error);
        Assert.Equal(Path.GetFullPath(path), resolved);
    }

    [Fact]
    public void Resolve_RejectsEscapeInvalidMagicAndHashMismatch()
    {
        var invalid = Path.Combine(_fontRoot, "invalid.ttf");
        File.WriteAllBytes(invalid, "not-a-font"u8.ToArray());
        var valid = Path.Combine(_fontRoot, "valid.ttf");
        File.WriteAllBytes(valid, [0x00, 0x01, 0x00, 0x00, 0x01]);

        Assert.False(RuntimeFontFileValidator.TryResolveManagedFontPath(
            _root, "../outside.ttf", string.Empty, out _, out var escapeError));
        Assert.Contains("BepInEx/Font", escapeError);
        Assert.False(RuntimeFontFileValidator.TryResolveManagedFontPath(
            _root, "BepInEx/Font/invalid.ttf", string.Empty, out _, out var magicError));
        Assert.Contains("文件头", magicError);
        Assert.False(RuntimeFontFileValidator.TryResolveManagedFontPath(
            _root, "BepInEx/Font/valid.ttf", new string('0', 64), out _, out var hashError));
        Assert.Contains("SHA-256", hashError);
    }

    [Fact]
    public void Resolve_RejectsReparsePointWhenSymlinksAreAvailable()
    {
        var outside = Path.Combine(_root, "outside.ttf");
        File.WriteAllBytes(outside, [0x00, 0x01, 0x00, 0x00, 0x01]);
        var link = Path.Combine(_fontRoot, "linked.ttf");
        try
        {
            File.CreateSymbolicLink(link, outside);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or PlatformNotSupportedException)
        {
            return;
        }

        Assert.False(RuntimeFontFileValidator.TryResolveManagedFontPath(
            _root, "BepInEx/Font/linked.ttf", string.Empty, out _, out var error));
        Assert.Contains("重解析点", error);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }
}
