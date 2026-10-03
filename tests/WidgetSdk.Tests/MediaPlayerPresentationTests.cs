using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

internal static class MediaPlayerPresentationTests
{
    internal static Task Contract()
    {
        var capture = new CaptureAttachment(new string('a',32), "video/mp4", 640,360,100,5,DateTimeOffset.UtcNow.AddMinutes(1).ToUnixTimeMilliseconds());
        MediaPlayerSource[] sources = [MediaPlayerSource.FromCapture(capture), MediaPlayerSource.PackageAsset("media/trailer.mp4"),
            MediaPlayerSource.WebUrl("https://example.com/live.m3u8"), MediaPlayerSource.LocalFile(@"C:\Videos\guide.mkv")];
        foreach(var source in sources)
        {
            var snapshot = new WidgetView(UI.MediaPlayer(source,"player", options:new(Loop:true))).CreateSnapshot("fixture",1);
            Check(snapshot.ProtocolVersion == 75 && ViewSnapshotValidator.Validate(snapshot).Count == 0 && WinUiPresentationContract.Validate(snapshot).Count == 0);
            Check(SnapshotJson.Deserialize(SnapshotJson.Serialize(snapshot)).Root.MediaPlayer == snapshot.Root.MediaPlayer);
            Check(ViewSnapshotValidator.Validate(snapshot with { ProtocolVersion = 74 }).Count > 0);
        }
        var original = new WidgetView(UI.MediaPlayer(sources[1], "player")).CreateSnapshot("fixture", 1);
        var changed = original with { Sequence = 2, Root = original.Root with { MediaPlayer = original.Root.MediaPlayer! with { Source = sources[2] } } };
        var generation = new string('a', 32);
        var update = WidgetPresentationDiff.Create(original, changed, generation, 1, PresentationUpdateCapabilities.Current,
            WidgetPresentationTransactionKind.IncrementalUpdate).Update;
        Check(update is not null);
        Check(SnapshotJson.Serialize(PresentationUpdateMaterializer.Apply(original, update!, generation)).SequenceEqual(SnapshotJson.Serialize(changed)));
        // The same property participates in resource and authority invalidation.
        Check((PresentationPropertyMetadata.Impact(PresentationProperty.MediaPlayer) & PresentationPropertyImpact.Authority) != 0);
        foreach(var invalid in new[] { "../private.mp4", "/root.mp4", "media/../private.mp4", @"C:\private.mp4", "media//file.mp4" })
            Check(!MediaPlayerSource.PackageAsset(invalid).IsWellFormed());
        foreach(var invalid in new[] { "file:///C:/private", "javascript:alert(1)", "https://user:password@example.com/video" })
            Check(!MediaPlayerSource.WebUrl(invalid).IsWellFormed());
        foreach(var invalid in new[] { @"\\server\share\video.mp4", @"\\.\pipe\secret", @"C:\video.mp4:secret", "relative.mp4" })
            Check(!MediaPlayerSource.LocalFile(invalid).IsWellFormed());
        Check(!new MediaPlayerOptions(Volume:double.NaN).IsWellFormed());
        Check(!new MediaPlayerOptions(PlaybackRate:0).IsWellFormed());
        Check(!new MediaPlayerSource(MediaPlayerSourceKind.WebUrl,"https://example.com/video",capture).IsWellFormed());
        return Task.CompletedTask;
    }
    private static void Check(bool value) { if(!value) throw new Exception("Media player contract regression."); }
}
