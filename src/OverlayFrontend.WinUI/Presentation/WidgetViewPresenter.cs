using PresentationSession = WidgetRail.WidgetPresentationSession.WidgetPresentationSession;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using WidgetRail.WidgetPresentationSession;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;
using WidgetRail.OverlayFrontend.WinUI.Motion;

namespace WidgetRail.OverlayFrontend.WinUI.Presentation;

internal sealed record WidgetElementIdentity(string Scope, string Id, ViewNodeKind Kind, string ItemPath);
internal sealed record WidgetActionRequest(WidgetPresentationFrame Displayed, WidgetActionEvent Action)
{
    internal WidgetPresentationAuthority Authority => Displayed.Authority;
}

/// <summary>
/// Trusted declaration-to-control adapter. WinUI owns all layout, focus traversal,
/// control behavior and painting. This adapter retains controls by semantic identity
/// and reconciles declaration membership; it contains no geometry or frame scheduler.
/// </summary>
internal sealed partial class WidgetViewPresenter : ContentControl, IAsyncDisposable
{
    private sealed record Declaration(ViewNode Node, WidgetElementIdentity Identity, string? ParentId);
    private sealed record Binding(WidgetElementIdentity Identity, FrameworkElement Element, Panel? Children, object Token,
        WidgetMotionHost? MotionHost = null)
    { internal FrameworkElement LayoutElement => MotionHost ?? Element; }
    private Dictionary<string, Declaration> declarations = new(StringComparer.Ordinal);
    private Dictionary<string, Binding> bindings = new(StringComparer.Ordinal);
    private readonly Dictionary<string, WidgetElementIdentity> remembered = new(StringComparer.Ordinal);
    private WidgetPresentationFrame? frame;
    private WidgetPresentationBinding? presentation;
    private ViewSnapshot? effectiveView => presentation?.View;
    private string activeScope => presentation?.Scope ?? string.Empty;
    private bool CanDispatchAction => presentationInputEnabled && (presentation?.Selection is not null ? Session is not null : DispatchActionAsync is not null);
    private bool applying;
    internal bool IsApplyingPresentation => applying;
    private bool needsEntry;
    private bool focusQueued;
    private bool restoreNativeFocus;
    private bool automaticFocusEnabled = true;
    private Binding? pendingRestore;
    private long actionSequence;
    private readonly bool presentationOnly;
    private readonly double nativeBaseFontSize;
    public PresentationSession? Session { get; set; }
    internal Previews.WindowPreviewRenderer? WindowPreviews { get; set; }
    public Func<WidgetActionRequest, Task>? DispatchActionAsync { get; set; }
    public Func<WidgetPresentationAuthority, CancellationToken, Task<bool>>? EnsureInteractionAsync { get; set; }
    internal bool IsInteractionCurrent(WidgetPresentationAuthority authority) => presentationInputEnabled && presentationActive && !disposed && !applying &&
        !presentationOnly && frame is not null && SameOwner(authority, frame.Authority) &&
        presentation?.IsCurrent == true && (presentation.Selection is not null || authority.ActiveInputScopeId == activeScope);
    private Task<bool> AdmitInteractionAsync(WidgetPresentationAuthority authority, CancellationToken cancellationToken = default) =>
        !presentationActive || !presentationInputEnabled ? Task.FromResult(false) :
        EnsureInteractionAsync?.Invoke(authority, cancellationToken) ?? Task.FromResult(IsInteractionCurrent(authority));

    /// <summary>Explicit page/window entry. Ordinary data updates do not call this.</summary>
    public void Enter(bool restoreNativeFocus = false)
    {
        TraceFocus("entry-request");
        this.restoreNativeFocus |= restoreNativeFocus;
        needsEntry = needsEntry || FocusedBinding() is not { } focused || !Navigable(focused) ||
            focused.Element is WidgetIndexedCollectionView { IsFocusParked: true, IsEntryPending: false } ||
            RememberedBinding() is { } preferred && !ReferenceEquals(preferred, focused);
        QueueEntryFocus();
    }

    /// <summary>Tray/background previews still update but cannot steal native focus.</summary>
    internal void SetAutomaticFocusEnabled(bool enabled)
    {
        if (automaticFocusEnabled == enabled) return;
        TraceFocus(enabled ? "enable-focus" : "disable-focus");
        if (!enabled) RememberFocus();
        automaticFocusEnabled = enabled;
        if (!enabled)
        {
            scrollGesture = null;
            directionalScroll = null;
            ClearEntryLayoutWait();
            foreach (var binding in bindings.Values)
                if (binding.Element is WidgetIndexedCollectionView collection) collection.CancelHostNavigation();
            return;
        }
        // Pointer or UIA focus is an explicit choice; enabling host interaction
        // must not replace it with an old pending initial-focus request.
        if (FocusedBinding() is { } focused && Navigable(focused) &&
            (RememberedBinding() is not { } preferred || ReferenceEquals(preferred, focused))) needsEntry = false;
    }

    public WidgetViewPresenter(bool presentationOnly = false)
    {
        this.presentationOnly = presentationOnly;
        if (!presentationOnly) Content = motionStage = new();
        nativeBaseFontSize = FontSize;
        WidgetControllerPrompts.Changed += ControllerPromptsChanged;
        HorizontalContentAlignment = HorizontalAlignment.Stretch;
        VerticalContentAlignment = VerticalAlignment.Stretch;
        IsTabStop = false;
        XYFocusKeyboardNavigation = XYFocusKeyboardNavigationMode.Enabled;
        Loaded += (_, _) => QueueEntryFocus();
        SizeChanged += (_, _) => { SettleTransitions(); SettleModalExit(); RefreshResponsiveLayout(); };
        Unloaded += (_, _) => { DismissTransientControl(); if (!IsLoaded) { directionalScroll = null; SettleTransitions(); SettleModalExit(); } };
        GotFocus += (_, _) => { RememberFocus(); NotifyControllerGuideChanged(); };
        GettingFocus += OnGettingFocus;
        PreviewKeyDown += DirectionalKeyDown;
        AddHandler(PointerPressedEvent, new PointerEventHandler((_, _) => CancelGroupEntry()), true);
        AddHandler(KeyDownEvent, new KeyEventHandler((_, args) =>
        {
            if (Input.GamepadKeyBoundary.Owns(this, args)) return;
            if (args.Key is Windows.System.VirtualKey.Up or Windows.System.VirtualKey.Down or Windows.System.VirtualKey.Left or
                Windows.System.VirtualKey.Right or Windows.System.VirtualKey.Tab or Windows.System.VirtualKey.Home or
                Windows.System.VirtualKey.End or Windows.System.VirtualKey.PageUp or Windows.System.VirtualKey.PageDown or
                Windows.System.VirtualKey.Enter or Windows.System.VirtualKey.Space or Windows.System.VirtualKey.Escape) CancelGroupEntry();
        }), true);
    }

    public void Apply(WidgetPresentationFrame next) => ApplyBinding(WidgetPresentationBinding.ForMain(next));

    internal void ApplyPinned(WidgetPinnedSelection selection, WidgetPinnedProjection projection) =>
        ApplyBinding(WidgetPresentationBinding.ForPinned(Session ?? throw new InvalidOperationException("Pinned presentation requires its session."), selection, projection));

    private void ApplyBinding(WidgetPresentationBinding next) => ApplyCore(next, next.View.Root,
        next.View.Root.InputScopeId ?? next.View.Root.Id);

    internal void ApplyFragment(WidgetPresentationFrame parent, ViewNode root, string scope) =>
        ApplyFragment(WidgetPresentationBinding.ForMain(parent), root, scope);
    internal void ApplyFragment(WidgetPresentationBinding parent, ViewNode root, string scope)
    { fragmentRootId = root.Id; ApplyCore(parent, root, scope); }

    private void ApplyCore(WidgetPresentationBinding nextPresentation, ViewNode root, string rootScope)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (!DispatcherQueue.HasThreadAccess) throw new InvalidOperationException("Widget presentation must use its WinUI dispatcher.");
        ArgumentNullException.ThrowIfNull(nextPresentation);
        var next = nextPresentation.Frame;
        var sameOwner = nextPresentation.SameSurface(presentation);
        if (!presentationOnly && sameOwner && (frame!.Authority.SnapshotSequence > next.Authority.SnapshotSequence ||
            frame.Authority.SnapshotSequence == next.Authority.SnapshotSequence && frame.AppearanceRevision >= next.AppearanceRevision)) return;
        if (!presentationOnly && nextPresentation.IsAppearanceUpdateOf(presentation))
        { ApplyAppearanceFrame(nextPresentation); return; }
        var plan = Plan(root, rootScope);
        // Transition preparation already revokes and detaches native controls.
        // Capture logical focus and enter the publication transaction before any
        // such mutation can trigger WinUI's focused-element-removal recovery.
        var focused = FocusedBinding();
        var oldScope = activeScope;
        TransitionCommit transition;
        ModalExit? modalExit;
        applying = true;
        try
        {
            transition = PrepareTransitions(plan, sameOwner);
            modalExit = PrepareModalExit(plan, sameOwner);
            var nextBindings = new Dictionary<string, Binding>(StringComparer.Ordinal);
            foreach (var declaration in plan.Values)
                nextBindings.Add(declaration.Node.Id, sameOwner && !transition.ReplacedIds.Contains(declaration.Node.Id)
                    && bindings.TryGetValue(declaration.Node.Id, out var retained)
                    && retained.Identity == declaration.Identity
                    && IsSwitch(declarations[declaration.Node.Id].Node) == IsSwitch(declaration.Node)
                    && SameGridLayoutMode(declarations[declaration.Node.Id].Node, declaration.Node)
                    && SameSurfaceOwnership(declaration, plan)
                    && declarations[declaration.Node.Id].Node.Transition?.Kind == declaration.Node.Transition?.Kind
                    && declarations[declaration.Node.Id].Node.ActionSurfacePresentation == declaration.Node.ActionSurfacePresentation
                    ? retained : Create(declaration, IsModalDialog(declaration, plan)));
            var currentRoot = nextBindings[root.Kind == ViewNodeKind.ModalLayer ? root.Children[0].Id : root.Id].LayoutElement;
            if (sameOwner) RetainBackgroundPaint(nextBindings, plan);
            var currentModal = root.Kind == ViewNodeKind.ModalLayer ? nextBindings[root.Id].LayoutElement : null;
            // Detach only changed parentage before any insert. In-place property
            // updates and adjacent insertions never clear surviving child controls.
            if (motionStage is not null)
            {
                if (!ReferenceEquals(motionStage.Current, currentRoot)) motionStage.SetCurrent(null);
                if (!ReferenceEquals(motionStage.Modal, currentModal)) motionStage.SetModal(null);
            }
            else if (Content is FrameworkElement previousRoot && !ReferenceEquals(previousRoot, nextBindings[root.Id].LayoutElement)) Content = null;
            foreach (var (id, binding) in bindings)
            {
                if (transition.Retained.Contains(binding) || modalExit?.Retained.Contains(binding) == true) continue;
                if (!nextBindings.TryGetValue(id, out var replacement) || !ReferenceEquals(binding, replacement)) Retire(binding);
                if (binding.Children is null) continue;
                for (var index = binding.Children.Children.Count - 1; index >= 0; --index)
                {
                    var child = binding.Children.Children[index];
                    if (binding.Element is WidgetModalLayer layer && ReferenceEquals(child, layer.Chrome)) continue;
                    var childId = ((FrameworkElement)child).Tag is WidgetElementIdentity identity ? identity.Id : string.Empty;
                    if (!plan.TryGetValue(childId, out var nextChild) || nextChild.ParentId != id
                        || !nextBindings.TryGetValue(id, out var nextParent) || !ReferenceEquals(nextParent, binding)
                        || !ReferenceEquals(nextBindings[childId].LayoutElement, child))
                        binding.Children.Children.RemoveAt(index);
                }
            }
            if (!sameOwner)
            {
                remembered.Clear();
                ResetGroupFocus();
                ClearSurfaceState();
            }
            declarations = plan;
            bindings = nextBindings;
            frame = next;
            presentation = nextPresentation;
            if (!sameOwner || oldScope != nextPresentation.Scope) directionalScroll = null;
            if (presentationActive && !presentationOnly) WindowPreviews?.Apply(next);
            UpdateResponsiveVisibility();
            // Scope memory survives transient/loading declarations. It is
            // validated when used and cleared with the owning incarnation.
            foreach (var declaration in plan.Values)
            {
                var binding = bindings[declaration.Node.Id];
                Update(binding, declaration.Node);
                if (binding.Children is { } panel)
                    for (var index = 0; index < declaration.Node.Children.Count; ++index)
                    {
                        // The authoritative parent remains in the stable stage;
                        // modal chrome overlays it without native unload/reload.
                        if (binding.Element is WidgetModalLayer && index == 0) continue;
                        var child = bindings[declaration.Node.Children[index].Id].LayoutElement;
                        if (index < panel.Children.Count && ReferenceEquals(panel.Children[index], child)) continue;
                        var oldIndex = panel.Children.IndexOf(child);
                        if (oldIndex >= 0) panel.Children.RemoveAt(oldIndex);
                        panel.Children.Insert(index, child);
                    }
            }
            UpdateModalGeometry();
            if (motionStage is not null) { motionStage.SetCurrent(currentRoot); motionStage.SetModal(currentModal); Content = motionStage; }
            else Content = bindings[root.Id].LayoutElement;
            needsEntry = needsEntry || !sameOwner || oldScope != nextPresentation.Scope
                || focused?.Element is WidgetIndexedCollectionView { IsFocusParked: true, IsEntryPending: false }
                || (focused is not null && (!bindings.TryGetValue(focused.Identity.Id, out var current)
                    || !ReferenceEquals(current, focused) || !Navigable(current)));
            if (!presentationOnly) UpdateFocusPolicy(nextPresentation.View);
            else needsEntry = false;
            pendingRestore = !needsEntry && focused is not null && Navigable(focused)
                && !ReferenceEquals(FocusedBinding(), focused) ? focused : null;
        }
        finally { applying = false; }
        NotifyControllerGuideChanged();
        ValidateTransientControl();
        ValidateSliderAdjustment();
        QueueMediaRefresh();
        if (needsEntry || pendingRestore is not null || pendingGroupEntry is not null) QueueEntryFocus();
        QueueSurfaceUpdate();
        if (presentationActive) { StartTransitions(transition); StartModalExit(modalExit); }
        else { SettleTransitions(); SettleModalExit(); }
    }

    private void QueueEntryFocus()
    {
        if (!presentationActive || presentationOnly || !automaticFocusEnabled) return;
        if (focusQueued) return;
        focusQueued = true;
        if (!DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, RestoreFocus)) focusQueued = false;
    }

    private void RestoreFocus()
    {
        focusQueued = false;
        TraceFocus("restore-focus");
        if (!presentationActive || !automaticFocusEnabled || !IsLoaded || XamlRoot is null || frame is null || applying) return;
        var reassertFocus = restoreNativeFocus;
        restoreNativeFocus = false;
        if (TryRestoreTransientFocus()) return;
        if (TryRestoreGroupEntry()) return;
        if (reassertFocus && FocusedBinding() is { } current && Navigable(current) &&
            current.Element is not WidgetIndexedCollectionView { IsFocusParked: true } &&
            (RememberedBinding() is not { } preferred || ReferenceEquals(preferred, current)) &&
            FocusManager.GetFocusedElement(XamlRoot) is Control leaf && leaf.Focus(FocusState.Keyboard))
            needsEntry = false;
        if (pendingRestore is { } restore)
        {
            pendingRestore = null;
            if (bindings.TryGetValue(restore.Identity.Id, out var retained) && ReferenceEquals(retained, restore) && Navigable(restore)
                && !ReferenceEquals(FocusedBinding(), restore)) FocusBinding(restore);
        }
        if (!needsEntry) return;
        var scope = activeScope;
        Binding? target = null;
        if (remembered.TryGetValue(scope, out var identity) && bindings.TryGetValue(identity.Id, out var rememberedBinding)
            && rememberedBinding.Identity == identity && Navigable(rememberedBinding)) target = rememberedBinding;
        if (target is null && effectiveView!.InitialFocusId is { } initial && bindings.TryGetValue(initial, out var initialBinding)
            && Navigable(initialBinding)) target = initialBinding;
        target ??= bindings.Values.FirstOrDefault(Navigable);
        if (target is not null)
        {
            if (FocusBinding(target)) { needsEntry = false; ClearEntryLayoutWait(); }
            else WaitForEntryLayout(target);
        }
    }

    public bool MoveFocus(FocusNavigationDirection direction, bool isRepeat = false)
    {
        if (NavigationObserved is null) return MoveFocusCore(direction, isRepeat);
        var before = InspectionFocusId();
        var handled = MoveFocusCore(direction, isRepeat);
        NavigationObserved?.Invoke($"{direction}: {before} → {InspectionFocusId()} ({(handled ? "handled" : "boundary")})");
        return handled;
    }

    private bool MoveFocusCore(FocusNavigationDirection direction, bool isRepeat)
    {
        if (!presentationActive || applying || !IsLoaded || frame is null) return true;
        if (!presentationInputEnabled) return true;
        // No row is focused yet. Directions cannot use the parking container as
        // a spatial target or cancel the only pending route back to real content.
        if (FindIndexedCollection() is { IsFocusParked: true, IsEntryPending: true }) return true;
        if (!SettleScrollFocus()) return true;
        CancelGroupEntry();
        if (textEntryPopup is { } edit) { edit.Dialog.MoveFocus(direction); return true; }
        if (MoveContextFocus(direction) || MoveSelectFocus(direction) || MoveSlider(direction)) return true;
        return MoveDirectionalFocus(direction, isRepeat);
    }

    public void ActivateFocused()
    {
        if (!presentationActive || applying || disposed || presentationOnly) return;
        // Parking is not an actionable row. Do not cancel its pending entry or
        // activate a future row which the user has not seen yet.
        if (FindIndexedCollection() is { IsFocusParked: true, IsEntryPending: true }) return;
        CancelGroupEntry();
        if (ActivateContextMenu() || ActivateTextEntry() || ActivateSelect() || HandleSliderButton(ControllerButton.A, ControllerEventPhase.Pressed)) return;
        if (FocusedBinding() is { Identity.Kind: ViewNodeKind.Slider } slider && Eligible(slider))
        { _ = InvokeAsync(slider.Identity, slider.Token); return; }
        if (FocusedBinding() is { Element: ToggleSwitch toggle } toggleBinding && Eligible(toggleBinding))
        { toggle.IsOn = !toggle.IsOn; return; }
        if (FindIndexedCollection()?.ActivateFocused() == true) return;
        if (!applying && FocusedBinding() is { Element: Button { Command: { } command } button } binding
            && Eligible(binding) && command.CanExecute(button.CommandParameter)) command.Execute(button.CommandParameter);
    }

    // Busy is an action-admission state, not removal of the user's focus target.
    private bool Eligible(Binding binding) => Navigable(binding) && declarations[binding.Identity.Id].Node.IsBusy != true;
    private bool Navigable(Binding binding) => presentationActive && presentation?.IsCurrent == true && frame is not null && binding.Identity.Scope == activeScope
        && (declarations[binding.Identity.Id].Node.IsFocusable || binding.Element is WidgetIndexedCollectionView)
        && declarations[binding.Identity.Id].Node.IsDisabled != true
        && IsMemoryVisible(binding.Element);

    private Binding? FocusedBinding() => XamlRoot is null ? null : FindBinding(FocusManager.GetFocusedElement(XamlRoot) as DependencyObject);

    private Binding? FindBinding(DependencyObject? element)
    {
        for (var focused = element; focused is not null && !ReferenceEquals(focused, this);
            focused = VisualTreeHelper.GetParent(focused))
        {
            var id = focused is FrameworkElement { Tag: WidgetElementIdentity identity } ? identity.Id : string.Empty;
            if (bindings.TryGetValue(id, out var binding) && ReferenceEquals(binding.Element, focused)) return binding;
        }
        return null;
    }

    partial void TraceFocus(string phase);
    partial void ConfigureCollectionTrace(WidgetIndexedCollectionView collection);

    private void RememberFocus()
    {
        if (!applying && automaticFocusEnabled && presentationInputEnabled && FocusedBinding() is { } binding && Navigable(binding))
        {
            // A new native/pointer/automation focus choice after structural
            // reconciliation supersedes the pending low-priority restoration.
            pendingRestore = null;
            if (waitingEntry is not null) { needsEntry = false; ClearEntryLayoutWait(); }
            remembered.Remove(binding.Identity.Scope);
            remembered[binding.Identity.Scope] = binding.Identity;
            if (remembered.Count > MaximumMemoryEntries) remembered.Remove(remembered.Keys.First());
            // Native Focus() can settle before its routed GotFocus notification.
            // Capture the exact item when yielding input, not just its collection.
            if (binding.Element is WidgetIndexedCollectionView collection && collection.CaptureFocusedItem() is { } item)
                RememberCollectionFocus(binding.Identity, item);
            RememberGroupFocus(binding);
            UpdateNativeNeighbors();
            TraceFocus("remember-focus");
        }
        QueueMediaRefresh();
        QueueSurfaceUpdate();
    }

    private async Task InvokeAsync(WidgetElementIdentity identity, object token)
    {
        if (applying || presentationOnly || frame is null || !CanDispatchAction || !bindings.TryGetValue(identity.Id, out var binding)
            || binding.Identity != identity || !ReferenceEquals(binding.Token, token) || !Eligible(binding)
            || declarations[identity.Id].Node.ActionId is not { } action) return;
        CancelGroupEntry();
        var displayed = presentation!;
        try
        {
            await DispatchCapturedActionAsync(displayed, new(action, identity.Id, Sequence: ++actionSequence,
                MonotonicTimestampMicroseconds: Environment.TickCount64 * 1000, InputScopeId: displayed.Scope)
                { FocusedElementId = identity.Id });
        }
        catch (OperationCanceledException) when (disposed) { }
        catch (Exception error) { ReportFailure(error); }
    }

    private Binding Create(Declaration declaration, bool modalDialog)
    {
        var node = declaration.Node;
        var token = new object();
        Panel? children = null;
        FrameworkElement element;
        switch (node.Kind)
        {
            case ViewNodeKind.Stack:
            case ViewNodeKind.Row:
                element = children = modalDialog ? new WidgetModalPanel() : new Grid();
                break;
            case ViewNodeKind.ModalLayer:
                element = children = new WidgetModalLayer();
                break;
            case ViewNodeKind.Grid:
                element = children = node.GridLayout is null ? new WidgetResponsiveGrid() : new Grid();
                break;
            case ViewNodeKind.Icon:
                element = new WidgetPackageIconView();
                break;
            case ViewNodeKind.ControllerGlyph:
                element = new FontIcon();
                break;
            case ViewNodeKind.IndexedCollection:
                if (Session is null) throw new InvalidOperationException("Indexed widgets require a presentation session.");
                element = new WidgetIndexedCollectionView(Session, ReportFailure)
                { EnsureInteractionAsync = AdmitInteractionAsync, FocusRemembered = item =>
                    {
                        if (!applying && automaticFocusEnabled && presentationActive && presentationInputEnabled)
                            RememberCollectionFocus(declaration.Identity, item);
                    }, PresentationChanged = QueueSurfaceUpdate, ContextChanged = () => { ValidateTransientControl(); NotifyControllerGuideChanged(); } };
                ConfigureCollectionTrace((WidgetIndexedCollectionView)element);
                break;
            case ViewNodeKind.BackgroundSurface:
            case ViewNodeKind.FocusPresentationSurface:
                var surface = new WidgetPresentationSurface(node.Kind);
                element = surface;
                children = surface.ContentPanel;
                break;
            case ViewNodeKind.Scroll:
                children = new StackPanel { Spacing = 12 };
                element = new ScrollViewer { Content = children, HorizontalContentAlignment = HorizontalAlignment.Stretch };
                break;
            case ViewNodeKind.ActionSurface:
                children = node.ActionSurfacePresentation == ActionSurfacePresentation.Poster
                    ? new WidgetPosterPanel() : new Grid();
                // Worker admission owns action sequencing. A pending IPC response
                // must not suppress another deliberate press on an enabled control.
                element = presentationOnly ? children : new Button { Content = children, HorizontalContentAlignment = HorizontalAlignment.Stretch,
                    Command = new AsyncRelayCommand(() => InvokeAsync(declaration.Identity, token), AsyncRelayCommandOptions.AllowConcurrentExecutions) };
                break;
            case ViewNodeKind.TextEntry:
                element = presentationOnly ? new TextBlock() : CreateTextEntry(declaration.Identity, token);
                break;
            case ViewNodeKind.Select:
                element = presentationOnly ? new TextBlock() : CreateSelect(declaration.Identity, token);
                break;
            case ViewNodeKind.Button:
                element = presentationOnly ? new TextBlock { TextWrapping = TextWrapping.Wrap } :
                    IsSwitch(node) ? CreateSwitch(declaration.Identity, token) : new Button { Command = new AsyncRelayCommand(() => InvokeAsync(declaration.Identity, token), AsyncRelayCommandOptions.AllowConcurrentExecutions) };
                break;
            case ViewNodeKind.Slider: element = presentationOnly ? new TextBlock() : CreateSlider(declaration.Identity, token); break;
            case ViewNodeKind.Image: element = new WidgetArtworkView(); break;
            case ViewNodeKind.MediaViewport: element = new Media.WidgetMediaViewport(); break;
            case ViewNodeKind.WindowPreview: element = new Previews.WidgetWindowPreview(); break;
            case ViewNodeKind.Text: element = new TextBlock { TextWrapping = TextWrapping.Wrap }; break;
            case ViewNodeKind.Progress: element = new ProgressBar(); break;
            case ViewNodeKind.LoadingIndicator: element = new ProgressRing { IsActive = true }; break;
            case ViewNodeKind.Spacer: element = new Border(); break;
            default: throw new NotSupportedException($"WinUI presentation for {node.Kind} is not implemented.");
        }
        element.Tag = declaration.Identity;
        if (element is Button depthButton)
        {
            depthButton.Template = (ControlTemplate)Application.Current.Resources["WidgetDepthButtonTemplate"];
            depthButton.UseSystemFocusVisuals = true;
        }
        AutomationProperties.SetAutomationId(element, $"Widget.{node.Id}");
        var host = node.Transition is not null || element is Panel and not WidgetModalLayer || element is Border or WidgetPresentationSurface or Image
            ? new WidgetMotionHost(element, node.Transition?.Kind == WidgetTransitionKind.Selection, node.Transition is not null) { Tag = declaration.Identity } : null;
        return new(declaration.Identity, element, children, token, host);
    }

    private void Update(Binding binding, ViewNode node)
    {
        var element = binding.Element;
        if (element is WidgetIndexedCollectionView indexed)
        {
            if (!presentationActive) _ = indexed.SetPresentationActiveAsync(false);
            indexed.Apply(presentation!, node, binding.Identity.Scope);
        }
        if (element is WidgetArtworkView image) UpdateImage(binding, image, node);
        UpdateContainerLayout(binding, node);
        if (binding.Children is WidgetPosterPanel poster) UpdatePoster(poster, node);
        ApplySizeAndTypography(element, node);
        if (element is Media.WidgetMediaViewport viewport) UpdateMediaViewport(viewport, node, binding.Identity.Scope);
        if (element is Previews.WidgetWindowPreview preview) preview.Configure(presentationOnly || !presentationActive ? null : WindowPreviews, frame!, node);
        AutomationProperties.SetName(element, node.AccessibilityLabel ?? node.Text ?? node.Id);
        if (element is WidgetValueButton valueButton)
            valueButton.SetAccessibleValue(node.AccessibilityValue ?? (node.Kind == ViewNodeKind.Select
                ? node.SelectOptions.FirstOrDefault(option => option.IsSelected)?.Label : node.TextEntryValue),
                node.Kind == ViewNodeKind.TextEntry && node.TextEntryInputKind == TextEntryInputKind.Sensitive);
        if (element is FontIcon icon) WidgetGlyphs.Apply(icon, node, playStationPrompts);
        if (element is WidgetPackageIconView packageIcon) UpdateNativeIcon(packageIcon, node);
        if (element is TextBlock text) WidgetTextStyleAdapter.SetSource(text, node.Kind == ViewNodeKind.TextEntry ? TextEntryLabel(node) : node.Text ?? string.Empty);
        if (element is Control control)
        {
            control.IsEnabled = node.IsDisabled != true;
            // Selection is authored state, not a ToggleButton command. Preserve
            // native Invoke semantics while announcing both selection and work.
            AutomationProperties.SetItemStatus(control, WidgetAccessibleState.ItemStatus(element is ToggleSwitch ? node with { IsSelected = null } : node));
            control.IsTabStop = element is not WidgetIndexedCollectionView && node.IsFocusable && binding.Identity.Scope == activeScope;
            control.IsHitTestVisible = element is not (WidgetPackageIconView or Media.WidgetMediaViewport or Previews.WidgetWindowPreview) && (!node.IsFocusable || binding.Identity.Scope == activeScope);
        }
        if (element is ToggleSwitch toggle) toggles[toggle].Publish(node);
        if (element is Button button && node.Kind is ViewNodeKind.Button or ViewNodeKind.Select) UpdateButtonContent(binding, button, node);
        if (element is Button entry && node.Kind == ViewNodeKind.TextEntry) UpdateButtonLabel(entry, TextEntryLabel(node));
        if (element is ScrollViewer scroll)
        {
            var horizontal = node.ScrollAxis == ScrollAxis.Horizontal;
            scroll.HorizontalScrollMode = horizontal ? ScrollMode.Enabled : ScrollMode.Disabled;
            scroll.VerticalScrollMode = horizontal ? ScrollMode.Disabled : ScrollMode.Enabled;
            // Hidden suppresses a bar but still permits unbounded measurement
            // and focus-driven offsets on that axis. Disable the inactive axis
            // so native Grid stars/text wrapping receive a finite viewport.
            scroll.HorizontalScrollBarVisibility = !horizontal ? ScrollBarVisibility.Disabled :
                node.ShowScrollbar != false ? ScrollBarVisibility.Auto : ScrollBarVisibility.Hidden;
            scroll.VerticalScrollBarVisibility = horizontal ? ScrollBarVisibility.Disabled :
                node.ShowScrollbar != false ? ScrollBarVisibility.Auto : ScrollBarVisibility.Hidden;
            ((StackPanel)binding.Children!).Orientation = horizontal ? Orientation.Horizontal : Orientation.Vertical;
        }
        if (element is Slider slider) UpdateSlider(slider, node);
        if (element is ProgressBar progress)
        {
            progress.Minimum = node.Minimum ?? 0;
            progress.Maximum = node.Maximum ?? 100;
            progress.Value = node.Value ?? 0;
        }
        ApplyComputedStyles(binding, node);
        ApplyMotionGeometry(binding);
        if (binding.MotionHost?.SelectionSurface is not null)
            Canvas.SetZIndex(binding.LayoutElement, node.IsSelected is true ? -1 : 0);
    }

    private static Dictionary<string, Declaration> Plan(ViewNode root, string rootScope)
    {
        var result = new Dictionary<string, Declaration>(StringComparer.Ordinal);
        Visit(root, rootScope, null, "");
        return result;

        void Visit(ViewNode node, string inheritedScope, string? parent, string itemPath)
        {
            var unsupported = WinUiPresentationContract.ValidateNode(node, "$");
            if (unsupported.Count != 0)
            {
                // Build diagnostic paths only on failure, not on every rendered node.
                var indices = new Stack<int>();
                var child = node;
                for (var owner = parent; owner is not null; owner = result[owner].ParentId)
                {
                    var children = result[owner].Node.Children;
                    for (var index = 0; index < children.Count; ++index)
                        if (ReferenceEquals(children[index], child)) { indices.Push(index); break; }
                    child = result[owner].Node;
                }
                var declarationPath = "$.root" + string.Concat(indices.Select(index => $".children[{index}]"));
                throw new NotSupportedException(string.Join(Environment.NewLine, unsupported.Select(error =>
                    $"{declarationPath}{error.Path[1..]} ({error.Code}): {error.Message}")));
            }
            var scope = node.InputScopeId ?? inheritedScope;
            var path = node.CollectionItemKey is { } key ? itemPath + key.Length + ":" + key : itemPath;
            result.Add(node.Id, new(node, new(scope, node.Id, node.Kind, path), parent));
            foreach (var child in node.Children) Visit(child, scope, node.Id, path);
        }
    }

    private static bool SameOwner(WidgetPresentationAuthority first, WidgetPresentationAuthority second) =>
        first.WidgetId == second.WidgetId && first.RuntimeGeneration == second.RuntimeGeneration
        && first.PresentationGeneration == second.PresentationGeneration && first.SessionGeneration == second.SessionGeneration
        && first.WidgetInstanceId == second.WidgetInstanceId;
}
