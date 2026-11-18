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
    internal static Task PreviewContract()
    {
        var source = MediaPlayerSource.FromSavedCapture(new string('a', 32), 123);
        var player = UI.MediaPlayer(source, "preview.player", options: new(AutoPlay: true, Loop: true, Muted: true));
        var preview = UI.FocusPresentationSurface(UI.Button("Clip", "open", "clip").PresentOnFocus(player), UI.Text("", "empty"), "previews") with
        { Preview = new() { EnterButton = ControllerButton.RightStick, AllowMedia = true } };
        var snapshot = new WidgetView(preview).CreateSnapshot("media-preview", 1);
        Check(snapshot.ProtocolVersion == 116 && ViewSnapshotValidator.Validate(snapshot).Count == 0 && WinUiPresentationContract.Validate(snapshot).Count == 0);
        Check(ViewSnapshotValidator.Validate(snapshot with { ProtocolVersion = 115 }).Count > 0);
        Check(source.Attachment is null && source.Location is null && source.SavedCapture == new SavedCaptureMediaReference(new string('a', 32), 123));
        Check(!MediaPlayerSource.FromSavedCapture("not-a-capture", 123).IsWellFormed() && !MediaPlayerSource.FromSavedCapture(new string('a', 32), -1).IsWellFormed());
        Check(!(source with { Location = "C:/private.mp4" }).IsWellFormed());
        var disabled = snapshot with { Root = snapshot.Root with { FocusPreview = new() { EnterButton = ControllerButton.RightStick } } };
        Check(ViewSnapshotValidator.Validate(disabled).Any(error => error.Code == "invalid_interactive_preview"));
        var trigger = snapshot.Root.Children.Single();
        var declaredPlayer = trigger.FocusPresentation!;
        var two = snapshot with { Root = snapshot.Root with { Children = [trigger with { FocusPresentation = new()
            { Id = "two", Kind = ViewNodeKind.Stack, Children = [declaredPlayer, declaredPlayer with { Id = "another.player" }] } }] } };
        Check(ViewSnapshotValidator.Validate(two).Any(error => error.Code == "preview_media_limit"));
        Check(typeof(FocusPreviewContentContract).GetMethod("Validate", [typeof(ViewNode), typeof(bool)]) is not null);
        var poster = new WidgetView(UI.MediaPlayer(MediaPlayerSource.PackageAsset("media/video.mp4"), "poster.player")
            .WithPoster(new("clip.poster"))).CreateSnapshot("media-poster", 1);
        Check(poster.ProtocolVersion == 116 && poster.Root.ArtworkHandle == "clip.poster" && poster.Root.ImageFit == ImageFit.Contain);
        Check(ViewSnapshotValidator.Validate(poster).Count == 0 && WinUiPresentationContract.Validate(poster).Count == 0);
        Check(ViewSnapshotValidator.Validate(poster with { ProtocolVersion = 115 }).Count > 0);
        Check(SnapshotJson.Deserialize(SnapshotJson.Serialize(poster)).Root.ArtworkHandle == "clip.poster");
        Check(ViewSnapshotValidator.Validate(poster with { Root = poster.Root with { ArtworkHandle = null, ImageSource = "https://example.com/poster.png" } })
            .Any(error => error.Code == "invalid_media_poster"));
        Check(ViewSnapshotValidator.Validate(poster with { Root = poster.Root with { ImageFit = ImageFit.Cover } })
            .Any(error => error.Code == "invalid_media_poster"));
        return Task.CompletedTask;
    }
    private static void Check(bool value) { if(!value) throw new Exception("Media player contract regression."); }
}
