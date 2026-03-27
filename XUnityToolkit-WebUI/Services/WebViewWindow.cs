using System.Runtime.InteropServices;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace XUnityToolkit_WebUI.Services;

internal sealed class WebViewWindow : Form
{
    private readonly WebView2 _webView;
    private readonly string _appUrl;
    private readonly string _userDataFolder;
    private readonly ILogger _logger;

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

    public WebViewWindow(string appUrl, string userDataFolder, ILogger logger)
    {
        _appUrl = appUrl;
        _userDataFolder = userDataFolder;
        _logger = logger;

        Text = "XUnity Toolkit WebUI";
        Size = new Size(1200, 800);
        MinimumSize = new Size(500, 400);
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.None;
        Icon = Icon.ExtractAssociatedIcon(Environment.ProcessPath!) ?? SystemIcons.Application;

        _webView = new WebView2 { Dock = DockStyle.Fill };
        Controls.Add(_webView);
    }

    public async Task InitializeAsync()
    {
        Directory.CreateDirectory(_userDataFolder);

        var env = await CoreWebView2Environment.CreateAsync(
            browserExecutableFolder: null,
            userDataFolder: _userDataFolder);
        await _webView.EnsureCoreWebView2Async(env);

        _webView.CoreWebView2.Settings.IsStatusBarEnabled = false;
        _webView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = true;

        // Enable CSS app-region: drag for custom title bar
        try { _webView.CoreWebView2.Settings.IsNonClientRegionSupportEnabled = true; }
        catch { /* Older runtime — drag region won't work, but the app is still usable */ }

        _webView.CoreWebView2.WebMessageReceived += OnWebMessageReceived;

        _webView.CoreWebView2.Navigate(_appUrl);
        _logger.LogInformation("WebView2 initialized, navigating to {Url}", _appUrl);
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

        // Application.Exit() path -- allow close, dispose WebView2
        try { _webView.Dispose(); }
        catch { /* best effort */ }

        base.OnFormClosing(e);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MINMAXINFO
    {
        public Point ptReserved;
        public Point ptMaxSize;
        public Point ptMaxPosition;
        public Point ptMinTrackSize;
        public Point ptMaxTrackSize;
    }
}
