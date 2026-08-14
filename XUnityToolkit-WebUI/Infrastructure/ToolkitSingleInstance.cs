using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using XUnityToolkit_WebUI.Models;

namespace XUnityToolkit_WebUI.Infrastructure;

internal sealed class ToolkitSingleInstance : IDisposable
{
    private readonly FileStream _lockStream;
    private readonly string _lockPath;

    private ToolkitSingleInstance(FileStream lockStream, string lockPath)
    {
        _lockStream = lockStream;
        _lockPath = lockPath;
    }

    public static bool TryAcquire(string appDataRoot, out ToolkitSingleInstance? instance, out string? error)
    {
        instance = null;
        error = null;

        try
        {
            var normalizedRoot = Path.GetFullPath(appDataRoot)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                .ToUpperInvariant();
            var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalizedRoot)));
            var lockDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "XUnityToolkit", "instance-locks");
            Directory.CreateDirectory(lockDirectory);
            var lockPath = Path.Combine(lockDirectory, $"{hash}.lock");
            var stream = new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            instance = new ToolkitSingleInstance(stream, lockPath);
            return true;
        }
        catch (IOException)
        {
            error = "另一个工具箱实例正在使用同一数据目录。";
            return false;
        }
        catch (UnauthorizedAccessException ex)
        {
            error = $"无法创建单实例锁：{ex.Message}";
            return false;
        }
    }

    public static async Task<bool> TryActivateExistingAsync(
        string discoveryFile,
        TimeSpan timeout,
        CancellationToken ct = default)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(timeout);
        using var handler = ToolkitHttpHandlers.CreateLoopbackHandler();
        using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(2) };

        while (!timeoutCts.IsCancellationRequested)
        {
            try
            {
                if (File.Exists(discoveryFile))
                {
                    var json = await File.ReadAllTextAsync(discoveryFile, timeoutCts.Token);
                    var record = JsonSerializer.Deserialize<ToolkitEndpointDiscoveryRecord>(
                        json, FileHelper.DataJsonOptions);
                    if (record is not null
                        && record.SchemaVersion == ToolkitRuntimeEndpointState.DiscoverySchemaVersion
                        && record.Product == ToolkitRuntimeEndpointState.ProductName
                        && IsValidLoopbackBaseUrl(record.BaseUrl))
                    {
                        var ping = await client.GetFromJsonAsync<JsonElement>(
                            $"{record.BaseUrl}/api/translate/ping", timeoutCts.Token);
                        if (ping.TryGetProperty("product", out var product)
                            && product.GetString() == ToolkitRuntimeEndpointState.ProductName
                            && ping.TryGetProperty("instanceId", out var instanceId)
                            && instanceId.GetString() == record.InstanceId)
                        {
                            using var response = await client.PostAsJsonAsync(
                                $"{record.BaseUrl}/api/app/activate", new { }, timeoutCts.Token);
                            return response.IsSuccessStatusCode;
                        }
                    }
                }
            }
            catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested)
            {
                break;
            }
            catch
            {
                // Existing process may still be publishing discovery state. Retry until timeout.
            }

            try
            {
                await Task.Delay(250, timeoutCts.Token);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        return false;
    }

    public void Dispose()
    {
        _lockStream.Dispose();
        try { File.Delete(_lockPath); }
        catch { /* stale lock files are harmless; the open handle is the actual lock */ }
    }

    private static bool IsValidLoopbackBaseUrl(string value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri)
        && uri.Scheme == Uri.UriSchemeHttp
        && uri.Host == "127.0.0.1"
        && uri.Port is > 0 and <= 65535
        && (uri.AbsolutePath == "/" || string.IsNullOrEmpty(uri.AbsolutePath));
}
