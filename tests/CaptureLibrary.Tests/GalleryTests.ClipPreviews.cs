using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

internal static partial class GalleryTests
{
    internal static async Task ClipPreviewUsesDeclarativeIdentity()
    {
        await using var fixture = new GalleryFixture();
        fixture.Items[0] = fixture.Items[0] with { Media = fixture.Items[0].Media with
            { ContentType = "video/mp4", DurationSeconds = 5, Origin = CaptureAttachmentOrigin.SavedLibrary } };
        await fixture.Start();
        Check(fixture.Node("capture-library.gallery.preview")?.FocusPreview is { AllowMedia: true, EnterButton: ControllerButton.RightStick, DelayMilliseconds: 1500 },
            "Clip previews lost the shared delayed/explicit-entry policy");
        using var host = WidgetTestHost.CreateIndexedCollectionHost(fixture.Widget, "gallery-clip-preview", 100); fixture.Host = host;
        using (var lease = await host.AcquireAsync("capture-library.entries", 0, 3))
        {
            var preview = lease.Range.Items[0].Root.FocusPresentation;
            var media = preview is null ? null : Flatten(preview).Single(node => node.Kind == ViewNodeKind.MediaPlayer).MediaPlayer;
            Check(media is { Options: { AutoPlay: true, Loop: true, Muted: true }, Source.Kind: MediaPlayerSourceKind.SavedCapture } &&
                media.Source.SavedCapture == new SavedCaptureMediaReference(fixture.Items[0].Id, fixture.Items[0].Revision) &&
                media.Source.Attachment is null && media.Source.Location is null,
                "Cached row preview must contain exact saved identity, not an opened attachment/path");
            var screenshot = lease.Range.Items[1].Root.FocusPresentation;
            var imagePlayer = screenshot is null ? null : Flatten(screenshot).Single(node => node.Kind == ViewNodeKind.MediaPlayer).MediaPlayer;
            Check(imagePlayer is { Source.Kind: MediaPlayerSourceKind.SavedCapture, Options: { AutoPlay: false, Loop: false, Muted: true } } &&
                imagePlayer.Source.SavedCapture == new SavedCaptureMediaReference(fixture.Items[1].Id, fixture.Items[1].Revision) && fixture.Opened == 0,
                "Screenshot preview must retain exact lazy saved identity without starting playback or opening files during realization");
            var playerNode = Flatten(preview!).Single(node => node.Kind == ViewNodeKind.MediaPlayer);
            Check(playerNode.ArtworkHandle == Flatten(lease.Range.Items[0].Root).First(node => node.Kind == ViewNodeKind.Image).ArtworkHandle &&
                playerNode.ImageFit == ImageFit.Contain,
                "Clip poster must reuse the same scoped thumbnail handle without a second resolver");
        }
        await fixture.Action("capture-library.selection.toggle"); host.PublishSnapshot();
        using (var selection = await host.AcquireAsync("capture-library.entries", 0, 3))
            Check(fixture.Node("capture-library.gallery.preview") is null && selection.Range.Items.All(item => item.Root.FocusPresentation is null),
                "Batch selection retained automatic clip playback demand");
        await fixture.Action("capture-library.selection.toggle");
        await fixture.Action("capture-library.settings");
        await fixture.Until(() => fixture.Node("capture-library.settings.previews") is not null && !fixture.Busy);
        await fixture.Action("capture-library.option.previews.off");
        await fixture.Until(() => !fixture.Busy && fixture.Node("capture-library.settings.previews")!.SelectOptions.Any(option => option.ActionId == "capture-library.option.previews.off" && option.IsSelected));
        await fixture.Action("capture-library.settings.back"); host.PublishSnapshot();
        using var disabled = await host.AcquireAsync("capture-library.entries", 0, 3);
        Check(disabled.Range.Items.All(item => item.Root.FocusPresentation is null) && fixture.Node("capture-library.gallery.preview") is null && fixture.Opened == 0,
            "Disabling automatic clip previews did not withdraw declarative playback demand");
        await using var restored = new GalleryFixture(privateState: new(fixture.PrivateState.Json!, fixture.PrivateState.Revision));
        await restored.Start();
        Check(restored.Node("capture-library.gallery.preview") is null, "A widget restart forgot the disabled preview preference");
    }
}
