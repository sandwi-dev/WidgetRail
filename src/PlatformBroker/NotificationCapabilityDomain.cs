using System.Text.Json;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.PlatformBroker;

internal sealed class NotificationCapabilityDomain(Func<WidgetNotificationCommand, CancellationToken, ValueTask<WidgetNotificationReceipt>>? publish)
{
    private long nextAllowed;
    private readonly SemaphoreSlim gate = new(1, 1);
    internal async Task<JsonElement> ExecuteAsync(JsonElement payload, CancellationToken token)
    {
        var command = BrokerJson.ParsePayload<WidgetNotificationCommand>(payload);
        if (!WidgetNotificationContract.IsValid(command)) throw new BrokerException("invalid_payload", "Notification is invalid.");
        // Bound each authenticated worker, including replacement updates. No unbounded queued background publications.
        if (!await gate.WaitAsync(0, token).ConfigureAwait(false)) return BrokerJson.ToElement(new WidgetNotificationReceipt(false));
        try
        {
            if (publish is null || command.Notification is not null && Environment.TickCount64 < nextAllowed)
                return BrokerJson.ToElement(new WidgetNotificationReceipt(false));
            if (command.Notification is not null) nextAllowed = Environment.TickCount64 + 200;
            return BrokerJson.ToElement(await publish(command, token).ConfigureAwait(false));
        }
        finally { gate.Release(); }
    }
}
