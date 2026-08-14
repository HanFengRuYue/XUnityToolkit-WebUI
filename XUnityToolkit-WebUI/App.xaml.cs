using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using XUnityToolkit_WebUI.Infrastructure;
using XUnityToolkit_WebUI.Services;

namespace XUnityToolkit_WebUI;

public sealed partial class DesktopApp : Microsoft.UI.Xaml.Application
{
    private readonly WebApplication _webApplication;
    private readonly ILogger<DesktopApp> _logger;
    private MainWindow? _mainWindow;
    private Task? _startupTask;

    // The XAML compiler always emits an unused generated entry point that constructs App.
    // Our custom STA Main supplies the WebApplication through the overload below.
    public DesktopApp()
    {
        throw new InvalidOperationException("DesktopApp must be created by the custom program entry point.");
    }

    public DesktopApp(WebApplication webApplication)
    {
        _webApplication = webApplication;
        _logger = webApplication.Services.GetRequiredService<ILogger<DesktopApp>>();
        InitializeComponent();
        UnhandledException += OnUnhandledException;
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        var services = _webApplication.Services;
        var desktopWindow = services.GetRequiredService<IDesktopWindowService>();

        _mainWindow = new MainWindow(
            services.GetRequiredService<ToolkitRuntimeEndpointState>(),
            services.GetRequiredService<AppDataPaths>(),
            services.GetRequiredService<IHttpClientFactory>(),
            desktopWindow,
            services.GetRequiredService<ILogger<MainWindow>>());

        desktopWindow.Attach(_mainWindow, DispatcherQueue.GetForCurrentThread());
        _mainWindow.Activate();
        _startupTask = StartBackendAndWebViewAsync(desktopWindow);
    }

    private async Task StartBackendAndWebViewAsync(IDesktopWindowService desktopWindow)
    {
        var webViewPreparation = _mainWindow!.PrepareWebViewAsync();
        try
        {
            await _webApplication.StartAsync();

            if (!await webViewPreparation)
            {
                desktopWindow.SwitchToBrowserFallback(openImmediately: true);
                return;
            }

            await _mainWindow.NavigateToBackendAsync();
        }
        catch (OperationCanceledException) when (_webApplication.Lifetime.ApplicationStopping.IsCancellationRequested)
        {
            // Normal shutdown while startup was still in progress.
        }
        catch (Exception ex)
        {
            _logger.LogCritical(ex, "工具箱本机服务启动失败");
            _mainWindow.ShowStartupError("工具箱无法启动本机服务。", ex.GetBaseException().Message);
        }
    }

    private void OnUnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
    {
        _logger.LogCritical(e.Exception, "WinUI 桌面壳发生未处理异常");
        e.Handled = true;
        _webApplication.Services.GetRequiredService<IDesktopWindowService>().RequestExit();
    }
}
