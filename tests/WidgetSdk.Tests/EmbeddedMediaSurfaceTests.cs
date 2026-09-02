using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;
using WidgetRail.Samples.EmbeddedMediaWidget;
using WidgetRail.EmbeddedMediaAdapterConformance;

internal static class EmbeddedMediaSurfaceTests
{
    internal static async Task Run()
    {
        await PlaybackEventPublicationComposesWithStateOwnersAsync();
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
        var preference = Valid("primary-media") with
        {
            PendingCommand = new()
            {
                Sequence = 8,
                Kind = EmbeddedMediaPlaybackCommandKind.SetPlaybackRate,
                MediaKey = "aurora-tone",
                PlaybackRate = 1.5,
            },
        };
        var preferenceWire = (view with { EmbeddedMedia = preference })
            .CreateSnapshot("fixture.instance", 6);
        Equal(ProtocolConstants.EmbeddedMediaPlaybackPreferencesVersion,
            preferenceWire.ProtocolVersion);
        var preferenceRoundTrip = SnapshotJson.Deserialize(
            SnapshotJson.Serialize(preferenceWire)).EmbeddedMedia!.PendingCommand!;
        Equal(EmbeddedMediaPlaybackCommandKind.SetPlaybackRate,
            preferenceRoundTrip.Kind);
        Equal(1.5D, preferenceRoundTrip.PlaybackRate);
        var familyMedia = Valid("primary-media") with
        {
            AllowedFrameDomainFamilies = ["example.com"],
        };
        var familyWire = (view with { EmbeddedMedia = familyMedia })
            .CreateSnapshot("fixture.instance", 6);
        Equal(ProtocolConstants.EmbeddedMediaFrameDomainFamiliesVersion,
            familyWire.ProtocolVersion);
        Equal("example.com", familyWire.EmbeddedMedia?.AllowedFrameDomainFamilies.Single());
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
        foreach (var origin in new[]
        {
            "https://media-fixture.invalid/",
            "HTTPS://media-fixture.invalid",
            "https://user@media-fixture.invalid",
            "https://*.media-fixture.invalid",
        })
        {
            Error(snapshot with
            {
                EmbeddedMedia = Valid("primary-media") with
                {
                    AllowedFrameOrigins = [origin],
                },
            }, "invalid_origin");
        }
        foreach (var family in new[]
        {
            "com", "co.uk", "deep.example.com", "Example.com",
            "https://example.com", "example.com:443", "*.example.com",
            "user@example.com", "127.0.0.1", "éxample.com",
        })
        {
            Error(snapshot with
            {
                EmbeddedMedia = Valid("primary-media") with
                {
                    AllowedFrameDomainFamilies = [family],
                },
            }, "invalid_domain_family");
        }
        Error(snapshot with
        {
            EmbeddedMedia = Valid("primary-media") with
            {
                AllowedFrameDomainFamilies = Enumerable.Repeat("example.com", 5).ToArray(),
            },
        }, "too_many");
        Error(snapshot with
        {
            EmbeddedMedia = Valid("primary-media") with
            {
                AllowedFrameDomainFamilies = [new string('a', 254)],
            },
        }, "invalid_domain_family");
        Error(snapshot with
        {
            EmbeddedMedia = Valid("primary-media") with
            {
                AllowedFrameDomainFamilies =
                    Enumerable.Range(0, 4).Select(_ => new string('a', 130)).ToArray(),
            },
        }, "aggregate_too_large");
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
            EmbeddedMedia = Valid("primary-media") with
            {
                PendingCommand = new()
                {
                    Sequence = 2,
                    Kind = EmbeddedMediaPlaybackCommandKind.SetPlaybackRate,
                    MediaKey = "fixture-media",
                    PlaybackRate = 2.01,
                },
            },
        }, "out_of_range");
        Error(snapshot with
        {
            EmbeddedMedia = Valid("primary-media") with
            {
                PendingCommand = new()
                {
                    Sequence = 2,
                    Kind = EmbeddedMediaPlaybackCommandKind.SetMuted,
                    MediaKey = "fixture-media",
                },
            },
        }, "required");
        Error(snapshot with
        {
            EmbeddedMedia = Valid("primary-media") with
            {
                PendingCommand = new()
                {
                    Sequence = 2,
                    Kind = EmbeddedMediaPlaybackCommandKind.Play,
                    MediaKey = "fixture-media",
                    Loop = true,
                },
            },
        }, "unexpected");
        Error(snapshot with
        {
            Root = snapshot.Root with
            {
                Children = snapshot.Root.Children.Where(
                    child => child.Kind is not ViewNodeKind.MediaViewport).ToArray(),
            },
        }, "media_viewport_required");
        var retainedHidden = snapshot with
        {
            ProtocolVersion = ProtocolConstants.RetainedHiddenEmbeddedMediaVersion,
            EmbeddedMedia = Valid("primary-media") with
            {
                RetainSessionWhenHidden = true,
            },
            Root = snapshot.Root with
            {
                Children = snapshot.Root.Children.Where(
                    child => child.Kind is not ViewNodeKind.MediaViewport).ToArray(),
            },
        };
        Equal(0, ViewSnapshotValidator.Validate(retainedHidden).Count);
        Equal(true, SnapshotJson.Deserialize(
            SnapshotJson.Serialize(retainedHidden)).EmbeddedMedia?.RetainSessionWhenHidden);
        Error(retainedHidden with
        {
            ProtocolVersion = ProtocolConstants.RetainedHiddenEmbeddedMediaVersion - 1,
        }, "feature_requires_version");
        var overlayFullscreen = snapshot with
        {
            ProtocolVersion = ProtocolConstants.OverlayFullscreenMediaPresentationVersion,
            EmbeddedMedia = Valid("primary-media") with
            {
                OverlayFullscreenCapable = true,
            },
        };
        Equal(0, ViewSnapshotValidator.Validate(overlayFullscreen).Count);
        Equal(true, SnapshotJson.Deserialize(
            SnapshotJson.Serialize(overlayFullscreen)).EmbeddedMedia?
            .OverlayFullscreenCapable);
        Error(overlayFullscreen with
        {
            ProtocolVersion = ProtocolConstants.OverlayFullscreenMediaPresentationVersion - 1,
        }, "feature_requires_version");
        // The seek step drives overlay fullscreen as well as the compact pinned
        // player, so a fullscreen-capable surface may declare one without opting
        // into an unrelated pinned presentation to do it.
        var fullscreenSeekStep = snapshot with
        {
            ProtocolVersion = ProtocolConstants.OverlayFullscreenMediaPresentationVersion,
            EmbeddedMedia = Valid("primary-media") with
            {
                OverlayFullscreenCapable = true,
                MediaSeekStepSeconds = 5,
            },
        };
        Equal(0, ViewSnapshotValidator.Validate(fullscreenSeekStep).Count);
        Equal(5.0, SnapshotJson.Deserialize(SnapshotJson.Serialize(fullscreenSeekStep))
            .EmbeddedMedia?.MediaSeekStepSeconds);
        // A step still needs some presentation that seeks on the widget's behalf.
        Error(fullscreenSeekStep with
        {
            EmbeddedMedia = Valid("primary-media") with { MediaSeekStepSeconds = 5 },
        }, "seek_presentation_required");
        var fullscreenObserver = new FullscreenObserverWidget();
        var entered = new ControllerInputEvent(
            ControllerButton.View, ControllerEventPhase.Pressed,
            ControllerInputContext.OverlayFullscreenPresentation)
        {
            IsOverlayFullscreenActive = true,
        };
        Equal(true, await fullscreenObserver.OnControllerInputAsync(entered));
        Equal(true, await fullscreenObserver.OnControllerInputAsync(entered));
        Equal(1, fullscreenObserver.Changes.Count);
        Equal(true, fullscreenObserver.Changes[0]);
        Equal(true, await fullscreenObserver.OnControllerInputAsync(entered with
        {
            IsOverlayFullscreenActive = false,
        }));
        Equal(2, fullscreenObserver.Changes.Count);
        Equal(true, fullscreenObserver.Changes[0]);
        Equal(false, fullscreenObserver.Changes[1]);
        Error(snapshot with
        {
            ProtocolVersion = ProtocolConstants.RetainedHiddenEmbeddedMediaVersion,
            EmbeddedMedia = Valid("primary-media") with
            {
                RetainSessionWhenHidden = true,
            },
        }, "hidden_media_has_viewport");
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
        var initialSampleSnapshot = sample.RenderSnapshot(
            "embedded-media-sample.instance", 1);
        Equal(0D, Find(initialSampleSnapshot.Root, "media-shell.timeline.slider").Value);
        await EmbeddedMediaAdapterConformanceGate.VerifyAsync(
            initialSampleSnapshot,
            Path.Combine(AppContext.BaseDirectory, "media",
                "embedded-media-sample-adapter.html"),
            EmbeddedMediaFakePlayerProfile.HtmlMediaElement,
            Enum.GetValues<EmbeddedMediaPlaybackCommandKind>());

        await sample.OnActionAsync(new WidgetActionEvent(
            "host.embeddedMedia.togglePlayback", "media-shell.play"));
        var playCommand = sample.Render().EmbeddedMedia?.PendingCommand;
        Equal(EmbeddedMediaPlaybackCommandKind.Play, playCommand?.Kind);
        Equal("aurora-video-0", playCommand?.MediaKey);
        await sample.OnEmbeddedMediaPlaybackEventAsync(new EmbeddedMediaPlaybackEvent
        {
            SurfaceId = "embedded-media-sample.primary",
            Sequence = 1,
            CommandSequence = playCommand!.Sequence + 1,
            MediaKey = playCommand.MediaKey,
            State = EmbeddedMediaPlaybackState.Playing,
            PositionSeconds = 7,
            DurationSeconds = 60,
            Volume = 1,
        });
        Equal(playCommand, sample.Render().EmbeddedMedia?.PendingCommand);
        await sample.OnEmbeddedMediaPlaybackEventAsync(new EmbeddedMediaPlaybackEvent
        {
            SurfaceId = "embedded-media-sample.primary",
            Sequence = 1,
            CommandSequence = playCommand.Sequence,
            MediaKey = "cedar-tone-0",
            State = EmbeddedMediaPlaybackState.Playing,
            PositionSeconds = 7,
            DurationSeconds = 60,
            Volume = 1,
        });
        Equal(playCommand, sample.Render().EmbeddedMedia?.PendingCommand);
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
            "embedded-media-sample.instance", 2).Root, "media-shell.timeline.slider").Value);

        await sample.OnActionAsync(new WidgetActionEvent(
            "host.embeddedMedia.seekForward", "media-shell.seek-forward"));
        var seekCommand = sample.Render().EmbeddedMedia?.PendingCommand;
        Equal(EmbeddedMediaPlaybackCommandKind.Seek, seekCommand?.Kind);
        Equal(9D, seekCommand?.PositionSeconds);
        await sample.OnEmbeddedMediaPlaybackEventAsync(new EmbeddedMediaPlaybackEvent
        {
            SurfaceId = "embedded-media-sample.primary",
            Sequence = 2,
            CommandSequence = seekCommand!.Sequence,
            MediaKey = seekCommand.MediaKey,
            State = EmbeddedMediaPlaybackState.Playing,
            PositionSeconds = 9,
            DurationSeconds = 60,
            Volume = 1,
        });
        await sample.OnActionAsync(new WidgetActionEvent(
            "host.embeddedMedia.next", "media-shell.next"));
        var nextCommand = sample.Render().EmbeddedMedia?.PendingCommand;
        Equal(EmbeddedMediaPlaybackCommandKind.Load, nextCommand?.Kind);
        Equal("horizon-video-1", nextCommand?.MediaKey);

        var preferences = new EmbeddedMediaSampleWidget();
        await preferences.OnActionAsync(new WidgetActionEvent(
            "host.embeddedMedia.playbackRate", "media-shell.rate"));
        var rateCommand = preferences.Render().EmbeddedMedia!.PendingCommand!;
        Equal(EmbeddedMediaPlaybackCommandKind.SetPlaybackRate, rateCommand.Kind);
        Equal(1.25D, rateCommand.PlaybackRate);
        await preferences.OnEmbeddedMediaPlaybackEventAsync(PreferenceEvent(
            1, rateCommand, playbackRate: 1.25, muted: false, loop: true));
        Equal(null, preferences.Render().EmbeddedMedia?.PendingCommand);
        Equal("1.25x", Find(preferences.Render().CreateSnapshot(
            "embedded-media-sample.instance", 3).Root, "media-shell.rate").Text);

        await preferences.OnActionAsync(new WidgetActionEvent(
            "host.embeddedMedia.muted", "media-shell.mute"));
        var muteCommand = preferences.Render().EmbeddedMedia!.PendingCommand!;
        Equal(true, muteCommand.Muted);
        await preferences.OnEmbeddedMediaPlaybackEventAsync(PreferenceEvent(
            2, muteCommand, playbackRate: 1.25, muted: true, loop: true,
            volume: 0.8));
        Equal("Unmute", Find(preferences.Render().CreateSnapshot(
            "embedded-media-sample.instance", 4).Root, "media-shell.mute").Text);

        await preferences.OnActionAsync(new WidgetActionEvent(
            "host.embeddedMedia.loop", "media-shell.loop"));
        var loopCommand = preferences.Render().EmbeddedMedia!.PendingCommand!;
        Equal(false, loopCommand.Loop);
        await preferences.OnEmbeddedMediaPlaybackEventAsync(PreferenceEvent(
            3, loopCommand, playbackRate: 1.25, muted: true, loop: false,
            volume: 0.8));
        Equal("Loop off", Find(preferences.Render().CreateSnapshot(
            "embedded-media-sample.instance", 5).Root, "media-shell.loop").Text);
    }

    private static async Task PlaybackEventPublicationComposesWithStateOwnersAsync()
    {
        var fieldBacked = new PlaybackPublicationWidget(useModel: false);
        await WidgetTestHost.InitializeAsync(fieldBacked);
        var fieldInvalidations = 0;
        fieldBacked.Invalidated += (_, _) => fieldInvalidations++;
        await fieldBacked.ApplyEmbeddedMediaPlaybackEventAsync(
            PublicationEvent(), CancellationToken.None);
        Equal(1, fieldBacked.Value);
        Equal(1, fieldInvalidations);
        await WidgetTestHost.DestroyAsync(fieldBacked);

        var modelBacked = new PlaybackPublicationWidget(useModel: true);
        await WidgetTestHost.InitializeAsync(modelBacked);
        var modelInvalidations = 0;
        modelBacked.Invalidated += (_, _) => modelInvalidations++;
        await modelBacked.ApplyEmbeddedMediaPlaybackEventAsync(
            PublicationEvent(), CancellationToken.None);
        Equal(1, modelBacked.Value);
        Equal(2, modelInvalidations);
        await WidgetTestHost.DestroyAsync(modelBacked);
    }

    private static EmbeddedMediaPlaybackEvent PublicationEvent() => new()
    {
        SurfaceId = "publication.primary",
        Sequence = 1,
        CommandSequence = 0,
        MediaKey = "publication-media",
        State = EmbeddedMediaPlaybackState.Playing,
        PositionSeconds = 1,
        DurationSeconds = 10,
        Volume = 1,
    };

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

    private sealed class FullscreenObserverWidget : Widget
    {
        internal List<bool> Changes { get; } = [];
        public override WidgetView Render() => new(UI.Text("Fixture", "root"));
        public override ValueTask OnOverlayFullscreenChangedAsync(
            bool isActive,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Changes.Add(isActive);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class PlaybackPublicationWidget : Widget
    {
        private readonly WidgetModel<int>? _model;
        private int _value;

        internal PlaybackPublicationWidget(bool useModel)
        {
            if (useModel) _model = CreateModel(0);
        }

        internal int Value => _model?.Value ?? _value;

        public override WidgetView Render() => new(
            UI.Text(Value.ToString(System.Globalization.CultureInfo.InvariantCulture), "root"));

        public override ValueTask OnEmbeddedMediaPlaybackEventAsync(
            EmbeddedMediaPlaybackEvent playbackEvent,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_model is null)
                _value++;
            else
                _model.Update(value => value + 1);
            return ValueTask.CompletedTask;
        }
    }

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

    private static EmbeddedMediaPlaybackEvent PreferenceEvent(
        long eventSequence,
        EmbeddedMediaPlaybackCommand command,
        double playbackRate,
        bool muted,
        bool loop,
        double volume = 0.8) => new()
    {
        SurfaceId = "embedded-media-sample.primary",
        Sequence = eventSequence,
        CommandSequence = command.Sequence,
        MediaKey = command.MediaKey,
        State = EmbeddedMediaPlaybackState.Ready,
        PositionSeconds = 0,
        DurationSeconds = 60,
        Volume = volume,
        PlaybackRate = playbackRate,
        Muted = muted,
        Loop = loop,
    };

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
