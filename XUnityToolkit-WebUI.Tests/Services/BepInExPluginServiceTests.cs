using Microsoft.Extensions.Logging.Abstractions;
using XUnityToolkit_WebUI.Models;
using XUnityToolkit_WebUI.Services;
using Xunit;

namespace XUnityToolkit_WebUI.Tests.Services;

public sealed class BepInExPluginServiceTests : IDisposable
{
    private readonly string _gamePath;
    private readonly string _pluginsPath;
    private readonly Game _game;
    private readonly BepInExPluginService _service;

    public BepInExPluginServiceTests()
    {
        _gamePath = Path.Combine(
            Path.GetTempPath(),
            $"xunitytoolkit-plugin-tests-{Guid.NewGuid():N}");
        _pluginsPath = Path.Combine(_gamePath, "BepInEx", "plugins");
        Directory.CreateDirectory(_pluginsPath);

        _game = new Game
        {
            Name = "Test Game",
            GamePath = _gamePath,
            InstallState = InstallState.FullyInstalled,
        };
        _service = new BepInExPluginService(NullLogger<BepInExPluginService>.Instance);
    }

    [Fact]
    public async Task ListPluginsAsync_ExcludesOnlyXUnityTranslatorEndpoints()
    {
        CreatePluginFile("XUnity.AutoTranslator/Translators/BaiduTranslate.dll");
        CreatePluginFile("XUnity.AutoTranslator/Translators/FullNET/Common.ExtProtocol.dll");
        CreatePluginFile("XUnity.AutoTranslator/Translators/LLMTranslate.dll.disabled");

        CreatePluginFile("XUnity.AutoTranslator/XUnity.AutoTranslator.Plugin.Core.dll");
        CreatePluginFile("ThirdParty/ThirdParty.Plugin.dll");
        CreatePluginFile("ThirdParty/Translators/NestedPlugin.dll");
        CreatePluginFile("XUnity.AutoTranslator/TranslatorsExtra/NearMatch.Plugin.dll");

        var plugins = await _service.ListPluginsAsync(_game);

        Assert.Equal(4, plugins.Count);
        Assert.DoesNotContain(plugins, plugin =>
            plugin.RelativePath.StartsWith(
                "XUnity.AutoTranslator/Translators/",
                StringComparison.OrdinalIgnoreCase));
        Assert.Contains(plugins, plugin =>
            plugin.RelativePath == "XUnity.AutoTranslator/XUnity.AutoTranslator.Plugin.Core.dll"
            && plugin.IsToolkitManaged);
        Assert.Contains(plugins, plugin => plugin.RelativePath == "ThirdParty/ThirdParty.Plugin.dll");
        Assert.Contains(plugins, plugin => plugin.RelativePath == "ThirdParty/Translators/NestedPlugin.dll");
        Assert.Contains(plugins, plugin =>
            plugin.RelativePath == "XUnity.AutoTranslator/TranslatorsExtra/NearMatch.Plugin.dll");
    }

    [Theory]
    [InlineData("XUnity.AutoTranslator\\Translators\\BaiduTranslate.dll")]
    [InlineData("XUnity.AutoTranslator/Translators/FullNET/Http.ExtProtocol.dll")]
    [InlineData("xunity.autotranslator/TRANSLATORS/CustomTranslate.dll.disabled")]
    public async Task TogglePluginAsync_RejectsXUnityTranslatorEndpoints(string relativePath)
    {
        var fullPath = CreatePluginFile(relativePath);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _service.TogglePluginAsync(_game, relativePath));

        Assert.Equal("无法切换工具箱管理的插件", exception.Message);
        Assert.True(File.Exists(fullPath));
    }

    [Theory]
    [InlineData("XUnity.AutoTranslator\\Translators\\BaiduTranslate.dll")]
    [InlineData("XUnity.AutoTranslator/Translators/FullNET/Common.ExtProtocol.dll")]
    [InlineData("xunity.autotranslator/TRANSLATORS/CustomTranslate.dll.disabled")]
    public async Task UninstallPluginAsync_RejectsXUnityTranslatorEndpoints(string relativePath)
    {
        var fullPath = CreatePluginFile(relativePath);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _service.UninstallPluginAsync(_game, relativePath));

        Assert.Equal("无法卸载工具箱管理的插件", exception.Message);
        Assert.True(File.Exists(fullPath));
    }

    private string CreatePluginFile(string relativePath)
    {
        var platformPath = relativePath
            .Replace('/', Path.DirectorySeparatorChar)
            .Replace('\\', Path.DirectorySeparatorChar);
        var fullPath = Path.Combine(_pluginsPath, platformPath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        File.WriteAllBytes(fullPath, [0x4D, 0x5A]);
        return fullPath;
    }

    public void Dispose()
    {
        if (Directory.Exists(_gamePath))
            Directory.Delete(_gamePath, recursive: true);
    }
}
