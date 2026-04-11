using System.IO.Compression;
using XUnityToolkit_WebUI.Infrastructure;
using Xunit;

namespace XUnityToolkit_WebUI.Tests.Infrastructure;

public sealed class PathSecurityTests : IDisposable
{
    private readonly string _root;

    public PathSecurityTests()
    {
        _root = Path.Combine(Path.GetTempPath(), $"xut-path-security-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_root);
    }

    [Fact]
    public void SafeJoin_ShouldRejectTraversal()
    {
        Assert.Throws<InvalidOperationException>(() => PathSecurity.SafeJoin(_root, @"..\outside.txt"));
    }

    [Theory]
    [InlineData("http://127.0.0.1")]
    [InlineData("http://[::1]")]
    [InlineData("http://[::ffff:127.0.0.1]")]
    [InlineData("http://10.0.0.1")]
    public void ValidateExternalUrl_ShouldRejectPrivateTargets(string url)
    {
        Assert.Throws<ArgumentException>(() => PathSecurity.ValidateExternalUrl(url));
    }

    [Fact]
    public void PrepareZipExtractionPath_ShouldRejectOversizedEntriesAndArchives()
    {
        using var archive = CreateArchive(("data.txt", new string('a', 16)));
        var entry = archive.GetEntry("data.txt")!;

        long totalExtractedBytes = 0;
        Assert.Throws<InvalidDataException>(() =>
            PathSecurity.PrepareZipExtractionPath(_root, entry, ref totalExtractedBytes, maxEntryBytes: 8, maxTotalBytes: 64));

        totalExtractedBytes = 60;
        Assert.Throws<InvalidDataException>(() =>
            PathSecurity.PrepareZipExtractionPath(_root, entry, ref totalExtractedBytes, maxEntryBytes: 64, maxTotalBytes: 64));
    }

    [Fact]
    public async Task ExtractZipEntryAsync_ShouldWriteContentsToDestination()
    {
        using var archive = CreateArchive(("nested/file.txt", "hello world"));
        var entry = archive.GetEntry("nested/file.txt")!;

        long totalExtractedBytes = 0;
        var destinationPath = PathSecurity.PrepareZipExtractionPath(
            _root,
            entry,
            ref totalExtractedBytes,
            maxEntryBytes: 1024,
            maxTotalBytes: 1024);

        await PathSecurity.ExtractZipEntryAsync(entry, destinationPath);

        Assert.Equal("hello world", await File.ReadAllTextAsync(destinationPath));
    }

    private static ZipArchive CreateArchive(params (string Name, string Content)[] entries)
    {
        var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (name, content) in entries)
            {
                var entry = archive.CreateEntry(name);
                using var writer = new StreamWriter(entry.Open());
                writer.Write(content);
            }
        }

        stream.Position = 0;
        return new ZipArchive(stream, ZipArchiveMode.Read);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }
}
