using System.Runtime.CompilerServices;
using WidgetRail.WidgetBridge;

namespace WidgetRail.WidgetPresentationSession;

public sealed partial class WidgetPresentationSession
{
    private sealed class StyleAttempt { internal long Revision = -1; }
    private readonly ConditionalWeakTable<WidgetPresentationFrame, StyleAttempt> _frameStyleAttempts = new();
    private Task? _styleRefresh;
    private bool _styleRefreshRunning;

    // One coalescing lane per session; appearance must not consume provider
    // capacity or enqueue one unbounded task per realized/recycled control.
    private void QueueStyleRefreshLocked()
    {
        if (_disposed || _terminalFailure is not null || _notifiedAppearanceRevision < 0 || _styleRefreshRunning) return;
        var revision = _notifiedAppearanceRevision;
        if (!_states.Values.Any(state => state.LastGood is { } frame && frame.AppearanceRevision < revision &&
                _frameStyleAttempts.GetOrCreateValue(frame).Revision < revision) &&
            !_indexedLeases.Values.Any(lease => IsIndexedLeaseCurrentLocked(lease) && lease.AppearanceRevision < revision &&
                lease.StyleRefreshAttemptedRevision < revision)) return;
        _styleRefreshRunning = true;
        _styleRefresh = Task.Run(RefreshStylesAsync);
    }

    private async Task RefreshStylesAsync()
    {
        while (true)
        {
            WidgetPresentationFrame? frame;
            WidgetPresentationIndexedLease? lease = null;
            long revision;
            CancellationToken token;
            using (_gate.Enter())
            {
                revision = _notifiedAppearanceRevision;
                if (_disposed || _terminalFailure is not null) { _styleRefreshRunning = false; return; }
                frame = _states.Values.Select(state => state.LastGood).FirstOrDefault(candidate => candidate is not null &&
                    candidate.AppearanceRevision < revision && _frameStyleAttempts.GetOrCreateValue(candidate).Revision < revision);
                if (frame is not null) _frameStyleAttempts.GetOrCreateValue(frame).Revision = revision;
                else
                {
                    lease = _indexedLeases.Values.FirstOrDefault(candidate => IsIndexedLeaseCurrentLocked(candidate) &&
                        candidate.AppearanceRevision < revision && candidate.StyleRefreshAttemptedRevision < revision);
                    if (lease is null) { _styleRefreshRunning = false; return; }
                    lease.StyleRefreshAttemptedRevision = revision;
                }
                token = lease?.Lifetime.Token ?? _lifetime.Token;
            }
            try
            {
                using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token, _lifetime.Token);
                deadline.CancelAfter(TimeSpan.FromSeconds(10));
                if (frame is not null) await RefreshFrameStylesAsync(frame, revision, deadline.Token).ConfigureAwait(false);
                else await RefreshLeaseStylesAsync(lease!, revision, deadline.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (_disposed || token.IsCancellationRequested || _lifetime.IsCancellationRequested) { }
            catch (Exception error) when (error is not OutOfMemoryException)
            {
                // Retain the prior valid pixels and authority. An unavailable
                // incarnation never turns a style reply into a data reload.
                RecordDiagnostic("style_refresh_failed", Bound(error.Message), frame?.Authority.WidgetId ?? lease!.Authority.WidgetId);
            }
        }
    }

    private async Task RefreshFrameStylesAsync(WidgetPresentationFrame frame, long revision, CancellationToken token)
    {
        var authority = frame.Authority;
        var response = await RequestAsync(BridgeMessageTypes.RefreshPresentationStyles,
            new BridgePresentationStylesRequest(authority.WidgetId, authority.WidgetInstanceId, authority.RuntimeGeneration,
                authority.PresentationGeneration, authority.SnapshotSequence), BridgeMessageTypes.PresentationStyles, token).ConfigureAwait(false);
        var value = BridgeJson.FromElement<BridgePresentationStylesResponse>(response.Payload);
        if (!MatchesIndexedIdentity(authority, value.WidgetId, value.InstanceId, value.RuntimeGeneration, value.PresentationGeneration) ||
            value.SnapshotSequence != authority.SnapshotSequence || value.AppearanceRevision < revision)
            throw new BridgeProtocolException("Style refresh returned foreign or older presentation authority.");
        var styles = BridgeRenderStyleContract.ValidateAndFreeze(value.RenderStyles,
            BridgeRenderStyleContract.SnapshotNodeIds(frame.Snapshot), requireComplete: true);
        var startPublications = false;
        using (_gate.Enter())
        {
            token.ThrowIfCancellationRequested();
            if (_disposed || _terminalFailure is not null || !_states.TryGetValue(authority.WidgetId, out var current) ||
                !ReferenceEquals(current.LastGood, frame) || value.AppearanceRevision < _notifiedAppearanceRevision) return;
            // Genuine session publication with the same immutable snapshot,
            // query, action origin and preview grants; only computed styles differ.
            CommitStateLocked(current with { LastGood = frame with { RenderStyles = styles, AppearanceRevision = value.AppearanceRevision } }, publish: true);
            startPublications = TakePublicationOwnershipLocked();
        }
        if (startPublications) DrainStatePublications();
    }

    private async Task RefreshLeaseStylesAsync(WidgetPresentationIndexedLease lease, long revision, CancellationToken token)
    {
        var authority = lease.Authority;
        var response = await RequestAsync(BridgeMessageTypes.RefreshIndexedStyles,
            new BridgeIndexedLeaseRequest(authority.WidgetId, authority.WidgetInstanceId, authority.RuntimeGeneration,
                authority.PresentationGeneration, lease.LeaseId), BridgeMessageTypes.IndexedStyles, token).ConfigureAwait(false);
        var value = BridgeJson.FromElement<BridgeIndexedStylesResponse>(response.Payload);
        if (!MatchesIndexedIdentity(authority, value.WidgetId, value.InstanceId, value.RuntimeGeneration, value.PresentationGeneration) ||
            value.LeaseId != lease.LeaseId || value.AppearanceRevision < revision)
            throw new BridgeProtocolException("Style refresh returned foreign or older indexed authority.");
        var styles = BridgeRenderStyleContract.ValidateAndFreeze(value.RenderStyles,
            BridgeRenderStyleContract.RangeNodeIds(lease.Range), requireComplete: true);
        using (_gate.Enter())
        {
            token.ThrowIfCancellationRequested();
            if (!IsIndexedLeaseCurrentLocked(lease) || value.AppearanceRevision < _notifiedAppearanceRevision ||
                value.AppearanceRevision <= lease.AppearanceRevision) return;
            lease.SetStyles(new(value.AppearanceRevision, styles));
        }
        lease.NotifyStylesChanged();
    }
}
