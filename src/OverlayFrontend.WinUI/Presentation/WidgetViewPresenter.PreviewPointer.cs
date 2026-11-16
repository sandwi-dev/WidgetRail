using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using Windows.Foundation;

namespace WidgetRail.OverlayFrontend.WinUI.Presentation;
internal sealed partial class WidgetViewPresenter
{
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
