using System.Text;
using System.Text.RegularExpressions;
using XUnityToolkit_WebUI.Models;
using Xunit;

namespace XUnityToolkit_WebUI.Tests.Services;

public sealed class AiTranslationPromptTests
{
    [Fact]
    public void DefaultSystemPrompt_ShouldStayInSyncWithFrontendConstant()
    {
        var backendPrompt = new AiTranslationSettings().SystemPrompt;
        var frontendPrompt = ReadFrontendPrompt();

        Assert.Equal(backendPrompt, frontendPrompt);
    }

    [Fact]
    public void DefaultSystemPrompt_ShouldContainStrengthenedWrapperRules()
    {
        var prompt = new AiTranslationSettings().SystemPrompt;

        Assert.Contains("只返回翻译结果的 JSON 数组", prompt);
        Assert.Contains("每个数组项只包含译文本体", prompt);
        Assert.Contains("不得新增 “”、\"\"、''、「」、『』、【】、[]、()", prompt);
        Assert.Contains("不得补出“他说：”“旁白：”这类前缀", prompt);
    }

    private static string ReadFrontendPrompt()
    {
        var repoRoot = FindRepoRoot();
        var filePath = Path.Combine(repoRoot, "XUnityToolkit-Vue", "src", "constants", "prompts.ts");
        var content = File.ReadAllText(filePath, Encoding.UTF8);

        var matches = Regex.Matches(content, @"'((?:\\.|[^'])*)'");
        var sb = new StringBuilder();
        foreach (Match match in matches)
            sb.Append(DecodeJavaScriptSingleQuotedString(match.Groups[1].Value));

        return sb.ToString();
    }

    private static string FindRepoRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null && !File.Exists(Path.Combine(current.FullName, "AGENTS.md")))
            current = current.Parent;

        return current?.FullName ?? throw new DirectoryNotFoundException("Unable to locate repo root.");
    }

    private static string DecodeJavaScriptSingleQuotedString(string value)
    {
        var sb = new StringBuilder(value.Length);

        for (var i = 0; i < value.Length; i++)
        {
            var ch = value[i];
            if (ch != '\\' || i == value.Length - 1)
            {
                sb.Append(ch);
                continue;
            }

            var next = value[++i];
            switch (next)
            {
                case '\\':
                    sb.Append('\\');
                    break;
                case '\'':
                    sb.Append('\'');
                    break;
                case '"':
                    sb.Append('"');
                    break;
                case 'n':
                    sb.Append('\n');
                    break;
                case 'r':
                    sb.Append('\r');
                    break;
                case 't':
                    sb.Append('\t');
                    break;
                case 'u' when i + 4 < value.Length:
                    var hex = value.Substring(i + 1, 4);
                    sb.Append((char)Convert.ToInt32(hex, 16));
                    i += 4;
                    break;
                default:
                    sb.Append(next);
                    break;
            }
        }

        return sb.ToString();
    }
}
