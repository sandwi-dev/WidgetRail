using WidgetRail.WidgetBridge;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

internal static partial class BridgeIndexedLeaseRegistryScenarios
{
    internal static async Task PreviewMediaUsesExactLease()
    {
        await using var fixture = await Fixture.Start();
        var media = new MediaPlayerDefinition(MediaPlayerSource.FromSavedCapture(new string('a', 32), 123), new(AutoPlay: true, Muted: true));
        fixture.Client.PreviewMedia = media;
        fixture.Client.PreviewPosterArtwork = "library.art.0123456789abcdef0123456789abcdef";
        using var frame = await fixture.Registry.GetSnapshotAsync(fixture.Configured.Id, default, default);
        using var publication = await fixture.Registry.AcquireIndexedRangeAsync(fixture.Request("media"), default);
        var lease = publication.Value;
        var request = new BridgeMediaPlayerRequest(fixture.Configured.Id, frame.Value.WorkerRun!, frame.Value.Snapshot.Sequence, "preview.player", media)
        { IndexedOrigin = new(lease.InstanceId, lease.RuntimeGeneration, lease.PresentationGeneration, new(lease.Lease.LeaseId, "item.0")) };
        using (var admission = fixture.Registry.AdmitMediaPlayer(request))
            Check(admission.IsCurrent && ReferenceEquals(admission.Widget, fixture.Configured), "Media preview did not retain exact indexed owner.");
        RejectMedia(request with { Media = media with { Source = MediaPlayerSource.FromSavedCapture(new string('b', 32), 123) } });
        RejectMedia(request with { IndexedOrigin = request.IndexedOrigin with { Item = new(lease.Lease.LeaseId, "foreign") } });
        RejectMedia(request with { IndexedOrigin = request.IndexedOrigin with { RuntimeGeneration = "foreign" } });
        RejectMedia(request with { ElementId = "undeclared.player" });
        var poster = new BridgeIndexedArtworkRequest(lease.WidgetId, lease.InstanceId, lease.RuntimeGeneration,
            lease.PresentationGeneration, new(lease.Lease.LeaseId, "item.0"), fixture.Client.PreviewPosterArtwork, "poster-demand");
        var pixels = new WidgetEncodedArtwork(WidgetArtworkContentType.Png,
            Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+jq1sAAAAASUVORK5CYII="));
        Check(await fixture.Registry.ResolveIndexedArtworkAsync(poster, default, (_, handle, _) =>
            Task.FromResult<WidgetEncodedArtwork?>(handle == fixture.Client.PreviewPosterArtwork ? pixels : null)) == pixels,
            "Scoped poster did not use the existing exact-row artwork resolver.");
        Check(await fixture.Registry.ResolveIndexedArtworkAsync(poster with { Item = new(lease.Lease.LeaseId, "foreign") }, default,
            (_, _, _) => throw new Exception("Foreign poster reached resolver.")) is null, "Foreign row borrowed a poster.");
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var latePoster = fixture.Registry.ResolveIndexedArtworkAsync(poster, default, async (_, _, _) =>
        { entered.TrySetResult(); await finish.Task; return pixels; });
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
        var retirement = fixture.Registry.ReleaseIndexedLeaseAsync(fixture.Release(lease));
        finish.TrySetResult();
        await retirement;
        await Reject(() => latePoster);
        RejectMedia(request);
        void RejectMedia(BridgeMediaPlayerRequest invalid)
        {
            try { using var unexpected = fixture.Registry.AdmitMediaPlayer(invalid); }
            catch (Exception error) when (error is BridgeProtocolException or ArgumentException) { return; }
            throw new Exception("Stale/foreign/undeclared media preview accepted.");
        }
    }
}
