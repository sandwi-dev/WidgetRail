using WidgetRail.WidgetBridge;

namespace WidgetRail.WidgetPresentationSession;

public sealed partial class WidgetPresentationSession
{
    /// <summary>
    /// Reports the host's current display through the existing appearance request.
    /// The bridge resolves connected physical identity used by saved sizing and
    /// Settings. A connection token is never treated as a saved display key.
    /// </summary>
    public async Task<string> ResolveDisplayAsync(string connectionId, string name, IReadOnlyList<string> devicePaths,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(devicePaths);
        if (connectionId is null || string.IsNullOrWhiteSpace(name) || connectionId.Length > 256 || name.Length > 128 ||
            connectionId.Any(char.IsControl) || name.Any(char.IsControl) || devicePaths.Count > 16 ||
            devicePaths.Count == 0 && connectionId.Length != 0 ||
            devicePaths.Any(path => string.IsNullOrWhiteSpace(path) || path.Length > 256 || path.Any(char.IsControl)) ||
            devicePaths.Distinct(StringComparer.OrdinalIgnoreCase).Count() != devicePaths.Count)
            throw new ArgumentException("Invalid monitor connection context.");
        var response = await RequestAsync(BridgeMessageTypes.GetPlatformAppearance,
            new { display = new { id = connectionId, name, devicePaths } }, BridgeMessageTypes.PlatformAppearance, cancellationToken).ConfigureAwait(false);
        var active = ReadString(response.Payload, "activeDisplayId", allowEmpty: true);
        if (active.Length > 256 || active.Any(char.IsControl)) throw new BridgeProtocolException("Invalid active display identity.");
        return active;
    }
}
