using GameBarAlternative.WidgetProtocol;

namespace GameBarAlternative.WidgetSdk;

public sealed record WidgetView(
    WidgetElement Root,
    string? InitialFocusId = null,
    IReadOnlyList<WidgetQuickAction>? QuickActions = null,
    string? ActiveInputScopeId = null,
    WidgetSurfaceHints? Surface = null)
{
    public ViewSnapshot CreateSnapshot(string widgetInstanceId, long sequence)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(widgetInstanceId);
        if (sequence < 0) throw new ArgumentOutOfRangeException(nameof(sequence));

        var snapshot = new ViewSnapshot
        {
            ProtocolVersion = RequiredProtocolVersion(),
            Sequence = sequence,
            WidgetInstanceId = widgetInstanceId,
            ActiveInputScopeId = ActiveInputScopeId ?? RootScopeId(Root),
            InitialFocusId = InitialFocusId,
            QuickActions = QuickActions?.ToArray() ?? [],
            Surface = Surface,
            Root = Root.ToProtocolNode(),
        };
        var errors = ViewSnapshotValidator.Validate(snapshot);
        if (errors.Count != 0) throw new ProtocolValidationException(errors);
        return snapshot;

        int RequiredProtocolVersion()
        {
            var required = ProtocolConstants.BaselineVersion;
            if (Surface is not null || ContainsScroll(Root))
                required = Math.Max(required, ProtocolConstants.ScrollContainerVersion);
            if (ContainsSlider(Root))
                required = Math.Max(required, ProtocolConstants.SliderVersion);
            if (ContainsActivationRequiredSlider(Root))
                required = Math.Max(required, ProtocolConstants.SliderActivationVersion);
            if (QuickActions?.Any(action => action.Capability is not null) == true)
                required = Math.Max(required, ProtocolConstants.DashboardGestureAuthorityVersion);
            if (ContainsLoadingIndicator(Root))
                required = Math.Max(required, ProtocolConstants.LoadingIndicatorVersion);
            if (ContainsInlinePng(Root))
                required = Math.Max(required, ProtocolConstants.InlinePngImageVersion);
            if (ContainsActionSurface(Root))
                required = Math.Max(required, ProtocolConstants.ActionSurfaceVersion);
            if (ContainsGrid(Root))
                required = Math.Max(required, ProtocolConstants.ResponsiveGridVersion);
            if (ContainsResponsiveVisibility(Root))
                required = Math.Max(required, ProtocolConstants.ResponsiveVisibilityVersion);
            return required;
        }
    }

    private static bool ContainsResponsiveVisibility(WidgetElement element) => element switch
    {
        ResponsiveBranchElement => true,
        StackElement stack => stack.Children.Any(ContainsResponsiveVisibility),
        RowElement row => row.Children.Any(ContainsResponsiveVisibility),
        ScrollElement scroll => scroll.Children.Any(ContainsResponsiveVisibility),
        ActionSurfaceElement actionSurface => actionSurface.Children.Any(ContainsResponsiveVisibility),
        GridElement grid => grid.Children.Any(ContainsResponsiveVisibility),
        _ => false,
    };

    private static bool ContainsLoadingIndicator(WidgetElement element) => element switch
    {
        ResponsiveBranchElement branch => ContainsLoadingIndicator(branch.Child),
        LoadingIndicatorElement => true,
        StackElement stack => stack.Children.Any(ContainsLoadingIndicator),
        RowElement row => row.Children.Any(ContainsLoadingIndicator),
        ScrollElement scroll => scroll.Children.Any(ContainsLoadingIndicator),
        ActionSurfaceElement actionSurface => actionSurface.Children.Any(ContainsLoadingIndicator),
        GridElement grid => grid.Children.Any(ContainsLoadingIndicator),
        _ => false,
    };

    private static bool ContainsScroll(WidgetElement element) => element switch
    {
        ResponsiveBranchElement branch => ContainsScroll(branch.Child),
        ScrollElement => true,
        StackElement stack => stack.Children.Any(ContainsScroll),
        RowElement row => row.Children.Any(ContainsScroll),
        ActionSurfaceElement actionSurface => actionSurface.Children.Any(ContainsScroll),
        GridElement grid => grid.Children.Any(ContainsScroll),
        _ => false,
    };

    private static bool ContainsSlider(WidgetElement element) => element switch
    {
        ResponsiveBranchElement branch => ContainsSlider(branch.Child),
        SliderElement => true,
        ScrubberElement => true,
        StackElement stack => stack.Children.Any(ContainsSlider),
        RowElement row => row.Children.Any(ContainsSlider),
        ScrollElement scroll => scroll.Children.Any(ContainsSlider),
        ActionSurfaceElement actionSurface => actionSurface.Children.Any(ContainsSlider),
        GridElement grid => grid.Children.Any(ContainsSlider),
        _ => false,
    };

    private static bool ContainsActivationRequiredSlider(WidgetElement element) => element switch
    {
        ResponsiveBranchElement branch => ContainsActivationRequiredSlider(branch.Child),
        SliderElement slider =>
            slider.ControllerInteractionMode is SliderInteractionMode.ActivateToAdjust,
        ScrubberElement scrubber =>
            scrubber.ControllerInteractionMode is SliderInteractionMode.ActivateToAdjust,
        StackElement stack => stack.Children.Any(ContainsActivationRequiredSlider),
        RowElement row => row.Children.Any(ContainsActivationRequiredSlider),
        ScrollElement scroll => scroll.Children.Any(ContainsActivationRequiredSlider),
        ActionSurfaceElement actionSurface =>
            actionSurface.Children.Any(ContainsActivationRequiredSlider),
        GridElement grid => grid.Children.Any(ContainsActivationRequiredSlider),
        _ => false,
    };

    private static bool ContainsActionSurface(WidgetElement element) => element switch
    {
        ResponsiveBranchElement branch => ContainsActionSurface(branch.Child),
        ActionSurfaceElement => true,
        StackElement stack => stack.Children.Any(ContainsActionSurface),
        RowElement row => row.Children.Any(ContainsActionSurface),
        ScrollElement scroll => scroll.Children.Any(ContainsActionSurface),
        GridElement grid => grid.Children.Any(ContainsActionSurface),
        _ => false,
    };

    private static bool ContainsGrid(WidgetElement element) => element switch
    {
        ResponsiveBranchElement branch => ContainsGrid(branch.Child),
        GridElement => true,
        StackElement stack => stack.Children.Any(ContainsGrid),
        RowElement row => row.Children.Any(ContainsGrid),
        ScrollElement scroll => scroll.Children.Any(ContainsGrid),
        ActionSurfaceElement actionSurface => actionSurface.Children.Any(ContainsGrid),
        _ => false,
    };

    private static bool ContainsInlinePng(WidgetElement element) => element switch
    {
        ResponsiveBranchElement branch => ContainsInlinePng(branch.Child),
        ImageElement image when image.Source.StartsWith(
            "data:image/png;base64,", StringComparison.Ordinal) => true,
        ButtonElement button when button.LeadingImageSource?.StartsWith(
            "data:image/png;base64,", StringComparison.Ordinal) == true => true,
        StackElement stack => stack.Children.Any(ContainsInlinePng),
        RowElement row => row.Children.Any(ContainsInlinePng),
        ScrollElement scroll => scroll.Children.Any(ContainsInlinePng),
        ActionSurfaceElement actionSurface => actionSurface.Children.Any(ContainsInlinePng),
        GridElement grid => grid.Children.Any(ContainsInlinePng),
        _ => false,
    };

    private static string RootScopeId(WidgetElement root) => root switch
    {
        ResponsiveBranchElement branch => RootScopeId(branch.Child),
        StackElement stack => stack.InputScopeId ?? stack.Id,
        RowElement row => row.InputScopeId ?? row.Id,
        ScrollElement scroll => scroll.InputScopeId ?? scroll.Id,
        GridElement grid => grid.InputScopeId ?? grid.Id,
        _ => root.Id,
    };
}

public sealed record WidgetActionEvent(
    string ActionId,
    string SourceElementId,
    ControllerButton? ControllerButton = null,
    ControllerEventPhase Phase = ControllerEventPhase.Pressed,
    long Sequence = 0,
    long MonotonicTimestampMicroseconds = 0,
    double? RequestedValue = null,
    string? InputScopeId = null);

public enum ControllerInputContext
{
    DashboardQuickAction,
    OpenWidget,
}

/// <summary>
/// Raw semantic controller input routed to a widget. The host retains the
/// Guide button and dashboard navigation buttons rather than sending them.
/// </summary>
public sealed record ControllerInputEvent(
    ControllerButton Button,
    ControllerEventPhase Phase,
    ControllerInputContext Context,
    string? FocusedElementId = null,
    long Sequence = 0,
    long MonotonicTimestampMicroseconds = 0,
    string? ActiveInputScopeId = null,
    long SnapshotSequence = 0,
    double? RequestedValue = null);

public sealed record WidgetInvalidatedEventArgs(long Revision);

/// <summary>The runtime-managed lifecycle of a widget instance.</summary>
public enum WidgetLifecycleState
{
    /// <summary>
    /// The runtime has constructed the widget but has not completed
    /// <see cref="Widget.OnCreatedAsync"/>. This state is runtime-owned.
    /// </summary>
    Created,

    /// <summary>
    /// The worker is resident, but the widget is not presented. Process-lifetime
    /// work may continue; visible work must be stopped.
    /// </summary>
    Background,

    /// <summary>
    /// The widget is presented but does not own controller interaction.
    /// </summary>
    Visible,

    /// <summary>
    /// The widget is presented and is the active controller interaction target.
    /// </summary>
    Interactive,

    /// <summary>
    /// Terminal bounded cleanup. All widget-owned lifetime tokens are already
    /// canceled when this state is entered. This state is runtime-owned.
    /// </summary>
    Destroying,
}

public abstract partial class Widget
{
    private static readonly CancellationToken InactiveCancellationToken = new(canceled: true);
    private readonly SemaphoreSlim _lifecycleGate = new(1, 1);
    private readonly CancellationTokenSource _widgetLifetime = new();
    private long _revision;
    private int _isActive;
    private int _lifecycleState = (int)WidgetLifecycleState.Created;
    private int _created;
    private CancellationTokenSource? _activeLifetime;
    private CancellationTokenSource? _stateLifetime = new();
    private ViewSnapshot? _latestSnapshot;
    private WidgetHostServices _hostServices = WidgetHostServices.Unavailable;
    private int _hostServicesAttached;

    public event EventHandler<WidgetInvalidatedEventArgs>? Invalidated;

    public long Revision => Interlocked.Read(ref _revision);
    public bool IsActive => Volatile.Read(ref _isActive) != 0;

    /// <summary>
    /// Runtime-injected platform services. The capability client is unavailable
    /// in previews/tests unless the harness supplies a fake explicitly.
    /// </summary>
    protected WidgetHostServices HostServices => Volatile.Read(ref _hostServices);

    /// <summary>
    /// The current runtime lifecycle state. The runtime owns all transitions;
    /// widget implementations must observe this property rather than mutate state.
    /// </summary>
    protected WidgetLifecycleState LifecycleState =>
        (WidgetLifecycleState)Volatile.Read(ref _lifecycleState);

    /// <summary>
    /// Owned by the runtime and valid from Created until Destroying. Use it for
    /// lightweight process-lifetime work that may continue in Background.
    /// </summary>
    protected CancellationToken WidgetLifetimeToken => _widgetLifetime.Token;

    /// <summary>
    /// Owned by the runtime and canceled before every state exit. A new token is
    /// supplied for each stable state, including Visible to Interactive changes.
    /// </summary>
    protected CancellationToken StateLifetimeToken =>
        Volatile.Read(ref _stateLifetime)?.Token ?? InactiveCancellationToken;

    /// <summary>
    /// Owned by the runtime and canceled when the widget returns to Background.
    /// One token spans Visible and Interactive transitions. It is already
    /// canceled before first visibility and while in Background.
    /// </summary>
    protected CancellationToken ActiveCancellationToken =>
        Volatile.Read(ref _activeLifetime)?.Token ?? InactiveCancellationToken;

    public abstract WidgetView Render();

    internal void AttachHostServices(WidgetHostServices services)
    {
        ArgumentNullException.ThrowIfNull(services);
        if (Volatile.Read(ref _created) != 0 ||
            Interlocked.CompareExchange(ref _hostServicesAttached, 1, 0) != 0)
            throw new InvalidOperationException(
                "Widget host services must be attached exactly once before creation.");
        Volatile.Write(ref _hostServices, services);
    }

    /// <summary>
    /// Called exactly once while the widget is in Created. Start process-lifetime
    /// work using <paramref name="widgetLifetime"/> and return promptly.
    /// </summary>
    protected virtual ValueTask OnCreatedAsync(CancellationToken widgetLifetime) =>
        ValueTask.CompletedTask;

    /// <summary>
    /// Called after a stable-state transition. <paramref name="stateLifetime"/>
    /// belongs to <paramref name="current"/> and is canceled when that state is
    /// exited. Start state-scoped work and return promptly.
    /// </summary>
    protected virtual ValueTask OnLifecycleStateChangedAsync(
        WidgetLifecycleState previous,
        WidgetLifecycleState current,
        CancellationToken stateLifetime) => ValueTask.CompletedTask;

    /// <summary>
    /// Called during bounded runtime shutdown after all widget-owned lifetime
    /// tokens have already been canceled. Honor <paramref name="shutdownToken"/>
    /// and return promptly; the worker does not wait indefinitely for cleanup.
    /// </summary>
    protected virtual ValueTask OnDestroyingAsync(CancellationToken shutdownToken) =>
        ValueTask.CompletedTask;

    /// <summary>
    /// Called once per active lifetime. The token remains valid for the entire
    /// lifetime and is canceled before <see cref="OnDeactivatedAsync"/> runs.
    /// Implementations must start background work and return promptly.
    /// </summary>
    protected virtual ValueTask OnActivatedAsync(CancellationToken activeLifetime) =>
        ValueTask.CompletedTask;

    /// <summary>Called once after the active-lifetime token has been canceled.</summary>
    protected virtual ValueTask OnDeactivatedAsync(CancellationToken transitionToken) =>
        ValueTask.CompletedTask;

    /// <summary>Starts the runtime-owned Created to Background initialization.</summary>
    internal async ValueTask InitializeAsync(CancellationToken transitionToken)
    {
        await _lifecycleGate.WaitAsync(transitionToken).ConfigureAwait(false);
        try
        {
            await EnsureInitializedCoreAsync(transitionToken).ConfigureAwait(false);
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    /// <summary>Runtime lifecycle entrypoint. Widget code should use the protected hooks.</summary>
    internal async ValueTask SetLifecycleStateAsync(
        WidgetLifecycleState state,
        CancellationToken transitionToken)
    {
        ValidateHostState(state);
        await _lifecycleGate.WaitAsync(transitionToken).ConfigureAwait(false);
        try
        {
            await EnsureInitializedCoreAsync(transitionToken).ConfigureAwait(false);
            if (LifecycleState == state) return;
            await TransitionCoreAsync(state, transitionToken).ConfigureAwait(false);
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    /// <summary>Compatibility entrypoint for SDKs using the former active/inactive model.</summary>
    internal ValueTask SetActiveAsync(bool active, CancellationToken transitionToken) =>
        SetLifecycleStateAsync(active ? WidgetLifecycleState.Interactive : WidgetLifecycleState.Background,
            transitionToken);

    /// <summary>Enters the terminal runtime-owned Destroying state at most once.</summary>
    internal async ValueTask DestroyAsync(CancellationToken shutdownToken)
    {
        await _lifecycleGate.WaitAsync(shutdownToken).ConfigureAwait(false);
        try
        {
            if (LifecycleState == WidgetLifecycleState.Destroying) return;

            var stateLifetime = Interlocked.Exchange(ref _stateLifetime, null);
            var activeLifetime = Interlocked.Exchange(ref _activeLifetime, null);
            stateLifetime?.Cancel();
            activeLifetime?.Cancel();
            _widgetLifetime.Cancel();
            Volatile.Write(ref _isActive, 0);
            Volatile.Write(ref _lifecycleState, (int)WidgetLifecycleState.Destroying);

            try
            {
                if (activeLifetime is not null)
                    await OnDeactivatedAsync(shutdownToken).ConfigureAwait(false);
            }
            finally
            {
                try
                {
                    await OnDestroyingAsync(shutdownToken).ConfigureAwait(false);
                }
                finally
                {
                    stateLifetime?.Dispose();
                    activeLifetime?.Dispose();
                }
            }
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    private async ValueTask EnsureInitializedCoreAsync(CancellationToken transitionToken)
    {
        if (LifecycleState == WidgetLifecycleState.Destroying)
            throw new InvalidOperationException("A destroying widget cannot transition.");
        if (Interlocked.Exchange(ref _created, 1) != 0) return;

        await OnCreatedAsync(_widgetLifetime.Token).ConfigureAwait(false);
        await TransitionCoreAsync(WidgetLifecycleState.Background, transitionToken).ConfigureAwait(false);
    }

    private async ValueTask TransitionCoreAsync(
        WidgetLifecycleState current,
        CancellationToken transitionToken)
    {
        var previous = LifecycleState;
        if (previous == current) return;
        if (previous == WidgetLifecycleState.Destroying)
            throw new InvalidOperationException("A destroying widget cannot transition.");

        var wasVisible = IsVisible(previous);
        var isVisible = IsVisible(current);
        var previousStateLifetime = Interlocked.Exchange(ref _stateLifetime, null);
        previousStateLifetime?.Cancel();
        var currentStateLifetime = new CancellationTokenSource();
        Volatile.Write(ref _stateLifetime, currentStateLifetime);

        CancellationTokenSource? endedActiveLifetime = null;
        if (!wasVisible && isVisible)
        {
            var activeLifetime = new CancellationTokenSource();
            Volatile.Write(ref _activeLifetime, activeLifetime);
            Volatile.Write(ref _isActive, 1);
        }
        else if (wasVisible && !isVisible)
        {
            endedActiveLifetime = Interlocked.Exchange(ref _activeLifetime, null);
            Volatile.Write(ref _isActive, 0);
            endedActiveLifetime?.Cancel();
        }

        Volatile.Write(ref _lifecycleState, (int)current);
        try
        {
            await OnLifecycleStateChangedAsync(previous, current, currentStateLifetime.Token)
                .ConfigureAwait(false);
            if (!wasVisible && isVisible)
                await OnActivatedAsync(ActiveCancellationToken).ConfigureAwait(false);
            else if (wasVisible && !isVisible)
                await OnDeactivatedAsync(transitionToken).ConfigureAwait(false);
        }
        finally
        {
            previousStateLifetime?.Dispose();
            endedActiveLifetime?.Dispose();
        }
    }

    private static bool IsVisible(WidgetLifecycleState state) =>
        state is WidgetLifecycleState.Visible or WidgetLifecycleState.Interactive;

    private static void ValidateHostState(WidgetLifecycleState state)
    {
        if (state is not (WidgetLifecycleState.Background or
            WidgetLifecycleState.Visible or WidgetLifecycleState.Interactive))
            throw new ArgumentOutOfRangeException(nameof(state), state,
                "Hosts may request only Background, Visible, or Interactive.");
    }

    /// <summary>Renders and remembers the exact snapshot used for subsequent input resolution.</summary>
    public ViewSnapshot RenderSnapshot(string widgetInstanceId, long sequence)
    {
        var snapshot = Render().CreateSnapshot(widgetInstanceId, sequence);
        Volatile.Write(ref _latestSnapshot, snapshot);
        return snapshot;
    }

    public virtual ValueTask OnActionAsync(WidgetActionEvent action, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(action);
        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// Resolves raw input against the latest host-rendered snapshot. Override
    /// this for controls that are not represented by declarative shortcuts.
    /// </summary>
    public virtual ValueTask<bool> OnControllerInputAsync(
        ControllerInputEvent input,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        cancellationToken.ThrowIfCancellationRequested();
        var snapshot = Volatile.Read(ref _latestSnapshot);
        if (snapshot is null) return ValueTask.FromResult(false);

        if (input.Context == ControllerInputContext.DashboardQuickAction)
        {
            if (input.Phase != ControllerEventPhase.Pressed ||
                input.SnapshotSequence != snapshot.Sequence)
                return ValueTask.FromResult(false);
            var quickAction = snapshot.QuickActions.FirstOrDefault(action => action.Button == input.Button);
            if (quickAction is null) return ValueTask.FromResult(false);
            return ValueTask.FromResult(TryQueueControllerAction(new WidgetActionEvent(
                quickAction.ActionId,
                "dashboard-card",
                input.Button,
                input.Phase,
                input.Sequence,
                input.MonotonicTimestampMicroseconds),
                new WidgetCapabilityGestureContext(
                    input.Sequence, input.SnapshotSequence)));
        }

        if (input.Context == ControllerInputContext.OpenWidget)
        {
            if (input.SnapshotSequence != snapshot.Sequence ||
                !string.Equals(input.ActiveInputScopeId, snapshot.ActiveInputScopeId, StringComparison.Ordinal))
                return ValueTask.FromResult(false);
            var scopeRoot = FindInputScope(snapshot.Root, snapshot.ActiveInputScopeId, isRoot: true);
            if (scopeRoot is null) return ValueTask.FromResult(false);
            var focusedNode = input.FocusedElementId is { } focusedId
                ? FindNodeInScope(scopeRoot, focusedId, isScopeRoot: true)
                : null;
            if (input.FocusedElementId is not null && focusedNode is null) return ValueTask.FromResult(false);
            var focusedActionable = focusedNode is not null &&
                focusedNode.IsDisabled is not true && focusedNode.IsBusy is not true;
            if (focusedNode is { Kind: ViewNodeKind.Slider } slider &&
                input.Button is ControllerButton.DPadLeft or ControllerButton.DPadRight)
            {
                // Horizontal directions belong to a focused slider even at a
                // bound or while unavailable; they never leak into focus
                // navigation. The host supplies a quantized absolute target;
                // the SDK rejects stale snapshots before queueing it.
                if (input.Phase is not (ControllerEventPhase.Pressed or ControllerEventPhase.Repeated))
                    return ValueTask.FromResult(true);
                if (!focusedActionable) return ValueTask.FromResult(true);
                if (input.RequestedValue is not { } requested ||
                    slider.Minimum is not { } minimum || slider.Maximum is not { } maximum ||
                    slider.Step is not { } step ||
                    !SliderMath.IsValidRequestedValue(requested, minimum, maximum, step))
                    return ValueTask.FromResult(true);
                var sliderActionId = slider.ValueChangedActionId;
                if (string.IsNullOrWhiteSpace(sliderActionId)) return ValueTask.FromResult(true);
                return ValueTask.FromResult(TryQueueControllerAction(new WidgetActionEvent(
                    sliderActionId,
                    slider.Id,
                    input.Button,
                    input.Phase,
                    input.Sequence,
                    input.MonotonicTimestampMicroseconds,
                    requested,
                    snapshot.ActiveInputScopeId)));
            }
            if (input.Button == ControllerButton.A &&
                input.Phase == ControllerEventPhase.Pressed &&
                focusedActionable &&
                focusedNode is { Kind: ViewNodeKind.Button or ViewNodeKind.Slider or ViewNodeKind.ActionSurface,
                    ActionId: { Length: > 0 } actionId })
            {
                return ValueTask.FromResult(TryQueueControllerAction(new WidgetActionEvent(
                    actionId,
                    focusedNode.Id,
                    input.Button,
                    input.Phase,
                    input.Sequence,
                    input.MonotonicTimestampMicroseconds)));
            }

            // A always belongs to focused activation and never falls back to a
            // component shortcut elsewhere in the surface.
            if (input.Button == ControllerButton.A) return ValueTask.FromResult(false);

            var focusedShortcut = focusedNode?.Shortcuts.FirstOrDefault(candidate =>
                candidate.Button == input.Button && candidate.Phase == input.Phase);
            if (focusedShortcut is not null)
            {
                if (!focusedActionable) return ValueTask.FromResult(false);
                return ValueTask.FromResult(TryQueueControllerAction(new WidgetActionEvent(
                    focusedShortcut.ActionId,
                    focusedNode!.Id,
                    input.Button,
                    input.Phase,
                    input.Sequence,
                    input.MonotonicTimestampMicroseconds)));
            }

            var scopedShortcut = input.FocusedElementId is { } activeFocus
                ? FindNearestAncestorShortcutInScope(
                    scopeRoot, activeFocus, input.Button, input.Phase)
                : FindScopeRootShortcut(scopeRoot, input.Button, input.Phase);
            if (scopedShortcut is null) return ValueTask.FromResult(false);
            return ValueTask.FromResult(TryQueueControllerAction(new WidgetActionEvent(
                scopedShortcut.Value.Shortcut.ActionId,
                scopedShortcut.Value.Node.Id,
                input.Button,
                input.Phase,
                input.Sequence,
                input.MonotonicTimestampMicroseconds)));
        }

        return ValueTask.FromResult(false);
    }

    protected void Invalidate()
    {
        var revision = Interlocked.Increment(ref _revision);
        Invalidated?.Invoke(this, new WidgetInvalidatedEventArgs(revision));
    }

    private static ViewNode? FindInputScope(ViewNode node, string scopeId, bool isRoot)
    {
        if (isRoot || node.InputScopeId is not null)
        {
            var publicScopeId = node.InputScopeId ?? node.Id;
            if (string.Equals(publicScopeId, scopeId, StringComparison.Ordinal)) return node;
        }
        foreach (var child in node.Children)
        {
            var match = FindInputScope(child, scopeId, isRoot: false);
            if (match is not null) return match;
        }
        return null;
    }

    private static ViewNode? FindNodeInScope(ViewNode node, string id, bool isScopeRoot)
    {
        if (!isScopeRoot && node.InputScopeId is not null) return null;
        if (string.Equals(node.Id, id, StringComparison.Ordinal)) return node;
        foreach (var child in node.Children)
        {
            var match = FindNodeInScope(child, id, isScopeRoot: false);
            if (match is not null) return match;
        }
        return null;
    }

    private static (ViewNode Node, ControllerShortcut Shortcut)? FindScopeRootShortcut(
        ViewNode scopeRoot,
        ControllerButton button,
        ControllerEventPhase phase)
    {
        if (scopeRoot.IsDisabled is true || scopeRoot.IsBusy is true) return null;
        var shortcut = scopeRoot.Shortcuts.FirstOrDefault(candidate =>
            candidate.Button == button && candidate.Phase == phase);
        return shortcut is null ? null : (scopeRoot, shortcut);
    }

    private static (ViewNode Node, ControllerShortcut Shortcut)?
        FindNearestAncestorShortcutInScope(
            ViewNode scopeRoot,
            string focusedElementId,
            ControllerButton button,
            ControllerEventPhase phase)
    {
        var path = new List<ViewNode>();
        if (!FindPath(scopeRoot, focusedElementId, isScopeRoot: true, path)) return null;
        for (var index = path.Count - 2; index >= 0; index--)
        {
            var ancestor = path[index];
            var shortcut = ancestor.Shortcuts.FirstOrDefault(candidate =>
                candidate.Button == button && candidate.Phase == phase);
            if (shortcut is not null) return (ancestor, shortcut);
        }
        return null;

        static bool FindPath(
            ViewNode node,
            string targetId,
            bool isScopeRoot,
            List<ViewNode> path)
        {
            if (!isScopeRoot && node.InputScopeId is not null) return false;
            path.Add(node);
            if (string.Equals(node.Id, targetId, StringComparison.Ordinal)) return true;
            foreach (var child in node.Children)
            {
                if (FindPath(child, targetId, isScopeRoot: false, path)) return true;
            }
            path.RemoveAt(path.Count - 1);
            return false;
        }
    }
}
