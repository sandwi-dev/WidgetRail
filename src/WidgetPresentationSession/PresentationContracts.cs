using WidgetRail.WidgetBridge;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetRuntime;

namespace WidgetRail.WidgetPresentationSession;

public sealed record WidgetPresentationSessionOptions
{
    public string ClientName { get; init; } = "ManagedPresentationHost";
    public TimeSpan ConnectTimeout { get; init; } = TimeSpan.FromSeconds(5);
    public int MaximumMessageBytes { get; init; } = BridgeProtocol.DefaultMaximumMessageBytes;
    public int MaximumPendingRequests { get; init; } = 32;
    public int MaximumPendingArtworkRequests { get; init; } = 32;
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
        if (MaximumRetainedDiagnostics is < 1 or > 256)
            throw new ArgumentOutOfRangeException(nameof(MaximumRetainedDiagnostics));
    }
}

public sealed record WidgetPresentationCatalog(
    long Revision,
    IReadOnlyList<BridgeWidgetDescriptor> Widgets);

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

public sealed record WidgetPresentationArtwork(
    WidgetPresentationAuthority Authority,
    string ArtworkHandle,
    ReadOnlyMemory<byte> PngBytes);

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
    TaskCompletionSource<WidgetPresentationArtwork> Completion);

internal sealed record BridgeRequestFailure(string Code, string Message);

internal static class PresentationContractLimits
{
    internal const int MaximumDiagnosticTextLength = 512;
    internal const int MaximumArtworkBytes = 8 * 1024 * 1024;
}
