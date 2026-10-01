using WidgetRail.WidgetBridge;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

namespace WidgetRail.WidgetPresentationSession;

public sealed partial class WidgetPresentationSession
{
    public bool HasPinnedIntentAction(WidgetPinnedSelection selection, WidgetPinnedProjection projection, WidgetActionEvent action)
    {
        lock (_gate)
        {
            var current = DemandPinnedInputLocked(selection, projection);
            if (IntentActionAuthority.Revalidate(projection.Snapshot, projection.Snapshot, action) is null) return false;
            if (IntentActionAuthority.Revalidate(projection.Snapshot, current, action) is null)
                throw PinnedStale("The displayed pinned intent changed.");
            return true;
        }
    }

    public async Task<WidgetIntentPreparation> PreparePinnedIntentAsync(WidgetPinnedSelection selection,
        WidgetPinnedProjection projection, WidgetActionEvent action, CancellationToken cancellationToken = default)
    {
        using var dispatch = await AcquirePinnedDispatchAsync(cancellationToken).ConfigureAwait(false);
        if (!HasPinnedIntentAction(selection, projection, action) || projection.Frame.Authority.WorkerRun is null)
            throw PinnedStale("No current pinned intent action is available.");
        return await PrepareIntentCoreAsync(projection.Frame, action, projection.LayoutId, null, cancellationToken).ConfigureAwait(false);
    }

    public WidgetActionEvent? ResolveIndexedIntentAction(WidgetPresentationIndexedLease lease,
        WidgetPresentationFrame displayed, string itemKey)
    {
        lock (_gate)
        {
            var owners = ValidateDisplayedIndexedInputLocked(lease, displayed);
            var item = lease.Range.Items.SingleOrDefault(item => item.Key == itemKey)
                ?? throw new WidgetPresentationSessionException("indexed_input_stale", "The intent item is outside its lease.");
            if (item.Root.ActionId is not { } actionId) return null;
            var action = new WidgetActionEvent(actionId, item.Root.Id, ControllerButton.A, InputScopeId: lease.ScopeId);
            return IntentActionAuthority.RevalidateIndexed(owners.Origin, owners.Current, item.Root, action) is null ? null : action;
        }
    }

    public async Task<WidgetIntentPreparation> PrepareIndexedIntentAsync(WidgetPresentationIndexedLease lease,
        WidgetPresentationFrame displayed, string itemKey, CancellationToken cancellationToken = default)
    {
        using var dispatch = lease.Request.Range.PinnedLayoutId is not null
            ? await AcquirePinnedDispatchAsync(cancellationToken).ConfigureAwait(false) : null;
        var action = ResolveIndexedIntentAction(lease, displayed, itemKey);
        if (action is null || displayed.Authority.WorkerRun is null)
            throw new WidgetPresentationSessionException("indexed_input_stale", "No current indexed intent action is available.");
        return await PrepareIntentCoreAsync(displayed, action, lease.Request.Range.PinnedLayoutId,
            new(lease.LeaseId, itemKey), cancellationToken).ConfigureAwait(false);
    }
}
