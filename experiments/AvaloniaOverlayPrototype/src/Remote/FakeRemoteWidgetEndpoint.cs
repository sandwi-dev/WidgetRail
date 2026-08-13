namespace GameBarAlternative.AvaloniaPrototype.Remote;

public sealed class FakeRemoteWidgetEndpoint : IRemoteWidgetEndpoint
{
    public const int ItemCount = 10_000;
    private readonly List<RemoteWidgetAction> actions = [];
    private long revision;

    public IReadOnlyList<RemoteWidgetAction> Actions
    {
        get { lock (actions) return actions.ToArray(); }
    }

    public async Task<RemoteWidgetSnapshot> GetSnapshotAsync(CancellationToken cancellationToken)
    {
        await Task.Delay(TimeSpan.FromMilliseconds(45), cancellationToken);
        var currentRevision = Interlocked.Increment(ref revision);
        var items = new RemoteWidgetItemSnapshot[ItemCount];
        for (var index = 0; index < items.Length; index++)
        {
            var ordinal = index + 1;
            items[index] = new RemoteWidgetItemSnapshot(
                new RemoteWidgetItemId($"game-{ordinal:D5}"),
                $"Application {ordinal:D5}",
                ordinal % 7 == 0 ? "Artwork pending" : "Installed locally");
        }

        return new RemoteWidgetSnapshot(currentRevision, items, $"Revision {currentRevision} ready");
    }

    public async Task InvokeAsync(RemoteWidgetAction action, CancellationToken cancellationToken)
    {
        await Task.Delay(TimeSpan.FromMilliseconds(12), cancellationToken);
        lock (actions) actions.Add(action);
    }
}
