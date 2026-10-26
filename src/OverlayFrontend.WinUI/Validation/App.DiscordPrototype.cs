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
        if (discord is null && capture is null && backend is null) return;
        handled = true;
        var page = backend is not null ? (FrameworkElement)new Validation.WebSessionBackendValidationPage(backend) : discord is not null ? new Validation.DiscordWebViewPrototypePage(discord) : new Validation.DiscordCaptureProbePage(capture!);
        var window = new Window { Title = backend is not null ? "WidgetRail Backend Session Validation" : discord is not null ? "Discord WebView2 Prototype" : "WidgetRail Capture Probe", Content = page };
        Window = window;
        DispatcherQueue = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();
        window.AppWindow.Resize(new(discord is not null ? 1440 : 1000, 1000));
        var closing = false;
        window.AppWindow.Closing += async (_, e) =>
        {
            if (closing) return;
            e.Cancel = true;
            closing = true;
            try { await ((IAsyncDisposable)page).DisposeAsync(); }
            finally { window.Close(); }
        };
        window.Activate();
    }
}
