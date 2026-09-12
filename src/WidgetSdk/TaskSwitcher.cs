using System.Text.Json.Serialization;

namespace WidgetRail.WidgetSdk;

public sealed record WidgetTaskWindow(
    [property: JsonRequired] string WindowId,
    [property: JsonRequired] string ApplicationName,
    [property: JsonRequired] string Title,
    [property: JsonRequired] bool IsMinimized);

public sealed record WidgetTaskWindowRequest([property: JsonRequired] string WindowId);

public static class WidgetTaskSwitcherCapabilities
{
    /// <summary>Manifest consent for host-only live rendering; grants no pixel-reading operation.</summary>
    public const string PreviewPermission = "system.apps.windows.preview.v1";
    public static WidgetCapabilityOperation<WidgetCapabilityQuery, IReadOnlyList<WidgetTaskWindow>> List { get; } =
        new("system.apps.windows.read.v1", "apps.windows.list");
    public static WidgetCapabilityOperation<WidgetTaskWindowRequest, WidgetCapabilityAcknowledgement> Switch { get; } =
        new("system.apps.windows.switch.v1", "apps.windows.switch");
    public static WidgetCapabilityOperation<WidgetTaskWindowRequest, WidgetCapabilityAcknowledgement> Close { get; } =
        new("system.apps.windows.close.v1", "apps.windows.close");
}

/// <summary>Current open windows. IDs are temporary and scoped to this widget's broker session.</summary>
public sealed class WidgetTaskSwitcherService
{
    private readonly IWidgetCapabilityClient _client;
    internal WidgetTaskSwitcherService(IWidgetCapabilityClient client) => _client = client;

    public async ValueTask<IReadOnlyList<WidgetTaskWindow>> GetWindowsAsync(CancellationToken cancellationToken = default)
    {
        var windows = await _client.InvokeAsync(WidgetTaskSwitcherCapabilities.List, new(), cancellationToken).ConfigureAwait(false);
        if (windows is null || windows.Count > 64 ||
            windows.Any(window => window is null || !ValidId(window.WindowId) ||
                !ValidText(window.ApplicationName, 120) || !ValidText(window.Title, 240)) ||
            windows.Select(window => window.WindowId).Distinct(StringComparer.Ordinal).Count() != windows.Count)
            throw new WidgetCapabilityException("malformed_response", "Window information is unavailable.");
        return windows.ToArray();
    }

    /// <summary>Activates an existing window. The host hides the overlay when foreground changes.</summary>
    public ValueTask SwitchAsync(string windowId, CancellationToken cancellationToken = default) =>
        SendAsync(WidgetTaskSwitcherCapabilities.Switch, windowId, cancellationToken);

    /// <summary>Requests a normal window close. The app may prompt to save or refuse to close.</summary>
    public ValueTask CloseAsync(string windowId, CancellationToken cancellationToken = default) =>
        SendAsync(WidgetTaskSwitcherCapabilities.Close, windowId, cancellationToken);

    private async ValueTask SendAsync(
        WidgetCapabilityOperation<WidgetTaskWindowRequest, WidgetCapabilityAcknowledgement> operation,
        string windowId, CancellationToken cancellationToken)
    {
        if (!ValidId(windowId)) throw new ArgumentException("Window ID is invalid.", nameof(windowId));
        var response = await _client.InvokeAsync(operation, new(windowId), cancellationToken).ConfigureAwait(false);
        if (response is null || !response.Acknowledged)
            throw new WidgetCapabilityException("malformed_response", "The window request was not acknowledged.");
    }

    private static bool ValidId(string? value) => ValidText(value, 128) &&
        value!.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_' or '.');
    private static bool ValidText(string? value, int limit) =>
        !string.IsNullOrWhiteSpace(value) && value.Length <= limit && !value.Any(char.IsControl);
}
