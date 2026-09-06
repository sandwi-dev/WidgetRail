using WidgetRail.WidgetProtocol;

namespace WidgetRail.WidgetSdk;

/// <summary>Result of a bounded navigation request.</summary>
public enum WidgetNavigationResult
{
    Changed,
    Unchanged,
    CannotGoBack,
    RejectedCapacity,
    RejectedConflict,
}

/// <summary>
/// Optional authored scope policy for a navigator. Omitting options preserves
/// the generated per-route scopes used by existing widgets.
/// </summary>
public sealed class WidgetNavigatorOptions<TRoute> where TRoute : notnull
{
    /// <summary>
    /// Stable scope shared only by routes listed in <see cref="RootRoutes"/>
    /// while they are selected as root frames.
    /// </summary>
    public string? SharedRootScopeId { get; init; }

    /// <summary>Routes that participate in the optional shared root scope.</summary>
    public IReadOnlyCollection<TRoute> RootRoutes { get; init; } = [];

    /// <summary>
    /// Optional stable scopes for routes that do not use the shared root scope.
    /// </summary>
    public IReadOnlyDictionary<TRoute, string> RouteScopeIds { get; init; } =
        new Dictionary<TRoute, string>();
}

/// <summary>
/// Immutable render-facing state for one controller-native navigation route.
/// </summary>
public sealed record WidgetNavigationSnapshot<TRoute>(
    TRoute Route,
    TRoute RootRoute,
    int Depth,
    string InputScopeId,
    string? InitialFocusId,
    string? BackActionId,
    long Revision,
    CancellationToken RouteCancellationToken) where TRoute : notnull
{
    public bool CanGoBack => Depth > 0;

    /// <summary>
    /// Lifetime of work owned by the selected root page. Unlike route work,
    /// this remains active while a nested route is pushed above that page.
    /// </summary>
    public CancellationToken RootRouteCancellationToken { get; init; }
}

/// <summary>
/// A bounded widget-local route stack with stable input scopes, return-focus
/// restoration, exact nested Back routing, and route-owned cancellation.
/// </summary>
public sealed class WidgetNavigator<TRoute> : IDisposable where TRoute : notnull
{
    public const int DefaultMaximumDepth = 8;
    public const int MaximumDepthLimit = 16;
    public const int DefaultMaximumRoutes = 32;
    public const int MaximumRoutesLimit = 32;

    private sealed record Frame(TRoute Route, string InputScopeId, string? ReturnFocusId);

    private enum RootFocusBehavior
    {
        RestoreRemembered,
        PreserveCurrent,
        RestoreRememberedOrDefault,
    }

    private readonly object _gate = new();
    private readonly string _id;
    private readonly string _backActionId;
    private readonly int _maximumDepth;
    private readonly int _maximumRoutes;
    private readonly IEqualityComparer<TRoute> _comparer;
    private readonly Dictionary<TRoute, string> _scopeIds;
    private readonly Dictionary<TRoute, string> _focusMemory;
    private readonly HashSet<TRoute> _knownRoutes;
    private readonly HashSet<TRoute> _rootRoutes;
    private readonly HashSet<string> _reservedScopeIds = new(StringComparer.Ordinal);
    private string? _sharedRootScopeId;
    private readonly bool _usesAuthoredScopePolicy;
    private readonly List<Frame> _stack = [];
    private readonly CancellationToken _widgetLifetime;
    private readonly Action _invalidate;
    private CancellationTokenRegistration _widgetLifetimeRegistration;
    private CancellationTokenSource _rootRouteLifetime;
    private CancellationTokenSource _routeLifetime;
    private WidgetNavigationSnapshot<TRoute> _snapshot;
    private int _nextScopeOrdinal;
    private bool _disposed;

    internal WidgetNavigator(
        string id,
        TRoute initialRoute,
        int maximumDepth,
        int maximumRoutes,
        IEqualityComparer<TRoute>? comparer,
        CancellationToken widgetLifetime,
        Action invalidate,
        WidgetNavigatorOptions<TRoute>? options = null)
    {
        StableIdentifier.Validate(id, nameof(id));
        ArgumentNullException.ThrowIfNull(initialRoute);
        if (maximumDepth is < 1 or > MaximumDepthLimit)
            throw new ArgumentOutOfRangeException(nameof(maximumDepth));
        if (maximumRoutes is < 1 or > MaximumRoutesLimit)
            throw new ArgumentOutOfRangeException(nameof(maximumRoutes));
        ArgumentNullException.ThrowIfNull(invalidate);

        _id = id;
        _backActionId = StableIdentifier.Child(id, "back");
        _ = StableIdentifier.Child(id, $"scope-{maximumRoutes - 1}");
        _maximumDepth = maximumDepth;
        _maximumRoutes = maximumRoutes;
        _comparer = comparer ?? EqualityComparer<TRoute>.Default;
        _scopeIds = new(_comparer);
        _focusMemory = new(_comparer);
        _knownRoutes = new(_comparer);
        _rootRoutes = new(_comparer);
        _widgetLifetime = widgetLifetime;
        _invalidate = invalidate;
        _usesAuthoredScopePolicy = options is not null;
        CopyAndValidateOptions(options);
        _knownRoutes.Add(initialRoute);
        if (_knownRoutes.Count > maximumRoutes)
            throw new ArgumentOutOfRangeException(nameof(options),
                "Authored routes exceed the navigator route bound.");
        _rootRouteLifetime = CancellationTokenSource.CreateLinkedTokenSource(widgetLifetime);
        _routeLifetime = CancellationTokenSource.CreateLinkedTokenSource(widgetLifetime);

        if (!TryGetOrCreateScopeId(initialRoute, rootFrame: true, out var scopeId))
            throw new ArgumentOutOfRangeException(nameof(options));
        _stack.Add(new(initialRoute, scopeId, null));
        _snapshot = CreateSnapshot(null, 0);
        _widgetLifetimeRegistration = widgetLifetime.UnsafeRegister(
            static state => ((WidgetNavigator<TRoute>)state!).OwnerCanceled(), this);
    }

    /// <summary>Current immutable route state for rendering and route-owned work.</summary>
    public WidgetNavigationSnapshot<TRoute> Value
    {
        get { lock (_gate) return _snapshot; }
    }

    /// <summary>
    /// Selects a root destination and clears nested history. The supplied focus
    /// belongs to the route being left; revisiting that route restores it.
    /// </summary>
    public WidgetNavigationResult Navigate(TRoute route, string? sourceFocusId = null)
    {
        ArgumentNullException.ThrowIfNull(route);
        ValidateOptionalFocus(sourceFocusId);
        return ReplaceRoot(
            route,
            RootFocusBehavior.RestoreRemembered,
            sourceFocusId,
            defaultFocusId: null);
    }

    /// <summary>
    /// Selects a root destination without publishing a focus override. This is
    /// intended for persistent navigation controls whose logical focus should
    /// remain selected while sibling page content changes.
    /// </summary>
    public WidgetNavigationResult NavigateRoot(TRoute route)
    {
        ArgumentNullException.ThrowIfNull(route);
        return ReplaceRoot(
            route,
            RootFocusBehavior.PreserveCurrent,
            sourceFocusId: null,
            defaultFocusId: null);
    }

    /// <summary>
    /// Selects a sibling root destination, remembers an optional content focus
    /// for the page being left, and enters the destination's remembered focus
    /// or its authored focusable fallback.
    /// </summary>
    public WidgetNavigationResult SwitchRoot(
        TRoute route,
        string defaultFocusId,
        string? sourceContentFocusId = null)
    {
        ArgumentNullException.ThrowIfNull(route);
        StableIdentifier.Validate(defaultFocusId, nameof(defaultFocusId));
        ValidateOptionalFocus(sourceContentFocusId);
        return ReplaceRoot(
            route,
            RootFocusBehavior.RestoreRememberedOrDefault,
            sourceContentFocusId,
            defaultFocusId);
    }

    private WidgetNavigationResult ReplaceRoot(
        TRoute route,
        RootFocusBehavior focusBehavior,
        string? sourceFocusId,
        string? defaultFocusId)
    {
        RetiredLifetimes retired = default;
        lock (_gate)
        {
            ThrowIfDisposed();
            var current = _stack[^1];
            var root = _stack[0];
            if (_stack.Count == 1 && _comparer.Equals(root.Route, route))
            {
                if (focusBehavior != RootFocusBehavior.PreserveCurrent)
                    RememberFocus(current.Route, sourceFocusId);
                return WidgetNavigationResult.Unchanged;
            }
            if (!TryGetOrCreateScopeId(route, rootFrame: true, out var scopeId))
                return WidgetNavigationResult.RejectedCapacity;

            if (focusBehavior != RootFocusBehavior.PreserveCurrent)
                RememberFocus(current.Route, sourceFocusId);
            var rootChanged = !_comparer.Equals(root.Route, route);
            _stack.Clear();
            _stack.Add(new(route, scopeId, null));
            string? restoredFocus = null;
            if (focusBehavior != RootFocusBehavior.PreserveCurrent)
            {
                _focusMemory.TryGetValue(route, out restoredFocus);
                if (restoredFocus is null &&
                    focusBehavior == RootFocusBehavior.RestoreRememberedOrDefault)
                    restoredFocus = defaultFocusId;
            }
            retired = Advance(restoredFocus, rootChanged);
        }
        Publish(retired);
        return WidgetNavigationResult.Changed;
    }

    /// <summary>
    /// Pushes a nested route. Back restores <paramref name="sourceFocusId"/>
    /// in the parent route rather than guessing from rendered geometry.
    /// </summary>
    public WidgetNavigationResult Push(TRoute route, string? sourceFocusId = null)
    {
        ArgumentNullException.ThrowIfNull(route);
        ValidateOptionalFocus(sourceFocusId);
        RetiredLifetimes retired = default;
        lock (_gate)
        {
            ThrowIfDisposed();
            if (_stack.Count - 1 >= _maximumDepth)
                return WidgetNavigationResult.RejectedCapacity;
            if (!TryGetOrCreateScopeId(route, rootFrame: false, out var scopeId))
                return WidgetNavigationResult.RejectedCapacity;
            if (_usesAuthoredScopePolicy && _stack.Any(frame =>
                    string.Equals(frame.InputScopeId, scopeId, StringComparison.Ordinal)))
                return WidgetNavigationResult.RejectedConflict;

            RememberFocus(_stack[^1].Route, sourceFocusId);
            _stack.Add(new(route, scopeId, sourceFocusId));
            _focusMemory.TryGetValue(route, out var restoredFocus);
            retired = Advance(restoredFocus, replaceRootLifetime: false);
        }
        Publish(retired);
        return WidgetNavigationResult.Changed;
    }

    /// <summary>
    /// Pops one nested route. The optional focus belongs to the route being
    /// left and is remembered if that route is opened again.
    /// </summary>
    public WidgetNavigationResult Back(string? sourceFocusId = null)
    {
        ValidateOptionalFocus(sourceFocusId);
        RetiredLifetimes retired = default;
        lock (_gate)
        {
            ThrowIfDisposed();
            if (_stack.Count == 1) return WidgetNavigationResult.CannotGoBack;

            var removed = _stack[^1];
            RememberFocus(removed.Route, sourceFocusId);
            _stack.RemoveAt(_stack.Count - 1);
            retired = Advance(removed.ReturnFocusId, replaceRootLifetime: false);
        }
        Publish(retired);
        return WidgetNavigationResult.Changed;
    }

    /// <summary>
    /// Handles only the navigator's exact pressed Back action in the exact
    /// current nested input scope. Stale actions fail closed.
    /// </summary>
    public bool TryHandleBack(WidgetActionEvent action, string? sourceFocusId = null)
    {
        ArgumentNullException.ThrowIfNull(action);
        ValidateOptionalFocus(sourceFocusId);
        RetiredLifetimes retired = default;
        lock (_gate)
        {
            ThrowIfDisposed();
            if (_stack.Count == 1 || action.Phase != ControllerEventPhase.Pressed ||
                !string.Equals(action.ActionId, _backActionId, StringComparison.Ordinal) ||
                !string.Equals(action.InputScopeId, _snapshot.InputScopeId,
                    StringComparison.Ordinal))
                return false;

            var removed = _stack[^1];
            RememberFocus(removed.Route, sourceFocusId);
            _stack.RemoveAt(_stack.Count - 1);
            retired = Advance(removed.ReturnFocusId, replaceRootLifetime: false);
        }
        Publish(retired);
        return true;
    }

    /// <summary>
    /// Pushes a nested route and captures the element that actually held focus
    /// when a controller action was dispatched. The action declaration owner
    /// remains available separately through <see cref="WidgetActionEvent.SourceElementId"/>.
    /// </summary>
    public WidgetNavigationResult PushFromAction(
        TRoute route,
        WidgetActionEvent action,
        string? returnFocusOverrideId = null)
    {
        ArgumentNullException.ThrowIfNull(action);
        var returnFocusId = returnFocusOverrideId ?? action.FocusedElementId;
        if (returnFocusId is null)
            throw new ArgumentException(
                "Action-aware navigation requires an actual focused element or an explicit return-focus override.",
                nameof(action));
        return Push(route, returnFocusId);
    }

    /// <summary>
    /// Applies the current stable input scope and publishes B only for a nested
    /// route. Scope accepts any declarative container while preserving its
    /// concrete immutable container type at runtime. Use the returned scope and
    /// <see cref="Value"/> together when constructing a <see cref="WidgetView"/>.
    /// </summary>
    public ContainerElement Scope(ContainerElement root) => Scope(Value, root);

    public ContainerElement Scope(
        WidgetNavigationSnapshot<TRoute> snapshot,
        ContainerElement root) => ApplyScope(snapshot, root);

    /// <summary>
    /// Validates a dynamically supplied page root before applying navigator
    /// scope ownership. Only layout containers can own a scope or Back shortcut.
    /// </summary>
    public ContainerElement Scope(WidgetElement root) => Scope(Value, root);

    public ContainerElement Scope(
        WidgetNavigationSnapshot<TRoute> snapshot,
        WidgetElement root)
    {
        ArgumentNullException.ThrowIfNull(root);
        if (root is not ContainerElement container)
            throw new ArgumentException(
                "A WidgetNavigator scope root must be a ContainerElement; leaf controls cannot own an input scope or Back shortcut.",
                nameof(root));
        return Scope(snapshot, container);
    }

    // Keep concrete overloads for source and binary compatibility. Every
    // container type delegates to the one shared ContainerElement owner above.
    public StackElement Scope(StackElement root) => Scope(Value, root);

    public StackElement Scope(
        WidgetNavigationSnapshot<TRoute> snapshot,
        StackElement root) => (StackElement)Scope(snapshot, (ContainerElement)root);

    public RowElement Scope(RowElement root) => Scope(Value, root);

    public RowElement Scope(
        WidgetNavigationSnapshot<TRoute> snapshot,
        RowElement root) => (RowElement)Scope(snapshot, (ContainerElement)root);

    public ScrollElement Scope(ScrollElement root) => Scope(Value, root);

    public ScrollElement Scope(
        WidgetNavigationSnapshot<TRoute> snapshot,
        ScrollElement root) => (ScrollElement)Scope(snapshot, (ContainerElement)root);

    public GridElement Scope(GridElement root) => Scope(Value, root);

    public GridElement Scope(
        WidgetNavigationSnapshot<TRoute> snapshot,
        GridElement root) => (GridElement)Scope(snapshot, (ContainerElement)root);

    public void Dispose()
    {
        CancellationTokenSource? routeLifetime;
        CancellationTokenSource? rootLifetime;
        CancellationTokenRegistration registration;
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            routeLifetime = _routeLifetime;
            rootLifetime = _rootRouteLifetime;
            registration = _widgetLifetimeRegistration;
            _widgetLifetimeRegistration = default;
        }
        registration.Dispose();
        CancelAndDisposeAll(new RetiredLifetimes(routeLifetime, rootLifetime));
    }

    private ContainerElement ApplyScope(
        WidgetNavigationSnapshot<TRoute> snapshot,
        ContainerElement root)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(root);
        var existingScope = root.InputScopeId;
        var existing = root.Shortcuts;
        lock (_gate)
        {
            ThrowIfDisposed();
            if (!_scopeIds.Values.Contains(snapshot.InputScopeId, StringComparer.Ordinal) ||
                snapshot.BackActionId is not null &&
                !string.Equals(snapshot.BackActionId, _backActionId, StringComparison.Ordinal))
                throw new ArgumentException(
                    "The navigation snapshot does not belong to this navigator.",
                    nameof(snapshot));
        }

        if (existingScope is not null &&
            !string.Equals(existingScope, snapshot.InputScopeId, StringComparison.Ordinal))
            throw new InvalidOperationException(
                "The route root already owns a conflicting input scope.");

        // A widget can retain and reuse an immutable scoped root across a pop.
        // Strip only this navigator's generated shortcut before rebuilding the
        // current route contract so Back never leaks onto the root route.
        IReadOnlyList<ControllerShortcut> shortcuts = existing
            .Where(item => !(item.Button == ControllerButton.B &&
                item.Phase == ControllerEventPhase.Pressed &&
                string.Equals(item.ActionId, _backActionId, StringComparison.Ordinal)))
            .ToArray();
        if (snapshot.BackActionId is not null)
        {
            var back = shortcuts.FirstOrDefault(item =>
                item.Button == ControllerButton.B &&
                item.Phase == ControllerEventPhase.Pressed);
            if (back is not null &&
                !string.Equals(back.ActionId, snapshot.BackActionId,
                    StringComparison.Ordinal))
                throw new InvalidOperationException(
                    "The route root already owns a conflicting pressed B shortcut.");
            if (back is null)
                shortcuts = [.. shortcuts, new ControllerShortcut(
                    ControllerButton.B, snapshot.BackActionId)];
        }
        return root with
        {
            InputScopeId = snapshot.InputScopeId,
            Shortcuts = shortcuts,
        };
    }

    private readonly record struct RetiredLifetimes(
        CancellationTokenSource? Route,
        CancellationTokenSource? Root);

    private RetiredLifetimes Advance(
        string? restoredFocus,
        bool replaceRootLifetime)
    {
        var previousRoute = _routeLifetime;
        CancellationTokenSource? previousRoot = null;
        if (replaceRootLifetime)
        {
            previousRoot = _rootRouteLifetime;
            _rootRouteLifetime = CancellationTokenSource.CreateLinkedTokenSource(_widgetLifetime);
        }
        _routeLifetime = CancellationTokenSource.CreateLinkedTokenSource(_widgetLifetime);
        var revision = checked(_snapshot.Revision + 1);
        _snapshot = CreateSnapshot(restoredFocus, revision);
        return new(previousRoute, previousRoot);
    }

    private WidgetNavigationSnapshot<TRoute> CreateSnapshot(
        string? initialFocusId,
        long revision)
    {
        var current = _stack[^1];
        return new(
            current.Route,
            _stack[0].Route,
            _stack.Count - 1,
            current.InputScopeId,
            initialFocusId,
            _stack.Count > 1 ? _backActionId : null,
            revision,
            _routeLifetime.Token)
        {
            RootRouteCancellationToken = _rootRouteLifetime.Token,
        };
    }

    private bool TryGetOrCreateScopeId(
        TRoute route,
        bool rootFrame,
        out string scopeId)
    {
        if (rootFrame && _sharedRootScopeId is not null && _rootRoutes.Contains(route))
        {
            scopeId = _sharedRootScopeId;
            return true;
        }
        if (_scopeIds.TryGetValue(route, out scopeId!)) return true;
        if (!_knownRoutes.Contains(route) && _knownRoutes.Count >= _maximumRoutes)
        {
            scopeId = string.Empty;
            return false;
        }
        _knownRoutes.Add(route);
        scopeId = CreateScopeId(route);
        return true;
    }

    private string CreateScopeId(TRoute route)
    {
        string scopeId;
        do
        {
            scopeId = StableIdentifier.Child(_id, $"scope-{_nextScopeOrdinal++}");
        }
        while (_reservedScopeIds.Contains(scopeId));
        _scopeIds.Add(route, scopeId);
        _reservedScopeIds.Add(scopeId);
        return scopeId;
    }

    private void CopyAndValidateOptions(WidgetNavigatorOptions<TRoute>? options)
    {
        if (options is null) return;
        ArgumentNullException.ThrowIfNull(options.RootRoutes);
        ArgumentNullException.ThrowIfNull(options.RouteScopeIds);

        if (options.SharedRootScopeId is not null)
        {
            StableIdentifier.Validate(
                options.SharedRootScopeId, nameof(options.SharedRootScopeId));
            _sharedRootScopeId = options.SharedRootScopeId;
            _reservedScopeIds.Add(options.SharedRootScopeId);
        }

        foreach (var route in options.RootRoutes)
        {
            ArgumentNullException.ThrowIfNull(route);
            _rootRoutes.Add(route);
            _knownRoutes.Add(route);
        }
        if (_sharedRootScopeId is not null && _rootRoutes.Count == 0)
            throw new ArgumentException(
                "A shared root scope requires at least one authored root route.",
                nameof(options));
        if (_sharedRootScopeId is null && _rootRoutes.Count != 0)
            throw new ArgumentException(
                "Authored root routes require a shared root scope.",
                nameof(options));

        foreach (var (route, scopeId) in options.RouteScopeIds)
        {
            ArgumentNullException.ThrowIfNull(route);
            StableIdentifier.Validate(scopeId, nameof(options.RouteScopeIds));
            if (_sharedRootScopeId is not null && _rootRoutes.Contains(route))
                throw new ArgumentException(
                    "A shared root route cannot also declare a route scope.",
                    nameof(options));
            if (!_reservedScopeIds.Add(scopeId))
                throw new ArgumentException(
                    "Navigator scope IDs must be unique.", nameof(options));
            _scopeIds.Add(route, scopeId);
            _knownRoutes.Add(route);
        }
    }

    private void RememberFocus(TRoute route, string? focusId)
    {
        if (focusId is not null) _focusMemory[route] = focusId;
    }

    private void Publish(RetiredLifetimes retired)
    {
        try
        {
            CancelAndDisposeAll(retired);
        }
        finally
        {
            // The route snapshot has already changed. Even a faulty author
            // cancellation callback must not leave the renderer on stale UI.
            _invalidate();
        }
    }

    private void OwnerCanceled()
    {
        CancellationTokenSource? routeLifetime;
        CancellationTokenSource? rootLifetime;
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            routeLifetime = _routeLifetime;
            rootLifetime = _rootRouteLifetime;
        }
        CancelAndDisposeAll(new RetiredLifetimes(routeLifetime, rootLifetime));
    }

    private static void CancelAndDisposeAll(RetiredLifetimes lifetimes)
    {
        try
        {
            if (lifetimes.Route is not null) CancelAndDispose(lifetimes.Route);
        }
        finally
        {
            if (lifetimes.Root is not null) CancelAndDispose(lifetimes.Root);
        }
    }

    private static void CancelAndDispose(CancellationTokenSource lifetime)
    {
        try { lifetime.Cancel(); }
        catch (ObjectDisposedException) { }
        finally { lifetime.Dispose(); }
    }

    private static void ValidateOptionalFocus(string? focusId)
    {
        if (focusId is not null) StableIdentifier.Validate(focusId, nameof(focusId));
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }
}

public abstract partial class Widget
{
    /// <summary>
    /// Creates a runtime-integrated bounded navigator. Route changes invalidate
    /// once; current route work is canceled before that invalidation publishes.
    /// </summary>
    protected WidgetNavigator<TRoute> CreateNavigator<TRoute>(
        string id,
        TRoute initialRoute,
        int maximumDepth = WidgetNavigator<TRoute>.DefaultMaximumDepth,
        int maximumRoutes = WidgetNavigator<TRoute>.DefaultMaximumRoutes,
        IEqualityComparer<TRoute>? comparer = null) where TRoute : notnull =>
        new(id, initialRoute, maximumDepth, maximumRoutes, comparer,
            WidgetLifetimeToken, () =>
            {
                if (LifecycleState != WidgetLifecycleState.Destroying) Invalidate();
            });

    /// <summary>
    /// Creates a navigator with an optional shared root scope and authored
    /// route scopes. Existing widgets can continue using
    /// <see cref="CreateNavigator{TRoute}(string, TRoute, int, int, IEqualityComparer{TRoute}?)"/>
    /// with unchanged generated-scope behavior.
    /// </summary>
    protected WidgetNavigator<TRoute> CreateNavigatorWithOptions<TRoute>(
        string id,
        TRoute initialRoute,
        WidgetNavigatorOptions<TRoute> options,
        int maximumDepth = WidgetNavigator<TRoute>.DefaultMaximumDepth,
        int maximumRoutes = WidgetNavigator<TRoute>.DefaultMaximumRoutes,
        IEqualityComparer<TRoute>? comparer = null) where TRoute : notnull
    {
        ArgumentNullException.ThrowIfNull(options);
        return new(id, initialRoute, maximumDepth, maximumRoutes, comparer,
            WidgetLifetimeToken, () =>
            {
                if (LifecycleState != WidgetLifecycleState.Destroying) Invalidate();
            }, options);
    }
}
