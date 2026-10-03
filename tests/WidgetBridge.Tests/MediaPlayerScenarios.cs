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
        var projection=session.ResolvePinnedProjection(frame,"host.full-widget");
        var selection=await session.SelectPinnedLayoutAsync(projection,cancellationToken:deadline.Token);
        var pinned=await session.ResolveMediaPlayerSourceAsync(frame,"player",selection,projection,deadline.Token);
        Check(pinned==source);
        await session.ClearPinnedSelectionAsync(selection,frame,cancellationToken:deadline.Token);
        await session.DisposeAsync(); await serving.WaitAsync(TimeSpan.FromSeconds(5));
    }
    internal sealed class Probe : Widget
    {
        public override WidgetView Render()=>new(UI.MediaPlayer(MediaPlayerSource.WebUrl("https://example.com/video.mp4"),"player"));
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
        }
        finally { File.Delete(path); Directory.Delete(root); }
    }
    internal static async Task Authority()
    {
        var media = new MediaPlayerDefinition(MediaPlayerSource.WebUrl("https://example.com/video.mp4"),new());
        var widget = new ConfiguredWidget { Id="media", PackageId="example.media", PublisherId="example", Name="Media", InstanceId="media.instance", WorkerExecutable=Environment.ProcessPath!,
            WorkerFingerprint=new string('a',64), CatalogFingerprint=new string('b',64) };
        await using var fixture = new RegistryFixture(new([widget]),configure:(_,client)=>client.SnapshotFactory=sequence=>
            new WidgetView(UI.MediaPlayer(media.Source,"player",options:media.Options)).CreateSnapshot(widget.InstanceId,sequence));
        await fixture.SetLifecycleAsync("media",WidgetLifecycleState.Interactive);
        var frame = await fixture.GetSnapshotAsync("media");
        var request = new BridgeMediaPlayerRequest("media",frame.WorkerRun!,frame.Snapshot.Sequence,"player",media);
        Check(fixture.Registry.DemandMediaPlayer(request).Id=="media");
        _=await fixture.GetSnapshotAsync("media");
        Check(fixture.Registry.DemandMediaPlayer(request).Id=="media");
        try { fixture.Registry.DemandMediaPlayer(request with { Media=media with { Source=MediaPlayerSource.WebUrl("https://example.com/forged") } }); throw new Exception("Forged source admitted."); }
        catch(BridgeProtocolException) { }
        media=media with { Source=MediaPlayerSource.WebUrl("https://example.com/replaced") };
        _=await fixture.GetSnapshotAsync("media");
        try { fixture.Registry.DemandMediaPlayer(request); throw new Exception("Stale source admitted."); } catch(BridgeProtocolException) { }
    }
    private static async Task Reject(Func<Task<HostMediaPlayerSource>> action) { try { await action(); } catch(BridgeProtocolException) { return; } throw new Exception("Unsafe media source admitted."); }
    private static void Check(bool value) { if(!value) throw new Exception("Media player resolution regression."); }
}
