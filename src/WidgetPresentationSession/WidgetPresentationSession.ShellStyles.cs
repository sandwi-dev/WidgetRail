using System.Text.Json;
using WidgetRail.WidgetBridge;

namespace WidgetRail.WidgetPresentationSession;

public sealed partial class WidgetPresentationSession
{
    /// <summary>Read the bridge-resolved host shell palette without changing display or settings.</summary>
    public async Task<IReadOnlyDictionary<string, BridgeNodeRenderStyles>> ReadShellStylesAsync(CancellationToken cancellationToken = default)
    {
        var response = await RequestAsync(BridgeMessageTypes.GetPlatformAppearance, new { },
            BridgeMessageTypes.PlatformAppearance, cancellationToken).ConfigureAwait(false);
        if (response.Payload.ValueKind != JsonValueKind.Object || !response.Payload.TryGetProperty("shellStyles", out var raw) ||
            raw.ValueKind != JsonValueKind.Object || raw.EnumerateObject().Count() > 16)
            throw new BridgeProtocolException("Shell style inventory exceeds its bound.");
        var styles = new Dictionary<string, BridgeNodeRenderStyles>(StringComparer.Ordinal);
        var allowed = new HashSet<string>(["canvas", "backdrop", "panel", "tray", "tray-item", "tray-item:selected",
            "tray-item:focused", "tray-item:selected:focused", "title", "body", "hint", "status"], StringComparer.Ordinal);
        foreach (var property in raw.EnumerateObject())
        {
            if (!allowed.Contains(property.Name) || styles.ContainsKey(property.Name) || property.Value.ValueKind != JsonValueKind.Object ||
                property.Value.EnumerateObject().Count() > BridgeRenderStyleLimits.MaximumPropertiesPerState)
                throw new BridgeProtocolException("Invalid shell style role.");
            var state = property.Value.Deserialize<Dictionary<string, BridgeComputedStyleValue>>(BridgeJson.Options)
                ?? throw new BridgeProtocolException("Missing shell style state.");
            styles.Add(property.Name, new() { Base = state, Focused = state, Pressed = state });
        }
        return BridgeRenderStyleContract.ValidateAndFreeze(styles, allowed, requireComplete: false);
    }
}
