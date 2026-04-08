using System.Runtime.InteropServices;
using System.Net;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace XUnityToolkit_WebUI.Services;

internal sealed class WebViewWindow : Form
{
    private readonly WebView2 _webView;
    private readonly string _appUrl;
    private readonly Task<CoreWebView2Environment>? _preCreatedEnvTask;
    private readonly ILogger _logger;
    private Panel? _loadingOverlay;
    private string _overlayTitle = "XUnity Toolkit";
    private string _overlayMessage = "Loading...";
    private string? _overlayDetail;

    // Win32 constants
    private const int WM_NCHITTEST = 0x0084;
    private const int WM_GETMINMAXINFO = 0x0024;
    private const int HTCLIENT = 1;
    private const int HTLEFT = 10;
    private const int HTRIGHT = 11;
    private const int HTTOP = 12;
    private const int HTTOPLEFT = 13;
    private const int HTTOPRIGHT = 14;
    private const int HTBOTTOM = 15;
    private const int HTBOTTOMLEFT = 16;
    private const int HTBOTTOMRIGHT = 17;

    private const int ResizeBorderWidth = 6;

    // DWM rounded corner support (Windows 11+)
    private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    private const int DWMWCP_ROUND = 2;

    public WebViewWindow(string appUrl, Task<CoreWebView2Environment>? preCreatedEnvTask, Icon? cachedIcon, ILogger logger)
    {
        _appUrl = appUrl;
        _preCreatedEnvTask = preCreatedEnvTask;
        _logger = logger;

        Text = "XUnity Toolkit WebUI";
        Size = new Size(1200, 800);
        MinimumSize = new Size(500, 400);
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.None;
        BackColor = Color.FromArgb(0x0b, 0x0b, 0x11); // Match --bg-root
        Icon = cachedIcon ?? SystemIcons.Application;

        _webView = new WebView2 { Dock = DockStyle.Fill, Visible = false };
        Controls.Add(_webView);

        // Native loading overlay — shown immediately while WebView2 initializes
        _loadingOverlay = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.FromArgb(0x0b, 0x0b, 0x11)
        };
        _loadingOverlay.Paint += PaintLoadingOverlay;
        Controls.Add(_loadingOverlay);
        _loadingOverlay.BringToFront();
    }

    private void PaintLoadingOverlay(object? sender, PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;

        var panel = (Panel)sender!;
        var cx = panel.Width / 2;
        var cy = panel.Height / 2;

        // App icon
        if (Icon is not null)
        {
            var iconSize = 48;
            g.DrawIcon(Icon, new Rectangle(cx - iconSize / 2, cy - 60, iconSize, iconSize));
        }

        // App name
        using var titleFont = new Font("Segoe UI", 16f, FontStyle.Regular);
        using var titleBrush = new SolidBrush(Color.FromArgb(0xe0, 0xe0, 0xe8));
        var titleText = _overlayTitle;
        var titleSize = g.MeasureString(titleText, titleFont);
        g.DrawString(titleText, titleFont, titleBrush, cx - titleSize.Width / 2, cy + 4);

        // Loading or error text
        using var messageFont = new Font("Segoe UI", 10f, FontStyle.Regular);
        using var messageBrush = new SolidBrush(Color.FromArgb(0x80, 0x80, 0x90));
        using var detailFont = new Font("Segoe UI", 9f, FontStyle.Regular);
        using var detailBrush = new SolidBrush(Color.FromArgb(0xa8, 0xa8, 0xb6));
        var messageRect = new RectangleF(cx - 280, cy + 32, 560, 48);
        var detailRect = new RectangleF(cx - 280, cy + 72, 560, 88);
        using var centeredFormat = new StringFormat
        {
            Alignment = StringAlignment.Center,
            LineAlignment = StringAlignment.Near
        };

        g.DrawString(_overlayMessage, messageFont, messageBrush, messageRect, centeredFormat);
        if (!string.IsNullOrWhiteSpace(_overlayDetail))
        {
            g.DrawString(_overlayDetail, detailFont, detailBrush, detailRect, centeredFormat);
        }
    }

    public void HideLoadingOverlay()
    {
        if (_loadingOverlay is null) return;
        _webView.Visible = true;
        Controls.Remove(_loadingOverlay);
        _loadingOverlay.Dispose();
        _loadingOverlay = null;
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);

        // Windows 11 (build 22000+) rounded corners
        if (Environment.OSVersion.Version.Build >= 22000)
        {
            try
            {
                var preference = DWMWCP_ROUND;
                DwmSetWindowAttribute(Handle, DWMWA_WINDOW_CORNER_PREFERENCE,
                    ref preference, sizeof(int));
            }
            catch { /* DWM unavailable — square corners fallback */ }
        }
    }

    public async Task InitializeAsync()
    {
        // Use pre-created environment if available (started in SystemTrayService.StartAsync,
        // overlapping with Kestrel startup for ~300-2000ms savings)
        var env = _preCreatedEnvTask is not null
            ? await _preCreatedEnvTask
            : await CoreWebView2Environment.CreateAsync(browserExecutableFolder: null, userDataFolder: null);
        await _webView.EnsureCoreWebView2Async(env);

        _webView.CoreWebView2.Settings.IsStatusBarEnabled = false;
        _webView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = true;

        // Enable CSS app-region: drag for custom title bar
        try { _webView.CoreWebView2.Settings.IsNonClientRegionSupportEnabled = true; }
        catch { /* Older runtime — drag region won't work, but the app is still usable */ }

        _webView.CoreWebView2.WebMessageReceived += OnWebMessageReceived;
        _logger.LogInformation("WebView2 initialized, probing startup page {Url}", _appUrl);

        var probeResult = await ProbeHomePageAsync();
        if (!probeResult.Success)
        {
            _logger.LogError("Startup page probe failed for {Url}: {Detail}", _appUrl, probeResult.Detail);
            ShowOverlayError(
                "前端页面无法访问，请检查安装目录中的 wwwroot 文件。",
                probeResult.Detail);
            return;
        }

        _logger.LogInformation(
            "Startup page probe succeeded with status {StatusCode}, navigating to {Url}",
            probeResult.StatusCode,
            _appUrl);

        var navigationResult = await NavigateAndWaitForReadyAsync();
        if (!navigationResult.Success)
        {
            _logger.LogError("Initial WebView2 navigation failed for {Url}: {Detail}", _appUrl, navigationResult.Detail);
            ShowOverlayError("前端页面加载失败，请重启工具箱或查看日志。", navigationResult.Detail);
            return;
        }

        HideLoadingOverlay();
    }

    private async Task<StartupProbeResult> ProbeHomePageAsync()
    {
        try
        {
            using var client = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(5)
            };
            using var response = await client.GetAsync(_appUrl, HttpCompletionOption.ResponseHeadersRead);
            if (!response.IsSuccessStatusCode)
            {
                return new StartupProbeResult(
                    false,
                    $"首页返回 HTTP {(int)response.StatusCode} {response.ReasonPhrase}",
                    response.StatusCode);
            }

            return new StartupProbeResult(true, null, response.StatusCode);
        }
        catch (Exception ex)
        {
            return new StartupProbeResult(false, ex.Message);
        }
    }

    private async Task<StartupProbeResult> NavigateAndWaitForReadyAsync()
    {
        var navigationTcs = new TaskCompletionSource<StartupProbeResult>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        EventHandler<CoreWebView2NavigationCompletedEventArgs>? handler = null;
        handler = (_, args) =>
        {
            if (!args.IsSuccess)
            {
                navigationTcs.TrySetResult(new StartupProbeResult(
                    false,
                    $"WebView2 导航失败: {args.WebErrorStatus}"));
                return;
            }

            navigationTcs.TrySetResult(new StartupProbeResult(true, null, HttpStatusCode.OK));
        };

        _webView.CoreWebView2.NavigationCompleted += handler;
        try
        {
            _webView.CoreWebView2.Navigate(_appUrl);
            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            using var registration = timeoutCts.Token.Register(() =>
                navigationTcs.TrySetResult(new StartupProbeResult(false, "首页导航超时")));
            return await navigationTcs.Task;
        }
        finally
        {
            _webView.CoreWebView2.NavigationCompleted -= handler;
        }
    }

    private void ShowOverlayError(string message, string? detail)
    {
        _overlayTitle = "前端页面加载失败";
        _overlayMessage = message;
        _overlayDetail = detail;
        _webView.Visible = false;
        _loadingOverlay?.Invalidate();
    }

    private void OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        var message = e.TryGetWebMessageAsString();
        switch (message)
        {
            case "minimize":
                WindowState = FormWindowState.Minimized;
                break;
            case "maximize":
                WindowState = WindowState == FormWindowState.Maximized
                    ? FormWindowState.Normal
                    : FormWindowState.Maximized;
                break;
            case "close":
                Hide();
                break;
        }
    }

    protected override void OnSizeChanged(EventArgs e)
    {
        base.OnSizeChanged(e);
        NotifyWindowState();
    }

    private void NotifyWindowState()
    {
        try
        {
            if (_webView.CoreWebView2 is not null)
            {
                var state = WindowState == FormWindowState.Maximized ? "maximized" : "normal";
                _webView.CoreWebView2.PostWebMessageAsString(state);
            }
        }
        catch { /* WebView2 may not be ready yet */ }
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WM_NCHITTEST && WindowState != FormWindowState.Maximized)
        {
            var lp = (nint)m.LParam;
            var screenPoint = new Point(
                (short)(lp & 0xFFFF),
                (short)((lp >> 16) & 0xFFFF));
            var pos = PointToClient(screenPoint);

            var hitTest = GetResizeHitTest(pos);
            if (hitTest != HTCLIENT)
            {
                m.Result = (IntPtr)hitTest;
                return;
            }
        }

        if (m.Msg == WM_GETMINMAXINFO)
        {
            var mmi = Marshal.PtrToStructure<MINMAXINFO>(m.LParam);
            var screen = Screen.FromHandle(Handle);
            var workArea = screen.WorkingArea;
            mmi.ptMaxPosition = new Point(
                workArea.Left - screen.Bounds.Left,
                workArea.Top - screen.Bounds.Top);
            mmi.ptMaxSize = new Point(workArea.Width, workArea.Height);
            Marshal.StructureToPtr(mmi, m.LParam, false);
        }

        base.WndProc(ref m);
    }

    private int GetResizeHitTest(Point pos)
    {
        bool top = pos.Y <= ResizeBorderWidth;
        bool bottom = pos.Y >= ClientSize.Height - ResizeBorderWidth;
        bool left = pos.X <= ResizeBorderWidth;
        bool right = pos.X >= ClientSize.Width - ResizeBorderWidth;

        if (top && left) return HTTOPLEFT;
        if (top && right) return HTTOPRIGHT;
        if (bottom && left) return HTBOTTOMLEFT;
        if (bottom && right) return HTBOTTOMRIGHT;
        if (top) return HTTOP;
        if (bottom) return HTBOTTOM;
        if (left) return HTLEFT;
        if (right) return HTRIGHT;

        return HTCLIENT;
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            Hide();
            return;
        }

        // Application.Exit() path — detach WebView2 to skip its slow Dispose (~500-1000ms).
        // The browser subprocess self-terminates when the host process exits (IPC channel breaks).
        Controls.Remove(_webView);

        base.OnFormClosing(e);
    }

    [DllImport("dwmapi.dll", ExactSpelling = true, PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(
        IntPtr hwnd, int dwAttribute, ref int pvAttribute, int cbAttribute);

    [StructLayout(LayoutKind.Sequential)]
    private struct MINMAXINFO
    {
        public Point ptReserved;
        public Point ptMaxSize;
        public Point ptMaxPosition;
        public Point ptMinTrackSize;
        public Point ptMaxTrackSize;
    }

    private readonly record struct StartupProbeResult(bool Success, string? Detail, HttpStatusCode? StatusCode = null);
}
