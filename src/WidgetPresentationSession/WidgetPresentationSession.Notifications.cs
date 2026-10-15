using System.Text.Json;
using WidgetRail.WidgetBridge;
using WidgetRail.WidgetProtocol;
namespace WidgetRail.WidgetPresentationSession;

public sealed record WidgetPresentationNotification(WidgetHostEffectAuthority Authority, WidgetNotificationCommand Command, string? IntentToken = null);
public sealed class WidgetNotificationEventArgs(WidgetPresentationNotification notification) : EventArgs
{
    public WidgetPresentationNotification Notification { get; } = notification;
}
public sealed partial class WidgetPresentationSession
{
    private readonly Dictionary<string, long> notificationSequences = new(StringComparer.Ordinal);
    public event EventHandler<WidgetNotificationEventArgs>? NotificationReceived;
    private void HandleWidgetNotification(JsonElement payload)
    {
        var message = BridgeJson.FromElement<BridgeWidgetNotification>(payload);
        _ = ReadHostIdentifier(payload, "widgetId");
        _ = ReadHostIdentifier(payload, "instanceId");
        _ = ReadHostIdentifier(payload, "runtimeGeneration");
        if (message.Sequence <= 0 || message.InitiatedAtMilliseconds < 0 || !WidgetNotificationContract.IsValid(message.Command) ||
            (message.Command.Notification?.OpenIntent is null ? message.IntentToken is not null : !Guid.TryParseExact(message.IntentToken, "N", out _)))
            throw new BridgeProtocolException("Invalid widget notification.");
        WidgetHostEffectAuthority? authority = null;
        using (_gate.Enter())
        {
            if (_disposed || _terminalFailure is not null || message.Sequence <= notificationSequences.GetValueOrDefault(message.WidgetId)) return;
            if (_descriptors.TryGetValue(message.WidgetId, out var descriptor) && descriptor.InstanceId == message.InstanceId &&
                descriptor.RuntimeGeneration == message.RuntimeGeneration &&
                (!_hostEffectAdmissionBoundaries.TryGetValue(descriptor.Id, out var boundary) || message.InitiatedAtMilliseconds > boundary))
            {
                notificationSequences[descriptor.Id] = message.Sequence;
                authority = new(descriptor.Id, descriptor.InstanceId, descriptor.RuntimeGeneration, _sessionGenerations.GetValueOrDefault(descriptor.Id));
            }
        }
        if (authority is not null) NotificationReceived?.Invoke(this, new(new(authority, message.Command, message.IntentToken)));
    }

    public async Task<WidgetIntentPreparation> PrepareNotificationIntentAsync(WidgetPresentationNotification notification, CancellationToken token = default)
    {
        if (notification.IntentToken is null || !IsHostEffectAuthorityCurrent(notification.Authority))
            throw OrdinaryInputStale("Notification source is no longer current.");
        var response = await RequestAsync(BridgeMessageTypes.PrepareNotificationIntent,
            new BridgeNotificationIntentPrepare(notification.Authority.WidgetId, notification.IntentToken), BridgeMessageTypes.IntentPrepared, token).ConfigureAwait(false);
        var result = BridgeJson.FromElement<WidgetIntentPreparation>(response.Payload);
        ValidateIntentPreparation(result, notification.Authority.WidgetId);
        return result;
    }
    public async Task ReleaseNotificationIntentAsync(WidgetPresentationNotification notification, CancellationToken token = default)
    {
        if (notification.IntentToken is null) return;
        await RequestAsync(BridgeMessageTypes.ReleaseNotificationIntent, new BridgeNotificationIntentPrepare(notification.Authority.WidgetId, notification.IntentToken),
            BridgeMessageTypes.Acknowledged, token).ConfigureAwait(false);
    }
    public async Task<WidgetIntentPreparation> PrepareAppUpdateIntentAsync(CancellationToken token = default)
    {
        var response = await RequestAsync(BridgeMessageTypes.PrepareAppUpdateIntent, new BridgeEmptyPayload(), BridgeMessageTypes.IntentPrepared, token).ConfigureAwait(false);
        var result = BridgeJson.FromElement<WidgetIntentPreparation>(response.Payload);
        ValidateIntentPreparation(result, "settings");
        return result;
    }
}
