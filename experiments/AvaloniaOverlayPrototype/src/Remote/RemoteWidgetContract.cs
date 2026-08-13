using System.Text;

namespace GameBarAlternative.AvaloniaPrototype.Remote;

public readonly record struct RemoteWidgetItemId
{
    public const int MaximumUtf8Bytes = 128;
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    public RemoteWidgetItemId(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        var bytes = StrictUtf8.GetByteCount(value);
        if (bytes > MaximumUtf8Bytes)
        {
            throw new ArgumentOutOfRangeException(nameof(value), $"Item identity must not exceed {MaximumUtf8Bytes} UTF-8 bytes.");
        }

        Value = value;
    }

    public string Value { get; }

    public override string ToString() => Value;
}

public readonly record struct RemoteWidgetActionId
{
    public const int MaximumUtf8Bytes = 64;
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    public RemoteWidgetActionId(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        var bytes = StrictUtf8.GetByteCount(value);
        if (bytes > MaximumUtf8Bytes)
        {
            throw new ArgumentOutOfRangeException(nameof(value), $"Action identity must not exceed {MaximumUtf8Bytes} UTF-8 bytes.");
        }

        Value = value;
    }

    public string Value { get; }

    public override string ToString() => Value;
}

public sealed record RemoteWidgetDeclaredAction(RemoteWidgetActionId Id, string Name);

public sealed record RemoteWidgetItemSnapshot(
    RemoteWidgetItemId Id,
    string Title,
    string Subtitle,
    IReadOnlyList<RemoteWidgetDeclaredAction> Actions);

public sealed record RemoteWidgetSnapshot(
    long Revision,
    IReadOnlyList<RemoteWidgetItemSnapshot> Items,
    string Status);

public sealed record RemoteWidgetAction(
    RemoteWidgetItemId ItemId,
    RemoteWidgetActionId ActionId);

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
    public const int MaximumSnapshotItems = 10_000;
    public const int MaximumActionsPerItem = 8;
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
            var snapshot = await endpoint.GetSnapshotAsync(ownedRequest.Token).ConfigureAwait(false);
            ValidateSnapshot(snapshot);
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

            var item = state.LastGoodSnapshot?.Items.FirstOrDefault(candidate => candidate.Id == action.ItemId);
            if (item is null || item.Actions.All(declared => declared.Id != action.ActionId))
            {
                throw new ArgumentException("The action must be an exact item/action tuple declared by the latest good snapshot.", nameof(action));
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

    private static void ValidateSnapshot(RemoteWidgetSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (snapshot.Items.Count > MaximumSnapshotItems)
        {
            throw new InvalidDataException($"Remote snapshot exceeded {MaximumSnapshotItems} items.");
        }

        var itemIds = new HashSet<RemoteWidgetItemId>();
        foreach (var item in snapshot.Items)
        {
            if (string.IsNullOrWhiteSpace(item.Id.Value))
            {
                throw new InvalidDataException("Remote snapshot contained an empty item identity.");
            }

            if (!itemIds.Add(item.Id))
            {
                throw new InvalidDataException($"Remote snapshot repeated item identity '{item.Id}'.");
            }

            if (item.Actions.Count > MaximumActionsPerItem)
            {
                throw new InvalidDataException($"Remote item '{item.Id}' exceeded {MaximumActionsPerItem} declared actions.");
            }

            var actionIds = new HashSet<RemoteWidgetActionId>();
            foreach (var action in item.Actions)
            {
                if (string.IsNullOrWhiteSpace(action.Id.Value))
                {
                    throw new InvalidDataException($"Remote item '{item.Id}' contained an empty action identity.");
                }

                if (!actionIds.Add(action.Id))
                {
                    throw new InvalidDataException($"Remote item '{item.Id}' repeated action identity '{action.Id}'.");
                }
            }
        }
    }

    private static CancellationTokenSource CreateCancelledLifetime()
    {
        var lifetime = new CancellationTokenSource();
        lifetime.Cancel();
        return lifetime;
    }
}
