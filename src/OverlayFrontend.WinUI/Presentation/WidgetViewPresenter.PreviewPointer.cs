using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using Windows.Foundation;

namespace WidgetRail.OverlayFrontend.WinUI.Presentation;
internal sealed partial class WidgetViewPresenter
{
    private IDisposable? previewOutsidePressInput;

    private void ObservePreviewOutsidePresses()
    {
        previewOutsidePressInput?.Dispose();
        previewOutsidePressInput = null;
        if (XamlRoot?.Content is not UIElement root) return;
        PointerEventHandler pressed = (_, args) =>
        {
            if (!IsFocusPreviewOpen || previewSource is not { } source) return;
            // Its expanded native player already owns the complete fullscreen
            // input boundary, including portaled chrome and its collapse click.
            if (previewSurface?.Fragment?.FocusedMediaPlayer is { IsExpanded: true } or { IsMovingPresentation: true }) return;
            for (var target = args.OriginalSource as DependencyObject; target is not null;
                target = PresentationLayers.LogicalParent(target))
                if (ReferenceEquals(target, previewChrome) || ReferenceEquals(target, source.Anchor)) return;

            // Observe the existing routed press, including handled presses. Do not
            // consume/replay it, hit-test another target, or return focus: native
            // controls and the shell retain their ordinary click/pin policy.
            ExitFocusPreview(restoreFocus: false);
            previewPointerIntent = true;
            previewPointerTarget = null;
            previewPointerFocus = XamlRoot is null ? null : FocusManager.GetFocusedElement(XamlRoot);
            ClearFocusPreview();
        };
        PointerEventHandler moved = (_, args) =>
        {
            if (!IsFocusPreviewOpen || previewEntered ||
                args.Pointer.PointerDeviceType == Microsoft.UI.Input.PointerDeviceType.Touch ||
                Input.OverlayMouseSession.SuppressesHover(XamlRoot) ||
                previewSurface?.Fragment?.FocusedMediaPlayer is { IsExpanded: true } or { IsMovingPresentation: true }) return;
            var target = args.OriginalSource as DependencyObject;
            if (IsWithin(target, this) || previewChrome is { } chrome && IsWithin(target, chrome) ||
                FindBinding(target) is not null) return;
            // An overlapping sibling can receive movement without a presenter
            // exit. Feed that real routed position into the existing corridor
            // and bounded exit timer; leave ordinary controls and clicks alone.
            previewPointerOverChrome = false;
            SetPreviewPointerTarget(null, args.GetCurrentPoint(this).Position);
        };
        root.AddHandler(UIElement.PointerPressedEvent, pressed, true);
        root.AddHandler(UIElement.PointerMovedEvent, moved, true);
        previewOutsidePressInput = new InputRootLease(() =>
        {
            root.RemoveHandler(UIElement.PointerPressedEvent, pressed);
            root.RemoveHandler(UIElement.PointerMovedEvent, moved);
        });
    }

    private void PreviewPointerMoved(object sender, PointerRoutedEventArgs args)
    {
        if (args.Pointer.PointerDeviceType == Microsoft.UI.Input.PointerDeviceType.Touch) return;
        SetPreviewPointerTarget(args.OriginalSource as DependencyObject, args.GetCurrentPoint(this).Position);
    }
    internal void SetPreviewPointerTarget(DependencyObject? target, Point? position = null)
    {
        if (Input.OverlayMouseSession.SuppressesHover(XamlRoot)) return;
        previewPointerIntent = true;
        if (position is { } point) previewPointerPosition = (point.X, point.Y);
        if (IsFocusPreviewOpen && previewSource is { } previous && RetainPreviewSource(previous) is { } source)
        {
            if (previewEntered || position is not null && RetainPreviewPointer())
            { CancelPreviewPointerExit(); previewHoverRetained = true; return; }
            // A bound layout element is not necessarily a preview source. Give
            // the pointer the same grace across those gaps as across empty space.
            if (PreviewSourceFromTarget(target) is null)
            { SchedulePreviewPointerExit(); return; }
        }
        CancelPreviewPointerExit(); previewHoverRetained = false;
        previewPointerTarget = target;
        previewPointerFocus = XamlRoot is null ? null : FocusManager.GetFocusedElement(XamlRoot);
        QueueSurfaceUpdate();
    }
    private bool RetainPreviewPointer()
    {
        if (previewPointerPosition is not { } point || previewBounds is not { } bounds || previewSource is not { } source ||
            VisibleAnchorBounds(source.Anchor, this) is not { Valid: true } anchor) return false;
        previewTransit.SetBounds(anchor, bounds);
        return previewTransit.Retain(point);
    }
    private void PreviewPointerExited(object sender, PointerRoutedEventArgs args)
    {
        if (Input.OverlayMouseSession.SuppressesHover(XamlRoot)) return;
        previewPointerIntent = true;
        var point = args.GetCurrentPoint(this).Position;
        previewPointerPosition = (point.X, point.Y);
        if (IsFocusPreviewOpen && RetainPreviewPointer())
        { CancelPreviewPointerExit(); previewHoverRetained = true; return; }
        SchedulePreviewPointerExit();
    }
    private void PreviewChromeEntered(object sender, PointerRoutedEventArgs args)
    {
        if (Input.OverlayMouseSession.SuppressesHover(XamlRoot)) return;
        CancelPreviewPointerExit(); previewPointerIntent = true; previewPointerOverChrome = true; previewHoverRetained = true;
        PreviewChromeMoved(sender, args);
    }
    private void PreviewChromeMoved(object sender, PointerRoutedEventArgs args)
    {
        if (Input.OverlayMouseSession.SuppressesHover(XamlRoot)) return;
        var point = args.GetCurrentPoint(this).Position;
        previewPointerPosition = (point.X, point.Y); RetainPreviewPointer();
    }
    private void PreviewChromeExited(object sender, PointerRoutedEventArgs args)
    {
        if (Input.OverlayMouseSession.SuppressesHover(XamlRoot)) return;
        previewPointerOverChrome = false;
        PreviewChromeMoved(sender, args);
        if (RetainPreviewPointer()) { CancelPreviewPointerExit(); previewHoverRetained = true; }
        else SchedulePreviewPointerExit();
    }
    private void CancelPreviewPointerExit() => previewExitTimer?.Stop();
    private void PreviewPointerExitElapsed(DispatcherQueueTimer sender, object args)
    {
        sender.Stop();
        if (!disposed && previewPointerIntent && !previewPointerOverChrome && !previewEntered &&
            !Input.OverlayMouseSession.SuppressesHover(XamlRoot))
        { previewPointerTarget = null; ClearFocusPreview(); }
    }
    private void SchedulePreviewPointerExit()
    {
        previewHoverRetained = false;
        if (!IsFocusPreviewOpen || previewEntered || previewPointerOverChrome) return;
        if (previewExitTimer is null)
        {
            previewExitTimer = DispatcherQueue.CreateTimer();
            previewExitTimer.IsRepeating = false;
            previewExitTimer.Interval = TimeSpan.FromMilliseconds(200);
            previewExitTimer.Tick += PreviewPointerExitElapsed;
        }
        // One bounded timer, not one task per mouse movement. Continued movement
        // outside the corridor must not postpone dismissal indefinitely.
        if (!previewExitTimer.IsRunning) previewExitTimer.Start();
    }
}
