using System.Text.Json;
using GameBarAlternative.PlatformBroker;

namespace GameBarAlternative.WidgetBridge;

internal enum BridgeRequestKind
{
    ListWidgets,
    GetPlatformAppearance,
    GetLauncherExperience,
    GetSnapshot,
    ResolveArtwork,
    RestartWidget,
    SetWidgetLifecycle,
    Action,
    ControllerInput,
    ConnectProtectedWifi,
    InstallLocalWidgetPackage,
    CancelLocalWidgetPackageInstall,
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
    internal bool IsKnown => Kind is not (BridgeRequestKind.Malformed or
        BridgeRequestKind.Unknown);

    internal static BridgeRequestKey Global(BridgeRequestKind kind)
    {
        if (kind is BridgeRequestKind.GetSnapshot or
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

    private static bool IsIdentifier(string? value) =>
        value is { Length: > 0 and <= 128 } &&
        value.All(character => char.IsAsciiLetterOrDigit(character) ||
            character is '-' or '_' or '.');
}

/// <summary>
/// Converts the closed post-handshake request vocabulary into the one typed
/// scheduling key consumed by <see cref="BridgeRequestDispatcher"/>.
/// </summary>
internal static class BridgeRequestClassifier
{
    internal static BridgeRequestKey Classify(BridgeEnvelope request)
    {
        ArgumentNullException.ThrowIfNull(request);
        try
        {
            return request.Type switch
            {
                BridgeMessageTypes.ListWidgets => Empty(
                    request.Payload, BridgeRequestKind.ListWidgets),
                BridgeMessageTypes.GetPlatformAppearance => Empty(
                    request.Payload, BridgeRequestKind.GetPlatformAppearance),
                BridgeMessageTypes.GetLauncherExperience => Empty(
                    request.Payload, BridgeRequestKind.GetLauncherExperience),
                BridgeMessageTypes.GetSnapshot => Widget(
                    BridgeJson.FromElement<WidgetIdRequest>(request.Payload).WidgetId,
                    BridgeRequestKind.GetSnapshot),
                BridgeMessageTypes.ResolveArtwork => Artwork(request.Payload),
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
                BridgeMessageTypes.QuickAction => Widget(
                    BridgeJson.FromElement<BridgeQuickActionRequest>(request.Payload).WidgetId,
                    BridgeRequestKind.QuickAction),
                BridgeMessageTypes.Stop => Empty(request.Payload, BridgeRequestKind.Stop),
                _ => BridgeRequestKey.Global(BridgeRequestKind.Unknown),
            };
        }
        catch (Exception exception) when (exception is JsonException or
            NotSupportedException or BridgeProtocolException)
        {
            return BridgeRequestKey.Global(BridgeRequestKind.Malformed);
        }
    }

    private static BridgeRequestKey Empty(JsonElement payload, BridgeRequestKind kind) =>
        payload.ValueKind == JsonValueKind.Object && !payload.EnumerateObject().Any()
            ? BridgeRequestKey.Global(kind)
            : BridgeRequestKey.Global(BridgeRequestKind.Malformed);

    private static BridgeRequestKey Widget(string widgetId, BridgeRequestKind kind) =>
        BridgeRequestKey.Widget(kind, widgetId);

    private static BridgeRequestKey Artwork(JsonElement payload)
    {
        var request = BridgeJson.FromElement<BridgeArtworkRequest>(payload);
        _ = BridgeRequestKey.Widget(BridgeRequestKind.GetSnapshot, request.WidgetId);
        if (!AppLibraryArtworkRegistry.IsHandle(request.ArtworkHandle))
            throw new BridgeProtocolException("Artwork handle is invalid.");
        return BridgeRequestKey.Global(BridgeRequestKind.ResolveArtwork);
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
}
