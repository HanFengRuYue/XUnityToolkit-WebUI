using System.Net;
using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Web.WebView2.Core;
using Windows.Graphics;
using WinRT.Interop;
using XUnityToolkit_WebUI.Infrastructure;
using XUnityToolkit_WebUI.Services;
using WinUIColor = Windows.UI.Color;

namespace XUnityToolkit_WebUI;

public sealed partial class MainWindow : Window
{
    private const int DefaultWidth = 1200;
    private const int DefaultHeight = 800;
    private const int MinimumWidthEpx = 330;
    private const int MinimumHeightEpx = 400;

    private readonly ToolkitRuntimeEndpointState _runtimeEndpoint;
    private readonly AppDataPaths _paths;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IDesktopWindowService _desktopWindow;
    private readonly ILogger<MainWindow> _logger;
    private readonly WebView2 Browser = new()
    {
        HorizontalAlignment = Microsoft.UI.Xaml.HorizontalAlignment.Stretch,
        VerticalAlignment = VerticalAlignment.Stretch,
        Visibility = Visibility.Collapsed
    };
    private readonly SemaphoreSlim _navigationLock = new(1, 1);
    private readonly NativeWindowMinimumSizeHook _minimumSizeHook;
    private readonly nint _windowHandle;
    private bool _webViewPrepared;

    public MainWindow(
        ToolkitRuntimeEndpointState runtimeEndpoint,
        AppDataPaths paths,
        IHttpClientFactory httpClientFactory,
        IDesktopWindowService desktopWindow,
        ILogger<MainWindow> logger)
    {
        InitializeComponent();
        BrowserHost.Children.Add(Browser);

        _runtimeEndpoint = runtimeEndpoint;
        _paths = paths;
        _httpClientFactory = httpClientFactory;
        _desktopWindow = desktopWindow;
        _logger = logger;

        _windowHandle = WindowNative.GetWindowHandle(this);
        _minimumSizeHook = new NativeWindowMinimumSizeHook(
            _windowHandle,
            MinimumWidthEpx,
            MinimumHeightEpx);

        ConfigureNativeWindow();
        AppWindow.Closing += OnAppWindowClosing;
        Closed += OnWindowClosed;
    }

    public async Task<bool> PrepareWebViewAsync()
    {
        try
        {
            var runtimeVersion = CoreWebView2Environment.GetAvailableBrowserVersionString();
            if (string.IsNullOrWhiteSpace(runtimeVersion))
            {
                _logger.LogWarning("WebView2 runtime 不可用，将回退到默认浏览器");
                return false;
            }

            _logger.LogInformation("WebView2 runtime detected: {Version}", runtimeVersion);
            Directory.CreateDirectory(_paths.WebView2CacheDirectory);

            var environment = await CoreWebView2Environment.CreateWithOptionsAsync(
                string.Empty,
                _paths.WebView2CacheDirectory,
                new CoreWebView2EnvironmentOptions());
            await Browser.EnsureCoreWebView2Async(environment);

            Browser.DefaultBackgroundColor = WinUIColor.FromArgb(255, 11, 11, 17);
            Browser.CoreWebView2.Settings.IsStatusBarEnabled = false;
            Browser.CoreWebView2.Settings.AreDefaultContextMenusEnabled = true;
            Browser.CoreWebView2.WebMessageReceived += OnWebMessageReceived;
            Browser.CoreWebView2.ProcessFailed += OnWebViewProcessFailed;
            await Browser.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(
                """
                (() => {
                  const descriptor = Object.freeze({
                    protocolVersion: 1,
                    shell: 'winui3',
                    nativeTitleBar: true
                  });
                  Object.defineProperty(globalThis, '__XUNITY_DESKTOP_HOST__', {
                    value: descriptor,
                    configurable: false,
                    enumerable: false,
                    writable: false
                  });
                })();
                """);

            _webViewPrepared = true;
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "WinUI WebView2 初始化失败，将回退到默认浏览器");
            return false;
        }
    }

    public async Task NavigateToBackendAsync()
    {
        await _navigationLock.WaitAsync();
        try
        {
            if (!_webViewPrepared)
                return;

            ShowLoading("正在加载工具箱页面…");
            var appUrl = _runtimeEndpoint.BaseUrl;
            var probe = await ProbeHomePageAsync(appUrl);
            if (!probe.Success)
            {
                _logger.LogError("Startup page probe failed for {Url}: {Detail}", appUrl, probe.Detail);
                ShowStartupError(
                    "前端页面无法访问，请检查安装目录中的 wwwroot 文件。",
                    probe.Detail);
                _desktopWindow.OpenInDefaultBrowser();
                return;
            }

            _logger.LogInformation(
                "Startup page probe succeeded with status {StatusCode}, navigating to {Url}",
                probe.StatusCode,
                appUrl);

            var navigation = await NavigateAndWaitAsync(appUrl);
            if (!navigation.Success)
            {
                _logger.LogError("Initial WebView2 navigation failed for {Url}: {Detail}", appUrl, navigation.Detail);
                ShowStartupError("前端页面加载失败，请重试或查看日志。", navigation.Detail);
                _desktopWindow.OpenInDefaultBrowser();
                return;
            }

            StartupOverlay.Visibility = Visibility.Collapsed;
            Browser.Visibility = Visibility.Visible;
        }
        finally
        {
            _navigationLock.Release();
        }
    }

    public void ShowStartupError(string message, string? detail = null)
    {
        Browser.Visibility = Visibility.Collapsed;
        StartupOverlay.Visibility = Visibility.Visible;
        LoadingRing.IsActive = false;
        LoadingRing.Visibility = Visibility.Collapsed;
        OverlayTitle.Text = "工具箱启动失败";
        OverlayMessage.Text = message;
        OverlayDetail.Text = detail ?? string.Empty;
        OverlayDetail.Visibility = string.IsNullOrWhiteSpace(detail)
            ? Visibility.Collapsed
            : Visibility.Visible;
        ErrorActions.Visibility = Visibility.Visible;
    }

    internal void ShowAndActivate()
    {
        if (NativeMethods.IsIconic(_windowHandle))
            NativeMethods.ShowWindow(_windowHandle, NativeMethods.SW_RESTORE);

        AppWindow.Show(true);
        Activate();
        NativeMethods.SetForegroundWindow(_windowHandle);
    }

    internal void HideWindow() => AppWindow.Hide();

    internal void CloseForExit() => Close();

    private void ConfigureNativeWindow()
    {
        Title = "XUnity Toolkit WebUI";
        AppWindow.Title = Title;

        var iconPath = Path.Combine(AppContext.BaseDirectory, "app.ico");
        if (File.Exists(iconPath))
            AppWindow.SetIcon(iconPath);

        var displayArea = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary);
        if (displayArea is null)
        {
            AppWindow.Resize(new SizeInt32(DefaultWidth, DefaultHeight));
            return;
        }

        var width = Math.Min(DefaultWidth, Math.Max(MinimumWidthEpx, displayArea.WorkArea.Width));
        var height = Math.Min(DefaultHeight, Math.Max(MinimumHeightEpx, displayArea.WorkArea.Height));
        var x = displayArea.WorkArea.X + Math.Max(0, (displayArea.WorkArea.Width - width) / 2);
        var y = displayArea.WorkArea.Y + Math.Max(0, (displayArea.WorkArea.Height - height) / 2);
        AppWindow.MoveAndResize(new RectInt32(x, y, width, height));
    }

    private async Task<StartupProbeResult> ProbeHomePageAsync(string appUrl)
    {
        try
        {
            using var client = _httpClientFactory.CreateClient("ToolkitLoopback");
            using var response = await client.GetAsync(appUrl, HttpCompletionOption.ResponseHeadersRead);
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

    private async Task<StartupProbeResult> NavigateAndWaitAsync(string appUrl)
    {
        var completion = new TaskCompletionSource<StartupProbeResult>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        Windows.Foundation.TypedEventHandler<
            CoreWebView2,
            CoreWebView2NavigationCompletedEventArgs>? handler = null;
        handler = (_, args) => completion.TrySetResult(
            args.IsSuccess
                ? new StartupProbeResult(true, null, HttpStatusCode.OK)
                : new StartupProbeResult(false, $"WebView2 导航失败: {args.WebErrorStatus}"));

        Browser.CoreWebView2.NavigationCompleted += handler;
        try
        {
            Browser.CoreWebView2.Navigate(appUrl);
            try
            {
                return await completion.Task.WaitAsync(TimeSpan.FromSeconds(15));
            }
            catch (TimeoutException)
            {
                return new StartupProbeResult(false, "首页导航超时");
            }
        }
        finally
        {
            Browser.CoreWebView2.NavigationCompleted -= handler;
        }
    }

    private void OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        if (!IsTrustedMessageSource(e.Source))
        {
            _logger.LogWarning("忽略来自非工具箱页面的 WebView 消息: {Source}", e.Source);
            return;
        }

        try
        {
            using var message = JsonDocument.Parse(e.WebMessageAsJson);
            var root = message.RootElement;
            if (!root.TryGetProperty("type", out var type)
                || type.GetString() != "themeChanged"
                || !root.TryGetProperty("theme", out var theme))
            {
                return;
            }

            switch (theme.GetString())
            {
                case "dark":
                    ApplyNativeTheme(dark: true);
                    break;
                case "light":
                    ApplyNativeTheme(dark: false);
                    break;
            }
        }
        catch (JsonException ex)
        {
            _logger.LogDebug(ex, "忽略无效的桌面宿主消息");
        }
    }

    private bool IsTrustedMessageSource(string source)
    {
        if (!Uri.TryCreate(source, UriKind.Absolute, out var sourceUri)
            || !Uri.TryCreate(_runtimeEndpoint.BaseUrl, UriKind.Absolute, out var appUri))
        {
            return false;
        }

        return sourceUri.Scheme == Uri.UriSchemeHttp
            && sourceUri.Host == "127.0.0.1"
            && sourceUri.Port == appUri.Port;
    }

    private void ApplyNativeTheme(bool dark)
    {
        RootLayout.RequestedTheme = dark ? ElementTheme.Dark : ElementTheme.Light;
        if (!AppWindowTitleBar.IsCustomizationSupported())
            return;

        var titleBar = AppWindow.TitleBar;
        if (dark)
        {
            titleBar.ForegroundColor = WinUIColor.FromArgb(255, 224, 224, 232);
            titleBar.BackgroundColor = WinUIColor.FromArgb(255, 11, 11, 17);
            titleBar.ButtonForegroundColor = WinUIColor.FromArgb(255, 224, 224, 232);
            titleBar.ButtonBackgroundColor = WinUIColor.FromArgb(255, 11, 11, 17);
            titleBar.ButtonHoverForegroundColor = WinUIColor.FromArgb(255, 255, 255, 255);
            titleBar.ButtonHoverBackgroundColor = WinUIColor.FromArgb(255, 35, 35, 45);
            titleBar.ButtonPressedForegroundColor = WinUIColor.FromArgb(255, 255, 255, 255);
            titleBar.ButtonPressedBackgroundColor = WinUIColor.FromArgb(255, 48, 48, 60);
            titleBar.InactiveForegroundColor = WinUIColor.FromArgb(255, 144, 144, 158);
            titleBar.InactiveBackgroundColor = WinUIColor.FromArgb(255, 11, 11, 17);
            titleBar.ButtonInactiveForegroundColor = WinUIColor.FromArgb(255, 144, 144, 158);
            titleBar.ButtonInactiveBackgroundColor = WinUIColor.FromArgb(255, 11, 11, 17);
        }
        else
        {
            titleBar.ForegroundColor = WinUIColor.FromArgb(255, 31, 31, 38);
            titleBar.BackgroundColor = WinUIColor.FromArgb(255, 248, 248, 250);
            titleBar.ButtonForegroundColor = WinUIColor.FromArgb(255, 31, 31, 38);
            titleBar.ButtonBackgroundColor = WinUIColor.FromArgb(255, 248, 248, 250);
            titleBar.ButtonHoverForegroundColor = WinUIColor.FromArgb(255, 0, 0, 0);
            titleBar.ButtonHoverBackgroundColor = WinUIColor.FromArgb(255, 230, 230, 235);
            titleBar.ButtonPressedForegroundColor = WinUIColor.FromArgb(255, 0, 0, 0);
            titleBar.ButtonPressedBackgroundColor = WinUIColor.FromArgb(255, 216, 216, 224);
            titleBar.InactiveForegroundColor = WinUIColor.FromArgb(255, 112, 112, 124);
            titleBar.InactiveBackgroundColor = WinUIColor.FromArgb(255, 248, 248, 250);
            titleBar.ButtonInactiveForegroundColor = WinUIColor.FromArgb(255, 112, 112, 124);
            titleBar.ButtonInactiveBackgroundColor = WinUIColor.FromArgb(255, 248, 248, 250);
        }
    }

    private void ShowLoading(string message)
    {
        Browser.Visibility = Visibility.Collapsed;
        StartupOverlay.Visibility = Visibility.Visible;
        LoadingRing.Visibility = Visibility.Visible;
        LoadingRing.IsActive = true;
        OverlayTitle.Text = "XUnity Toolkit";
        OverlayMessage.Text = message;
        OverlayDetail.Visibility = Visibility.Collapsed;
        ErrorActions.Visibility = Visibility.Collapsed;
    }

    private void OnWebViewProcessFailed(object? sender, CoreWebView2ProcessFailedEventArgs e)
    {
        _logger.LogError("WebView2 process failed: {Kind}, {Reason}", e.ProcessFailedKind, e.Reason);
        ShowStartupError("WebView2 进程异常退出，请重试或在浏览器中打开。", e.Reason.ToString());
        _desktopWindow.SwitchToBrowserFallback(openImmediately: true);
    }

    private void OnAppWindowClosing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        if (_desktopWindow.IsExitRequested)
            return;

        args.Cancel = _desktopWindow.HandleUserClose();
    }

    private void OnWindowClosed(object sender, WindowEventArgs args)
    {
        _minimumSizeHook.Dispose();
        _navigationLock.Dispose();
    }

    private void Retry_Click(object sender, RoutedEventArgs e) => _ = NavigateToBackendAsync();

    private void OpenBrowser_Click(object sender, RoutedEventArgs e) =>
        _desktopWindow.OpenInDefaultBrowser();

    private readonly record struct StartupProbeResult(
        bool Success,
        string? Detail,
        HttpStatusCode? StatusCode = null);

    private sealed class NativeWindowMinimumSizeHook : IDisposable
    {
        private const uint WM_GETMINMAXINFO = 0x0024;
        private const uint WM_NCDESTROY = 0x0082;

        private readonly nint _window;
        private readonly int _minimumWidthEpx;
        private readonly int _minimumHeightEpx;
        private readonly NativeMethods.SubclassProc _callback;
        private readonly nuint _subclassId;
        private bool _attached;

        public NativeWindowMinimumSizeHook(nint window, int minimumWidthEpx, int minimumHeightEpx)
        {
            _window = window;
            _minimumWidthEpx = minimumWidthEpx;
            _minimumHeightEpx = minimumHeightEpx;
            _callback = WindowProc;
            _subclassId = unchecked((nuint)GetHashCode());
            _attached = NativeMethods.SetWindowSubclass(_window, _callback, _subclassId, 0);
        }

        public void Dispose()
        {
            if (!_attached)
                return;

            NativeMethods.RemoveWindowSubclass(_window, _callback, _subclassId);
            _attached = false;
        }

        private nint WindowProc(
            nint window,
            uint message,
            nuint wParam,
            nint lParam,
            nuint subclassId,
            nuint referenceData)
        {
            if (message == WM_GETMINMAXINFO)
            {
                var info = Marshal.PtrToStructure<NativeMethods.MinMaxInfo>(lParam);
                var dpi = NativeMethods.GetDpiForWindow(window);
                info.MinTrackSize.X = NativeMethods.MulDiv(_minimumWidthEpx, (int)dpi, 96);
                info.MinTrackSize.Y = NativeMethods.MulDiv(_minimumHeightEpx, (int)dpi, 96);
                Marshal.StructureToPtr(info, lParam, false);
            }
            else if (message == WM_NCDESTROY)
            {
                Dispose();
            }

            return NativeMethods.DefSubclassProc(window, message, wParam, lParam);
        }
    }

    private static partial class NativeMethods
    {
        internal const int SW_RESTORE = 9;

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        internal delegate nint SubclassProc(
            nint window,
            uint message,
            nuint wParam,
            nint lParam,
            nuint subclassId,
            nuint referenceData);

        [LibraryImport("comctl32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static partial bool SetWindowSubclass(
            nint window,
            SubclassProc callback,
            nuint subclassId,
            nuint referenceData);

        [LibraryImport("comctl32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static partial bool RemoveWindowSubclass(
            nint window,
            SubclassProc callback,
            nuint subclassId);

        [LibraryImport("comctl32.dll")]
        internal static partial nint DefSubclassProc(nint window, uint message, nuint wParam, nint lParam);

        [LibraryImport("user32.dll")]
        internal static partial uint GetDpiForWindow(nint window);

        [LibraryImport("kernel32.dll")]
        internal static partial int MulDiv(int number, int numerator, int denominator);

        [LibraryImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static partial bool IsIconic(nint window);

        [LibraryImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static partial bool ShowWindow(nint window, int command);

        [LibraryImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static partial bool SetForegroundWindow(nint window);

        [StructLayout(LayoutKind.Sequential)]
        internal struct Point
        {
            public int X;
            public int Y;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct MinMaxInfo
        {
            public Point Reserved;
            public Point MaxSize;
            public Point MaxPosition;
            public Point MinTrackSize;
            public Point MaxTrackSize;
        }
    }
}
