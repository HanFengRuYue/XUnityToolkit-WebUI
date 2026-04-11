using System.Text;
using Microsoft.Extensions.Logging;
using XUnityToolkit_WebUI.Infrastructure;
using Xunit;

namespace XUnityToolkit_WebUI.Tests.Infrastructure;

public sealed class FileLoggerProviderTests
{
    [Fact]
    public void ExportSessionLog_ShouldIncludeWholeCurrentSessionBeyondRingBuffer()
    {
        var tempDir = CreateTempDirectory();
        try
        {
            using var provider = new FileLoggerProvider(tempDir, LogLevel.Debug);

            for (var i = 0; i < 600; i++)
                provider.WriteEntry("Tests.Log", LogLevel.Information, $"entry {i:000}", null);

            using var snapshot = provider.ExportSessionLog();
            var content = ReadSnapshot(snapshot);

            Assert.Contains("entry 000", content);
            Assert.Contains("entry 599", content);

            var recent = provider.GetRecentEntries(600);
            Assert.Equal(500, recent.Length);
            Assert.Equal("entry 100", recent[0].Message);
            Assert.Equal("entry 599", recent[^1].Message);
        }
        finally
        {
            TryDeleteDirectory(tempDir);
        }
    }

    [Fact]
    public void ExportSessionLog_ShouldPreserveMultilineExceptionText()
    {
        var tempDir = CreateTempDirectory();
        try
        {
            using var provider = new FileLoggerProvider(tempDir, LogLevel.Debug);
            var exception = new InvalidOperationException("outer failure", new ArgumentException("inner failure"));

            provider.WriteEntry("Tests.Log", LogLevel.Error, "log with exception", exception);

            using var snapshot = provider.ExportSessionLog();
            var content = ReadSnapshot(snapshot);

            Assert.Contains("log with exception", content);
            Assert.Contains("Exception: System.InvalidOperationException: outer failure", content);
            Assert.Contains("System.ArgumentException: inner failure", content);
        }
        finally
        {
            TryDeleteDirectory(tempDir);
        }
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), $"XUnityToolkit-Logs-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }

    private static string ReadSnapshot(MemoryStream snapshot)
    {
        snapshot.Position = 0;
        return Encoding.UTF8.GetString(snapshot.ToArray()).TrimStart('\uFEFF');
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
        }
        catch
        {
            // Best effort cleanup for temporary test output.
        }
    }
}
