namespace WidgetRail.WidgetSdk;

public sealed record WidgetDisplayControlMode(string Id, int Width, int Height, uint RefreshNumerator, uint RefreshDenominator)
{
    public double RefreshRate => RefreshDenominator == 0 ? 0 : (double)RefreshNumerator / RefreshDenominator;
}
public sealed record WidgetDisplayControlMonitor(string Id, string Name, IReadOnlyList<WidgetDisplayControlMode> Modes,
    string? CurrentModeId, bool HdrSupported, bool HdrEnabled, bool HdrActive, string? UnavailableReason, bool ModesTruncated = false);
public sealed record WidgetDisplayControlsState(IReadOnlyList<WidgetDisplayControlMonitor> Displays);
public sealed record WidgetDisplayControlRequest(string DisplayId, string? ModeId = null, bool? HdrEnabled = null);

public static class WidgetDisplayControlsCapabilities
{
    public static WidgetCapabilityOperation<WidgetCapabilityQuery, WidgetDisplayControlsState> Get { get; } =
        new("system.displays.read.v1", "display-controls.get");
    public static WidgetCapabilityOperation<WidgetDisplayControlRequest, WidgetDisplayProfilesState> Apply { get; } =
        new("system.displays.control.v1", "display-controls.apply");
}
