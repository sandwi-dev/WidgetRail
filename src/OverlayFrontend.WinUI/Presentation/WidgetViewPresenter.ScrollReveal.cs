using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WidgetRail.WidgetProtocol;
using Windows.Foundation;

namespace WidgetRail.OverlayFrontend.WinUI.Presentation;

internal sealed partial class WidgetViewPresenter
{
    private sealed record PendingScrollReveal(ScrollRevealRequest Request, Binding Scroll, Binding Target);
    private PendingScrollReveal? pendingScrollReveal;
    private long lastScrollRevealRequest;

    private void UpdateScrollReveal(ViewSnapshot snapshot)
    {
        if (snapshot.ScrollRevealRequest is not { } request || presentationOnly || !presentationActive ||
            !bindings.TryGetValue(request.ScrollId, out var scroll) || scroll.Element is not ScrollViewer ||
            !bindings.TryGetValue(request.TargetId, out var target) || scroll.Identity.Scope != activeScope ||
            target.Identity.Scope != activeScope || !IsDescendant(request.TargetId, request.ScrollId))
        { CancelScrollReveal(); return; }
        if (pendingScrollReveal is { } old && (old.Request != request || !ReferenceEquals(old.Scroll, scroll) || !ReferenceEquals(old.Target, target)))
            CancelScrollReveal();
        if (request.RequestId <= lastScrollRevealRequest) return;
        CancelScrollReveal();
        lastScrollRevealRequest = request.RequestId;
        var pending = new PendingScrollReveal(request, scroll, target);
        pendingScrollReveal = pending;
        LayoutUpdated += ScrollRevealLayoutUpdated;
        // Focus restoration runs first. Reveal scrolls only its own viewport and never moves focus.
        DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
        {
            if (ReferenceEquals(pendingScrollReveal, pending)) ApplyScrollReveal();
        });
    }

    private void ScrollRevealLayoutUpdated(object? sender, object args) => ApplyScrollReveal();

    private void ApplyScrollReveal()
    {
        if (pendingScrollReveal is not { } pending || applying) return;
        if (disposed || !presentationActive || presentationOnly || HasTransientControl ||
            !bindings.TryGetValue(pending.Request.ScrollId, out var scroll) || !ReferenceEquals(scroll, pending.Scroll) ||
            !bindings.TryGetValue(pending.Request.TargetId, out var target) || !ReferenceEquals(target, pending.Target) ||
            scroll.Identity.Scope != activeScope || target.Identity.Scope != activeScope)
        { CancelScrollReveal(); return; }
        var viewport = (ScrollViewer)scroll.Element;
        var element = target.LayoutElement;
        if (element.Visibility != Visibility.Visible) { CancelScrollReveal(); return; }
        // Lightweight TextBlocks can have valid native layout while IsLoaded
        // remains false. Require attachment to this viewport's live XamlRoot
        // instead; binding/scope checks above retain snapshot ownership.
        if (!IsLoaded || !viewport.IsLoaded || element.XamlRoot is null ||
            !ReferenceEquals(element.XamlRoot, viewport.XamlRoot) || viewport.ViewportHeight <= 0 ||
            element.ActualWidth <= 0 || element.ActualHeight <= 0) return;
        if (NearestScroll(element) != viewport)
        { CancelScrollReveal(); return; }
        var bounds = element.TransformToVisual(viewport).TransformBounds(new Rect(0, 0, element.ActualWidth, element.ActualHeight));
        var horizontal = declarations[scroll.Identity.Id].Node.ScrollAxis == ScrollAxis.Horizontal;
        var offset = horizontal ? viewport.HorizontalOffset + bounds.X : viewport.VerticalOffset + bounds.Y;
        var limit = horizontal ? viewport.ScrollableWidth : viewport.ScrollableHeight;
        CancelScrollReveal(); // Consume before ChangeView can trigger layout again.
        viewport.ChangeView(horizontal ? Math.Clamp(offset, 0, limit) : null,
            horizontal ? null : Math.Clamp(offset, 0, limit), null,
            disableAnimation: Motion.WidgetMotionOptions.From(appearance, systemAnimationsEnabled).Reduced);
    }

    private void CancelScrollReveal()
    {
        pendingScrollReveal = null;
        LayoutUpdated -= ScrollRevealLayoutUpdated;
    }
}
