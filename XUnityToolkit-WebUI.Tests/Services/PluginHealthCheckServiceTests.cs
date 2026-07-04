using System.Reflection;
using XUnityToolkit_WebUI.Models;
using XUnityToolkit_WebUI.Services;
using Xunit;

namespace XUnityToolkit_WebUI.Tests.Services;

public sealed class PluginHealthCheckServiceTests
{
    [Fact]
    public void CheckDoorstopProxy_IL2CPP_AcceptsCoreDobbyDll()
    {
        using var temp = new TemporaryDirectory();
        File.WriteAllText(Path.Combine(temp.Path, "winhttp.dll"), "proxy");
        var coreDir = Path.Combine(temp.Path, "BepInEx", "core");
        Directory.CreateDirectory(coreDir);
        File.WriteAllText(Path.Combine(coreDir, "dobby.dll"), "dobby");

        var item = InvokeCheckDoorstopProxy(temp.Path, isIL2CPP: true);

        Assert.Equal("doorstopProxy", item.Id);
        Assert.Equal(HealthStatus.Healthy, item.Status);
    }

    [Fact]
    public void CheckDoorstopProxy_IL2CPP_AcceptsLegacyRootDobbyDll()
    {
        using var temp = new TemporaryDirectory();
        File.WriteAllText(Path.Combine(temp.Path, "winhttp.dll"), "proxy");
        File.WriteAllText(Path.Combine(temp.Path, "dobby.dll"), "dobby");

        var item = InvokeCheckDoorstopProxy(temp.Path, isIL2CPP: true);

        Assert.Equal("doorstopProxy", item.Id);
        Assert.Equal(HealthStatus.Healthy, item.Status);
    }

    [Fact]
    public void CheckDoorstopProxy_IL2CPP_ReportsMissingDobbyWhenNoAcceptedPathExists()
    {
        using var temp = new TemporaryDirectory();
        File.WriteAllText(Path.Combine(temp.Path, "winhttp.dll"), "proxy");

        var item = InvokeCheckDoorstopProxy(temp.Path, isIL2CPP: true);

        Assert.Equal("doorstopProxy", item.Id);
        Assert.Equal(HealthStatus.Error, item.Status);
        Assert.Contains("BepInEx/core/dobby.dll", item.Detail);
        Assert.Contains("根目录 dobby.dll", item.Detail);
    }

    [Fact]
    public void CheckDoorstopProxy_IL2CPP_ReportsMissingProxyAndDobby()
    {
        using var temp = new TemporaryDirectory();

        var item = InvokeCheckDoorstopProxy(temp.Path, isIL2CPP: true);

        Assert.Equal("doorstopProxy", item.Id);
        Assert.Equal(HealthStatus.Error, item.Status);
        Assert.Contains("winhttp.dll", item.Detail);
        Assert.Contains("BepInEx/core/dobby.dll", item.Detail);
    }

    [Fact]
    public void CheckDoorstopProxy_Mono_DoesNotRequireDobbyDll()
    {
        using var temp = new TemporaryDirectory();
        File.WriteAllText(Path.Combine(temp.Path, "winhttp.dll"), "proxy");

        var item = InvokeCheckDoorstopProxy(temp.Path, isIL2CPP: false);

        Assert.Equal("doorstopProxy", item.Id);
        Assert.Equal(HealthStatus.Healthy, item.Status);
    }

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

    private static HealthCheckItem InvokeCheckDoorstopProxy(string gamePath, bool isIL2CPP)
    {
        var checks = new List<HealthCheckItem>();
        var method = typeof(PluginHealthCheckService).GetMethod(
            "CheckDoorstopProxy",
            BindingFlags.NonPublic | BindingFlags.Static);

        Assert.NotNull(method);
        method!.Invoke(null, [checks, gamePath, isIL2CPP]);
        return Assert.Single(checks);
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            $"XUnityToolkitTests-{Guid.NewGuid():N}");

        public TemporaryDirectory()
        {
            Directory.CreateDirectory(Path);
        }

        public void Dispose()
        {
            if (Directory.Exists(Path))
                Directory.Delete(Path, recursive: true);
        }
    }
}
