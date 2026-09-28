using WidgetRail.WidgetBridge;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

namespace WidgetRail.WidgetPresentationSession;

public sealed record WidgetPresentationSessionOptions
{
    public string ClientName { get; init; } = "ManagedPresentationHost";
    public TimeSpan ConnectTimeout { get; init; } = TimeSpan.FromSeconds(5);
    public int MaximumMessageBytes { get; init; } = BridgeProtocol.DefaultMaximumMessageBytes;
    public int MaximumPendingRequests { get; init; } = 32;
    public int MaximumPendingArtworkRequests { get; init; } = 32;
    /// <summary>Bounds local artwork demand, including admission and completion. Does not cancel server-side decoding.</summary>
    public TimeSpan ArtworkTimeout { get; init; } = TimeSpan.FromSeconds(30);
    public int MaximumPendingIndexedRanges { get; init; } = 4;
    /// <summary>Provider range budget; independent of ordinary action/IPC timeouts.</summary>
    public TimeSpan IndexedRangeTimeout { get; init; } = TimeSpan.FromSeconds(35);
    public int MaximumRetainedDiagnostics { get; init; } = 64;

    internal void Validate()
    {
        if (string.IsNullOrWhiteSpace(ClientName) || ClientName.Length > 128)
            throw new ArgumentException("The presentation client name is invalid.", nameof(ClientName));
        if (ConnectTimeout <= TimeSpan.Zero || ConnectTimeout > TimeSpan.FromMinutes(1))
            throw new ArgumentOutOfRangeException(nameof(ConnectTimeout));
        if (MaximumMessageBytes is < 256 or > BridgeProtocol.AbsoluteMaximumMessageBytes)
            throw new ArgumentOutOfRangeException(nameof(MaximumMessageBytes));
        if (MaximumPendingRequests is < 1 or > 256)
            throw new ArgumentOutOfRangeException(nameof(MaximumPendingRequests));
        if (MaximumPendingArtworkRequests is < 1 or > 256)
            throw new ArgumentOutOfRangeException(nameof(MaximumPendingArtworkRequests));
        if (ArtworkTimeout < TimeSpan.FromMilliseconds(100) || ArtworkTimeout > TimeSpan.FromMinutes(2))
            throw new ArgumentOutOfRangeException(nameof(ArtworkTimeout));
        if (MaximumPendingIndexedRanges is < 1 or > 16)
            throw new ArgumentOutOfRangeException(nameof(MaximumPendingIndexedRanges));
        if (IndexedRangeTimeout < TimeSpan.FromMilliseconds(100) || IndexedRangeTimeout > TimeSpan.FromMinutes(2))
            throw new ArgumentOutOfRangeException(nameof(IndexedRangeTimeout));
        if (MaximumRetainedDiagnostics is < 1 or > 256)
            throw new ArgumentOutOfRangeException(nameof(MaximumRetainedDiagnostics));
    }
}

public sealed record WidgetPresentationCatalog(
    long Revision,
    IReadOnlyList<BridgeWidgetDescriptor> Widgets)
{
    /// <summary>False while installed package admission is still in progress.</summary>
    public bool IsComplete { get; init; }
}

public sealed record WidgetPresentationTarget(
    long CatalogRevision,
    BridgeWidgetDescriptor Descriptor);

public sealed record WidgetPresentationAuthority(
    string WidgetId,
    string RuntimeGeneration,
    string PresentationGeneration,
    long SessionGeneration,
    string WidgetInstanceId,
    long SnapshotSequence,
    string ActiveInputScopeId);

public sealed record WidgetPresentationFrame(
    WidgetPresentationAuthority Authority,
    BridgeWidgetDescriptor Descriptor,
    ViewSnapshot Snapshot,
    IReadOnlyDictionary<string, BridgeNodeRenderStyles> RenderStyles);

public sealed record WidgetPresentationFailure(
    string WidgetId,
    string Code,
    string Message,
    string? RuntimeGeneration = null,
    string? ActionId = null,
    string? SourceElementId = null,
    int? ExitCode = null,
    string? DiagnosticCode = null,
    int? RestartsUsed = null,
    bool CanRestart = false);

public sealed record WidgetPresentationState(
    string WidgetId,
    WidgetPresentationFrame? LastGood,
    WidgetPresentationFailure? Failure,
    long InvalidationRevision)
{
    public long PublicationRevision { get; init; }
}

public sealed record WidgetPresentationInvalidation(string WidgetId, long Revision);

/// <summary>Identifies the worker incarnation admitted when an effect arrived.</summary>
public sealed record WidgetHostEffectAuthority(
    string WidgetId, string WidgetInstanceId, string RuntimeGeneration, long SessionGeneration);

public enum WidgetHostEffectKind
{
    /// <summary>An optional effect unknown to this frontend; never execute it.</summary>
    Unsupported,
    CloseOverlayAfterAppLaunch,
    ActivateTaskWindow,
}

/// <summary>
/// Host-private native window identity. Revalidate process creation and class identity
/// with the platform adapter before activation. Never send this to widget workers.
/// </summary>
public sealed record WidgetHostWindowTarget(
    string WindowId, ulong Handle, uint ProcessId, ulong ProcessCreated, string ClassName);

/// <summary>
/// Broker-issued, one-shot effect. Sequence is session-wide. InitiatedAtMilliseconds
/// uses Windows uptime, not wall time. A UI dispatcher must recheck authority and
/// its current visible session before acting; admission is not platform execution.
/// Unsupported effects are observable but must be ignored. No raw provider payload is exposed.
/// </summary>
public sealed record WidgetPresentationHostEffect(
    WidgetHostEffectAuthority Authority, WidgetHostEffectKind Kind, string WireName,
    long Sequence, long InitiatedAtMilliseconds, WidgetHostWindowTarget? WindowTarget);

public sealed class WidgetHostEffectEventArgs(WidgetPresentationHostEffect effect) : EventArgs
{
    public WidgetPresentationHostEffect Effect { get; } = effect;
}

/// <summary>A revision notification, not a replacement catalog or appearance snapshot.</summary>
public sealed class WidgetRevisionChangedEventArgs(long revision) : EventArgs
{
    public long Revision { get; } = revision;
}

public sealed record WidgetPresentationArtwork(
    WidgetPresentationAuthority Authority,
    string ArtworkHandle,
    WidgetArtworkContentType ContentType,
    ReadOnlyMemory<byte> EncodedBytes);

public sealed record WidgetPresentationDiagnostic(
    DateTimeOffset Timestamp,
    string Code,
    string Message,
    string? WidgetId = null);

public sealed class WidgetPresentationChangedEventArgs(WidgetPresentationState state) : EventArgs
{
    public WidgetPresentationState State { get; } = state;
}

public sealed class WidgetPresentationInvalidatedEventArgs(
    WidgetPresentationInvalidation invalidation) : EventArgs
{
    public WidgetPresentationInvalidation Invalidation { get; } = invalidation;
}

public sealed class WidgetPresentationArtworkEventArgs(
    WidgetPresentationArtwork artwork) : EventArgs
{
    public WidgetPresentationArtwork Artwork { get; } = artwork;
}

public sealed class WidgetPresentationDiagnosticEventArgs(
    WidgetPresentationDiagnostic diagnostic) : EventArgs
{
    public WidgetPresentationDiagnostic Diagnostic { get; } = diagnostic;
}

public sealed class WidgetPresentationSessionException : Exception
{
    public WidgetPresentationSessionException(string code, string message)
        : base(message) => Code = code;

    public WidgetPresentationSessionException(string code, string message, Exception innerException)
        : base(message, innerException) => Code = code;

    public string Code { get; }
}

internal sealed record PendingArtwork(
    WidgetPresentationAuthority Authority,
    string ArtworkHandle,
    CancellationToken CancellationToken,
    TaskCompletionSource<WidgetPresentationArtwork> Completion);

internal sealed record BridgeRequestFailure(string Code, string Message);

internal static class PresentationContractLimits
{
    internal const int MaximumDiagnosticTextLength = 512;
}
