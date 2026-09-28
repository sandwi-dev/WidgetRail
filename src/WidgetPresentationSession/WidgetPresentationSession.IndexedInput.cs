using System.Runtime.CompilerServices;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.WidgetPresentationSession;

public sealed partial class WidgetPresentationSession
{
    // A native UI retains the exact published frame it displays. Weak provenance
    // permits that frame to survive receive-thread publication without retaining
    // another history of full widget trees or accepting fabricated authorities.
    private readonly ConditionalWeakTable<WidgetPresentationFrame, object> _publishedInputFrames = new();
    private static readonly object PublishedInputFrameMarker = new();

    private (IReadOnlyList<ViewNode> Origin, IReadOnlyList<ViewNode> Current) ValidateDisplayedIndexedInputLocked(
        WidgetPresentationIndexedLease lease, WidgetPresentationFrame origin)
    {
        DemandIndexedLeaseLocked(lease);
        ThrowIfTerminalLocked();
        if (!_publishedInputFrames.TryGetValue(origin, out _) || !SameIndexedOwner(origin.Authority, lease.Authority) ||
            _states.GetValueOrDefault(origin.Authority.WidgetId)?.LastGood is not { } current ||
            !SameIndexedOwner(origin.Authority, current.Authority) || origin.Authority.SnapshotSequence > current.Authority.SnapshotSequence)
            throw new WidgetPresentationSessionException("indexed_input_stale", "The indexed input frame was not published by the active session.");
        try
        {
            return (IndexedCollectionInputContract.ResolveOwnerPath(origin.Snapshot, lease.Request.Range),
                IndexedCollectionInputContract.ResolveOwnerPath(current.Snapshot, lease.Request.Range));
        }
        catch (Exception error) when (error is InvalidOperationException or ArgumentException or ProtocolValidationException)
        { throw new WidgetPresentationSessionException("input_scope_stale", "The indexed row is outside its displayed or current input scope."); }
    }

    internal bool DisplayedIndexedInputIsClaimed(WidgetPresentationIndexedLease lease, WidgetPresentationFrame origin,
        string key, ControllerButton button, ControllerEventPhase phase)
    {
        ArgumentNullException.ThrowIfNull(origin);
        var input = new IndexedCollectionInputRequest(new(lease.LeaseId, key), button, phase);
        IndexedCollectionInputContract.ValidateInput(input);
        lock (_gate)
        {
            var owners = ValidateDisplayedIndexedInputLocked(lease, origin);
            var item = lease.Range.Items.SingleOrDefault(item => item.Key == key)
                ?? throw new WidgetPresentationSessionException("indexed_input_stale", "The indexed input item is outside its lease.");
            DemandSameIndexedBinding(owners.Origin, owners.Current, item.Root, input);
            return IndexedCollectionInputContract.ClaimsInput(owners.Origin, item.Root, button, phase);
        }
    }

    private static void DemandSameIndexedBinding(IReadOnlyList<ViewNode> origin, IReadOnlyList<ViewNode> current,
        ViewNode item, IndexedCollectionInputRequest input)
    {
        if (IndexedCollectionInputContract.Resolve(origin, item, input) != IndexedCollectionInputContract.Resolve(current, item, input))
            throw new WidgetPresentationSessionException("indexed_input_stale", "The indexed input binding changed after presentation.");
        // Disabled/busy owners still claim their shortcut. An unchanged null
        // admission must not hide an ownership change and escape to the shell.
        if (IndexedCollectionInputContract.ClaimsInput(origin, item, input.Button, input.Phase) !=
            IndexedCollectionInputContract.ClaimsInput(current, item, input.Button, input.Phase))
            throw new WidgetPresentationSessionException("indexed_input_stale", "The indexed input owner changed after presentation.");
    }
}
