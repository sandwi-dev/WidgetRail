using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using WidgetRail.OverlayFrontend.WinUI.Collections;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;
using WidgetRail.WidgetPresentationSession;

namespace WidgetRail.OverlayFrontend.WinUI.Presentation;

internal sealed partial class WidgetViewPresenter
{
    private sealed record PreviewSource(Binding Owner, Binding Source, FrameworkElement Anchor,
        WidgetIndexedRow? Row, ViewNode? Content, string Scope, FocusPreviewOptions Options,
        IndexedCollectionDescriptor? Provider=null,string? ContentKey=null,WidgetPresentationIndexedLease? ContentLease=null)
    {
        internal object Key => (Owner.Token, Source.Token, Anchor, Row is null ? null : RowIdentity(Row),
            Row?.Lease.LeaseId, Options, Provider, ContentKey);
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
    private WidgetPreviewFrame? previewFrame;
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
    private bool previewEntered;
    private WidgetViewPresenter? previewOwner;
    private Control? previewReturnTarget;
    internal bool IsFocusPreviewOpen => previewPortal?.Visibility == Visibility.Visible;
    internal FrameworkElement? FocusPreviewChrome => previewChrome;
    internal WidgetPreviewFrame? FocusPreviewFrame => previewFrame;
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
        ClearFocusPreview(); ClearDeferredPreviews();
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
        if (Input.OverlayMouseSession.SuppressesHover(XamlRoot)) return;
        // Touch contact has no persistent hover. Neither hover nor preview selects a list row.
        if (args.Pointer.PointerDeviceType == Microsoft.UI.Input.PointerDeviceType.Touch) return;
        SetPreviewPointerTarget(args.OriginalSource as DependencyObject);
    }
    internal void SetPreviewPointerTarget(DependencyObject? target)
    {
        if (previewPointerIntent && ReferenceEquals(previewPointerTarget, target)) { previewExitRevision++; return; }
        if (PreviewEnterButton(previewSource) is not null && FindBinding(target) is null)
        { _ = DismissPreviewAfterPointerExitAsync(++previewExitRevision); return; }
        previewExitRevision++;
        previewPointerIntent = true; previewPointerTarget = target;
        previewPointerFocus = XamlRoot is null ? null : FocusManager.GetFocusedElement(XamlRoot);
        QueueSurfaceUpdate();
    }
    private void PreviewPointerExited(object sender, PointerRoutedEventArgs args)
    {
        if (Input.OverlayMouseSession.SuppressesHover(XamlRoot)) return;
        previewPointerIntent = true;
        if (PreviewEnterButton(previewSource) is not null) { _ = DismissPreviewAfterPointerExitAsync(++previewExitRevision); return; }
        previewPointerTarget = null; ClearFocusPreview();
    }
    private async Task DismissPreviewAfterPointerExitAsync(long revision)
    {
        await Task.Delay(150);
        if (!disposed && revision == previewExitRevision && !previewPointerOverChrome && !previewEntered)
        { previewPointerTarget = null; ClearFocusPreview(); }
    }
    private ControllerButton? PreviewEnterButton(PreviewSource? source) => source?.Options.EnterButton;
    private bool RetainedPreviewIsCurrent(PreviewSource source) =>
        bindings.GetValueOrDefault(source.Source.Identity.Id)?.Token == source.Source.Token &&
        bindings.GetValueOrDefault(source.Owner.Identity.Id)?.Token == source.Owner.Token &&
        source.Source.Identity.Scope == activeScope &&
        declarations[source.Owner.Identity.Id].Node.FocusPreview == source.Options &&
        (source.Row is not null || declarations[source.Source.Identity.Id].Node.FocusPresentation is not null ||
            declarations[source.Source.Identity.Id].Node.FocusPreviewKey == source.ContentKey && source.ContentKey is not null) &&
        (source.Provider is null || declarations[source.Owner.Identity.Id].Node.FocusPreviewSource == source.Provider) &&
        source.ContentLease?.IsCurrent != false &&
        declarations[source.Source.Identity.Id].Node.IsDisabled != true &&
        (source.Row is null || source.Row.Lease.IsCurrent) &&
        VisibleAnchorBounds(source.Anchor, this) is { Valid: true };

    private void PreviewGotFocus(object sender, RoutedEventArgs args)
    {
        if (previewEntered && previewSurface?.Fragment?.FocusedBinding() is null) ExitFocusPreview(restoreFocus: false);
        // Routed GotFocus can trail an already-completed native focus change.
        // Do not let that older event replace a newer pointer hover choice.
        if (!previewPointerIntent || XamlRoot is null || !ReferenceEquals(previewPointerFocus, FocusManager.GetFocusedElement(XamlRoot)))
            UseFocusedPreviewSource();
        QueueSurfaceUpdate();
    }
    private void PreviewLostFocus(object sender, RoutedEventArgs args) => QueueSurfaceUpdate();
    private void PreviewKeyPressed(object sender, KeyRoutedEventArgs args)
    {
        if (args.Key == Windows.System.VirtualKey.F6 && PreviewEnterButton(CurrentPreviewSource()) is not null)
        { args.Handled = true; EnterFocusPreview(); return; }
        UseFocusedPreviewSource();
    }
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
        var usePointer = previewPointerIntent && !Input.OverlayMouseSession.SuppressesHover(XamlRoot);
        if ((previewEntered || usePointer && previewPointerOverChrome) && previewSource is { } retained &&
            PreviewEnterButton(retained) is not null && RetainedPreviewIsCurrent(retained))
            return retained with { Content = retained.Provider is not null ? retained.Content : retained.Row?.Item.Root.FocusPresentation ?? declarations[retained.Source.Identity.Id].Node.FocusPresentation! };
        var target = usePointer ? previewPointerTarget : XamlRoot is null ? null : FocusManager.GetFocusedElement(XamlRoot) as DependencyObject;
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
        var contentKey = row?.Item.Root.FocusPreviewKey ?? declarations[source.Identity.Id].Node.FocusPreviewKey;
        if (content is null && contentKey is null) return null;
        for (var declaration = declarations.GetValueOrDefault(source.Identity.Id); declaration is not null;
             declaration = declaration.ParentId is { } parent ? declarations.GetValueOrDefault(parent) : null)
        {
            if (declaration.Node.Kind != ViewNodeKind.FocusPresentationSurface) continue;
            // Nearest presentation owner is authoritative even when it uses inline layout.
            if (declaration.Node.FocusPreview is not { } options ||
                usePointer && !options.OnHover || !usePointer && !options.OnFocus ||
                !bindings.TryGetValue(declaration.Node.Id, out var owner) ||
                VisibleAnchorBounds(anchor, this) is not { Valid: true }) return null;
            return new(owner, source, anchor, row, content, row?.Lease.Range.ScopeId ?? source.Identity.Scope, options,
                contentKey is null ? null : declaration.Node.FocusPreviewSource,contentKey);
        }
        return null;
    }

    private void UpdateFocusPreview()
    {
        EnsurePreviewEvents(); PruneDeferredPreviews();
        var next = CurrentPreviewSource();
        if (next is null) { ClearFocusPreview(); return; }
        var changed = previewDelay.Select(next.Key, next.Options.DelayMilliseconds, Environment.TickCount64);
        previewSource = next;
        if (changed)
        {
            previewTimer?.Stop(); CancelDeferredPreviewRead();
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
        if (ResolveDeferredPreview(next) is { } resolved) { previewSource=resolved; ShowFocusPreview(resolved); }
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
        if (source.Content is null) return;
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
            previewFrame = new();
            previewChrome = previewFrame.Surface;
            previewChrome.Child = previewContentPanel;
            previewChrome.IsHitTestVisible = PreviewEnterButton(source) is not null;
            previewChrome.PointerEntered += (_, _) =>
            {
                if (Input.OverlayMouseSession.SuppressesHover(XamlRoot)) return;
                previewExitRevision++; previewPointerIntent = true; previewPointerOverChrome = true;
            };
            previewChrome.PointerExited += (_, _) =>
            {
                if (Input.OverlayMouseSession.SuppressesHover(XamlRoot)) return;
                previewPointerOverChrome = false; _ = DismissPreviewAfterPointerExitAsync(++previewExitRevision);
            };

            previewLayer = new Grid { IsHitTestVisible = true };
            previewLayer.Children.Add(previewFrame);
            previewPortal = new(layers, this, previewLayer, PresentationLayer.ContentPreview, passive: PreviewEnterButton(source) is null, followComposition: false, attachInput: false);
            // NativeTextScaleScope follows the portal's logical owner for authored
            // font sizes. WinUI property inheritance follows its paint parent instead.
            // Forward the already-scaled default once at the fragment root; explicit
            // child sizes still use their normal scoped style and do not multiply twice.
            previewOriginalFontSize = fragment.ReadLocalValue(FontSizeProperty);
            fragment.FontSize = FontSize;
            previewFontSizeSubscription = RegisterPropertyChangedCallback(FontSizeProperty,
                (_, _) => fragment.FontSize = FontSize);
            previewTheme = NativePopupTheme.Preview(previewFrame, this, presentation?.RenderStyles, appearance,
                () => { previewHeight.Reset(); previewBounds = null; });
            LayoutUpdated += PreviewLayoutChanged;
        }
        UpdatePreviewEnterHint(source);
        if (ReferenceEquals(previewPaintedContent, source.Content) && ReferenceEquals(previewPaintedPresentation, presentation))
        { PositionFocusPreview(); return; }
        previewPaintedContent = source.Content; previewPaintedPresentation = presentation;
        var row = source.Row;
        fragment.previewOwner = source.Options.EnterButton is null ? null : this;
        fragment.DispatchActionAsync = DispatchActionAsync;
        fragment.EnsureInteractionAsync = async (authority, token) =>
            PreviewInputCurrent(fragment) && await AdmitInteractionAsync(authority, token) && PreviewInputCurrent(fragment);
        fragment.Session = Session;
        fragment.ResolveArtworkAsync = source.ContentLease is { } previewLease ? (handle,token)=>previewLease.ResolveArtworkAsync(source.ContentKey!,handle,token) : row is null ? ResolveArtworkAsync : async (handle, token) =>
        {
            try { return await row.Owner.ResolveArtworkAsync(row, handle, token); }
            catch (WidgetRail.WidgetPresentationSession.WidgetPresentationSessionException) when (!row.Lease.IsCurrent) { return null; }
        };
        fragment.ArtworkGeneration = source.ContentLease?.LeaseId ?? (row is null ? ArtworkGeneration : row.Lease.LeaseId + ":" + row.Item.Key.Length + ":" + row.Item.Key);
        fragment.ArtworkAuthorityCurrent = source.ContentLease is { } contentLease ? ()=>contentLease.IsCurrent : row is null ? ArtworkAuthorityCurrent : () => row.Lease.IsCurrent && previewSource?.Key.Equals(source.Key) == true;
        fragment.Failed = ReportFailure;
        _ = fragment.SetPresentationActiveAsync(true);
        fragment.ApplyFragment(source.ContentLease is { } content ? presentation!.WithStyles(content.RenderStyles) : row is null ? presentation! : presentation!.WithStyles(row.Lease.RenderStyles), source.Content, source.Scope);
        PositionFocusPreview();
    }
    private void UpdatePreviewEnterHint(PreviewSource source)
    {
        if (previewEnterGlyph is null || previewEnterLabel is null) return;
        var button = PreviewEnterButton(source);
        if (previewChrome is not null) previewChrome.IsHitTestVisible = button is not null;
        ((FrameworkElement)VisualTreeHelper.GetParent(previewEnterGlyph)).Visibility = button is null ? Visibility.Collapsed : Visibility.Visible;
        if (button is null) return;
        previewEnterGlyph.FontSize = 22 * appearance.TextScale;
        previewEnterLabel.FontSize = 14 * appearance.TextScale;
        WidgetGlyphs.Apply(previewEnterGlyph, new() { Id = "preview-enter", Kind = ViewNodeKind.ControllerGlyph,
            ControllerPrompt = previewEntered ? ControllerPrompt.B : button switch
            { ControllerButton.X => ControllerPrompt.X, ControllerButton.Y => ControllerPrompt.Y,
                ControllerButton.LeftStick => ControllerPrompt.LeftStickPress, _ => ControllerPrompt.RightStickPress } }, WidgetControllerPrompts.PlayStation);
        previewEnterLabel.Text = previewEntered ? "Back to source" : "Interact";
        var palette = Shell.ShellChromePalette.Resolve(presentation?.RenderStyles, appearance);
        previewEnterGlyph.Foreground = previewEnterLabel.Foreground = new SolidColorBrush(palette.Muted);
    }
    private bool TryEnterFocusPreview(ControllerButton button, ControllerEventPhase phase)
    {
        if (PreviewEnterButton(CurrentPreviewSource()) != button) return false;
        if (phase == ControllerEventPhase.Pressed) EnterFocusPreview();
        return true;
    }
    private bool EnterFocusPreview(bool focusFirst = true)
    {
        if (previewEntered) return true;
        if (CurrentPreviewSource() is not { } source || PreviewEnterButton(source) is null) return false;
        if (!IsFocusPreviewOpen)
        {
            UpdateFocusPreview();
            previewTimer?.Stop(); previewDelay.Complete(Environment.TickCount64);
            if (ResolveDeferredPreview(source) is not { } resolved) return false;
            previewSource=source=resolved; ShowFocusPreview(source);
        }
        if (previewSurface?.Fragment is not { } fragment || !IsFocusPreviewOpen) return false;
        RememberFocus(source.Source);
        CancelGroupEntry(); needsEntry = false; pendingRestore = null; restoreNativeFocus = false;
        previewReturnTarget = source.Row is null ? source.Source.Element as Control : source.Anchor as Control;
        previewEntered = true;
        fragment.SetAutomaticFocusEnabled(true);
        // The first control is in authored order. Re-entry retains its control and scroll.
        if (focusFirst && (fragment.RememberedBinding() ?? fragment.bindings.Values.FirstOrDefault(fragment.Navigable)) is { } target)
            if (!fragment.FocusBinding(target)) fragment.Enter();
        UpdatePreviewEnterHint(source);
        NotifyControllerGuideChanged();
        return true;
    }
    private bool PreviewInputCurrent(WidgetViewPresenter fragment) => previewEntered &&
        ReferenceEquals(previewSurface?.Fragment, fragment) && previewSource is { } source &&
        presentationInputEnabled && presentationActive && !disposed && !applying && presentation?.IsCurrent == true &&
        RetainedPreviewIsCurrent(source);

    private async Task DispatchPreviewActionAsync(WidgetViewPresenter fragment, WidgetPresentationBinding displayed, WidgetActionEvent action)
    {
        if (!PreviewInputCurrent(fragment) || previewSource is not { } source ||
            declarations[source.Source.Identity.Id].Node.IsBusy == true ||
            !await AdmitInteractionAsync(displayed.Frame.Authority) || !PreviewInputCurrent(fragment) ||
            previewSource?.Key.Equals(source.Key) != true) return;
        if (source.ContentLease is { } content)
            await content.AdmitPreviewActionAsync(displayed.Frame,source.ContentKey!,new(action.SourceElementId,action.ActionId),action.Sequence,action.MonotonicTimestampMicroseconds);
        else if (source.Row is { } row)
            await row.Lease.AdmitPreviewActionAsync(displayed.Frame, row.Item.Key,
                new(action.SourceElementId, action.ActionId), action.Sequence, action.MonotonicTimestampMicroseconds);
        else await DispatchCapturedActionAsync(displayed, action);
    }
    private void ExitFocusPreview(bool restoreFocus = true)
    {
        if (!previewEntered) return;
        previewSurface?.Fragment?.SetAutomaticFocusEnabled(false);
        previewSurface?.Fragment?.DismissTransientControl();
        previewEntered = false;
        previewExitRevision++;
        previewPointerIntent = false;
        previewPointerTarget = null;
        if (previewSource is { } source && RetainedPreviewIsCurrent(source))
        {
            if (restoreFocus) previewReturnTarget?.Focus(FocusState.Keyboard);
            UpdatePreviewEnterHint(source);
        }
        NotifyControllerGuideChanged();
    }
    private void PositionFocusPreview()
    {
        if (previewSource is not { } source || previewChrome is null || previewFrame is null || previewLayer is null) return;
        if (VisibleAnchorBounds(source.Anchor, this) is not { Valid: true } anchor) { ClearFocusPreview(); return; }
        var inset = previewFrame.ShadowInset;
        var width = Math.Max(0, Math.Min(source.Options.Width, ActualWidth - 2 * inset));
        var maximumHeight = Math.Max(0, Math.Min(source.Options.Height, ActualHeight - 2 * inset));
        previewChrome.Width = width;
        var height = source.Options.SizeToContentHeight
            ? previewHeight.Resolve(width, 96, maximumHeight, appearance.TextScale,
                () => ContentSizedPresentationMeasure.Height(previewChrome, width, maximumHeight))
            : maximumHeight;
        var bounds = AnchoredPresentationGeometry.Place(ActualWidth, ActualHeight, anchor, width, height, source.Options.Placement, inset);
        if (previewBounds == bounds) return;
        previewBounds = bounds;
        previewFrame.Width = previewChrome.Width = bounds.Width;
        previewFrame.Height = previewChrome.Height = bounds.Height;
        previewFrame.Margin = new(bounds.X, bounds.Y, 0, 0);
        previewChrome.Clip = new RectangleGeometry { Rect = new(0, 0, bounds.Width, bounds.Height) };
    }
    private void HideFocusPreview()
    {
        var restoreSource = previewEntered;
        needsEntry |= restoreSource;
        previewEntered = false; previewReturnTarget = null;
        previewSurface?.Fragment?.SetAutomaticFocusEnabled(false);
        LayoutUpdated -= PreviewLayoutChanged;
        if (previewFontSizeSubscription is { } subscription)
            UnregisterPropertyChangedCallback(FontSizeProperty, subscription);
        previewFontSizeSubscription = null;
        previewPortal?.Dispose(); previewPortal = null;
        previewTheme?.Dispose(); previewTheme = null;
        previewFrame?.Dispose(); previewFrame = null;
        if (previewChrome is not null) previewChrome.Child = null;
        previewContentPanel?.Children.Clear(); previewContentPanel = null; previewEnterGlyph = null; previewEnterLabel = null;
        previewChrome = null; previewLayer = null;
        if (previewSurface is { Fragment: { } fragment } surface)
        {
            if (ReferenceEquals(previewOriginalFontSize, DependencyProperty.UnsetValue)) fragment.ClearValue(FontSizeProperty);
            else fragment.SetValue(FontSizeProperty, previewOriginalFontSize);
            fragment.previewOwner = null;
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
        if (restoreSource && !applying && presentationInputEnabled) QueueEntryFocus();
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
        CancelDeferredPreviewRead();
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
