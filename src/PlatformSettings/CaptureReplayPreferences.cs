using WidgetRail.WidgetProtocol;

namespace WidgetRail.PlatformSettings;

/// <summary>Host recording intent and the explicitly approved application, independent of widget residency.</summary>
public sealed record CaptureReplayPreferences
{
    public long Revision { get; init; }
    // This initializer runs only when the host preferences/settings member is
    // absent. A persisted explicit Recording value replaces it during loading;
    // the public legacy CaptureReplaySettings constructor remains unchanged.
    public CaptureReplaySettings Settings { get; init; } = new()
    {
        Recording = new CaptureReplaySettings().Recording with { Selection = new CaptureRecordingSelection() },
    };
    public string? OwnerPackageId { get; init; }
    public string? OwnerPublisherId { get; init; }
    public string? OwnerInstanceId { get; init; }
    public string? ApprovedApplication { get; init; }
    public string? ApplicationName { get; init; }

    public bool IsWellFormed() => Revision is >= 0 and <= CaptureConfirmationSettings.MaximumResetVersion &&
        Settings?.IsWellFormed() == true &&
        (OwnerPackageId is null && OwnerPublisherId is null && OwnerInstanceId is null && !Settings.Enabled ||
            Text(OwnerPackageId, 240) && Text(OwnerPublisherId, 240) && Text(OwnerInstanceId, 240)) &&
        (ApprovedApplication is null && ApplicationName is null || ApprovedApplication is { Length: 64 } &&
            ApprovedApplication.All(char.IsAsciiHexDigit) && Text(ApplicationName, 120));
    private static bool Text(string? value, int maximum) => value is { Length: > 0 } && value.Length <= maximum && !value.Any(char.IsControl);
}
