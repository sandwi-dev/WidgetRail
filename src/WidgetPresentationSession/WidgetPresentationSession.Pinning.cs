using System.Collections.ObjectModel;
using System.Runtime.CompilerServices;
using WidgetRail.WidgetBridge;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

namespace WidgetRail.WidgetPresentationSession;

public sealed partial class WidgetPresentationSession
{
    private sealed record PinnedEpoch(WidgetPresentationAuthority Owner);
    private readonly Dictionary<string, Dictionary<string, PinnedEpoch>> _pinnedEpochs = new(StringComparer.Ordinal);
    private readonly ConditionalWeakTable<WidgetPresentationFrame, IReadOnlyDictionary<string, PinnedEpoch>> _pinnedFrames = new();
    private readonly Dictionary<string, WidgetPinnedSelection> _pinnedSelections = new(StringComparer.Ordinal);
    private readonly HashSet<string> _pinnedSelectionPending = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _pinnedDispatch = new(1, 1);

    /// <summary>Capture the current selection, including an unconfirmed attempt, for a fresh host teardown intent.</summary>
    public WidgetPinnedSelection? GetPinnedSelection(WidgetPresentationFrame displayed)
    {
        ArgumentNullException.ThrowIfNull(displayed);
        using (_gate.Enter())
        {
            var current = DemandPinnedFrameLocked(displayed);
            if (current.Authority.SnapshotSequence != displayed.Authority.SnapshotSequence)
                throw PinnedStale("Selection lookup requires the current displayed frame.");
            return _pinnedSelections.GetValueOrDefault(current.Authority.WidgetId);
        }
    }

    public WidgetPinnedProjection ResolvePinnedProjection(WidgetPresentationFrame displayed, string layoutId)
    {
        ArgumentNullException.ThrowIfNull(displayed);
        using (_gate.Enter())
        {
            var current = DemandPinnedFrameLocked(displayed);
            if (!Safe(layoutId) || !_pinnedFrames.TryGetValue(displayed, out var origins) ||
                !origins.TryGetValue(layoutId, out var epoch) ||
                !ReferenceEquals(_pinnedEpochs.GetValueOrDefault(current.Authority.WidgetId)?.GetValueOrDefault(layoutId), epoch))
                throw PinnedStale("The displayed pinned layout was removed or replaced.");
            var (snapshot, ownRoot) = PinnedSnapshot(displayed, layoutId);
            var ids = BridgeRenderStyleContract.SnapshotNodeIds(snapshot);
            var prefix = ownRoot ? layoutId + "/" : string.Empty;
            var styles = ids.Where(id => displayed.RenderStyles.ContainsKey(prefix + id))
                .ToDictionary(id => id, id => displayed.RenderStyles[prefix + id], StringComparer.Ordinal);
            return new(this, displayed, layoutId, epoch, snapshot,
                new ReadOnlyDictionary<string, BridgeNodeRenderStyles>(styles), ownRoot || layoutId == PinnedSurfaceContract.FullWidgetLayoutId);
        }
    }

    /// <summary>Changes the host selection, preserving its exact displayed origin on the wire.</summary>
    public async Task<WidgetPinnedSelection> SelectPinnedLayoutAsync(WidgetPinnedProjection projection,
        long sequence = 0, long monotonicTimestampMicroseconds = 0, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(projection);
        cancellationToken.ThrowIfCancellationRequested();
        WidgetPinnedSelection selected;
        using (_gate.Enter())
        {
            var current = DemandPinnedProjectionLocked(projection);
            if (current.Authority.SnapshotSequence != projection.Frame.Authority.SnapshotSequence ||
                _indexedHiddenWidgets.Contains(current.Authority.WidgetId))
                throw PinnedStale("Selection requires the current visible widget frame.");
            ValidatePinnedClock(sequence, monotonicTimestampMicroseconds);
            var widget = current.Authority.WidgetId;
            if (!_pinnedSelectionPending.Add(widget)) throw PinnedStale("A pinned selection change is pending.");
            if (_pinnedSelections.TryGetValue(widget, out var prior))
            { prior.Active = false; RetirePinnedIndexedLocked(widget, prior.LayoutId); }
            selected = new(this, projection);
            _pinnedSelections[widget] = selected;
        }
        try
        {
            using var dispatch = await AcquirePinnedDispatchAsync(cancellationToken).ConfigureAwait(false);
            using (_gate.Enter())
                if (DemandPinnedProjectionLocked(projection).Authority.SnapshotSequence != projection.Frame.Authority.SnapshotSequence ||
                    !ReferenceEquals(_pinnedSelections.GetValueOrDefault(selected.WidgetId), selected) ||
                    _indexedHiddenWidgets.Contains(selected.WidgetId))
                    throw PinnedStale("The selection retired or changed before notification.");
            // Full-widget is a host projection, not a package handle. Clearing
            // authored demand cancels the prior handle without inventing one.
            var full = projection.LayoutId == PinnedSurfaceContract.FullWidgetLayoutId;
            var input = new ControllerInputEvent(ControllerButton.View, ControllerEventPhase.Pressed,
                ControllerInputContext.PinnedLayoutSelection, Sequence: sequence,
                MonotonicTimestampMicroseconds: monotonicTimestampMicroseconds,
                SnapshotSequence: projection.Frame.Authority.SnapshotSequence)
            { PinnedLayoutId = full ? null : projection.LayoutId, IsPinnedLayoutSelected = !full };
            var acknowledged = await SendPinnedWireAsync(projection.Frame, input, null, null, dispatch, cancellationToken).ConfigureAwait(false);
            using (_gate.Enter())
            {
                cancellationToken.ThrowIfCancellationRequested();
                DemandPinnedProjectionLocked(projection);
                if (!ReferenceEquals(_pinnedSelections.GetValueOrDefault(selected.WidgetId), selected) ||
                    _indexedHiddenWidgets.Contains(selected.WidgetId)) throw PinnedStale("The selection retired during notification.");
                selected.DemandAcknowledged = acknowledged;
                selected.Active = true;
                return selected;
            }
        }
        finally { using (_gate.Enter()) _pinnedSelectionPending.Remove(selected.WidgetId); }
    }

    /// <summary>Revokes local input immediately. The genuine current frame notifies the same worker of demand removal.</summary>
    public async Task<bool> ClearPinnedSelectionAsync(WidgetPinnedSelection selection, WidgetPresentationFrame displayed,
        long sequence = 0, long monotonicTimestampMicroseconds = 0, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(selection); ArgumentNullException.ThrowIfNull(displayed);
        cancellationToken.ThrowIfCancellationRequested();
        using (_gate.Enter())
        {
            var current = DemandPinnedFrameLocked(displayed);
            if (!ReferenceEquals(selection.Owner, this) || !ReferenceEquals(_pinnedSelections.GetValueOrDefault(selection.WidgetId), selection) ||
                !SameIndexedOwner(selection.Authority, current.Authority) ||
                current.Authority.SnapshotSequence != displayed.Authority.SnapshotSequence)
                throw PinnedStale("The deselection belongs to another selection or frame.");
            ValidatePinnedClock(sequence, monotonicTimestampMicroseconds);
            if (!_pinnedSelectionPending.Add(selection.WidgetId)) throw PinnedStale("A pinned selection change is pending.");
            selection.Active = false; RetirePinnedIndexedLocked(selection.WidgetId, selection.LayoutId);
        }
        try
        {
            using var dispatch = await AcquirePinnedDispatchAsync(cancellationToken).ConfigureAwait(false);
            using (_gate.Enter())
                if (DemandPinnedFrameLocked(displayed).Authority.SnapshotSequence != displayed.Authority.SnapshotSequence)
                    throw PinnedStale("The deselection frame changed before notification.");
            var input = new ControllerInputEvent(ControllerButton.View, ControllerEventPhase.Pressed,
                ControllerInputContext.PinnedLayoutSelection, Sequence: sequence,
                MonotonicTimestampMicroseconds: monotonicTimestampMicroseconds, SnapshotSequence: displayed.Authority.SnapshotSequence)
            // The package owns one authored demand. A cancelled selection may
            // or may not have reached it; the existing null-id revoke handles
            // both states without targeting a replacement local selection.
            { PinnedLayoutId = null, IsPinnedLayoutSelected = false };
            var handled = await SendPinnedWireAsync(displayed, input, null, null, dispatch, cancellationToken).ConfigureAwait(false);
            using (_gate.Enter()) if (ReferenceEquals(_pinnedSelections.GetValueOrDefault(selection.WidgetId), selection)) _pinnedSelections.Remove(selection.WidgetId);
            return handled;
        }
        finally { using (_gate.Enter()) _pinnedSelectionPending.Remove(selection.WidgetId); }
    }

    public bool IsPinnedSelectionCurrent(WidgetPinnedSelection selection)
    {
        ArgumentNullException.ThrowIfNull(selection);
        using (_gate.Enter()) return !_disposed && _terminalFailure is null && ReferenceEquals(selection.Owner, this) && selection.Active &&
            !_indexedHiddenWidgets.Contains(selection.WidgetId) && ReferenceEquals(_pinnedSelections.GetValueOrDefault(selection.WidgetId), selection) &&
            ReferenceEquals(_pinnedEpochs.GetValueOrDefault(selection.WidgetId)?.GetValueOrDefault(selection.LayoutId), selection.Epoch);
    }

    private WidgetPresentationFrame DemandPinnedFrameLocked(WidgetPresentationFrame frame)
    {
        ObjectDisposedException.ThrowIf(_disposed, this); ThrowIfTerminalLocked();
        if (!_publishedInputFrames.TryGetValue(frame, out _) ||
            _states.GetValueOrDefault(frame.Authority.WidgetId) is not { Failure: null, LastGood: { } current } ||
            !SameIndexedOwner(frame.Authority, current.Authority) || frame.Authority.SnapshotSequence > current.Authority.SnapshotSequence)
            throw PinnedStale("The pinned frame was not published by the current session.");
        return current;
    }
    private WidgetPresentationFrame DemandPinnedProjectionLocked(WidgetPinnedProjection projection)
    {
        var current = DemandPinnedFrameLocked(projection.Frame);
        if (!ReferenceEquals(projection.Owner, this) ||
            !ReferenceEquals(_pinnedEpochs.GetValueOrDefault(current.Authority.WidgetId)?.GetValueOrDefault(projection.LayoutId), projection.Epoch))
            throw PinnedStale("The pinned layout lifetime retired.");
        return current;
    }
    private ViewSnapshot DemandPinnedInputLocked(WidgetPinnedSelection selection, WidgetPinnedProjection projection)
    {
        var current = DemandPinnedProjectionLocked(projection);
        if (!IsPinnedSelectionCurrent(selection) || selection.WidgetId != current.Authority.WidgetId || selection.LayoutId != projection.LayoutId ||
            !ReferenceEquals(selection.Epoch, projection.Epoch) || !projection.SupportsOrdinaryInput)
            throw PinnedStale("Pinned input requires the selected layout and its supported input scope.");
        return PinnedSnapshot(current, projection.LayoutId).Snapshot;
    }
    private static void ValidatePinnedClock(long sequence, long timestamp)
    { if (sequence < 0 || timestamp < 0) throw PinnedStale("Pinned input timestamps cannot be negative."); }
    private static WidgetPresentationSessionException PinnedStale(string message) => new("pinned_input_stale", message);

    private static (ViewSnapshot Snapshot, bool OwnRoot) PinnedSnapshot(WidgetPresentationFrame frame, string id)
    {
        if (!frame.Descriptor.PinningSupported) throw PinnedStale("The widget does not support pinning.");
        var parent = PinnedSurfaceContract.WithoutModal(frame.Snapshot);
        if (id == PinnedSurfaceContract.FullWidgetLayoutId)
        {
            if (!frame.Descriptor.FullWidgetPinningSupported) throw PinnedStale("Full-widget pinning is not declared.");
            return (parent with { PinnedLayouts = [], QuickActions = [] }, false);
        }
        var layout = frame.Snapshot.PinnedLayouts.SingleOrDefault(layout => layout.Id == id)
            ?? throw PinnedStale("The pinned layout is not declared.");
        if (layout.Root is not { } root) return (parent with { PinnedLayouts = [], QuickActions = [], Surface = layout.Surface }, false);
        return (frame.Snapshot with { Root = root, ActiveInputScopeId = layout.ActiveInputScopeId!, InitialFocusId = layout.InitialFocusId,
            Surface = layout.Surface, PinnedLayouts = [], QuickActions = [], FocusGroupEntryRequest = null, ScrollRevealRequest = null }, true);
    }

    private void ReconcilePinnedProjectionsLocked(WidgetPresentationState state)
    {
        if (state is not { Failure: null, LastGood: { } frame }) { RetirePinnedProjectionsLocked(state.WidgetId); return; }
        var previous = _pinnedEpochs.GetValueOrDefault(state.WidgetId);
        var next = new Dictionary<string, PinnedEpoch>(StringComparer.Ordinal);
        if (frame.Descriptor.PinningSupported)
        {
            var ids = frame.Snapshot.PinnedLayouts.Select(layout => layout.Id);
            if (frame.Descriptor.FullWidgetPinningSupported) ids = ids.Append(PinnedSurfaceContract.FullWidgetLayoutId);
            foreach (var id in ids)
            {
                var old = previous?.GetValueOrDefault(id);
                // Layout presence owns selection lifetime. Its active scope and
                // content can navigate independently; input still compares the
                // captured projection's own scope and binding with the current one.
                next.Add(id, old is not null && SameIndexedOwner(old.Owner, frame.Authority) ? old : new(frame.Authority));
            }
        }
        _pinnedEpochs[state.WidgetId] = next;
        _pinnedFrames.GetValue(frame, _ => new ReadOnlyDictionary<string, PinnedEpoch>(next));
        if (_pinnedSelections.TryGetValue(state.WidgetId, out var selected) &&
            !ReferenceEquals(next.GetValueOrDefault(selected.LayoutId), selected.Epoch)) selected.Active = false;
        ReconcilePinnedArtworkLocked(state.WidgetId);
    }
    private void RetirePinnedProjectionsLocked(string? widget = null)
    {
        RetirePinnedArtworkLocked(widget);
        if (widget is null) { _pinnedEpochs.Clear(); foreach (var item in _pinnedSelections.Values) item.Active = false; _pinnedSelections.Clear(); }
        else { _pinnedEpochs.Remove(widget); if (_pinnedSelections.Remove(widget, out var selected)) selected.Active = false; }
    }
    private void RetirePinnedIndexedLocked(string widget, string id)
    {
        RetirePinnedArtworkLocked(widget, id);
        foreach (var lease in _indexedLeases.Values.ToArray())
            if (lease.Authority.WidgetId == widget && lease.Request.Range.PinnedLayoutId == id) RetireIndexedLeaseLocked(lease);
        foreach (var demand in _indexedDemands.Values)
            if (demand.Authority.WidgetId == widget && demand.Request.Range.PinnedLayoutId == id)
                demand.Retirement ??= demand.Lifetime.CancelAsync();
    }
    private void DemandPinnedIndexedInputLocked(WidgetPresentationIndexedLease lease)
    {
        if (lease.Request.Range.PinnedLayoutId is not { } id) return;
        if (!_pinnedSelections.TryGetValue(lease.Authority.WidgetId, out var selected) || selected.LayoutId != id ||
            !IsPinnedSelectionCurrent(selected))
            throw PinnedStale("The indexed row does not belong to the selected pinned layout.");
    }

    // Cancellation stops the caller waiting, not a command already admitted to
    // the transport. Keep selection/input ordering until its real reply arrives.
    private sealed class PinnedDispatch(SemaphoreSlim gate) : IDisposable
    {
        private Task? _exchange;
        internal void Track(Task exchange)
        {
            _exchange = exchange;
            ObserveIndexedReply(exchange);
        }
        public void Dispose()
        {
            if (_exchange is null || _exchange.IsCompleted) gate.Release();
            else _ = _exchange.ContinueWith(_ => gate.Release(), CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        }
    }
    private async Task<PinnedDispatch> AcquirePinnedDispatchAsync(CancellationToken cancellationToken)
    {
        await _pinnedDispatch.WaitAsync(cancellationToken).ConfigureAwait(false);
        return new(_pinnedDispatch);
    }

    private async Task<bool> SendPinnedWireAsync(WidgetPresentationFrame origin, ControllerInputEvent input,
        string? action, string? option, PinnedDispatch dispatch, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var exchange = RequestAsync(BridgeMessageTypes.ControllerInput,
            new BridgeControllerInputRequest(origin.Authority.WidgetId, input, origin.Authority.RuntimeGeneration, action, option, origin.Authority.WorkerRun),
            BridgeMessageTypes.ControllerInputResult, CancellationToken.None);
        dispatch.Track(exchange);
        var reply = await exchange.WaitAsync(cancellationToken).ConfigureAwait(false);
        RequireObjectProperties(reply.Payload, "handled");
        return reply.Payload.GetProperty("handled").GetBoolean();
    }
}
