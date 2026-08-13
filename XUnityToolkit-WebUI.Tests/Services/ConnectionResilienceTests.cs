using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Reflection;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using XUnityToolkit_WebUI.Infrastructure;
using XUnityToolkit_WebUI.Models;
using XUnityToolkit_WebUI.Services;
using Xunit;

namespace XUnityToolkit_WebUI.Tests.Services;

public sealed class ConnectionResilienceTests
{
    [Fact]
    public void RuntimeEndpoint_UsesPreferredPortWhenAvailable()
    {
        var preferredPort = ReserveAvailablePort();
        var state = new ToolkitRuntimeEndpointState(preferredPort);

        using var socket = state.CreateBoundListenSocket(
            new IPEndPoint(IPAddress.Loopback, preferredPort));

        Assert.Equal(preferredPort, state.ActualPort);
        Assert.False(state.UsedFallback);
        Assert.Equal($"http://127.0.0.1:{preferredPort}", state.BaseUrl);
    }

    [Fact]
    public void RuntimeEndpoint_FallsBackAtomicallyWhenPreferredPortIsOccupied()
    {
        using var occupied = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp)
        {
            ExclusiveAddressUse = true
        };
        occupied.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        occupied.Listen(1);
        var preferredPort = ((IPEndPoint)occupied.LocalEndPoint!).Port;
        var state = new ToolkitRuntimeEndpointState(preferredPort);

        using var fallback = state.CreateBoundListenSocket(
            new IPEndPoint(IPAddress.Loopback, preferredPort));

        Assert.NotEqual(preferredPort, state.ActualPort);
        Assert.True(state.UsedFallback);
        Assert.Equal("PreferredPortInUse", state.FallbackReason);
        Assert.Equal(IPAddress.Loopback, ((IPEndPoint)fallback.LocalEndPoint!).Address);
    }

    [Fact]
    public async Task DiscoveryFile_UsesCustomRootAndOnlyOwnerCanDeleteIt()
    {
        using var temp = new TemporaryDirectory();
        var root = Path.Combine(temp.Path, "custom-data");
        var paths = CreatePaths(root);
        paths.EnsureDirectoriesExist();
        var state = BindRuntimeEndpoint();
        var service = new ToolkitRuntimeDiscoveryService(
            paths,
            state,
            new ThrowingHttpClientFactory(),
            NullLogger<ToolkitRuntimeDiscoveryService>.Instance);

        await service.PublishAsync();

        Assert.Equal(Path.Combine(root, "runtime", "toolbox-endpoint-v1.json"), paths.ToolkitEndpointDiscoveryFile);
        var published = JsonSerializer.Deserialize<ToolkitEndpointDiscoveryRecord>(
            await File.ReadAllTextAsync(paths.ToolkitEndpointDiscoveryFile),
            FileHelper.DataJsonOptions);
        Assert.NotNull(published);
        Assert.Equal(state.InstanceId, published.InstanceId);
        Assert.Equal(state.ActualPort, published.ActualPort);

        var anotherInstance = published with { InstanceId = Guid.NewGuid().ToString("N") };
        await FileHelper.WriteJsonAtomicAsync(paths.ToolkitEndpointDiscoveryFile, anotherInstance);
        service.DeleteOwnedDiscoveryFile();
        Assert.True(File.Exists(paths.ToolkitEndpointDiscoveryFile));

        await service.PublishAsync();
        service.DeleteOwnedDiscoveryFile();
        Assert.False(File.Exists(paths.ToolkitEndpointDiscoveryFile));

        await File.WriteAllTextAsync(paths.ToolkitEndpointDiscoveryFile, "{ damaged json");
        service.DeleteOwnedDiscoveryFile();
        Assert.True(File.Exists(paths.ToolkitEndpointDiscoveryFile));
    }

    [Fact]
    public void SingleInstance_IsScopedByNormalizedDataRoot()
    {
        using var temp = new TemporaryDirectory();
        var firstRoot = Path.Combine(temp.Path, "one");
        var secondRoot = Path.Combine(temp.Path, "two");

        Assert.True(ToolkitSingleInstance.TryAcquire(firstRoot, out var first, out _));
        using (first)
        {
            Assert.False(ToolkitSingleInstance.TryAcquire(firstRoot, out var duplicate, out _));
            Assert.Null(duplicate);

            Assert.True(ToolkitSingleInstance.TryAcquire(secondRoot, out var independent, out _));
            independent?.Dispose();
        }
    }

    [Fact]
    public async Task SecondInstance_ValidatesDiscoveryAndActivatesExistingInstanceWithoutProxy()
    {
        using var temp = new TemporaryDirectory();
        var instanceId = Guid.NewGuid().ToString("N");
        await using var server = await TinyProtocolServer.StartAsync(instanceId);
        var discoveryFile = Path.Combine(temp.Path, "runtime", "toolbox-endpoint-v1.json");
        await FileHelper.WriteJsonAtomicAsync(discoveryFile, new ToolkitEndpointDiscoveryRecord(
            1,
            ToolkitRuntimeEndpointState.ProductName,
            1,
            instanceId,
            Environment.ProcessId,
            server.BaseUrl,
            server.Port,
            server.Port,
            false,
            null,
            DateTime.UtcNow));

        var activated = await ToolkitSingleInstance.TryActivateExistingAsync(
            discoveryFile,
            TimeSpan.FromSeconds(5));

        Assert.True(activated, $"Requests: {string.Join(", ", server.SeenRequests)}; Error: {server.Error}");
        Assert.Equal(["GET /api/translate/ping", "POST /api/app/activate"], await server.Requests);
    }

    [Fact]
    public async Task DuplicateTranslationRequest_IsExecutedOnlyOnce()
    {
        using var lifetime = new TestApplicationLifetime();
        var coordinator = new TranslationRequestCoordinator(lifetime);
        var operationStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseOperation = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var executions = 0;

        async Task<string> Operation(CancellationToken ct)
        {
            Interlocked.Increment(ref executions);
            operationStarted.TrySetResult();
            await releaseOperation.Task.WaitAsync(ct);
            return "translated";
        }

        var first = coordinator.ExecuteAsync("session", "request", Operation, CancellationToken.None);
        await operationStarted.Task;
        var retry = coordinator.ExecuteAsync("session", "request", Operation, CancellationToken.None);
        await Task.Yield();

        Assert.Equal(1, Volatile.Read(ref executions));
        releaseOperation.TrySetResult();
        Assert.Equal("translated", await first);
        Assert.Equal("translated", await retry);
        Assert.Equal(1, executions);
    }

    [Fact]
    public void HeartbeatRegistry_ExpiresOnlineStatusAfterThirtySeconds()
    {
        var clock = new ManualTimeProvider(new DateTimeOffset(2026, 8, 13, 0, 0, 0, TimeSpan.Zero));
        var registry = new PluginConnectionRegistry(clock);
        var gameId = Guid.NewGuid().ToString("N");
        registry.RecordHeartbeat(gameId, "session", "2.0.0", true, true, "http://127.0.0.1:51821");

        Assert.Equal(1, registry.GetSummary().ConnectedCount);

        clock.Advance(TimeSpan.FromSeconds(31));

        Assert.Equal(0, registry.GetSummary().ConnectedCount);
        Assert.NotNull(registry.GetLatest(gameId));
    }

    [Fact]
    public void ProxyPolicy_IsDisabledOnlyForLoopbackProtocolClient()
    {
        using var loopback = ToolkitHttpHandlers.CreateLoopbackHandler();
        using var cloud = ToolkitHttpHandlers.CreateCloudLlmHandler();

        Assert.False(loopback.UseProxy);
        Assert.True(cloud.UseProxy);
    }

    [Fact]
    public async Task UnreachableProxy_IsIgnoredByLoopbackPingAndTranslationClients()
    {
        var instanceId = Guid.NewGuid().ToString("N");
        await using var server = await TinyProtocolServer.StartAsync(instanceId);
        var unreachableProxy = new WebProxy($"http://127.0.0.1:{ReserveAvailablePort()}");

        using (var handler = ToolkitHttpHandlers.CreateLoopbackHandler())
        {
            handler.Proxy = unreachableProxy;
            using var http = new HttpClient(handler);
            var ping = await http.GetStringAsync($"{server.BaseUrl}/api/translate/ping");
            Assert.Contains("XUnityToolkit", ping, StringComparison.Ordinal);
        }

        var endpointAssembly = LoadTranslatorEndpointAssembly();
        var clientType = endpointAssembly.GetType("LLMTranslate.DirectWebClient", throwOnError: true)!;
        using var endpointClient = (WebClient)Activator.CreateInstance(
            clientType,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            binder: null,
            args: [2000],
            culture: null)!;
        endpointClient.Proxy = unreachableProxy;
        endpointClient.Headers[HttpRequestHeader.ContentType] = "application/json";
        var translated = endpointClient.UploadString(
            $"{server.BaseUrl}/api/translate",
            "POST",
            "{\"texts\":[\"Hello\"]}");

        Assert.Contains("translated", translated, StringComparison.Ordinal);
    }

    [Fact]
    public void TranslatorEndpoint_WebClientsExplicitlyDisableProxy()
    {
        var endpointAssembly = LoadTranslatorEndpointAssembly();
        foreach (var typeName in new[] { "LLMTranslate.DirectWebClient", "LLMTranslate.DirectXUnityWebClient" })
        {
            var type = endpointAssembly.GetType(typeName, throwOnError: true)!;
            var instance = Activator.CreateInstance(
                type,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                binder: null,
                args: [1000],
                culture: null);
            var proxy = type.GetProperty("Proxy", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            Assert.NotNull(proxy);
            Assert.Null(proxy.GetValue(instance));
            (instance as IDisposable)?.Dispose();
        }
    }

    [Theory]
    [InlineData(WebExceptionStatus.ConnectFailure, "ConnectionRefusedOrUnavailable")]
    [InlineData(WebExceptionStatus.Timeout, "TimeoutPossibleTunnelOrSecuritySoftware")]
    [InlineData(WebExceptionStatus.ProtocolError, "UnexpectedHttpService")]
    public void TranslatorEndpoint_ClassifiesConnectionFailures(
        WebExceptionStatus status,
        string expected)
    {
        var manager = LoadTranslatorEndpointAssembly()
            .GetType("LLMTranslate.ToolkitConnectionManager", throwOnError: true)!;
        var classify = manager.GetMethod(
            "ClassifyFailure",
            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)!;

        var actual = (string)classify.Invoke(
            null,
            [new WebException("test", status)])!;

        Assert.Equal(expected, actual);
    }

    [Fact]
    public async Task TranslatorEndpoint_RecoversAfterLateStartAndRuntimePortChange()
    {
        using var temp = new TemporaryDirectory();
        var endpointAssembly = LoadTranslatorEndpointAssembly();
        var managerType = endpointAssembly.GetType("LLMTranslate.ToolkitConnectionManager", throwOnError: true)!;
        var deadPort = ReserveAvailablePort();
        var discoveryFile = Path.Combine(temp.Path, "toolbox-endpoint-v1.json");
        await File.WriteAllTextAsync(discoveryFile, "{ damaged discovery");
        var messages = new System.Collections.Concurrent.ConcurrentQueue<string>();
        var manager = Activator.CreateInstance(
            managerType,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            binder: null,
            args:
            [
                $"http://127.0.0.1:{deadPort}",
                discoveryFile,
                Guid.NewGuid().ToString("N"),
                Guid.NewGuid().ToString("N"),
                "2.0.0",
                (Action<string>)(message => messages.Enqueue(message)),
                (Action<string>)(message => messages.Enqueue(message)),
            ],
            culture: null)!;
        var start = managerType.GetMethod("Start", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!;
        var currentBaseUrl = managerType.GetProperty("CurrentBaseUrl", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!;
        var markFailure = managerType.GetMethod("MarkTransportFailure", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!;
        var timerField = managerType.GetField("_timer", BindingFlags.Instance | BindingFlags.NonPublic)!;

        TinyProtocolServer? firstServer = null;
        TinyProtocolServer? secondServer = null;
        try
        {
            start.Invoke(manager, null);
            await WaitUntilAsync(
                () => messages.Any(message => message.Contains("发现文件无效或已过期", StringComparison.Ordinal)),
                TimeSpan.FromSeconds(2));

            var firstInstance = Guid.NewGuid().ToString("N");
            firstServer = await TinyProtocolServer.StartAsync(firstInstance);
            await WriteDiscoveryAsync(discoveryFile, firstServer, firstInstance);
            await WaitUntilAsync(
                () => string.Equals((string?)currentBaseUrl.GetValue(manager), firstServer.BaseUrl, StringComparison.Ordinal),
                TimeSpan.FromSeconds(5));

            var firstUrl = firstServer.BaseUrl;
            await firstServer.DisposeAsync();
            firstServer = null;
            markFailure.Invoke(manager,
            [
                firstUrl,
                new WebException("connection closed", WebExceptionStatus.ConnectionClosed),
            ]);
            Assert.Null((string?)currentBaseUrl.GetValue(manager));

            var secondInstance = Guid.NewGuid().ToString("N");
            secondServer = await TinyProtocolServer.StartAsync(secondInstance);
            await WriteDiscoveryAsync(discoveryFile, secondServer, secondInstance);
            try
            {
                await WaitUntilAsync(
                    () => string.Equals((string?)currentBaseUrl.GetValue(manager), secondServer.BaseUrl, StringComparison.Ordinal),
                    TimeSpan.FromSeconds(6));
            }
            catch (OperationCanceledException)
            {
                Assert.Fail($"Endpoint did not recover. Current={currentBaseUrl.GetValue(manager)}; Logs={string.Join(" | ", messages)}");
            }

            Assert.Contains(messages, message => message.Contains("本机直连=是", StringComparison.Ordinal));
        }
        finally
        {
            (timerField.GetValue(manager) as Timer)?.Dispose();
            if (firstServer is not null) await firstServer.DisposeAsync();
            if (secondServer is not null) await secondServer.DisposeAsync();
        }
    }

    [Fact]
    public void OfficialMetadata_RecognizesLegacyHashAndRejectsUnknownHash()
    {
        var installer = new XUnityInstallerService(NullLogger<XUnityInstallerService>.Instance);

        Assert.Equal(
            TranslatorEndpointOrigin.OfficialOutdated,
            installer.ClassifyHash("438E48A43C964D1A44CCA0D5589DC6E1F4C1B91F19833C1142D20F4FCF0849FA"));
        Assert.Equal(
            TranslatorEndpointOrigin.UnknownOrCustom,
            installer.ClassifyHash(new string('A', 64)));
    }

    [Fact]
    public void UnknownEndpoint_IsPreservedUntilExplicitReplacement()
    {
        using var temp = new TemporaryDirectory();
        var game = new Game { Name = "Test", GamePath = temp.Path };
        var endpointPath = Path.Combine(
            temp.Path,
            "BepInEx",
            "plugins",
            "XUnity.AutoTranslator",
            "Translators",
            "LLMTranslate.dll");
        Directory.CreateDirectory(Path.GetDirectoryName(endpointPath)!);
        File.WriteAllText(endpointPath, "custom endpoint");
        var installer = new XUnityInstallerService(NullLogger<XUnityInstallerService>.Instance);

        var preserved = installer.EnsureTranslatorEndpoint(game);

        Assert.Equal(TranslatorEndpointOrigin.UnknownOrCustom, preserved.Origin);
        Assert.Equal("custom endpoint", File.ReadAllText(endpointPath));

        var replaced = installer.EnsureTranslatorEndpoint(game, forceReplaceUnknown: true);

        Assert.Equal(TranslatorEndpointOrigin.OfficialCurrent, replaced.Origin);
        Assert.True(replaced.AutoDiscoverySupported);
        Assert.True(replaced.DirectConnectionMode);
        Assert.NotEqual("custom endpoint", File.ReadAllText(endpointPath));
    }

    private static ToolkitRuntimeEndpointState BindRuntimeEndpoint()
    {
        var state = new ToolkitRuntimeEndpointState(ReserveAvailablePort());
        using (state.CreateBoundListenSocket(new IPEndPoint(IPAddress.Loopback, state.PreferredPort)))
        {
        }
        return state;
    }

    private static Assembly LoadTranslatorEndpointAssembly()
    {
        var root = FindRepositoryRoot();
        var libs = Path.Combine(root, "TranslatorEndpoint", "libs");
        Assembly.LoadFrom(Path.Combine(libs, "XUnity.Common.dll"));
        Assembly.LoadFrom(Path.Combine(libs, "XUnity.AutoTranslator.Plugin.Core.dll"));
        return Assembly.LoadFrom(Path.Combine(
            root,
            "TranslatorEndpoint",
            "bin",
            "Release",
            "net35",
            "LLMTranslate.dll"));
    }

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "TranslatorEndpoint", "TranslatorEndpoint.csproj")))
                return directory.FullName;
        }

        throw new DirectoryNotFoundException("Could not locate the repository root.");
    }

    private static Task WriteDiscoveryAsync(string path, TinyProtocolServer server, string instanceId) =>
        FileHelper.WriteJsonAtomicAsync(path, new ToolkitEndpointDiscoveryRecord(
            1,
            ToolkitRuntimeEndpointState.ProductName,
            1,
            instanceId,
            Environment.ProcessId,
            server.BaseUrl,
            server.Port,
            server.Port,
            false,
            null,
            DateTime.UtcNow));

    private static async Task WaitUntilAsync(Func<bool> predicate, TimeSpan timeout)
    {
        using var cts = new CancellationTokenSource(timeout);
        while (!predicate())
            await Task.Delay(50, cts.Token);
    }

    private static int ReserveAvailablePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    private static AppDataPaths CreatePaths(string root)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["AppData:Root"] = root })
            .Build();
        return new AppDataPaths(configuration);
    }

    private sealed class ThrowingHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) =>
            throw new InvalidOperationException("This test only publishes discovery state.");
    }

    private sealed class ManualTimeProvider(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset _now = now;
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan duration) => _now += duration;
    }

    private sealed class TestApplicationLifetime : IHostApplicationLifetime, IDisposable
    {
        private readonly CancellationTokenSource _started = new();
        private readonly CancellationTokenSource _stopping = new();
        private readonly CancellationTokenSource _stopped = new();
        public CancellationToken ApplicationStarted => _started.Token;
        public CancellationToken ApplicationStopping => _stopping.Token;
        public CancellationToken ApplicationStopped => _stopped.Token;
        public void StopApplication() => _stopping.Cancel();
        public void Dispose()
        {
            _started.Dispose();
            _stopping.Dispose();
            _stopped.Dispose();
        }
    }

    private sealed class TinyProtocolServer : IAsyncDisposable
    {
        private readonly WebApplication _app;
        private readonly System.Collections.Concurrent.ConcurrentQueue<string> _requests;

        private TinyProtocolServer(
            WebApplication app,
            int port,
            System.Collections.Concurrent.ConcurrentQueue<string> requests)
        {
            _app = app;
            Port = port;
            _requests = requests;
        }

        public int Port { get; }
        public string BaseUrl => $"http://127.0.0.1:{Port}";
        public string[] SeenRequests => _requests.ToArray();
        public string? Error => null;
        public Task<string[]> Requests => Task.FromResult(SeenRequests);

        public static async Task<TinyProtocolServer> StartAsync(string instanceId)
        {
            var requests = new System.Collections.Concurrent.ConcurrentQueue<string>();
            var builder = WebApplication.CreateSlimBuilder();
            builder.WebHost.ConfigureKestrel(options => options.Listen(IPAddress.Loopback, 0));
            var app = builder.Build();
            app.MapGet("/api/translate/ping", () =>
            {
                requests.Enqueue("GET /api/translate/ping");
                var addresses = app.Services.GetRequiredService<IServer>()
                    .Features.Get<IServerAddressesFeature>()!.Addresses;
                var baseUrl = addresses.Single();
                return Results.Ok(new
                {
                    status = "ok",
                    product = ToolkitRuntimeEndpointState.ProductName,
                    protocolVersion = 1,
                    instanceId,
                    baseUrl,
                });
            });
            app.MapPost("/api/app/activate", () =>
            {
                requests.Enqueue("POST /api/app/activate");
                return Results.Ok();
            });
            app.MapPost("/api/translate", () => Results.Ok(new
            {
                translations = new[] { "translated" }
            }));
            await app.StartAsync();
            var address = app.Services.GetRequiredService<IServer>()
                .Features.Get<IServerAddressesFeature>()!.Addresses.Single();
            return new TinyProtocolServer(app, new Uri(address).Port, requests);
        }

        public async ValueTask DisposeAsync()
        {
            await _app.StopAsync();
            await _app.DisposeAsync();
        }
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"xunitytoolkit-connection-tests-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path))
                Directory.Delete(Path, recursive: true);
        }
    }
}
