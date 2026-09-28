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
    private BridgeResolvedStyleSnapshot styles;
    public IReadOnlyDictionary<string, BridgeNodeRenderStyles> RenderStyles => Volatile.Read(ref styles).RenderStyles;
    public long AppearanceRevision => Volatile.Read(ref styles).Revision;
    /// <summary>Styles changed on the session thread. Marshal to the native UI dispatcher.</summary>
    public event EventHandler? RenderStylesChanged;
    internal long StyleRefreshAttemptedRevision = -1;
    internal void SetStyles(BridgeResolvedStyleSnapshot value) => Volatile.Write(ref styles, value);
    internal void NotifyStylesChanged() => RenderStylesChanged?.Invoke(this, EventArgs.Empty);
    public bool IsCurrent => owner.IsIndexedLeaseCurrent(this);

    internal WidgetPresentationIndexedLease(WidgetPresentationSession owner, WidgetPresentationAuthority authority,
        BridgeIndexedRangeRequest request, string scopeId, string leaseId, IndexedCollectionRange range, IReadOnlyDictionary<string, BridgeNodeRenderStyles> renderStyles,
        long appearanceRevision = 0)
    { this.owner = owner; Authority = authority; Request = request; ScopeId = scopeId; LeaseId = leaseId; Range = range; styles = new(appearanceRevision, renderStyles); }

    /// <summary>
    /// Tests ownership using the exact displayed frame and the same shortcut
    /// resolver as worker admission. Unavailable declarations still claim input.
    /// A null admission reply is not an ownership test: it also means stale input.
    /// Throws for stale frame/scope/lease rather than returning false.
    /// </summary>
    public bool ClaimsInput(WidgetPresentationAuthority origin, string itemKey, ControllerButton button,
        ControllerEventPhase phase = ControllerEventPhase.Pressed) => owner.IndexedInputIsClaimed(this, origin, itemKey, button, phase);

    /// <summary>
    /// Uses the exact session-published frame currently displayed by the native
    /// UI. Unrelated newer publications are allowed only while query, scope and
    /// input binding remain current. No sequence is substituted or replayed.
    /// </summary>
    public bool ClaimsInput(WidgetPresentationFrame displayed, string itemKey, ControllerButton button,
        ControllerEventPhase phase = ControllerEventPhase.Pressed) => owner.DisplayedIndexedInputIsClaimed(this, displayed, itemKey, button, phase);

    public Task<WidgetOperationAdmission?> AdmitInputAsync(WidgetPresentationFrame displayed,
        string itemKey, ControllerButton button, ControllerEventPhase phase = ControllerEventPhase.Pressed,
        string? contextActionOwnerId = null, string? contextActionId = null,
        long sequence = 0, long monotonicTimestampMicroseconds = 0, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(displayed);
        return owner.AdmitIndexedInputAsync(this, displayed.Authority,
            new(new(LeaseId, itemKey), button, phase, contextActionOwnerId, contextActionId),
            new(ScopeId, displayed.Authority.SnapshotSequence, sequence, monotonicTimestampMicroseconds), cancellationToken, displayed);
    }

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
