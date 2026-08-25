using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

internal static class EmbeddedMediaSurfaceTests
{
    internal static Task Run()
    {
        var view = new WidgetView(UI.Text("Fixture", "root"), ActiveInputScopeId: "root")
        {
            EmbeddedMedia = Valid("primary-media"),
        };
        var snapshot = view.CreateSnapshot("fixture.instance", 4);
        Equal(ProtocolConstants.EmbeddedMediaSurfaceVersion, snapshot.ProtocolVersion);
        Equal("primary-media", snapshot.EmbeddedMedia?.Id);
        var roundTrip = SnapshotJson.Deserialize(SnapshotJson.Serialize(snapshot));
        Equal("media/adapter.html", roundTrip.EmbeddedMedia?.EntryAsset);
        Equal(2, roundTrip.EmbeddedMedia?.Resources.Count);

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
