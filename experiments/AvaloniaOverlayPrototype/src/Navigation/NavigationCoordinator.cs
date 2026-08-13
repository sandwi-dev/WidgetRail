namespace GameBarAlternative.AvaloniaPrototype.Navigation;

public enum NavigationOutcome
{
    Committed,
    Superseded,
    Cancelled,
}

public sealed record NavigationResult<TPage>(
    NavigationOutcome Outcome,
    PrototypeRoute Route,
    TPage? Page,
    TimeSpan LoadDuration);

public sealed class NavigationCoordinator<TPage> : IDisposable
    where TPage : class
{
    private readonly object gate = new();
    private readonly List<PrototypeRoute> history = [];
    private CancellationTokenSource? pendingNavigation;
    private long generation;
    private bool disposed;

    public PrototypeRoute? CurrentRoute { get; private set; }

    public TPage? CurrentPage { get; private set; }

    public PrototypeRoute? PendingRoute { get; private set; }

    public bool IsLoading => PendingRoute is not null;

    public IReadOnlyList<PrototypeRoute> History
    {
        get
        {
            lock (gate)
            {
                return history.ToArray();
            }
        }
    }

    public async Task<NavigationResult<TPage>> NavigateAsync(
        PrototypeRoute route,
        Func<PrototypeRoute, CancellationToken, Task<TPage>> loader,
        bool recordHistory = true,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(loader);
        CancellationTokenSource ownedCancellation;
        long ownedGeneration;

        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            pendingNavigation?.Cancel();
            pendingNavigation?.Dispose();
            pendingNavigation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            ownedCancellation = pendingNavigation;
            ownedGeneration = ++generation;
            PendingRoute = route;
        }

        var started = System.Diagnostics.Stopwatch.StartNew();
        TPage page;
        try
        {
            page = await loader(route, ownedCancellation.Token);
        }
        catch (OperationCanceledException) when (ownedCancellation.IsCancellationRequested)
        {
            lock (gate)
            {
                if (ownedGeneration == generation)
                {
                    PendingRoute = null;
                }
            }

            return new NavigationResult<TPage>(
                cancellationToken.IsCancellationRequested ? NavigationOutcome.Cancelled : NavigationOutcome.Superseded,
                route,
                null,
                started.Elapsed);
        }

        lock (gate)
        {
            if (ownedGeneration != generation || ownedCancellation.IsCancellationRequested)
            {
                return new NavigationResult<TPage>(NavigationOutcome.Superseded, route, null, started.Elapsed);
            }

            if (recordHistory && CurrentRoute is { } current && current != route)
            {
                history.Add(current);
            }

            CurrentRoute = route;
            CurrentPage = page;
            PendingRoute = null;
            return new NavigationResult<TPage>(NavigationOutcome.Committed, route, page, started.Elapsed);
        }
    }

    public PrototypeRoute? PopHistory()
    {
        lock (gate)
        {
            if (history.Count == 0)
            {
                return null;
            }

            var route = history[^1];
            history.RemoveAt(history.Count - 1);
            return route;
        }
    }

    public void CancelPending()
    {
        lock (gate)
        {
            pendingNavigation?.Cancel();
            PendingRoute = null;
        }
    }

    public void Dispose()
    {
        lock (gate)
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            pendingNavigation?.Cancel();
            pendingNavigation?.Dispose();
            pendingNavigation = null;
            PendingRoute = null;
        }
    }
}
