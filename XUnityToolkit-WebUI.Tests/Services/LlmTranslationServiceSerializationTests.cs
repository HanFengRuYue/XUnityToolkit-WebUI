using System.Text.Json;
using XUnityToolkit_WebUI.Services;
using Xunit;

namespace XUnityToolkit_WebUI.Tests.Services;

public sealed class LlmTranslationServiceSerializationTests
{
    [Fact]
    public void SerializeUserContent_ShouldKeepNonAsciiCharactersReadable()
    {
        IList<string> source = ["こんにちは", "中文测试", "特殊字符 <>&"];

        var serialized = LlmTranslationService.SerializeUserContent(source);

        Assert.Contains("こんにちは", serialized, StringComparison.Ordinal);
        Assert.Contains("中文测试", serialized, StringComparison.Ordinal);
        Assert.DoesNotContain("\\u", serialized, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(source, JsonSerializer.Deserialize<List<string>>(serialized));
    }
}
