using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

internal static class CaptureEditingServiceTests
{
    internal static async Task Run()
    {
        var media = new CaptureAttachment(new string('a', 32), "image/png", 100, 100, 1024, 0, long.MaxValue);
        var saved = new CaptureLibraryMutationReference(new string('b', 32), 2, 3);
        var result = new CaptureLibraryItem(new string('c', 32), media with { Origin = CaptureAttachmentOrigin.SavedLibrary }, 1000);
        var calls = 0;
        CaptureLibraryEditRequest? received = null;
        var services = new WidgetTestHostServicesBuilder().WithHandler(WidgetCaptureLibraryCapabilities.EditCopy,
            (request, _) => { calls++; received = request; return ValueTask.FromResult(new CaptureLibraryEditResult(CaptureLibraryEditOutcome.Saved, result)); }).Build();
        Check((await services.CaptureLibrary.EditCopyAsync(saved)).Item == result && received == new CaptureLibraryEditRequest(Saved: saved), "Saved revision changed in transit.");
        Check((await services.CaptureLibrary.EditCopyAsync(media)).Item == result && received == new CaptureLibraryEditRequest(Attachment: media), "Owned capture changed in transit.");
        Check(!new CaptureLibraryEditRequest(saved, media).IsWellFormed() && !new CaptureLibraryEditRequest().IsWellFormed(), "Source union accepts ambiguity.");
        try { await services.CaptureLibrary.EditCopyAsync(media with { Origin = CaptureAttachmentOrigin.SavedLibrary }); throw new Exception("Opened saved lease accepted as temporary source."); }
        catch (ArgumentException) { }
        Check(calls == 2, "Invalid source reached broker.");
        var reusedTemporary = new WidgetTestHostServicesBuilder().WithResponse(WidgetCaptureLibraryCapabilities.EditCopy,
            new CaptureLibraryEditResult(CaptureLibraryEditOutcome.Saved, result with { Id = media.Id })).Build();
        try { await reusedTemporary.CaptureLibrary.EditCopyAsync(media); throw new Exception("Edited result reused temporary source identity."); }
        catch (WidgetCapabilityException error) when (error.ErrorCode == "malformed_response") { }
        foreach (var bad in new[] { new CaptureLibraryEditResult(CaptureLibraryEditOutcome.Saved),
            new CaptureLibraryEditResult(CaptureLibraryEditOutcome.Saved, result with { Id = saved.Id }),
            new CaptureLibraryEditResult(CaptureLibraryEditOutcome.Cancelled, result),
            new CaptureLibraryEditResult((CaptureLibraryEditOutcome)99) })
        {
            var invalid = new WidgetTestHostServicesBuilder().WithResponse(WidgetCaptureLibraryCapabilities.EditCopy, bad).Build();
            try { await invalid.CaptureLibrary.EditCopyAsync(saved); throw new Exception("Malformed edit result accepted."); }
            catch (WidgetCapabilityException error) when (error.ErrorCode == "malformed_response") { }
        }
        var cancelled = new WidgetTestHostServicesBuilder().WithResponse(WidgetCaptureLibraryCapabilities.EditCopy,
            new CaptureLibraryEditResult(CaptureLibraryEditOutcome.Cancelled)).Build();
        Check((await cancelled.CaptureLibrary.EditCopyAsync(saved)).Item is null, "Cancel invented an item.");
        foreach (var code in new[] { "unsupported_operation", "permission_denied", "capture_library_metadata_conflict", "request_timeout" })
        {
            var attempts = 0;
            var failing = new WidgetTestHostServicesBuilder().WithHandler(WidgetCaptureLibraryCapabilities.EditCopy,
                (_, _) => { attempts++; return ValueTask.FromException<CaptureLibraryEditResult>(new WidgetCapabilityException(code, "fixture")); }).Build();
            try { await failing.CaptureLibrary.EditCopyAsync(saved); throw new Exception("Edit failure hidden."); }
            catch (WidgetCapabilityException error) when (error.ErrorCode == code) { }
            Check(attempts == 1, "Possibly committed edit was retried.");
        }
    }
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
}
