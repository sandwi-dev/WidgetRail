using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using WinUIEx;
using WinUIEx.Messaging;

namespace WidgetRail.OverlayFrontend.WinUI.Shell;

/// <summary>Peer native window. The coordinator owns widget/session and input authority.</summary>
internal sealed class PinnedWidgetWindow : IDisposable
{
    private readonly Window window;
    private WindowMessageMonitor? messages;
    private readonly Grid content = new();
    private bool disposed;
    internal event Action? CloseRequested;
    internal event Action<bool>? ForegroundChanged;
    internal bool Interactive { get; private set; }
    internal bool IsVisible => !disposed && window.AppWindow.IsVisible;
    internal nint Handle { get; }
    internal FrameworkElement? Child { get; private set; }
    internal Window NativeWindow => window;
    internal PinnedBounds Bounds => new(window.AppWindow.Position.X, window.AppWindow.Position.Y,
        window.AppWindow.Size.Width, window.AppWindow.Size.Height);
    internal int OpacityPercent { get; private set; } = 100;

    internal PinnedWidgetWindow(string name)
    {
        window = new Window { Title = "WidgetRail pinned — " + name, ExtendsContentIntoTitleBar = true,
            SystemBackdrop = new TransparentTintBackdrop(), Content = content };
        Handle = WinRT.Interop.WindowNative.GetWindowHandle(window);
        try
        {
            window.AppWindow.IsShownInSwitchers = false;
            var presenter = (OverlappedPresenter)window.AppWindow.Presenter;
            presenter.SetBorderAndTitleBar(false, false);
            presenter.IsResizable = presenter.IsMaximizable = presenter.IsMinimizable = false;
            presenter.IsAlwaysOnTop = true;
            messages = new(window);
            messages.WindowMessageReceived += (_, args) =>
            {
                if (Interactive) return;
                if (args.Message.MessageId == 0x0084) { args.Handled = true; args.Result = -1; } // HTTRANSPARENT
                else if (args.Message.MessageId == 0x0021) { args.Handled = true; args.Result = 3; } // MA_NOACTIVATE
            };
            window.AppWindow.Closing += (_, args) =>
            {
                if (disposed) return;
                args.Cancel = true;
                CloseRequested?.Invoke();
            };
            window.Activated += (_, args) => ForegroundChanged?.Invoke(args.WindowActivationState != WindowActivationState.Deactivated);
            SetInteraction(false);
            SetOpacity(100);
        }
        catch
        {
            disposed = true;
            messages?.Dispose();
            window.Close();
            throw;
        }
    }

    internal void SetContent(FrameworkElement element)
    {
        DemandAlive();
        if (ReferenceEquals(Child, element)) return;
        content.Children.Clear(); content.Children.Add(element); Child = element;
    }
    internal void Place(PinnedBounds bounds)
    {
        DemandAlive();
        if (bounds.Width <= 0 || bounds.Height <= 0) throw new ArgumentOutOfRangeException(nameof(bounds));
        if (Bounds != bounds) window.AppWindow.MoveAndResize(new(bounds.X, bounds.Y, bounds.Width, bounds.Height));
    }
    internal void Show()
    {
        DemandAlive();
        if (!IsVisible) window.AppWindow.Show(activateWindow: false);
    }
    internal void Hide()
    {
        if (disposed) return;
        SetInteraction(false);
        window.AppWindow.Hide();
    }
    internal void SetInteraction(bool enabled)
    {
        DemandAlive();
        Interactive = enabled;
        var style = window.GetExtendedWindowStyle() | ExtendedWindowStyle.ToolWindow | ExtendedWindowStyle.Layered;
        style = enabled ? style & ~(ExtendedWindowStyle.NoActivate | ExtendedWindowStyle.Transparent)
            : style | ExtendedWindowStyle.NoActivate | ExtendedWindowStyle.Transparent;
        window.SetExtendedWindowStyle(style);
        content.IsHitTestVisible = enabled;
        // Passive content remains readable. The coordinator must also reject
        // automation actions through its presentation admission callback.
        AutomationProperties.SetAccessibilityView(content, enabled ? AccessibilityView.Content : AccessibilityView.Raw);
        // Do not set IsEnabled on the presentation: that would apply disabled
        // widget styles just because the host window is click-through. The
        // coordinator separately gates presenter entry and action admission.
    }
    internal void SetOpacity(int percent)
    {
        DemandAlive();
        OpacityPercent = Math.Clamp(percent, 30, 100);
        window.SetWindowOpacity((byte)Math.Round(OpacityPercent * 255d / 100));
    }
    private void DemandAlive()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (!content.DispatcherQueue.HasThreadAccess) throw new InvalidOperationException("Pinned windows require their XAML dispatcher.");
    }
    public void Dispose()
    {
        if (disposed) return;
        DemandAlive();
        SetInteraction(false); window.AppWindow.Hide();
        disposed = true;
        messages?.Dispose(); messages = null;
        content.Children.Clear(); Child = null;
        window.Close();
    }
}
