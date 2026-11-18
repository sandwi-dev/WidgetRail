using WidgetRail.PlatformBroker;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

internal static class KnownCaptureFolderTests
{
    private static readonly BrokerWidgetIdentity Widget = new("example.folders", "example.publisher", "folders.instance");
    private static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    internal static async Task SdkAndKnownFolderHint()
    {
        CaptureLibraryChooseSourceRequest? chosen = null;
        var services = new WidgetTestHostServicesBuilder()
            .WithHandler(WidgetCaptureLibrarySourceCapabilities.Choose, (request, _) =>
            { chosen = request; return ValueTask.FromResult(new CaptureLibrarySourceChoice(null)); }).Build();
        Check(await services.CaptureLibrary.ChooseKnownSourceAsync(CaptureLibraryKnownLocation.WindowsGameCaptures, "Windows captures") is null,
            "Cancelled linking fabricated a folder grant");
        Check(chosen?.KnownLocation == CaptureLibraryKnownLocation.WindowsGameCaptures, "Known folder hint lost");
        await services.CaptureLibrary.ChooseSourceAsync("Manual folder");
        Check(chosen?.KnownLocation is null, "Ordinary picker acquired a Windows-specific hint");
        Check(!BrokerJson.ToElement(chosen!).TryGetProperty("knownLocation", out _), "Legacy picker payload gained an extra field");
        try { await services.CaptureLibrary.ChooseKnownSourceAsync((CaptureLibraryKnownLocation)99, "Invalid"); throw new InvalidOperationException("Unknown folder accepted"); }
        catch (ArgumentException) { }
        var choosing = CaptureLibrarySourceRequests.ChooseAsync(Widget, "Windows captures", default, CaptureLibraryKnownLocation.WindowsGameCaptures);
        var host = CaptureLibrarySourceRequests.Pending!;
        Check(host.KnownLocation == CaptureLibraryKnownLocation.WindowsGameCaptures, "Broker dropped closed hint");
        CaptureLibrarySourceRequests.Complete(host.Id, null);
        Check(await choosing is null, "Cancelled host folder confirmation created source");
    }
}
