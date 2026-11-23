using WidgetRail.WidgetBridge;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

internal static partial class BridgeIndexedLeaseRegistryScenarios
{
    internal static async Task InlineVideoUsesExactLease()
    {
        await using var fixture=await Fixture.Start();
        var media=new MediaPlayerDefinition(MediaPlayerSource.PublicFile("https://example.com/clip.mp4"),new(Muted:true))
            {Presentation=MediaPlayerPresentation.Inline};
        fixture.Client.BodyAnimation=media;fixture.Client.BodyReadingGroup=true;
        using var frame=await fixture.Registry.GetSnapshotAsync(fixture.Configured.Id,default,default);
        using var publication=await fixture.Registry.AcquireIndexedRangeAsync(fixture.Request("inline"),default);
        var lease=publication.Value;
        var request=new BridgeMediaPlayerRequest(fixture.Configured.Id,frame.Value.WorkerRun!,frame.Value.Snapshot.Sequence,"body.animation",media)
            {IndexedOrigin=new(lease.InstanceId,lease.RuntimeGeneration,lease.PresentationGeneration,new(lease.Lease.LeaseId,"item.0"))};
        using var admission=fixture.Registry.AdmitMediaPlayer(request);
        Check(admission.IsCurrent,"An explicit inline video is admitted by its reading-row lease without preview permissions.");
        RejectInline(request with{ElementId="foreign"});
        RejectInline(request with{Media=media with{Options=new(AutoPlay:true)}});
        RejectInline(request with{Media=media with{Source=MediaPlayerSource.PublicFile("https://example.com/foreign.mp4")}});
        fixture.Client.SelectionActive=true;_ = await fixture.Snapshot();
        Check(!admission.IsCurrent,"Selection mode retires interactive inline-media admission.");
        RejectInline(request);
        fixture.Client.SelectionActive=false;fixture.Client.ActiveOtherScope=true;_ = await fixture.Snapshot();
        RejectInline(request);
        fixture.Client.ActiveOtherScope=false;_ = await fixture.Snapshot();
        admission.Dispose();
        await fixture.Registry.ReleaseIndexedLeaseAsync(fixture.Release(lease));
        RejectInline(request);
        void RejectInline(BridgeMediaPlayerRequest invalid)
        {
            try { using var unexpected=fixture.Registry.AdmitMediaPlayer(invalid); }
            catch(Exception error)when(error is BridgeProtocolException or BridgeStaleIndexedInputAuthorityException or ArgumentException){return;}
            throw new Exception("Invalid inline media admission succeeded.");
        }
    }
    internal static async Task AnimationUsesExactLease()
    {
        await using var fixture = await Fixture.Start();
        var media = new MediaPlayerDefinition(MediaPlayerSource.WebUrl("https://example.com/loop.mp4"),
            new(AutoPlay: true, Muted: true, Loop: true) { IsAnimation = true }) { Presentation = MediaPlayerPresentation.Animation };
        fixture.Client.BodyAnimation = media;
        using var frame = await fixture.Registry.GetSnapshotAsync(fixture.Configured.Id, default, default);
        using var publication = await fixture.Registry.AcquireIndexedRangeAsync(fixture.Request("animation"), default);
        var lease = publication.Value;
        var request = new BridgeMediaPlayerRequest(fixture.Configured.Id, frame.Value.WorkerRun!, frame.Value.Snapshot.Sequence, "body.animation", media)
        { IndexedOrigin = new(lease.InstanceId, lease.RuntimeGeneration, lease.PresentationGeneration, new(lease.Lease.LeaseId, "item.0")) };
        using (var admission = fixture.Registry.AdmitMediaPlayer(request))
            Check(admission.IsCurrent, "A passive animation is admitted without interactive preview permission.");
        RejectAnimation(request with { Media = media with { Presentation = MediaPlayerPresentation.Interactive } });
        RejectAnimation(request with { Media = media with { Source = MediaPlayerSource.WebUrl("https://example.com/foreign.mp4") } });
        RejectAnimation(request with { Media = media with { Options = media.Options with { Muted = false } } });
        RejectAnimation(request with { Media = media with { Source = MediaPlayerSource.LocalFile(@"C:\private.mp4") } });
        RejectAnimation(request with { GalleryItemId = "foreign" });
        RejectAnimation(request with { ElementId = "foreign" });
        RejectAnimation(request with { IndexedOrigin = request.IndexedOrigin with { Item = new(lease.Lease.LeaseId, "foreign") } });
        fixture.Client.ActiveOtherScope = true;
        _ = await fixture.Snapshot();
        using (var admission = fixture.Registry.AdmitMediaPlayer(request))
            Check(admission.IsCurrent, "An unchanged passive row retains data authority beneath a modal.");
        RejectAnimation(request with { Media = media with { Presentation = MediaPlayerPresentation.Interactive } });
        await fixture.Registry.ReleaseIndexedLeaseAsync(fixture.Release(lease));
        RejectAnimation(request);
        void RejectAnimation(BridgeMediaPlayerRequest invalid)
        {
            try { using var unexpected = fixture.Registry.AdmitMediaPlayer(invalid); }
            catch (Exception error) when (error is BridgeProtocolException or BridgeStaleIndexedInputAuthorityException or ArgumentException) { return; }
            throw new Exception("Foreign, stale or interactive row media was accepted.");
        }
    }
    internal static async Task PreviewMediaUsesExactLease()
    {
        await using var fixture = await Fixture.Start();
        var saved = MediaPlayerSource.FromSavedCapture(new string('a', 32), 123);
        var media = new MediaPlayerDefinition(saved, new(AutoPlay: true, Muted: true))
        {
            Gallery = [new("saved", "Saved capture", saved),
                new("remote", "Remote image", MediaPlayerSource.WebImage("https://example.com/image.png"))],
        };
        fixture.Client.PreviewMedia = media;
        fixture.Client.PreviewPosterArtwork = "library.art.0123456789abcdef0123456789abcdef";
        using var frame = await fixture.Registry.GetSnapshotAsync(fixture.Configured.Id, default, default);
        using var publication = await fixture.Registry.AcquireIndexedRangeAsync(fixture.Request("media"), default);
        var lease = publication.Value;
        var request = new BridgeMediaPlayerRequest(fixture.Configured.Id, frame.Value.WorkerRun!, frame.Value.Snapshot.Sequence, "preview.player", media)
        { IndexedOrigin = new(lease.InstanceId, lease.RuntimeGeneration, lease.PresentationGeneration, new(lease.Lease.LeaseId, "item.0")) };
        using (var admission = fixture.Registry.AdmitMediaPlayer(request))
            Check(admission.IsCurrent && ReferenceEquals(admission.Widget, fixture.Configured), "Media preview did not retain exact indexed owner.");
        using (var admission = fixture.Registry.AdmitMediaPlayer(request with { GalleryItemId = "remote" }))
            Check(admission.IsCurrent, "Gallery members retain the same exact indexed preview authority.");
        RejectMedia(request with { GalleryItemId = "foreign" });
        RejectMedia(request with { Media = media with { Gallery = [media.Gallery[0], media.Gallery[1] with { Source = MediaPlayerSource.WebImage("https://example.com/forged") }] } });
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
