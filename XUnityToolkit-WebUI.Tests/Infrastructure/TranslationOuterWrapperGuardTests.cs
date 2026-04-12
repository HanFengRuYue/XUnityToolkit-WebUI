using XUnityToolkit_WebUI.Infrastructure;
using Xunit;

namespace XUnityToolkit_WebUI.Tests.Infrastructure;

public sealed class TranslationOuterWrapperGuardTests
{
    [Fact]
    public void Normalize_ShouldStripAddedChineseQuotes_WhenSourceHasNoOuterWrapper()
    {
        var result = TranslationOuterWrapperGuard.Normalize("What?", "“什么？”");

        Assert.True(result.WasNormalized);
        Assert.True(result.IsValid);
        Assert.Equal("什么？", result.Text);
        Assert.Equal("中文双引号", result.RemovedWrapperTrail);
    }

    [Fact]
    public void Normalize_ShouldStripAddedSquareBrackets_WhenSourceHasNoOuterWrapper()
    {
        var result = TranslationOuterWrapperGuard.Normalize("Cancel", "[取消]");

        Assert.True(result.WasNormalized);
        Assert.True(result.IsValid);
        Assert.Equal("取消", result.Text);
        Assert.Equal("方括号", result.RemovedWrapperTrail);
    }

    [Fact]
    public void Normalize_ShouldNotStrip_WhenSourceAlreadyHasOuterWrapper()
    {
        var result = TranslationOuterWrapperGuard.Normalize("「What?」", "「什么？」");

        Assert.False(result.WasNormalized);
        Assert.True(result.IsValid);
        Assert.Equal("「什么？」", result.Text);
    }

    [Fact]
    public void Normalize_ShouldNotStrip_WhenWrapperIsNotPaired()
    {
        var result = TranslationOuterWrapperGuard.Normalize("What?", "“什么？");

        Assert.False(result.WasNormalized);
        Assert.True(result.IsValid);
        Assert.Equal("“什么？", result.Text);
    }

    [Fact]
    public void Normalize_ShouldMarkInvalid_WhenTextBecomesWhitespaceAfterStrip()
    {
        var result = TranslationOuterWrapperGuard.Normalize("...", "[]");

        Assert.True(result.WasNormalized);
        Assert.False(result.IsValid);
        Assert.Equal("方括号", result.RemovedWrapperTrail);
    }

    [Fact]
    public void Normalize_ShouldSupportNestedAddedWrappers()
    {
        var result = TranslationOuterWrapperGuard.Normalize("What?", "[“什么？”]");

        Assert.True(result.WasNormalized);
        Assert.True(result.IsValid);
        Assert.Equal("什么？", result.Text);
        Assert.Equal("方括号 -> 中文双引号", result.RemovedWrapperTrail);
    }
}
