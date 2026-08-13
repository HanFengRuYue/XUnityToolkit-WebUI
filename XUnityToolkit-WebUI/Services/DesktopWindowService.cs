using System.Diagnostics;
using Microsoft.UI.Dispatching;
using XUnityToolkit_WebUI.Infrastructure;

namespace XUnityToolkit_WebUI.Services;

public interface IDesktopWindowService
{
    bool UsesBrowserFallback { get; }
    bool IsExitRequested { get; }
    event EventHandler? ModeChanged;

    void Attach(MainWindow window, DispatcherQueue dispatcherQueue);
    void SetTrayAvailable(bool available);
    bool Activate();
    void Hide();
    bool HandleUserClose();
    void SwitchToBrowserFallback(bool openImmediately);
    bool OpenInDefaultBrowser();
    void RequestExit();
    void ShutdownFromHost();
}

internal sealed class DesktopWindowService(
    IHostApplicationLifetime lifetime,
    ToolkitRuntimeEndpointState runtimeEndpoint,
    ILogger<DesktopWindowService> logger) : IDesktopWindowService
{
    private readonly object _gate = new();
    private MainWindow? _window;
    private DispatcherQueue? _dispatcherQueue;
    private volatile bool _usesBrowserFallback;
    private volatile bool _trayAvailable;
    private int _exitRequested;

    public bool UsesBrowserFallback => _usesBrowserFallback;
    public bool IsExitRequested => Volatile.Read(ref _exitRequested) != 0;
    public event EventHandler? ModeChanged;

    public void Attach(MainWindow window, DispatcherQueue dispatcherQueue)
    {
        lock (_gate)
        {
            _window = window;
            _dispatcherQueue = dispatcherQueue;
        }
    }

    public void SetTrayAvailable(bool available) => _trayAvailable = available;

    public bool Activate()
    {
        if (_usesBrowserFallback)
            return OpenInDefaultBrowser();

        return RunOnUiThread(static window => window.ShowAndActivate());
    }

    public void Hide() => RunOnUiThread(static window => window.HideWindow());

    public bool HandleUserClose()
    {
        if (IsExitRequested)
            return false;

        if (_trayAvailable)
        {
            Hide();
            return true;
        }

        // Avoid leaving an invisible process if the window is closed before the tray loop is ready.
        RequestExit();
        return true;
    }

    public void SwitchToBrowserFallback(bool openImmediately)
    {
        if (!_usesBrowserFallback)
        {
            _usesBrowserFallback = true;
            ModeChanged?.Invoke(this, EventArgs.Empty);
        }

        Hide();
        if (openImmediately)
            OpenInDefaultBrowser();
    }

    public bool OpenInDefaultBrowser()
    {
        try
        {
            if (!runtimeEndpoint.IsStarted)
                return false;

            Process.Start(new ProcessStartInfo
            {
                FileName = runtimeEndpoint.BaseUrl,
                UseShellExecute = true
            });
            return true;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "无法使用默认浏览器打开工具箱页面");
            return false;
        }
    }

    public void RequestExit()
    {
        if (Interlocked.Exchange(ref _exitRequested, 1) != 0)
            return;

        Hide();
        lifetime.StopApplication();
        ExitXamlLoop();
    }

    public void ShutdownFromHost()
    {
        Interlocked.Exchange(ref _exitRequested, 1);
        Hide();
        ExitXamlLoop();
    }

    private bool RunOnUiThread(Action<MainWindow> action)
    {
        MainWindow? window;
        DispatcherQueue? dispatcherQueue;
        lock (_gate)
        {
            window = _window;
            dispatcherQueue = _dispatcherQueue;
        }

        if (window is null || dispatcherQueue is null)
            return false;

        if (dispatcherQueue.HasThreadAccess)
        {
            action(window);
            return true;
        }

        return dispatcherQueue.TryEnqueue(() => action(window));
    }

    private void ExitXamlLoop()
    {
        MainWindow? window;
        DispatcherQueue? dispatcherQueue;
        lock (_gate)
        {
            window = _window;
            dispatcherQueue = _dispatcherQueue;
        }

        if (window is null || dispatcherQueue is null)
            return;

        void Exit()
        {
            // Explicitly close the HWND first so the XAML island and WebView2 controller release
            // their native message hooks before ending the WinUI dispatcher loop.
            window.CloseForExit();
            Microsoft.UI.Xaml.Application.Current?.Exit();
        }

        if (dispatcherQueue.HasThreadAccess)
            Exit();
        else
            dispatcherQueue.TryEnqueue(Exit);
    }

}
