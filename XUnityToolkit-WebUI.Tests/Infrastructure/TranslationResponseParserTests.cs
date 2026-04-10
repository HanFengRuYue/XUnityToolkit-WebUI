using Microsoft.Extensions.Logging.Abstractions;
using XUnityToolkit_WebUI.Infrastructure;
using Xunit;

namespace XUnityToolkit_WebUI.Tests.Infrastructure;

public sealed class TranslationResponseParserTests
{
    [Fact]
    public void Parse_ShouldReturnJsonArrayCandidates()
    {
        var result = TranslationResponseParser.Parse(
            "[\"你好\",\"世界\"]",
            2,
            ["Hello", "World"],
            NullLogger.Instance);

        Assert.Collection(result,
            item =>
            {
                Assert.Equal("你好", item.Text);
                Assert.Equal(TranslationCandidateKind.JsonArray, item.Kind);
                Assert.True(item.CanPersist);
            },
            item =>
            {
                Assert.Equal("世界", item.Text);
                Assert.Equal(TranslationCandidateKind.JsonArray, item.Kind);
                Assert.True(item.CanPersist);
            });
    }

    [Fact]
    public void Parse_ShouldStripThinkAndCodeFenceBeforeJsonArray()
    {
        const string content = """
<think>
先分析一下
</think>
```json
["那么，你到底在烦恼些什么呢？"]
```
""";

        var result = TranslationResponseParser.Parse(
            content,
            1,
            ["So then, what exactly are you having problems with?"],
            NullLogger.Instance);

        var candidate = Assert.Single(result);
        Assert.Equal("那么，你到底在烦恼些什么呢？", candidate.Text);
        Assert.Equal(TranslationCandidateKind.JsonArray, candidate.Kind);
        Assert.True(candidate.CanPersist);
    }

    [Fact]
    public void Parse_ShouldAcceptSinglePlainTextCandidate()
    {
        var result = TranslationResponseParser.Parse(
            "那么，你到底在烦恼些什么呢？",
            1,
            ["So then, what exactly are you having problems with?"],
            NullLogger.Instance);

        var candidate = Assert.Single(result);
        Assert.Equal("那么，你到底在烦恼些什么呢？", candidate.Text);
        Assert.Equal(TranslationCandidateKind.PlainText, candidate.Kind);
        Assert.True(candidate.CanPersist);
    }

    [Fact]
    public void Parse_ShouldFallbackToOriginalsForBatchNonJson()
    {
        var result = TranslationResponseParser.Parse(
            "你好，世界",
            2,
            ["Hello", "World"],
            NullLogger.Instance);

        Assert.Collection(result,
            item =>
            {
                Assert.Equal("Hello", item.Text);
                Assert.Equal(TranslationCandidateKind.FallbackOriginal, item.Kind);
                Assert.False(item.CanPersist);
            },
            item =>
            {
                Assert.Equal("World", item.Text);
                Assert.Equal(TranslationCandidateKind.FallbackOriginal, item.Kind);
                Assert.False(item.CanPersist);
            });
    }
}
