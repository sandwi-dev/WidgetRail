using System.Text.Json.Serialization;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.WidgetSdk;

public sealed record WidgetView(
    WidgetElement Root,
    string? InitialFocusId = null,
    IReadOnlyList<WidgetQuickAction>? QuickActions = null,
    string? ActiveInputScopeId = null,
    WidgetSurfaceHints? Surface = null)
{
    /// <summary>
    /// Optional bounded size profiles or declarative projections for the
    /// host-owned pinned surface.
    /// This additive property deliberately preserves the positional constructor
    /// and Deconstruct contracts.
    /// </summary>
    public IReadOnlyList<PinnedPresentationLayout>? PinnedLayouts { get; init; }

    /// <summary>
    /// Optional host-owned embedded-media surface. This additive property
    /// preserves the established positional constructor and Deconstruct API.
    /// </summary>
    public EmbeddedMediaSurface? EmbeddedMedia { get; init; }

    /// <summary>
    /// Creates one bounded pinned layout. Omitting <paramref name="root"/>
    /// preserves the protocol-v20 size-profile behavior.
    /// </summary>
    public static PinnedPresentationLayout PinnedLayout(
        string id,
        string name,
        WidgetSurfaceHints surface,
        WidgetElement? root = null,
        string? initialFocusId = null,
        string? activeInputScopeId = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(surface);
        return new PinnedPresentationLayout
        {
            Id = id,
            Name = name,
            Surface = surface,
            Root = root?.ToProtocolNode(),
            ActiveInputScopeId = root is null
                ? null
                : activeInputScopeId ?? RootScopeId(root),
            InitialFocusId = root is null ? null : initialFocusId,
        };
    }

    public ViewSnapshot CreateSnapshot(string widgetInstanceId, long sequence)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(widgetInstanceId);
        if (sequence < 0) throw new ArgumentOutOfRangeException(nameof(sequence));

        var snapshot = new ViewSnapshot
        {
            ProtocolVersion = ProtocolConstants.BaselineVersion,
            Sequence = sequence,
            WidgetInstanceId = widgetInstanceId,
            ActiveInputScopeId = ActiveInputScopeId ?? RootScopeId(Root),
            InitialFocusId = InitialFocusId,
            QuickActions = QuickActions?.ToArray() ?? [],
            Surface = Surface,
            PinnedLayouts = PinnedLayouts?.ToArray() ?? [],
            EmbeddedMedia = EmbeddedMedia,
            Root = Root.ToProtocolNode(),
        };
        var requirements = ProtocolVersionRequirements.Calculate(snapshot);
        snapshot = snapshot with { ProtocolVersion = requirements.RequiredVersion };
        var errors = ViewSnapshotValidator.Validate(snapshot, requirements);
        if (errors.Count != 0) throw new ProtocolValidationException(errors);
        return snapshot;
    }

    private static string RootScopeId(WidgetElement root) => root switch
    {
        ResponsiveBranchElement branch => RootScopeId(branch.Child),
        ContainerElement container => container.InputScopeId ?? container.Id,
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
    string? InputScopeId = null)
{
    /// <summary>Bounded host-committed text. Raw/intermediate input is never exposed.</summary>
    public string? CommittedText { get; init; }
}

public enum ControllerInputContext
{
    DashboardQuickAction,
    OpenWidget,
    PinnedLayoutSelection,
    PinnedSurface,
    OverlayFullscreenPresentation,
}

/// <summary>
/// Identifies the trusted host ingress that produced semantic controller input.
/// Accessibility automation may invoke ordinary actions but is never proof of
/// a physical dashboard gesture for capability admission.
/// </summary>
public enum ControllerInputOrigin
{
    PhysicalController = 0,
    AccessibilityAutomation = 1,
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
    double? RequestedValue = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    ControllerInputOrigin Origin = ControllerInputOrigin.PhysicalController)
{
    /// <summary>
    /// The package-authored layout selected by the current host generation.
    /// Null with IsPinnedLayoutSelected false revokes prior selection demand.
    /// </summary>
    public string? PinnedLayoutId { get; init; }
    public bool? IsPinnedLayoutSelected { get; init; }
    /// <summary>Host-owned overlay fullscreen state; valid only for its notification context.</summary>
    public bool? IsOverlayFullscreenActive { get; init; }
}

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
    private readonly object _pinnedLayoutGate = new();
    private readonly Dictionary<string, PinnedLayoutHandle> _pinnedLayoutHandles =
        new(StringComparer.Ordinal);
    private string? _selectedPinnedLayoutId;
    private CancellationTokenSource? _pinnedLayoutSelectionLifetime;
    private readonly object _overlayFullscreenGate = new();
    private bool? _overlayFullscreenActive;

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

    /// <summary>
    /// Returns the exact seek step declared by the latest admitted media
    /// surface, or the protocol default when the declaration omits it.
    /// </summary>
    protected double CurrentMediaSeekStepSeconds =>
        Volatile.Read(ref _latestSnapshot)?.EmbeddedMedia?.MediaSeekStepSeconds ??
        ProtocolConstants.DefaultMediaSeekStepSeconds;

    /// <summary>
    /// Creates and uniquely registers one optional widget-instance handle for a
    /// stable authored pinned layout.
    /// The existing low-level pinned-layout callback remains supported.
    /// </summary>
    public PinnedLayoutHandle CreatePinnedLayoutHandle(
        string id,
        string name,
        WidgetSurfaceHints surface,
        string? initialFocusId = null,
        string? activeInputScopeId = null)
    {
        StableIdentifier.Validate(id, nameof(id));
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(surface);
        if (initialFocusId is not null)
            StableIdentifier.Validate(initialFocusId, nameof(initialFocusId));
        if (activeInputScopeId is not null)
            StableIdentifier.Validate(activeInputScopeId, nameof(activeInputScopeId));
        lock (_pinnedLayoutGate)
        {
            if (_pinnedLayoutHandles.ContainsKey(id))
                throw new InvalidOperationException(
                    $"Pinned layout handle '{id}' is already registered by this widget.");
            var handle = new PinnedLayoutHandle(
                id, name, surface, initialFocusId, activeInputScopeId);
            _pinnedLayoutHandles.Add(id, handle);
            return handle;
        }
    }

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
            EndPinnedLayoutSelection();
            Volatile.Write(ref _isActive, 0);
            Volatile.Write(ref _lifecycleState, (int)WidgetLifecycleState.Destroying);

            var endingLifetimes = new List<CancellationToken>(3);
            if (stateLifetime is not null) endingLifetimes.Add(stateLifetime.Token);
            if (activeLifetime is not null) endingLifetimes.Add(activeLifetime.Token);
            endingLifetimes.Add(_widgetLifetime.Token);
            await _operations.DrainLifetimesAsync(endingLifetimes, shutdownToken)
                .ConfigureAwait(false);
            await DrainTimedMutationsAsync(endingLifetimes, shutdownToken)
                .ConfigureAwait(false);
            DisposeTimedMutations();
            if (activeLifetime is not null)
                await DrainActionQueueAsync(activeLifetime.Token, shutdownToken)
                    .ConfigureAwait(false);

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
            var endingLifetimes = new List<CancellationToken>(2);
            if (previousStateLifetime is not null)
                endingLifetimes.Add(previousStateLifetime.Token);
            if (endedActiveLifetime is not null)
                endingLifetimes.Add(endedActiveLifetime.Token);
            await _operations.DrainLifetimesAsync(endingLifetimes, transitionToken)
                .ConfigureAwait(false);
            await DrainTimedMutationsAsync(endingLifetimes, transitionToken)
                .ConfigureAwait(false);
            if (endedActiveLifetime is not null)
                await DrainActionQueueAsync(endedActiveLifetime.Token, transitionToken)
                    .ConfigureAwait(false);
            await OnLifecycleStateChangedAsync(previous, current, currentStateLifetime.Token)
                .ConfigureAwait(false);
            if (!wasVisible && isVisible)
                await OnActivatedAsync(ActiveCancellationToken).ConfigureAwait(false);
            else if (wasVisible && !isVisible)
            {
                try
                {
                    await OnDeactivatedAsync(transitionToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException exception) when (
                    endedActiveLifetime is not null &&
                    endedActiveLifetime.IsCancellationRequested &&
                    !transitionToken.IsCancellationRequested &&
                    exception.CancellationToken == endedActiveLifetime.Token)
                {
                    // The host deliberately ended this exact active lifetime before
                    // invoking the deactivation hook. Awaiting work owned by that
                    // lifetime may therefore complete as cancelled. That is normal
                    // lifecycle completion, not cancellation of the host transition.
                }
            }
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

    internal WidgetPresentationPublication RenderPublication(
        string widgetInstanceId,
        string presentationGeneration,
        long sequence,
        long expectedBaseSequence,
        PresentationUpdateCapabilities capabilities,
        WidgetPresentationTransactionKind transactionKind,
        long recoveryOriginSequence = 0)
    {
        var previous = Volatile.Read(ref _latestSnapshot);
        var snapshot = Render().CreateSnapshot(widgetInstanceId, sequence);
        var publication = WidgetPresentationDiff.Create(
            previous,
            snapshot,
            presentationGeneration,
            expectedBaseSequence,
            capabilities,
            transactionKind,
            recoveryOriginSequence);
        Volatile.Write(ref _latestSnapshot, publication.Snapshot);
        return publication;
    }

    public virtual ValueTask OnActionAsync(WidgetActionEvent action, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(action);
        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// Resolves one exact artwork handle declared by the current snapshot.
    /// The runtime validates format, size, and current-handle authority before
    /// publishing the encoded bytes to the host.
    /// </summary>
    public virtual ValueTask<WidgetEncodedArtwork?> OnResolveArtworkAsync(
        WidgetArtworkHandle handle,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult<WidgetEncodedArtwork?>(null);
    }

    internal async ValueTask<WidgetEncodedArtwork?> ResolveArtworkAsync(
        string artworkHandle,
        CancellationToken cancellationToken)
    {
        StableIdentifier.Validate(artworkHandle, nameof(artworkHandle));
        var snapshot = Volatile.Read(ref _latestSnapshot);
        if (snapshot is null || !DeclaresArtwork(snapshot, artworkHandle))
            return null;
        var result = await OnResolveArtworkAsync(
            new WidgetArtworkHandle(artworkHandle), cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        if (!ReferenceEquals(snapshot, Volatile.Read(ref _latestSnapshot)) ||
            !DeclaresArtwork(snapshot, artworkHandle))
            return null;
        return result;
    }

    private static bool DeclaresArtwork(ViewSnapshot snapshot, string artworkHandle)
    {
        if (Contains(snapshot.Root)) return true;
        return snapshot.PinnedLayouts.Any(layout => layout.Root is not null && Contains(layout.Root));

        bool Contains(ViewNode node)
        {
            if (string.Equals(node.ArtworkHandle, artworkHandle, StringComparison.Ordinal) ||
                string.Equals(node.FocusBackgroundArtworkHandle, artworkHandle, StringComparison.Ordinal))
                return true;
            if (node.FocusPresentation is not null && Contains(node.FocusPresentation))
                return true;
            if (node.DefaultFocusPresentation is not null && Contains(node.DefaultFocusPresentation))
                return true;
            foreach (var child in node.Children)
                if (Contains(child)) return true;
            return false;
        }
    }

    /// <summary>
    /// Receives one validated playback observation from the exact current
    /// host-owned embedded-media session. The runtime republishes the widget
    /// after this callback completes, including for widgets that store the
    /// observation in an ordinary field. SDK state owners updated by this
    /// callback may request their own publication; do not add a manual
    /// <see cref="Invalidate"/> to compensate or batch those owner changes.
    /// </summary>
    public virtual ValueTask OnEmbeddedMediaPlaybackEventAsync(
        EmbeddedMediaPlaybackEvent playbackEvent,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(playbackEvent);
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.CompletedTask;
    }

    internal async ValueTask ApplyEmbeddedMediaPlaybackEventAsync(
        EmbeddedMediaPlaybackEvent playbackEvent,
        CancellationToken cancellationToken)
    {
        await OnEmbeddedMediaPlaybackEventAsync(playbackEvent, cancellationToken)
            .ConfigureAwait(false);
        // Compatibility publication for field-backed widgets. An SDK state
        // owner used by the callback publishes independently; the runtime
        // coalesces adjacent invalidation requests before rendering.
        Invalidate();
    }

    /// <summary>
    /// Observes demand for one package-authored pinned projection. The host
    /// revokes demand with <paramref name="layoutId"/> null. This notification
    /// grants no provider, capability, focus, or window authority.
    /// </summary>
    public virtual ValueTask OnPinnedLayoutSelectionChangedAsync(
        string? layoutId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// Observes the host-owned overlay fullscreen state. This notification
    /// grants no provider, capability, focus, window, or presentation authority.
    /// </summary>
    public virtual ValueTask OnOverlayFullscreenChangedAsync(
        bool isActive,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
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
        if (!Enum.IsDefined(input.Origin)) return ValueTask.FromResult(false);
        if (input.Context == ControllerInputContext.PinnedLayoutSelection)
        {
            if (input.IsPinnedLayoutSelected is null ||
                (input.IsPinnedLayoutSelected.Value &&
                 string.IsNullOrWhiteSpace(input.PinnedLayoutId)))
                return ValueTask.FromResult(false);
            return ObservePinnedLayoutSelectionAsync(input, cancellationToken);
        }
        if (input.Context == ControllerInputContext.OverlayFullscreenPresentation)
        {
            if (input.IsOverlayFullscreenActive is null ||
                input.IsPinnedLayoutSelected is not null || input.PinnedLayoutId is not null)
                return ValueTask.FromResult(false);
            return ObserveOverlayFullscreenAsync(
                input.IsOverlayFullscreenActive.Value, cancellationToken);
        }
        if (input.IsOverlayFullscreenActive is not null)
            return ValueTask.FromResult(false);
        var snapshot = Volatile.Read(ref _latestSnapshot);
        if (snapshot is null) return ValueTask.FromResult(false);

        if (input.Context == ControllerInputContext.DashboardQuickAction)
        {
            if (input.SnapshotSequence != snapshot.Sequence)
                return ValueTask.FromResult(false);
            var quickAction = snapshot.QuickActions.FirstOrDefault(action => action.Button == input.Button);
            if (quickAction is null ||
                (input.Phase == ControllerEventPhase.Repeated &&
                 quickAction.RepeatPolicy != ControllerActionRepeatPolicy.WhileHeld) ||
                input.Phase is not (ControllerEventPhase.Pressed or ControllerEventPhase.Repeated))
                return ValueTask.FromResult(false);
            var gestureContext = input.Origin == ControllerInputOrigin.PhysicalController
                ? new WidgetCapabilityGestureContext(input.Sequence, input.SnapshotSequence)
                : null;
            return ValueTask.FromResult(TryQueueControllerAction(new WidgetActionEvent(
                quickAction.ActionId,
                "dashboard-card",
                input.Button,
                input.Phase,
                input.Sequence,
                input.MonotonicTimestampMicroseconds),
                gestureContext));
        }

        if (input.Context is ControllerInputContext.OpenWidget or
            ControllerInputContext.PinnedSurface)
        {
            ViewNode inputRoot;
            string inputScopeId;
            if (input.Context == ControllerInputContext.OpenWidget)
            {
                inputRoot = snapshot.Root;
                inputScopeId = snapshot.ActiveInputScopeId;
            }
            else
            {
                if (string.IsNullOrWhiteSpace(input.PinnedLayoutId))
                    return ValueTask.FromResult(false);
                if (string.Equals(input.PinnedLayoutId, "host.full-widget", StringComparison.Ordinal))
                {
                    inputRoot = snapshot.Root;
                    inputScopeId = snapshot.ActiveInputScopeId;
                }
                else
                {
                    var selectedLayout = snapshot.PinnedLayouts.SingleOrDefault(layout =>
                        string.Equals(layout.Id, input.PinnedLayoutId, StringComparison.Ordinal));
                    if (selectedLayout?.Root is null ||
                        string.IsNullOrWhiteSpace(selectedLayout.ActiveInputScopeId))
                        return ValueTask.FromResult(false);
                    inputRoot = selectedLayout.Root;
                    inputScopeId = selectedLayout.ActiveInputScopeId;
                }
            }
            if (input.SnapshotSequence != snapshot.Sequence ||
                !string.Equals(input.ActiveInputScopeId, inputScopeId, StringComparison.Ordinal))
                return ValueTask.FromResult(false);
            var scopeRoot = FindInputScope(inputRoot, inputScopeId, isRoot: true);
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
                    inputScopeId)));
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
                    input.MonotonicTimestampMicroseconds,
                    InputScopeId: inputScopeId)));
            }

            // A always belongs to focused activation and never falls back to a
            // component shortcut elsewhere in the surface.
            if (input.Button == ControllerButton.A) return ValueTask.FromResult(false);

            var shortcutResolution = ControllerShortcutResolver.Resolve(
                scopeRoot, input.FocusedElementId, input.Button, input.Phase);
            if (shortcutResolution.Status != ControllerShortcutResolutionStatus.Resolved)
                return ValueTask.FromResult(false);
            return ValueTask.FromResult(TryQueueControllerAction(new WidgetActionEvent(
                shortcutResolution.Shortcut!.ActionId,
                shortcutResolution.Owner!.Id,
                input.Button,
                input.Phase,
                input.Sequence,
                input.MonotonicTimestampMicroseconds,
                InputScopeId: inputScopeId)));
        }

        return ValueTask.FromResult(false);
    }


    private async ValueTask<bool> ObservePinnedLayoutSelectionAsync(
        ControllerInputEvent input,
        CancellationToken cancellationToken)
    {
        var selectedId = input.IsPinnedLayoutSelected == true
            ? input.PinnedLayoutId
            : null;
        bool usesHandles;
        lock (_pinnedLayoutGate) usesHandles = _pinnedLayoutHandles.Count != 0;
        if (!usesHandles)
        {
            await OnPinnedLayoutSelectionChangedAsync(selectedId, cancellationToken)
                .ConfigureAwait(false);
            return true;
        }

        if (!IsCurrentPinnedLayoutNotification(input)) return false;
        if (!TryChangePinnedLayoutSelection(selectedId, out var priorLifetime)) return true;
        CancelAndDispose(priorLifetime);
        try
        {
            await OnPinnedLayoutSelectionChangedAsync(selectedId, cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            Invalidate();
        }
        return true;
    }

    private async ValueTask<bool> ObserveOverlayFullscreenAsync(
        bool isActive,
        CancellationToken cancellationToken)
    {
        bool? prior;
        lock (_overlayFullscreenGate)
        {
            if (_overlayFullscreenActive == isActive) return true;
            prior = _overlayFullscreenActive;
            _overlayFullscreenActive = isActive;
        }
        try
        {
            await OnOverlayFullscreenChangedAsync(isActive, cancellationToken)
                .ConfigureAwait(false);
        }
        catch
        {
            lock (_overlayFullscreenGate)
            {
                if (_overlayFullscreenActive == isActive)
                    _overlayFullscreenActive = prior;
            }
            throw;
        }
        return true;
    }

    private bool IsCurrentPinnedLayoutNotification(ControllerInputEvent input)
    {
        var snapshot = Volatile.Read(ref _latestSnapshot);
        if (snapshot is null || input.SnapshotSequence != snapshot.Sequence) return false;
        if (input.IsPinnedLayoutSelected == true)
            return snapshot.PinnedLayouts.Any(layout =>
                string.Equals(layout.Id, input.PinnedLayoutId, StringComparison.Ordinal));
        lock (_pinnedLayoutGate)
        {
            return _selectedPinnedLayoutId is not null &&
                (input.PinnedLayoutId is null || string.Equals(
                    input.PinnedLayoutId, _selectedPinnedLayoutId,
                    StringComparison.Ordinal));
        }
    }

    private bool TryChangePinnedLayoutSelection(
        string? selectedId,
        out CancellationTokenSource? priorLifetime)
    {
        lock (_pinnedLayoutGate)
        {
            priorLifetime = null;
            if (string.Equals(
                    _selectedPinnedLayoutId, selectedId, StringComparison.Ordinal))
                return false;

            priorLifetime = _pinnedLayoutSelectionLifetime;
            if (_selectedPinnedLayoutId is { } priorId &&
                _pinnedLayoutHandles.TryGetValue(priorId, out var priorHandle))
                priorHandle.SetSelection(false, default);

            _selectedPinnedLayoutId = selectedId;
            _pinnedLayoutSelectionLifetime = selectedId is null
                ? null
                : CancellationTokenSource.CreateLinkedTokenSource(_widgetLifetime.Token);
            if (selectedId is not null &&
                _pinnedLayoutHandles.TryGetValue(selectedId, out var selectedHandle))
                selectedHandle.SetSelection(
                    true, _pinnedLayoutSelectionLifetime!.Token);
            return true;
        }
    }

    private void EndPinnedLayoutSelection()
    {
        CancellationTokenSource? lifetime;
        lock (_pinnedLayoutGate)
        {
            lifetime = _pinnedLayoutSelectionLifetime;
            _pinnedLayoutSelectionLifetime = null;
            if (_selectedPinnedLayoutId is { } selectedId &&
                _pinnedLayoutHandles.TryGetValue(selectedId, out var handle))
                handle.SetSelection(false, default);
            _selectedPinnedLayoutId = null;
        }
        CancelAndDispose(lifetime);
    }

    private static void CancelAndDispose(CancellationTokenSource? lifetime)
    {
        if (lifetime is null) return;
        lifetime.Cancel();
        lifetime.Dispose();
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

}
