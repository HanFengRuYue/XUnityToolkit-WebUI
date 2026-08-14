using Microsoft.Extensions.Logging.Abstractions;
using XUnityToolkit_WebUI.Models;
using XUnityToolkit_WebUI.Services;
using Xunit;

namespace XUnityToolkit_WebUI.Tests.Services;

public sealed class PluginHealthCheckServiceTests
{
    [Fact]
    public void EndpointVersionCheck_CompatibleCurrentIsHealthyAndDisclosesUnverifiedHash()
    {
        var status = new TranslatorEndpointStatus(
            true,
            TranslatorEndpointOrigin.CompatibleCurrent,
            "2.0.0.0",
            new string('A', 64),
            false,
            true,
            true,
            "compatible");

        var item = PluginHealthCheckService.CreateTranslatorEndpointVersionCheck(status);

        Assert.Equal(HealthStatus.Healthy, item.Status);
        Assert.Contains("同版兼容", item.Detail);
        Assert.Contains("SHA-256 未列入官方清单", item.Detail);
    }

    [Fact]
    public void CheckDoorstopProxy_Il2Cpp_AcceptsCoreDobbyDll()
    {
        using var temp = new TemporaryDirectory();
        File.WriteAllText(Path.Combine(temp.Path, "winhttp.dll"), "proxy");
        var coreDir = Path.Combine(temp.Path, "BepInEx", "core");
        Directory.CreateDirectory(coreDir);
        File.WriteAllText(Path.Combine(coreDir, "dobby.dll"), "dobby");

        var item = CheckDoorstop(temp.Path, isIl2Cpp: true);

        Assert.Equal(HealthStatus.Healthy, item.Status);
        Assert.Contains("winhttp.dll", item.Detail);
        Assert.Contains("dobby.dll", item.Detail);
    }

    [Fact]
    public void CheckDoorstopProxy_Il2Cpp_AcceptsLegacyRootDobbyDll()
    {
        using var temp = new TemporaryDirectory();
        File.WriteAllText(Path.Combine(temp.Path, "winhttp.dll"), "proxy");
        File.WriteAllText(Path.Combine(temp.Path, "dobby.dll"), "dobby");

        Assert.Equal(HealthStatus.Healthy, CheckDoorstop(temp.Path, isIl2Cpp: true).Status);
    }

    [Fact]
    public void CheckDoorstopProxy_MissingFile_ReportsOnlyObservedFact()
    {
        using var temp = new TemporaryDirectory();

        var item = CheckDoorstop(temp.Path, isIl2Cpp: true);

        Assert.Equal(HealthStatus.Error, item.Status);
        Assert.Equal("所需文件 winhttp.dll 不存在。", item.Detail);
        Assert.DoesNotContain("安全软件", item.Detail);
    }

    [Fact]
    public void SanitizeContent_RedactsSecretsPathsAndUrlCredentialsWithoutChangingLineCount()
    {
        using var temp = new TemporaryDirectory();
        var content = string.Join('\n',
            "ApiKey=sk-abcdefghijklmnop1234",
            "Authorization: Bearer abcdefghijklmnopqrstuvwxyz",
            "Endpoint=https://user:password@example.com/v1?token=secret-value&mode=test",
            "UserName=huang",
            "{\"apiKey\":\"json-secret\",\"mode\":\"test\"}",
            "<add key=\"Password\" value=\"xml-secret\" />",
            "ForwardPath=C:/Users/huang/Game/plugin.cfg",
            @"NetworkPath=\\server\private-share\plugin.cfg",
            $"PluginPath={Path.Combine(temp.Path, "BepInEx", "plugins", "Test.dll")}",
            "GameId=0123456789abcdef0123456789abcdef");

        var sanitized = PluginDiagnosticArtifactCollector.SanitizeContent(content, temp.Path);

        Assert.Equal(content.Count(c => c == '\n'), sanitized.Count(c => c == '\n'));
        Assert.DoesNotContain("abcdefghijklmnop1234", sanitized);
        Assert.DoesNotContain("abcdefghijklmnopqrstuvwxyz", sanitized);
        Assert.DoesNotContain("user:password", sanitized);
        Assert.DoesNotContain("secret-value", sanitized);
        Assert.DoesNotContain("huang", sanitized, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("private-share", sanitized, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("json-secret", sanitized, StringComparison.Ordinal);
        Assert.DoesNotContain("xml-secret", sanitized, StringComparison.Ordinal);
        Assert.DoesNotContain(temp.Path, sanitized, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("0123456789abcdef", sanitized);
        Assert.Contains("[REDACTED]", sanitized);
        Assert.Contains("[GAME]", sanitized);
    }

    [Fact]
    public void IsSafeRegularFile_RejectsFileOutsideGameRoot()
    {
        using var game = new TemporaryDirectory();
        using var outside = new TemporaryDirectory();
        var outsideFile = Path.Combine(outside.Path, "plugin.cfg");
        File.WriteAllText(outsideFile, "Enabled=true");

        Assert.False(PluginDiagnosticArtifactCollector.IsSafeRegularFile(game.Path, outsideFile));
    }

    [Fact]
    public void IsSafeRegularFile_RejectsReparsePointInPath()
    {
        using var game = new TemporaryDirectory();
        using var outside = new TemporaryDirectory();
        var outsideFile = Path.Combine(outside.Path, "plugin.cfg");
        File.WriteAllText(outsideFile, "Enabled=true");
        var link = Path.Combine(game.Path, "linked-config");

        try
        {
            Directory.CreateSymbolicLink(link, outside.Path);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or PlatformNotSupportedException)
        {
            // Some Windows installations do not allow creating a test symlink without Developer Mode.
            return;
        }

        Assert.False(PluginDiagnosticArtifactCollector.IsSafeRegularFile(
            game.Path,
            Path.Combine(link, "plugin.cfg")));
    }

    [Fact]
    public void IsProbablyText_RejectsBinaryControlsButAllowsAnsiEscapeSequences()
    {
        Assert.False(PluginDiagnosticArtifactCollector.IsProbablyText("text\0binary\u0001"));
        Assert.True(PluginDiagnosticArtifactCollector.IsProbablyText("\u001b[31m[Error]\u001b[0m 正常日志\n"));
    }

    [Fact]
    public async Task ReadSelectedAsync_UsesSharedReadAndEnforcesTotalContextBudget()
    {
        using var game = new TemporaryDirectory();
        var path = Path.Combine(game.Path, "large.cfg");
        File.WriteAllText(path, new string('A', 12_000));
        await using var sharedHandle = new FileStream(path, FileMode.Open, FileAccess.ReadWrite,
            FileShare.ReadWrite | FileShare.Delete);
        var descriptor = new DiagnosticArtifactDescriptor(
            "large", "大型配置", "config", "large.cfg", "", 12_000, null, path, null, false);
        var snapshot = new DiagnosticArtifactSnapshot("fingerprint", [descriptor]);
        var collector = new PluginDiagnosticArtifactCollector(
            NullLogger<PluginDiagnosticArtifactCollector>.Instance);

        var result = await collector.ReadSelectedAsync(
            snapshot,
            new Dictionary<string, string?> { ["large"] = "测试上下文限额" },
            game.Path,
            1_000,
            CancellationToken.None);

        var content = Assert.Single(result);
        Assert.True(content.Truncated);
        Assert.Equal(1_000, Assert.Single(content.Lines).Length);
    }

    [Fact]
    public async Task ReadSelectedAsync_SkipsNonTextCandidate()
    {
        using var game = new TemporaryDirectory();
        var path = Path.Combine(game.Path, "binary.cfg");
        File.WriteAllBytes(path, [0x00, 0x01, 0x02, 0x03, 0xFF]);
        var descriptor = new DiagnosticArtifactDescriptor(
            "binary", "伪装配置", "config", "binary.cfg", "", 5, null, path, null, false);
        var collector = new PluginDiagnosticArtifactCollector(
            NullLogger<PluginDiagnosticArtifactCollector>.Instance);

        var result = await collector.ReadSelectedAsync(
            new DiagnosticArtifactSnapshot("fingerprint", [descriptor]),
            new Dictionary<string, string?> { ["binary"] = "测试非文本拒绝" },
            game.Path,
            1_000,
            CancellationToken.None);

        Assert.Empty(result);
    }

    [Fact]
    public async Task InventoryFingerprint_IgnoresTransientPingCheckAcrossPages()
    {
        using var gameDirectory = new TemporaryDirectory();
        var game = new Game { Name = "测试游戏", GamePath = gameDirectory.Path };
        var stable = new HealthCheckItem("files", "文件", HealthStatus.Healthy, "文件未变化");
        var ping = new HealthCheckItem("toolboxConnectivity", "工具箱连通性", HealthStatus.Healthy,
            "本次启动已收到 ping");
        var collector = new PluginDiagnosticArtifactCollector(
            NullLogger<PluginDiagnosticArtifactCollector>.Instance);

        var verified = await collector.CollectInventoryAsync(game, [stable, ping]);
        var passive = await collector.CollectInventoryAsync(game, [stable]);

        Assert.Equal(verified.Fingerprint, passive.Fingerprint);
    }

    [Theory]
    [InlineData(@"BepInEx\core\BepInEx.Preloader.dll")]
    [InlineData(@"BepInEx\core\BepInEx.Unity.IL2CPP.dll")]
    public async Task CheckDoorstopConfig_CurrentBepInExFormatIsHealthy(string targetAssembly)
    {
        using var game = new TemporaryDirectory();
        var core = Path.Combine(game.Path, "BepInEx", "core");
        Directory.CreateDirectory(core);
        File.WriteAllText(Path.Combine(game.Path, targetAssembly), "preloader");
        var config = Path.Combine(game.Path, "doorstop_config.ini");
        await File.WriteAllTextAsync(config,
            $"[General]\nenabled = true\ntarget_assembly = {targetAssembly}\n");
        var checks = new List<HealthCheckItem>();

        await PluginHealthCheckService.CheckDoorstopConfigAsync(checks, game.Path);

        var healthy = Assert.Single(checks);
        Assert.Equal(HealthStatus.Healthy, healthy.Status);
        Assert.Contains("已识别当前配置", healthy.Detail);
        Assert.Contains(targetAssembly.Replace('\\', '/'), healthy.Detail);
    }

    [Fact]
    public async Task CheckDoorstopConfig_LegacyFormatRemainsSupported()
    {
        using var game = new TemporaryDirectory();
        var core = Path.Combine(game.Path, "BepInEx", "core");
        Directory.CreateDirectory(core);
        File.WriteAllText(Path.Combine(core, "BepInEx.Preloader.dll"), "preloader");
        var config = Path.Combine(game.Path, "doorstop_config.ini");
        await File.WriteAllTextAsync(config,
            "[UnityDoorstop]\nenabled=true\ntargetAssembly=BepInEx\\core\\BepInEx.Preloader.dll\n");
        var checks = new List<HealthCheckItem>();

        await PluginHealthCheckService.CheckDoorstopConfigAsync(checks, game.Path);

        var healthy = Assert.Single(checks);
        Assert.Equal(HealthStatus.Healthy, healthy.Status);
        Assert.Contains("已识别旧版配置", healthy.Detail);
    }

    [Fact]
    public async Task CheckDoorstopConfig_CurrentFormatReportsDeterministicMisconfiguration()
    {
        using var game = new TemporaryDirectory();
        var config = Path.Combine(game.Path, "doorstop_config.ini");
        await File.WriteAllTextAsync(config,
            "[General]\nenabled=false\ntarget_assembly=missing.dll\n");
        var checks = new List<HealthCheckItem>();

        await PluginHealthCheckService.CheckDoorstopConfigAsync(checks, game.Path);

        var invalid = Assert.Single(checks);
        Assert.Equal(HealthStatus.Error, invalid.Status);
        Assert.Contains("General.enabled", invalid.Detail);
        Assert.Contains("General.target_assembly", invalid.Detail);
        Assert.Contains("missing.dll", invalid.Detail);
    }

    [Fact]
    public void BuildEvidence_RejectsUnknownArtifactAndInvalidLineRange()
    {
        var descriptor = new DiagnosticArtifactDescriptor(
            "log", "运行日志", "log", "BepInEx/LogOutput.log", "", 10, null, null, null, false);
        var content = new DiagnosticArtifactContent(descriptor, ["first", "second", "third"], false, null);
        var reviewed = new Dictionary<string, DiagnosticArtifactContent>(StringComparer.Ordinal)
        {
            [descriptor.Id] = content
        };

        Assert.Null(PluginDiagnosticArtifactCollector.BuildEvidence(reviewed, "missing", 1, 1));
        Assert.Null(PluginDiagnosticArtifactCollector.BuildEvidence(reviewed, "log", 0, 2));
        Assert.Null(PluginDiagnosticArtifactCollector.BuildEvidence(reviewed, "log", 3, 2));

        var evidence = PluginDiagnosticArtifactCollector.BuildEvidence(reviewed, "log", 2, 20);
        Assert.NotNull(evidence);
        Assert.Equal(2, evidence!.StartLine);
        Assert.Equal(3, evidence.EndLine);
        Assert.Equal("second\nthird", evidence.Excerpt);
    }

    [Fact]
    public void ValidateSelection_AllowsOnlyListedOptionalArtifactIds()
    {
        var required = new DiagnosticArtifactDescriptor(
            "environment", "环境", "environment", null, "", 1, null, null, "facts", true);
        var optional = new DiagnosticArtifactDescriptor(
            "log", "日志", "log", "BepInEx/LogOutput.log", "", 1, null, null, null, false);
        var response = new PluginDiagnosticAgentService.SelectionResponse
        {
            Artifacts =
            [
                new() { ArtifactId = "missing", Reason = "越界请求" },
                new() { ArtifactId = "environment", Reason = "重复基础资料" },
                new() { ArtifactId = "log", Reason = new string('R', 400) }
            ]
        };

        var selected = PluginDiagnosticAgentService.ValidateSelection(
            response,
            new DiagnosticArtifactSnapshot("fingerprint", [required, optional]));

        var reason = Assert.Single(selected);
        Assert.Equal("log", reason.Key);
        Assert.NotNull(reason.Value);
        Assert.True(reason.Value!.Length <= 243);
    }

    [Fact]
    public void ValidateAnalysis_DropsFindingWithoutBackendValidatedEvidence()
    {
        var descriptor = new DiagnosticArtifactDescriptor(
            "log", "运行日志", "log", "BepInEx/LogOutput.log", "", 10, null, null, null, false);
        var reviewed = new DiagnosticArtifactContent(descriptor, ["loaded", "failure"], false, "诊断加载失败");
        var response = new PluginDiagnosticAgentService.AnalysisResponse
        {
            Summary = "模型返回两个问题",
            Findings =
            [
                new()
                {
                    Severity = "Error", Confidence = "High", Category = "加载",
                    Title = "无效问题", Explanation = "引用了未知资料。",
                    Evidence = [new() { ArtifactId = "unknown", StartLine = 1, EndLine = 1 }]
                },
                new()
                {
                    Severity = "Warning", Confidence = "Medium", Category = "加载",
                    Title = "有效问题", Explanation = "日志记录了 failure。",
                    Evidence = [new() { ArtifactId = "log", StartLine = 2, EndLine = 50 }]
                }
            ]
        };

        var analysis = PluginDiagnosticAgentService.ValidateAnalysis(response, [reviewed], "测试端点");

        var finding = Assert.Single(analysis.Findings);
        Assert.Equal("有效问题", finding.Title);
        var evidence = Assert.Single(finding.Evidence);
        Assert.Equal(2, evidence.StartLine);
        Assert.Equal(2, evidence.EndLine);
        Assert.Equal("failure", evidence.Excerpt);
    }

    [Fact]
    public void ReadAssemblyMetadata_UsesPeMetadataAndFindsReferences()
    {
        var assemblyPath = typeof(PluginHealthCheckServiceTests).Assembly.Location;

        var metadata = PluginDiagnosticArtifactCollector.TryReadAssemblyMetadata(assemblyPath);

        Assert.NotNull(metadata);
        Assert.Equal("XUnityToolkit-WebUI.Tests", metadata!.AssemblyName);
        Assert.Contains(metadata.References, reference => reference.Equals("xunit.core", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void StructuredJsonParser_AcceptsCodeFenceAndRejectsMalformedResponse()
    {
        const string fenced = """
            ```json
            {"summary":"ok","findings":[]}
            ```
            """;

        Assert.True(PluginDiagnosticAgentService.TryDeserialize<Dictionary<string, object>>(fenced, out var parsed));
        Assert.NotNull(parsed);
        Assert.False(PluginDiagnosticAgentService.TryDeserialize<Dictionary<string, object>>(
            "analysis: {not-json}", out _));
    }

    [Fact]
    public void SelectCloudDiagnosticEndpoint_IgnoresTranslationSwitchAndStaleLocalEndpoint()
    {
        var ai = new AiTranslationSettings
        {
            Enabled = false,
            ActiveMode = "cloud",
            Endpoints =
            [
                new ApiEndpointConfig { Id = "local", Name = "本地", ApiKey = "local", Enabled = true, Priority = 99 },
                new ApiEndpointConfig { Id = "low", Name = "低优先级", ApiKey = "key-1", Enabled = true, Priority = 1 },
                new ApiEndpointConfig { Id = "high", Name = "高优先级", ApiKey = "key-2", Enabled = true, Priority = 8 }
            ]
        };

        var selected = PluginDiagnosticAgentService.SelectCloudDiagnosticEndpoint(ai);

        Assert.NotNull(selected);
        Assert.Equal("high", selected!.Id);
    }

    [Fact]
    public void DiagnosticRunGate_AllowsOnlyOneTaskPerGameAndExposesRunningState()
    {
        var agent = new PluginDiagnosticAgentService(
            null!, null!, null!, NullLogger<PluginDiagnosticAgentService>.Instance);
        const string gameId = "game";

        Assert.True(agent.TryBeginDiagnostic(gameId));
        Assert.False(agent.TryBeginDiagnostic(gameId));

        var running = agent.AttachCached(gameId, "fingerprint", BaseReport(HealthStatus.Healthy));
        Assert.Equal(PluginAnalysisState.Running, running.AnalysisState);
        Assert.Equal(HealthStatus.Unknown, running.Overall);

        agent.EndDiagnostic(gameId);
        Assert.True(agent.TryBeginDiagnostic(gameId));
        agent.EndDiagnostic(gameId);
    }

    [Fact]
    public void DetermineOverall_RequiresFreshRunBeforeHealthy()
    {
        var analysis = new PluginDiagnosticAnalysis(
            "未发现有证据的问题", [], [], "测试端点", DateTime.UtcNow);
        var historical = BaseReport(HealthStatus.Healthy) with
        {
            AnalysisState = PluginAnalysisState.Completed,
            Analysis = analysis,
            FreshRunVerified = false
        };
        var fresh = historical with { FreshRunVerified = true };
        var incomplete = fresh with { ObjectiveOverall = HealthStatus.Unknown };

        Assert.Equal(HealthStatus.Unknown,
            PluginDiagnosticAgentService.DetermineOverall(historical, includeAnalysis: true));
        Assert.Equal(HealthStatus.Healthy,
            PluginDiagnosticAgentService.DetermineOverall(fresh, includeAnalysis: true));
        Assert.Equal(HealthStatus.Unknown,
            PluginDiagnosticAgentService.DetermineOverall(incomplete, includeAnalysis: true));
    }

    [Fact]
    public void DetermineOverall_EvidenceBackedFindingAndObjectiveErrorTakePrecedence()
    {
        var finding = new PluginDiagnosticFinding(
            "finding", DiagnosticSeverity.Warning, DiagnosticConfidence.High,
            "加载", "插件加载警告", "日志记录了加载警告。", [],
            [new PluginDiagnosticEvidence("log", "日志", "BepInEx/LogOutput.log", 1, 1, "warning")]);
        var analysis = new PluginDiagnosticAnalysis("发现警告", [finding], [], "测试端点", DateTime.UtcNow);
        var warning = BaseReport(HealthStatus.Healthy) with
        {
            AnalysisState = PluginAnalysisState.Completed,
            Analysis = analysis,
            FreshRunVerified = true
        };
        var objectiveError = warning with { ObjectiveOverall = HealthStatus.Error };

        Assert.Equal(HealthStatus.Warning,
            PluginDiagnosticAgentService.DetermineOverall(warning, includeAnalysis: true));
        Assert.Equal(HealthStatus.Error,
            PluginDiagnosticAgentService.DetermineOverall(objectiveError, includeAnalysis: true));
    }

    [Fact]
    public void RepairAvailability_RequiresFreshCompletedEvidenceBackedMediumOrHighFinding()
    {
        var evidence = new PluginDiagnosticEvidence(
            "log", "日志", "BepInEx/LogOutput.log", 1, 1, "warning");
        var low = new PluginDiagnosticFinding(
            "low", DiagnosticSeverity.Warning, DiagnosticConfidence.Low,
            "加载", "低置信度", "说明", [], [evidence]);
        var highWithoutEvidence = new PluginDiagnosticFinding(
            "empty", DiagnosticSeverity.Error, DiagnosticConfidence.High,
            "加载", "无证据", "说明", [], []);
        var medium = new PluginDiagnosticFinding(
            "medium", DiagnosticSeverity.Warning, DiagnosticConfidence.Medium,
            "加载", "可修复", "说明", [], [evidence]);

        var report = BaseReport(HealthStatus.Healthy) with
        {
            AnalysisState = PluginAnalysisState.Completed,
            Analysis = new PluginDiagnosticAnalysis(
                "测试", [low, highWithoutEvidence], [], "测试端点", DateTime.UtcNow)
        };
        Assert.False(PluginAutoRepairService.HasEvidenceBackedAiRepair(report));

        report = report with
        {
            Analysis = report.Analysis with { Findings = [medium] }
        };
        Assert.True(PluginAutoRepairService.HasEvidenceBackedAiRepair(report));
        Assert.False(PluginAutoRepairService.HasEvidenceBackedAiRepair(
            report with { AnalysisState = PluginAnalysisState.Stale }));
    }

    private static PluginHealthReport BaseReport(HealthStatus objectiveOverall) => new(
        HealthStatus.Unknown,
        objectiveOverall,
        [],
        PluginAnalysisState.NotRun,
        null,
        null,
        null,
        false,
        false,
        DateTime.UtcNow);

    private static HealthCheckItem CheckDoorstop(string gamePath, bool isIl2Cpp)
    {
        var checks = new List<HealthCheckItem>();
        PluginHealthCheckService.CheckDoorstopProxy(checks, gamePath, isIl2Cpp);
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
