using WidgetRail.WidgetBridge;
using WidgetRail.WidgetCatalog;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

internal static class ArtworkFileAdmissionTests
{
    internal static Task Run()
    {
        var sandbox = new ConfiguredWidget
        {
            Id = "file-test", PackageId = "dev.files", PublisherId = "dev", Name = "Files",
            InstanceId = "files.instance", WorkerExecutable = @"C:\worker.exe",
            PackageRoot = @"C:\packages\files",
            VerifiedPackageFiles = new Dictionary<string, VerifiedPackageFile>
            {
                ["assets/cover.png"] = new("assets/cover.png", 32, new string('a', 64)),
            },
        };
        var local = new WidgetFileArtwork(@"C:\images\cover.png", "one");
        Denied(() => BridgeClientRegistry.AdmitArtworkSource(sandbox, local));
        if (!ReferenceEquals(local, BridgeClientRegistry.AdmitArtworkSource(
                sandbox with { ExecutionTrust = WidgetExecutionTrust.FullTrustCurrentUser }, local)))
            throw new Exception("Full-access file changed unexpectedly.");
        var admitted = BridgeClientRegistry.AdmitArtworkSource(sandbox,
            new WidgetFileArtwork("assets/cover.png", "one", packageRelative: true)) as WidgetFileArtwork;
        if (admitted?.File.AllowedRoot != sandbox.PackageRoot || admitted.PackageRelative)
            throw new Exception("Package file was not constrained to its admitted root.");
        Denied(() => BridgeClientRegistry.AdmitArtworkSource(sandbox,
            new WidgetFileArtwork("assets/unlisted.png", "one", packageRelative: true)));
        Denied(() => BridgeClientRegistry.AdmitArtworkSource(sandbox,
            new WidgetFileArtwork(new ArtworkFileReference(@"C:\images\cover.png", "one") { AllowedRoot = @"C:\" })));
        return Task.CompletedTask;
    }

    private static void Denied(Action action)
    {
        try { action(); }
        catch (BridgeProtocolException) { return; }
        throw new Exception("Unadmitted artwork file was accepted.");
    }
}
