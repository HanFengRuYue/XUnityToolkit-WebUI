using XUnityToolkit_WebUI.Models;
using XUnityToolkit_WebUI.Services;
using Xunit;

namespace XUnityToolkit_WebUI.Tests.Services;

public sealed class UpdateManifestFileSetTests : IDisposable
{
    private readonly string _root;

    public UpdateManifestFileSetTests()
    {
        _root = Path.Combine(Path.GetTempPath(), $"xut-update-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_root);
    }

    [Fact]
    public void ComputeDiff_ShouldTreatMatchingManagedFilesAsUnchanged()
    {
        WriteFile("bundled/llama/version.txt", "b8756");
        WriteFile("bundled/llama/llama-b8756-bin-win-cpu-x64.zip", "cpu-zip");
        WriteFile("bundled/fonts/SourceHanSans_U2018", "slice");
        WriteFile("bundled/fonts/SourceHanSansCN-Regular.ttf", "ttf");
        WriteFile("wwwroot/assets/index.js.gz", "gzipped");
        WriteFile("wwwroot/assets/index.js.br", "brotli");
        WriteFile("Microsoft.Web.WebView2.Core.xml", "<xml />");

        var manifest = BuildManifest(
            ("bundled/llama/version.txt", "bundled-llama"),
            ("bundled/llama/llama-b8756-bin-win-cpu-x64.zip", "bundled-llama"),
            ("bundled/fonts/SourceHanSans_U2018", "bundled-fonts"),
            ("bundled/fonts/SourceHanSansCN-Regular.ttf", "bundled-fonts"),
            ("wwwroot/assets/index.js.gz", "wwwroot"),
            ("wwwroot/assets/index.js.br", "wwwroot"),
            ("Microsoft.Web.WebView2.Core.xml", "app"));

        var diff = UpdateManifestFileSet.ComputeDiff(
            _root,
            manifest,
            preserveCustomLlamaFiles: false,
            hasBundledLlama: true);

        Assert.Empty(diff.ChangedPackages);
        Assert.Equal(0, diff.ChangedFileCount);
        Assert.Empty(diff.DeletedFiles);
    }

    [Fact]
    public void ComputeDiff_ShouldFlagOnlyBundledLlamaWhenVersionMarkerChanges()
    {
        WriteFile("bundled/llama/version.txt", "b8756");
        WriteFile("bundled/fonts/SourceHanSans_U2018", "slice");
        WriteFile("wwwroot/assets/index.js.gz", "gzipped");

        var manifest = BuildManifest(
            ("bundled/llama/version.txt", "bundled-llama"),
            ("bundled/fonts/SourceHanSans_U2018", "bundled-fonts"),
            ("wwwroot/assets/index.js.gz", "wwwroot"));

        WriteFile("bundled/llama/version.txt", "broken");

        var diff = UpdateManifestFileSet.ComputeDiff(
            _root,
            manifest,
            preserveCustomLlamaFiles: false,
            hasBundledLlama: true);

        Assert.Equal(["bundled-llama"], diff.ChangedPackages);
        Assert.Equal(1, diff.ChangedFileCount);
        Assert.Empty(diff.DeletedFiles);
    }

    [Fact]
    public void ComputeDiff_ShouldFlagOnlyWwwrootWhenCompressedAssetChanges()
    {
        WriteFile("bundled/llama/version.txt", "b8756");
        WriteFile("wwwroot/assets/index.js.gz", "gzipped");
        WriteFile("wwwroot/assets/index.js.br", "brotli");

        var manifest = BuildManifest(
            ("bundled/llama/version.txt", "bundled-llama"),
            ("wwwroot/assets/index.js.gz", "wwwroot"),
            ("wwwroot/assets/index.js.br", "wwwroot"));

        WriteFile("wwwroot/assets/index.js.gz", "changed");

        var diff = UpdateManifestFileSet.ComputeDiff(
            _root,
            manifest,
            preserveCustomLlamaFiles: false,
            hasBundledLlama: true);

        Assert.Equal(["wwwroot"], diff.ChangedPackages);
        Assert.Equal(1, diff.ChangedFileCount);
        Assert.Empty(diff.DeletedFiles);
    }

    [Fact]
    public void ComputeDiff_ShouldPreserveBundledLlamaForNoLlamaEdition()
    {
        WriteFile("bundled/llama/version.txt", "local-override");
        WriteFile("wwwroot/index.html", "<html></html>");

        var manifest = BuildManifest(("wwwroot/index.html", "wwwroot"));

        var preservedDiff = UpdateManifestFileSet.ComputeDiff(
            _root,
            manifest,
            preserveCustomLlamaFiles: true,
            hasBundledLlama: false);

        var normalDiff = UpdateManifestFileSet.ComputeDiff(
            _root,
            manifest,
            preserveCustomLlamaFiles: false,
            hasBundledLlama: false);

        Assert.DoesNotContain("bundled/llama/version.txt", preservedDiff.DeletedFiles);
        Assert.Contains("bundled/llama/version.txt", normalDiff.DeletedFiles);
    }

    private UpdateManifest BuildManifest(params (string RelativePath, string Package)[] files)
    {
        var manifestFiles = new Dictionary<string, ManifestFileEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var (relativePath, package) in files)
        {
            var fullPath = ToFullPath(relativePath);
            manifestFiles[relativePath] = new ManifestFileEntry
            {
                Hash = UpdateManifestFileSet.ComputeFileHash(fullPath),
                Size = new FileInfo(fullPath).Length,
                Package = package
            };
        }

        return new UpdateManifest
        {
            Version = "4.8.0",
            Rid = "win-x64",
            Files = manifestFiles
        };
    }

    private void WriteFile(string relativePath, string contents)
    {
        var fullPath = ToFullPath(relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        File.WriteAllText(fullPath, contents);
    }

    private string ToFullPath(string relativePath)
    {
        return Path.Combine(_root, relativePath.Replace('/', Path.DirectorySeparatorChar));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }
}
