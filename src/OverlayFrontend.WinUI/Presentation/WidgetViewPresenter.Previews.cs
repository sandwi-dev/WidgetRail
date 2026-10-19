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
    private readonly ContentSizedPresentationState previewHeight = new();
    private bool previewPointerOverChrome;
    private long previewExitRevision;
    private Grid? previewContentPanel;
    private FontIcon? previewEnterGlyph;
    private TextBlock? previewEnterLabel;
    private bool previewEntering;
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
        if (previewPointerIntent && ReferenceEquals(previewPointerTarget, target)) { previewExitRevision++; return; }
        if (PreviewEnterShortcut(previewSource) is not null && FindBinding(target) is null)
        { _ = DismissPreviewAfterPointerExitAsync(++previewExitRevision); return; }
        previewExitRevision++;
        previewPointerIntent = true; previewPointerTarget = target;
        previewPointerFocus = XamlRoot is null ? null : FocusManager.GetFocusedElement(XamlRoot);
        QueueSurfaceUpdate();
    }
    private void PreviewPointerExited(object sender, PointerRoutedEventArgs args)
    {
        previewPointerIntent = true;
        if (PreviewEnterShortcut(previewSource) is not null) { _ = DismissPreviewAfterPointerExitAsync(++previewExitRevision); return; }
        previewPointerTarget = null; ClearFocusPreview();
    }
    private async Task DismissPreviewAfterPointerExitAsync(long revision)
    {
        await Task.Delay(150);
        if (!disposed && revision == previewExitRevision && !previewPointerOverChrome)
        { previewPointerTarget = null; ClearFocusPreview(); }
    }
    private ControllerShortcut? PreviewEnterShortcut(PreviewSource? source)
    {
        if (source?.Options.EnterButton is not { } button) return null;
        var node = source.Row?.Item.Root ?? declarations.GetValueOrDefault(source.Source.Identity.Id)?.Node;
        if (node is null || node.IsDisabled == true || node.IsBusy == true || node.ContextMenuButton == button && node.ContextActions.Count > 0) return null;
        return node.Shortcuts.FirstOrDefault(shortcut => shortcut.Button == button && shortcut.Phase == ControllerEventPhase.Pressed);
    }
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
        if (previewPointerIntent && previewPointerOverChrome && previewSource is { } retained && PreviewEnterShortcut(retained) is not null &&
            bindings.GetValueOrDefault(retained.Source.Identity.Id)?.Token == retained.Source.Token &&
            (retained.Row is null || retained.Row.Lease.IsCurrent) && VisibleAnchorBounds(retained.Anchor, this) is { Valid: true }) return retained;
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
            previewContentPanel = new Grid();
            previewContentPanel.RowDefinitions.Add(new() { Height = new(1, GridUnitType.Star) });
            previewContentPanel.RowDefinitions.Add(new() { Height = GridLength.Auto });
            previewContentPanel.Children.Add(fragment);
            var enterHint = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new(0, 8, 0, 0) };
            previewEnterGlyph = new FontIcon { FontSize = 22 };
            previewEnterLabel = new TextBlock { FontSize = 14, VerticalAlignment = VerticalAlignment.Center };
            enterHint.Children.Add(previewEnterGlyph); enterHint.Children.Add(previewEnterLabel);
            Grid.SetRow(enterHint, 1); previewContentPanel.Children.Add(enterHint);
            previewChrome = new Border { Child = previewContentPanel, IsHitTestVisible = PreviewEnterShortcut(source) is not null,
                HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
            previewChrome.PointerEntered += (_, _) => { previewExitRevision++; previewPointerIntent = true; previewPointerOverChrome = true; };
            previewChrome.PointerExited += (_, _) => { previewPointerOverChrome = false; _ = DismissPreviewAfterPointerExitAsync(++previewExitRevision); };
            previewChrome.PointerPressed += (_, args) => { if (PreviewEnterShortcut(previewSource) is not null) { args.Handled = true; _ = EnterPreviewFromPointerAsync(); } };
            previewLayer = new Grid { IsHitTestVisible = true };
            previewLayer.Children.Add(previewChrome);
            previewPortal = new(layers, this, previewLayer, PresentationLayer.ContentPreview, passive: PreviewEnterShortcut(source) is null, followComposition: false);
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
        UpdatePreviewEnterHint(source);
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
    private void UpdatePreviewEnterHint(PreviewSource source)
    {
        if (previewEnterGlyph is null || previewEnterLabel is null) return;
        var shortcut = PreviewEnterShortcut(source);
        if (previewChrome is not null) previewChrome.IsHitTestVisible = shortcut is not null;
        ((FrameworkElement)VisualTreeHelper.GetParent(previewEnterGlyph)).Visibility = shortcut is null ? Visibility.Collapsed : Visibility.Visible;
        if (shortcut is null) return;
        previewEnterGlyph.FontSize = 22 * appearance.TextScale;
        previewEnterLabel.FontSize = 14 * appearance.TextScale;
        WidgetGlyphs.Apply(previewEnterGlyph, new() { Id = "preview-enter", Kind = ViewNodeKind.ControllerGlyph,
            ControllerPrompt = shortcut.Button switch { ControllerButton.X => ControllerPrompt.X, ControllerButton.Y => ControllerPrompt.Y,
                ControllerButton.LeftStick => ControllerPrompt.LeftStickPress, _ => ControllerPrompt.RightStickPress } }, WidgetControllerPrompts.PlayStation);
        previewEnterLabel.Text = shortcut.Label ?? "Interact";
        var palette = Shell.ShellChromePalette.Resolve(presentation?.RenderStyles, appearance);
        previewEnterGlyph.Foreground = previewEnterLabel.Foreground = new SolidColorBrush(palette.Muted);
    }
    internal async Task EnterPreviewFromPointerAsync()
    {
        if (previewEntering || previewSource is not { } source || PreviewEnterShortcut(source) is not { } shortcut ||
            CurrentPreviewSource()?.Key.Equals(source.Key) != true) return;
        if (source.Anchor is Control control && control.Focus(FocusState.Pointer)) await EnterFocusPreviewAsync(source, shortcut);
    }
    private async Task<bool> TryEnterFocusPreviewAsync(ControllerButton button, ControllerEventPhase phase)
    {
        if (CurrentPreviewSource() is not { } source || PreviewEnterShortcut(source) is not { } shortcut || shortcut.Button != button) return false;
        if (phase == ControllerEventPhase.Pressed) await EnterFocusPreviewAsync(source, shortcut);
        return true;
    }
    private async Task EnterFocusPreviewAsync(PreviewSource source, ControllerShortcut shortcut)
    {
        if (previewEntering || presentation is not { } displayed || CurrentPreviewSource()?.Key.Equals(source.Key) != true) return;
        previewEntering = true;
        try
        {
            if (source.Row is { } row && source.Source.Element is WidgetIndexedCollectionView indexed)
                await indexed.InvokePreviewShortcutAsync(row, shortcut.Button);
            else if (IsBindingCurrent(displayed))
                await DispatchCapturedActionAsync(displayed, new(shortcut.ActionId, source.Source.Identity.Id, shortcut.Button,
                    Sequence: ++actionSequence, MonotonicTimestampMicroseconds: Environment.TickCount64 * 1000,
                    InputScopeId: displayed.Scope) { FocusedElementId = source.Source.Identity.Id });
        }
        catch (WidgetRail.WidgetPresentationSession.WidgetPresentationSessionException error) when (IsRetiredInput(error)) { Session?.RequestInputRefresh(displayed.Frame); }
        catch (OperationCanceledException) when (disposed) { }
        catch (Exception error) { ReportFailure(error, IsBindingCurrent(displayed)); }
        finally { previewEntering = false; }
    }
    private void PositionFocusPreview()
    {
        if (previewSource is not { } source || previewChrome is null || previewLayer is null) return;
        if (VisibleAnchorBounds(source.Anchor, this) is not { Valid: true } anchor) { ClearFocusPreview(); return; }
        var width = Math.Max(0, Math.Min(source.Options.Width, ActualWidth - 32));
        var maximumHeight = Math.Max(0, Math.Min(source.Options.Height, ActualHeight - 32));
        previewChrome.Width = width;
        var height = source.Options.SizeToContentHeight
            ? previewHeight.Resolve(width, 96, maximumHeight, appearance.TextScale,
                () => ContentSizedPresentationMeasure.Height(previewChrome, width, maximumHeight))
            : maximumHeight;
        var bounds = AnchoredPresentationGeometry.Place(ActualWidth, ActualHeight, anchor, width, height, source.Options.Placement);
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
        previewContentPanel?.Children.Clear(); previewContentPanel = null; previewEnterGlyph = null; previewEnterLabel = null;
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
        previewHeight.Reset();
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
        previewExitRevision++; previewPointerOverChrome = false;
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
