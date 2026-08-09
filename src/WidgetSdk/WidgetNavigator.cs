using GameBarAlternative.WidgetProtocol;

namespace GameBarAlternative.WidgetSdk;

/// <summary>Result of a bounded navigation request.</summary>
public enum WidgetNavigationResult
{
    Changed,
    Unchanged,
    CannotGoBack,
    RejectedCapacity,
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

    private readonly object _gate = new();
    private readonly string _id;
    private readonly string _backActionId;
    private readonly int _maximumDepth;
    private readonly int _maximumRoutes;
    private readonly IEqualityComparer<TRoute> _comparer;
    private readonly Dictionary<TRoute, string> _scopeIds;
    private readonly Dictionary<TRoute, string> _focusMemory;
    private readonly List<Frame> _stack = [];
    private readonly CancellationToken _widgetLifetime;
    private readonly Action _invalidate;
    private CancellationTokenRegistration _widgetLifetimeRegistration;
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
        Action invalidate)
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
        _widgetLifetime = widgetLifetime;
        _invalidate = invalidate;
        _routeLifetime = CancellationTokenSource.CreateLinkedTokenSource(widgetLifetime);

        var scopeId = CreateScopeId(initialRoute);
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
        CancellationTokenSource? previousLifetime = null;
        lock (_gate)
        {
            ThrowIfDisposed();
            var current = _stack[^1];
            var root = _stack[0];
            if (_stack.Count == 1 && _comparer.Equals(root.Route, route))
            {
                RememberFocus(current.Route, sourceFocusId);
                return WidgetNavigationResult.Unchanged;
            }
            if (!TryGetOrCreateScopeId(route, out var scopeId))
                return WidgetNavigationResult.RejectedCapacity;

            RememberFocus(current.Route, sourceFocusId);
            _stack.Clear();
            _stack.Add(new(route, scopeId, null));
            _focusMemory.TryGetValue(route, out var restoredFocus);
            previousLifetime = Advance(restoredFocus);
        }
        Publish(previousLifetime);
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
        CancellationTokenSource? previousLifetime = null;
        lock (_gate)
        {
            ThrowIfDisposed();
            if (_stack.Count - 1 >= _maximumDepth)
                return WidgetNavigationResult.RejectedCapacity;
            if (!TryGetOrCreateScopeId(route, out var scopeId))
                return WidgetNavigationResult.RejectedCapacity;

            RememberFocus(_stack[^1].Route, sourceFocusId);
            _stack.Add(new(route, scopeId, sourceFocusId));
            _focusMemory.TryGetValue(route, out var restoredFocus);
            previousLifetime = Advance(restoredFocus);
        }
        Publish(previousLifetime);
        return WidgetNavigationResult.Changed;
    }

    /// <summary>
    /// Pops one nested route. The optional focus belongs to the route being
    /// left and is remembered if that route is opened again.
    /// </summary>
    public WidgetNavigationResult Back(string? sourceFocusId = null)
    {
        ValidateOptionalFocus(sourceFocusId);
        CancellationTokenSource? previousLifetime = null;
        lock (_gate)
        {
            ThrowIfDisposed();
            if (_stack.Count == 1) return WidgetNavigationResult.CannotGoBack;

            var removed = _stack[^1];
            RememberFocus(removed.Route, sourceFocusId);
            _stack.RemoveAt(_stack.Count - 1);
            previousLifetime = Advance(removed.ReturnFocusId);
        }
        Publish(previousLifetime);
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
        CancellationTokenSource? previousLifetime = null;
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
            previousLifetime = Advance(removed.ReturnFocusId);
        }
        Publish(previousLifetime);
        return true;
    }

    /// <summary>
    /// Applies the current stable input scope and publishes B only for a nested
    /// route. Use the returned scope and <see cref="Value"/> together when
    /// constructing a <see cref="WidgetView"/>.
    /// </summary>
    public StackElement Scope(StackElement root) => Scope(Value, root);

    public StackElement Scope(
        WidgetNavigationSnapshot<TRoute> snapshot,
        StackElement root) => ApplyScope(snapshot, root,
        static (value, scope, shortcuts) => value with
        {
            InputScopeId = scope,
            Shortcuts = shortcuts,
        });

    public RowElement Scope(RowElement root) => Scope(Value, root);

    public RowElement Scope(
        WidgetNavigationSnapshot<TRoute> snapshot,
        RowElement root) => ApplyScope(snapshot, root,
        static (value, scope, shortcuts) => value with
        {
            InputScopeId = scope,
            Shortcuts = shortcuts,
        });

    public ScrollElement Scope(ScrollElement root) => Scope(Value, root);

    public ScrollElement Scope(
        WidgetNavigationSnapshot<TRoute> snapshot,
        ScrollElement root) => ApplyScope(snapshot, root,
        static (value, scope, shortcuts) => value with
        {
            InputScopeId = scope,
            Shortcuts = shortcuts,
        });

    public GridElement Scope(GridElement root) => Scope(Value, root);

    public GridElement Scope(
        WidgetNavigationSnapshot<TRoute> snapshot,
        GridElement root) => ApplyScope(snapshot, root,
        static (value, scope, shortcuts) => value with
        {
            InputScopeId = scope,
            Shortcuts = shortcuts,
        });

    public void Dispose()
    {
        CancellationTokenSource? lifetime;
        CancellationTokenRegistration registration;
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            lifetime = _routeLifetime;
            registration = _widgetLifetimeRegistration;
            _widgetLifetimeRegistration = default;
        }
        registration.Dispose();
        CancelAndDispose(lifetime);
    }

    private TElement ApplyScope<TElement>(
        WidgetNavigationSnapshot<TRoute> snapshot,
        TElement root,
        Func<TElement, string, IReadOnlyList<ControllerShortcut>, TElement> apply)
        where TElement : WidgetElement
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(root);
        var (existingScope, existing) = root switch
        {
            StackElement stack => (stack.InputScopeId, stack.Shortcuts),
            RowElement row => (row.InputScopeId, row.Shortcuts),
            ScrollElement scroll => (scroll.InputScopeId, scroll.Shortcuts),
            GridElement grid => (grid.InputScopeId, grid.Shortcuts),
            _ => throw new ArgumentException("A navigation scope requires a container root.",
                nameof(root)),
        };
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
        return apply(root, snapshot.InputScopeId, shortcuts);
    }

    private CancellationTokenSource Advance(string? restoredFocus)
    {
        var previous = _routeLifetime;
        _routeLifetime = CancellationTokenSource.CreateLinkedTokenSource(_widgetLifetime);
        var revision = checked(_snapshot.Revision + 1);
        _snapshot = CreateSnapshot(restoredFocus, revision);
        return previous;
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
            _routeLifetime.Token);
    }

    private bool TryGetOrCreateScopeId(TRoute route, out string scopeId)
    {
        if (_scopeIds.TryGetValue(route, out scopeId!)) return true;
        if (_scopeIds.Count >= _maximumRoutes)
        {
            scopeId = string.Empty;
            return false;
        }
        scopeId = CreateScopeId(route);
        return true;
    }

    private string CreateScopeId(TRoute route)
    {
        var scopeId = StableIdentifier.Child(_id, $"scope-{_nextScopeOrdinal++}");
        _scopeIds.Add(route, scopeId);
        return scopeId;
    }

    private void RememberFocus(TRoute route, string? focusId)
    {
        if (focusId is not null) _focusMemory[route] = focusId;
    }

    private void Publish(CancellationTokenSource previousLifetime)
    {
        try
        {
            CancelAndDispose(previousLifetime);
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
        CancellationTokenSource? lifetime;
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            lifetime = _routeLifetime;
        }
        CancelAndDispose(lifetime);
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
}
