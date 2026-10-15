using System.Text.Json;
using WidgetRail.WidgetProtocol;
namespace WidgetRail.PlatformBroker;

internal sealed class ActivityCapabilityDomain(Func<WidgetActivity, CancellationToken, ValueTask<WidgetActivityReceipt>>? report)
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private WidgetActivity? last;
    private long nextAllowed;
    internal async Task<JsonElement> ExecuteAsync(JsonElement payload, CancellationToken token)
    {
        if (payload.ValueKind != JsonValueKind.Object || payload.EnumerateObject().Any(property =>
            property.Name is not ("localAudio" or "remotePlayback" or "recording") || property.Value.ValueKind is not (JsonValueKind.True or JsonValueKind.False)))
            throw new BrokerException("invalid_payload", "Activity supports only localAudio, remotePlayback and recording booleans.");
        var activity = BrokerJson.ParsePayload<WidgetActivity>(payload);
        if (activity is null) throw new BrokerException("invalid_payload", "Activity is invalid.");
        // No unbounded publication queue. Identical reports do not cross the bridge.
        if (!await gate.WaitAsync(0, token).ConfigureAwait(false)) return BrokerJson.ToElement(new WidgetActivityReceipt(false));
        try
        {
            if (report is null) return BrokerJson.ToElement(new WidgetActivityReceipt(false));
            if (activity == last) return BrokerJson.ToElement(new WidgetActivityReceipt(true));
            if (activity != new WidgetActivity() && Environment.TickCount64 < nextAllowed) return BrokerJson.ToElement(new WidgetActivityReceipt(false));
            nextAllowed = Environment.TickCount64 + 50;
            var result = await report(activity, token).ConfigureAwait(false);
            if (result.Accepted) last = activity;
            return BrokerJson.ToElement(result);
        }
        finally { gate.Release(); }
    }
}
