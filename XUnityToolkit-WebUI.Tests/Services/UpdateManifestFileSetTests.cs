using XUnityToolkit_WebUI.Models;
using XUnityToolkit_WebUI.Services;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
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
        WriteFile("bundled/llama/version.txt", "b10375");
        WriteFile("bundled/llama/llama-b10375-bin-win-cpu-x64.zip", "cpu-zip");
        WriteFile("bundled/fonts/SourceHanSans_U2018", "slice");
        WriteFile("bundled/fonts/SourceHanSansCN-Regular.ttf", "ttf");
        WriteFile("wwwroot/assets/index.js.gz", "gzipped");
        WriteFile("wwwroot/assets/index.js.br", "brotli");
        WriteFile("Microsoft.Web.WebView2.Core.xml", "<xml />");

        var manifest = BuildManifest(
            ("bundled/llama/version.txt", "bundled-llama"),
            ("bundled/llama/llama-b10375-bin-win-cpu-x64.zip", "bundled-llama"),
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
        WriteFile("bundled/llama/version.txt", "b10375");
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
        WriteFile("bundled/llama/version.txt", "b10375");
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

    [Fact]
    public void EnumerateManagedLocalFiles_ShouldUseInventoryForWinUiNativeResources()
    {
        WriteFile("Microsoft.UI.Xaml/Assets/NoiseAsset_256X256_PNG.png", "native-resource");
        WriteFile("custom/user-extension.txt", "preserve-me");
        WriteFile("user-note.txt", "preserve-me-too");
        WriteFile("wwwroot/index.html", "<html></html>");
        WriteInventory(
            "Microsoft.UI.Xaml/Assets/NoiseAsset_256X256_PNG.png",
            UpdateManifestFileSet.AppFileInventoryName);

        var files = UpdateManifestFileSet.EnumerateManagedLocalFiles(
            _root,
            preserveCustomLlamaFiles: false,
            hasBundledLlama: true);

        Assert.Contains("Microsoft.UI.Xaml/Assets/NoiseAsset_256X256_PNG.png", files.Keys);
        Assert.Contains(UpdateManifestFileSet.AppFileInventoryName, files.Keys);
        Assert.Contains("wwwroot/index.html", files.Keys);
        Assert.DoesNotContain("custom/user-extension.txt", files.Keys);
        Assert.DoesNotContain("user-note.txt", files.Keys);
    }

    [Fact]
    public void EnumerateManagedLocalFiles_ShouldUseLegacySafeRootsWithoutInventory()
    {
        WriteFile("runtimes/win-x64/native/WebView2Loader.dll", "loader");
        WriteFile("Microsoft.UI.Xaml/Assets/untracked.png", "unknown-nested-file");
        WriteFile("XUnityToolkit-WebUI.exe", "app");

        var files = UpdateManifestFileSet.EnumerateManagedLocalFiles(
            _root,
            preserveCustomLlamaFiles: false,
            hasBundledLlama: true);

        Assert.Contains("runtimes/win-x64/native/WebView2Loader.dll", files.Keys);
        Assert.Contains("XUnityToolkit-WebUI.exe", files.Keys);
        Assert.DoesNotContain("Microsoft.UI.Xaml/Assets/untracked.png", files.Keys);
    }

    [Fact]
    public void ComputeDiff_ShouldInstallNestedAppFileWhenUpgradingFromLegacyVersion()
    {
        WriteFile("Microsoft.UI.Xaml/Assets/WinUI.png", "native-resource");
        var manifest = BuildManifest(("Microsoft.UI.Xaml/Assets/WinUI.png", "app"));

        var diff = UpdateManifestFileSet.ComputeDiff(
            _root,
            manifest,
            preserveCustomLlamaFiles: false,
            hasBundledLlama: true);

        Assert.Equal(["app"], diff.ChangedPackages);
        Assert.Equal(1, diff.ChangedFileCount);
    }

    [Theory]
    [InlineData("../outside.dll")]
    [InlineData("data/private.json")]
    [InlineData("appsettings.Production.json")]
    [InlineData("C:/outside.dll")]
    public void ShouldManageForManifest_ShouldRejectUnsafeOrProtectedPaths(string path)
    {
        Assert.False(UpdateManifestFileSet.ShouldManageForManifest(path));
    }

    [Fact]
    public void ValidateAppPackageInventory_ShouldAcceptMatchingPackageAndManifest()
    {
        var paths = new[]
        {
            UpdateManifestFileSet.AppFileInventoryName,
            "Microsoft.UI.Xaml/Assets/WinUI.png",
            "XUnityToolkit-WebUI.exe"
        };
        using var archiveStream = BuildAppArchive(paths, paths);
        using var archive = new ZipArchive(archiveStream, ZipArchiveMode.Read);
        var manifest = BuildPathOnlyManifest(paths);

        UpdateManifestFileSet.ValidateAppPackageInventory(archive, manifest);
    }

    [Fact]
    public void ValidateAppPackageInventory_ShouldRejectManifestPathMissingFromInventory()
    {
        var inventoryPaths = new[]
        {
            UpdateManifestFileSet.AppFileInventoryName,
            "XUnityToolkit-WebUI.exe"
        };
        var manifestPaths = inventoryPaths.Append("Microsoft.UI.Xaml/Assets/WinUI.png").ToArray();
        using var archiveStream = BuildAppArchive(inventoryPaths, inventoryPaths);
        using var archive = new ZipArchive(archiveStream, ZipArchiveMode.Read);
        var manifest = BuildPathOnlyManifest(manifestPaths);

        Assert.Throws<InvalidOperationException>(() =>
            UpdateManifestFileSet.ValidateAppPackageInventory(archive, manifest));
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
            Version = "5.0.0",
            Rid = "win-x64",
            Files = manifestFiles
        };
    }

    private static UpdateManifest BuildPathOnlyManifest(IEnumerable<string> paths)
    {
        return new UpdateManifest
        {
            Version = "5.0.0",
            Rid = "win-x64",
            Files = paths.ToDictionary(
                path => path,
                _ => new ManifestFileEntry { Hash = "sha256:test", Size = 1, Package = "app" },
                StringComparer.OrdinalIgnoreCase)
        };
    }

    private static MemoryStream BuildAppArchive(
        IEnumerable<string> inventoryPaths,
        IEnumerable<string> archivePaths)
    {
        var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var path in archivePaths)
            {
                var entry = archive.CreateEntry(path);
                using var writer = new StreamWriter(entry.Open(), Encoding.UTF8, leaveOpen: false);
                writer.Write(path == UpdateManifestFileSet.AppFileInventoryName
                    ? JsonSerializer.Serialize(new { protocolVersion = 1, files = inventoryPaths })
                    : "test");
            }
        }

        stream.Position = 0;
        return stream;
    }

    private void WriteFile(string relativePath, string contents)
    {
        var fullPath = ToFullPath(relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        File.WriteAllText(fullPath, contents);
    }

    private void WriteInventory(params string[] files)
    {
        WriteFile(
            UpdateManifestFileSet.AppFileInventoryName,
            JsonSerializer.Serialize(new { protocolVersion = 1, files }));
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
