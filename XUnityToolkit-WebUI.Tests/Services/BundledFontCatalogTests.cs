using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using XUnityToolkit_WebUI.Models;
using XUnityToolkit_WebUI.Services;
using Xunit;

namespace XUnityToolkit_WebUI.Tests.Services;

public sealed class BundledFontCatalogTests
{
    [Fact]
    public void OfficialSourceHanSansFilesAndLicenseMatchCatalog()
    {
        var root = FindRepositoryRoot();
        var fontRoot = Path.Combine(root, "bundled", "fonts");
        var runtimeFont = Path.Combine(fontRoot, BundledFontCatalog.RuntimeFileName);
        var assetFont = Path.Combine(fontRoot, BundledFontCatalog.AssetReplacementFileName);
        var license = Path.Combine(fontRoot, "licenses", "SourceHanSans-OFL-1.1.txt");

        Assert.Equal(17_749_860, new FileInfo(runtimeFont).Length);
        Assert.Equal(BundledFontCatalog.RuntimeSha256, Hash(runtimeFont));
        Assert.Equal(8_429_224, new FileInfo(assetFont).Length);
        Assert.Equal(BundledFontCatalog.AssetReplacementSha256, Hash(assetFont));
        Assert.Contains("SIL OPEN FONT LICENSE Version 1.1", File.ReadAllText(license));

        using var catalog = JsonDocument.Parse(File.ReadAllText(Path.Combine(fontRoot, "font-catalog.json")));
        var family = Assert.Single(catalog.RootElement.GetProperty("families").EnumerateArray());
        Assert.Equal(BundledFontCatalog.DefaultSourceId, family.GetProperty("id").GetString());
        Assert.Equal(BundledFontCatalog.Version, family.GetProperty("version").GetString());
    }

    [Fact]
    public async Task RuntimeFontLoaderAssembliesAreToolkitManaged()
    {
        var root = Path.Combine(Path.GetTempPath(), $"xut-managed-plugin-{Guid.NewGuid():N}");
        var pluginDirectory = Path.Combine(root, "BepInEx", "plugins", "XUnityToolkit");
        Directory.CreateDirectory(pluginDirectory);
        await File.WriteAllTextAsync(
            Path.Combine(pluginDirectory, "XUnityToolkit.RuntimeFontLoader.dll"),
            "test");
        try
        {
            var plugins = await new BepInExPluginService(NullLogger<BepInExPluginService>.Instance)
                .ListPluginsAsync(new Game { Name = "Test", GamePath = root });

            var plugin = Assert.Single(plugins);
            Assert.True(plugin.IsToolkitManaged);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static string Hash(string path) =>
        Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "XUnityToolkit-WebUI.slnx")))
                return directory.FullName;
            directory = directory.Parent;
        }
        throw new DirectoryNotFoundException("Could not find repository root.");
    }
}
