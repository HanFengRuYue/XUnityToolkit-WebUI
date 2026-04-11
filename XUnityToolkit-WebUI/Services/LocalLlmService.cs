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
    ILogger<LocalLlmService> logger) : IDisposable
{
    private static readonly JsonSerializerOptions LoadJsonOptions = new() { PropertyNameCaseInsensitive = true };
    private static readonly JsonSerializerOptions SaveJsonOptions = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

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
    private readonly ConcurrentDictionary<string, bool> _downloadModelScopeState = new();

    // LLAMA binary download
    private CancellationTokenSource? _llamaDownloadCts;
    private volatile bool _isDownloadingLlama;
    public bool IsDownloadingLlama => _isDownloadingLlama;

    // GPU cache
    private List<GpuInfo>? _gpuCache;

    // GPU monitoring (nvidia-smi)
    private volatile int _gpuUtilization;
    private volatile int _gpuVramUsed;   // MB
    private volatile int _gpuVramTotal;  // MB — -1 means never polled yet
    private volatile CancellationTokenSource? _gpuPollCts;
    private readonly ConcurrentQueue<string> _recentProcessOutput = new();

    // ── llama.cpp binary constants ──

    public const string LlamaVersion = "b8756";

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
        _settingsCache = JsonSerializer.Deserialize<LocalLlmSettings>(json, LoadJsonOptions) ?? new LocalLlmSettings();
        return _settingsCache;
    }

    public async Task SaveSettingsAsync(LocalLlmSettings settings, CancellationToken ct = default)
    {
        _settingsCache = settings;
        var json = JsonSerializer.Serialize(settings, SaveJsonOptions);
        var tmpPath = paths.LocalLlmSettingsFile + ".tmp";
        await File.WriteAllTextAsync(tmpPath, json, ct);
        File.Move(tmpPath, paths.LocalLlmSettingsFile, overwrite: true);
    }

    public void InvalidateSettingsCache() => _settingsCache = null;

    /// <summary>Merges user-editable fields without overwriting Models/PausedDownloads.</summary>
    public async Task UpdateUserSettingsAsync(Endpoints.UpdateLocalLlmSettingsRequest req, CancellationToken ct = default)
    {
        var settings = await LoadSettingsAsync(ct);
        settings.GpuLayers = req.GpuLayers;
        settings.ContextLength = req.ContextLength;
        if (req.KvCacheType is "f16" or "q8_0" or "q4_0")
            settings.KvCacheType = req.KvCacheType;
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

        var anyInstalled = backends.Any(b => b.IsInstalled);
        var installedVersion = anyInstalled ? ReadBundledLlamaVersion() : null;
        var needsUpdate = installedVersion is not null
            && installedVersion != LlamaVersion
            && !EditionInfo.HasBundledLlama;

        return new LlamaStatus(LlamaVersion, backends, recommended, _isDownloadingLlama,
            installedVersion, needsUpdate);
    }

    /// <summary>
    /// Reads the installed llama version from bundled/llama/version.txt,
    /// falling back to parsing from ZIP filenames for backward compatibility.
    /// </summary>
    private string? ReadBundledLlamaVersion()
    {
        var versionFile = Path.Combine(bundledPaths.LlamaDirectory, "version.txt");
        if (File.Exists(versionFile))
        {
            try { return File.ReadAllText(versionFile).Trim(); }
            catch { /* ignore read errors */ }
        }

        // Fallback: parse version from ZIP filename (e.g., llama-b8756-bin-win-cuda-13.1-x64.zip)
        try
        {
            var llamaDir = bundledPaths.LlamaDirectory;
            if (Directory.Exists(llamaDir))
            {
                foreach (var zip in Directory.GetFiles(llamaDir, "llama-*.zip"))
                {
                    var fileName = Path.GetFileName(zip);
                    var match = System.Text.RegularExpressions.Regex.Match(fileName, @"llama-(b\d+)-");
                    if (match.Success)
                        return match.Groups[1].Value;
                }
            }
        }
        catch { /* ignore */ }

        return null;
    }

    /// <summary>
    /// Ensures the llama.cpp binary for the given backend is extracted from the bundled ZIP.
    /// Called automatically before starting the server.
    /// </summary>
    private async Task EnsureLlamaExtractedAsync(GpuBackend backend)
    {
        var serverPath = GetLlamaServerPath(backend);
        var runtimeVersionFile = Path.Combine(paths.LlamaDirectory, "version.txt");

        // Check if already extracted with correct version
        if (File.Exists(serverPath))
        {
            string? runtimeVersion = null;
            try { if (File.Exists(runtimeVersionFile)) runtimeVersion = File.ReadAllText(runtimeVersionFile).Trim(); }
            catch { /* ignore */ }

            if (runtimeVersion == LlamaVersion)
                return; // Up to date

            // Version mismatch or unknown — clear all backend dirs and re-extract
            logger.LogInformation("llama.cpp 运行时版本不匹配 ({Installed} → {Expected})，重新解压",
                runtimeVersion ?? "unknown", LlamaVersion);
            foreach (var dir in Directory.GetDirectories(paths.LlamaDirectory))
            {
                try { Directory.Delete(dir, true); }
                catch (Exception ex) { logger.LogWarning(ex, "无法清理旧 llama 运行时目录: {Dir}", dir); }
            }
        }

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

        // Write runtime version marker
        try { File.WriteAllText(runtimeVersionFile, LlamaVersion); }
        catch (Exception ex) { logger.LogWarning(ex, "无法写入 llama 运行时版本标记"); }

        logger.LogInformation("llama.cpp {Backend} 后端已从捆绑包解压 (版本 {Version})", backend, LlamaVersion);
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
                _error = $"模型文件不存在: {Path.GetFileName(modelPath)}";
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
                _error = "llama-server.exe 未找到，请检查 llama 二进制文件是否已正确解压";
                await BroadcastStatus();
                return GetStatus();
            }

            var launchModelPath = ResolveLaunchModelPath(serverPath, modelPath);
            if (!launchModelPath.Success)
            {
                _state = LocalLlmServerState.Failed;
                _error = launchModelPath.Error;
                await BroadcastStatus();
                return GetStatus();
            }

            // Find available port
            _internalPort = GetAvailablePort();

            // Build arguments (--reasoning-budget 0 disables thinking in reasoning models)
            // Sanitize modelPath to prevent argument injection via embedded quotes
            var safeModelPath = launchModelPath.LaunchPath.Replace("\"", "");

            // Performance flags: flash attention + continuous batching + larger micro-batch
            // CPU mode: explicit thread counts for optimal utilization
            var perfArgs = _activeBackend == GpuBackend.CPU
                ? $"--flash-attn on --cont-batching -ub 1024 -t {Environment.ProcessorCount} -tb {Environment.ProcessorCount}"
                : "--flash-attn on --cont-batching -ub 1024";

            // KV cache quantization: reduces VRAM usage (q8_0 ~50% savings, near-lossless)
            var currentSettings = await LoadSettingsAsync(ct);
            var kvType = currentSettings.KvCacheType is "f16" or "q8_0" or "q4_0"
                ? currentSettings.KvCacheType : "q8_0";
            var kvArgs = kvType != "f16" ? $"--cache-type-k {kvType} --cache-type-v {kvType}" : "";

            var args = $"-m \"{safeModelPath}\" --host 127.0.0.1 --port {_internalPort} -ngl {gpuLayers} -c {contextLength} {perfArgs} {kvArgs} --no-webui --reasoning-budget 0";

            logger.LogInformation("llama 模型启动路径策略: {Strategy}, launch path: {LaunchPath}, resolved file: {ResolvedFilePath}",
                launchModelPath.Strategy, launchModelPath.LaunchPath, launchModelPath.ResolvedFilePath);
            logger.LogInformation("启动 llama-server: {Path} {Args}", serverPath, args);
            ResetRecentProcessOutput();

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
                if (e.Data is not null) RecordProcessOutput("stdout", e.Data);
            };
            _process.ErrorDataReceived += (_, e) =>
            {
                if (e.Data is not null) RecordProcessOutput("stderr", e.Data);
            };
            _process.Exited += OnProcessExited;

            _process.Start();
            _process.BeginOutputReadLine();
            _process.BeginErrorReadLine();

            // Health check loop
            var healthy = await WaitForHealthAsync(_internalPort, ct);
            if (!healthy)
            {
                var process = _process;
                var exitedEarly = process is { HasExited: true };
                var exitCode = TryGetExitCode(process);
                LogRecentProcessFailure(
                    exitedEarly ? "llama-server 在 health check 前退出" : "llama-server health check 超时",
                    exitCode);

                _state = LocalLlmServerState.Failed;
                _error = exitedEarly
                    ? exitCode is int code
                        ? $"llama-server 在 health check 前退出（退出码 {code}）"
                        : "llama-server 在 health check 前退出"
                    : "llama-server 启动超时，/health 在 30 秒内未就绪";
                try { process?.Kill(true); } catch { /* ignore */ }
                try { process?.Dispose(); } catch { /* ignore */ }
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
            _error = "启动 llama-server 失败，请查看日志获取详情";
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
            try { _process?.Dispose(); } catch { /* ignore */ }
            _process = null;
            return;
        }

        _state = LocalLlmServerState.Stopping;
        try
        {
            _process.Kill(true);
            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await _process.WaitForExitAsync(timeoutCts.Token);
        }
        catch
        {
            // Force kill if graceful failed
            try { _process.Kill(true); } catch { /* ignore */ }
        }
        try { _process.Dispose(); } catch { /* ignore */ }
        _process = null;
        _state = LocalLlmServerState.Idle;
        _error = null;
        logger.LogInformation("llama-server 已停止");
    }

    internal LocalLlmLaunchPathResolution ResolveLaunchModelPath(string serverPath, string modelPath) =>
        LocalLlmLaunchPathResolver.Resolve(serverPath, modelPath, paths.LlamaLaunchCacheDirectory);

    private async void OnProcessExited(object? sender, EventArgs e)
    {
        try
        {
            if (_state == LocalLlmServerState.Stopping
                || _state == LocalLlmServerState.Idle
                || (_state == LocalLlmServerState.Failed && _process is null))
            {
                return;
            }

            var process = sender as Process ?? _process;
            var exitCode = TryGetExitCode(process);

            StopGpuPolling();
            _state = LocalLlmServerState.Failed;
            _error = exitCode is int code
                ? $"llama-server 进程意外退出（退出码 {code}）"
                : "llama-server 进程意外退出";
            try { process?.Dispose(); } catch { /* ignore */ }
            _process = null;
            LogRecentProcessFailure("llama-server 进程意外退出", exitCode);
            await UnregisterEndpointAsync(default);
            await BroadcastStatus();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "OnProcessExited 处理失败");
        }
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

    private void ResetRecentProcessOutput()
    {
        while (_recentProcessOutput.TryDequeue(out _))
        {
        }
    }

    private void RecordProcessOutput(string stream, string line)
    {
        var entry = $"[{stream}] {line}";
        _recentProcessOutput.Enqueue(entry);

        while (_recentProcessOutput.Count > 60 && _recentProcessOutput.TryDequeue(out _))
        {
        }

        logger.LogDebug("[llama-server/{Stream}] {Line}", stream, line);
    }

    private int? TryGetExitCode(Process? process)
    {
        try
        {
            return process is { HasExited: true } ? process.ExitCode : null;
        }
        catch
        {
            return null;
        }
    }

    private void LogRecentProcessFailure(string reason, int? exitCode)
    {
        var output = _recentProcessOutput.ToArray();
        var outputText = output.Length == 0 ? "<no output captured>" : string.Join(Environment.NewLine, output);

        if (exitCode is int code)
        {
            logger.LogWarning("{Reason}，退出码 {ExitCode}。最近的 llama-server 输出:{NewLine}{Output}",
                reason, code, Environment.NewLine, outputText);
            return;
        }

        logger.LogWarning("{Reason}。最近的 llama-server 输出:{NewLine}{Output}",
            reason, Environment.NewLine, outputText);
    }

    // ── GPU Monitoring (nvidia-smi) ──

    private void StartGpuPolling()
    {
        _gpuVramTotal = -1; // Indicate "not yet polled"
        var newCts = new CancellationTokenSource();
        var oldCts = Interlocked.Exchange(ref _gpuPollCts, newCts);
        oldCts?.Cancel();
        oldCts?.Dispose();
        _ = Task.Run(() => GpuPollLoopAsync(newCts.Token));
    }

    private void StopGpuPolling()
    {
        var cts = Interlocked.Exchange(ref _gpuPollCts, null);
        cts?.Cancel();
        cts?.Dispose();
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
        Process? proc = null;
        try
        {
            proc = new Process
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
        catch (OperationCanceledException)
        {
            // Kill orphaned process on cancellation
            try { if (proc is { HasExited: false }) proc.Kill(); } catch { /* ignore */ }
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "nvidia-smi 轮询失败");
        }
        finally
        {
            proc?.Dispose();
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
            _downloadModelScopeState.TryGetValue(catalogId, out var modelScope);
            await BroadcastDownloadProgress(new LocalLlmDownloadProgress(
                catalogId, 0, 0, 0, false, ex.Message, UseMirror: mirror, UseModelScope: modelScope));
            await CleanupModelDownloadAsync(catalogId);
            trayService.ShowNotification("下载失败", $"模型 {catalogEntry.Name} 下载失败", ToolTipIcon.Error);
        }
        finally
        {
            _downloads.TryRemove(catalogId, out _);
            _downloadMirrorState.TryRemove(catalogId, out _);
            _downloadModelScopeState.TryRemove(catalogId, out _);
            cts.Dispose();
        }
    }

    private async Task DownloadModelInternalAsync(BuiltInModelInfo entry, CancellationToken ct)
    {
        var appSettings = await settingsService.GetAsync(ct);
        var useModelScope = appSettings.ModelDownloadSource == ModelDownloadSource.ModelScope
            && !string.IsNullOrEmpty(entry.ModelScopeRepo)
            && !string.IsNullOrEmpty(entry.ModelScopeFile);

        string url;
        string fileName;
        if (useModelScope)
        {
            // ModelScope uses /models/{repo}/resolve/master/{file} (branch: master, not main)
            url = $"https://modelscope.cn/models/{entry.ModelScopeRepo}/resolve/master/{entry.ModelScopeFile}";
            PathSecurity.ValidateExternalUrl(url);
            fileName = entry.ModelScopeFile!;
            _downloadMirrorState[entry.Id] = false;
            _downloadModelScopeState[entry.Id] = true;
        }
        else
        {
            var useHfMirror = !string.IsNullOrWhiteSpace(appSettings.HfMirrorUrl);
            _downloadMirrorState[entry.Id] = useHfMirror;
            var mirrorBase = useHfMirror
                ? appSettings.HfMirrorUrl.TrimEnd('/')
                : "https://huggingface.co";
            url = $"{mirrorBase}/{entry.HuggingFaceRepo}/resolve/main/{entry.HuggingFaceFile}";
            PathSecurity.ValidateExternalUrl(url);
            fileName = entry.HuggingFaceFile;
            _downloadModelScopeState[entry.Id] = false;
        }

        var finalPath = Path.Combine(paths.ModelsDirectory, fileName);
        var tempPath = finalPath + ".downloading";

        long existingBytes = 0;
        if (File.Exists(tempPath))
            existingBytes = new FileInfo(tempPath).Length;

        var client = httpClientFactory.CreateClient("LocalLlmDownload");

        _downloadMirrorState.TryGetValue(entry.Id, out var isMirror);
        _downloadModelScopeState.TryGetValue(entry.Id, out var isModelScope);

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
                    await FinalizeModelDownloadAsync(entry, finalPath, existingBytes, isMirror, isModelScope, ct);
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
                await DownloadModelStreamAsync(entry, rangeResp, tempPath, finalPath, existingBytes, totalBytes, isMirror, isModelScope, ct);
                return;
            }
        }

        // Fresh download
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        using var resp = await client.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
        resp.EnsureSuccessStatusCode();

        var freshTotal = resp.Content.Headers.ContentLength ?? entry.FileSizeBytes;
        await DownloadModelStreamAsync(entry, resp, tempPath, finalPath, 0, freshTotal, isMirror, isModelScope, ct);
    }

    private async Task DownloadModelStreamAsync(BuiltInModelInfo entry, HttpResponseMessage resp,
        string tempPath, string finalPath, long existingBytes, long totalBytes, bool useMirror, bool useModelScope, CancellationToken ct)
    {
        long bytesDownloaded;
        {
            await using var stream = await resp.Content.ReadAsStreamAsync(ct);
            await using var fs = new FileStream(tempPath, existingBytes > 0 ? FileMode.Append : FileMode.Create,
                FileAccess.Write, FileShare.None, 81920);

            var buffer = new byte[81920];
            bytesDownloaded = existingBytes;
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
                        entry.Id, bytesDownloaded, totalBytes, speed, false, null, UseMirror: useMirror, UseModelScope: useModelScope));
                    lastBroadcastTicks = now;
                }
            }
        }

        if (File.Exists(finalPath)) File.Delete(finalPath);
        File.Move(tempPath, finalPath);

        await FinalizeModelDownloadAsync(entry, finalPath, bytesDownloaded, useMirror, useModelScope, ct);
    }

    private async Task FinalizeModelDownloadAsync(BuiltInModelInfo entry, string finalPath, long fileSize, bool useMirror, bool useModelScope, CancellationToken ct)
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
            entry.Id, fileSize, fileSize, 0, true, null, UseMirror: useMirror, UseModelScope: useModelScope));

        logger.LogInformation("模型下载完成: {Name} → {Path}", entry.Name, finalPath);
        trayService.ShowNotification("下载完成", $"模型 {entry.Name} 已下载完成");
    }

    /// <summary>Returns the download filename for a model, preferring ModelScope when configured.</summary>
    private async Task<string> GetDownloadFileNameAsync(BuiltInModelInfo entry, CancellationToken ct = default)
    {
        var appSettings = await settingsService.GetAsync(ct);
        if (appSettings.ModelDownloadSource == ModelDownloadSource.ModelScope
            && !string.IsNullOrEmpty(entry.ModelScopeFile))
            return entry.ModelScopeFile;
        return entry.HuggingFaceFile;
    }

    private async Task SavePausedStateAsync(BuiltInModelInfo entry)
    {
        var fileName = await GetDownloadFileNameAsync(entry);
        var tempPath = Path.Combine(paths.ModelsDirectory, fileName + ".downloading");
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
            // Clean up temp files for both possible download sources
            foreach (var file in new[] { entry.HuggingFaceFile, entry.ModelScopeFile })
            {
                if (string.IsNullOrEmpty(file)) continue;
                var tempPath = Path.Combine(paths.ModelsDirectory, file + ".downloading");
                try { if (File.Exists(tempPath)) File.Delete(tempPath); }
                catch { /* file might be briefly in use */ }
            }
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

    // ── LLAMA Binary Download ──

    private const string GitHubOwner = "HanFengRuYue";
    private const string GitHubRepo = "XUnityToolkit-WebUI";

    private static readonly JsonSerializerOptions GitHubJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public async Task DownloadLlamaAsync(CancellationToken ct)
    {
        var newCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        if (Interlocked.CompareExchange(ref _llamaDownloadCts, newCts, null) is not null)
        {
            newCts.Dispose();
            throw new InvalidOperationException("llama 二进制文件正在下载中");
        }

        var token = newCts.Token;
        _isDownloadingLlama = true;
        var tempFile = Path.Combine(AppContext.BaseDirectory, "bundled-llama.zip.downloading");

        try
        {
            await BroadcastLlamaDownloadProgress(new LlamaDownloadProgress(0, 0, 0, false, null));

            // Resolve latest release to get bundled-llama.zip URL
            var client = httpClientFactory.CreateClient("GitHubUpdate");
            var releaseUrl = $"https://api.github.com/repos/{GitHubOwner}/{GitHubRepo}/releases/latest";
            using var releaseResponse = await client.GetAsync(releaseUrl, token);

            if (releaseResponse.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests)
                throw new InvalidOperationException("GitHub API 请求频率超限，请稍后再试");

            releaseResponse.EnsureSuccessStatusCode();

            var releaseJson = await releaseResponse.Content.ReadAsStringAsync(token);
            var release = JsonSerializer.Deserialize<GitHubRelease>(releaseJson, GitHubJsonOptions)
                ?? throw new InvalidOperationException("无法解析 GitHub Release 信息");

            var asset = release.Assets.FirstOrDefault(a =>
                a.Name.Equals("bundled-llama.zip", StringComparison.OrdinalIgnoreCase))
                ?? throw new InvalidOperationException("最新发行版中未找到 bundled-llama.zip");

            var downloadUrl = asset.BrowserDownloadUrl;
            PathSecurity.ValidateExternalUrl(downloadUrl);
            var totalBytes = asset.Size;

            logger.LogInformation("开始下载 llama 二进制文件: {Url} ({Size:N0} bytes)", downloadUrl, totalBytes);

            // Stream download with progress
            var cdnClient = httpClientFactory.CreateClient("GitHubCdn");
            using var downloadResponse = await cdnClient.GetAsync(
                downloadUrl, HttpCompletionOption.ResponseHeadersRead, token);
            downloadResponse.EnsureSuccessStatusCode();

            if (totalBytes == 0)
                totalBytes = downloadResponse.Content.Headers.ContentLength ?? 0;

            long downloadedBytes;
            {
                await using var stream = await downloadResponse.Content.ReadAsStreamAsync(token);
                await using var fileStream = File.Create(tempFile);

                var buffer = new byte[81920];
                downloadedBytes = 0;
                var lastBroadcast = Stopwatch.GetTimestamp();
                var speedWindow = new Queue<(long ticks, long bytes)>();
                int bytesRead;

                while ((bytesRead = await stream.ReadAsync(buffer, token)) > 0)
                {
                    await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), token);
                    downloadedBytes += bytesRead;

                    var now = Stopwatch.GetTimestamp();
                    speedWindow.Enqueue((now, downloadedBytes));
                    while (speedWindow.Count > 0 &&
                           Stopwatch.GetElapsedTime(speedWindow.Peek().ticks, now).TotalSeconds > 2)
                        speedWindow.Dequeue();

                    if (Stopwatch.GetElapsedTime(lastBroadcast, now).TotalMilliseconds >= 200)
                    {
                        lastBroadcast = now;
                        long speed = 0;
                        if (speedWindow.Count > 1)
                        {
                            var first = speedWindow.Peek();
                            var elapsed = Stopwatch.GetElapsedTime(first.ticks, now).TotalSeconds;
                            if (elapsed > 0)
                                speed = (long)((downloadedBytes - first.bytes) / elapsed);
                        }
                        await BroadcastLlamaDownloadProgress(
                            new LlamaDownloadProgress(downloadedBytes, totalBytes, speed, false, null));
                    }
                }
            }

            logger.LogInformation("llama 二进制文件下载完成: {Size:N0} bytes", downloadedBytes);

            // Extract bundled-llama.zip to bundled/llama/
            var llamaDir = bundledPaths.LlamaDirectory;
            if (Directory.Exists(llamaDir))
                Directory.Delete(llamaDir, true);
            Directory.CreateDirectory(llamaDir);

            using (var archive = ZipFile.OpenRead(tempFile))
            {
                foreach (var entry in archive.Entries)
                {
                    if (string.IsNullOrEmpty(entry.Name)) continue;

                    // Entries are prefixed with bundled/llama/ — strip that prefix
                    var entryPath = entry.FullName.Replace('\\', '/');
                    const string prefix = "bundled/llama/";
                    if (!entryPath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                        continue;

                    var relativeName = entryPath[prefix.Length..];
                    if (string.IsNullOrEmpty(relativeName)) continue;

                    var destPath = PathSecurity.SafeJoin(llamaDir, relativeName);
                    Directory.CreateDirectory(Path.GetDirectoryName(destPath)!);
                    entry.ExtractToFile(destPath, overwrite: true);
                }
            }

            // Clean up temp file
            File.Delete(tempFile);

            // Write version marker for bundled ZIPs
            try { File.WriteAllText(Path.Combine(llamaDir, "version.txt"), LlamaVersion); }
            catch (Exception ex) { logger.LogWarning(ex, "无法写入 llama 版本标记"); }

            // Clear extracted runtime dirs so next start re-extracts from new ZIPs
            try
            {
                if (Directory.Exists(paths.LlamaDirectory))
                {
                    foreach (var dir in Directory.GetDirectories(paths.LlamaDirectory))
                        Directory.Delete(dir, true);
                    var runtimeVersionFile = Path.Combine(paths.LlamaDirectory, "version.txt");
                    if (File.Exists(runtimeVersionFile))
                        File.Delete(runtimeVersionFile);
                }
            }
            catch (Exception ex)
            {
                // Files may be locked if server is running — version check on next start will handle it
                logger.LogDebug(ex, "无法清理 llama 运行时目录（可能正在运行中）");
            }

            logger.LogInformation("llama 二进制文件已解压到 {Dir} (版本 {Version})", llamaDir, LlamaVersion);

            await BroadcastLlamaDownloadProgress(
                new LlamaDownloadProgress(downloadedBytes, totalBytes, 0, true, null));
        }
        catch (OperationCanceledException)
        {
            logger.LogInformation("llama 二进制文件下载已取消");
            if (File.Exists(tempFile)) File.Delete(tempFile);
            await BroadcastLlamaDownloadProgress(
                new LlamaDownloadProgress(0, 0, 0, true, "下载已取消"));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "下载 llama 二进制文件失败");
            if (File.Exists(tempFile)) File.Delete(tempFile);
            await BroadcastLlamaDownloadProgress(
                new LlamaDownloadProgress(0, 0, 0, true, "下载 llama 引擎失败，请检查网络连接"));
        }
        finally
        {
            _isDownloadingLlama = false;
            Interlocked.Exchange(ref _llamaDownloadCts, null)?.Dispose();
        }
    }

    public void CancelLlamaDownload()
    {
        Volatile.Read(ref _llamaDownloadCts)?.Cancel();
    }

    private async Task BroadcastLlamaDownloadProgress(LlamaDownloadProgress progress)
    {
        try
        {
            await hubContext.Clients.Group("local-llm")
                .SendAsync("llamaDownloadProgress", progress);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "llama 下载进度推送失败");
        }
    }

    public void Dispose()
    {
        // Prevent OnProcessExited from entering its body and racing against _stateLock.Dispose()
        _state = LocalLlmServerState.Stopping;

        // Kill llama-server without waiting — the OS will clean up the process tree.
        // Do NOT call StopInternalAsync (has a 5s wait timeout).
        try
        {
            if (_process is { HasExited: false })
                _process.Kill(true);
        }
        catch { /* best effort */ }

        try { _process?.Dispose(); }
        catch { /* best effort */ }
        _process = null;

        StopGpuPolling();
        _stateLock.Dispose();
    }
}
