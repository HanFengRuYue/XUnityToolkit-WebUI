using System.Diagnostics;
using XUnityToolkit_WebUI.Infrastructure;

namespace XUnityToolkit_WebUI.Services;

/// <summary>
/// Schedules deletion of the complete AppData root after the current process exits, so locked
/// WebView2, log, cache, model, and conversation files are included in the reset.
/// </summary>
public sealed class ToolboxDataResetService(
    AppDataPaths paths,
    IHostApplicationLifetime lifetime,
    ILogger<ToolboxDataResetService> logger)
{
    private int _scheduled;

    public Task ScheduleAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (Interlocked.CompareExchange(ref _scheduled, 1, 0) != 0)
            throw new InvalidOperationException("工具箱数据清空已经安排执行。 ");

        try
        {
            var updaterSource = Path.Combine(AppContext.BaseDirectory, "Updater.exe");
            if (!File.Exists(updaterSource))
                throw new FileNotFoundException("找不到数据清空助手 Updater.exe。", updaterSource);

            var executable = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(executable) || !File.Exists(executable))
                throw new InvalidOperationException("无法确定工具箱主程序路径，不能安全安排数据清空。 ");

            var dataRoot = Path.GetFullPath(paths.Root)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var filesystemRoot = Path.GetPathRoot(dataRoot)?
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (string.IsNullOrWhiteSpace(filesystemRoot)
                || string.Equals(dataRoot, filesystemRoot, StringComparison.OrdinalIgnoreCase)
                || IsProtectedResetRoot(dataRoot))
                throw new InvalidOperationException("拒绝把磁盘根目录、用户目录或系统数据目录作为工具箱数据清空目标。 ");

            var executablePath = Path.GetFullPath(executable);
            var dataPrefix = dataRoot + Path.DirectorySeparatorChar;
            if (executablePath.StartsWith(dataPrefix, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("工具箱程序位于数据目录内，不能安全清空后重启。 ");

            var helperDirectory = Path.Combine(
                Path.GetTempPath(),
                $"XUnityToolkit-reset-helper-{Guid.NewGuid():N}");
            Directory.CreateDirectory(helperDirectory);
            var updaterCopy = Path.Combine(helperDirectory, "Updater.exe");
            File.Copy(updaterSource, updaterCopy, overwrite: false);

            var startInfo = new ProcessStartInfo
            {
                FileName = updaterCopy,
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden
            };
            startInfo.ArgumentList.Add("--reset-data");
            startInfo.ArgumentList.Add("--pid");
            startInfo.ArgumentList.Add(Environment.ProcessId.ToString());
            startInfo.ArgumentList.Add("--data-dir");
            startInfo.ArgumentList.Add(dataRoot);
            startInfo.ArgumentList.Add("--exe-path");
            startInfo.ArgumentList.Add(executablePath);

            using var process = Process.Start(startInfo)
                                ?? throw new InvalidOperationException("无法启动工具箱数据清空助手。 ");
            logger.LogWarning("已安排退出后完整清空工具箱数据目录并重启: {Root}", dataRoot);

            _ = Task.Run(async () =>
            {
                await Task.Delay(750, CancellationToken.None);
                lifetime.StopApplication();
            });
            return Task.CompletedTask;
        }
        catch
        {
            Interlocked.Exchange(ref _scheduled, 0);
            throw;
        }
    }

    internal static bool IsProtectedResetRoot(string candidate)
    {
        var prefix = candidate.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                     + Path.DirectorySeparatorChar;
        var protectedPaths = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            Path.GetTempPath()
        };
        return protectedPaths
            .Where(static path => !string.IsNullOrWhiteSpace(path))
            .Select(static path => Path.GetFullPath(path)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
            .Any(path => string.Equals(candidate, path, StringComparison.OrdinalIgnoreCase)
                         || path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
    }
}
