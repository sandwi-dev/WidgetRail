using System.Text.Json.Serialization;

namespace WidgetRail.WidgetSdk;

public sealed record WidgetPowerAvailability(
    [property: JsonRequired] bool CanShutDown,
    [property: JsonRequired] bool CanRestart,
    [property: JsonRequired] bool CanSleep);

public static class WidgetPowerCapabilities
{
    public static WidgetCapabilityOperation<WidgetCapabilityQuery, WidgetPowerAvailability> Get { get; } =
        new("system.power.read.v1", "power.get");
    public static WidgetCapabilityOperation<WidgetCapabilityQuery, WidgetCapabilityAcknowledgement> ShutDown { get; } =
        new("system.power.control.v1", "power.shut-down");
    public static WidgetCapabilityOperation<WidgetCapabilityQuery, WidgetCapabilityAcknowledgement> Restart { get; } =
        new("system.power.control.v1", "power.restart");
    public static WidgetCapabilityOperation<WidgetCapabilityQuery, WidgetCapabilityAcknowledgement> Sleep { get; } =
        new("system.power.control.v1", "power.sleep");
}

/// <summary>PC power operations. Confirm shutdown and restart with the user before invoking.</summary>
public sealed class WidgetPowerService
{
    private readonly IWidgetCapabilityClient _client;
    internal WidgetPowerService(IWidgetCapabilityClient client) => _client = client;
    public async ValueTask<WidgetPowerAvailability> GetAvailabilityAsync(CancellationToken cancellationToken = default) =>
        await _client.InvokeAsync(WidgetPowerCapabilities.Get, new(), cancellationToken).ConfigureAwait(false)
        ?? throw new WidgetCapabilityException("malformed_response", "Power information is unavailable.");
    public ValueTask ShutDownAsync(CancellationToken cancellationToken = default) =>
        SendAsync(WidgetPowerCapabilities.ShutDown, cancellationToken);
    public ValueTask RestartAsync(CancellationToken cancellationToken = default) =>
        SendAsync(WidgetPowerCapabilities.Restart, cancellationToken);
    public ValueTask SleepAsync(CancellationToken cancellationToken = default) =>
        SendAsync(WidgetPowerCapabilities.Sleep, cancellationToken);
    private async ValueTask SendAsync(WidgetCapabilityOperation<WidgetCapabilityQuery, WidgetCapabilityAcknowledgement> operation,
        CancellationToken token)
    {
        var result = await _client.InvokeAsync(operation, new(), token).ConfigureAwait(false);
        if (result is null || !result.Acknowledged)
            throw new WidgetCapabilityException("malformed_response", "The power request was not acknowledged.");
    }
}
