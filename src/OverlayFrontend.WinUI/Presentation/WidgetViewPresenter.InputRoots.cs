using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;

namespace WidgetRail.OverlayFrontend.WinUI.Presentation;

internal sealed partial class WidgetViewPresenter
{
    // The same policy applies to the ordinary tree and root-level overlay portals.
    internal IDisposable AttachPresentationInput(UIElement root)
    {
        RoutedEventHandler focused = (_, _) => { RememberFocus(); NotifyControllerGuideChanged(); };
        PointerEventHandler pressed = (_, _) => { CancelGroupEntry(); CancelScrollReveal(); };
        PointerEventHandler wheel = (_, _) => CancelScrollReveal();
        KeyEventHandler key = (_, args) =>
        {
            if (Input.GamepadKeyBoundary.Owns(this, args)) return;
            if (args.Key is Windows.System.VirtualKey.Up or Windows.System.VirtualKey.Down or
                Windows.System.VirtualKey.Left or Windows.System.VirtualKey.Right &&
                FindIndexedCollection() is { IsFocusParked: true, IsEntryPending: true }) return;
            if (args.Key is Windows.System.VirtualKey.Up or Windows.System.VirtualKey.Down or Windows.System.VirtualKey.Left or
                Windows.System.VirtualKey.Right or Windows.System.VirtualKey.Tab or Windows.System.VirtualKey.Home or
                Windows.System.VirtualKey.End or Windows.System.VirtualKey.PageUp or Windows.System.VirtualKey.PageDown or
                Windows.System.VirtualKey.Enter or Windows.System.VirtualKey.Space or Windows.System.VirtualKey.Escape) CancelGroupEntry();
        };
        root.GotFocus += focused;
        root.GettingFocus += OnGettingFocus;
        // Cancel the previous intent before this key starts a new one. Doing
        // this on bubbling KeyDown cancels the collection entry just queued by
        // DirectionalKeyDown for the very same arrow press.
        root.PreviewKeyDown += key;
        root.PreviewKeyDown += DirectionalKeyDown;
        root.AddHandler(PointerPressedEvent, pressed, true);
        root.AddHandler(PointerWheelChangedEvent, wheel, true);
        return new InputRootLease(() =>
        {
            root.GotFocus -= focused; root.GettingFocus -= OnGettingFocus; root.PreviewKeyDown -= key; root.PreviewKeyDown -= DirectionalKeyDown;
            root.RemoveHandler(PointerPressedEvent, pressed); root.RemoveHandler(PointerWheelChangedEvent, wheel);
        });
    }
    private sealed class InputRootLease(Action release) : IDisposable
    {
        private Action? release = release;
        public void Dispose() => Interlocked.Exchange(ref release, null)?.Invoke();
    }
}
