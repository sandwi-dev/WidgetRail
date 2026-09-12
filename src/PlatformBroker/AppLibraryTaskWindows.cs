using System.Text.Json;

namespace WidgetRail.PlatformBroker;

internal sealed partial class AppLibraryCapabilityDomain
{
    private Dictionary<string, string> _windowTargets = new(StringComparer.Ordinal);

    private async Task<JsonElement> TaskWindowsAsync(
        string operation, JsonElement payload, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (operation == PlatformCapabilities.TaskWindowsList)
            {
                BrokerCapabilityDomains.DemandEmptyPayload(payload);
                var windows = await _backend.GetTaskWindowsAsync(cancellationToken).ConfigureAwait(false);
                if (windows is null || windows.Count > 64 ||
                    windows.Any(window => window is null || !ValidWindowText(window.WindowId, 128) ||
                        !ValidWindowText(window.ApplicationName, 120) || !ValidWindowText(window.Title, 240)) ||
                    windows.Select(window => window.WindowId).Distinct(StringComparer.Ordinal).Count() != windows.Count)
                    throw new BrokerException("invalid_backend_data", "Window information is unavailable.");
                var oldIds = _windowTargets.ToDictionary(pair => pair.Value, pair => pair.Key, StringComparer.Ordinal);
                var next = new Dictionary<string, string>(StringComparer.Ordinal);
                var result = windows.Select(window =>
                {
                    var id = oldIds.GetValueOrDefault(window.WindowId) ?? "window-" + Guid.NewGuid().ToString("N");
                    next.Add(id, window.WindowId);
                    return window with { WindowId = id };
                }).ToArray();
                _windowTargets = next;
                return BrokerJson.ToElement(result);
            }
            var request = BrokerJson.ParsePayload<TaskWindowRequest>(payload);
            ContractValidation.OpaqueId(request.WindowId);
            if (!_windowTargets.TryGetValue(request.WindowId, out var target))
                throw new BrokerException("window_unavailable", "This window is no longer available.");
            if (operation == PlatformCapabilities.TaskWindowsSwitch)
                await _backend.SwitchTaskWindowAsync(target, cancellationToken).ConfigureAwait(false);
            else
                await _backend.CloseTaskWindowAsync(target, cancellationToken).ConfigureAwait(false);
            return BrokerCapabilityDomains.Acknowledged();
        }
        finally { _gate.Release(); }
    }

    private static bool ValidWindowText(string? value, int limit) =>
        !string.IsNullOrWhiteSpace(value) && value.Length <= limit && !value.Any(char.IsControl);
}
