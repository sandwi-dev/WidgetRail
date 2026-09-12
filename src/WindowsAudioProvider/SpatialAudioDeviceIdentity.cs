namespace WidgetRail.WindowsAudioProvider;

internal sealed record SpatialAudioDeviceInterface(string InterfaceId, string? InstanceId, bool IsEnabled);

internal static class SpatialAudioDeviceIdentity
{
    internal const string InstanceIdProperty = "System.Devices.DeviceInstanceId";

    internal static string? Resolve(string endpointId, IEnumerable<SpatialAudioDeviceInterface> interfaces)
    {
        if (string.IsNullOrWhiteSpace(endpointId)) return null;
        // Correlate the OS-reported devnode identity, never the friendly name,
        // another default-device role, or a guessed device-interface path.
        var expectedInstance = "SWD\\MMDEVAPI\\" + endpointId;
        string? result = null;
        foreach (var device in interfaces)
        {
            if (!device.IsEnabled || string.IsNullOrWhiteSpace(device.InterfaceId) ||
                !string.Equals(device.InstanceId, expectedInstance, StringComparison.OrdinalIgnoreCase)) continue;
            if (result is not null && !string.Equals(result, device.InterfaceId, StringComparison.OrdinalIgnoreCase))
                return null;
            result = device.InterfaceId;
        }
        return result;
    }
}
