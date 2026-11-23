using System.Security.Cryptography;
using WidgetRail.WidgetBridge;
using WidgetRail.WidgetCatalog;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

internal static class MediaPlayerScenarios
{
    internal static async Task ThroughPipes()
    {
        var pipe="media-source-"+Guid.NewGuid().ToString("N");
        using var deadline=new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var widget=new ConfiguredWidget { Id="media-player", PackageId="example.media", PublisherId="example", Name="Media", InstanceId="media-player.instance", WorkerExecutable=Environment.ProcessPath!,
            WorkerFingerprint=new string('a',64), CatalogFingerprint=new string('b',64), PinningSupported=true, FullWidgetPinningSupported=true };
        await using var server=new WidgetBridgeServer(pipe,new([widget]));
        var serving=server.RunAsync(TimeSpan.FromSeconds(3),deadline.Token);
        await using var session=await WidgetRail.WidgetPresentationSession.WidgetPresentationSession.ConnectAsync(pipe,cancellationToken:deadline.Token);
        _=await session.ListWidgetsAsync(deadline.Token);
        var frame=await session.EstablishPresentationAsync(session.GetTarget("media-player"),WidgetLifecycleState.Interactive,deadline.Token);
        var source=await session.ResolveMediaPlayerSourceAsync(frame,"player",token:deadline.Token);
        Check(source.Uri=="https://example.com/video.mp4" && !source.IsImage);
        var image = await session.ResolveMediaPlayerSourceAsync(frame, "image", token: deadline.Token);
        Check(image.IsImage && image.Uri == "https://example.com/image.png");
        var publicMedia = await session.ResolveMediaPlayerSourceAsync(frame, "public", token: deadline.Token);
        Check(publicMedia.Media.Source.IsPublicMedia && publicMedia.Uri == "https://example.com/public.mp4");
        var galleryImage = await session.ResolveMediaPlayerSourceAsync(frame, "gallery", null, null, deadline.Token, null, "picture");
        var galleryClip = await session.ResolveMediaPlayerSourceAsync(frame, "gallery", null, null, deadline.Token, null, "clip");
        Check(galleryImage.IsImage && !galleryClip.IsImage && galleryImage.Media.Gallery is null && galleryClip.Uri == "https://example.com/second.mp4");
        try { _ = await session.ResolveMediaPlayerSourceAsync(frame, "gallery", null, null, deadline.Token, null, "not-declared"); throw new Exception("Unknown gallery member admitted."); }
        catch (WidgetRail.WidgetPresentationSession.WidgetPresentationSessionException) { }
        var projection=session.ResolvePinnedProjection(frame,"host.full-widget");
        var selection=await session.SelectPinnedLayoutAsync(projection,cancellationToken:deadline.Token);
        var pinned=await session.ResolveMediaPlayerSourceAsync(frame,"player",selection,projection,deadline.Token);
        Check(pinned==source);
        Check(await session.ResolveMediaPlayerSourceAsync(frame, "image", selection, projection, deadline.Token) == image);
        Check(await session.ResolveMediaPlayerSourceAsync(frame, "gallery", selection, projection, deadline.Token, null, "clip") == galleryClip);
        var descriptor = frame.Snapshot.Root.Children.Single(node => node.Id == "animations").IndexedCollection!;
        await using var rows = await session.AcquireIndexedRangeAsync(frame.Authority, "animations", descriptor, 0, 1, cancellationToken: deadline.Token);
        var animation = await session.ResolveIndexedMediaPlayerSourceAsync(rows, "gif", "row.animation", deadline.Token);
        Check(animation.Media.Presentation == MediaPlayerPresentation.Animation && animation.Uri == "https://example.com/loop.mp4" && !animation.IsImage);
        try { _ = await session.ResolveIndexedMediaPlayerSourceAsync(rows, "gif", "row.interactive", deadline.Token); throw new Exception("Interactive row media bypassed preview authority."); }
        catch (WidgetRail.WidgetPresentationSession.WidgetPresentationSessionException) { }
        await rows.DisposeAsync();
        try { _ = await session.ResolveIndexedMediaPlayerSourceAsync(rows, "gif", "row.animation", deadline.Token); throw new Exception("Retired row animation was resolved."); }
        catch (WidgetRail.WidgetPresentationSession.WidgetPresentationSessionException) { }
        await session.ClearPinnedSelectionAsync(selection,frame,cancellationToken:deadline.Token);
        await session.DisposeAsync(); await serving.WaitAsync(TimeSpan.FromSeconds(5));
    }
    internal sealed class Probe : Widget
    {
        private readonly WidgetIndexedCollection<string, int> animations;
        internal Probe() => animations = CreateIndexedCollection<string, int>("animations.source", "fixed", 1, new()
        {
            ItemKey = _ => new("gif"), ReadRange = (_, _, _, _) => ValueTask.FromResult<IReadOnlyList<int>>([0]),
            RenderItem = (_, _, _) => UI.ActionSurface("open", "row", "GIF", ActionSurfaceOrientation.Vertical,
                UI.Animation("https://example.com/loop.mp4", "row.animation"),
                UI.MediaPlayer(MediaPlayerSource.WebUrl("https://example.com/ordinary.mp4"), "row.interactive")),
            OnAction = (_, _, _, _) => ValueTask.CompletedTask
        });
        public override WidgetView Render()=>new(UI.Stack("media.page",
            UI.MediaPlayer(MediaPlayerSource.WebUrl("https://example.com/video.mp4"),"player"),
            UI.MediaPlayer(MediaPlayerSource.WebImage("https://example.com/image.png"),"image"),
            UI.MediaPlayer(MediaPlayerSource.PublicFile("https://example.com/public.mp4"),"public"),
            UI.MediaGallery([new("picture", "Picture", MediaPlayerSource.WebImage("https://example.com/image.png")),
                new("clip", "Clip", MediaPlayerSource.WebUrl("https://example.com/second.mp4"))], "gallery"),
            UI.CollectionList("animations", animations, 180, "Animations")));
    }
    internal static async Task Sources()
    {
        var root = Path.Combine(Path.GetTempPath(),"wrail-media-source-"+Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        var path = Path.Combine(root,"video.mp4"); byte[] bytes = [1,2,3,4]; await File.WriteAllBytesAsync(path,bytes);
        try
        {
            var widget = new ConfiguredWidget { Id="media", PackageId="example.media", PublisherId="example", Name="Media", InstanceId="media.instance", WorkerExecutable=Environment.ProcessPath!,
                PackageRoot=root, VerifiedPackageFiles=new Dictionary<string,VerifiedPackageFile> { ["video.mp4"]=new("video.mp4",4,Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant()) } };
            var asset = new MediaPlayerDefinition(MediaPlayerSource.PackageAsset("video.mp4"),new());
            Check((await MediaPlayerSourceResolver.ResolveAsync(widget,asset,default)).Uri == new Uri(path).AbsoluteUri);
            await File.WriteAllBytesAsync(path,[4,3,2,1]);
            await Reject(() => MediaPlayerSourceResolver.ResolveAsync(widget,asset,default));
            await Reject(() => MediaPlayerSourceResolver.ResolveAsync(widget,asset with { Source=MediaPlayerSource.PackageAsset("missing.mp4") },default));
            var local = new MediaPlayerDefinition(MediaPlayerSource.LocalFile(path),new());
            await Reject(() => MediaPlayerSourceResolver.ResolveAsync(widget,local,default));
            Check((await MediaPlayerSourceResolver.ResolveAsync(widget with { ExecutionTrust=WidgetExecutionTrust.FullTrustCurrentUser },local,default)).Uri == new Uri(path).AbsoluteUri);
            var web = new MediaPlayerDefinition(MediaPlayerSource.WebUrl("https://example.com/video.mp4"),new());
            Check((await MediaPlayerSourceResolver.ResolveAsync(widget,web,default)).Uri == web.Source.Location);
            var image = new MediaPlayerDefinition(MediaPlayerSource.WebImage("https://example.com/opaque-attachment"), new());
            var resolvedImage = await MediaPlayerSourceResolver.ResolveAsync(widget, image, default);
            Check(resolvedImage.IsImage && resolvedImage.Uri == image.Source.Location && resolvedImage.Width == 0);
            await Reject(() => MediaPlayerSourceResolver.ResolveAsync(widget,
                image with { Source = MediaPlayerSource.WebImage("http://example.com/image.png") }, default));
        }
        finally { File.Delete(path); Directory.Delete(root); }
    }
    internal static async Task Authority()
    {
        var media = new MediaPlayerDefinition(MediaPlayerSource.PublicFile("https://example.com/video.mp4"),new());
        var widget = new ConfiguredWidget { Id="media", PackageId="example.media", PublisherId="example", Name="Media", InstanceId="media.instance", WorkerExecutable=Environment.ProcessPath!,
            WorkerFingerprint=new string('a',64), CatalogFingerprint=new string('b',64) };
        await using var fixture = new RegistryFixture(new([widget]),configure:(_,client)=>client.SnapshotFactory=sequence=>
            new WidgetView(UI.MediaPlayer(media.Source,"player",options:media.Options) with { Media = media }).CreateSnapshot(widget.InstanceId,sequence));
        await fixture.SetLifecycleAsync("media",WidgetLifecycleState.Interactive);
        var frame = await fixture.GetSnapshotAsync("media");
        var request = new BridgeMediaPlayerRequest("media",frame.WorkerRun!,frame.Snapshot.Sequence,"player",media);
        Check(fixture.Registry.DemandMediaPlayer(request).Id=="media");
        try { fixture.Registry.DemandMediaPlayer(request with { Media = media with { Source = media.Source with { IsPublicMedia = false } } });
            throw new Exception("Public media transport was downgraded by a resolution request."); }
        catch (BridgeProtocolException) { }
        try { fixture.Registry.DemandMediaPlayer(request with { GalleryItemId = "not-declared" }); throw new Exception("Forged member admitted."); }
        catch (BridgeProtocolException) { }
        _=await fixture.GetSnapshotAsync("media");
        Check(fixture.Registry.DemandMediaPlayer(request).Id=="media");
        try { fixture.Registry.DemandMediaPlayer(request with { Media=media with { Source=MediaPlayerSource.WebUrl("https://example.com/forged") } }); throw new Exception("Forged source admitted."); }
        catch(BridgeProtocolException) { }
        media=media with { Source=MediaPlayerSource.WebUrl("https://example.com/replaced") };
        _=await fixture.GetSnapshotAsync("media");
        try { fixture.Registry.DemandMediaPlayer(request); throw new Exception("Stale source admitted."); } catch(BridgeProtocolException) { }
        media = new(MediaPlayerSource.WebUrl("https://example.com/first"), new())
        {
            Gallery = [new("first", "First", MediaPlayerSource.WebUrl("https://example.com/first")),
                new("second", "Second", MediaPlayerSource.WebImage("https://example.com/second"))],
        };
        frame = await fixture.GetSnapshotAsync("media");
        request = new("media", frame.WorkerRun!, frame.Snapshot.Sequence, "player", media) { GalleryItemId = "second" };
        Check(fixture.Registry.DemandMediaPlayer(request).Id == "media");
        media = media with { Gallery = [media.Gallery![0], media.Gallery[1] with { Source = MediaPlayerSource.WebImage("https://example.com/replaced-second") }] };
        _ = await fixture.GetSnapshotAsync("media");
        try { fixture.Registry.DemandMediaPlayer(request); throw new Exception("Changed secondary gallery member admitted."); } catch (BridgeProtocolException) { }
    }
    private static async Task Reject(Func<Task<HostMediaPlayerSource>> action) { try { await action(); } catch(BridgeProtocolException) { return; } throw new Exception("Unsafe media source admitted."); }
    private static void Check(bool value) { if(!value) throw new Exception("Media player resolution regression."); }
}
