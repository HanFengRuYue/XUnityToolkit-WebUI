using System.Reflection;
using XUnityToolkit_WebUI.Models;
using XUnityToolkit_WebUI.Services;
using Xunit;

namespace XUnityToolkit_WebUI.Tests.Services;

public sealed class PluginHealthCheckServiceTests
{
    [Fact]
    public void CheckLogErrors_DoesNotTreatGameIdContaining507AsApiFailure()
    {
        var lines = new[]
        {
            "[Info   :   Console] [LLMTranslate]   游戏 ID: fb507f09a2f046a29c02b1583632aca4",
            "[Info   :   Console] [LLMTranslate]   连通性测试已发送: http://127.0.0.1:51821/api/translate/ping?gameId=fb507f09a2f046a29c02b1583632aca4"
        };

        var checks = InvokeCheckLogErrors(lines);

        var logCheck = Assert.Single(checks);
        Assert.Equal("logErrors", logCheck.Id);
        Assert.Equal(HealthStatus.Healthy, logCheck.Status);
        Assert.Null(logCheck.Details);
    }

    [Fact]
    public void CheckLogErrors_DetectsExplicit502EndpointFailure()
    {
        var lines = new[]
        {
            "[Info   :   Console] [LLMTranslate] [错误] API 调用失败: HTTP 502 Bad Gateway"
        };

        var checks = InvokeCheckLogErrors(lines);

        var logCheck = Assert.Single(checks);
        Assert.Equal("logErrors", logCheck.Id);
        Assert.Equal(HealthStatus.Warning, logCheck.Status);
        var detail = Assert.Single(logCheck.Details!);
        Assert.Equal("API 调用失败", detail.Category);
    }

    private static List<HealthCheckItem> InvokeCheckLogErrors(string[] lines)
    {
        var checks = new List<HealthCheckItem>();
        var method = typeof(PluginHealthCheckService).GetMethod(
            "CheckLogErrors",
            BindingFlags.NonPublic | BindingFlags.Static);

        Assert.NotNull(method);
        method!.Invoke(null, [checks, lines, null]);
        return checks;
    }
}
