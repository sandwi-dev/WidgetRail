using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using WidgetRail.OverlayFrontend.WinUI.Collections;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.OverlayFrontend.WinUI.Presentation;

internal sealed partial class WidgetViewPresenter
{
    private sealed record PreviewSource(Binding Owner, Binding Source, FrameworkElement Anchor,
        WidgetIndexedRow? Row, ViewNode Content, string Scope, FocusPreviewOptions Options)
    {
        internal object Key => (Owner.Token, Source.Token, Anchor, Row is null ? null : RowIdentity(Row),
            Row?.Lease.LeaseId, Options);
    }
    private readonly FocusPreviewDelayState previewDelay = new();
    private PreviewSource? previewSource;
    private DependencyObject? previewPointerTarget;
    private object? previewPointerFocus;
    private bool previewPointerIntent;
    private bool previewEventsAttached;
    private DispatcherQueueTimer? previewTimer;
    private PresentationPortal? previewPortal;
    private Grid? previewLayer;
    private Border? previewChrome;
    private IDisposable? previewTheme;
    private WidgetPresentationSurface? previewSurface;
    private long? previewFontSizeSubscription;
    private object previewOriginalFontSize = DependencyProperty.UnsetValue;
    private readonly List<ScrollViewer> previewScrollers = [];
    private readonly Dictionary<WidgetPresentationSurface, Task> previewSuspensions = [];
    private ViewNode? previewPaintedContent;
    private WidgetPresentationBinding? previewPaintedPresentation;
    private AnchorRectangle? previewBounds;
    internal bool IsFocusPreviewOpen => previewPortal?.Visibility == Visibility.Visible;
    internal FrameworkElement? FocusPreviewChrome => previewChrome;
    internal object FocusPreviewDiagnostics() => new
    {
        source = previewSource?.Source.Identity.Id, current = CurrentPreviewSource()?.Source.Identity.Id,
        pointerIntent = previewPointerIntent, pointer = FindBinding(previewPointerTarget)?.Identity.Id,
        ready = previewDelay.Ready(Environment.TickCount64), remaining = previewDelay.Remaining(Environment.TickCount64),
        timer = previewTimer?.IsRunning, open = IsFocusPreviewOpen,
        surface = previewSurface is not null, suspensions = previewSuspensions.Count,
        fragmentRoot = previewPaintedContent?.Id,
        pointerSource = FindBinding(previewPointerTarget) is { } candidate ? new
        {
            candidate.Identity, navigable = Navigable(candidate),
            content = declarations[candidate.Identity.Id].Node.FocusPresentation?.Id,
            anchor = VisibleAnchorBounds(candidate.LayoutElement, this),
            width = candidate.LayoutElement.ActualWidth, height = candidate.LayoutElement.ActualHeight,
            visible = candidate.LayoutElement.Visibility.ToString(),
        } : null,
    };
    private bool IsFocusPreviewSurface(WidgetPresentationSurface surface) => ReferenceEquals(previewSurface, surface);

    private void EnsurePreviewEvents()
    {
        if (previewEventsAttached || presentationOnly || !declarations.Values.Any(item => item.Node.FocusPreview is not null)) return;
        previewEventsAttached = true;
        PointerMoved += PreviewPointerMoved;
        PointerExited += PreviewPointerExited;
        GotFocus += PreviewGotFocus;
        LostFocus += PreviewLostFocus;
        Unloaded += PreviewUnloaded;
        PreviewKeyDown += PreviewKeyPressed;
        LayoutUpdated += PreviewSourceLayoutReady;
    }
    private void PreviewSourceLayoutReady(object? sender, object args)
    {
        if (disposed || applying || !presentationActive || !presentationInputEnabled) return;
        // Indexed focus can arrive before its container has visible geometry.
        // Retry only a semantic source change when native layout settles.
        var current = CurrentPreviewSource();
        if (!Equals(current?.Key, previewSource?.Key)) QueueSurfaceUpdate();
    }
    private void DisposePreviewEvents()
    {
        ClearFocusPreview();
        previewTimer?.Stop();
        if (previewTimer is not null) previewTimer.Tick -= PreviewTimerElapsed;
        previewTimer = null; previewPointerTarget = null; previewPointerFocus = null;
        if (!previewEventsAttached) return;
        previewEventsAttached = false;
        PointerMoved -= PreviewPointerMoved; PointerExited -= PreviewPointerExited;
        GotFocus -= PreviewGotFocus; LostFocus -= PreviewLostFocus;
        Unloaded -= PreviewUnloaded; PreviewKeyDown -= PreviewKeyPressed;
        LayoutUpdated -= PreviewSourceLayoutReady;
    }
    private void PreviewPointerMoved(object sender, PointerRoutedEventArgs args)
    {
        // Touch contact has no persistent hover. Neither hover nor preview selects a list row.
        if (args.Pointer.PointerDeviceType == Microsoft.UI.Input.PointerDeviceType.Touch) return;
        SetPreviewPointerTarget(args.OriginalSource as DependencyObject);
    }
    internal void SetPreviewPointerTarget(DependencyObject? target)
    {
        if (previewPointerIntent && ReferenceEquals(previewPointerTarget, target)) return;
        previewPointerIntent = true; previewPointerTarget = target;
        previewPointerFocus = XamlRoot is null ? null : FocusManager.GetFocusedElement(XamlRoot);
        QueueSurfaceUpdate();
    }
    private void PreviewPointerExited(object sender, PointerRoutedEventArgs args)
    { previewPointerIntent = true; previewPointerTarget = null; ClearFocusPreview(); }
    private void PreviewGotFocus(object sender, RoutedEventArgs args)
    {
        // Routed GotFocus can trail an already-completed native focus change.
        // Do not let that older event replace a newer pointer hover choice.
        if (!previewPointerIntent || XamlRoot is null || !ReferenceEquals(previewPointerFocus, FocusManager.GetFocusedElement(XamlRoot)))
            UseFocusedPreviewSource();
        QueueSurfaceUpdate();
    }
    private void PreviewLostFocus(object sender, RoutedEventArgs args) => QueueSurfaceUpdate();
    private void PreviewKeyPressed(object sender, KeyRoutedEventArgs args) => UseFocusedPreviewSource();
    private void UseFocusedPreviewSource()
    {
        if (!previewPointerIntent) return;
        previewPointerIntent = false; previewPointerTarget = null; previewPointerFocus = null; QueueSurfaceUpdate();
    }
    private void PreviewUnloaded(object sender, RoutedEventArgs args)
    { previewPointerTarget = null; previewPointerFocus = null; ClearFocusPreview(); }

    private PreviewSource? CurrentPreviewSource()
    {
        if (disposed || applying || !presentationActive || !presentationInputEnabled || presentationOnly ||
            !IsLoaded || presentation?.IsCurrent != true || HasTransientControl || activeTransition is not null ||
            surfaceResize?.Playback is { IsCompleted: false } || effectiveView?.Root.Kind == ViewNodeKind.ModalLayer)
            return null;
        var target = previewPointerIntent ? previewPointerTarget : XamlRoot is null ? null : FocusManager.GetFocusedElement(XamlRoot) as DependencyObject;
        var source = FindBinding(target);
        if (source is null || source.Identity.Scope != activeScope || !Navigable(source)) return null;
        WidgetIndexedRow? row = null;
        var anchor = source.LayoutElement;
        if (source.Element is WidgetIndexedCollectionView indexed)
        {
            var occurrence = indexed.CapturePreviewRow(target);
            if (occurrence is null) return null;
            row = occurrence.Value.Row; anchor = occurrence.Value.Anchor;
        }
        var content = row?.Item.Root.FocusPresentation ?? declarations[source.Identity.Id].Node.FocusPresentation;
        if (content is null) return null;
        for (var declaration = declarations.GetValueOrDefault(source.Identity.Id); declaration is not null;
             declaration = declaration.ParentId is { } parent ? declarations.GetValueOrDefault(parent) : null)
        {
            if (declaration.Node.Kind != ViewNodeKind.FocusPresentationSurface) continue;
            // Nearest presentation owner is authoritative even when it uses inline layout.
            if (declaration.Node.FocusPreview is not { } options ||
                previewPointerIntent && !options.OnHover || !previewPointerIntent && !options.OnFocus ||
                !bindings.TryGetValue(declaration.Node.Id, out var owner) ||
                VisibleAnchorBounds(anchor, this) is not { Valid: true }) return null;
            return new(owner, source, anchor, row, content, row?.Lease.Range.ScopeId ?? source.Identity.Scope, options);
        }
        return null;
    }

    private void UpdateFocusPreview()
    {
        EnsurePreviewEvents();
        var next = CurrentPreviewSource();
        if (next is null) { ClearFocusPreview(); return; }
        var changed = previewDelay.Select(next.Key, next.Options.DelayMilliseconds, Environment.TickCount64);
        previewSource = next;
        if (changed)
        {
            previewTimer?.Stop();
            HideFocusPreview();
            WatchPreviewScrollers(next.Anchor);
        }
        if (!previewDelay.Ready(Environment.TickCount64))
        {
            previewTimer ??= DispatcherQueue.CreateTimer();
            previewTimer.Tick -= PreviewTimerElapsed;
            previewTimer.Tick += PreviewTimerElapsed;
            previewTimer.IsRepeating = false;
            if (!previewTimer.IsRunning)
            {
                previewTimer.Interval = TimeSpan.FromMilliseconds(Math.Max(1, previewDelay.Remaining(Environment.TickCount64)));
                previewTimer.Start();
            }
            return;
        }
        previewTimer?.Stop();
        ShowFocusPreview(next);
    }
    private void PreviewTimerElapsed(DispatcherQueueTimer sender, object args)
    {
        sender.Stop();
        try { UpdateFocusPreview(); }
        catch (Exception error) { ClearFocusPreview(); ReportFailure(error); }
    }
    private void WatchPreviewScrollers(FrameworkElement anchor)
    {
        foreach (var scroll in previewScrollers) scroll.ViewChanged -= PreviewScrolled;
        previewScrollers.Clear();
        for (DependencyObject? node = anchor; node is not null && !ReferenceEquals(node, this); node = VisualTreeHelper.GetParent(node))
            if (node is ScrollViewer scroll) { scroll.ViewChanged += PreviewScrolled; previewScrollers.Add(scroll); }
    }
    private void PreviewScrolled(object? sender, ScrollViewerViewChangedEventArgs args)
    {
        // Offset events, not a render loop: a recycled or hidden occurrence is revoked immediately.
        if (CurrentPreviewSource() is not { } current || previewSource?.Key.Equals(current.Key) != true) ClearFocusPreview();
        else PositionFocusPreview();
    }
    private void PreviewLayoutChanged(object? sender, object args)
    {
        if (previewSource is null || applying) return;
        if (CurrentPreviewSource() is not { } current || !previewSource.Key.Equals(current.Key)) { ClearFocusPreview(); return; }
        PositionFocusPreview();
    }
    private void ShowFocusPreview(PreviewSource source)
    {
        if (source.Owner.Element is not WidgetPresentationSurface { Fragment: { } fragment } surface ||
            PresentationLayers.Find(this) is not { } layers) return;
        if (previewSuspensions.TryGetValue(surface, out var pending))
        {
            if (!pending.IsCompleted) return;
            previewSuspensions.Remove(surface);
            if (!pending.IsCompletedSuccessfully) { ClearFocusPreview(); ReportFailure(pending.Exception!); return; }
        }
        if (!ReferenceEquals(previewSurface, surface))
        {
            HideFocusPreview();
            previewSurface = surface;
            surface.ConfigurePreview(true);
            previewChrome = new Border { Child = fragment, IsHitTestVisible = false,
                HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
            previewLayer = new Grid { IsHitTestVisible = false };
            previewLayer.Children.Add(previewChrome);
            previewPortal = new(layers, this, previewLayer, PresentationLayer.ContentPreview, passive: true, followComposition: false);
            // NativeTextScaleScope follows the portal's logical owner for authored
            // font sizes. WinUI property inheritance follows its paint parent instead.
            // Forward the already-scaled default once at the fragment root; explicit
            // child sizes still use their normal scoped style and do not multiply twice.
            previewOriginalFontSize = fragment.ReadLocalValue(FontSizeProperty);
            fragment.FontSize = FontSize;
            previewFontSizeSubscription = RegisterPropertyChangedCallback(FontSizeProperty,
                (_, _) => fragment.FontSize = FontSize);
            previewTheme = NativePopupTheme.Preview(previewChrome, this, presentation?.RenderStyles, appearance);
            LayoutUpdated += PreviewLayoutChanged;
        }
        if (ReferenceEquals(previewPaintedContent, source.Content) && ReferenceEquals(previewPaintedPresentation, presentation))
        { PositionFocusPreview(); return; }
        previewPaintedContent = source.Content; previewPaintedPresentation = presentation;
        var row = source.Row;
        fragment.Session = Session;
        fragment.ResolveArtworkAsync = row is null ? ResolveArtworkAsync : async (handle, token) =>
        {
            try { return await row.Owner.ResolveArtworkAsync(row, handle, token); }
            catch (WidgetRail.WidgetPresentationSession.WidgetPresentationSessionException) when (!row.Lease.IsCurrent) { return null; }
        };
        fragment.ArtworkGeneration = row is null ? ArtworkGeneration : row.Lease.LeaseId + ":" + row.Item.Key.Length + ":" + row.Item.Key;
        fragment.ArtworkAuthorityCurrent = row is null ? ArtworkAuthorityCurrent : () => row.Lease.IsCurrent && previewSource?.Key.Equals(source.Key) == true;
        fragment.Failed = ReportFailure;
        _ = fragment.SetPresentationActiveAsync(true);
        fragment.ApplyFragment(row is null ? presentation! : presentation!.WithStyles(row.Lease.RenderStyles), source.Content, source.Scope);
        PositionFocusPreview();
    }
    private void PositionFocusPreview()
    {
        if (previewSource is not { } source || previewChrome is null || previewLayer is null) return;
        if (VisibleAnchorBounds(source.Anchor, this) is not { Valid: true } anchor) { ClearFocusPreview(); return; }
        var bounds = AnchoredPresentationGeometry.Place(ActualWidth, ActualHeight, anchor, source.Options.Width, source.Options.Height, source.Options.Placement);
        if (previewBounds == bounds) return;
        previewBounds = bounds;
        previewChrome.Width = bounds.Width; previewChrome.Height = bounds.Height;
        previewChrome.Margin = new(bounds.X, bounds.Y, 0, 0);
        previewChrome.Clip = new RectangleGeometry { Rect = new(0, 0, bounds.Width, bounds.Height) };
    }
    private void HideFocusPreview()
    {
        LayoutUpdated -= PreviewLayoutChanged;
        if (previewFontSizeSubscription is { } subscription)
            UnregisterPropertyChangedCallback(FontSizeProperty, subscription);
        previewFontSizeSubscription = null;
        previewPortal?.Dispose(); previewPortal = null;
        previewTheme?.Dispose(); previewTheme = null;
        if (previewChrome is not null) previewChrome.Child = null;
        previewChrome = null; previewLayer = null;
        if (previewSurface is { Fragment: { } fragment } surface)
        {
            if (ReferenceEquals(previewOriginalFontSize, DependencyProperty.UnsetValue)) fragment.ClearValue(FontSizeProperty);
            else fragment.SetValue(FontSizeProperty, previewOriginalFontSize);
            var suspended = fragment.SetPresentationActiveAsync(false);
            if (!suspended.IsCompleted)
            {
                previewSuspensions[surface] = suspended;
                _ = ResumePreviewAfterSuspensionAsync(surface, suspended);
            }
        }
        previewOriginalFontSize = DependencyProperty.UnsetValue;
        previewSurface = null; previewBounds = null; previewPaintedContent = null; previewPaintedPresentation = null;
    }
    private async Task ResumePreviewAfterSuspensionAsync(WidgetPresentationSurface surface, Task task)
    {
        try { await task; }
        catch (Exception error) { if (!disposed) ReportFailure(error); }
        finally
        {
            if (previewSuspensions.GetValueOrDefault(surface) == task) previewSuspensions.Remove(surface);
            if (!disposed) QueueSurfaceUpdate();
        }
    }
    private void ClearFocusPreview()
    {
        previewTimer?.Stop(); previewDelay.Clear(); previewSource = null;
        foreach (var scroll in previewScrollers) scroll.ViewChanged -= PreviewScrolled;
        previewScrollers.Clear(); HideFocusPreview();
    }
    private async Task RefreshPreviewAfterMotionAsync(Task motion)
    {
        try { await motion; }
        catch (Exception error) { if (!disposed) ReportFailure(error); }
        if (!disposed) QueueSurfaceUpdate();
    }
}
