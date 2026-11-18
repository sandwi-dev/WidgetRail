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
        internal object VisualKey => (Owner.Token, Source.Token, Anchor, Row is null ? null : RowIdentity(Row), Options, Provider, ContentKey);
        internal object Key => (VisualKey, Row?.Lease.LeaseId);
    }
    private readonly FocusPreviewDelayState previewDelay = new();
    private PreviewSource? previewSource;
    private WidgetPresentationIndexedLease? retainedPreviewLease;
    private IAsyncDisposable? retainedPreviewLeaseOwner;
#if ENABLE_WIDGET_VALIDATION
    internal WidgetPresentationIndexedLease? FocusPreviewLeaseForValidation => previewSource?.Row?.Lease;
#endif
    private DependencyObject? previewPointerTarget;
    private object? previewPointerFocus;
    private bool previewPointerIntent;
    private bool previewEventsAttached;
    private DispatcherQueueTimer? previewTimer;
    private PresentationPortal? previewPortal;
    private Grid? previewLayer;
    private Border? previewChrome;
    private WidgetPreviewFrame? previewFrame;
    private Motion.WidgetDialogMotion? previewMotion;
    private NativePopupTheme.Lease? previewTheme;
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
    private readonly FocusPreviewPointerTransit previewTransit = new();
    private (double X, double Y)? previewPointerPosition;
    private bool previewHoverRetained;
    private DispatcherQueueTimer? previewExitTimer;
    private FontIcon? previewEnterGlyph;
    private TextBlock? previewEnterLabel;
    private bool previewEntered;
    private PreviewSource? pendingPrimaryPreview;
    private WidgetViewPresenter? previewOwner;
    private Control? previewReturnTarget;
    internal bool IsFocusPreviewOpen => previewPortal is not null &&
        (previewPortal.Visibility == Visibility.Visible || previewSource is { } source && PreviewOwnsExpandedMedia(source));
    internal FrameworkElement? FocusPreviewChrome => previewChrome;
    internal WidgetPreviewFrame? FocusPreviewFrame => previewFrame;
    internal Task<Motion.WidgetMotionOutcome>? FocusPreviewOpening => previewMotion?.Opening;
    internal object FocusPreviewDiagnostics() => new
    {
        source = previewSource?.Source.Identity.Id, current = CurrentPreviewSource()?.Source.Identity.Id,
        focusedSource = PreviewFocusedSourceDiagnostics(),
        previewEntered, pendingPrimary = pendingPrimaryPreview?.Source.Identity.Id, applying, presentationActive, presentationInputEnabled,
        authorityCurrent = presentation?.IsCurrent, transitioning = activeTransition is not null,
        resizing = surfaceResize?.Playback is { IsCompleted: false },
        rowCurrent = previewSource?.Row?.Lease.IsCurrent,
        rowSource = previewSource?.Row?.Lease.Range.Source,
        admittedSource = previewSource is { } src ? declarations.GetValueOrDefault(src.Source.Identity.Id)?.Node.IndexedCollection : null,
        anchorLoaded = previewSource?.Anchor.IsLoaded,
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
    private object? PreviewFocusedSourceDiagnostics()
    {
        var target = XamlRoot is null ? null : FocusManager.GetFocusedElement(XamlRoot) as DependencyObject;
        if (FindBinding(target) is not { } binding) return null;
        var row = (binding.Element as WidgetIndexedCollectionView)?.CapturePreviewRow(target);
        return new { binding.Identity, navigable = Navigable(binding), binding.Element.IsLoaded,
            binding.Element.ActualWidth, binding.Element.ActualHeight,
            capturedRow = row?.Row.Item.Key, rowCurrent = row?.Row.Lease.IsCurrent,
            previewKey = row?.Row.Item.Root.FocusPreviewKey, sourceKey = declarations[binding.Identity.Id].Node.FocusPreviewKey,
            anchor = VisibleAnchorBounds(row?.Anchor ?? binding.LayoutElement, this) };
    }

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
        previewExitTimer?.Stop();
        if (previewExitTimer is not null) previewExitTimer.Tick -= PreviewPointerExitElapsed;
        previewExitTimer = null; previewTransit.Reset(); previewPointerPosition = null;
        if (!previewEventsAttached) return;
        previewEventsAttached = false;
        PointerMoved -= PreviewPointerMoved; PointerExited -= PreviewPointerExited;
        GotFocus -= PreviewGotFocus; LostFocus -= PreviewLostFocus;
        Unloaded -= PreviewUnloaded; PreviewKeyDown -= PreviewKeyPressed;
        LayoutUpdated -= PreviewSourceLayoutReady;
    }
    private ControllerButton? PreviewEnterButton(PreviewSource? source) =>
        source is { Options.EnterButton: ControllerButton.A, Source.Element: WidgetIndexedCollectionView { SelectionActive: true } }
            ? null : source?.Options.EnterButton;
    private bool RetainedPreviewIsCurrent(PreviewSource source) =>
        RetainedPreviewOwnerIsCurrent(source) && (source.Row is null || source.Row.Lease.IsCurrent);
    private void PreparePreviewRefreshFocus(IReadOnlyDictionary<string,Declaration> plan,WidgetPresentationBinding next)
    {
        if(disposed||!presentationActive||!presentationInputEnabled||!previewEntered||presentation is null||
            !next.SameSurface(presentation)||next.Scope!=activeScope||HasTransientControl||
            previewSource is not {Row:{} row,Source.Element:WidgetIndexedCollectionView collection} source||
            previewSurface?.Fragment is not {} fragment||fragment.HasTransientControl||fragment.FocusedBinding() is null||
            fragment.FocusedMediaPlayer is {IsExpanded:true} or {IsMovingPresentation:true} or {HasOptionsMenu:true}||
            !RetainedPreviewOwnerIsCurrent(source)||!plan.TryGetValue(source.Source.Identity.Id,out var declared)||
            declared.Identity.Scope!=activeScope)return;
        // The old semantic lease may already be retired by this publication.
        // It supplies immutable source identity only; no old action/media access
        // is admitted, and the collection reacquires the new query's row.
        if(!collection.AdoptPreviewRefreshFocus(row,next,declared.Node))return;
        ExitFocusPreview(restoreFocus:false);
        ClearFocusPreview();
    }
    private bool PreviewOwnsExpandedMedia(PreviewSource source) => previewEntered &&
        previewSource?.Key.Equals(source.Key) == true && previewSurface?.Fragment is { } fragment &&
        Equals(fragment.previewMediaOwner, source.Key) &&
        fragment.FocusedMediaPlayer is { IsExpanded: true } or { IsMovingPresentation: true };
    private bool RetainedPreviewOwnerIsCurrent(PreviewSource source) =>
        bindings.GetValueOrDefault(source.Source.Identity.Id)?.Token == source.Source.Token &&
        bindings.GetValueOrDefault(source.Owner.Identity.Id)?.Token == source.Owner.Token &&
        source.Source.Identity.Scope == activeScope &&
        declarations[source.Owner.Identity.Id].Node.FocusPreview == source.Options &&
        (source.Row is not null || declarations[source.Source.Identity.Id].Node.FocusPresentation is not null ||
            declarations[source.Source.Identity.Id].Node.FocusPreviewKey == source.ContentKey && source.ContentKey is not null) &&
        (source.Provider is null || declarations[source.Owner.Identity.Id].Node.FocusPreviewSource == source.Provider) &&
        source.ContentLease?.IsCurrent != false &&
        declarations[source.Source.Identity.Id].Node.IsDisabled != true &&
        (VisibleAnchorBounds(source.Anchor, this) is { Valid: true } || PreviewOwnsExpandedMedia(source) &&
            source.Anchor.IsLoaded && ReferenceEquals(source.Anchor.XamlRoot, XamlRoot));

    private PreviewSource? RetainPreviewSource(PreviewSource source)
    {
        if (!RetainedPreviewOwnerIsCurrent(source)) return null;
        if (source.Row is { } row && source.Source.Element is WidgetIndexedCollectionView indexed)
        {
            // Fullscreen hides the shell's inline host, not its logical source.
            // Keep only this exact admitted row while its player owns placement.
            if (PreviewOwnsExpandedMedia(source)) return row.Lease.IsCurrent ? source : null;
            if (PreviewSourceFromTarget(source.Anchor) is { } current)
                return source.VisualKey.Equals(current.VisualKey) ? RetainUnchangedPrefixPreview(current) : null;
            return indexed.IsPreviewContentRefreshing(row, source.Anchor) ? source : null;
        }
        return source with { Content = source.Provider is not null ? source.Content : declarations[source.Source.Identity.Id].Node.FocusPresentation! };
    }

    private void PreviewGotFocus(object sender, RoutedEventArgs args)
    {
        TraceFocus($"preview-got-focus entered={previewEntered} fragment={previewSurface?.Fragment?.FocusedBinding()?.Identity.Id} transient={previewSurface?.Fragment?.HasTransientControl}");
        if (previewEntered && previewSurface?.Fragment is { } fragment && fragment.FocusedBinding() is null &&
            !fragment.OwnsSelectPopupFocus && !fragment.OwnsSelectFocusReturn &&
            fragment.FocusedMediaPlayer is not ({ IsExpanded: true } or { IsMovingPresentation: true } or { HasOptionsMenu: true })) ExitFocusPreview(restoreFocus: false);
        // Routed GotFocus can trail an already-completed native focus change.
        // Do not let that older event replace a newer pointer hover choice.
        if (!previewPointerIntent || XamlRoot is null || !ReferenceEquals(previewPointerFocus, FocusManager.GetFocusedElement(XamlRoot)))
            UseFocusedPreviewSource();
        QueueSurfaceUpdate();
    }
    private void PreviewLostFocus(object sender, RoutedEventArgs args) => QueueSurfaceUpdate();
    private void PreviewKeyPressed(object sender, KeyRoutedEventArgs args)
    {
        if (args.Key == Windows.System.VirtualKey.Escape && pendingPrimaryPreview is not null)
        {
            args.Handled = true;
            pendingPrimaryPreview = null; CancelDeferredPreviewRead(); NotifyControllerGuideChanged();
            return;
        }
        if (args.Key == Windows.System.VirtualKey.F6 && PreviewEnterButton(CurrentPreviewSource()) is not null)
        { args.Handled = true; EnterFocusPreview(); return; }
        UseFocusedPreviewSource();
    }
    private void UseFocusedPreviewSource()
    {
        if (!previewPointerIntent) return;
        CancelPreviewPointerExit(); previewHoverRetained = false; previewTransit.Reset();
        previewPointerIntent = false; previewPointerTarget = null; previewPointerFocus = null; QueueSurfaceUpdate();
    }
    private void PreviewUnloaded(object sender, RoutedEventArgs args)
    { previewPointerTarget = null; previewPointerFocus = null; ClearFocusPreview(); }

    private bool CanEnterPreview => !disposed && !applying && presentationActive && presentationInputEnabled && !presentationOnly &&
        IsLoaded && presentation?.IsCurrent == true && !HasTransientControl && activeTransition is null &&
        surfaceResize?.Playback is not { IsCompleted: false };

    private PreviewSource? CurrentPreviewSource()
    {
        // Applying/layout probes are temporary admission pauses, not a source
        // departure. Lifecycle retirement clears the intent in ClearFocusPreview.
        if (!CanEnterPreview) return null;
        if (pendingPrimaryPreview is { } pending)
        {
            var focused = XamlRoot is null ? null : FocusManager.GetFocusedElement(XamlRoot) as DependencyObject;
            if (PreviewEnterButton(pending) == ControllerButton.A && PreviewSourceFromTarget(focused, explicitActivation: true)?.Key.Equals(pending.Key) == true &&
                RetainPreviewSource(pending) is { } retainedPending) return retainedPending;
            pendingPrimaryPreview = null;
        }
        if ((previewEntered || previewPointerIntent && (previewPointerOverChrome || previewHoverRetained || previewExitTimer?.IsRunning == true)) &&
            previewSource is { } retained)
            return RetainPreviewSource(retained);
        var target = previewPointerIntent ? previewPointerTarget : XamlRoot is null ? null : FocusManager.GetFocusedElement(XamlRoot) as DependencyObject;
        return PreviewSourceFromTarget(target) is { } current ? RetainUnchangedPrefixPreview(current) : null;
    }

    private PreviewSource RetainUnchangedPrefixPreview(PreviewSource current) =>
        previewSource is { Row: { } row } retained && retainedPreviewLeaseOwner is not null &&
        ReferenceEquals(retainedPreviewLease, row.Lease) && retained.VisualKey.Equals(current.VisualKey) &&
        retained.Source.Element is WidgetIndexedCollectionView collection &&
        RetainedPreviewOwnerIsCurrent(retained) && collection.CanRetainPrefixPreview(row, retained.Anchor)
            ? retained : current;

    private bool RetainPreviewLease(PreviewSource source)
    {
        var lease = source.Row?.Lease;
        if (ReferenceEquals(lease, retainedPreviewLease)) return true;
        ReleasePreviewLease();
        if (lease is null) return true;
        try
        {
            retainedPreviewLeaseOwner = lease.Retain(); retainedPreviewLease = lease;
            TraceFocus($"preview-lease-retain count={lease.Range.Source.Count} current={lease.IsCurrent}");
            lease.RenderStylesChanged += PreviewLeaseStylesChanged;
            return true;
        }
        catch (WidgetPresentationSessionException) { ClearFocusPreview(); return false; }
    }

    private void PreviewLeaseStylesChanged(object? sender, EventArgs args)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            if (!disposed && ReferenceEquals(sender, retainedPreviewLease)) QueueSurfaceUpdate();
        });
    }

    private void ReleasePreviewLease()
    {
        if (retainedPreviewLease is { } lease)
        {
            TraceFocus($"preview-lease-release count={lease.Range.Source.Count} current={lease.IsCurrent}");
            lease.RenderStylesChanged -= PreviewLeaseStylesChanged;
        }
        var owner = retainedPreviewLeaseOwner;
        retainedPreviewLeaseOwner = null; retainedPreviewLease = null;
        if (owner is not null) TrackRetirement(owner.DisposeAsync().AsTask());
    }

    private PreviewSource? PreviewSourceFromTarget(DependencyObject? target, bool explicitActivation = false)
    {
        var source = FindBinding(target);
        // Text/artwork in a composed source are separate render bindings, but
        // the nearest declared focusable control owns its hover semantics.
        // Stop at that control even if disabled; never borrow an outer action.
        while (source is not null && !declarations[source.Identity.Id].Node.IsFocusable)
        {
            var parent = declarations[source.Identity.Id].ParentId;
            source = parent is null ? null : bindings.GetValueOrDefault(parent);
        }
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
                !explicitActivation && (previewPointerIntent && !options.OnHover || !previewPointerIntent && !options.OnFocus) ||
                !bindings.TryGetValue(declaration.Node.Id, out var owner) || owner.Identity.Scope != activeScope ||
                VisibleAnchorBounds(anchor, this) is not { Valid: true }) return null;
            return new(owner, source, anchor, row, content, row?.Lease.Range.ScopeId ?? source.Identity.Scope, options,
                contentKey is null ? null : declaration.Node.FocusPreviewSource,contentKey);
        }
        return null;
    }

    private void UpdateFocusPreview(PreviewSource? activatedSource = null)
    {
        EnsurePreviewEvents(); PruneDeferredPreviews();
        var next = activatedSource ?? CurrentPreviewSource();
        if (next is null) { ClearFocusPreview(); return; }
        var changed = previewDelay.Select(next.VisualKey, next.Options.DelayMilliseconds, Environment.TickCount64);
        var priorCollection = previewSource?.Source.Element as WidgetIndexedCollectionView;
        previewSource = next;
        priorCollection?.RefreshReaderOwnership();
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
        if (ResolveDeferredPreview(next) is { } resolved)
        {
            previewSource = resolved; ShowFocusPreview(resolved);
            if (IsFocusPreviewOpen && pendingPrimaryPreview?.Key.Equals(resolved.Key) == true)
            {
                pendingPrimaryPreview = null;
                EnterFocusPreview(activatedSource: resolved);
            }
        }
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
        if (CurrentPreviewSource() is not { } current || previewSource?.VisualKey.Equals(current.VisualKey) != true) ClearFocusPreview();
        else PositionFocusPreview();
    }
    private void PreviewLayoutChanged(object? sender, object args)
    {
        if (previewSource is null || applying) return;
        if (CurrentPreviewSource() is not { } current || !previewSource.VisualKey.Equals(current.VisualKey)) { ClearFocusPreview(); return; }
        if (!previewSource.Key.Equals(current.Key)) QueueSurfaceUpdate();
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
            var enterHint = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            previewEnterGlyph = new FontIcon { FontSize = 22 };
            previewEnterLabel = new TextBlock { FontSize = 14, VerticalAlignment = VerticalAlignment.Center };
            enterHint.Children.Add(previewEnterGlyph); enterHint.Children.Add(previewEnterLabel);
            previewFrame = new();
            previewMotion = new(previewFrame, null, appearance, systemAnimationsEnabled,
                source.Options.AllowMedia ? Motion.WidgetDialogMotionRole.MediaPreview : Motion.WidgetDialogMotionRole.Preview);
            previewChrome = previewFrame.Surface;
            previewFrame.SetContent(fragment, enterHint);
            previewChrome.IsHitTestVisible = PreviewEnterButton(source) is not null;
            previewChrome.PointerEntered += PreviewChromeEntered;
            previewChrome.PointerMoved += PreviewChromeMoved;
            previewChrome.PointerExited += PreviewChromeExited;

            previewLayer = new Grid { IsHitTestVisible = true };
            previewLayer.Children.Add(previewFrame);
            previewPortal = new(layers, this, previewLayer,
                effectiveView?.Root.Kind == ViewNodeKind.ModalLayer ? PresentationLayer.WidgetDialogPreview : PresentationLayer.ContentPreview,
                passive: PreviewEnterButton(source) is null, followComposition: false, attachInput: false);
            ObservePreviewOutsidePresses();
            (source.Source.Element as WidgetIndexedCollectionView)?.RefreshReaderOwnership();
            // NativeTextScaleScope follows the portal's logical owner for authored
            // font sizes. WinUI property inheritance follows its paint parent instead.
            // Forward the already-scaled default once at the fragment root; explicit
            // child sizes still use their normal scoped style and do not multiply twice.
            previewOriginalFontSize = fragment.ReadLocalValue(FontSizeProperty);
            fragment.FontSize = FontSize;
            previewFontSizeSubscription = RegisterPropertyChangedCallback(FontSizeProperty,
                (_, _) => fragment.FontSize = FontSize);
            previewTheme = NativePopupTheme.Preview(previewFrame, this, presentation?.RenderStyles, appearance,
                () => { previewHeight.Reset(); previewBounds = null; },
                () => presentation?.RenderStyles.GetValueOrDefault(source.Owner.Identity.Id));
            LayoutUpdated += PreviewLayoutChanged;
        }
        if (!RetainPreviewLease(source)) return;
        previewTheme?.Apply();
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
#if ENABLE_WIDGET_VALIDATION
        fragment.ValidationMediaResolver = ValidationMediaResolver;
#endif
        fragment.previewMediaAllowed = source.Options.AllowMedia;
        fragment.previewMediaLease = source.ContentLease ?? row?.Lease;
        fragment.previewMediaItem = source.ContentKey ?? row?.Item.Key;
        fragment.previewMediaOwner = source.Options.AllowMedia ? source.Key : null;
        fragment.BorrowPreviewArtwork = row is not null && source.Source.Element is WidgetIndexedCollectionView indexed
            ? handle => indexed.BorrowPreviewArtwork(row, source.Anchor, handle) : null;
        fragment.previewMediaCurrent = () => !disposed && presentationActive &&
            ReferenceEquals(previewSurface?.Fragment, fragment) && previewSource?.Key.Equals(source.Key) == true &&
            (source.ContentLease ?? row?.Lease)?.IsCurrent != false;
        fragment.ResolveArtworkAsync = source.ContentLease is { } previewLease ? (handle,token)=>previewLease.ResolveArtworkSourceAsync(source.ContentKey!,handle,token) : row is null ? ResolveArtworkAsync : async (handle, token) =>
        {
            try { return await row.Owner.ResolveArtworkAsync(row, handle, token); }
            catch (WidgetRail.WidgetPresentationSession.WidgetPresentationSessionException) when (!row.Lease.IsCurrent) { return null; }
        };
        fragment.ResolveArtworkFallbackAsync = source.ContentLease is { } fallbackLease
            ? (handle, token) => fallbackLease.ResolveArtworkAsync(source.ContentKey!, handle, token)
            : row is null ? ResolveArtworkFallbackAsync : async (handle, token) =>
                await row.Owner.ResolveArtworkAsync(row, handle, token, allowFileSource: false) as WidgetEncodedArtwork;
        fragment.ArtworkGeneration = source.ContentLease?.LeaseId ?? (row is null ? ArtworkGeneration : row.Lease.LeaseId + ":" + row.Item.Key.Length + ":" + row.Item.Key);
        fragment.ArtworkAuthorityCurrent = source.ContentLease is { } contentLease ? ()=>contentLease.IsCurrent : row is null ? ArtworkAuthorityCurrent : () => row.Lease.IsCurrent && previewSource?.Key.Equals(source.Key) == true;
        fragment.Failed = ReportFailure;
        fragment.FocusTrace = FocusTrace;
        _ = fragment.SetPresentationActiveAsync(true);
        fragment.ApplyFragment(source.ContentLease is { } content ? presentation!.WithStyles(content.RenderStyles) : row is null ? presentation! : presentation!.WithStyles(row.Lease.RenderStyles), source.Content, source.Scope);
        PositionFocusPreview();
    }
    private void UpdatePreviewEnterHint(PreviewSource source)
    {
        if (previewEnterGlyph is null || previewEnterLabel is null) return;
        var button = PreviewEnterButton(source);
        if (previewChrome is not null) previewChrome.IsHitTestVisible = button is not null;
        if (previewFrame?.SetFooterVisible(button is not null) == true) { previewHeight.Reset(); previewBounds = null; }
        if (button is null) return;
        previewEnterGlyph.FontSize = 22 * appearance.TextScale;
        previewEnterLabel.FontSize = 14 * appearance.TextScale;
        WidgetGlyphs.Apply(previewEnterGlyph, new() { Id = "preview-enter", Kind = ViewNodeKind.ControllerGlyph,
            ControllerPrompt = previewEntered ? ControllerPrompt.B : button switch
            { ControllerButton.A => ControllerPrompt.A, ControllerButton.X => ControllerPrompt.X, ControllerButton.Y => ControllerPrompt.Y,
                ControllerButton.LeftStick => ControllerPrompt.LeftStickPress, _ => ControllerPrompt.RightStickPress } }, WidgetControllerPrompts.PlayStation);
        previewEnterLabel.Text = previewEntered ? "Back to source" : "Interact";
        var palette = Shell.ShellChromePalette.Resolve(presentation?.RenderStyles, appearance);
        previewEnterGlyph.Foreground = previewEnterLabel.Foreground = new SolidColorBrush(palette.Muted);
    }
    private PreviewSource? FocusedPrimaryPreviewSource()
    {
        if (!CanEnterPreview || XamlRoot is null) return null;
        var source = PreviewSourceFromTarget(FocusManager.GetFocusedElement(XamlRoot) as DependencyObject, explicitActivation: true);
        return PreviewEnterButton(source) == ControllerButton.A ? source : null;
    }
    private bool TryEnterFocusPreview(ControllerButton button, ControllerEventPhase phase)
    {
        var source = CurrentPreviewSource() ?? (button == ControllerButton.A ? FocusedPrimaryPreviewSource() : null);
        if (PreviewEnterButton(source) != button) return false;
        if (phase == ControllerEventPhase.Pressed) EnterFocusPreview(activatedSource: source);
        return true;
    }
    private bool DeclaresPrimaryPreview(string sourceId)
    {
        var source = declarations.GetValueOrDefault(sourceId);
        if (source is null || source.Node.FocusPresentation is null && source.Node.FocusPreviewKey is null) return false;
        for (var owner = source; owner is not null; owner = owner.ParentId is { } parent ? declarations.GetValueOrDefault(parent) : null)
            if (owner.Node.Kind == ViewNodeKind.FocusPresentationSurface)
                return owner.Node.FocusPreview?.EnterButton == ControllerButton.A;
        return false;
    }

    private bool HasPrimaryPreview(DependencyObject target) =>
        PreviewEnterButton(PreviewSourceFromTarget(target, explicitActivation: true)) == ControllerButton.A;
    private bool IsPrimaryPreviewExpanded(DependencyObject target) => previewEntered &&
        PreviewSourceFromTarget(target, explicitActivation: true)?.Key.Equals(previewSource?.Key) == true;
    private void CollapsePrimaryPreview(DependencyObject target)
    {
        if (PreviewSourceFromTarget(target, explicitActivation: true) is not { } source) return;
        if (pendingPrimaryPreview?.Key.Equals(source.Key) == true) pendingPrimaryPreview = null;
        if (previewSource?.Key.Equals(source.Key) == true) ExitFocusPreview();
    }

    private bool TryActivatePrimaryPreview(DependencyObject target)
    {
        if (!CanEnterPreview || PreviewSourceFromTarget(target, explicitActivation: true) is not { } source ||
            PreviewEnterButton(source) != ControllerButton.A || !Eligible(source.Source) ||
            source.Row?.Item.Root.IsBusy == true || source.Row?.Item.Root.IsDisabled == true) return false;
        // Native invoke owns the exact source, not whichever item last received hover.
        // Consume this activation even if a deferred fragment is not ready yet.
        if (previewEntered && previewSource?.Key.Equals(source.Key) != true) ExitFocusPreview(restoreFocus: false);
        if (source.Anchor is Control anchor) anchor.Focus(FocusState.Programmatic);
        UseFocusedPreviewSource();
        EnterFocusPreview(activatedSource: source);
        return true;
    }

    private void ClaimPreviewEntryFocus(PreviewSource source)
    {
        RememberFocus(source.Source);
        CancelGroupEntry(); needsEntry = false; pendingRestore = null; restoreNativeFocus = false;
    }

    private bool EnterFocusPreview(bool focusFirst = true, PreviewSource? activatedSource = null)
    {
        if (previewEntered) return true;
        if ((activatedSource ?? CurrentPreviewSource()) is not { } source || PreviewEnterButton(source) is null) return false;
        // Accepting A supersedes queued automatic restoration immediately, not
        // only after deferred content arrives. A later deliberate departure still cancels.
        if (source.Options.EnterButton == ControllerButton.A) ClaimPreviewEntryFocus(source);
        if (!IsFocusPreviewOpen || previewSource?.Key.Equals(source.Key) != true)
        {
            UpdateFocusPreview(source);
            previewTimer?.Stop(); previewDelay.Complete(Environment.TickCount64);
            if (ResolveDeferredPreview(source) is not { } resolved)
            {
                // One exact-source intent, not a queued widget action. Completion is
                // admitted only while focus, declaration, provider and row lease match.
                if (source.Options.EnterButton == ControllerButton.A) pendingPrimaryPreview = source;
                return false;
            }
            previewSource=source=resolved; ShowFocusPreview(source);
        }
        if (previewSurface?.Fragment is not { } fragment || !IsFocusPreviewOpen)
        {
            if (source.Options.EnterButton == ControllerButton.A) pendingPrimaryPreview = source;
            return false;
        }
        ClaimPreviewEntryFocus(source);
        previewReturnTarget = source.Row is null ? source.Source.Element as Control : source.Anchor as Control;
        pendingPrimaryPreview = null;
        previewEntered = true;
        fragment.RefreshMediaPlayers();
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
        var intentLease = source.ContentLease ?? source.Row?.Lease;
        var intentKey = source.ContentLease is not null ? source.ContentKey : source.Row?.Item.Key;
        if (Session is { } session && intentLease is not null && intentKey is not null &&
            session.ResolveIndexedIntentAction(intentLease, displayed.Frame, intentKey, action) is { } intentAction)
        {
            if (DispatchActionAsync is { } dispatch) await dispatch(new(displayed.Frame, intentAction)
            { IndexedLease = intentLease, IndexedItemKey = intentKey, PinnedSelection = displayed.Selection, PinnedProjection = displayed.Projection, IsPreviewIntent = true });
        }
        else if (source.ContentLease is { } content)
            await content.AdmitPreviewActionAsync(displayed.Frame,source.ContentKey!,new(action.SourceElementId,action.ActionId) { RequestedValue = action.RequestedValue },action.Sequence,action.MonotonicTimestampMicroseconds);
        else if (source.Row is { } row)
            await row.Lease.AdmitPreviewActionAsync(displayed.Frame, row.Item.Key,
                new(action.SourceElementId, action.ActionId) { RequestedValue = action.RequestedValue }, action.Sequence, action.MonotonicTimestampMicroseconds);
        else await DispatchCapturedActionAsync(displayed, action, previewIntent: true);
    }
    private void ExitFocusPreview(bool restoreFocus = true)
    {
        if (!previewEntered) return;
        previewSurface?.Fragment?.SetAutomaticFocusEnabled(false);
        previewSurface?.Fragment?.DismissTransientControl();
        previewEntered = false;
        previewSurface?.Fragment?.RefreshMediaPlayers();
        CancelPreviewPointerExit();
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
        if (PreviewOwnsExpandedMedia(source)) return; // The fullscreen host owns geometry until inline restoration settles.
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
        previewTransit.SetBounds(anchor, bounds);
        if (previewPointerPosition is { } point) previewTransit.Retain(point);
        previewFrame.Width = previewChrome.Width = bounds.Width;
        previewFrame.Height = previewChrome.Height = bounds.Height;
        previewFrame.Margin = new(bounds.X, bounds.Y, 0, 0);
        previewChrome.Clip = new RectangleGeometry { Rect = new(0, 0, bounds.Width, bounds.Height) };
    }
    private void HideFocusPreview([System.Runtime.CompilerServices.CallerMemberName] string caller = "")
    {
        if (retainedPreviewLease is not null) TraceFocus($"preview-hide:{caller} entered={previewEntered} loaded={IsLoaded} applying={applying} input={presentationInputEnabled}");
        ReleasePreviewLease();
        previewOutsidePressInput?.Dispose(); previewOutsidePressInput = null;
        CancelPreviewPointerExit(); previewTransit.Reset(); previewHoverRetained = false;
        // Native Slider/Thumb capture can otherwise survive detaching the
        // retained fragment and resume an old drag when it is shown again.
        if (previewSurface?.Fragment is { } pointerOwner) Input.PointerCaptureLifetime.ReleaseWithin(pointerOwner);
        var restoreSource = previewEntered;
        needsEntry |= restoreSource;
        previewEntered = false; previewReturnTarget = null;
        previewSurface?.Fragment?.SetAutomaticFocusEnabled(false);
        LayoutUpdated -= PreviewLayoutChanged;
        if (previewFontSizeSubscription is { } subscription)
            UnregisterPropertyChangedCallback(FontSizeProperty, subscription);
        previewFontSizeSubscription = null;
        previewMotion?.Dispose(); previewMotion = null;
        previewPortal?.Dispose(); previewPortal = null;
        (previewSource?.Source.Element as WidgetIndexedCollectionView)?.RefreshReaderOwnership();
        previewTheme?.Dispose(); previewTheme = null;
        previewFrame?.Dispose(); previewFrame = null;
        if (previewChrome is not null) previewChrome.Child = null;
        previewEnterGlyph = null; previewEnterLabel = null;
        previewChrome = null; previewLayer = null;
        if (previewSurface is { Fragment: { } fragment } surface)
        {
            if (ReferenceEquals(previewOriginalFontSize, DependencyProperty.UnsetValue)) fragment.ClearValue(FontSizeProperty);
            else fragment.SetValue(FontSizeProperty, previewOriginalFontSize);
            fragment.previewOwner = null;
            fragment.BorrowPreviewArtwork = null;
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
    private void ClearFocusPreview([System.Runtime.CompilerServices.CallerMemberName] string caller = "")
    {
        if (previewSource is { } traceSource) TraceFocus($"preview-clear:{caller} ready={CanEnterPreview} anchorLoaded={traceSource.Anchor.IsLoaded} visible={VisibleAnchorBounds(traceSource.Anchor, this)?.Valid} current={traceSource.Row?.Lease.IsCurrent} count={traceSource.Row?.Lease.Range.Source.Count}");
        pendingPrimaryPreview = null;
        CancelDeferredPreviewRead();
        CancelPreviewPointerExit(); previewPointerOverChrome = false;
        var priorCollection = previewSource?.Source.Element as WidgetIndexedCollectionView;
        previewTimer?.Stop(); previewDelay.Clear(); previewSource = null;
        foreach (var scroll in previewScrollers) scroll.ViewChanged -= PreviewScrolled;
        previewScrollers.Clear(); HideFocusPreview();
        priorCollection?.RefreshReaderOwnership();
    }
    private async Task RefreshPreviewAfterMotionAsync(Task motion)
    {
        try { await motion; }
        catch (Exception error) { if (!disposed) ReportFailure(error); }
        if (!disposed) QueueSurfaceUpdate();
    }
}
