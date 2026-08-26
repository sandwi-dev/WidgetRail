using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

internal static class EmbeddedMediaSurfaceTests
{
    internal static Task Run()
    {
        var media = Valid("primary-media");
        var view = new WidgetView(
            UI.Stack(
                "root",
                UI.Text("Fixture", "title"),
                UI.MediaViewport(media, "media-viewport"),
                UI.Text("Native controls", "controls")),
            ActiveInputScopeId: "root")
        {
            EmbeddedMedia = media,
        };
        var snapshot = view.CreateSnapshot("fixture.instance", 4);
        Equal(ProtocolConstants.MediaViewportVersion, snapshot.ProtocolVersion);
        Equal("primary-media", snapshot.EmbeddedMedia?.Id);
        Equal(ViewNodeKind.MediaViewport, snapshot.Root.Children[1].Kind);
        Equal("primary-media", snapshot.Root.Children[1].MediaSurfaceId);
        var roundTrip = SnapshotJson.Deserialize(SnapshotJson.Serialize(snapshot));
        Equal("media/adapter.html", roundTrip.EmbeddedMedia?.EntryAsset);
        Equal(2, roundTrip.EmbeddedMedia?.Resources.Count);
        Equal("primary-media", roundTrip.Root.Children[1].MediaSurfaceId);

        // The additive property must not change the established positional API.
        var (root, initialFocus, quickActions, activeScope, surface) = view;
        Equal("root", root.Id);
        Equal(null, initialFocus);
        Equal(null, quickActions);
        Equal("root", activeScope);
        Equal(null, surface);

        Error(snapshot with
        {
            EmbeddedMedia = Valid("primary-media") with
            {
                EntryAsset = "../outside.html",
            },
        }, "invalid_package_asset_path");
        Error(snapshot with
        {
            EmbeddedMedia = Valid("primary-media") with
            {
                Resources =
                [
                    new() { Path = "media/adapter.html", ContentType = "text/html" },
                    new() { Path = "media/adapter.html", ContentType = "text/html" },
                ],
            },
        }, "duplicate_resource");
        Error(snapshot with
        {
            EmbeddedMedia = Valid("primary-media") with { AspectRatio = double.NaN },
        }, "out_of_range");
        Error(snapshot with
        {
            EmbeddedMedia = Valid("primary-media") with
            {
                Surface = new WidgetSurfaceHints
                {
                    PreferredWidth = 760,
                    PreferredHeight = 425,
                },
            },
        }, "complete_bounds_required");
        Error(snapshot with { ProtocolVersion = 21 }, "feature_requires_version");
        Error(snapshot with
        {
            Root = snapshot.Root with
            {
                Children = snapshot.Root.Children.Where(
                    child => child.Kind is not ViewNodeKind.MediaViewport).ToArray(),
            },
        }, "media_viewport_required");
        Error(snapshot with
        {
            Root = snapshot.Root with
            {
                Children =
                [
                    .. snapshot.Root.Children,
                    snapshot.Root.Children[1] with { Id = "second-viewport" },
                ],
            },
        }, "duplicate_media_viewport");
        Error(snapshot with
        {
            Root = snapshot.Root with
            {
                Children =
                [
                    snapshot.Root.Children[0],
                    snapshot.Root.Children[1] with { MediaSurfaceId = "wrong-media" },
                    snapshot.Root.Children[2],
                ],
            },
        }, "media_viewport_surface_mismatch");
        Error(snapshot with { EmbeddedMedia = null }, "media_viewport_without_surface");
        return Task.CompletedTask;
    }

    private static EmbeddedMediaSurface Valid(string id) => new()
    {
        Id = id,
        AccessibleName = "Provider-neutral media",
        EntryAsset = "media/adapter.html",
        Surface = new WidgetSurfaceHints
        {
            PreferredWidth = 760,
            PreferredHeight = 425,
            MinimumWidth = 320,
            MinimumHeight = 180,
        },
        AspectRatio = 16.0 / 9.0,
        Resources =
        [
            new() { Path = "media/adapter.html", ContentType = "text/html" },
            new() { Path = "media/sample.wav", ContentType = "audio/wav" },
        ],
        Commands =
        [
            EmbeddedMediaCommand.Activate,
            EmbeddedMediaCommand.TogglePlayback,
            EmbeddedMediaCommand.Back,
        ],
    };

    private static void Error(ViewSnapshot snapshot, string code)
    {
        if (!ViewSnapshotValidator.Validate(snapshot).Any(error => error.Code == code))
            throw new InvalidOperationException($"Expected validation error '{code}'.");
    }

    private static void Equal<T>(T expected, T actual)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException($"Expected '{expected}', got '{actual}'.");
    }
}
