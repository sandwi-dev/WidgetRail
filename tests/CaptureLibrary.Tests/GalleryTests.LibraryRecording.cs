using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

internal static partial class GalleryTests
{
    internal static async Task LibraryRecordingReviewAndSave()
    {
        await ReviewAndSave(false);
        await ReviewAndSave(true);
    }
    private static async Task ReviewAndSave(bool failSavedPreview)
    {
        var draft = new CaptureAttachment(new string('f', 32), "video/mp4", 3840, 2160, 160_000_000, 60, long.MaxValue)
        { Origin = CaptureAttachmentOrigin.LibraryRecordingDraft, Properties = new(3840, 2160, 30, 1800, Encoder: CaptureEncoderMode.Hardware) };
        CaptureSessionRequest? requested = null;
        var saveCalls = 0; var discards = 0; var discardAfterReplacement = false;
        await using var fixture = new GalleryFixture((builder, owner) => builder
            .WithResponse(WidgetCaptureCapabilities.Validate, new CaptureConfigurationValidation(true))
            .WithHandler(WidgetCaptureCapabilities.Start, (value, _) =>
            { requested = value; return ValueTask.FromResult(new WindowCaptureTicket(new string('e', 32))); })
            .WithResponse(WidgetCaptureCapabilities.Status, new WindowCaptureStatus(new string('e', 32), WindowCapturePhase.Ready, draft))
            .WithHandler(WidgetCaptureLibraryCapabilities.SaveCurrent, (value, _) =>
            {
                Check(value.Attachment == draft, "Save lost exact draft authority."); saveCalls++;
                var saved = new CaptureLibraryItem(draft.Id, draft with { Origin = CaptureAttachmentOrigin.SavedLibrary }, 1000);
                owner.Items.Add(saved); return ValueTask.FromResult(saved);
            })
            .WithHandler(WidgetCaptureCapabilities.Discard, (value, _) =>
            { discards++; discardAfterReplacement = Preview(owner)?.Origin == CaptureAttachmentOrigin.SavedLibrary; return ValueTask.FromResult(new WidgetCapabilityAcknowledgement(true)); }));
        if (failSavedPreview) fixture.OpenOverride = (_, _) => ValueTask.FromException<CaptureAttachment>(
            new WidgetCapabilityException("capture_unavailable", "fixture"));
        await fixture.Start(); await fixture.Action("capture-library.video");
        await fixture.Until(() => Preview(fixture)?.Id == draft.Id && !fixture.Busy);
        Check(requested is { OutputPurpose: CaptureOutputPurpose.LibraryRecording, MaximumBytes: CaptureLimits.MaximumLibraryRecordingBytes },
            "Record clip retained attachment budget.");
        Check(saveCalls == 0 && fixture.Node("capture-library.save") is not null, "Recording skipped review or saved automatically.");
        Check(ViewSnapshotValidator.Validate(fixture.View()).Count == 0 && fixture.View().ProtocolVersion >= ProtocolConstants.LibraryRecordingDraftVersion,
            "Large draft presentation is invalid or negotiated to an old validator.");
        await fixture.Action("capture-library.save");
        if (failSavedPreview)
        {
            await fixture.Until(() => Preview(fixture) is null && !fixture.Busy);
            Check(saveCalls == 1 && fixture.Node("capture-library.save") is null &&
                fixture.Node("capture-library.heading") is not null,
                "Committed recording became an unsaved draft again after preview failure.");
            return;
        }
        await fixture.Until(() => Preview(fixture)?.Origin == CaptureAttachmentOrigin.SavedLibrary && !fixture.Busy);
        Check(saveCalls == 1 && discards == 1 && discardAfterReplacement && fixture.Node("capture-library.save") is null,
            "Saving retained an invalid draft, duplicated publication, or discarded before replacement.");
    }
}
