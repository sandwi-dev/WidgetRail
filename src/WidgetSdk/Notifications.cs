using WidgetRail.WidgetProtocol;

namespace WidgetRail.WidgetSdk;

public static class WidgetNotificationCapabilities
{
    public const string Service = "widget.notifications.v1";
    public static WidgetCapabilityOperation<WidgetNotificationCommand, WidgetNotificationReceipt> Publish { get; } = new(Service, "notifications.publish");
}

/// <summary>Available to host-launched widgets, including background residents. Acceptance means delivered to the host;
/// preferences, placement and quiet periods may suppress banners. Pending entries last for the current host session.</summary>
public sealed class WidgetNotificationService
{
    private readonly IWidgetCapabilityClient client;
    internal WidgetNotificationService(IWidgetCapabilityClient client) => this.client = client;
    public ValueTask<WidgetNotificationReceipt> ShowAsync(WidgetNotification notification, CancellationToken cancellationToken = default)
    {
        if (!WidgetNotificationContract.IsValid(notification)) throw new ArgumentException("Notification is invalid.", nameof(notification));
        return SendAsync(new(notification.Key, notification), cancellationToken);
    }
    public ValueTask<WidgetNotificationReceipt> DismissAsync(string key, CancellationToken cancellationToken = default)
    {
        if (!WidgetNotificationContract.ValidKey(key)) throw new ArgumentException("Notification key is invalid.", nameof(key));
        return SendAsync(new(key), cancellationToken);
    }
    /// <summary>Replace content only while its notification still exists; never resurrect an expired banner.</summary>
    public ValueTask<WidgetNotificationReceipt> UpdateAsync(WidgetNotification notification, CancellationToken cancellationToken = default)
    {
        if (!WidgetNotificationContract.IsValid(notification)) throw new ArgumentException("Notification is invalid.", nameof(notification));
        return SendAsync(new(notification.Key, notification, true), cancellationToken);
    }
    private async ValueTask<WidgetNotificationReceipt> SendAsync(WidgetNotificationCommand command, CancellationToken token)
    {
        var result = await client.InvokeAsync(WidgetNotificationCapabilities.Publish, command, token).ConfigureAwait(false);
        return result ?? throw new WidgetCapabilityException("malformed_response", "Notification response is invalid.");
    }
}
