using System.Text.Json;

namespace WidgetRail.PlatformBroker;

public sealed record PowerAvailability(bool CanShutDown, bool CanRestart, bool CanSleep);
public enum PowerCommand { ShutDown, Restart, Sleep }

public interface IPowerPlatformBrokerBackend
{
    Task<PowerAvailability> GetPowerAvailabilityAsync(CancellationToken cancellationToken) =>
        Task.FromResult(new PowerAvailability(false, false, false));
    Task ExecutePowerAsync(PowerCommand command, CancellationToken cancellationToken) =>
        Task.FromException(new BrokerException("power_unavailable", "PC power controls are unavailable."));
}

internal sealed class PowerCapabilityDomain(IPlatformBrokerBackend backend)
{
    internal async Task<JsonElement> ExecuteAsync(string operation, JsonElement payload, CancellationToken token)
    {
        BrokerCapabilityDomains.DemandEmptyPayload(payload);
        if (operation == PlatformCapabilities.PowerGet)
            return BrokerJson.ToElement(await backend.GetPowerAvailabilityAsync(token).ConfigureAwait(false));
        var command = operation switch
        {
            PlatformCapabilities.PowerShutDown => PowerCommand.ShutDown,
            PlatformCapabilities.PowerRestart => PowerCommand.Restart,
            PlatformCapabilities.PowerSleep => PowerCommand.Sleep,
            _ => throw new BrokerException("unsupported_operation", "Power operation is unsupported."),
        };
        await backend.ExecutePowerAsync(command, token).ConfigureAwait(false);
        return BrokerCapabilityDomains.Acknowledged();
    }
}
