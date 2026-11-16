using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

internal static class ExtendedSavedMediaTests
{
    internal static Task Contract()
    {
        var ordinary = new CaptureAttachment(new string('a', 32), "video/mp4", 640, 360, 1024, 5, long.MaxValue)
            { Origin = CaptureAttachmentOrigin.SavedLibrary };
        foreach (var capture in new[]
        {
            ordinary with { DurationSeconds = 6 },
            ordinary with { Width = 1920, Height = 1080 },
            ordinary with { ByteLength = CaptureLimits.MaximumContextBytes + 1 },
            ordinary with { DurationSeconds = 7200, ByteLength = 134L * 1024 * 1024 },
        })
        {
            Check(capture.IsWellFormed(), "Supported imported video rejected");
            foreach (var element in new WidgetElement[] { UI.MediaPlayer(MediaPlayerSource.FromCapture(capture), "media"), UI.CapturedMedia(capture, "legacy") })
            {
                var snapshot = new WidgetView(element).CreateSnapshot("extended.saved-media", 1);
                Check(snapshot.ProtocolVersion == ProtocolConstants.ExtendedSavedMediaVersion, "Extended media did not negotiate its semantic version");
                Check(!ViewSnapshotValidator.Validate(snapshot).Any(), "Valid extended snapshot rejected");
                Check(ViewSnapshotValidator.Validate(snapshot with { ProtocolVersion = 113 }).Any(error => error.Code == "feature_requires_version"),
                    "Old hosts were allowed a payload outside their validation envelope");
                var roundtrip = SnapshotJson.Deserialize(SnapshotJson.Serialize(snapshot));
                Check(MediaPlayerDefinition.FromNode(roundtrip.Root)!.Source.Attachment == capture, "Extended capture lost facts on the wire");
            }
        }
        Check(new WidgetView(UI.MediaPlayer(MediaPlayerSource.FromCapture(ordinary), "media")).CreateSnapshot("old.saved", 1).ProtocolVersion < 114,
            "Compatible existing media unnecessarily requires the new protocol");
        var configured = ordinary with { Origin = CaptureAttachmentOrigin.WindowCapture, Width = 1920, Height = 1080, DurationSeconds = 120,
            Properties = new(1920, 1080, 30, 3600, 0, CaptureEncoderMode.Software) };
        Check(new WidgetView(UI.MediaPlayer(MediaPlayerSource.FromCapture(configured), "media")).CreateSnapshot("configured", 1).ProtocolVersion == ProtocolConstants.ConfigurableCaptureVersion,
            "Existing native capture metadata changed its minimum version");
        Check(!(configured with { DurationSeconds = 7200 }).IsWellFormed(), "Native capture duration admission was widened");
        Check(!(ordinary with { DurationSeconds = CaptureLibraryLimits.MaximumVideoSeconds + 1 }).IsWellFormed(), "Library duration bound missing");
        Check(!(ordinary with { ByteLength = CaptureLibraryLimits.MaximumVideoBytes + 1 }).IsWellFormed(), "Library byte bound missing");
        return Task.CompletedTask;
    }
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
}
