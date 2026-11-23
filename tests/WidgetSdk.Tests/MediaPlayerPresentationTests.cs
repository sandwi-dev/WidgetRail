using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

internal static class MediaPlayerPresentationTests
{
    internal static Task InlineVideoContract()
    {
        var video=UI.InlineVideo(MediaPlayerSource.PublicFile("https://media.example/video"),"video","Clip")
            .WithPosterSource("https://media.example/poster");
        var snapshot=new WidgetView(UI.ReadingGroup("message","Message",ActionSurfaceOrientation.Vertical,
            UI.Text("A clip","caption"),video)).CreateSnapshot("inline.fixture",1);
        Check(snapshot.ProtocolVersion==134&&ViewSnapshotValidator.Validate(snapshot).Count==0);
        var definition=snapshot.Root.Children[1].MediaPlayer!;
        Check(definition.Presentation==MediaPlayerPresentation.Inline&&!definition.Options.AutoPlay&&definition.Options.Muted);
        Check(snapshot.Root.Children[1].IsFocusable&&SnapshotJson.Deserialize(SnapshotJson.Serialize(snapshot)).Root.Children[1].MediaPlayer==definition);
        Check(ViewSnapshotValidator.Validate(snapshot with{ProtocolVersion=133}).Count>0);
        Check(!(definition with{Options=definition.Options with{AutoPlay=true}}).IsWellFormed());
        Check(!(definition with{Options=definition.Options with{IsAnimation=true}}).IsWellFormed());
        Check(!(definition with{Source=MediaPlayerSource.PublicImage("https://media.example/image")}).IsWellFormed());
        Check(!(definition with{Gallery=[new("clip","Clip",definition.Source)]}).IsWellFormed());
        try { _=UI.ActionSurface("open","tile","Tile",ActionSurfaceOrientation.Vertical,video);throw new InvalidOperationException("Nested interactive media accepted"); }
        catch(ArgumentException){}
        try { _=UI.ReadingGroup("row","Row",ActionSurfaceOrientation.Vertical,
            UI.ActionSurface("open","tile","Tile",ActionSurfaceOrientation.Vertical,UI.Text("Label","label")) with {Children=[video]});
            throw new InvalidOperationException("Reading action admitted a nested player"); }
        catch(ArgumentException){}
        return Task.CompletedTask;
    }
    internal static Task PublicMediaContract()
    {
        var image = new WidgetView(UI.PublicImage("https://media.example/image?signature=opaque", "image", "Image"))
            .CreateSnapshot("public.fixture", 1);
        Check(image.ProtocolVersion == 133 && image.Root.ImageIsPublicMedia);
        Check(ViewSnapshotValidator.Validate(image).Count == 0);
        Check(ViewSnapshotValidator.Validate(image with { ProtocolVersion = 132 }).Count > 0);
        Check(SnapshotJson.Deserialize(SnapshotJson.Serialize(image)).Root.ImageIsPublicMedia);
        foreach (var bad in new[] { "http://media.example/a", "https://user:secret@media.example/a", "https://media.example:444/a", "https://media.example/a#part", "file:///C:/a" })
        {
            Check(!MediaPlayerSource.PublicFile(bad).IsWellFormed());
            Check(ViewSnapshotValidator.Validate(image with { Root = image.Root with { ImageSource = bad } }).Count > 0);
        }
        Check(!(MediaPlayerSource.LocalFile(@"C:\a.mp4") with { IsPublicMedia = true }).IsWellFormed());
        Check(ViewSnapshotValidator.Validate(image with { Root = image.Root with { ImageSource = null, ArtworkHandle = "artwork" } }).Count > 0);
        var video = MediaPlayerSource.PublicFile("https://media.example/file");
        var gallery = new WidgetView(UI.MediaGallery([new("video", "Video", video),
            new("image", "Image", MediaPlayerSource.PublicImage("https://media.example/image"))], "gallery"))
            .CreateSnapshot("public.gallery", 1);
        Check(gallery.ProtocolVersion == 133 && ViewSnapshotValidator.Validate(gallery).Count == 0);
        Check(gallery.Root.MediaPlayer!.SelectGalleryItem("image").Source.IsPublicMedia);
        var animation = new WidgetView(UI.PublicAnimation("https://media.example/animation", "animation")
            .WithPosterSource("https://media.example/poster")).CreateSnapshot("public.animation", 1);
        Check(animation.ProtocolVersion == 133 && animation.Root.ImageIsPublicMedia && animation.Root.MediaPlayer!.Source.IsPublicMedia);
        Check(ViewSnapshotValidator.Validate(animation).Count == 0);
        var updated = image with { Sequence = 2, Root = image.Root with { ImageIsPublicMedia = false } };
        var generation = new string('a', 32);
        var update = WidgetPresentationDiff.Create(image, updated, generation, 1, PresentationUpdateCapabilities.Current,
            WidgetPresentationTransactionKind.IncrementalUpdate).Update;
        Check(update is not null && !PresentationUpdateMaterializer.Apply(image, update, generation).Root.ImageIsPublicMedia);
        Check((PresentationPropertyMetadata.Impact(PresentationProperty.ImageIsPublicMedia) & PresentationPropertyImpact.Resource) != 0);
        Check(MediaPlayerSource.WebUrl("https://media.example/live.m3u8").IsWellFormed());
        Check(!MediaPlayerSource.WebUrl("https://media.example/live.m3u8").IsPublicMedia);
        return Task.CompletedTask;
    }
    internal static Task AnimationContract()
    {
        var snapshot = new WidgetView(UI.Animation("https://example.com/loop.mp4", "animation")
            .WithPosterSource("https://example.com/poster.png")).CreateSnapshot("animation.fixture", 1);
        var definition = snapshot.Root.MediaPlayer!;
        Check(snapshot.ProtocolVersion == 131 && !snapshot.Root.IsFocusable);
        Check(ViewSnapshotValidator.Validate(snapshot).Count == 0 && WinUiPresentationContract.Validate(snapshot).Count == 0);
        Check(ViewSnapshotValidator.Validate(snapshot with { ProtocolVersion = 130 }).Count > 0);
        Check(SnapshotJson.Deserialize(SnapshotJson.Serialize(snapshot)).Root.MediaPlayer == definition);
        Check(definition.SelectGalleryItem(null) == definition);
        Check(!(definition with { Presentation = (MediaPlayerPresentation)42 }).IsWellFormed());
        Check(!(definition with { Source = MediaPlayerSource.WebUrl("http://example.com/loop.mp4") }).IsWellFormed());
        Check(!(definition with { Source = MediaPlayerSource.LocalFile(@"C:\private.mp4") }).IsWellFormed());
        Check(!(definition with { Gallery = [new("one", "One", definition.Source)] }).IsWellFormed());
        foreach (var options in new[] { definition.Options with { AutoPlay = false }, definition.Options with { Loop = false },
            definition.Options with { Muted = false }, definition.Options with { IsAnimation = false } })
            Check(!(definition with { Options = options }).IsWellFormed());
        var interactive = new WidgetView(UI.MediaPlayer(definition.Source, "viewer", options: definition.Options))
            .CreateSnapshot("animation.viewer", 1);
        Check(interactive.Root.IsFocusable && interactive.ProtocolVersion == 131);
        var gallery = new WidgetView(UI.MediaGallery([new("gif", "GIF", definition.Source, definition.Options)], "gallery"))
            .CreateSnapshot("animation.gallery", 1);
        Check(gallery.ProtocolVersion == 131 && ViewSnapshotValidator.Validate(gallery).Count == 0);
        Check(new WidgetView(UI.MediaPlayer(definition.Source, "poster").WithPosterSource("https://example.com/poster.png"))
            .CreateSnapshot("poster.fixture", 1).ProtocolVersion == 131);
        return Task.CompletedTask;
    }
    internal static Task GalleryContract()
    {
        var items = new[]
        {
            new MediaPlayerGalleryItem("image", "Screenshot", MediaPlayerSource.WebImage("https://example.com/image")),
            new MediaPlayerGalleryItem("clip", "Clip", MediaPlayerSource.WebUrl("https://example.com/clip"), new(Loop: true, Muted: true)),
        };
        var gallery = UI.MediaGallery(items, "gallery");
        var snapshot = new WidgetView(gallery).CreateSnapshot("media.gallery", 1);
        var definition = snapshot.Root.MediaPlayer!;
        Check(snapshot.ProtocolVersion == ProtocolConstants.MediaPlayerGalleryVersion && ViewSnapshotValidator.Validate(snapshot).Count == 0);
        Check(SnapshotJson.Deserialize(SnapshotJson.Serialize(snapshot)).Root.MediaPlayer == definition);
        Check(ViewSnapshotValidator.Validate(snapshot with { ProtocolVersion = 128 }).Count > 0);
        var illustrated = items.Select(item => item with { PublicThumbnail = "https://example.com/small.jpg",
            PublicPreviewImage = "https://example.com/large.jpg" }).ToArray();
        var carousel = new WidgetView(UI.MediaGallery(illustrated, "gallery", inlineCarousel: true)).CreateSnapshot("media.gallery", 1);
        Check(carousel.ProtocolVersion == ProtocolConstants.InlineCarouselVersion && ViewSnapshotValidator.Validate(carousel).Count == 0);
        Check(ViewSnapshotValidator.Validate(carousel with { ProtocolVersion = ProtocolConstants.InlineCarouselVersion - 1 }).Count > 0);
        Check(SnapshotJson.Deserialize(SnapshotJson.Serialize(carousel)).Root.MediaPlayer == carousel.Root.MediaPlayer);
        Check(!(definition with { InlineCarousel = true }).IsWellFormed());
        Check(!(illustrated[0] with { PublicPreviewImage = "file:///C:/private.png" }).IsWellFormed());
        Check(definition.SelectGalleryItem("clip") == new MediaPlayerDefinition(items[1].Source, items[1].Options!) &&
            definition.SelectGalleryItem(null).Source == items[0].Source);
        Check(!definition.DeclaresGalleryItem("unknown"));
        items[0] = items[0] with { Label = "Changed after construction" };
        Check(definition.Gallery![0].Label == "Screenshot");
        Check(definition != definition with { Gallery = [definition.Gallery[0], definition.Gallery[1] with { Source = MediaPlayerSource.WebUrl("https://example.com/replaced") }] });
        Check(!(definition with { Gallery = [] }).IsWellFormed());
        Check(!(definition with { Gallery = [definition.Gallery[0], definition.Gallery[0]] }).IsWellFormed());
        Check(!(definition with { Gallery = Enumerable.Repeat(definition.Gallery[0], 17).ToArray() }).IsWellFormed());
        Check(!(definition with { Gallery = [definition.Gallery[0], definition.Gallery[1] with { Label = "bad\nlabel" }] }).IsWellFormed());
        Check(!(definition with { Source = definition.Gallery[1].Source }).IsWellFormed());
        var preview = UI.FocusPresentationSurface(UI.Button("Attachments", "open", "attachments").PresentOnFocus(gallery), UI.Text("", "empty"), "previews") with
        { Preview = new() { EnterButton = ControllerButton.RightStick, AllowMedia = true } };
        Check(ViewSnapshotValidator.Validate(new WidgetView(preview).CreateSnapshot("gallery.preview", 1)).Count == 0);
        return Task.CompletedTask;
    }

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
        var image = new WidgetView(UI.MediaPlayer(MediaPlayerSource.WebImage("https://example.com/image"), "image"))
            .CreateSnapshot("remote.image", 1);
        Check(image.ProtocolVersion == ProtocolConstants.RemoteImagePlayerVersion && ViewSnapshotValidator.Validate(image).Count == 0);
        Check(ViewSnapshotValidator.Validate(image with { ProtocolVersion = 128 }).Count > 0);
        Check(SnapshotJson.Deserialize(SnapshotJson.Serialize(image)).Root.MediaPlayer == image.Root.MediaPlayer);
        foreach (var invalid in new[] { "http://example.com/image.png", "file:///C:/image.png", "https://user:password@example.com/image.png" })
            Check(!MediaPlayerSource.WebImage(invalid).IsWellFormed());
        Check(!(MediaPlayerSource.WebImage("https://example.com/image.png") with { Attachment = capture }).IsWellFormed());
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
        Check(ViewSnapshotValidator.Validate(poster with { ProtocolVersion = 131,
            Root = poster.Root with { ArtworkHandle = null, ImageSource = "https://example.com/poster.png" } }).Count == 0);
        Check(ViewSnapshotValidator.Validate(poster with { Root = poster.Root with { ImageFit = ImageFit.Cover } })
            .Any(error => error.Code == "invalid_media_poster"));
        return Task.CompletedTask;
    }
    private static void Check(bool value) { if(!value) throw new Exception("Media player contract regression."); }
}
