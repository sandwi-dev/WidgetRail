using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;

namespace WidgetRail.OverlayFrontend.WinUI.Presentation;

internal sealed partial class WidgetViewPresenter
{
    // The same policy applies to the ordinary tree and root-level overlay portals.
    internal IDisposable AttachPresentationInput(UIElement root)
    {
        RoutedEventHandler focused = (_, _) => { RememberFocus(); RefreshScrollFocusOutline(); NotifyControllerGuideChanged(); };
        PointerEventHandler pressed = (_, _) => { CancelPassiveScrollFocus(); CancelGroupEntry(); CancelScrollReveal(); scrollGesture=null;directionalScroll=null;RefreshScrollFocusOutline(); };
        PointerEventHandler wheel = (_, _) =>
        {
            CancelPassiveScrollFocus();
            CancelScrollReveal();
            // Wheel input now owns the viewport. Retire controller settlement
            // without cancelling the native wheel motion already admitted.
            if (scrollGesture is { } gesture) gesture.Animated = false;
            scrollGesture = null;
        };
        KeyEventHandler key = (_, args) =>
        {
            if (Input.GamepadKeyBoundary.Owns(this, args)) return;
            if (args.Key is Windows.System.VirtualKey.Up or Windows.System.VirtualKey.Down or
                Windows.System.VirtualKey.Left or Windows.System.VirtualKey.Right &&
                FindIndexedCollection() is { IsFocusParked: true, IsEntryPending: true }) return;
            if (args.Key is Windows.System.VirtualKey.Up or Windows.System.VirtualKey.Down or Windows.System.VirtualKey.Left or
                Windows.System.VirtualKey.Right or Windows.System.VirtualKey.Tab or Windows.System.VirtualKey.Home or
                Windows.System.VirtualKey.End or Windows.System.VirtualKey.PageUp or Windows.System.VirtualKey.PageDown or
                Windows.System.VirtualKey.Enter or Windows.System.VirtualKey.Space or Windows.System.VirtualKey.Escape)
            { CancelPassiveScrollFocus(); scrollGesture?.Stop(); CancelGroupEntry(); CancelScrollReveal(); }
        };
        root.ContextRequested += NativeContextRequested;
        root.PreviewKeyDown += NativeContextKeyDown;
        root.GotFocus += focused;
        root.GettingFocus += OnGettingFocus;
        // Cancel the previous intent before this key starts a new one. Doing
        // this on bubbling KeyDown cancels the collection entry just queued by
        // DirectionalKeyDown for the very same arrow press.
        root.PreviewKeyDown += key;
        root.PreviewKeyDown += DirectionalKeyDown;
        // Bubble after native child controls have had their own Escape handling.
        root.KeyDown += ModalKeyDown;
        root.AddHandler(PointerPressedEvent, pressed, true);
        root.AddHandler(PointerWheelChangedEvent, wheel, true);
        // Dialog paint portals live outside this presenter's visual subtree.
        // Forward their preview events through the same owner and scope checks.
        var portal = !ReferenceEquals(root, this);
        PointerEventHandler previewMoved = (sender, args) => { if (previewEventsAttached) PreviewPointerMoved(sender, args); };
        PointerEventHandler previewExited = (sender, args) => { if (previewEventsAttached) PreviewPointerExited(sender, args); };
        RoutedEventHandler previewFocused = (sender, args) => { if (previewEventsAttached) PreviewGotFocus(sender, args); };
        RoutedEventHandler previewLost = (sender, args) => { if (previewEventsAttached) PreviewLostFocus(sender, args); };
        KeyEventHandler previewKey = (sender, args) => { if (previewEventsAttached) PreviewKeyPressed(sender, args); };
        if (portal)
        {
            root.PointerMoved += previewMoved; root.PointerExited += previewExited;
            root.GotFocus += previewFocused; root.LostFocus += previewLost; root.PreviewKeyDown += previewKey;
        }
        return new InputRootLease(() =>
        {
            root.ContextRequested -= NativeContextRequested; root.PreviewKeyDown -= NativeContextKeyDown;
            root.GotFocus -= focused; root.GettingFocus -= OnGettingFocus; root.PreviewKeyDown -= key; root.PreviewKeyDown -= DirectionalKeyDown;
            root.KeyDown -= ModalKeyDown;
            root.RemoveHandler(PointerPressedEvent, pressed); root.RemoveHandler(PointerWheelChangedEvent, wheel);
            if (portal)
            {
                root.PointerMoved -= previewMoved; root.PointerExited -= previewExited;
                root.GotFocus -= previewFocused; root.LostFocus -= previewLost; root.PreviewKeyDown -= previewKey;
            }
        });
    }
    private sealed class InputRootLease(Action release) : IDisposable
    {
        private Action? release = release;
        public void Dispose() => Interlocked.Exchange(ref release, null)?.Invoke();
    }
}
