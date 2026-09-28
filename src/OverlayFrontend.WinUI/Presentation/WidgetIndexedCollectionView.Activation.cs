using System.ComponentModel;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using WidgetRail.OverlayFrontend.WinUI.Collections;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.OverlayFrontend.WinUI.Presentation;

internal sealed partial class WidgetIndexedCollectionView
{
    // This is one short-lived user intent, never a stale lease or a queue of
    // worker actions. The worker still validates the replacement row normally.
    private sealed record PendingActivation(WidgetIndexedRows Source, IndexedItem<WidgetIndexedRow> Slot,
        string Key, string Scope, string OwnerId, ViewNodeKind Kind, string Action,
        PropertyChangedEventHandler Changed, DispatcherQueueTimer Expiry);
    private PendingActivation? pendingActivation;
    private bool activationQueued;

    private bool DeferActivation(WidgetIndexedRow row)
    {
        if (pendingActivation is not null) return true;
        if (source is null || !ReferenceEquals(row.Owner, source) || FocusedIndex() is not { } index ||
            source.Items[index] is not IndexedItem<WidgetIndexedRow> slot || !ReferenceEquals(slot.Value, row) ||
            row.Item.Root is not { ActionId: { } action, IsDisabled: not true, IsBusy: not true } root) return false;
        PropertyChangedEventHandler changed = (_, _) => QueueActivation();
        var expiry = DispatcherQueue.CreateTimer();
        expiry.Interval = TimeSpan.FromSeconds(2);
        expiry.IsRepeating = false;
        expiry.Tick += (_, _) => { if (ReferenceEquals(pendingActivation?.Expiry, expiry)) CancelPendingActivation(); };
        pendingActivation = new(source, slot, row.Item.Key, source.Frame.Authority.ActiveInputScopeId,
            root.Id, root.Kind, action, changed, expiry);
        slot.PropertyChanged += changed;
        expiry.Start();
        return true;
    }

    internal void CancelPendingActivation()
    {
        if (pendingActivation is not { } pending) return;
        pendingActivation = null;
        pending.Slot.PropertyChanged -= pending.Changed;
        pending.Expiry.Stop();
    }

    private bool ActivationOwnerCurrent(PendingActivation pending) => !disposed && IsLoaded && CanReceiveInput &&
        ReferenceEquals(source, pending.Source) && FocusedIndex() == pending.Slot.Index &&
        source.Frame.Authority.ActiveInputScopeId == pending.Scope && pending.Slot.Key == pending.Key;

    private void ValidatePendingActivation()
    {
        if (pendingActivation is { } pending && (!ActivationOwnerCurrent(pending) || pending.Slot.Failed)) CancelPendingActivation();
    }

    private void QueueActivation()
    {
        if (activationQueued || pendingActivation is null) return;
        activationQueued = true;
        // Slot notifications also update the native row template. Dispatch only
        // after that synchronous publication has completed, using its new frame.
        if (!DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, CompleteActivation))
        { activationQueued = false; CancelPendingActivation(); }
    }

    private void CompleteActivation()
    {
        activationQueued = false;
        ValidatePendingActivation();
        if (pendingActivation is not { } pending || pending.Slot.Value is not { } row || !row.Lease.IsCurrent) return;
        CancelPendingActivation(); // Consume before dispatch/reentrancy: at most once.
        // A resolves to the root action only (IndexedCollectionInputContract).
        // Never replay a press as a changed command, including Play -> Install.
        if (row.Item.Root is { IsDisabled: not true, IsBusy: not true } root && root.Id == pending.OwnerId &&
            root.Kind == pending.Kind && root.ActionId == pending.Action)
            _ = InvokeAsync(row, ControllerButton.A, allowDeferredActivation: false);
    }
}
