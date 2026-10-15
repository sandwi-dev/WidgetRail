using System.Text.Json;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.PlatformBroker;

internal sealed class NotificationCapabilityDomain(Func<WidgetNotificationCommand, CancellationToken, ValueTask<WidgetNotificationReceipt>>? publish)
{
    private long nextAllowed, nextConfigurationAllowed;
    private readonly SemaphoreSlim gate = new(1, 1);
    internal async Task<JsonElement> ExecuteAsync(JsonElement payload, CancellationToken token)
    {
        var command = BrokerJson.ParsePayload<WidgetNotificationCommand>(payload);
        if (!WidgetNotificationContract.IsValid(command)) throw new BrokerException("invalid_payload", "Notification is invalid.");
        // Bound each authenticated worker, including replacement updates. No unbounded queued background publications.
        if (!await gate.WaitAsync(0, token).ConfigureAwait(false)) return BrokerJson.ToElement(new WidgetNotificationReceipt(false));
        try
        {
            var configuration = command.ConfigureCategoryId is not null;
            var announcement = command.Notification is not null;
            var now = Environment.TickCount64;
            if (publish is null || configuration && now < nextConfigurationAllowed || announcement && now < nextAllowed)
                return BrokerJson.ToElement(new WidgetNotificationReceipt(false));
            // A playback tick must not consume the user's preferences gesture.
            if (configuration) nextConfigurationAllowed = now + 200;
            if (announcement) nextAllowed = now + 200;
            return BrokerJson.ToElement(await publish(command, token).ConfigureAwait(false));
        }
        finally { gate.Release(); }
    }
}
