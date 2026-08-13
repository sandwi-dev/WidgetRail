namespace GameBarAlternative.AvaloniaPrototype.Remote;

public readonly record struct RemoteWidgetItemId(string Value)
{
    public override string ToString() => Value;
}

public sealed record RemoteWidgetItemSnapshot(
    RemoteWidgetItemId Id,
    string Title,
    string Subtitle);

public sealed record RemoteWidgetSnapshot(
    long Revision,
    IReadOnlyList<RemoteWidgetItemSnapshot> Items,
    string Status);

public sealed record RemoteWidgetAction(
    RemoteWidgetItemId ItemId,
    string ActionId);

public interface IRemoteWidgetEndpoint
{
    Task<RemoteWidgetSnapshot> GetSnapshotAsync(CancellationToken cancellationToken);

    Task InvokeAsync(RemoteWidgetAction action, CancellationToken cancellationToken);
}

public sealed record RemoteWidgetProjectionState(
    RemoteWidgetSnapshot? LastGoodSnapshot,
    bool IsLoading,
    string? Failure);

public sealed class RemoteWidgetProjection : IDisposable
{
    private readonly object gate = new();
    private readonly IRemoteWidgetEndpoint endpoint;
    private CancellationTokenSource activeLifetime = CreateCancelledLifetime();
    private CancellationTokenSource? request;
    private RemoteWidgetProjectionState state = new(null, false, null);
    private long generation;
    private bool active;
    private bool disposed;

    public RemoteWidgetProjection(IRemoteWidgetEndpoint endpoint) =>
        this.endpoint = endpoint ?? throw new ArgumentNullException(nameof(endpoint));

    public RemoteWidgetProjectionState State
    {
        get { lock (gate) return state; }
    }

    public event EventHandler<RemoteWidgetProjectionState>? StateChanged;

    public async Task ActivateAsync(CancellationToken cancellationToken = default)
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (!active)
            {
                activeLifetime.Dispose();
                activeLifetime = new CancellationTokenSource();
                active = true;
            }
        }

        await RefreshAsync(cancellationToken);
    }

    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        CancellationTokenSource ownedRequest;
        long ownedGeneration;
        RemoteWidgetProjectionState loading;
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (!active)
            {
                throw new InvalidOperationException("The remote projection must be active before refresh.");
            }

            request?.Cancel();
            request?.Dispose();
            request = CancellationTokenSource.CreateLinkedTokenSource(activeLifetime.Token, cancellationToken);
            ownedRequest = request;
            ownedGeneration = ++generation;
            loading = state with { IsLoading = true, Failure = null };
            state = loading;
        }

        StateChanged?.Invoke(this, loading);

        try
        {
            var snapshot = await endpoint.GetSnapshotAsync(ownedRequest.Token);
            PublishIfCurrent(ownedGeneration, ownedRequest, new(snapshot, false, null));
        }
        catch (OperationCanceledException) when (ownedRequest.IsCancellationRequested)
        {
        }
        catch (Exception)
        {
            RemoteWidgetProjectionState failed;
            lock (gate)
            {
                if (!IsCurrent(ownedGeneration, ownedRequest))
                {
                    return;
                }

                failed = state with { IsLoading = false, Failure = "Remote update failed; showing the last good snapshot." };
                state = failed;
            }

            StateChanged?.Invoke(this, failed);
        }
    }

    public async Task InvokeAsync(RemoteWidgetAction action, CancellationToken cancellationToken = default)
    {
        CancellationToken activeToken;
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (!active)
            {
                throw new InvalidOperationException("The remote projection is inactive.");
            }

            if (string.IsNullOrWhiteSpace(action.ItemId.Value) || string.IsNullOrWhiteSpace(action.ActionId) ||
                state.LastGoodSnapshot?.Items.Any(item => item.Id == action.ItemId) != true)
            {
                throw new ArgumentException("The action must identify an item from the last good snapshot.", nameof(action));
            }

            activeToken = activeLifetime.Token;
        }

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(activeToken, cancellationToken);
        await endpoint.InvokeAsync(action, linked.Token);
    }

    public void Deactivate()
    {
        RemoteWidgetProjectionState? inactive = null;
        lock (gate)
        {
            if (!active)
            {
                return;
            }

            active = false;
            generation++;
            request?.Cancel();
            request?.Dispose();
            request = null;
            activeLifetime.Cancel();
            if (state.IsLoading)
            {
                inactive = state with { IsLoading = false };
                state = inactive;
            }
        }

        if (inactive is not null)
        {
            StateChanged?.Invoke(this, inactive);
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
            active = false;
            generation++;
            request?.Cancel();
            request?.Dispose();
            request = null;
            activeLifetime.Cancel();
            activeLifetime.Dispose();
        }
    }

    private void PublishIfCurrent(
        long ownedGeneration,
        CancellationTokenSource ownedRequest,
        RemoteWidgetProjectionState next)
    {
        lock (gate)
        {
            if (!IsCurrent(ownedGeneration, ownedRequest))
            {
                return;
            }

            state = next;
        }

        StateChanged?.Invoke(this, next);
    }

    private bool IsCurrent(long ownedGeneration, CancellationTokenSource ownedRequest) =>
        active && !ownedRequest.IsCancellationRequested && ownedGeneration == generation && ReferenceEquals(request, ownedRequest);

    private static CancellationTokenSource CreateCancelledLifetime()
    {
        var lifetime = new CancellationTokenSource();
        lifetime.Cancel();
        return lifetime;
    }
}
