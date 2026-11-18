using WidgetRail.WidgetProtocol;

namespace WidgetRail.PlatformBroker;

/// <summary>Older strict readers can observe/stop an active session without interpreting or overwriting new preferences.</summary>
internal static class CaptureReplayProjection
{
    internal static bool RequiresEncodingDetails(CaptureReplaySettings settings) =>
        settings.Recording.Selection is not null || settings.Recording.Encoding is not null ||
        settings.Recording.MaximumWidth > 3840 || settings.Recording.MaximumHeight > 2160;

    internal static CaptureReplayStatus ForReader(CaptureReplayStatus status, bool details)
    {
        if (details) return status;
        var recording = status.Settings.Recording;
        // The old schema cannot describe ultrawide bounds or Auto. Keep its safe
        // legacy envelope and explicitly flag that these settings need a newer UI.
        var extended = RequiresEncodingDetails(status.Settings);
        recording = recording with { Selection = null, Encoding = null };
        if (recording.MaximumWidth > 3840 || recording.MaximumHeight > 2160)
        {
            var scale = Math.Min(3840d / recording.MaximumWidth, 2160d / recording.MaximumHeight);
            recording = recording with { MaximumWidth = Math.Max(2, (int)(recording.MaximumWidth * scale) / 2 * 2),
                MaximumHeight = Math.Max(2, (int)(recording.MaximumHeight * scale) / 2 * 2) };
        }
        return status with { Resolved = null, Settings = status.Settings with { Recording = recording },
            Issue = extended && status.Issue == CaptureReplayIssue.None || status.Issue == CaptureReplayIssue.ConfigurationUnavailable ? CaptureReplayIssue.SettingsChanged : status.Issue };
    }

    internal static CaptureReplayControlResult ForReader(CaptureReplayControlResult result, bool details) =>
        result with { Status = ForReader(result.Status, details), Issue = !details && result.Issue == CaptureReplayIssue.ConfigurationUnavailable
            ? CaptureReplayIssue.SettingsChanged : result.Issue };
}
