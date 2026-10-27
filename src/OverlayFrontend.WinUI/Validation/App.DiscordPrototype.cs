using Microsoft.UI.Xaml;
using WidgetRail.OverlayFrontend.WinUI.Shell;

namespace WidgetRail.OverlayFrontend.WinUI;

public partial class App
{
    partial void TryLaunchMediaPrototype(ref bool handled)
    {
        var discord = FrontendArguments.Value(arguments, "--discord-webview-prototype");
        var capture = FrontendArguments.Value(arguments, "--discord-capture-probe");
        var backend = FrontendArguments.Value(arguments, "--validate-web-session-backend");
        var microphone = FrontendArguments.Value(arguments, "--validate-web-session-microphone");
        var webCapture = FrontendArguments.Value(arguments, "--validate-web-session-capture");
        var browserSource = FrontendArguments.Value(arguments, "--capture-browser-source");
        if (discord is null && capture is null && backend is null && microphone is null && webCapture is null && browserSource is null) return;
        handled = true;
        var page = browserSource is not null ? (FrameworkElement)new Validation.BrowserCaptureSourcePage(browserSource) : webCapture is not null ? new Validation.WebSessionCaptureMediaValidationPage(webCapture) : microphone is not null ? new Validation.WebSessionMicrophoneValidationPage(microphone) : backend is not null ? new Validation.WebSessionBackendValidationPage(backend) : discord is not null ? new Validation.DiscordWebViewPrototypePage(discord) : new Validation.DiscordCaptureProbePage(capture!);
        var window = new Window { Title = browserSource is not null ? "WidgetRail synthetic browser capture target" : microphone is not null ? "WidgetRail Microphone Validation" : backend is not null ? "WidgetRail Backend Session Validation" : discord is not null ? "Discord WebView2 Prototype" : "WidgetRail Capture Probe", Content = page };
        Window = window;
        DispatcherQueue = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();
        window.AppWindow.Resize(browserSource is not null ? new(1280, 760) : new(discord is not null ? 1440 : 1000, 1000));
        if(browserSource is not null&&FrontendArguments.Value(arguments,"--capture-source-width") is { } requestedWidth)
        {
            if(!int.TryParse(requestedWidth,out var sourceWidth)||!int.TryParse(FrontendArguments.Value(arguments,"--capture-source-height"),out var sourceHeight)||
                sourceWidth is <2 or >8192||sourceHeight is <2 or >8192||(long)sourceWidth*sourceHeight>33554432)
                throw new ArgumentException("Invalid synthetic source dimensions.");
            ((Microsoft.UI.Windowing.OverlappedPresenter)window.AppWindow.Presenter).SetBorderAndTitleBar(false,false);
            window.AppWindow.Resize(new(sourceWidth,sourceHeight));
        }
        var closing = false;
        window.AppWindow.Closing += async (_, e) =>
        {
            if (closing) return;
            e.Cancel = true;
            closing = true;
            try { await ((IAsyncDisposable)page).DisposeAsync(); }
            finally { window.Close(); }
        };
        if(arguments.Contains("--capture-track-constraints"))window.AppWindow.Show(false);
        else window.Activate();
    }
}
