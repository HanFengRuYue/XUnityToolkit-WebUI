using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using XUnityToolkit_WebUI.Infrastructure;
using XUnityToolkit_WebUI.Services;
using Xunit;

namespace XUnityToolkit_WebUI.Tests.Services;

public sealed class ConfigurationServiceTests
{
    [Fact]
    public async Task GetAsync_WhenConfigIsMissing_DefaultsSourceLanguageToAuto()
    {
        using var temp = new TemporaryDirectory();
        var service = CreateService(temp.Path);

        var config = await service.GetAsync(temp.Path);

        Assert.Equal("auto", config.SourceLanguage);
    }

    [Fact]
    public async Task ApplyOptimalDefaultsAsync_SetsAutoSourceAndPreservesOtherSections()
    {
        using var temp = new TemporaryDirectory();
        var service = CreateService(temp.Path);
        var configPath = service.GetConfigPath(temp.Path);
        Directory.CreateDirectory(Path.GetDirectoryName(configPath)!);
        await File.WriteAllTextAsync(
            configPath,
            "[General]\nLanguage=en\nFromLanguage=ja\n\n[Custom]\nKeepThis=1\n");

        await service.ApplyOptimalDefaultsAsync(temp.Path);

        var content = await File.ReadAllTextAsync(configPath);
        Assert.Contains("Language=zh", content);
        Assert.Contains("FromLanguage=auto", content);
        Assert.Contains("[Custom]", content);
        Assert.Contains("KeepThis=1", content);
    }

    private static ConfigurationService CreateService(string root)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AppData:Root"] = Path.Combine(root, "app-data")
            })
            .Build();
        var paths = new AppDataPaths(configuration);
        var settingsService = new AppSettingsService(paths, NullLogger<AppSettingsService>.Instance);
        return new ConfigurationService(
            NullLogger<ConfigurationService>.Instance,
            settingsService);
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"xunitytoolkit-config-tests-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path))
                Directory.Delete(Path, recursive: true);
        }
    }
}
