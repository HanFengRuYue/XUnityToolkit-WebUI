namespace XUnityToolkit_WebUI.Services;

using System.IO.Compression;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.SignalR;
using XUnityToolkit_WebUI.Hubs;
using XUnityToolkit_WebUI.Infrastructure;
using XUnityToolkit_WebUI.Models;

public sealed class UpdateService(
    IHttpClientFactory httpClientFactory,
    IHubContext<InstallProgressHub> hubContext,
    AppSettingsService settingsService,
    SystemTrayService trayService,
    AppDataPaths paths,
    IHostApplicationLifetime lifetime,
    ILogger<UpdateService> logger)
{
    private const string GitHubOwner = "HanFengRuYue";
    private const string GitHubRepo = "XUnityToolkit-WebUI";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    // Managed file extensions for delete-list computation (used in check and download)
    private static readonly HashSet<string> ManagedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".exe", ".dll", ".json", ".js", ".css", ".html", ".map", ".svg",
        ".png", ".woff", ".woff2", ".ttf", ".zip"
    };

    private readonly SemaphoreSlim _lock = new(1, 1);
    private UpdateStatusInfo _status = new() { State = UpdateState.None };
    private UpdateCheckResult? _lastCheckResult;
    private UpdateManifest? _remoteManifest;
    private List<GitHubAsset>? _releaseAssets;
    private CancellationTokenSource? _downloadCts;
    private UpdateAvailableInfo? _availableInfo;
    private bool _hasAutoChecked;
    private string? _lastCheckedApiUrl; // Track API URL for ETag cache invalidation
    private string? _dismissedVersion;

    private static string GetCurrentRid()
    {
        var arch = RuntimeInformation.ProcessArchitecture;
        return arch switch
        {
            System.Runtime.InteropServices.Architecture.X64 => "win-x64",
            _ => throw new PlatformNotSupportedException($"Unsupported architecture: {arch}")
        };
    }

    private static string GetCurrentVersion()
    {
        var version = Assembly.GetExecutingAssembly()
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion ?? "1.0.0";
        // Strip +metadata suffix (e.g., "1.3.202603151430+abc123" → "1.3.202603151430")
        var plusIndex = version.IndexOf('+');
        return plusIndex >= 0 ? version[..plusIndex] : version;
    }

    private static bool IsNewerVersion(string remote, string local)
    {
        if (remote == local) return false;

        var remoteParts = remote.Split('.');
        var localParts = local.Split('.');
        var len = Math.Max(remoteParts.Length, localParts.Length);

        for (int i = 0; i < len; i++)
        {
            if (!long.TryParse(i < remoteParts.Length ? remoteParts[i] : "0", out var r)) return true;
            if (!long.TryParse(i < localParts.Length ? localParts[i] : "0", out var l)) return true;
            if (r > l) return true;
            if (r < l) return false;
        }
        return false;
    }

    private static string ComputeFileHash(string filePath)
    {
        using var stream = File.OpenRead(filePath);
        var hash = SHA256.HashData(stream);
        return "sha256:" + Convert.ToHexStringLower(hash);
    }

    public UpdateStatusInfo GetUpdateStatus()
    {
        var status = _status;
        if (_availableInfo is not null && status.State is not UpdateState.None)
        {
            return new UpdateStatusInfo
            {
                State = status.State,
                Progress = status.Progress,
                DownloadedBytes = status.DownloadedBytes,
                TotalBytes = status.TotalBytes,
                CurrentPackage = status.CurrentPackage,
                Message = status.Message,
                Error = status.Error,
                AvailableUpdate = _availableInfo
            };
        }
        return status;
    }

    public async Task<UpdateCheckResult> CheckForUpdateAsync(CancellationToken ct = default)
    {
        if (!await _lock.WaitAsync(0, ct))
            return _lastCheckResult ?? new UpdateCheckResult();

        try
        {
            _status = new UpdateStatusInfo { State = UpdateState.Checking, Message = "正在检查更新..." };
            await BroadcastStatus();

            var settings = await settingsService.GetAsync(ct);
            var client = httpClientFactory.CreateClient("GitHubUpdate");
            var rid = GetCurrentRid();
            var localVersion = GetCurrentVersion();

            // Fetch latest release from GitHub
            var apiUrl = settings.ReceivePreReleaseUpdates
                ? $"https://api.github.com/repos/{GitHubOwner}/{GitHubRepo}/releases"
                : $"https://api.github.com/repos/{GitHubOwner}/{GitHubRepo}/releases/latest";

            // Load cached ETag (only use if API URL matches)
            var cachePath = Path.Combine(paths.Root, "update-check-cache.json");
            UpdateCheckCache? cache = null;
            if (File.Exists(cachePath))
            {
                var cacheJson = await File.ReadAllTextAsync(cachePath, ct);
                cache = JsonSerializer.Deserialize<UpdateCheckCache>(cacheJson, JsonOptions);
            }

            using var request = new HttpRequestMessage(HttpMethod.Get, apiUrl);
            if (cache?.ETag is not null && _lastCheckedApiUrl == apiUrl)
                request.Headers.TryAddWithoutValidation("If-None-Match", cache.ETag);
            _lastCheckedApiUrl = apiUrl;

            using var response = await client.SendAsync(request, ct);

            if (response.StatusCode == System.Net.HttpStatusCode.NotModified)
            {
                logger.LogInformation("GitHub API 返回 304 Not Modified，无新版本");
                _status = new UpdateStatusInfo { State = UpdateState.None };
                await BroadcastStatus();
                _lastCheckResult = new UpdateCheckResult();
                return _lastCheckResult;
            }

            if ((int)response.StatusCode == 403)
            {
                logger.LogWarning("GitHub API 速率限制，跳过检查");
                _status = new UpdateStatusInfo { State = UpdateState.Error, Error = "GitHub API 请求频率超限，请稍后再试" };
                await BroadcastStatus();
                throw new InvalidOperationException("GitHub API 请求频率超限，请稍后再试");
            }

            response.EnsureSuccessStatusCode();
            var json = await response.Content.ReadAsStringAsync(ct);

            GitHubRelease? release;
            if (settings.ReceivePreReleaseUpdates)
            {
                var releases = JsonSerializer.Deserialize<List<GitHubRelease>>(json, JsonOptions);
                release = releases?.FirstOrDefault();
            }
            else
            {
                release = JsonSerializer.Deserialize<GitHubRelease>(json, JsonOptions);
            }

            if (release is null)
            {
                _status = new UpdateStatusInfo { State = UpdateState.None };
                await BroadcastStatus();
                _lastCheckResult = new UpdateCheckResult();
                return _lastCheckResult;
            }

            // Save ETag cache
            var newCache = new UpdateCheckCache
            {
                ETag = response.Headers.ETag?.Tag,
                LatestVersion = release.TagName.TrimStart('v'),
                LastChecked = DateTime.UtcNow
            };
            await File.WriteAllTextAsync(cachePath,
                JsonSerializer.Serialize(newCache, JsonOptions), ct);

            var remoteVersion = release.TagName.TrimStart('v');

            // Check if dismissed
            var dismissedPath = Path.Combine(paths.Root, "update-dismissed-version.txt");
            if (File.Exists(dismissedPath))
            {
                var dismissed = (await File.ReadAllTextAsync(dismissedPath, ct)).Trim();
                if (dismissed == remoteVersion)
                {
                    _dismissedVersion = dismissed;
                    _status = new UpdateStatusInfo { State = UpdateState.None };
                    await BroadcastStatus();
                    _lastCheckResult = new UpdateCheckResult();
                    return _lastCheckResult;
                }
            }

            if (!IsNewerVersion(remoteVersion, localVersion))
            {
                logger.LogInformation("当前版本 {Local} 已是最新", localVersion);
                _status = new UpdateStatusInfo { State = UpdateState.None };
                await BroadcastStatus();
                _lastCheckResult = new UpdateCheckResult();
                return _lastCheckResult;
            }

            // Download remote manifest
            var manifestAsset = release.Assets
                .FirstOrDefault(a => a.Name == $"manifest-{rid}.json");
            if (manifestAsset is null)
            {
                logger.LogWarning("Release {Version} 缺少 manifest-{Rid}.json", remoteVersion, rid);
                _status = new UpdateStatusInfo { State = UpdateState.None };
                await BroadcastStatus();
                _lastCheckResult = new UpdateCheckResult();
                return _lastCheckResult;
            }

            var manifestJson = await client.GetStringAsync(manifestAsset.BrowserDownloadUrl, ct);
            _remoteManifest = JsonSerializer.Deserialize<UpdateManifest>(manifestJson, JsonOptions);
            _releaseAssets = release.Assets;

            if (_remoteManifest is null)
            {
                _status = new UpdateStatusInfo { State = UpdateState.None };
                await BroadcastStatus();
                _lastCheckResult = new UpdateCheckResult();
                return _lastCheckResult;
            }

            // Generate local manifest and compute diff
            var appDir = AppContext.BaseDirectory;
            var changedPackages = new HashSet<string>();
            var changedCount = 0;
            var deletedCount = 0;

            // Check remote files against local
            foreach (var (relativePath, entry) in _remoteManifest.Files)
            {
                var localPath = Path.Combine(appDir, relativePath.Replace('/', Path.DirectorySeparatorChar));
                if (!File.Exists(localPath) || ComputeFileHash(localPath) != entry.Hash)
                {
                    changedPackages.Add(entry.Package);
                    changedCount++;
                }
            }

            // Check for files to delete (local files not in remote manifest)
            foreach (var filePath in Directory.EnumerateFiles(appDir, "*", SearchOption.AllDirectories))
            {
                var relative = Path.GetRelativePath(appDir, filePath).Replace(Path.DirectorySeparatorChar, '/');
                if (relative.StartsWith("data/", StringComparison.OrdinalIgnoreCase)) continue;
                if (relative.StartsWith("appsettings", StringComparison.OrdinalIgnoreCase)) continue;
                if (!ManagedExtensions.Contains(Path.GetExtension(filePath))) continue;
                if (!_remoteManifest.Files.ContainsKey(relative))
                    deletedCount++;
            }

            // Calculate download size (sum of package ZIP sizes)
            long downloadSize = 0;
            foreach (var pkg in changedPackages)
            {
                var zipName = pkg == "app" ? $"app-{rid}.zip" : $"{pkg}.zip";
                var asset = release.Assets.FirstOrDefault(a => a.Name == zipName);
                if (asset is not null) downloadSize += asset.Size;
            }

            var changelog = ExtractChangelog(release.Body);

            _lastCheckResult = new UpdateCheckResult
            {
                UpdateAvailable = changedCount > 0 || deletedCount > 0,
                NewVersion = remoteVersion,
                Changelog = changelog,
                DownloadSize = downloadSize,
                ChangedPackages = changedPackages.ToList(),
                ChangedFileCount = changedCount,
                DeletedFileCount = deletedCount
            };

            if (_lastCheckResult.UpdateAvailable)
            {
                _availableInfo = new UpdateAvailableInfo
                {
                    Version = remoteVersion,
                    Changelog = changelog,
                    DownloadSize = downloadSize,
                    ChangedPackages = changedPackages.ToList()
                };
                _status = new UpdateStatusInfo { State = UpdateState.Available, Message = $"新版本 {remoteVersion} 可用" };
                await BroadcastStatus();
                await hubContext.Clients.Group("update").SendAsync("UpdateAvailable", _availableInfo, ct);
                logger.LogInformation("发现新版本 {Version}，需要下载 {Packages}",
                    remoteVersion, string.Join(", ", changedPackages));
                trayService.ShowNotification("XUnityToolkit", $"发现新版本 v{remoteVersion} 可用");
            }
            else
            {
                _status = new UpdateStatusInfo { State = UpdateState.None };
                await BroadcastStatus();
            }

            return _lastCheckResult;
        }
        catch (Exception ex) when (ex is not InvalidOperationException)
        {
            logger.LogError(ex, "检查更新失败");
            _status = new UpdateStatusInfo { State = UpdateState.Error, Error = "检查更新失败，请检查网络连接" };
            await BroadcastStatus();
            throw new InvalidOperationException("检查更新失败，请检查网络连接", ex);
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task DownloadUpdateAsync(CancellationToken ct = default)
    {
        if (_lastCheckResult is not { UpdateAvailable: true } || _remoteManifest is null || _releaseAssets is null)
            throw new InvalidOperationException("没有可用的更新");

        _downloadCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var token = _downloadCts.Token;

        var stagingDir = Path.Combine(paths.Root, "update-staging");
        var filesDir = Path.Combine(stagingDir, "files");

        try
        {
            // Clean up any existing staging
            if (Directory.Exists(stagingDir))
                Directory.Delete(stagingDir, true);
            Directory.CreateDirectory(filesDir);

            var client = httpClientFactory.CreateClient("GitHubUpdate");
            var rid = GetCurrentRid();
            var appDir = AppContext.BaseDirectory;
            var totalBytes = _lastCheckResult.DownloadSize;
            long downloadedBytes = 0;

            _status = new UpdateStatusInfo
            {
                State = UpdateState.Downloading,
                TotalBytes = totalBytes,
                Message = "正在下载更新..."
            };
            await BroadcastStatus();

            // Download each required component ZIP
            foreach (var pkg in _lastCheckResult.ChangedPackages)
            {
                token.ThrowIfCancellationRequested();

                var zipName = pkg == "app" ? $"app-{rid}.zip" : $"{pkg}.zip";
                var asset = _releaseAssets.FirstOrDefault(a => a.Name == zipName);
                if (asset is null)
                {
                    logger.LogWarning("找不到 Release Asset: {Name}", zipName);
                    continue;
                }

                _status.CurrentPackage = zipName;
                await BroadcastStatus();

                var zipPath = Path.Combine(stagingDir, zipName);

                // Download with progress (scoped to release file handle before extraction)
                {
                    using var response = await client.GetAsync(asset.BrowserDownloadUrl,
                        HttpCompletionOption.ResponseHeadersRead, token);
                    response.EnsureSuccessStatusCode();

                    await using var stream = await response.Content.ReadAsStreamAsync(token);
                    await using var fileStream = File.Create(zipPath);
                    var buffer = new byte[81920];
                    int bytesRead;
                    while ((bytesRead = await stream.ReadAsync(buffer, token)) > 0)
                    {
                        await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), token);
                        downloadedBytes += bytesRead;
                        _status.DownloadedBytes = downloadedBytes;
                        _status.Progress = totalBytes > 0 ? (double)downloadedBytes / totalBytes * 100 : 0;
                        await BroadcastStatus();
                    }
                }

                logger.LogInformation("已下载: {Name} ({Size:N0} bytes)", zipName, new FileInfo(zipPath).Length);

                // Extract only changed files from this package (scoped to release file handle before delete)
                {
                    using var archive = ZipFile.OpenRead(zipPath);
                    foreach (var entry in archive.Entries)
                    {
                        if (string.IsNullOrEmpty(entry.Name)) continue; // Skip directories

                        var entryRelativePath = entry.FullName.Replace('\\', '/');
                        // For wwwroot.zip and bundled.zip, the zip may contain the directory name
                        // Normalize path
                        if (!_remoteManifest.Files.TryGetValue(entryRelativePath, out var manifestEntry))
                            continue;

                        // Check if this file actually changed
                        var localPath = Path.Combine(appDir, entryRelativePath.Replace('/', Path.DirectorySeparatorChar));
                        if (File.Exists(localPath) && ComputeFileHash(localPath) == manifestEntry.Hash)
                            continue;

                        // Extract to staging (with path traversal protection)
                        var destPath = PathSecurity.SafeJoin(filesDir, entryRelativePath.Replace('/', Path.DirectorySeparatorChar));
                        Directory.CreateDirectory(Path.GetDirectoryName(destPath)!);
                        entry.ExtractToFile(destPath, overwrite: true);
                    }
                }

                // Clean up downloaded ZIP
                File.Delete(zipPath);
            }

            // Build delete list
            var deleteList = new List<string>();

            foreach (var filePath in Directory.EnumerateFiles(appDir, "*", SearchOption.AllDirectories))
            {
                var relative = Path.GetRelativePath(appDir, filePath).Replace(Path.DirectorySeparatorChar, '/');
                if (relative.StartsWith("data/", StringComparison.OrdinalIgnoreCase)) continue;
                if (relative.StartsWith("appsettings", StringComparison.OrdinalIgnoreCase)) continue;
                if (!ManagedExtensions.Contains(Path.GetExtension(filePath))) continue;
                if (!_remoteManifest.Files.ContainsKey(relative))
                    deleteList.Add(relative);
            }

            if (deleteList.Count > 0)
            {
                var deleteListPath = Path.Combine(stagingDir, "delete-list.txt");
                await File.WriteAllLinesAsync(deleteListPath, deleteList, token);
            }

            // Verify extracted files
            foreach (var filePath in Directory.EnumerateFiles(filesDir, "*", SearchOption.AllDirectories))
            {
                var relative = Path.GetRelativePath(filesDir, filePath).Replace(Path.DirectorySeparatorChar, '/');
                if (_remoteManifest.Files.TryGetValue(relative, out var expected))
                {
                    var actualHash = ComputeFileHash(filePath);
                    if (actualHash != expected.Hash)
                        throw new InvalidOperationException($"文件校验失败: {relative} (预期: {expected.Hash}, 实际: {actualHash})");
                }
            }

            // Write update manifest for staging recovery
            var updateManifestPath = Path.Combine(stagingDir, "update-manifest.json");
            await File.WriteAllTextAsync(updateManifestPath,
                JsonSerializer.Serialize(_remoteManifest, JsonOptions), token);

            _status = new UpdateStatusInfo
            {
                State = UpdateState.Ready,
                Progress = 100,
                DownloadedBytes = totalBytes,
                TotalBytes = totalBytes,
                Message = "更新已下载完成，等待应用"
            };
            await BroadcastStatus();

            logger.LogInformation("更新下载完成，等待应用");
            trayService.ShowNotification("XUnityToolkit", "更新已就绪，请在设置中重启更新");
        }
        catch (OperationCanceledException)
        {
            logger.LogInformation("更新下载已取消");
            if (Directory.Exists(stagingDir))
                Directory.Delete(stagingDir, true);
            _status = new UpdateStatusInfo { State = UpdateState.Available, Message = "下载已取消" };
            await BroadcastStatus();
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "下载更新失败");
            if (Directory.Exists(stagingDir))
                Directory.Delete(stagingDir, true);
            _status = new UpdateStatusInfo { State = UpdateState.Error, Error = "下载更新失败，请重试" };
            await BroadcastStatus();
            trayService.ShowNotification("XUnityToolkit", "下载更新失败，请重试");
            throw;
        }
        finally
        {
            var cts = _downloadCts;
            _downloadCts = null;
            cts?.Dispose();
        }
    }

    public void CancelDownload()
    {
        _downloadCts?.Cancel();
    }

    public async Task<string> ApplyUpdateAsync(CancellationToken ct = default)
    {
        var appDir = AppContext.BaseDirectory;
        var stagingDir = Path.Combine(paths.Root, "update-staging");
        var filesDir = Path.Combine(stagingDir, "files");
        var updaterSrc = Path.Combine(appDir, "Updater.exe");

        if (!Directory.Exists(filesDir))
            throw new InvalidOperationException("没有就绪的更新");

        if (!File.Exists(updaterSrc))
            throw new InvalidOperationException("找不到 Updater.exe");

        // Safety checks — check for active operations
        // These are injected/checked at the endpoint level to avoid circular DI dependencies.
        // The endpoint should validate before calling this method.

        // Copy Updater.exe to temp directory
        var tempDir = Path.Combine(paths.Root, "update-temp");
        Directory.CreateDirectory(tempDir);
        var updaterDst = Path.Combine(tempDir, "Updater.exe");
        File.Copy(updaterSrc, updaterDst, overwrite: true);

        // Build arguments (use ArgumentList to avoid backslash-quote escaping issues —
        // AppContext.BaseDirectory ends with '\', and "path\" is parsed as escaped quote by CommandLineToArgvW)
        var pid = Environment.ProcessId;
        var exeName = Path.GetFileName(Environment.ProcessPath ?? "XUnityToolkit-WebUI.exe");
        var deleteListPath = Path.Combine(stagingDir, "delete-list.txt");

        var startInfo = new System.Diagnostics.ProcessStartInfo
        {
            FileName = updaterDst,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add("--pid");
        startInfo.ArgumentList.Add(pid.ToString());
        startInfo.ArgumentList.Add("--app-dir");
        startInfo.ArgumentList.Add(appDir);
        startInfo.ArgumentList.Add("--staging-dir");
        startInfo.ArgumentList.Add(filesDir);
        startInfo.ArgumentList.Add("--exe-name");
        startInfo.ArgumentList.Add(exeName);
        startInfo.ArgumentList.Add("--data-dir");
        startInfo.ArgumentList.Add(paths.Root);
        if (File.Exists(deleteListPath))
        {
            startInfo.ArgumentList.Add("--delete-list");
            startInfo.ArgumentList.Add(deleteListPath);
        }

        // Launch Updater
        logger.LogInformation("启动 Updater.exe: {Args}", string.Join(" ", startInfo.ArgumentList));
        using var process = new System.Diagnostics.Process { StartInfo = startInfo };
        process.Start();

        _status = new UpdateStatusInfo { State = UpdateState.Applying, Message = "正在应用更新..." };
        await BroadcastStatus();

        // Schedule shutdown after response is sent
        _ = Task.Run(async () =>
        {
            await Task.Delay(500, CancellationToken.None);
            lifetime.StopApplication();
        });

        return "更新正在应用，应用即将重启";
    }

    public async Task DismissUpdateAsync(CancellationToken ct = default)
    {
        if (_lastCheckResult?.NewVersion is { } version)
        {
            var dismissedPath = Path.Combine(paths.Root, "update-dismissed-version.txt");
            await File.WriteAllTextAsync(dismissedPath, version, ct);
            _dismissedVersion = version;
        }
        _status = new UpdateStatusInfo { State = UpdateState.None };
        _lastCheckResult = null;
        _availableInfo = null;
        await BroadcastStatus();
    }

    public async Task AutoCheckOnStartupAsync()
    {
        if (_hasAutoChecked) return;
        _hasAutoChecked = true;

        // Check for stale staging directory
        var stagingDir = Path.Combine(paths.Root, "update-staging");
        if (Directory.Exists(stagingDir))
        {
            var manifestPath = Path.Combine(stagingDir, "update-manifest.json");
            if (File.Exists(manifestPath))
            {
                // Verify staged files
                try
                {
                    var json = await File.ReadAllTextAsync(manifestPath);
                    var manifest = JsonSerializer.Deserialize<UpdateManifest>(json, JsonOptions);
                    var filesDir = Path.Combine(stagingDir, "files");
                    if (manifest is not null && Directory.Exists(filesDir))
                    {
                        _remoteManifest = manifest;
                        _availableInfo = new UpdateAvailableInfo
                        {
                            Version = manifest.Version,
                            ChangedPackages = []
                        };
                        _status = new UpdateStatusInfo
                        {
                            State = UpdateState.Ready,
                            Message = "上次下载的更新已就绪"
                        };
                        await BroadcastStatus();
                        logger.LogInformation("检测到已就绪的更新 (v{Version})", manifest.Version);
                        return;
                    }
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "读取暂存更新清单失败，清理暂存目录");
                }
            }
            // Stale staging — clean up
            try { Directory.Delete(stagingDir, true); } catch { /* best effort */ }
        }

        // Check for update error from previous Updater run
        var errorPath = Path.Combine(paths.Root, "update-error.json");
        if (File.Exists(errorPath))
        {
            try
            {
                var errorJson = await File.ReadAllTextAsync(errorPath);
                logger.LogWarning("上次更新失败: {Error}", errorJson);
                _status = new UpdateStatusInfo
                {
                    State = UpdateState.Error,
                    Error = errorJson
                };
                await BroadcastStatus();
                File.Delete(errorPath);
            }
            catch { /* best effort */ }
            return;
        }

        // Clean up temp directories from previous update
        var tempDir = Path.Combine(paths.Root, "update-temp");
        if (Directory.Exists(tempDir))
        {
            try { Directory.Delete(tempDir, true); } catch { /* best effort */ }
        }
        var backupDir = Path.Combine(paths.Root, "update-backup");
        if (Directory.Exists(backupDir))
        {
            try { Directory.Delete(backupDir, true); } catch { /* best effort */ }
        }

        // Auto-check for updates
        await Task.Delay(5000);
        try
        {
            await CheckForUpdateAsync();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "启动时自动检查更新失败");
        }
    }

    /// <summary>
    /// Extracts the "### Changelog" section from GitHub release body,
    /// stripping download links, installation instructions, etc.
    /// </summary>
    private static string? ExtractChangelog(string? releaseBody)
    {
        if (string.IsNullOrWhiteSpace(releaseBody)) return null;

        var startIndex = releaseBody.IndexOf("### Changelog", StringComparison.OrdinalIgnoreCase);
        if (startIndex < 0) return releaseBody; // No Changelog section found, return full body

        // Skip past the "### Changelog" line
        var contentStart = releaseBody.IndexOf('\n', startIndex);
        if (contentStart < 0) return null;
        contentStart++; // Skip the newline

        // Find the end (next ## or ### section, or end of string)
        var endIndex = releaseBody.IndexOf("\n##", contentStart, StringComparison.Ordinal);

        var content = endIndex >= 0
            ? releaseBody[contentStart..endIndex]
            : releaseBody[contentStart..];

        var trimmed = content.Trim();
        return trimmed.Length > 0 ? trimmed : null;
    }

    private Task BroadcastStatus()
    {
        return hubContext.Clients.Group("update").SendAsync("UpdateStatus", _status);
    }
}
