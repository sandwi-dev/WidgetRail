namespace WidgetRail.PlatformBroker;

/// <summary>Host-enumerated modes. Refresh rates remain rational rather than rounded to integer Hz.</summary>
public sealed record DisplayControlMode(string Id, int Width, int Height, uint RefreshNumerator, uint RefreshDenominator);
public sealed record DisplayControlMonitor(string Id, string Name, IReadOnlyList<DisplayControlMode> Modes,
    string? CurrentModeId, bool HdrSupported, bool HdrEnabled, bool HdrActive, string? UnavailableReason, bool ModesTruncated = false);
public sealed record DisplayControlsState(IReadOnlyList<DisplayControlMonitor> Displays);
public sealed record DisplayControlRequest(string DisplayId, string? ModeId = null, bool? HdrEnabled = null);

internal static class DisplayControlValidation
{
    internal static void Validate(DisplayControlRequest request)
    {
        if (request.DisplayId is not { Length: 24 } || request.DisplayId.Any(c => !char.IsAsciiHexDigit(c)) ||
            (request.ModeId is null) == (request.HdrEnabled is null) ||
            request.ModeId is { } mode && (mode.Length is < 1 or > 96 || mode.Any(c => !(char.IsAsciiLetterOrDigit(c) || c is '-' or '.'))))
            throw new BrokerException("invalid_payload", "Choose a supported display control.");
    }
}
