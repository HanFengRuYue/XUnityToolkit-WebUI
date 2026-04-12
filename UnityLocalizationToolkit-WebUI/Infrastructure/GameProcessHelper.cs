using System.Diagnostics;

namespace UnityLocalizationToolkit_WebUI.Infrastructure;

/// <summary>
/// Helper for killing game processes, including those launched via shell (e.g. Steam).
/// When UseShellExecute=true, Process.Start may return a shell launcher process that exits
/// immediately while the actual game runs as a separate process. This helper finds and kills
/// the real game process by executable name.
/// </summary>
public static class GameProcessHelper
{
    /// <summary>
    /// Kill a game process and any remaining processes matching the executable name within the game directory.
    /// </summary>
    /// <param name="gameProcess">The process returned by Process.Start (may be a shell launcher).</param>
    /// <param name="exeName">Game executable filename (e.g. "Game.exe").</param>
    /// <param name="gamePath">Absolute path to the game directory.</param>
    /// <param name="logger">Logger for diagnostics.</param>
    public static async Task KillGameProcessAsync(Process? gameProcess, string exeName, string gamePath, ILogger logger)
    {
        var killed = false;

        // 1. Try killing the direct process reference
        if (gameProcess != null && !gameProcess.HasExited)
        {
            try
            {
                gameProcess.Kill();
                gameProcess.WaitForExit(5000);
                logger.LogInformation("已关闭游戏进程 (PID: {PID})", gameProcess.Id);
                killed = true;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "关闭游戏进程失败");
            }
        }
        gameProcess?.Dispose();

        // 2. Find and kill any remaining processes by executable name
        //    This handles cases where UseShellExecute=true returns a shell launcher
        //    (e.g. Steam) that exits immediately while the real game keeps running.
        var processName = Path.GetFileNameWithoutExtension(exeName);
        var normalizedGamePath = Path.GetFullPath(gamePath).TrimEnd(Path.DirectorySeparatorChar)
            + Path.DirectorySeparatorChar;

        foreach (var proc in Process.GetProcessesByName(processName))
        {
            try
            {
                // Verify the process is from the game directory
                string? modulePath = null;
                try { modulePath = proc.MainModule?.FileName; }
                catch { /* Access denied for elevated processes - skip */ }

                if (modulePath != null &&
                    modulePath.StartsWith(normalizedGamePath, StringComparison.OrdinalIgnoreCase))
                {
                    if (!proc.HasExited)
                    {
                        proc.Kill();
                        proc.WaitForExit(5000);
                        logger.LogInformation("已通过进程名关闭游戏进程: {ProcessName} (PID: {PID})",
                            processName, proc.Id);
                        killed = true;
                    }
                }
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "尝试关闭进程 {ProcessName} 失败（可能已退出）", processName);
            }
            finally
            {
                proc.Dispose();
            }
        }

        // 3. Brief delay to ensure file handles are released after process termination
        if (killed)
            await Task.Delay(1000);
    }
}
