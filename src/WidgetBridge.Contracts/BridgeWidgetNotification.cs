using WidgetRail.WidgetProtocol;
namespace WidgetRail.WidgetBridge;
internal sealed record BridgeWidgetNotification(string WidgetId, string InstanceId, string RuntimeGeneration,
    long Sequence, long InitiatedAtMilliseconds, WidgetNotificationCommand Command, string? IntentToken = null);
internal sealed record BridgeNotificationIntentPrepare(string WidgetId, string IntentToken);
