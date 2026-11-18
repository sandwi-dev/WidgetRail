using System.Text.Json;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

internal static class LibraryRecordingServiceTests
{
    internal static async Task Run()
    {
        var options = new RecordingOptions(3840, 2160, 30, 21_000_000, 60,
            CaptureEncoderMode.Hardware, CaptureAudioMode.Application);
        var ordinary = new CaptureSessionRequest { Recording = options };
        var library = ordinary with { OutputPurpose = CaptureOutputPurpose.LibraryRecording,
            MaximumBytes = CaptureLimits.MaximumLibraryRecordingBytes };
        var capabilities = new CaptureCapabilities(true, [CaptureImageFormat.Png],
            [new("fixture", 3840, 2160, 30, 100_000, 100_000_000, CaptureEncoderMode.Hardware)],
            [CaptureAudioMode.None, CaptureAudioMode.Application], true, 120);
        Check(ordinary.IsWellFormed() && capabilities.Validate(ordinary).ErrorCode == "capture_output_budget_too_small",
            "Temporary attachment budget widened.");
        Check(capabilities.Validate(library).Supported, "The reported 60s/21Mbps library configuration is still rejected.");
        Check(!JsonSerializer.SerializeToElement(ordinary).TryGetProperty("OutputPurpose", out _),
            "Default requests gained an unknown wire member for old hosts.");
        Check(!(ordinary with { MaximumBytes = CaptureLimits.MaximumAttachmentBytes + 1L }).IsWellFormed(), "Temporary cap widened.");
        Check(!(library with { MaximumBytes = CaptureLimits.MaximumLibraryRecordingBytes + 1 }).IsWellFormed(), "Library cap missing.");
        Check(!(library with { Recording = null, Screenshot = new() }).IsWellFormed(), "Screenshot accepted a video-only budget.");
        Check(!(library with { OutputPurpose = (CaptureOutputPurpose)99 }).IsWellFormed(), "Unknown purpose accepted.");
        var gameHelp = ordinary with { Recording = new(), MaximumBytes = CaptureLimits.MaximumContextBytes };
        Check(gameHelp.IsWellFormed() && gameHelp.MaximumBytes == 8 * 1024 * 1024, "Game Help request semantics changed.");

        CaptureSessionRequest? received = null;
        var ticket = new WindowCaptureTicket(new string('a', 32));
        var draft = new CaptureAttachment(new string('b', 32), "video/mp4", 3840, 2160,
            160_000_000, 60, long.MaxValue) { Origin = CaptureAttachmentOrigin.LibraryRecordingDraft,
                Properties = new(3840, 2160, 30, 1800, Encoder: CaptureEncoderMode.Hardware, Audio: CaptureAudioMode.Application) };
        Check(draft.IsWellFormed() && !(draft with { Origin = CaptureAttachmentOrigin.WindowCapture }).IsWellFormed(),
            "Large draft and temporary attachment were conflated.");
        Check(!(draft with { Properties = null }).IsWellFormed(), "Draft accepted absent recording metadata.");
        Check(new CaptureLibraryEditRequest(Attachment: draft).IsWellFormed(), "Draft cannot use shared editor.");
        var discarded = false;
        var host = new WidgetTestHostServicesBuilder()
            .WithHandler(WidgetCaptureCapabilities.Start, (request, _) => { received = request; return ValueTask.FromResult(ticket); })
            .WithResponse(WidgetCaptureCapabilities.Status, new WindowCaptureStatus(ticket.RequestId, WindowCapturePhase.Ready, draft))
            .WithHandler(WidgetCaptureCapabilities.Discard, (request, _) =>
            { discarded = request.AttachmentId == draft.Id; return ValueTask.FromResult(new WidgetCapabilityAcknowledgement(true)); })
            .Build();
        Check(await host.Capture.RequestLibraryRecordingAsync(options) == ticket && received == library,
            "SDK did not request the generic library purpose.");
        Check((await host.Capture.GetStatusAsync(ticket)).Attachment == draft, "SDK rejected ready draft.");
        try { await host.Capture.ReadContentAsync(draft); throw new Exception("Large draft allocated an entire in-memory video."); }
        catch (ArgumentException) { }
        await host.Capture.DiscardAsync(draft);
        Check(discarded, "Draft used saved-library release rather than owner capture discard.");
        await host.Capture.RequestRecordingAsync(options);
        Check(received == ordinary, "Existing recording helper changed its default purpose.");

        var calls = 0;
        var unavailable = new WidgetTestHostServicesBuilder().WithHandler(WidgetCaptureCapabilities.Start, (_, _) =>
        { calls++; return ValueTask.FromException<WindowCaptureTicket>(new WidgetCapabilityException("unsupported_operation", "fixture")); }).Build();
        try { await unavailable.Capture.RequestLibraryRecordingAsync(options); throw new Exception("Unsupported host hidden."); }
        catch (WidgetCapabilityException error) when (error.ErrorCode == "unsupported_operation") { }
        Check(calls == 1, "SDK retried a potentially started recording or downgraded its output purpose.");
        var legacySaves = 0;
        var oldSaveHost = new WidgetTestHostServicesBuilder()
            .WithHandler(WidgetCaptureLibraryCapabilities.SaveCurrent, (_, _) => ValueTask.FromException<CaptureLibraryItem>(
                new WidgetCapabilityException("unsupported_operation", "fixture")))
            .WithHandler(WidgetCaptureLibraryCapabilities.Save, (_, _) =>
            { legacySaves++; throw new Exception("Draft sent to an incompatible legacy save operation."); }).Build();
        try { await oldSaveHost.CaptureLibrary.SaveAsync(draft); throw new Exception("Unsupported draft save hidden."); }
        catch (WidgetCapabilityException error) when (error.ErrorCode == "unsupported_operation") { }
        Check(legacySaves == 0, "Draft save retried against old wire semantics.");
    }
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
}
