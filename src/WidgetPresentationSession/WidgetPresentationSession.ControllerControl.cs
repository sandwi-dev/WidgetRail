using System.Text.Json;
using WidgetRail.PlatformDiagnostics;
using WidgetRail.PlatformSettings;
using WidgetRail.WidgetBridge;

namespace WidgetRail.WidgetPresentationSession;

public sealed partial class WidgetPresentationSession
{
    private readonly SemaphoreSlim _controllerControlExchange = new(1, 1);
    private static readonly IReadOnlySet<string> ControllerPreferenceProperties = new HashSet<string>(StringComparer.Ordinal)
        { "exclusiveControl", "revision", "openShortcut", "holdDpadToScroll", "f1ShortcutEnabled", "overlayShortcut" };

    /// <summary>
    /// Reports the native controller adapter's actual status and reads the next
    /// persisted preference. The host must apply that preference before reporting
    /// again. Cancellation never replays a report already admitted to the bridge.
    /// </summary>
    public async Task<ControllerSettings> ExchangeControllerControlAsync(
        ControllerControlStatus status, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(status);
        if (!Enum.IsDefined(status.State)) throw new ArgumentOutOfRangeException(nameof(status));
        if (!_options.ExclusiveControllerControl)
            throw new InvalidOperationException("Controller control was not advertised by this host.");
        await _controllerControlExchange.WaitAsync(cancellationToken).ConfigureAwait(false);
        // Keep the lane until the actual response arrives, even if this caller
        // stops waiting. The broker attributes each report to the preceding reply.
        var exchange = ExchangeControllerControlCoreAsync(status);
        _ = exchange.ContinueWith(completed =>
        {
            _ = completed.Exception;
            _controllerControlExchange.Release();
        }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        return await exchange.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task<ControllerSettings> ExchangeControllerControlCoreAsync(ControllerControlStatus status)
    {
        var response = await RequestAsync(BridgeMessageTypes.ControllerControl, status,
            BridgeMessageTypes.ControllerControl, CancellationToken.None).ConfigureAwait(false);
        RequireAllowedProperties(response.Payload, ControllerPreferenceProperties,
            "exclusiveControl", "revision", "openShortcut", "holdDpadToScroll");
        // Deserialization defaults must not silently turn malformed data into an
        // instruction to disable control or change the opening shortcut.
        // Older bridges omit F1; its model default is false. Newer replies must
        // carry a real Boolean, just like the other controller preferences.
        var hasF1 = response.Payload.TryGetProperty("f1ShortcutEnabled", out var f1);
        var hasKeyboard = response.Payload.TryGetProperty("overlayShortcut", out var keyboard);
        if (hasKeyboard && keyboard.ValueKind != JsonValueKind.Null)
        {
            RequireObjectProperties(keyboard, "enabled", "key", "modifiers");
            if (keyboard.GetProperty("enabled").ValueKind is not (JsonValueKind.True or JsonValueKind.False) ||
                keyboard.GetProperty("key").ValueKind != JsonValueKind.Number ||
                keyboard.GetProperty("modifiers").ValueKind != JsonValueKind.String ||
                !Enum.TryParse<OverlayShortcutModifiers>(keyboard.GetProperty("modifiers").GetString(), true, out var modifiers) ||
                ((int)modifiers & ~15) != 0 ||
                !keyboard.GetProperty("key").TryGetInt32(out var key) || !OverlayShortcutSettings.IsPrimaryKey(key))
                throw new BridgeProtocolException("WidgetBridge returned an invalid keyboard shortcut.");
        }
        if (response.Payload.EnumerateObject().Count() != 4 + (hasF1 ? 1 : 0) + (hasKeyboard ? 1 : 0) ||
            hasF1 && f1.ValueKind is not (JsonValueKind.True or JsonValueKind.False) ||
            response.Payload.GetProperty("exclusiveControl").ValueKind is not (JsonValueKind.True or JsonValueKind.False) ||
            response.Payload.GetProperty("holdDpadToScroll").ValueKind is not (JsonValueKind.True or JsonValueKind.False) ||
            response.Payload.GetProperty("revision").ValueKind != JsonValueKind.Number ||
            !response.Payload.GetProperty("revision").TryGetInt64(out var revision) || revision < 0 ||
            response.Payload.GetProperty("openShortcut").ValueKind != JsonValueKind.String ||
            response.Payload.GetProperty("openShortcut").GetString() is not ("guide" or "viewMenu"))
            throw new BridgeProtocolException("WidgetBridge returned invalid controller preferences.");
        var result = BridgeJson.FromElement<ControllerSettings>(response.Payload);
        if (!result.EffectiveOverlayShortcut.IsWellFormed())
            throw new BridgeProtocolException("WidgetBridge returned an invalid keyboard shortcut.");
        return result;
    }
}
