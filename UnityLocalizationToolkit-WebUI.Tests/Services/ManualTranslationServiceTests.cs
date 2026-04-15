using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using UnityLocalizationToolkit_WebUI.Infrastructure;
using UnityLocalizationToolkit_WebUI.Models;
using UnityLocalizationToolkit_WebUI.Services;
using Xunit;

namespace UnityLocalizationToolkit_WebUI.Tests.Services;

public sealed class ManualTranslationServiceTests : IDisposable
{
    private const string GameId = "game-1";

    private readonly string _root;
    private readonly AppDataPaths _paths;
    private readonly ManualTranslationService _service;

    public ManualTranslationServiceTests()
    {
        _root = Path.Combine(Path.GetTempPath(), $"xut-manual-translation-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_root);

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AppData:Root"] = _root,
            })
            .Build();

        _paths = new AppDataPaths(config);
        _paths.EnsureDirectoriesExist();
        _service = new ManualTranslationService(
            _paths,
            null!,
            null!,
            NullLogger<ManualTranslationService>.Instance);
    }

    [Fact]
    public async Task GetAssetsAsync_ShouldReturnOnlyRequestedPage()
    {
        var index = await SeedWorkspaceAsync();
        await _service.SaveOverrideAsync(GameId, "text-2", "覆盖文本");
        await _service.SaveOverrideAsync(GameId, "code-1", "覆盖代码");

        var result = await _service.GetAssetsAsync(GameId, new ManualTranslationAssetsQuery
        {
            Page = 2,
            PageSize = 2,
            ValueKind = ManualTranslationAssetFilterValueKind.All,
        });

        Assert.Equal(index.Assets.Count, result.Total);
        Assert.Equal(2, result.Page);
        Assert.Equal(2, result.PageSize);
        Assert.Equal(2, result.Assets.Count);
    }

    [Fact]
    public async Task GetAssetsAsync_ShouldUseDefaultQueryValuesWhenOptionalFiltersAreOmitted()
    {
        var index = await SeedWorkspaceAsync();

        var result = await _service.GetAssetsAsync(GameId, new ManualTranslationAssetsQuery());

        Assert.Equal(index.Assets.Count, result.Total);
        Assert.Equal(1, result.Page);
        Assert.Equal(50, result.PageSize);
        Assert.Equal(index.Assets.Count, result.Assets.Count);
    }

    [Fact]
    public async Task GetAssetsAsync_ShouldFilterByValueKind()
    {
        await SeedWorkspaceAsync();

        var result = await _service.GetAssetsAsync(GameId, new ManualTranslationAssetsQuery
        {
            ValueKind = ManualTranslationAssetFilterValueKind.Code,
        });

        Assert.Equal(1, result.Total);
        Assert.Single(result.Assets);
        Assert.All(result.Assets, asset => Assert.Equal(ManualTranslationAssetValueKind.Code, asset.ValueKind));
    }

    [Fact]
    public async Task GetAssetsAsync_ShouldRespectEditableAndOverriddenFilters()
    {
        await SeedWorkspaceAsync();
        await _service.SaveOverrideAsync(GameId, "text-2", "覆盖文本");
        await _service.SaveOverrideAsync(GameId, "code-1", "覆盖代码");

        var result = await _service.GetAssetsAsync(GameId, new ManualTranslationAssetsQuery
        {
            EditableOnly = true,
            OverriddenOnly = true,
            ValueKind = ManualTranslationAssetFilterValueKind.All,
        });

        Assert.Equal(2, result.Total);
        Assert.Equal(2, result.Assets.Count);
        Assert.All(result.Assets, asset =>
        {
            Assert.True(asset.Editable);
            Assert.True(asset.Overridden);
        });
    }

    [Fact]
    public async Task GetStatusAsync_ShouldExposeKindCounts()
    {
        var index = await SeedWorkspaceAsync();
        await _service.SaveOverrideAsync(GameId, "text-2", "覆盖文本");

        var status = await _service.GetStatusAsync(GameId);

        Assert.True(status.HasScan);
        Assert.Equal(index.Assets.Count, status.KindCounts.All);
        Assert.Equal(2, status.KindCounts.Text);
        Assert.Equal(1, status.KindCounts.Code);
        Assert.Equal(1, status.KindCounts.Image);
        Assert.Equal(1, status.KindCounts.Font);
        Assert.Equal(1, status.KindCounts.Binary);
        Assert.Equal(1, status.OverrideCount);
        Assert.Equal("dirty", status.State);
    }

    [Fact]
    public async Task GetAssetsAsync_ShouldPopulateListPresentationFields()
    {
        await SeedWorkspaceAsync();

        var result = await _service.GetAssetsAsync(GameId, new ManualTranslationAssetsQuery
        {
            ValueKind = ManualTranslationAssetFilterValueKind.All,
        });

        var textAsset = Assert.Single(result.Assets, asset => asset.AssetId == "text-1");
        Assert.Equal("Alpha Text", textAsset.ListTitle);
        Assert.Contains("dialogue/title", textAsset.ListSubtitle);
        Assert.Contains("sharedassets0.assets", textAsset.ListMeta);
        Assert.Equal("text", textAsset.IconKey);

        var imageAsset = Assert.Single(result.Assets, asset => asset.AssetId == "image-1");
        Assert.Equal("Cover Image", imageAsset.ListTitle);
        Assert.Contains("1920", imageAsset.ListSubtitle);
        Assert.Contains("1080", imageAsset.ListSubtitle);
        Assert.Equal("image", imageAsset.IconKey);
    }

    [Fact]
    public async Task SaveImageOverrideAsync_ShouldMarkImageAssetAsOverridden()
    {
        await SeedWorkspaceAsync();

        await using var stream = new MemoryStream(GetTinyPngBytes());
        await _service.SaveImageOverrideAsync(GameId, "image-1", stream);

        var detail = await _service.GetAssetDetailAsync(GameId, "image-1");
        Assert.NotNull(detail);
        Assert.True(detail!.Overridden);
    }

    [Fact]
    public async Task GetImagePreviewAsync_ShouldReturnFallbackPreviewWhenOriginalTextureCannotBeDecoded()
    {
        await SeedWorkspaceAsync();

        var game = new Game
        {
            Id = GameId,
            Name = "Test Game",
            GamePath = _root,
        };

        var result = await _service.GetImagePreviewAsync(
            game,
            "image-1",
            ManualTranslationImagePreviewSource.Original,
            ManualTranslationImagePreviewSize.Thumb);

        Assert.Equal("image/png", result.ContentType);
        await using var stream = result.Stream;
        using var memory = new MemoryStream();
        await stream.CopyToAsync(memory);
        Assert.NotEmpty(memory.ToArray());
    }

    [Fact]
    public async Task BuildAndImportPackageAsync_ShouldRoundTripImageOverrides()
    {
        await SeedWorkspaceAsync();

        await using (var stream = new MemoryStream(GetTinyPngBytes()))
        {
            await _service.SaveImageOverrideAsync(GameId, "image-1", stream);
        }

        var game = new Game
        {
            Id = GameId,
            Name = "Test Game",
            GamePath = _root,
        };
        var (packageStream, _) = await _service.BuildPackageAsync(game);
        var zipPath = Path.Combine(_root, "manual-translation.zip");
        await using (var file = File.Create(zipPath))
        {
            packageStream.Position = 0;
            await packageStream.CopyToAsync(file);
        }

        await _service.DeleteOverrideAsync(GameId, "image-1");
        await _service.ImportPackageAsync(GameId, zipPath);

        var detail = await _service.GetAssetDetailAsync(GameId, "image-1");
        Assert.NotNull(detail);
        Assert.True(detail!.Overridden);
    }

    [Fact]
    public async Task ImportPackageAsync_ShouldReplaceExistingOverridesAndResetApplyState()
    {
        await SeedWorkspaceAsync();
        await _service.SaveOverrideAsync(GameId, "text-1", "package override");

        var game = new Game
        {
            Id = GameId,
            Name = "Test Game",
            GamePath = _root,
        };
        var (packageStream, _) = await _service.BuildPackageAsync(game);
        var zipPath = Path.Combine(_root, "manual-translation-replace.zip");
        await using (var file = File.Create(zipPath))
        {
            packageStream.Position = 0;
            await packageStream.CopyToAsync(file);
        }

        await _service.SaveOverrideAsync(GameId, "code-1", "stale override");
        await using (var imageStream = new MemoryStream(GetTinyPngBytes()))
        {
            await _service.SaveImageOverrideAsync(GameId, "image-1", imageStream);
        }

        await FileHelper.WriteJsonAtomicAsync(_paths.ManualTranslationManifestFile(GameId), new ManualTranslationProjectManifest
        {
            GameId = GameId,
            TotalAssets = 6,
            EditableAssets = 3,
            OverridesApplied = 3,
            AppliedAt = DateTime.UtcNow,
            State = "applied",
        });

        await _service.ImportPackageAsync(GameId, zipPath);

        var textDetail = await _service.GetAssetDetailAsync(GameId, "text-1");
        var codeDetail = await _service.GetAssetDetailAsync(GameId, "code-1");
        var imageDetail = await _service.GetAssetDetailAsync(GameId, "image-1");
        var status = await _service.GetStatusAsync(GameId);

        Assert.NotNull(textDetail);
        Assert.True(textDetail!.Overridden);
        Assert.NotNull(codeDetail);
        Assert.False(codeDetail!.Overridden);
        Assert.NotNull(imageDetail);
        Assert.False(imageDetail!.Overridden);
        Assert.Equal(1, status.OverrideCount);
        Assert.Equal("dirty", status.State);
        Assert.Empty(Directory.GetFiles(_paths.ManualTranslationOverrideMediaDirectory(GameId), "*", SearchOption.AllDirectories));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    private async Task<ManualTranslationProjectIndex> SeedWorkspaceAsync()
    {
        var index = new ManualTranslationProjectIndex
        {
            GameId = GameId,
            Assets =
            [
                CreateAsset("text-1", ManualTranslationAssetValueKind.Text, "Alpha Text", editable: true),
                CreateAsset("text-2", ManualTranslationAssetValueKind.Text, "Zeta Override", editable: true),
                CreateAsset("code-1", ManualTranslationAssetValueKind.Code, "Beta Code", editable: true),
                CreateAsset("image-1", ManualTranslationAssetValueKind.Image, "Cover Image", editable: false),
                CreateAsset("font-1", ManualTranslationAssetValueKind.Font, "UIFont", editable: false),
                CreateAsset("binary-1", ManualTranslationAssetValueKind.Binary, "Packed Binary", editable: false),
            ],
        };

        var manifest = new ManualTranslationProjectManifest
        {
            GameId = GameId,
            TotalAssets = index.Assets.Count,
            EditableAssets = index.Assets.Count(asset => asset.Editable),
            OverridesApplied = 0,
            State = "ready",
        };

        await FileHelper.WriteJsonAtomicAsync(_paths.ManualTranslationAssetIndexFile(GameId), index);
        await FileHelper.WriteJsonAtomicAsync(_paths.ManualTranslationManifestFile(GameId), manifest);
        Directory.CreateDirectory(_paths.ManualTranslationOverridesDirectory(GameId));
        return index;
    }

    private static ManualTranslationAssetEntry CreateAsset(
        string assetId,
        ManualTranslationAssetValueKind valueKind,
        string displayName,
        bool editable)
    {
        return new ManualTranslationAssetEntry
        {
            AssetId = assetId,
            StorageKind = valueKind == ManualTranslationAssetValueKind.Code
                ? ManualTranslationAssetStorageKind.ManagedAssembly
                : ManualTranslationAssetStorageKind.BundleEntry,
            ValueKind = valueKind,
            ObjectType = valueKind == ManualTranslationAssetValueKind.Code ? "AssemblyString" : "MonoBehaviour",
            AssetFile = "sharedassets0.assets",
            BundleEntry = "level0",
            PathId = 155,
            DisplayName = displayName,
            Preview = displayName,
            OriginalText = $"{displayName} original",
            FieldPath = valueKind == ManualTranslationAssetValueKind.Code ? "Game.Type::Method" : "dialogue/title",
            Width = valueKind == ManualTranslationAssetValueKind.Image ? 1920 : null,
            Height = valueKind == ManualTranslationAssetValueKind.Image ? 1080 : null,
            Editable = editable,
            Exportable = true,
        };
    }

    private static byte[] GetTinyPngBytes()
    {
        return Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO9Wq1cAAAAASUVORK5CYII=");
    }
}
