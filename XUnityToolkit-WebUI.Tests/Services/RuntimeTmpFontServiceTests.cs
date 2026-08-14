using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using XUnityToolkit_WebUI.Infrastructure;
using XUnityToolkit_WebUI.Models;
using XUnityToolkit_WebUI.Services;
using Xunit;

namespace XUnityToolkit_WebUI.Tests.Services;

public sealed class RuntimeTmpFontServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"xut-runtime-font-{Guid.NewGuid():N}");
    private readonly string _bundledRoot;
    private readonly AppDataPaths _appDataPaths;
    private readonly BundledAssetPaths _bundledPaths;
    private readonly ConfigurationService _configuration;
    private readonly RuntimeTmpFontService _service;

    public RuntimeTmpFontServiceTests()
    {
        _bundledRoot = Path.Combine(_root, "bundled");
        var fontDirectory = Path.Combine(_bundledRoot, "fonts");
        Directory.CreateDirectory(fontDirectory);
        File.Copy(
            Path.Combine(FindRepositoryRoot(), "bundled", "fonts", BundledFontCatalog.RuntimeFileName),
            Path.Combine(fontDirectory, BundledFontCatalog.RuntimeFileName));
        File.WriteAllText(Path.Combine(fontDirectory, "SourceHanSans_U2022"), "legacy-bundle");
        Directory.CreateDirectory(Path.Combine(_bundledRoot, "runtime-font-loader", "mono"));
        Directory.CreateDirectory(Path.Combine(_bundledRoot, "runtime-font-loader", "il2cpp"));
        File.WriteAllText(
            Path.Combine(_bundledRoot, "runtime-font-loader", "mono", "XUnityToolkit.RuntimeFontLoader.dll"),
            "mono-plugin");
        File.WriteAllText(
            Path.Combine(_bundledRoot, "runtime-font-loader", "il2cpp", "XUnityToolkit.RuntimeFontLoader.IL2CPP.dll"),
            "il2cpp-plugin");

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["AppData:Root"] = Path.Combine(_root, "data") })
            .Build();
        _appDataPaths = new AppDataPaths(configuration);
        _appDataPaths.EnsureDirectoriesExist();
        _bundledPaths = new BundledAssetPaths(_bundledRoot);
        _configuration = new ConfigurationService(
            NullLogger<ConfigurationService>.Instance,
            new ToolkitRuntimeEndpointState(51821),
            _appDataPaths);
        var legacy = new TmpFontService(_bundledPaths, NullLogger<TmpFontService>.Instance);
        _service = new RuntimeTmpFontService(
            _bundledPaths,
            new BundledFontCatalog(_bundledPaths),
            legacy,
            _appDataPaths,
            _configuration,
            NullLogger<RuntimeTmpFontService>.Instance);
    }

    [Theory]
    [InlineData(UnityBackend.Mono, "XUnityToolkit.RuntimeFontLoader.dll")]
    [InlineData(UnityBackend.IL2CPP, "XUnityToolkit.RuntimeFontLoader.IL2CPP.dll")]
    public async Task InstallAsync_SelectsBackendAndReadsRuntimeStatus(
        UnityBackend backend,
        string expectedPlugin)
    {
        var game = CreateGame(backend);
        WriteConfig(game, string.Empty, string.Empty);

        var pending = await _service.InstallAsync(game, new TmpFontInstallRequest());

        Assert.True(pending.Installed);
        Assert.True(pending.RequiresRestart);
        Assert.Equal("pending", pending.ActiveLoader);
        Assert.True(File.Exists(Path.Combine(game.GamePath, "BepInEx", "plugins", "XUnityToolkit", expectedPlugin)));
        Assert.True(File.Exists(Path.Combine(game.GamePath, "BepInEx", "Font", BundledFontCatalog.RuntimeFileName)));
        Assert.Contains("AllowLegacyFallback = true", await File.ReadAllTextAsync(Path.Combine(
            game.GamePath, "BepInEx", "config", RuntimeTmpFontService.PluginId + ".cfg")));

        await File.WriteAllTextAsync(Path.Combine(
            game.GamePath, "BepInEx", "config", RuntimeTmpFontService.PluginId + ".status.json"),
            """
            {
              "generatedAtUtc": "2999-01-01T00:00:00Z",
              "activeLoader": "direct-ttf",
              "directTtfSupported": true,
              "legacyFallbackUsed": false,
              "overrideAdapterAvailable": true,
              "message": "runtime-ok"
            }
            """);

        var verified = await _service.GetStatusAsync(game);
        Assert.False(verified.RequiresRestart);
        Assert.Equal("direct-ttf", verified.ActiveLoader);
        Assert.True(verified.DirectTtfSupported);
        Assert.True(verified.OverrideAdapterAvailable);
        Assert.Equal("runtime-ok", verified.Message);
    }

    [Fact]
    public async Task InstallAsync_RejectsExternalConfigBeforeWriting_ThenRestoresItConditionally()
    {
        var game = CreateGame(UnityBackend.Mono);
        WriteConfig(game, "External.Override", "External/Fallback");

        await Assert.ThrowsAsync<RuntimeFontConfigurationConflictException>(() =>
            _service.InstallAsync(game, new TmpFontInstallRequest { ApplicationMode = "override" }));
        Assert.False(Directory.Exists(Path.Combine(game.GamePath, "BepInEx", "Font")));

        var installed = await _service.InstallAsync(game, new TmpFontInstallRequest
        {
            ApplicationMode = "override",
            ReplaceExistingConfig = true,
        });
        Assert.Equal("override", installed.ApplicationMode);
        var managedConfig = await _configuration.GetAsync(game.GamePath);
        Assert.Equal(RuntimeTmpFontService.OverrideSentinel, managedConfig.OverrideFontTextMeshPro);
        Assert.True(string.IsNullOrWhiteSpace(managedConfig.FallbackFontTextMeshPro));

        await _service.RemoveAsync(game);
        var restored = await _configuration.GetAsync(game.GamePath);
        Assert.Equal("External.Override", restored.OverrideFontTextMeshPro);
        Assert.Equal("External/Fallback", restored.FallbackFontTextMeshPro);
    }

    [Fact]
    public void RequestDefaults_PreserveLegacyEmptyBodyBehavior()
    {
        var request = new TmpFontInstallRequest();

        Assert.Equal(BundledFontCatalog.DefaultSourceId, request.SourceId);
        Assert.Equal("fallback", request.ApplicationMode);
        Assert.True(request.Enabled);
        Assert.False(request.ReplaceExistingConfig);
    }

    private Game CreateGame(UnityBackend backend)
    {
        var gamePath = Path.Combine(_root, "games", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(gamePath);
        return new Game
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = "Runtime Font Test",
            GamePath = gamePath,
            InstallState = InstallState.FullyInstalled,
            IsUnityGame = true,
            DetectedInfo = new UnityGameInfo
            {
                UnityVersion = "2022.3.62f3",
                Backend = backend,
                Architecture = Architecture.X64,
                DetectedExecutable = "Game.exe",
                HasTextMeshPro = true,
            },
        };
    }

    private void WriteConfig(Game game, string overrideFont, string fallbackFont)
    {
        var configPath = _configuration.GetConfigPath(game.GamePath);
        Directory.CreateDirectory(Path.GetDirectoryName(configPath)!);
        File.WriteAllText(configPath,
            $"[Behaviour]{Environment.NewLine}" +
            $"OverrideFontTextMeshPro={overrideFont}{Environment.NewLine}" +
            $"FallbackFontTextMeshPro={fallbackFont}{Environment.NewLine}");
    }

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

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }
}
