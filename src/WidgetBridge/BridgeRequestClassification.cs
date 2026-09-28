using System.Text.Json;
using WidgetRail.PlatformBroker;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.WidgetBridge;

internal enum BridgeRequestKind
{
    ListWidgets,
    GetPlatformAppearance,
    ControllerControl,
    ApplicationControl,
    WindowPreviewPermissions,
    GetSnapshot,
    ReadIndexedRange,
    CancelIndexedRange,
    AcquireIndexedRange,
    ReleaseIndexedLease,
    IndexedInput,
    ResolveIndexedArtwork,
    CancelIndexedArtwork,
    ResolveArtwork,
    ResolvePackageIcon,
    ResolveEmbeddedMedia,
    EmbeddedMediaPlaybackEvent,
    RestartWidget,
    SetWidgetLifecycle,
    Action,
    ControllerInput,
    ConnectProtectedWifi,
    InstallLocalWidgetPackage,
    CancelLocalWidgetPackageInstall,
    ApproveLocalWidgetPackageInstall,
    QuickAction,
    Stop,
    Malformed,
    Unknown,
}

internal readonly record struct BridgeRequestKey
{
    private BridgeRequestKey(BridgeRequestKind kind, string? widgetId)
    {
        Kind = kind;
        WidgetId = widgetId;
    }

    internal BridgeRequestKind Kind { get; }
    internal string? WidgetId { get; }
    internal BridgeIndexedRangeRequest? IndexedRange { get; private init; }
    internal BridgeIndexedArtworkRequest? IndexedArtwork { get; private init; }
    internal bool IsIndependent => Kind is BridgeRequestKind.ReadIndexedRange or BridgeRequestKind.CancelIndexedRange or
        BridgeRequestKind.AcquireIndexedRange or BridgeRequestKind.ReleaseIndexedLease or
        BridgeRequestKind.ResolveIndexedArtwork or BridgeRequestKind.CancelIndexedArtwork;
    internal bool IsIndexedProvider => Kind is BridgeRequestKind.ReadIndexedRange or BridgeRequestKind.AcquireIndexedRange or BridgeRequestKind.ResolveIndexedArtwork;
    internal bool IsKnown => Kind is not (BridgeRequestKind.Malformed or
        BridgeRequestKind.Unknown);

    internal static BridgeRequestKey Global(BridgeRequestKind kind)
    {
        if (kind is BridgeRequestKind.GetSnapshot or
            BridgeRequestKind.ReadIndexedRange or
            BridgeRequestKind.CancelIndexedRange or
            BridgeRequestKind.AcquireIndexedRange or BridgeRequestKind.ReleaseIndexedLease or BridgeRequestKind.IndexedInput or
            BridgeRequestKind.ResolveIndexedArtwork or BridgeRequestKind.CancelIndexedArtwork or
            BridgeRequestKind.ResolveArtwork or
            BridgeRequestKind.ResolvePackageIcon or
            BridgeRequestKind.ResolveEmbeddedMedia or
            BridgeRequestKind.EmbeddedMediaPlaybackEvent or
            BridgeRequestKind.RestartWidget or
            BridgeRequestKind.SetWidgetLifecycle or
            BridgeRequestKind.Action or
            BridgeRequestKind.ControllerInput or
            BridgeRequestKind.ConnectProtectedWifi or
            BridgeRequestKind.QuickAction)
            throw new ArgumentOutOfRangeException(nameof(kind));
        return new BridgeRequestKey(kind, null);
    }

    internal static BridgeRequestKey Widget(BridgeRequestKind kind, string? widgetId)
    {
        if (kind is not (BridgeRequestKind.GetSnapshot or
            BridgeRequestKind.ReadIndexedRange or
            BridgeRequestKind.CancelIndexedRange or
            BridgeRequestKind.AcquireIndexedRange or BridgeRequestKind.ReleaseIndexedLease or BridgeRequestKind.IndexedInput or
            BridgeRequestKind.ResolveIndexedArtwork or BridgeRequestKind.CancelIndexedArtwork or
            BridgeRequestKind.ResolveArtwork or
            BridgeRequestKind.ResolvePackageIcon or
            BridgeRequestKind.ResolveEmbeddedMedia or
            BridgeRequestKind.EmbeddedMediaPlaybackEvent or
            BridgeRequestKind.RestartWidget or
            BridgeRequestKind.SetWidgetLifecycle or
            BridgeRequestKind.Action or
            BridgeRequestKind.ControllerInput or
            BridgeRequestKind.ConnectProtectedWifi or
            BridgeRequestKind.QuickAction))
            throw new ArgumentOutOfRangeException(nameof(kind));
        if (!IsIdentifier(widgetId))
            throw new BridgeProtocolException("Bridge request widget ID is invalid.");
        return new BridgeRequestKey(kind, widgetId);
    }

    internal static BridgeRequestKey Indexed(BridgeRequestKind kind, BridgeIndexedRangeRequest request) =>
        Widget(kind, request.WidgetId) with { IndexedRange = request };
    internal static BridgeRequestKey IndexedArtworkDemand(BridgeRequestKind kind, BridgeIndexedArtworkRequest request) =>
        Widget(kind, request.WidgetId) with { IndexedArtwork = request };

    private static bool IsIdentifier(string? value) =>
        value is { Length: > 0 and <= 128 } &&
        value.All(character => char.IsAsciiLetterOrDigit(character) ||
            character is '-' or '_' or '.');

    internal static bool IsBoundedIdentifier(string? value) => IsIdentifier(value);
}

/// <summary>
/// Converts the closed post-handshake request vocabulary into the one typed
/// scheduling key consumed by <see cref="BridgeRequestDispatcher"/>.
/// </summary>
internal static class BridgeRequestClassifier
{
    private static BridgeRequestKey WindowPreviewPermissions(JsonElement payload)
    {
        var request = BridgeJson.FromElement<WidgetIdRequest>(payload);
        if (!BridgeRequestKey.IsBoundedIdentifier(request.WidgetId))
            throw new BridgeProtocolException("Preview widget ID is invalid.");
        return BridgeRequestKey.Global(BridgeRequestKind.WindowPreviewPermissions);
    }

    private static BridgeRequestKey ControllerControl(JsonElement payload)
    {
        var status = BridgeJson.FromElement<WidgetRail.PlatformDiagnostics.ControllerControlStatus>(payload);
        if (!Enum.IsDefined(status.State)) throw new BridgeProtocolException("Invalid controller status.");
        return BridgeRequestKey.Global(BridgeRequestKind.ControllerControl);
    }

    internal static BridgeRequestKey Classify(BridgeEnvelope request)
    {
        ArgumentNullException.ThrowIfNull(request);
        try
        {
            return request.Type switch
            {
                BridgeMessageTypes.ListWidgets => Empty(
                    request.Payload, BridgeRequestKind.ListWidgets),
                BridgeMessageTypes.GetPlatformAppearance => Appearance(request.Payload),
                BridgeMessageTypes.ControllerControl => ControllerControl(request.Payload),
                BridgeMessageTypes.WindowPreviewPermissions => WindowPreviewPermissions(request.Payload),
                BridgeMessageTypes.ApplicationControl => BridgeRequestKey.Global(BridgeRequestKind.ApplicationControl),
                BridgeMessageTypes.ReadIndexedRange => IndexedRange(request.Payload, BridgeRequestKind.ReadIndexedRange),
                BridgeMessageTypes.CancelIndexedRange => IndexedRange(request.Payload, BridgeRequestKind.CancelIndexedRange),
                BridgeMessageTypes.AcquireIndexedRange => IndexedRange(request.Payload, BridgeRequestKind.AcquireIndexedRange),
                BridgeMessageTypes.ReleaseIndexedLease => IndexedLease(request.Payload),
                BridgeMessageTypes.IndexedInput => IndexedInput(request.Payload),
                BridgeMessageTypes.ResolveIndexedArtwork => IndexedArtwork(request.Payload, BridgeRequestKind.ResolveIndexedArtwork),
                BridgeMessageTypes.CancelIndexedArtwork => IndexedArtwork(request.Payload, BridgeRequestKind.CancelIndexedArtwork),
                BridgeMessageTypes.GetSnapshot => Widget(
                    BridgeJson.FromElement<BridgePresentationRequest>(request.Payload).WidgetId,
                    BridgeRequestKind.GetSnapshot),
                BridgeMessageTypes.ResolveArtwork => Artwork(request.Payload),
                BridgeMessageTypes.ResolvePackageIcon => PackageIcon(request.Payload),
                BridgeMessageTypes.ResolveEmbeddedMedia => EmbeddedMedia(request.Payload),
                BridgeMessageTypes.EmbeddedMediaPlaybackEvent => EmbeddedMediaEvent(request.Payload),
                BridgeMessageTypes.RestartWidget => Widget(
                    BridgeJson.FromElement<WidgetIdRequest>(request.Payload).WidgetId,
                    BridgeRequestKind.RestartWidget),
                BridgeMessageTypes.SetWidgetLifecycle => Widget(
                    BridgeJson.FromElement<BridgeWidgetLifecycleRequest>(request.Payload).WidgetId,
                    BridgeRequestKind.SetWidgetLifecycle),
                BridgeMessageTypes.Action => Widget(
                    BridgeJson.FromElement<BridgeActionRequest>(request.Payload).WidgetId,
                    BridgeRequestKind.Action),
                BridgeMessageTypes.ControllerInput => Widget(
                    BridgeJson.FromElement<BridgeControllerInputRequest>(request.Payload).WidgetId,
                    BridgeRequestKind.ControllerInput),
                BridgeMessageTypes.ConnectProtectedWifi => Widget(
                    BridgeJson.FromElement<BridgeProtectedWifiRequest>(request.Payload).WidgetId,
                    BridgeRequestKind.ConnectProtectedWifi),
                BridgeMessageTypes.InstallLocalWidgetPackage => LocalPackageInstall(
                    request.Payload),
                BridgeMessageTypes.CancelLocalWidgetPackageInstall => LocalPackageCancel(
                    request.Payload),
                BridgeMessageTypes.ApproveLocalWidgetPackageInstall => LocalPackageApproval(request.Payload),
                BridgeMessageTypes.QuickAction => Widget(
                    BridgeJson.FromElement<BridgeQuickActionRequest>(request.Payload).WidgetId,
                    BridgeRequestKind.QuickAction),
                BridgeMessageTypes.Stop => Empty(request.Payload, BridgeRequestKind.Stop),
                _ => BridgeRequestKey.Global(BridgeRequestKind.Unknown),
            };
        }
        catch (Exception exception) when (exception is JsonException or
            NotSupportedException or BridgeProtocolException or ArgumentException)
        {
            return BridgeRequestKey.Global(BridgeRequestKind.Malformed);
        }
    }

    private static void IndexedIdentity(string widget, string instance, string runtime, string presentation)
    {
        if (!new[] { widget, instance, runtime, presentation }.All(BridgeRequestKey.IsBoundedIdentifier))
            throw new BridgeProtocolException("Indexed identity is invalid.");
    }

    private static BridgeRequestKey IndexedLease(JsonElement payload)
    {
        var request = BridgeJson.FromElement<BridgeIndexedLeaseRequest>(payload);
        IndexedIdentity(request.WidgetId, request.InstanceId, request.RuntimeGeneration, request.PresentationGeneration);
        if (!Guid.TryParseExact(request.LeaseId, "N", out _)) throw new BridgeProtocolException("Indexed lease ID is invalid.");
        return Widget(request.WidgetId, BridgeRequestKind.ReleaseIndexedLease);
    }

    private static BridgeRequestKey IndexedInput(JsonElement payload)
    {
        var request = BridgeJson.FromElement<BridgeIndexedInputRequest>(payload);
        IndexedIdentity(request.WidgetId, request.InstanceId, request.RuntimeGeneration, request.PresentationGeneration);
        IndexedCollectionInputContract.ValidateInput(request.Input);
        IndexedCollectionInputContract.ValidateContext(request.Context);
        return Widget(request.WidgetId, BridgeRequestKind.IndexedInput);
    }

    private static BridgeRequestKey IndexedArtwork(JsonElement payload, BridgeRequestKind kind)
    {
        var request = BridgeJson.FromElement<BridgeIndexedArtworkRequest>(payload);
        IndexedIdentity(request.WidgetId, request.InstanceId, request.RuntimeGeneration, request.PresentationGeneration);
        IndexedCollectionInputContract.ValidateReference(request.Item);
        if (!BridgeRequestKey.IsBoundedIdentifier(request.ArtworkHandle) || !BridgeRequestKey.IsBoundedIdentifier(request.DemandId) || request.DemandId.Length > 64)
            throw new BridgeProtocolException("Indexed artwork demand is invalid.");
        return BridgeRequestKey.IndexedArtworkDemand(kind, request);
    }

    private static BridgeRequestKey Appearance(JsonElement payload)
    {
        if (payload.ValueKind != JsonValueKind.Object || payload.EnumerateObject().Any(p => p.Name != "display"))
            return BridgeRequestKey.Global(BridgeRequestKind.Malformed);
        if (payload.TryGetProperty("display", out var value))
        {
            BridgeJson.FromElement<BridgeDisplayContextRequest>(value).Validate();
        }
        return BridgeRequestKey.Global(BridgeRequestKind.GetPlatformAppearance);
    }

    private static BridgeRequestKey Empty(JsonElement payload, BridgeRequestKind kind) =>
        payload.ValueKind == JsonValueKind.Object && !payload.EnumerateObject().Any()
            ? BridgeRequestKey.Global(kind)
            : BridgeRequestKey.Global(BridgeRequestKind.Malformed);

    private static BridgeRequestKey Widget(string widgetId, BridgeRequestKind kind) =>
        BridgeRequestKey.Widget(kind, widgetId);

    private static BridgeRequestKey IndexedRange(JsonElement payload, BridgeRequestKind kind)
    {
        var request = BridgeJson.FromElement<BridgeIndexedRangeRequest>(payload);
        BridgeIndexedRangeValidation.Validate(request);
        return BridgeRequestKey.Indexed(kind, request);
    }

    private static BridgeRequestKey Artwork(JsonElement payload)
    {
        var request = BridgeJson.FromElement<BridgeArtworkRequest>(payload);
        _ = BridgeRequestKey.Widget(BridgeRequestKind.GetSnapshot, request.WidgetId);
        if (!BridgeRequestKey.IsBoundedIdentifier(request.ArtworkHandle))
            throw new BridgeProtocolException("Artwork handle is invalid.");
        if (request.DemandId is { } demandId &&
            (demandId.Length > 64 || !BridgeRequestKey.IsBoundedIdentifier(demandId)))
            throw new BridgeProtocolException("Artwork demand identifier is invalid.");
        if ((request.RuntimeGeneration is null) !=
                (request.PresentationGeneration is null) ||
            (request.RuntimeGeneration is not null &&
             (!BridgeRequestKey.IsBoundedIdentifier(request.RuntimeGeneration) ||
              !BridgeRequestKey.IsBoundedIdentifier(request.PresentationGeneration))))
            throw new BridgeProtocolException("Artwork generation authority is invalid.");
        return BridgeRequestKey.Widget(BridgeRequestKind.ResolveArtwork, request.WidgetId);
    }

    private static BridgeRequestKey PackageIcon(JsonElement payload)
    {
        var request = BridgeJson.FromElement<BridgePackageIconRequest>(payload);
        var key = BridgeRequestKey.Widget(
            BridgeRequestKind.ResolvePackageIcon, request.WidgetId);
        if (!BridgeRequestKey.IsBoundedIdentifier(request.RuntimeGeneration) ||
            !BridgeRequestKey.IsBoundedIdentifier(request.PresentationGeneration) ||
            !WidgetManifestValidator.IsPackageIconAssetId(request.AssetId) ||
            !IsSha256(request.PackageContentDigest) ||
            !IsSha256(request.SourceSha256) ||
            !IsSha256(request.NormalizedSha256))
            throw new BridgeProtocolException(
                "Package icon request authority is invalid.");
        return key;
    }

    private static bool IsSha256(string? value) =>
        value is { Length: 64 } && value.All(character =>
            character is >= '0' and <= '9' or >= 'a' and <= 'f');

    private static BridgeRequestKey EmbeddedMedia(JsonElement payload)
    {
        var request = BridgeJson.FromElement<BridgeEmbeddedMediaRequest>(payload);
        var key = BridgeRequestKey.Widget(
            BridgeRequestKind.ResolveEmbeddedMedia, request.WidgetId);
        if (!BridgeRequestKey.IsBoundedIdentifier(request.InstanceId) ||
            !BridgeRequestKey.IsBoundedIdentifier(request.RuntimeGeneration) ||
            !BridgeRequestKey.IsBoundedIdentifier(request.PresentationGeneration) ||
            request.Sequence <= 0 ||
            !BridgeRequestKey.IsBoundedIdentifier(request.SessionId))
            throw new BridgeProtocolException(
                "Embedded media request authority is invalid.");
        return key;
    }

    private static BridgeRequestKey EmbeddedMediaEvent(JsonElement payload)
    {
        var request = BridgeJson.FromElement<BridgeEmbeddedMediaPlaybackEventRequest>(payload);
        var key = BridgeRequestKey.Widget(
            BridgeRequestKind.EmbeddedMediaPlaybackEvent, request.WidgetId);
        if (!BridgeRequestKey.IsBoundedIdentifier(request.InstanceId) ||
            !BridgeRequestKey.IsBoundedIdentifier(request.RuntimeGeneration) ||
            !BridgeRequestKey.IsBoundedIdentifier(request.PresentationGeneration) ||
            request.Sequence <= 0 || request.Event is null ||
            !BridgeRequestKey.IsBoundedIdentifier(request.Event.SessionId))
            throw new BridgeProtocolException(
                "Embedded media event authority is invalid.");
        return key;
    }

    private static BridgeRequestKey LocalPackageInstall(JsonElement payload)
    {
        var request = BridgeJson.FromElement<BridgeLocalWidgetPackageInstallRequest>(payload);
        _ = BridgeRequestKey.Widget(
            BridgeRequestKind.GetSnapshot, request.Origin.WidgetId);
        return BridgeRequestKey.Global(BridgeRequestKind.InstallLocalWidgetPackage);
    }

    private static BridgeRequestKey LocalPackageCancel(JsonElement payload)
    {
        _ = BridgeJson.FromElement<BridgeLocalWidgetPackageInstallCancelRequest>(payload);
        return BridgeRequestKey.Global(BridgeRequestKind.CancelLocalWidgetPackageInstall);
    }

    private static BridgeRequestKey LocalPackageApproval(JsonElement payload)
    {
        _ = BridgeJson.FromElement<BridgeLocalWidgetPackageInstallApprovalRequest>(payload);
        return BridgeRequestKey.Global(BridgeRequestKind.ApproveLocalWidgetPackageInstall);
    }
}
