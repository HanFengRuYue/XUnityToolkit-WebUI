namespace XUnityToolkit_WebUI.Services;

/// <summary>
/// Owns only the WinForms NotifyIcon message loop. The application window is owned by WinUI 3
/// and all cross-thread window operations are routed through IDesktopWindowService.
/// </summary>
public sealed class SystemTrayService(
    ILogger<SystemTrayService> logger,
    IDesktopWindowService desktopWindow) : IHostedService, IDisposable
{
    private Thread? _staThread;
    private volatile NotifyIcon? _trayIcon;
    private volatile SynchronizationContext? _syncContext;
    private Icon? _cachedIcon;
    private ToolStripMenuItem? _openMenuItem;
    private int _disposed;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        desktopWindow.ModeChanged += OnDesktopModeChanged;

        _staThread = new Thread(RunTrayLoop)
        {
            IsBackground = true,
            Name = "XUnityToolkit.NotifyIcon"
        };
        _staThread.SetApartmentState(ApartmentState.STA);
        _staThread.Start();

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        desktopWindow.SetTrayAvailable(false);
        HideUIImmediately();

        var context = _syncContext;
        if (context is not null)
            context.Post(_ => System.Windows.Forms.Application.ExitThread(), null);

        var trayThread = _staThread;
        if (trayThread is not null
            && trayThread != Thread.CurrentThread
            && !trayThread.Join(TimeSpan.FromSeconds(1)))
        {
            logger.LogWarning("托盘消息线程未能在关闭超时内退出");
        }

        return Task.CompletedTask;
    }

    private void RunTrayLoop()
    {
        System.Windows.Forms.Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        System.Windows.Forms.Application.EnableVisualStyles();
        System.Windows.Forms.Application.SetCompatibleTextRenderingDefault(false);

        var context = new WindowsFormsSynchronizationContext();
        SynchronizationContext.SetSynchronizationContext(context);
        _syncContext = context;

        using var trayIcon = BuildTrayIcon();
        _trayIcon = trayIcon;
        trayIcon.Visible = true;
        desktopWindow.SetTrayAvailable(true);

        try
        {
            System.Windows.Forms.Application.Run();
        }
        finally
        {
            desktopWindow.SetTrayAvailable(false);
            trayIcon.Visible = false;
            _trayIcon = null;
            _syncContext = null;
        }
    }

    private NotifyIcon BuildTrayIcon()
    {
        _cachedIcon = Icon.ExtractAssociatedIcon(Environment.ProcessPath!) ?? SystemIcons.Application;
        var icon = new NotifyIcon
        {
            Text = "XUnity Toolkit WebUI",
            Icon = _cachedIcon,
            ContextMenuStrip = BuildContextMenu()
        };

        icon.DoubleClick += (_, _) => desktopWindow.Activate();
        return icon;
    }

    private ContextMenuStrip BuildContextMenu()
    {
        var menu = new ContextMenuStrip();

        _openMenuItem = new ToolStripMenuItem(GetOpenMenuText());
        _openMenuItem.Click += (_, _) => desktopWindow.Activate();

        var exitItem = new ToolStripMenuItem("退出");
        exitItem.Click += (_, _) =>
        {
            logger.LogInformation("用户从托盘菜单退出应用");
            HideUICore();
            desktopWindow.RequestExit();
        };

        menu.Items.Add(_openMenuItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(exitItem);
        return menu;
    }

    private string GetOpenMenuText() =>
        desktopWindow.UsesBrowserFallback ? "打开浏览器" : "显示窗口";

    private void OnDesktopModeChanged(object? sender, EventArgs e)
    {
        var context = _syncContext;
        if (context is null)
            return;

        context.Post(_ =>
        {
            if (_openMenuItem is not null)
                _openMenuItem.Text = GetOpenMenuText();

            if (desktopWindow.UsesBrowserFallback)
            {
                _trayIcon?.ShowBalloonTip(
                    3000,
                    "XUnity Toolkit WebUI",
                    "WebView2 无法使用，程序已改用默认浏览器",
                    ToolTipIcon.Warning);
            }
        }, null);
    }

    public bool ActivateUI() => desktopWindow.Activate();

    public void ShowNotification(string title, string body, ToolTipIcon icon = ToolTipIcon.Info)
    {
        var context = _syncContext;
        if (context is null)
            return;

        context.Post(_ =>
        {
            try
            {
                _trayIcon?.ShowBalloonTip(3000, title, body, icon);
            }
            catch
            {
                // Notifications are best effort and must not affect application work.
            }
        }, null);
    }

    /// <summary>
    /// Hides the tray icon and WinUI window immediately. Safe to call from any thread.
    /// </summary>
    public void HideUIImmediately()
    {
        desktopWindow.Hide();

        if (Thread.CurrentThread == _staThread)
        {
            HideUICore();
            return;
        }

        _syncContext?.Post(_ => HideUICore(), null);
    }

    private void HideUICore()
    {
        try
        {
            if (_trayIcon is not null)
                _trayIcon.Visible = false;
        }
        catch
        {
            // Best effort during shutdown.
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        desktopWindow.ModeChanged -= OnDesktopModeChanged;
        _cachedIcon?.Dispose();
    }
}
