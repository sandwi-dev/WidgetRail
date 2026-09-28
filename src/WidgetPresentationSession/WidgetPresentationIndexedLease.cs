using WidgetRail.WidgetBridge;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

namespace WidgetRail.WidgetPresentationSession;

/// <summary>
/// Owns worker semantic data, not a realized control. Dispose when the frontend no
/// longer retains the range, popup, focused presentation or artwork demand.
/// </summary>
public sealed class WidgetPresentationIndexedLease : IAsyncDisposable
{
    private readonly WidgetPresentationSession owner;
    internal readonly WidgetPresentationAuthority Authority;
    internal readonly BridgeIndexedRangeRequest Request;
    internal readonly string ScopeId;
    internal readonly CancellationTokenSource Lifetime = new();
    internal readonly TaskCompletionSource Released = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal bool Retired;
    public string LeaseId { get; }
    public IndexedCollectionRange Range { get; }
    public IReadOnlyDictionary<string, BridgeNodeRenderStyles> RenderStyles { get; }
    public bool IsCurrent => owner.IsIndexedLeaseCurrent(this);

    internal WidgetPresentationIndexedLease(WidgetPresentationSession owner, WidgetPresentationAuthority authority,
        BridgeIndexedRangeRequest request, string scopeId, string leaseId, IndexedCollectionRange range, IReadOnlyDictionary<string, BridgeNodeRenderStyles> renderStyles)
    { this.owner = owner; Authority = authority; Request = request; ScopeId = scopeId; LeaseId = leaseId; Range = range; RenderStyles = renderStyles; }

    /// <summary>
    /// Pass the exact frame displayed when input originated. Stale frames are never
    /// silently rebased. Cancellation stops waiting; a sent action may be admitted.
    /// </summary>
    public Task<WidgetOperationAdmission?> AdmitInputAsync(WidgetPresentationAuthority origin,
        string itemKey, ControllerButton button, ControllerEventPhase phase = ControllerEventPhase.Pressed,
        string? contextActionOwnerId = null, string? contextActionId = null,
        long sequence = 0, long monotonicTimestampMicroseconds = 0, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(origin);
        return owner.AdmitIndexedInputAsync(this, origin,
            new(new(LeaseId, itemKey), button, phase, contextActionOwnerId, contextActionId),
            new(ScopeId, origin.SnapshotSequence, sequence, monotonicTimestampMicroseconds), cancellationToken);
    }

    public Task<WidgetEncodedArtwork?> ResolveArtworkAsync(string itemKey, string artworkHandle,
        CancellationToken cancellationToken = default) =>
        owner.ResolveIndexedArtworkAsync(this, itemKey, artworkHandle, cancellationToken);

    public ValueTask DisposeAsync() => new(owner.ReleaseIndexedLeaseAsync(this));
}
