using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;
using WidgetRail.Samples.EmbeddedMediaWidget;

internal static class EmbeddedMediaSurfaceTests
{
    internal static async Task Run()
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
        var playback = Valid("primary-media") with
        {
            AllowedFrameOrigins = ["https://media-fixture.invalid"],
            PendingCommand = new()
            {
                Sequence = 7,
                Kind = EmbeddedMediaPlaybackCommandKind.Seek,
                MediaKey = "aurora-tone",
                PositionSeconds = 20,
            },
        };
        var playbackSnapshot = view with { EmbeddedMedia = playback };
        var playbackWire = playbackSnapshot.CreateSnapshot("fixture.instance", 5);
        Equal(ProtocolConstants.EmbeddedMediaPlaybackVersion, playbackWire.ProtocolVersion);
        Equal(7L, playbackWire.EmbeddedMedia?.PendingCommand?.Sequence);
        using (var document = System.Text.Json.JsonDocument.Parse(SnapshotJson.Serialize(snapshot)))
        {
            var commands = document.RootElement.GetProperty("embeddedMedia").GetProperty("commands")
                .EnumerateArray().Select(command => command.GetString()).ToArray();
            Equal(
                "navigatePrevious,navigateNext,activate,back,togglePlayback,seekBackward,seekForward",
                string.Join(',', commands));
        }

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
            EmbeddedMedia = Valid("primary-media") with
            {
                AllowedFrameOrigins = ["http://not-secure.invalid"],
            },
        }, "invalid_origin");
        Error(snapshot with
        {
            EmbeddedMedia = Valid("primary-media") with
            {
                PendingCommand = new()
                {
                    Sequence = 1,
                    Kind = EmbeddedMediaPlaybackCommandKind.Seek,
                    MediaKey = "fixture-media",
                },
            },
        }, "required");
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

        var sample = new EmbeddedMediaSampleWidget();
        var initialSample = sample.Render();
        Equal(null, initialSample.EmbeddedMedia?.PendingCommand);
        Equal(0D, Find(initialSample.CreateSnapshot(
            "embedded-media-sample.instance", 1).Root, "media-shell.progress").Value);

        await sample.OnActionAsync(new WidgetActionEvent(
            "host.embeddedMedia.togglePlayback", "media-shell.play"));
        var playCommand = sample.Render().EmbeddedMedia?.PendingCommand;
        Equal(EmbeddedMediaPlaybackCommandKind.Play, playCommand?.Kind);
        Equal("aurora-tone-0", playCommand?.MediaKey);
        await sample.OnEmbeddedMediaPlaybackEventAsync(new EmbeddedMediaPlaybackEvent
        {
            SurfaceId = "embedded-media-sample.primary",
            Sequence = 1,
            CommandSequence = playCommand!.Sequence,
            MediaKey = playCommand.MediaKey,
            State = EmbeddedMediaPlaybackState.Playing,
            PositionSeconds = 7,
            DurationSeconds = 60,
            Volume = 1,
        });
        var playingSample = sample.Render();
        Equal(null, playingSample.EmbeddedMedia?.PendingCommand);
        Equal(7D, Find(playingSample.CreateSnapshot(
            "embedded-media-sample.instance", 2).Root, "media-shell.progress").Value);

        await sample.OnActionAsync(new WidgetActionEvent(
            "host.embeddedMedia.seekForward", "media-shell.seek-forward"));
        var seekCommand = sample.Render().EmbeddedMedia?.PendingCommand;
        Equal(EmbeddedMediaPlaybackCommandKind.Seek, seekCommand?.Kind);
        Equal(17D, seekCommand?.PositionSeconds);
        await sample.OnEmbeddedMediaPlaybackEventAsync(new EmbeddedMediaPlaybackEvent
        {
            SurfaceId = "embedded-media-sample.primary",
            Sequence = 2,
            CommandSequence = seekCommand!.Sequence,
            MediaKey = seekCommand.MediaKey,
            State = EmbeddedMediaPlaybackState.Playing,
            PositionSeconds = 17,
            DurationSeconds = 60,
            Volume = 1,
        });
        await sample.OnActionAsync(new WidgetActionEvent(
            "host.embeddedMedia.next", "media-shell.next"));
        var nextCommand = sample.Render().EmbeddedMedia?.PendingCommand;
        Equal(EmbeddedMediaPlaybackCommandKind.Load, nextCommand?.Kind);
        Equal("aurora-tone-1", nextCommand?.MediaKey);
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
            EmbeddedMediaCommand.Previous,
            EmbeddedMediaCommand.Next,
            EmbeddedMediaCommand.Activate,
            EmbeddedMediaCommand.Back,
            EmbeddedMediaCommand.TogglePlayback,
            EmbeddedMediaCommand.SeekBackward,
            EmbeddedMediaCommand.SeekForward,
        ],
    };

    private static ViewNode Find(ViewNode node, string id)
    {
        if (string.Equals(node.Id, id, StringComparison.Ordinal)) return node;
        foreach (var child in node.Children)
        {
            var match = FindOrNull(child, id);
            if (match is not null) return match;
        }
        throw new InvalidOperationException($"Missing node '{id}'.");
    }

    private static ViewNode? FindOrNull(ViewNode node, string id)
    {
        if (string.Equals(node.Id, id, StringComparison.Ordinal)) return node;
        foreach (var child in node.Children)
        {
            var match = FindOrNull(child, id);
            if (match is not null) return match;
        }
        return null;
    }

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
