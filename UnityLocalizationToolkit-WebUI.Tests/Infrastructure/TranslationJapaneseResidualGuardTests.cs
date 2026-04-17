using UnityLocalizationToolkit_WebUI.Infrastructure;
using Xunit;

namespace UnityLocalizationToolkit_WebUI.Tests.Infrastructure;

public sealed class TranslationJapaneseResidualGuardTests
{
    [Fact]
    public void Inspect_ShouldFlagKatakanaResidual()
    {
        var result = TranslationJapaneseResidualGuard.Inspect("这是我的拿手プレイ呢");

        Assert.True(result.HasResidualJapanese);
        Assert.Contains("プレイ", result.ResidualPreview);
    }

    [Fact]
    public void Inspect_ShouldFlagMixedJapaneseResidual()
    {
        var result = TranslationJapaneseResidualGuard.Inspect("这边来，私の得意プレイ呢");

        Assert.True(result.HasResidualJapanese);
        Assert.Contains("の", result.ResidualPreview);
        Assert.Contains("プレイ", result.ResidualPreview);
    }

    [Fact]
    public void Inspect_ShouldAllowPureChineseResult()
    {
        var result = TranslationJapaneseResidualGuard.Inspect("这就是我的拿手玩法呢");

        Assert.False(result.HasResidualJapanese);
        Assert.Equal(string.Empty, result.ResidualPreview);
    }

    [Fact]
    public void Inspect_ShouldIgnoreKnownPlaceholders()
    {
        var result = TranslationJapaneseResidualGuard.Inspect("{PLAYER} [SPECIAL_01] {{DNT_1}}");

        Assert.False(result.HasResidualJapanese);
    }

    [Fact]
    public void Inspect_ShouldIgnoreAllowedDntJapaneseSegments()
    {
        var result = TranslationJapaneseResidualGuard.Inspect(
            "请按スキル键继续",
            ["スキル"]);

        Assert.False(result.HasResidualJapanese);
    }
}
