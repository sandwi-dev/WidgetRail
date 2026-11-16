using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

internal static class CaptureSaveCompatibilityTests
{
    internal static async Task Contract()
    {
        var native = new CaptureAttachment(new string('a', 32), "video/mp4", 640, 360, 1024, 6, long.MaxValue)
            { Properties = new(640, 360, 30, 180, 0, CaptureEncoderMode.Software) };
        var saved = new CaptureLibraryItem(new string('b', 32), native with { Origin = CaptureAttachmentOrigin.SavedLibrary, Properties = null }, 1);
        var legacyCalls = 0;
        WidgetTestHostServicesBuilder Legacy(WidgetTestHostServicesBuilder builder) => builder.WithHandler(WidgetCaptureLibraryCapabilities.Save,
            (_, _) => { legacyCalls++; return ValueTask.FromResult(saved); });
        var modern = Legacy(new WidgetTestHostServicesBuilder().WithResponse(WidgetCaptureLibraryCapabilities.SaveCurrent, saved)).Build();
        Check((await modern.CaptureLibrary.SaveAsync(native)).Media.Properties is null && legacyCalls == 0, "Modern save lost decoded metadata or used legacy projection");
        foreach (var code in new[] { "unsupported_capability", "unsupported_operation" })
        {
            var old = Legacy(new WidgetTestHostServicesBuilder().WithHandler(WidgetCaptureLibraryCapabilities.SaveCurrent,
                (_, _) => ValueTask.FromException<CaptureLibraryItem>(new WidgetCapabilityException(code, "Old host")))).Build();
            var before = legacyCalls; await old.CaptureLibrary.SaveAsync(native);
            Check(legacyCalls == before + 1, "Unsupported operation did not fall back exactly once");
        }
        foreach (var code in new[] { "permission_denied", "capability_not_declared", "operation_failed", "malformed_response", "test_handler_missing" })
        {
            var denied = Legacy(new WidgetTestHostServicesBuilder().WithHandler(WidgetCaptureLibraryCapabilities.SaveCurrent,
                (_, _) => ValueTask.FromException<CaptureLibraryItem>(new WidgetCapabilityException(code, "Do not retry")))).Build();
            var before = legacyCalls;
            try { await denied.CaptureLibrary.SaveAsync(native); throw new Exception("Save failure was hidden"); }
            catch (WidgetCapabilityException error) when (error.ErrorCode == code) { }
            Check(legacyCalls == before, "A failed/possibly committed save was retried");
        }
        var missing = Legacy(new WidgetTestHostServicesBuilder()).Build(); var missingBefore = legacyCalls;
        try { await missing.CaptureLibrary.SaveAsync(native); throw new Exception("Missing modern test handler was hidden"); }
        catch (WidgetCapabilityException error) when (error.ErrorCode == "test_handler_missing") { }
        Check(legacyCalls == missingBefore, "Fake-client handler omission silently used legacy API");
        var unavailable = Legacy(new WidgetTestHostServicesBuilder().WithHandler(WidgetCaptureLibraryCapabilities.SaveCurrent,
            (_, _) => ValueTask.FromException<CaptureLibraryItem>(new WidgetCapabilityUnavailableException("No authenticated channel")))).Build();
        try { await unavailable.CaptureLibrary.SaveAsync(native); throw new Exception("Missing channel was hidden"); }
        catch (WidgetCapabilityUnavailableException) { }
        Check(legacyCalls == missingBefore, "Missing authenticated channel triggered fallback");
        using var cancel = new CancellationTokenSource();
        var cancelled = Legacy(new WidgetTestHostServicesBuilder().WithHandler(WidgetCaptureLibraryCapabilities.SaveCurrent,
            (_, _) => { cancel.Cancel(); return ValueTask.FromException<CaptureLibraryItem>(new WidgetCapabilityException("unsupported_operation", "Old host")); })).Build();
        try { await cancelled.CaptureLibrary.SaveAsync(native, cancel.Token); throw new Exception("Cancellation ignored"); }
        catch (OperationCanceledException) when (cancel.IsCancellationRequested) { }
        Check(legacyCalls == missingBefore, "Cancellation between version probes retried the mutation");
    }
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
}
