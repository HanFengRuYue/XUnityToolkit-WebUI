using System.Diagnostics;
using Microsoft.Web.WebView2.Core;

namespace XUnityToolkit_WebUI.Services;

public sealed class SystemTrayService(
    ILogger<SystemTrayService> logger,
    IHostApplicationLifetime lifetime,
    IConfiguration configuration) : IHostedService, IDisposable
{
    private Thread? _staThread;
    private volatile NotifyIcon? _trayIcon;
    private volatile SynchronizationContext? _syncContext;
    private volatile WebViewWindow? _mainWindow;
    private volatile bool _webView2Available;
    private Task<CoreWebView2Environment>? _preCreatedEnvTask;
    private Icon? _cachedIcon;
    private ToolStripMenuItem? _openMenuItem;
    private readonly TaskCompletionSource _kestrelReady = new();

    private string AppUrl
    {
        get
        {
            var urls = configuration["urls"] ?? "http://127.0.0.1:51821";
            return urls.Split(';')[0];
        }
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        DetectWebView2Runtime();

        _staThread = new Thread(RunTrayLoop) { IsBackground = true };
        _staThread.SetApartmentState(ApartmentState.STA);
        _staThread.Start();

        lifetime.ApplicationStarted.Register(() => _kestrelReady.TrySetResult());

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        // Only call Application.Exit() — the STA thread owns the NotifyIcon and WebViewWindow lifecycle
        Application.Exit();
        return Task.CompletedTask;
    }

    private void RunTrayLoop()
    {
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        using var trayIcon = BuildTrayIcon();
        trayIcon.Visible = true;

        _syncContext = SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext();
        _trayIcon = trayIcon;

        // Pre-create WebView2 environment on STA thread — overlaps with Kestrel startup
        // instead of waiting until after Kestrel is ready (~300-2000ms saved).
        // Must run on STA thread; calling from MTA (StartAsync) causes RPC_E_CHANGED_MODE.
        if (_webView2Available)
        {
            try
            {
                var userDataFolder = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "XUnityToolkit", "webview2-cache");
                Directory.CreateDirectory(userDataFolder);
                _preCreatedEnvTask = CoreWebView2Environment.CreateAsync(
                    browserExecutableFolder: null,
                    userDataFolder: userDataFolder);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to pre-create WebView2 environment");
                _preCreatedEnvTask = null;
            }
        }

        WaitForKestrelThenShowUI();

        Application.Run(); // Blocks until Application.Exit() is called

        // Dispose WebViewWindow if Application.Exit() didn't close it
        // (e.g., form was never shown so not in OpenForms)
        var window = _mainWindow;
        _mainWindow = null;
        _trayIcon = null;
        trayIcon.Visible = false;
        try { window?.Dispose(); }
        catch { /* best effort */ }
    }

    private void DetectWebView2Runtime()
    {
        try
        {
            var version = CoreWebView2Environment.GetAvailableBrowserVersionString();
            _webView2Available = !string.IsNullOrEmpty(version);
            if (_webView2Available)
                logger.LogInformation("WebView2 runtime detected: {Version}", version);
            else
                logger.LogWarning("WebView2 runtime not available, falling back to default browser");
        }
        catch (Exception ex)
        {
            _webView2Available = false;
            logger.LogWarning(ex, "WebView2 runtime detection failed, falling back to default browser");
        }
    }

    private void WaitForKestrelThenShowUI()
    {
        var ctx = _syncContext!;
        _ = Task.Run(async () =>
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            try
            {
                await _kestrelReady.Task.WaitAsync(cts.Token);
            }
            catch (OperationCanceledException)
            {
                logger.LogWarning("Kestrel did not become ready within 30s, proceeding anyway");
            }

            ctx.Post(_ => ShowUIOnStaThread(), null);
        });
    }

    private void ShowUIOnStaThread()
    {
        if (_webView2Available)
        {
            try
            {
                var window = new WebViewWindow(AppUrl, _preCreatedEnvTask, _cachedIcon, logger);
                _ = InitializeAndShowWindow(window);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to create WebView2 window, falling back to default browser");
                FallbackToBrowser();
            }
        }
        else
        {
            OpenInDefaultBrowser();
            _trayIcon?.ShowBalloonTip(
                3000,
                "XUnity Toolkit WebUI",
                "程序已在后台运行，双击托盘图标打开浏览器",
                ToolTipIcon.Info);
        }
    }

    private async Task InitializeAndShowWindow(WebViewWindow window)
    {
        try
        {
            // Show window immediately with native loading overlay — instant visual feedback.
            // WebView2 initialization happens in the background.
            window.Show();
            _mainWindow = window;

            await window.InitializeAsync();

            _trayIcon?.ShowBalloonTip(
                3000,
                "XUnity Toolkit WebUI",
                "程序已在后台运行，关闭窗口将最小化到托盘",
                ToolTipIcon.Info);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "WebView2 initialization failed, falling back to default browser");
            _mainWindow = null;
            window.Dispose();
            FallbackToBrowser();
        }
    }

    private void FallbackToBrowser()
    {
        _webView2Available = false;
        _mainWindow = null;
        if (_openMenuItem is not null)
            _openMenuItem.Text = "打开浏览器";
        OpenInDefaultBrowser();
    }

    public void ShowNotification(string title, string body, ToolTipIcon icon = ToolTipIcon.Info)
    {
        var ctx = _syncContext;
        var tray = _trayIcon;
        if (tray is null || ctx is null) return;

        ctx.Post(_ =>
        {
            try { tray.ShowBalloonTip(3000, title, body, icon); }
            catch { /* best effort */ }
        }, null);
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

        icon.DoubleClick += (_, _) => ShowOrOpenUI();
        return icon;
    }

    private ContextMenuStrip BuildContextMenu()
    {
        var menu = new ContextMenuStrip();

        _openMenuItem = new ToolStripMenuItem(_webView2Available ? "显示窗口" : "打开浏览器");
        _openMenuItem.Click += (_, _) => ShowOrOpenUI();

        var exitItem = new ToolStripMenuItem("退出");
        exitItem.Click += (_, _) =>
        {
            logger.LogInformation("用户从托盘菜单退出应用");
            // Hide UI immediately — user perceives "app closed" before cleanup starts
            HideUICore();
            lifetime.StopApplication();
        };

        menu.Items.Add(_openMenuItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(exitItem);
        return menu;
    }

    private void ShowOrOpenUI()
    {
        var window = _mainWindow;
        if (window is not null && _webView2Available)
        {
            window.Show();
            if (window.WindowState == FormWindowState.Minimized)
                window.WindowState = FormWindowState.Normal;
            window.Activate();
        }
        else
        {
            OpenInDefaultBrowser();
        }
    }

    private void OpenInDefaultBrowser()
    {
        try
        {
            Process.Start(new ProcessStartInfo { FileName = AppUrl, UseShellExecute = true });
        }
        catch { /* best effort */ }
    }

    /// <summary>
    /// Hide tray icon and window immediately. Safe to call from any thread.
    /// Used by ApplicationStopping callback for non-tray shutdown triggers (e.g. UpdateService).
    /// </summary>
    public void HideUIImmediately()
    {
        if (Thread.CurrentThread == _staThread)
        {
            HideUICore();
            return;
        }

        var ctx = _syncContext;
        if (ctx is null) return;
        ctx.Post(_ => HideUICore(), null);
    }

    private void HideUICore()
    {
        try
        {
            var tray = _trayIcon;
            if (tray is not null) tray.Visible = false;
            _mainWindow?.Hide();
        }
        catch { /* best effort */ }
    }

    public void Dispose()
    {
        // NotifyIcon is owned and disposed by the STA thread via `using` in RunTrayLoop
        // WebViewWindow is disposed via FormClosing when Application.Exit() is called
    }
}
