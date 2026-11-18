using System.Globalization;
using WidgetRail.PlatformBroker;
using WidgetRail.WidgetBridge;
using WidgetRail.WidgetPresentationSession;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

internal static class MediaPreviewPipeScenarios
{
    private const string IdVariable = "WRAIL_TEST_PREVIEW_CAPTURE_ID", RevisionVariable = "WRAIL_TEST_PREVIEW_CAPTURE_REVISION";
    internal static Task SavedCaptureThroughPipes() => SavedCaptureThroughPipes("video/mp4");
    internal static Task SavedPngThroughPipes() => SavedCaptureThroughPipes("image/png");
    private static async Task SavedCaptureThroughPipes(string contentType)
    {
        var root = Path.Combine(Path.GetTempPath(), "widgetrail-preview-pipe-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var previousId = Environment.GetEnvironmentVariable(IdVariable); var previousRevision = Environment.GetEnvironmentVariable(RevisionVariable);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(25));
        try
        {
            var video = contentType == "video/mp4";
            var bytes = video ? new byte[] { 1, 2, 3, 4 } : [137,80,78,71,13,10,26,10,0,0,0,0];
            var path = Path.Combine(root, video ? "input.mp4" : "input.png"); await File.WriteAllBytesAsync(path, bytes);
            using var library = new CaptureLibraryStore(Path.Combine(root, "index"), Path.Combine(root, "owned"));
            var item = await library.SaveAsync(new(new(Guid.NewGuid().ToString("N"), contentType, 64, 64, bytes.Length, video ? 5 : 0, long.MaxValue), path), default);
            Environment.SetEnvironmentVariable(IdVariable, item.Id);
            Environment.SetEnvironmentVariable(RevisionVariable, item.Revision.ToString(CultureInfo.InvariantCulture));
            var identity = new BrokerWidgetIdentity("example.preview", "example.publisher", "media-preview.instance");
            var widget = new ConfiguredWidget { Id = "media-preview", PackageId = identity.PackageId, PublisherId = identity.PublisherId,
                InstanceId = identity.InstanceId, Name = "Preview", WorkerExecutable = Environment.ProcessPath!,
                WorkerFingerprint = new string('a', 64), CatalogFingerprint = new string('b', 64),
                DeclaredCapabilities = [PlatformCapabilities.CaptureLibraryReadV1], ExecutionTrust = WidgetExecutionTrust.FullTrustCurrentUser };
            var consent = new ConsentStore(Path.Combine(root, "consent"));
            await consent.SetDecisionAsync(identity, PlatformCapabilities.CaptureLibraryReadV1, ConsentDecision.Grant);
            var simulated = new SimulatedPlatformBrokerBackend();
            await using var backend = new CompositePlatformBrokerBackend(simulated, simulated, captureLibrary: library);
            var pipe = "preview-pipe-" + Guid.NewGuid().ToString("N");
            await using var server = new WidgetBridgeServer(pipe, new([widget]), consentStore: consent, platformBackend: backend);
            var serving = server.RunAsync(TimeSpan.FromSeconds(3), deadline.Token);
            await using var session = await WidgetPresentationSession.ConnectAsync(pipe, cancellationToken: deadline.Token);
            _ = await session.ListWidgetsAsync(deadline.Token);
            var frame = await session.EstablishPresentationAsync(session.GetTarget(widget.Id), WidgetLifecycleState.Interactive, deadline.Token);
            var descriptor = frame.Snapshot.Root.Children.Single().IndexedCollection!;
            await using var lease = await session.AcquireIndexedRangeAsync(frame.Authority, "items", descriptor, 0, 1, cancellationToken: deadline.Token);
            var owned = Guid.NewGuid().ToString("N");
            var media = await session.ResolveIndexedMediaPlayerSourceAsync(lease, "clip", "preview.player", deadline.Token, owned);
            var original = new Uri(media.Uri).LocalPath;
            if (media.PlaybackLeaseId != owned || media.Media.Source.SavedCapture?.Id != item.Id || media.IsImage == video ||
                !original.StartsWith(Path.Combine(root, "owned"), StringComparison.OrdinalIgnoreCase)) throw new Exception("Preview media lost original-file/lease authority through the pipes.");
            await consent.SetDecisionAsync(identity, PlatformCapabilities.CaptureLibraryReadV1, ConsentDecision.Deny);
            try { await session.ResolveIndexedMediaPlayerSourceAsync(lease, "clip", "preview.player", deadline.Token, owned); throw new Exception("Revoked library read permission renewed playback."); }
            catch (WidgetPresentationSessionException) { }
            using (File.Open(original, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) { }
            await consent.SetDecisionAsync(identity, PlatformCapabilities.CaptureLibraryReadV1, ConsentDecision.Grant);
            owned = Guid.NewGuid().ToString("N");
            _ = await session.ResolveIndexedMediaPlayerSourceAsync(lease, "clip", "preview.player", deadline.Token, owned);
            await lease.DisposeAsync();
            await session.ReleaseMediaPlayerAsync(owned, deadline.Token);
            using (File.Open(original, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) { }
            try { await session.ResolveIndexedMediaPlayerSourceAsync(lease, "clip", "preview.player", deadline.Token, Guid.NewGuid().ToString("N")); throw new Exception("Retired preview lease reopened media."); }
            catch (WidgetPresentationSessionException) { }
            await session.DisposeAsync(); await serving.WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally
        {
            Environment.SetEnvironmentVariable(IdVariable, previousId); Environment.SetEnvironmentVariable(RevisionVariable, previousRevision);
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }
    internal sealed class Probe : Widget
    {
        private readonly WidgetIndexedCollection<string, int> items;
        private readonly MediaPlayerSource source = MediaPlayerSource.FromSavedCapture(Environment.GetEnvironmentVariable(IdVariable)!,
            long.Parse(Environment.GetEnvironmentVariable(RevisionVariable)!, CultureInfo.InvariantCulture));
        internal Probe() => items = CreateIndexedCollection<string, int>("media-preview-source", "fixed", 1, new()
        {
            ItemKey = _ => new("clip"), ReadRange = (_, _, _, _) => ValueTask.FromResult<IReadOnlyList<int>>([0]),
            RenderItem = (_, _, _) => UI.ActionSurface("open", "clip", "Clip", ActionSurfaceOrientation.Vertical, UI.Text("Clip", "caption"))
                .PresentOnFocus(UI.MediaPlayer(source, "preview.player", options: new(AutoPlay: true, Muted: true))),
            OnAction = (_, _, _, _) => ValueTask.CompletedTask
        });
        public override WidgetView Render() => new(UI.FocusPresentationSurface(UI.CollectionList("items", items, 64, "Clips"), UI.Text("", "empty"), "previews") with
        { Preview = new() { EnterButton = ControllerButton.RightStick, AllowMedia = true } });
    }
}
