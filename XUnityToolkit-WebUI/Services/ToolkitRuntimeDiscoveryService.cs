using System.Text.Json;
using XUnityToolkit_WebUI.Infrastructure;
using XUnityToolkit_WebUI.Models;

namespace XUnityToolkit_WebUI.Services;

public sealed class ToolkitRuntimeDiscoveryService(
    AppDataPaths paths,
    ToolkitRuntimeEndpointState runtimeEndpoint,
    IHttpClientFactory httpClientFactory,
    ILogger<ToolkitRuntimeDiscoveryService> logger)
{
    public async Task PublishAndVerifyAsync(CancellationToken ct = default)
    {
        runtimeEndpoint.MarkStarted();
        await PublishAsync(ct);
        await VerifyLoopbackAsync(ct);
    }

    public Task PublishAsync(CancellationToken ct = default)
    {
        var record = new ToolkitEndpointDiscoveryRecord(
            ToolkitRuntimeEndpointState.DiscoverySchemaVersion,
            ToolkitRuntimeEndpointState.ProductName,
            ToolkitRuntimeEndpointState.ProtocolVersion,
            runtimeEndpoint.InstanceId,
            Environment.ProcessId,
            runtimeEndpoint.BaseUrl,
            runtimeEndpoint.PreferredPort,
            runtimeEndpoint.ActualPort,
            runtimeEndpoint.UsedFallback,
            runtimeEndpoint.FallbackReason,
            runtimeEndpoint.StartedAtUtc);

        return FileHelper.WriteJsonAtomicAsync(paths.ToolkitEndpointDiscoveryFile, record, ct: ct);
    }

    public async Task VerifyLoopbackAsync(CancellationToken ct = default)
    {
        try
        {
            var client = httpClientFactory.CreateClient("ToolkitLoopback");
            using var response = await client.GetAsync(
                $"{runtimeEndpoint.BaseUrl}/api/translate/ping", ct);
            response.EnsureSuccessStatusCode();
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
            var root = document.RootElement;
            var productMatches = root.TryGetProperty("product", out var product)
                && product.GetString() == ToolkitRuntimeEndpointState.ProductName;
            var instanceMatches = root.TryGetProperty("instanceId", out var instanceId)
                && instanceId.GetString() == runtimeEndpoint.InstanceId;
            if (!productMatches || !instanceMatches)
                throw new InvalidDataException("环回响应不是当前工具箱实例。");

            runtimeEndpoint.RecordLoopbackSelfTest(true, null);
            logger.LogInformation("本机直连自检通过: {BaseUrl}", runtimeEndpoint.BaseUrl);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            runtimeEndpoint.RecordLoopbackSelfTest(false, "LoopbackRequestFailed");
            logger.LogError(ex, "本机直连自检失败: {BaseUrl}", runtimeEndpoint.BaseUrl);
        }
    }

    public void DeleteOwnedDiscoveryFile()
    {
        try
        {
            if (!File.Exists(paths.ToolkitEndpointDiscoveryFile))
                return;

            using var document = JsonDocument.Parse(File.ReadAllText(paths.ToolkitEndpointDiscoveryFile));
            if (document.RootElement.TryGetProperty("instanceId", out var instanceId)
                && instanceId.GetString() == runtimeEndpoint.InstanceId)
            {
                File.Delete(paths.ToolkitEndpointDiscoveryFile);
            }
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "清理运行时端点发现文件失败");
        }
    }
}
