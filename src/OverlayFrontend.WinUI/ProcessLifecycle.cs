using Microsoft.UI.Xaml;
using WidgetRail.OverlayPlatformClient;
using WinUIEx.Messaging;

namespace WidgetRail.OverlayFrontend.WinUI;

/// <summary>Startup-thread lifetime around XAML, never part of a widget/input session.</summary>
internal sealed class ProcessLifecycle : IDisposable
{
    private const uint ShowMessage = 0x8000 + 0x719;
    private readonly OverlayProcessLease lease;
    private WindowMessageMonitor? monitor;
    internal static ProcessLifecycle? Current { get; private set; }
    internal ProcessLifecycle(OverlayProcessLease lease)
    {
        if (Current is not null) throw new InvalidOperationException("Process lifetime already exists.");
        this.lease = lease; Current = this;
    }
    // Call after the MainWindow constructor, before its initial Activate/StartInput.
    internal static void Attach(Window window, Action show)
    {
        if (Current is not { } current) return; // Validation-only launch.
        if (current.monitor is not null) throw new InvalidOperationException("Process activation is already attached.");
        current.monitor = new(window);
        current.monitor.WindowMessageReceived += (_, args) =>
        {
            if (args.Message.MessageId != ShowMessage) return;
            args.Handled = true;
            show(); // Same UI thread; the show route guards shutdown and never toggles.
        };
        current.lease.BindWindow((nuint)WinRT.Interop.WindowNative.GetWindowHandle(window), ShowMessage);
    }
    public void Dispose()
    {
        monitor?.Dispose(); monitor = null;
        lease.Dispose(); Current = null;
    }
}
