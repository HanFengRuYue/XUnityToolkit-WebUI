using System.Diagnostics;
using System.Runtime.InteropServices;

namespace XUnityToolkit_WebUI.Services;

public sealed class SystemTrayService(
    ILogger<SystemTrayService> logger,
    IHostApplicationLifetime lifetime) : IHostedService, IDisposable
{
    [DllImport("kernel32.dll")]
    private static extern IntPtr GetConsoleWindow();

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    private const int SW_HIDE = 0;
    private const int SW_SHOW = 5;

    private Thread? _staThread;

    private const string AppUrl = "https://localhost:51821";

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _staThread = new Thread(RunTrayLoop) { IsBackground = true };
        _staThread.SetApartmentState(ApartmentState.STA);
        _staThread.Start();

        // Auto-open browser
        Task.Run(() =>
        {
            try
            {
                Process.Start(new ProcessStartInfo { FileName = AppUrl, UseShellExecute = true });
                logger.LogInformation("已在浏览器中打开: {Url}", AppUrl);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "自动打开浏览器失败");
            }
        }, cancellationToken);

        // Hide console window
        Task.Run(() =>
        {
            try
            {
                var hwnd = GetConsoleWindow();
                if (hwnd != IntPtr.Zero)
                {
                    ShowWindow(hwnd, SW_HIDE);
                    logger.LogInformation("已隐藏控制台窗口");
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "隐藏控制台窗口失败");
            }
        }, cancellationToken);

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        // Only call Application.Exit() — the STA thread owns the NotifyIcon lifecycle
        Application.Exit();
        return Task.CompletedTask;
    }

    private void RunTrayLoop()
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        using var trayIcon = BuildTrayIcon();
        trayIcon.Visible = true;

        Application.Run(); // Blocks until Application.Exit() is called

        trayIcon.Visible = false;
    }

    private NotifyIcon BuildTrayIcon()
    {
        var icon = new NotifyIcon
        {
            Text = "XUnity Toolkit WebUI",
            Icon = SystemIcons.Application,
            ContextMenuStrip = BuildContextMenu()
        };

        icon.DoubleClick += (_, _) => OpenBrowser();
        return icon;
    }

    private ContextMenuStrip BuildContextMenu()
    {
        var menu = new ContextMenuStrip();

        var openItem = new ToolStripMenuItem("打开浏览器");
        openItem.Click += (_, _) => OpenBrowser();

        var showConsoleItem = new ToolStripMenuItem("显示控制台");
        showConsoleItem.Click += (_, _) =>
        {
            var hwnd = GetConsoleWindow();
            if (hwnd != IntPtr.Zero) ShowWindow(hwnd, SW_SHOW);
        };

        var exitItem = new ToolStripMenuItem("退出");
        exitItem.Click += (_, _) =>
        {
            logger.LogInformation("用户从托盘菜单退出应用");
            lifetime.StopApplication();
        };

        menu.Items.Add(openItem);
        menu.Items.Add(showConsoleItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(exitItem);
        return menu;
    }

    private static void OpenBrowser()
    {
        try
        {
            Process.Start(new ProcessStartInfo { FileName = AppUrl, UseShellExecute = true });
        }
        catch { /* best effort */ }
    }

    public void Dispose()
    {
        // NotifyIcon is owned and disposed by the STA thread via `using` in RunTrayLoop
    }
}
