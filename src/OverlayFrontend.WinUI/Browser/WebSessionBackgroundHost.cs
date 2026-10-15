using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using WidgetRail.OverlayFrontend.WinUI.Shell;
using WinUIEx;

namespace WidgetRail.OverlayFrontend.WinUI.Browser;

/// <summary>
/// A shared native backing for sessions without a visible widget view. Chromium
/// can defer new media loads indefinitely in a collapsed WebView or hidden HWND.
/// Keep a tiny, non-interactive offscreen island alive independently of the overlay.
/// This owns no browser environment, profile, transport, or extra process.
/// </summary>
internal sealed class WebSessionBackgroundHost : IDisposable
{
    private readonly AppWindow window;
    private readonly DesktopWindowXamlSource island;
    private readonly TaskCompletionSource ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private bool disposed;
    internal Grid Root { get; } = new() { IsHitTestVisible = false, Opacity = 0 };
    internal Task Ready => ready.Task;

    internal WebSessionBackgroundHost()
    {
        window = AppWindow.Create();
        island = new DesktopWindowXamlSource();
        try
        {
            window.Title = "WidgetRail background sessions";
            window.IsShownInSwitchers = false;
            var presenter = (OverlappedPresenter)window.Presenter;
            presenter.SetBorderAndTitleBar(false, false);
            presenter.IsResizable = presenter.IsMaximizable = presenter.IsMinimizable = false;
            var handle = Microsoft.UI.Win32Interop.GetWindowFromWindowId(window.Id);
            var style = HwndExtensions.GetExtendedWindowStyle(handle) |
                ExtendedWindowStyle.ToolWindow | ExtendedWindowStyle.NoActivate |
                ExtendedWindowStyle.Transparent | ExtendedWindowStyle.Layered;
            OverlayWindowFrame.SetExtendedStyleWithoutActivation(handle, (nint)(uint)style);
            HwndExtensions.SetWindowOpacity(handle, 0);
            window.MoveAndResize(new(-32000, -32000, 1, 1));
            AutomationProperties.SetAccessibilityView(Root, AccessibilityView.Raw);
            Root.Loaded += OnLoaded;
            island.Initialize(window.Id);
            island.Content = Root;
            window.Show(activateWindow: false);
            OverlayWindowFrame.SuppressBorder(handle);
            island.SiteBridge.MoveAndResize(new(0, 0, 1, 1));
            island.SiteBridge.Show();
            if (Root.IsLoaded) OnLoaded(Root, null!);
        }
        catch { Dispose(); throw; }
    }

    private void OnLoaded(object sender, RoutedEventArgs args)
    {
        Root.UpdateLayout();
        ready.TrySetResult();
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        Root.Loaded -= OnLoaded;
        ready.TrySetCanceled();
        try { island.Dispose(); }
        finally { window.Destroy(); }
    }
}
