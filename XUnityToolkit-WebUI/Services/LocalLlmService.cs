using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO.Compression;
using System.Management;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using Microsoft.AspNetCore.SignalR;
using XUnityToolkit_WebUI.Hubs;
using XUnityToolkit_WebUI.Infrastructure;
using XUnityToolkit_WebUI.Models;

namespace XUnityToolkit_WebUI.Services;

public sealed class LocalLlmService(
    IHttpClientFactory httpClientFactory,
    AppSettingsService settingsService,
    AppDataPaths paths,
    BundledAssetPaths bundledPaths,
    IHubContext<InstallProgressHub> hubContext,
    SystemTrayService trayService,
    ILogger<LocalLlmService> logger)
{
    private readonly SemaphoreSlim _stateLock = new(1, 1);
    private Process? _process;
    private volatile LocalLlmServerState _state = LocalLlmServerState.Idle;
    public bool IsRunning => _state == LocalLlmServerState.Running;
    private string? _error;
    private int _internalPort;
    private GpuBackend _activeBackend;
    private LocalLlmSettings? _settingsCache;

    // Download tracking (model downloads only)
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _downloads = new();
    private readonly ConcurrentDictionary<string, bool> _pauseRequests = new();
    private readonly ConcurrentDictionary<string, bool> _downloadMirrorState = new();

    // GPU cache
    private List<GpuInfo>? _gpuCache;

    // GPU monitoring (nvidia-smi)
    private volatile int _gpuUtilization;
    private volatile int _gpuVramUsed;   // MB
    private volatile int _gpuVramTotal;  // MB — -1 means never polled yet
    private CancellationTokenSource? _gpuPollCts;

    // ── llama.cpp binary constants ──

    public const string LlamaVersion = "b8354";

    // ── Settings persistence ──

    public async Task<LocalLlmSettings> LoadSettingsAsync(CancellationToken ct = default)
    {
        if (_settingsCache is not null) return _settingsCache;

        if (!File.Exists(paths.LocalLlmSettingsFile))
        {
            _settingsCache = new LocalLlmSettings();
            return _settingsCache;
        }

        var json = await File.ReadAllTextAsync(paths.LocalLlmSettingsFile, ct);
        _settingsCache = JsonSerializer.Deserialize<LocalLlmSettings>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        }) ?? new LocalLlmSettings();
        return _settingsCache;
    }

    public async Task SaveSettingsAsync(LocalLlmSettings settings, CancellationToken ct = default)
    {
        _settingsCache = settings;
        var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });
        await File.WriteAllTextAsync(paths.LocalLlmSettingsFile, json, ct);
    }

    /// <summary>Merges user-editable fields without overwriting Models/PausedDownloads.</summary>
    public async Task UpdateUserSettingsAsync(Endpoints.UpdateLocalLlmSettingsRequest req, CancellationToken ct = default)
    {
        var settings = await LoadSettingsAsync(ct);
        settings.GpuLayers = req.GpuLayers;
        settings.ContextLength = req.ContextLength;
        await SaveSettingsAsync(settings, ct);
    }

    // ── GPU Detection ──

    public Task<List<GpuInfo>> DetectGpusAsync()
    {
        if (_gpuCache is not null) return Task.FromResult(_gpuCache);

        List<GpuInfo> results;
        try
        {
            // DXGI provides accurate 64-bit VRAM (unlike WMI uint32 cap at 4GB)
            results = DxgiGpuDetector.DetectGpus();
            if (results.Count > 0)
            {
                logger.LogInformation("DXGI 检测到 {Count} 个 GPU", results.Count);
                _gpuCache = results;
                return Task.FromResult(results);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "DXGI GPU 检测失败，回退到 WMI");
        }

        // Fallback: WMI (AdapterRAM caps at 4GB for >4GB GPUs)
        results = [];
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT Name, AdapterRAM FROM Win32_VideoController");
            foreach (ManagementObject mo in searcher.Get())
            {
                var name = mo["Name"]?.ToString() ?? "";
                var adapterRam = Convert.ToInt64(mo["AdapterRAM"] ?? 0);
                if (adapterRam <= 0) adapterRam = 0;

                var vendor = name.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase) ? "NVIDIA"
                    : name.Contains("AMD", StringComparison.OrdinalIgnoreCase)
                      || name.Contains("Radeon", StringComparison.OrdinalIgnoreCase) ? "AMD"
                    : name.Contains("Intel", StringComparison.OrdinalIgnoreCase) ? "Intel"
                    : "Other";

                var backend = vendor switch
                {
                    "NVIDIA" => GpuBackend.CUDA,
                    "AMD" or "Intel" => GpuBackend.Vulkan,
                    _ => GpuBackend.CPU
                };

                results.Add(new GpuInfo(name, vendor, adapterRam, backend));
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "WMI GPU 检测失败");
        }

        _gpuCache = results;
        return Task.FromResult(results);
    }

    public void ClearGpuCache() => _gpuCache = null;

    public GpuBackend GetBestBackend(List<GpuInfo> gpus)
    {
        if (gpus.Any(g => g.Vendor == "NVIDIA"))
            return GpuBackend.CUDA;
        if (gpus.Any(g => g.Vendor is "AMD" or "Intel"))
            return GpuBackend.Vulkan;
        return GpuBackend.CPU;
    }

    // ── llama.cpp Binary Management (bundled) ──

    public async Task<LlamaStatus> GetLlamaStatusAsync(CancellationToken ct = default)
    {
        var gpus = await DetectGpusAsync();
        var recommended = GetBestBackend(gpus);
        var backends = new List<LlamaBackendInfo>();

        foreach (var backend in new[] { GpuBackend.CUDA, GpuBackend.Vulkan, GpuBackend.CPU })
        {
            var serverPath = GetLlamaServerPath(backend);
            var isInstalled = File.Exists(serverPath) || BundledLlamaZipExists(backend);

            backends.Add(new LlamaBackendInfo(
                backend.ToString(),
                isInstalled,
                isInstalled ? LlamaVersion : ""));
        }

        return new LlamaStatus(LlamaVersion, backends, recommended);
    }

    /// <summary>
    /// Ensures the llama.cpp binary for the given backend is extracted from the bundled ZIP.
    /// Called automatically before starting the server.
    /// </summary>
    private async Task EnsureLlamaExtractedAsync(GpuBackend backend)
    {
        var serverPath = GetLlamaServerPath(backend);
        if (File.Exists(serverPath)) return;

        // Find and extract the main backend ZIP
        var zipPattern = backend switch
        {
            GpuBackend.CUDA => "llama-*-bin-win-cuda-*.zip",
            GpuBackend.Vulkan => "llama-*-bin-win-vulkan-*.zip",
            GpuBackend.CPU => "llama-*-bin-win-cpu-*.zip",
            _ => throw new InvalidOperationException($"未知后端: {backend}")
        };

        var zip = bundledPaths.FindLlamaZip(zipPattern)
            ?? throw new InvalidOperationException(
                $"未找到捆绑的 llama.cpp {backend} ZIP。请重新构建发布版本。");

        var destDir = Path.GetDirectoryName(serverPath)!;
        Directory.CreateDirectory(destDir);
        await ExtractLlamaZipAsync(zip, destDir);

        // Also extract CUDA runtime if applicable
        if (backend == GpuBackend.CUDA)
        {
            var cudaRtZip = bundledPaths.FindLlamaZip("cudart-*.zip");
            if (cudaRtZip is not null)
                await ExtractLlamaZipAsync(cudaRtZip, destDir);
        }

        logger.LogInformation("llama.cpp {Backend} 后端已从捆绑包解压", backend);
    }

    private static Task ExtractLlamaZipAsync(string zipPath, string destDir)
    {
        return Task.Run(() =>
        {
            using var archive = ZipFile.OpenRead(zipPath);
            foreach (var entry in archive.Entries)
            {
                var fileName = Path.GetFileName(entry.FullName);
                if (string.IsNullOrEmpty(fileName)) continue;

                var isServer = fileName.Equals("llama-server.exe", StringComparison.OrdinalIgnoreCase);
                var isDll = Path.GetExtension(fileName).Equals(".dll", StringComparison.OrdinalIgnoreCase);
                if (!isServer && !isDll) continue;

                entry.ExtractToFile(Path.Combine(destDir, fileName), overwrite: true);
            }
        });
    }

    private bool BundledLlamaZipExists(GpuBackend backend)
    {
        var pattern = backend switch
        {
            GpuBackend.CUDA => "llama-*-bin-win-cuda-*.zip",
            GpuBackend.Vulkan => "llama-*-bin-win-vulkan-*.zip",
            GpuBackend.CPU => "llama-*-bin-win-cpu-*.zip",
            _ => ""
        };
        return bundledPaths.FindLlamaZip(pattern) is not null;
    }

    // ── Process Management ──

    public LocalLlmStatus GetStatus()
    {
        var settings = _settingsCache;
        string? modelName = null;
        if (settings?.LoadedModelPath is not null)
            modelName = Path.GetFileNameWithoutExtension(settings.LoadedModelPath);

        bool hasMetrics = _state == LocalLlmServerState.Running
            && _activeBackend == GpuBackend.CUDA
            && _gpuVramTotal > 0;

        return new LocalLlmStatus(
            _state,
            settings?.LoadedModelPath,
            modelName,
            _state == LocalLlmServerState.Running ? _activeBackend.ToString() : null,
            _error,
            hasMetrics ? _gpuUtilization : null,
            hasMetrics ? _gpuVramUsed : null,
            hasMetrics ? _gpuVramTotal : null);
    }

    public async Task<LocalLlmStatus> StartAsync(string modelPath, int gpuLayers, int contextLength, CancellationToken ct)
    {
        await _stateLock.WaitAsync(ct);
        try
        {
            if (_state == LocalLlmServerState.Running)
            {
                // Already running — stop first
                await StopInternalAsync();
            }

            _state = LocalLlmServerState.Starting;
            _error = null;
            await BroadcastStatus();

            // Validate model file
            if (!File.Exists(modelPath))
            {
                _state = LocalLlmServerState.Failed;
                _error = $"模型文件不存在: {modelPath}";
                await BroadcastStatus();
                return GetStatus();
            }

            // Detect GPU and select backend
            var gpus = await DetectGpusAsync();
            _activeBackend = GetBestBackend(gpus);

            // Ensure llama binary is extracted from bundled ZIP
            await EnsureLlamaExtractedAsync(_activeBackend);

            // Find llama-server binary
            var serverPath = GetLlamaServerPath(_activeBackend);
            if (!File.Exists(serverPath))
            {
                _state = LocalLlmServerState.Failed;
                _error = $"llama-server.exe 未找到: {serverPath}";
                await BroadcastStatus();
                return GetStatus();
            }

            // Find available port
            _internalPort = GetAvailablePort();

            // Build arguments (--reasoning-budget 0 disables thinking in reasoning models)
            var args = $"-m \"{modelPath}\" --host 127.0.0.1 --port {_internalPort} -ngl {gpuLayers} -c {contextLength} --log-disable --no-webui --reasoning-budget 0";

            logger.LogInformation("启动 llama-server: {Path} {Args}", serverPath, args);

            _process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = serverPath,
                    Arguments = args,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    WorkingDirectory = Path.GetDirectoryName(serverPath)!
                },
                EnableRaisingEvents = true
            };

            _process.OutputDataReceived += (_, e) =>
            {
                if (e.Data is not null) logger.LogDebug("[llama-server] {Line}", e.Data);
            };
            _process.ErrorDataReceived += (_, e) =>
            {
                if (e.Data is not null) logger.LogDebug("[llama-server] {Line}", e.Data);
            };
            _process.Exited += OnProcessExited;

            _process.Start();
            _process.BeginOutputReadLine();
            _process.BeginErrorReadLine();

            // Health check loop
            var healthy = await WaitForHealthAsync(_internalPort, ct);
            if (!healthy)
            {
                _state = LocalLlmServerState.Failed;
                _error = "llama-server 启动超时（30秒内未就绪）";
                try { _process.Kill(true); } catch { /* ignore */ }
                _process = null;
                await BroadcastStatus();
                return GetStatus();
            }

            _state = LocalLlmServerState.Running;
            logger.LogInformation("llama-server 已启动，端口 {Port}，后端 {Backend}", _internalPort, _activeBackend);

            // Start GPU monitoring for NVIDIA
            if (_activeBackend == GpuBackend.CUDA)
                StartGpuPolling();

            // Auto-register as AI translation endpoint
            await RegisterEndpointAsync(ct);

            // Save loaded model path
            var settings = await LoadSettingsAsync(ct);
            settings.LoadedModelPath = modelPath;
            settings.GpuLayers = gpuLayers;
            settings.ContextLength = contextLength;
            await SaveSettingsAsync(settings, ct);

            await BroadcastStatus();
            return GetStatus();
        }
        catch (Exception ex)
        {
            _state = LocalLlmServerState.Failed;
            _error = ex.Message;
            logger.LogError(ex, "启动 llama-server 失败");
            await BroadcastStatus();
            return GetStatus();
        }
        finally
        {
            _stateLock.Release();
        }
    }

    public async Task<LocalLlmStatus> StopAsync(CancellationToken ct)
    {
        await _stateLock.WaitAsync(ct);
        try
        {
            await StopInternalAsync();
            await UnregisterEndpointAsync(ct);
            await BroadcastStatus();
            return GetStatus();
        }
        finally
        {
            _stateLock.Release();
        }
    }

    private async Task StopInternalAsync()
    {
        StopGpuPolling();

        if (_process is null || _process.HasExited)
        {
            _state = LocalLlmServerState.Idle;
            _process = null;
            return;
        }

        _state = LocalLlmServerState.Stopping;
        try
        {
            _process.Kill(true);
            await _process.WaitForExitAsync(new CancellationTokenSource(TimeSpan.FromSeconds(5)).Token);
        }
        catch
        {
            // Force kill if graceful failed
            try { _process.Kill(true); } catch { /* ignore */ }
        }
        _process = null;
        _state = LocalLlmServerState.Idle;
        _error = null;
        logger.LogInformation("llama-server 已停止");
    }

    private async void OnProcessExited(object? sender, EventArgs e)
    {
        if (_state == LocalLlmServerState.Stopping || _state == LocalLlmServerState.Idle)
            return;

        StopGpuPolling();
        _state = LocalLlmServerState.Failed;
        _error = "llama-server 进程意外退出";
        _process = null;
        logger.LogWarning("llama-server 进程意外退出");
        try
        {
            await UnregisterEndpointAsync(default);
            await BroadcastStatus();
        }
        catch { /* ignore broadcast errors */ }
    }

    private async Task<bool> WaitForHealthAsync(int port, CancellationToken ct)
    {
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
        var url = $"http://127.0.0.1:{port}/health";

        for (int i = 0; i < 60; i++) // 60 × 500ms = 30s
        {
            ct.ThrowIfCancellationRequested();
            if (_process is null || _process.HasExited)
                return false;

            try
            {
                var resp = await client.GetAsync(url, ct);
                if (resp.IsSuccessStatusCode) return true;
            }
            catch
            {
                // Connection refused — server not ready yet
            }
            await Task.Delay(500, ct);
        }
        return false;
    }

    // ── GPU Monitoring (nvidia-smi) ──

    private void StartGpuPolling()
    {
        _gpuVramTotal = -1; // Indicate "not yet polled"
        _gpuPollCts = new CancellationTokenSource();
        _ = Task.Run(() => GpuPollLoopAsync(_gpuPollCts.Token));
    }

    private void StopGpuPolling()
    {
        _gpuPollCts?.Cancel();
        _gpuPollCts?.Dispose();
        _gpuPollCts = null;
        _gpuUtilization = 0;
        _gpuVramUsed = 0;
        _gpuVramTotal = 0;
    }

    private async Task GpuPollLoopAsync(CancellationToken ct)
    {
        // Poll once immediately, then every 3 seconds
        await PollGpuOnceAsync(ct);

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(3));
        try
        {
            while (await timer.WaitForNextTickAsync(ct))
                await PollGpuOnceAsync(ct);
        }
        catch (OperationCanceledException) { /* normal shutdown */ }
    }

    private async Task PollGpuOnceAsync(CancellationToken ct)
    {
        try
        {
            using var proc = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "nvidia-smi",
                    Arguments = "--query-gpu=utilization.gpu,memory.used,memory.total --format=csv,noheader,nounits",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true
                }
            };
            proc.Start();
            var output = await proc.StandardOutput.ReadToEndAsync(ct);
            await proc.WaitForExitAsync(ct);

            // Output format: "45, 4096, 16384"
            var parts = output.Trim().Split(',');
            if (parts.Length >= 3
                && int.TryParse(parts[0].Trim(), out var util)
                && int.TryParse(parts[1].Trim(), out var used)
                && int.TryParse(parts[2].Trim(), out var total))
            {
                _gpuUtilization = util;
                _gpuVramUsed = used;
                _gpuVramTotal = total;
                await BroadcastStatus();
            }
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "nvidia-smi 轮询失败");
        }
    }

    // ── Endpoint Auto-Registration ──

    private async Task RegisterEndpointAsync(CancellationToken ct)
    {
        var localSettings = await LoadSettingsAsync(ct);

        await settingsService.UpdateAsync(appSettings =>
        {
            var ai = appSettings.AiTranslation;
            var existing = ai.Endpoints.FirstOrDefault(e => e.Id == localSettings.EndpointId);
            if (existing is not null)
            {
                existing.ApiBaseUrl = $"http://127.0.0.1:{_internalPort}/v1";
                existing.ApiKey = "local";
                existing.Enabled = true;
            }
            else
            {
                ai.Endpoints.Add(new ApiEndpointConfig
                {
                    Id = localSettings.EndpointId,
                    Name = "本地模型",
                    Provider = LlmProvider.Custom,
                    ApiBaseUrl = $"http://127.0.0.1:{_internalPort}/v1",
                    ApiKey = "local",
                    ModelName = "",
                    Priority = 8,
                    Enabled = true
                });
            }
        }, ct);

        logger.LogInformation("已注册本地 LLM 端点 (ID: {Id})", localSettings.EndpointId);
    }

    private async Task UnregisterEndpointAsync(CancellationToken ct)
    {
        var localSettings = await LoadSettingsAsync(ct);

        await settingsService.UpdateAsync(appSettings =>
        {
            var existing = appSettings.AiTranslation.Endpoints.FirstOrDefault(e => e.Id == localSettings.EndpointId);
            if (existing is not null)
                existing.Enabled = false;
        }, ct);
    }

    // ── Model Download ──

    public async Task DownloadModelAsync(string catalogId, CancellationToken ct)
    {
        var catalogEntry = BuiltInModelCatalog.Models.FirstOrDefault(m => m.Id == catalogId)
            ?? throw new InvalidOperationException($"未找到模型: {catalogId}");

        if (_downloads.ContainsKey(catalogId))
            throw new InvalidOperationException("该模型正在下载中");

        var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        if (!_downloads.TryAdd(catalogId, cts))
        {
            cts.Dispose();
            throw new InvalidOperationException("该模型正在下载中");
        }

        // Remove from paused list if resuming
        var settings = await LoadSettingsAsync();
        if (settings.PausedDownloads.RemoveAll(p => p.CatalogId == catalogId) > 0)
            await SaveSettingsAsync(settings);

        try
        {
            await DownloadModelInternalAsync(catalogEntry, cts.Token);
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
            if (_pauseRequests.TryRemove(catalogId, out _))
            {
                await SavePausedStateAsync(catalogEntry);
            }
            else
            {
                await CleanupModelDownloadAsync(catalogId);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "模型下载失败: {Name}", catalogEntry.Name);
            _downloadMirrorState.TryGetValue(catalogId, out var mirror);
            await BroadcastDownloadProgress(new LocalLlmDownloadProgress(
                catalogId, 0, 0, 0, false, ex.Message, UseMirror: mirror));
            await CleanupModelDownloadAsync(catalogId);
            trayService.ShowNotification("下载失败", $"模型 {catalogEntry.Name} 下载失败", ToolTipIcon.Error);
        }
        finally
        {
            _downloads.TryRemove(catalogId, out _);
            _downloadMirrorState.TryRemove(catalogId, out _);
            cts.Dispose();
        }
    }

    private async Task DownloadModelInternalAsync(BuiltInModelInfo entry, CancellationToken ct)
    {
        var appSettings = await settingsService.GetAsync(ct);
        var useHfMirror = !string.IsNullOrWhiteSpace(appSettings.HfMirrorUrl);
        _downloadMirrorState[entry.Id] = useHfMirror;
        var mirrorBase = useHfMirror
            ? appSettings.HfMirrorUrl.TrimEnd('/')
            : "https://huggingface.co";

        var url = $"{mirrorBase}/{entry.HuggingFaceRepo}/resolve/main/{entry.HuggingFaceFile}";
        var finalPath = Path.Combine(paths.ModelsDirectory, entry.HuggingFaceFile);
        var tempPath = finalPath + ".downloading";

        long existingBytes = 0;
        if (File.Exists(tempPath))
            existingBytes = new FileInfo(tempPath).Length;

        var client = httpClientFactory.CreateClient("LocalLlmDownload");

        if (existingBytes > 0)
        {
            // Try range request for resume
            using var rangeReq = new HttpRequestMessage(HttpMethod.Get, url);
            rangeReq.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(existingBytes, null);
            using var rangeResp = await client.SendAsync(rangeReq, HttpCompletionOption.ResponseHeadersRead, ct);

            if (rangeResp.StatusCode == HttpStatusCode.RequestedRangeNotSatisfiable)
            {
                var actualSize = rangeResp.Content.Headers.ContentRange?.Length;
                if (actualSize.HasValue && existingBytes >= actualSize.Value)
                {
                    if (File.Exists(finalPath)) File.Delete(finalPath);
                    File.Move(tempPath, finalPath);
                    await FinalizeModelDownloadAsync(entry, finalPath, existingBytes, useHfMirror, ct);
                    return;
                }
                File.Delete(tempPath);
            }
            else
            {
                rangeResp.EnsureSuccessStatusCode();
                var totalBytes = rangeResp.Content.Headers.ContentLength.HasValue
                    ? rangeResp.Content.Headers.ContentLength.Value + existingBytes
                    : entry.FileSizeBytes;
                await DownloadModelStreamAsync(entry, rangeResp, tempPath, finalPath, existingBytes, totalBytes, useHfMirror, ct);
                return;
            }
        }

        // Fresh download
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        using var resp = await client.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
        resp.EnsureSuccessStatusCode();

        var freshTotal = resp.Content.Headers.ContentLength ?? entry.FileSizeBytes;
        await DownloadModelStreamAsync(entry, resp, tempPath, finalPath, 0, freshTotal, useHfMirror, ct);
    }

    private async Task DownloadModelStreamAsync(BuiltInModelInfo entry, HttpResponseMessage resp,
        string tempPath, string finalPath, long existingBytes, long totalBytes, bool useMirror, CancellationToken ct)
    {
        await using var stream = await resp.Content.ReadAsStreamAsync(ct);
        await using var fs = new FileStream(tempPath, existingBytes > 0 ? FileMode.Append : FileMode.Create,
            FileAccess.Write, FileShare.None, 81920);

        var buffer = new byte[81920];
        long bytesDownloaded = existingBytes;
        var sw = Stopwatch.StartNew();
        long lastBroadcastTicks = 0;

        int bytesRead;
        while ((bytesRead = await stream.ReadAsync(buffer, ct)) > 0)
        {
            await fs.WriteAsync(buffer.AsMemory(0, bytesRead), ct);
            bytesDownloaded += bytesRead;

            var now = sw.ElapsedTicks;
            if (now - lastBroadcastTicks >= TimeSpan.FromMilliseconds(200).Ticks)
            {
                var elapsed = sw.Elapsed.TotalSeconds;
                var speed = elapsed > 0 ? (long)((bytesDownloaded - existingBytes) / elapsed) : 0;

                await BroadcastDownloadProgress(new LocalLlmDownloadProgress(
                    entry.Id, bytesDownloaded, totalBytes, speed, false, null, UseMirror: useMirror));
                lastBroadcastTicks = now;
            }
        }

        await fs.DisposeAsync();

        if (File.Exists(finalPath)) File.Delete(finalPath);
        File.Move(tempPath, finalPath);

        await FinalizeModelDownloadAsync(entry, finalPath, bytesDownloaded, useMirror, ct);
    }

    private async Task FinalizeModelDownloadAsync(BuiltInModelInfo entry, string finalPath, long fileSize, bool useMirror, CancellationToken ct)
    {
        var settings = await LoadSettingsAsync(ct);
        settings.PausedDownloads.RemoveAll(p => p.CatalogId == entry.Id);

        if (!settings.Models.Any(m => m.CatalogId == entry.Id))
        {
            settings.Models.Add(new LocalModelEntry
            {
                Name = entry.Name,
                FilePath = finalPath,
                FileSizeBytes = fileSize,
                IsBuiltIn = true,
                CatalogId = entry.Id
            });
        }
        await SaveSettingsAsync(settings, ct);

        await BroadcastDownloadProgress(new LocalLlmDownloadProgress(
            entry.Id, fileSize, fileSize, 0, true, null, UseMirror: useMirror));

        logger.LogInformation("模型下载完成: {Name} → {Path}", entry.Name, finalPath);
        trayService.ShowNotification("下载完成", $"模型 {entry.Name} 已下载完成");
    }

    private async Task SavePausedStateAsync(BuiltInModelInfo entry)
    {
        var tempPath = Path.Combine(paths.ModelsDirectory, entry.HuggingFaceFile + ".downloading");
        long bytesDownloaded = File.Exists(tempPath) ? new FileInfo(tempPath).Length : 0;

        var settings = await LoadSettingsAsync();
        settings.PausedDownloads.RemoveAll(p => p.CatalogId == entry.Id);
        settings.PausedDownloads.Add(new PausedDownload
        {
            CatalogId = entry.Id,
            BytesDownloaded = bytesDownloaded,
            TotalBytes = entry.FileSizeBytes
        });
        await SaveSettingsAsync(settings);

        await BroadcastDownloadProgress(new LocalLlmDownloadProgress(
            entry.Id, bytesDownloaded, entry.FileSizeBytes, 0, false, null, Paused: true));
    }

    private async Task CleanupModelDownloadAsync(string catalogId)
    {
        var entry = BuiltInModelCatalog.Models.FirstOrDefault(m => m.Id == catalogId);
        if (entry is not null)
        {
            var tempPath = Path.Combine(paths.ModelsDirectory, entry.HuggingFaceFile + ".downloading");
            try { if (File.Exists(tempPath)) File.Delete(tempPath); }
            catch { /* file might be briefly in use */ }
        }

        var settings = await LoadSettingsAsync();
        if (settings.PausedDownloads.RemoveAll(p => p.CatalogId == catalogId) > 0)
            await SaveSettingsAsync(settings);
    }

    public void PauseDownload(string catalogId)
    {
        if (_downloads.TryGetValue(catalogId, out var cts))
        {
            _pauseRequests.TryAdd(catalogId, true);
            cts.Cancel();
        }
    }

    public async Task CancelDownloadAsync(string catalogId)
    {
        if (_downloads.TryGetValue(catalogId, out var cts))
        {
            _pauseRequests.TryRemove(catalogId, out _);
            cts.Cancel();
            return;
        }
        await CleanupModelDownloadAsync(catalogId);
    }

    // ── Model Inventory ──

    public async Task<LocalModelEntry> AddModelAsync(string filePath, string name, CancellationToken ct)
    {
        if (!File.Exists(filePath))
            throw new InvalidOperationException($"文件不存在: {filePath}");

        var settings = await LoadSettingsAsync(ct);
        var entry = new LocalModelEntry
        {
            Name = name,
            FilePath = filePath,
            FileSizeBytes = new FileInfo(filePath).Length,
            IsBuiltIn = false
        };
        settings.Models.Add(entry);
        await SaveSettingsAsync(settings, ct);
        return entry;
    }

    public async Task RemoveModelAsync(string modelId, CancellationToken ct)
    {
        var settings = await LoadSettingsAsync(ct);
        settings.Models.RemoveAll(m => m.Id == modelId);
        await SaveSettingsAsync(settings, ct);
    }

    // ── Helpers ──

    private string GetLlamaServerPath(GpuBackend backend)
    {
        var subdir = backend.ToString().ToLowerInvariant();
        return Path.Combine(paths.LlamaDirectory, subdir, "llama-server.exe");
    }

    private static int GetAvailablePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    // ── SignalR ──

    private async Task BroadcastStatus()
    {
        try
        {
            await hubContext.Clients.Group("local-llm")
                .SendAsync("localLlmStatusUpdate", GetStatus());
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "本地 LLM 状态推送失败");
        }
    }

    private async Task BroadcastDownloadProgress(LocalLlmDownloadProgress progress)
    {
        try
        {
            await hubContext.Clients.Group("local-llm")
                .SendAsync("localLlmDownloadProgress", progress);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "本地 LLM 下载进度推送失败");
        }
    }
}
