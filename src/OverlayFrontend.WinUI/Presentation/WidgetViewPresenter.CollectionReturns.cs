using WidgetRail.WidgetProtocol;

namespace WidgetRail.OverlayFrontend.WinUI.Presentation;

internal sealed partial class WidgetViewPresenter
{
    // Value-only memory; never retains controls, rows, leases, or provider data.
    // Ordinary focus/reveal requests do not consume this navigation-return cache.
    private readonly OrderedDictionary<CollectionMemoryKey, IndexedViewportMemento> returnViewports = [];
    private (PresentationMemoryOwner Owner, FocusGroupEntryRequest Request)? suspendedCollectionReturn;

    private void SuspendCollectionReturn()
    {
        if (CapturePendingCollectionReturn() is { } request && frame is not null)
            suspendedCollectionReturn = (PresentationMemoryOwner.From(frame.Authority), request);
    }

    private void ResumeSuspendedCollectionReturn()
    {
        if (suspendedCollectionReturn is not { } pending || !presentationActive || !presentationInputEnabled || !automaticFocusEnabled) return;
        suspendedCollectionReturn = null;
        if (frame is not null && pending.Owner == PresentationMemoryOwner.From(frame.Authority)) ResumeCollectionReturn(pending.Request);
    }

    private FocusGroupEntryRequest? CapturePendingCollectionReturn()
    {
        if (pendingGroupEntry is { RestoreViewport: true } entry && effectiveView?.FocusGroupEntryRequest is { RestoreViewport: true, TargetId: null } declared &&
            declared.RequestId == entry.RequestId && declared.GroupId == entry.Group.Id && declared.IndexedItem == entry.IndexedItem)
            return declared;
        foreach (var pending in pendingIndexedViewports)
            if (pending.ReturnIntent is { IndexedItem: { } target } request &&
                bindings.TryGetValue(request.GroupId, out var binding) && binding.Element is WidgetIndexedCollectionView collection &&
                collection.MatchesQuery(target) && (collection.IsEntryPending ||
                    collection.CaptureFocusedItem() == target && collection.RetainedFocusTarget(target) is { IsEnabled: true }))
                return request;
        return suspendedCollectionReturn is { } suspended && frame is not null && suspended.Owner == PresentationMemoryOwner.From(frame.Authority)
            ? suspended.Request : null;
    }

    private void ResumeCollectionReturn(FocusGroupEntryRequest pending)
    {
        if (pending.IndexedItem is not { } target || !pending.RestoreViewport || effectiveView?.FocusGroupEntryRequest != pending ||
            !bindings.TryGetValue(pending.GroupId, out var destination) || destination.Identity.Scope != activeScope ||
            destination.Element is not WidgetIndexedCollectionView collection || !collection.MatchesQuery(target)) return;
        pendingGroupEntry = new(pending.RequestId, destination.Identity, target, RestoreViewport: true);
        needsEntry = true;
        QueueEntryFocus();
    }

    private void ObserveReturnFocusDeparture(Microsoft.UI.Xaml.Input.GettingFocusEventArgs args)
    {
        var target = pendingGroupEntry is { RestoreViewport: true } entry ? entry.Group.Id :
            pendingIndexedViewports.FirstOrDefault(pending => pending.ReturnIntent is not null)?.ReturnIntent?.GroupId ??
            suspendedCollectionReturn?.Request.GroupId;
        if (target is null || FindBinding(args.NewFocusedElement)?.Identity.Id == target) return;
        // A native/UIA programmatic focus choice is input ownership too. Do not
        // confuse asynchronous recovery from a removed/disabled control with a
        // deliberate departure from a still-valid control.
        if (args.OldFocusedElement is Microsoft.UI.Xaml.Controls.Control { IsLoaded: true, IsEnabled: true } &&
            FindBinding(args.OldFocusedElement) is not null)
        {
            TraceFocus("return-focus-departure");
            CancelGroupEntry();
        }
        else TraceFocus("return-native-focus-recovery");
    }

    private void CaptureReturnViewport(Dictionary<string, Declaration> plan, bool sameOwner, Binding? focused)
    {
        if (!sameOwner) { returnViewports.Clear(); return; }
        if (!presentationActive || presentationOnly || focused?.Element is not WidgetIndexedCollectionView collection) return;
        if (plan.TryGetValue(focused.Identity.Id, out var next) && next.Identity == focused.Identity &&
            next.Node.IndexedCollection is { } query && declarations.TryGetValue(focused.Identity.Id, out var previous) &&
            previous.Node.IndexedCollection is { } prior && query.SourceId == prior.SourceId && query.QueryGeneration == prior.QueryGeneration)
            return; // Content refresh retains its native viewport; no per-refresh scan.
        if (collection.CaptureFocusedItem() is not { } item) return;
        var memory = collection.CaptureViewport(focused.Identity, preferFocused: true);
        if (memory is null || memory.Key != item.ItemKey || memory.Index != item.Index) return;
        var key = new CollectionMemoryKey(focused.Identity, item.SourceId, item.QueryGeneration);
        returnViewports.Remove(key);
        returnViewports.Add(key, memory);
        while (returnViewports.Count > MaximumMemoryEntries) returnViewports.RemoveAt(0);
    }

    private void RestoreReturnViewport(long requestId, WidgetElementIdentity identity, IndexedCollectionFocusTarget item)
    {
        var key = new CollectionMemoryKey(identity, item.SourceId, item.QueryGeneration);
        if (frame is null || !returnViewports.TryGetValue(key, out var memory) ||
            memory.Key != item.ItemKey || memory.Index != item.Index) return;
        if (pendingIndexedViewports.Any(pending => pending.Memory.Element == identity && pending.ReturnIntent?.RequestId == requestId)) return;
        // The normal entry owns focus and row admission. Existing memento machinery
        // waits for it to settle, then restores geometry. Later input/new requests
        // cancel this through the same entry-intent version and release all leases.
        pendingIndexedViewports.Add(new(memory, new() { RequestId = requestId, GroupId = identity.Id, IndexedItem = item, RestoreViewport = true }));
        memoryIntent = entryIntentVersion;
        memoryOwner = PresentationMemoryOwner.From(frame.Authority);
        LayoutUpdated -= MemoryLayoutUpdated;
        LayoutUpdated += MemoryLayoutUpdated;
        QueueMemoryRestoration();
    }
}
