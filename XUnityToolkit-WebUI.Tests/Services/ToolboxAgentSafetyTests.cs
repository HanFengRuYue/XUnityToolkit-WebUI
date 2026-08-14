using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using System.Text;
using System.Text.Json;
using XUnityToolkit_WebUI.Infrastructure;
using XUnityToolkit_WebUI.Models;
using XUnityToolkit_WebUI.Services;
using Xunit;

namespace XUnityToolkit_WebUI.Tests.Services;

public sealed class ToolboxAgentSafetyTests
{
    [Fact]
    public void DataReset_RejectsBroadProfileAndTempRootsButAllowsDedicatedChild()
    {
        var userProfile = Path.GetFullPath(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
        var tempRoot = Path.GetFullPath(Path.GetTempPath())
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var dedicated = Path.Combine(tempRoot, $"xunity-reset-{Guid.NewGuid():N}");

        Assert.True(ToolboxDataResetService.IsProtectedResetRoot(userProfile));
        Assert.True(ToolboxDataResetService.IsProtectedResetRoot(tempRoot));
        Assert.False(ToolboxDataResetService.IsProtectedResetRoot(dedicated));
    }

    [Fact]
    public async Task HostAccess_TrustedReadIsDirectButExternalReadAndScriptsRequireConfirmation()
    {
        var root = Path.Combine(Path.GetTempPath(), $"xunity-agent-host-{Guid.NewGuid():N}");
        var externalRoot = Path.Combine(Path.GetTempPath(), $"xunity-agent-external-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        Directory.CreateDirectory(externalRoot);
        await File.WriteAllTextAsync(Path.Combine(root, "raw.secret"), "token=raw-value");
        var externalFile = Path.Combine(externalRoot, "environment.log");
        await File.WriteAllTextAsync(externalFile, "external evidence");

        try
        {
            var service = CreateHostAccessService(root);
            using var trustedJson = JsonDocument.Parse("""
                {"scope":"toolbox","path":"raw.secret","mode":"text","purpose":"读取测试"}
                """);
            using var externalJson = JsonDocument.Parse(JsonSerializer.Serialize(new
            {
                scope = "external",
                path = externalFile,
                mode = "text",
                purpose = "检查外部环境日志"
            }));
            using var scriptJson = JsonDocument.Parse("""
                {"shell":"powershell","script":"Get-Process | Select-Object -First 1","purpose":"检查进程读取能力"}
                """);
            using var traversalJson = JsonDocument.Parse("""
                {"scope":"toolbox","path":"../escape.txt","mode":"text","purpose":"越界测试"}
                """);

            var trusted = await service.ReadFileAsync(trustedJson.RootElement, null, false, default);
            var external = await service.ReadFileAsync(externalJson.RootElement, null, false, default);
            var script = await service.RunScriptAsync(scriptJson.RootElement, false, default);

            Assert.True(trusted.Success);
            Assert.Contains("token=raw-value", trusted.ModelContent);
            Assert.False(trusted.RequiresConfirmation);
            Assert.True(external.RequiresConfirmation);
            Assert.Contains(externalFile, external.UserMessage);
            Assert.True(script.RequiresConfirmation);
            Assert.Contains("Get-Process | Select-Object -First 1", script.UserMessage);
            Assert.Contains("后端不能证明", script.UserMessage);
            await Assert.ThrowsAnyAsync<Exception>(() =>
                service.ReadFileAsync(traversalJson.RootElement, null, false, default));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
            Directory.Delete(externalRoot, recursive: true);
        }
    }

    [Fact]
    public async Task HostAccess_FileBatchWaitsForOneConfirmationAndThenWritesTrustedRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), $"xunity-agent-batch-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var service = CreateHostAccessService(root);
            using var json = JsonDocument.Parse("""
                {
                  "purpose":"建立诊断记录",
                  "operations":[
                    {"kind":"create_directory","scope":"toolbox","path":"agent-test"},
                    {"kind":"write_text","scope":"toolbox","path":"agent-test/result.any","content":"done"}
                  ]
                }
                """);

            var pending = await service.ManageFilesAsync("session", json.RootElement, null, false, default);
            Assert.True(pending.RequiresConfirmation);
            Assert.False(File.Exists(Path.Combine(root, "agent-test", "result.any")));

            var completed = await service.ManageFilesAsync("session", json.RootElement, null, true, default);
            Assert.True(completed.Success);
            Assert.Equal("done", await File.ReadAllTextAsync(Path.Combine(root, "agent-test", "result.any")));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void GamePathGate_AllowsTheRegisteredGameRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), $"xunity-agent-root-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            Assert.True(PluginDiagnosticArtifactCollector.IsSafeRegularDirectory(root, root));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void MissingGameDirectory_ReturnsNearestSafeDirectoryInsteadOfThrowing()
    {
        var root = Path.Combine(Path.GetTempPath(), $"xunity-agent-missing-{Guid.NewGuid():N}");
        var bepinex = Path.Combine(root, "BepInEx");
        Directory.CreateDirectory(bepinex);
        File.WriteAllText(Path.Combine(bepinex, "LogOutput.log"), "active log");
        try
        {
            var game = new Game { Name = "Test", GamePath = root };
            var requested = Path.Combine(bepinex, "logs");

            var result = ToolboxAgentToolExecutor.CreateMissingGameDirectoryResult(
                game, "BepInEx/logs", requested);

            Assert.False(result.Success);
            Assert.Equal("列出游戏文件", result.Description);
            Assert.Contains("BepInEx/logs", result.ModelContent);
            Assert.Contains("BepInEx", result.ModelContent);
            Assert.Contains("BepInEx/LogOutput.log", result.ModelContent);
            Assert.DoesNotContain(root, result.ModelContent, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task SharedTextSnapshot_ReadsLogWhileAnotherProcessKeepsWriting()
    {
        var root = Path.Combine(Path.GetTempPath(), $"xunity-agent-shared-log-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var logPath = Path.Combine(root, "LogOutput.log");
        await File.WriteAllTextAsync(logPath, "line one\n");
        try
        {
            await using (var writer = new FileStream(
                             logPath,
                             FileMode.Open,
                             FileAccess.ReadWrite,
                             FileShare.Read,
                             4096,
                             useAsync: true))
            {
                writer.Seek(0, SeekOrigin.End);
                await writer.WriteAsync(Encoding.UTF8.GetBytes("line two\n"));
                await writer.FlushAsync();

                var content = await ToolboxAgentToolExecutor.ReadSharedTextSnapshotAsync(
                    logPath, 512 * 1024, default);

                Assert.Equal("line one\nline two\n", content);
            }
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Theory]
    [InlineData("GET", "/api/games", true)]
    [InlineData("POST", "/api/games/game-id/detect", true)]
    [InlineData("GET", "/api/toolbox-agent/status", false)]
    [InlineData("GET", "/api/filesystem/list", false)]
    [InlineData("GET", "/api/settings", false)]
    [InlineData("GET", "/api/%73ettings", false)]
    [InlineData("POST", "/api/settings/reset", false)]
    [InlineData("POST", "/api/update/apply", false)]
    [InlineData("GET", "https://example.com/api/games", false)]
    [InlineData("GET", "/api/games/../settings", false)]
    [InlineData("GET", "/api/games/%252e%252e/settings", false)]
    [InlineData("GET", "/api/ai/models?provider=DeepSeek&apiKey=secret", false)]
    [InlineData("GET", "/api/ai/models?provider=DeepSeek&api%4Bey=secret", false)]
    [InlineData("GET", "/api/example?access_token=secret", false)]
    public void ApiGate_AllowsToolboxJsonRoutesAndRejectsPrivilegeEscapes(
        string method,
        string path,
        bool expected)
    {
        var allowed = ToolboxAgentToolExecutor.IsAllowedApiCall(method, path, out var error);

        Assert.Equal(expected, allowed);
        Assert.Equal(expected, error is null);
    }

    [Theory]
    [InlineData("DELETE", "/api/games/id", true)]
    [InlineData("POST", "/api/games/id/launch", true)]
    [InlineData("POST", "/api/games/id/install", true)]
    [InlineData("POST", "/api/games/add-with-detection", true)]
    [InlineData("POST", "/api/games/%69d/launch", true)]
    [InlineData("POST", "/api/local-llm/models/add", true)]
    [InlineData("POST", "/api/local-llm/download", true)]
    [InlineData("POST", "/api/settings/open-data-folder", true)]
    [InlineData("PUT", "/api/games/id/terms", true)]
    [InlineData("GET", "/api/local-llm/downloads", false)]
    [InlineData("GET", "/api/font-generation/history/download", false)]
    [InlineData("GET", "/api/games/id/open-folder", false)]
    [InlineData("POST", "/api/games/id/detect", false)]
    [InlineData("GET", "/api/games", false)]
    public void ApiGate_RequiresConfirmationForHighImpactOperations(
        string method,
        string path,
        bool expected)
    {
        Assert.Equal(expected, ToolboxAgentToolExecutor.RequiresConfirmation(method, path));
    }

    [Fact]
    public void IniPatcher_UpdatesExistingKeyAndPreservesOtherSections()
    {
        const string source = "[General]\nEnabled=false\nKeep=1\n\n[Other]\nValue=x\n";

        var updated = PluginAutoRepairService.SetIniValue(source, "General", "Enabled", "true");

        Assert.Contains("[General]", updated);
        Assert.Contains("Enabled=true", updated);
        Assert.Contains("Keep=1", updated);
        Assert.Contains("[Other]", updated);
        Assert.Contains("Value=x", updated);
        Assert.DoesNotContain("Enabled=false", updated);
    }

    [Fact]
    public void RepairPlanValidator_AllowsReviewedIniAndRejectsSecretsAndTraversal()
    {
        var descriptor = new DiagnosticArtifactDescriptor(
            "config", "插件配置", "config", "BepInEx/config/example.INI", "summary",
            100, DateTime.UtcNow, "C:\\game\\BepInEx\\config\\example.INI", null, false);
        var snapshot = new DiagnosticArtifactSnapshot("fingerprint", [descriptor]);
        var response = new PluginRepairPlanResponse
        {
            Actions =
            [
                new PluginRepairPlanAction
                {
                    Tool = "set_ini_value", Description = "启用插件", ArtifactId = "config",
                    Section = "General", Key = "Enabled", Value = "true"
                },
                new PluginRepairPlanAction
                {
                    Tool = "set_ini_value", Description = "泄露密钥", ArtifactId = "config",
                    Section = "Auth", Key = "ApiKey", Value = "secret"
                },
                new PluginRepairPlanAction
                {
                    Tool = "disable_plugin", Description = "越界", RelativePath = "..\\evil.dll"
                },
                new PluginRepairPlanAction
                {
                    Tool = "reinstall_component", Description = "恢复路由", Component = "translator_routing"
                }
            ]
        };

        var validated = PluginDiagnosticAgentService.ValidateRepairPlan(response, snapshot);

        Assert.Equal(2, validated.Count);
        Assert.Contains(validated, item => item.Tool == "set_ini_value" && item.Key == "Enabled");
        Assert.Contains(validated, item => item.Tool == "reinstall_component"
                                           && item.Component == "translator_routing");
    }

    [Fact]
    public void SafeSettingPatch_UpdatesEndpointModelWithoutTouchingApiKey()
    {
        var settings = new AppSettings
        {
            AiTranslation = new AiTranslationSettings
            {
                Endpoints =
                [
                    new ApiEndpointConfig
                    {
                        Id = "cloud",
                        Name = "Cloud",
                        ApiKey = "keep-secret",
                        ApiBaseUrl = "https://example.com",
                        ModelName = "old"
                    }
                ]
            }
        };
        using var document = System.Text.Json.JsonDocument.Parse("\"new-model\"");

        ToolboxAgentToolExecutor.ApplySafeSetting(
            settings,
            "aiTranslation.endpoints.cloud.modelName",
            document.RootElement);

        var endpoint = Assert.Single(settings.AiTranslation.Endpoints);
        Assert.Equal("new-model", endpoint.ModelName);
        Assert.Equal("keep-secret", endpoint.ApiKey);
        Assert.Equal("https://example.com", endpoint.ApiBaseUrl);
    }

    [Fact]
    public void FontGenerationInputCopy_CanBeConsumedWithoutDeletingTheAgentAttachment()
    {
        var root = Path.Combine(Path.GetTempPath(), $"xunity-agent-font-{Guid.NewGuid():N}");
        var source = Path.Combine(root, "attachment.ttf");
        var uploads = Path.Combine(root, "generation-uploads");
        Directory.CreateDirectory(root);
        File.WriteAllBytes(source, [0x00, 0x01, 0x00, 0x00, 0x74, 0x65, 0x73, 0x74]);

        try
        {
            var generationInput = ToolboxAgentToolExecutor.CreateFontGenerationInputCopy(source, uploads);

            Assert.NotEqual(source, generationInput);
            Assert.Equal(File.ReadAllBytes(source), File.ReadAllBytes(generationInput));

            File.Delete(generationInput);
            Assert.True(File.Exists(source));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static ToolboxAgentHostAccessService CreateHostAccessService(string root)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["AppData:Root"] = root })
            .Build();
        var paths = new AppDataPaths(configuration);
        paths.EnsureDirectoriesExist();
        return new ToolboxAgentHostAccessService(
            new GameLibraryService(paths, NullLogger<GameLibraryService>.Instance),
            paths,
            new ToolboxAgentAttachmentStore(paths, NullLogger<ToolboxAgentAttachmentStore>.Instance),
            NullLogger<ToolboxAgentHostAccessService>.Instance);
    }
}
