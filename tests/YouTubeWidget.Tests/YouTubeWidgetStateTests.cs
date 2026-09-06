using Microsoft.VisualStudio.TestTools.UnitTesting;
using WidgetRail.Samples.YouTubeWidget;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

namespace WidgetRail.YouTubeWidget.Tests;

/// <summary>
/// Transition coverage that constructs no widget. Every case here is a pure
/// function of the previous state, so the rules the rendered view depends on are
/// pinned without a host, a lifecycle, or a rendered snapshot.
/// </summary>
[TestClass]
public sealed class YouTubeWidgetStateTests
{
    private const string VideoId = "M7lc1UVf-VE";
    private const string OtherVideoId = "dQw4w9WgXcQ";

    [TestMethod]
    public void CommittedLinkLoadsSupportedLinksAndRejectsForeignOnesInPlace()
    {
        var loaded = YouTubeWidgetState.Initial.WithCommittedLink(
            $"  https://youtu.be/{VideoId}  ", sequence: 1);
        Assert.AreEqual(YouTubeRoute.Player, loaded.Route);
        Assert.AreEqual(VideoId, loaded.Playback.VideoId);
        Assert.IsNull(loaded.Playback.ValidationError);
        Assert.AreEqual($"https://youtu.be/{VideoId}", loaded.Playback.Link,
            "The committed link is retained trimmed, not rewritten.");
        Assert.AreEqual(EmbeddedMediaPlaybackCommandKind.Load,
            loaded.Playback.PendingCommand!.Kind);
        Assert.AreEqual(PendingMediaControl.None, loaded.Playback.PendingControl,
            "A load is not attributable to a transport control, so it never reports busy.");

        var rejected = loaded.WithCommittedLink("https://vimeo.com/1234", sequence: 2);
        Assert.AreEqual(YouTubeRoute.Player, rejected.Route,
            "A rejected link must not move the route.");
        Assert.IsNotNull(rejected.Playback.ValidationError);
        Assert.AreEqual(VideoId, rejected.Playback.VideoId,
            "A rejected link leaves the loaded video alone.");
    }

    [TestMethod]
    public void QueuedCommandsAdvanceOneSequenceAndRetireTheBusyProjection()
    {
        var state = YouTubeWidgetState.Initial.WithCommittedLink(
            $"https://youtu.be/{VideoId}", sequence: 1);
        var first = state.Playback.PendingCommand!;
        var busy = state.WithPlayback(playback =>
            playback.WithPendingFeedback(first.Sequence));
        Assert.IsTrue(busy.Playback.PendingFeedbackVisible);

        var next = busy.WithPlayback(playback => playback.WithQueuedCommand(
            sequence: 2, EmbeddedMediaPlaybackCommandKind.Play, VideoId,
            PendingMediaControl.TogglePlayback));
        Assert.AreEqual(first.Sequence + 1, next.Playback.PendingCommand!.Sequence);
        Assert.IsFalse(next.Playback.PendingFeedbackVisible,
            "A newly queued command restarts the feedback threshold from nothing.");
        Assert.AreEqual(PendingMediaControl.None, next.Playback.BusyControl);
    }

    [TestMethod]
    public void PendingFeedbackOnlyCommitsForTheCommandStillInFlight()
    {
        var state = YouTubeWidgetState.Initial
            .WithCommittedLink($"https://youtu.be/{VideoId}", sequence: 1)
            .WithPlayback(playback => playback.WithQueuedCommand(
                sequence: 2, EmbeddedMediaPlaybackCommandKind.Play, VideoId,
                PendingMediaControl.TogglePlayback));
        var stale = state.Playback.PendingCommand!.Sequence - 1;

        Assert.AreSame(state.Playback, state.Playback.WithPendingFeedback(stale),
            "A timer that outlived its own command commits nothing.");
        var current = state.Playback.WithPendingFeedback(state.Playback.PendingCommand.Sequence);
        Assert.AreEqual(PendingMediaControl.TogglePlayback, current.BusyControl);
    }

    [TestMethod]
    public void PlaybackEventsApplyOnlyTheAdmissionDecisionSuppliedByTheFacility()
    {
        var state = YouTubeWidgetState.Initial
            .WithCommittedLink($"https://youtu.be/{VideoId}", sequence: 1).Playback;
        var pending = state.PendingCommand!;
        var settled = state.WithPlaybackEvent(
            Event(EmbeddedMediaPlaybackState.Ready, sequence: 4,
                commandSequence: pending.Sequence), completesPending: true);
        Assert.IsTrue(settled.HasPlaybackObservation);
        Assert.IsNull(settled.PendingCommand, "The correlated report retires its own command.");
        var observed = settled.WithPlaybackEvent(
            Event(EmbeddedMediaPlaybackState.Playing, sequence: 5, commandSequence: 0),
            completesPending: false);
        Assert.AreEqual(EmbeddedMediaPlaybackState.Playing, observed.State);
    }

    [TestMethod]
    public void PlaybackEventsClampReportedPositionDurationAndVolume()
    {
        var settled = YouTubeWidgetState.Initial
            .WithCommittedLink($"https://youtu.be/{VideoId}", sequence: 1).Playback
            .WithPlaybackEvent(Event(EmbeddedMediaPlaybackState.Playing, sequence: 1,
                commandSequence: 1, position: -12, duration: -3, volume: 4.5),
                completesPending: true);
        Assert.AreEqual(0d, settled.Position);
        Assert.AreEqual(0d, settled.Duration);
        Assert.AreEqual(1d, settled.Volume);
    }

    [TestMethod]
    public void PreferenceTerminalsSeparateCommandFailuresFromProviderPlaybackErrors()
    {
        var observed = YouTubeWidgetState.Initial
            .WithCommittedLink($"https://youtu.be/{VideoId}", sequence: 1).Playback
            .WithPlaybackEvent(Event(EmbeddedMediaPlaybackState.Playing, sequence: 1,
                commandSequence: 1, position: 30, duration: 120),
                completesPending: true);
        foreach (var kind in new[]
                 {
                     EmbeddedMediaPlaybackCommandKind.SetPlaybackRate,
                     EmbeddedMediaPlaybackCommandKind.SetMuted,
                     EmbeddedMediaPlaybackCommandKind.SetLoop,
                 })
        {
            var pending = observed.WithQueuedCommand(2, kind, VideoId,
                PendingMediaControl.PlaybackRate);
            foreach (var code in new[]
                     {
                         "command-unsupported",
                         "player-operation-timeout",
                         "authority-replaced",
                         "player-operation-overlap",
                         "media-key-mismatch",
                     })
            {
                var rejected = pending.WithPlaybackEvent(
                    Event(EmbeddedMediaPlaybackState.Error, sequence: 2,
                        commandSequence: 2, position: 30, duration: 120,
                        errorCode: code), completesPending: true);
                Assert.IsNull(rejected.PendingCommand, $"{kind}/{code} left its command pending.");
                Assert.AreEqual(EmbeddedMediaPlaybackState.Playing, rejected.State,
                    $"{kind}/{code} must not poison established playback.");
                Assert.IsNull(rejected.PlaybackError);
                Assert.IsNotNull(rejected.PreferenceError);
            }

            var providerFailure = pending.WithPlaybackEvent(
                Event(EmbeddedMediaPlaybackState.Error, sequence: 2,
                    commandSequence: 2, position: 30, duration: 120,
                    errorCode: "embedding-disabled"), completesPending: true);
            Assert.IsNull(providerFailure.PendingCommand,
                $"{kind} provider failure did not retire its exact command.");
            Assert.AreEqual(EmbeddedMediaPlaybackState.Error, providerFailure.State);
            Assert.AreEqual("The video owner does not allow embedded playback.",
                providerFailure.PlaybackError);
            Assert.IsNull(providerFailure.PreferenceError,
                $"{kind} provider failure was misclassified as a setting rejection.");
            Assert.AreEqual(VideoId, providerFailure.VideoId);
        }
    }

    [TestMethod]
    public void SeekBufferingRetainsTheSettledSemanticAcrossAHeldRunOfSeeks()
    {
        var playing = YouTubeWidgetState.Initial
            .WithCommittedLink($"https://youtu.be/{VideoId}", sequence: 1).Playback
            .WithPlaybackEvent(Event(EmbeddedMediaPlaybackState.Playing, sequence: 1,
                commandSequence: 1, position: 30, duration: 120),
                completesPending: true);

        var firstSeek = playing.WithQueuedCommand(2, EmbeddedMediaPlaybackCommandKind.Seek,
            VideoId, PendingMediaControl.SeekForward, position: 40);
        var buffering = firstSeek.WithPlaybackEvent(Event(EmbeddedMediaPlaybackState.Loading,
            sequence: 2, commandSequence: firstSeek.PendingCommand!.Sequence, position: 40),
            completesPending: true);
        Assert.AreEqual(EmbeddedMediaPlaybackState.Playing, buffering.SeekBufferingSemantic);
        Assert.AreEqual(EmbeddedMediaPlaybackState.Playing, buffering.PlaybackSemantic,
            "The transport keeps presenting Pause while the seek buffers.");
        Assert.IsTrue(buffering.IsSeekBuffering);
        Assert.IsFalse(buffering.IsMediaLoading,
            "A buffering seek is not the media itself loading.");

        var secondSeek = buffering.WithQueuedCommand(3, EmbeddedMediaPlaybackCommandKind.Seek,
            VideoId, PendingMediaControl.SeekForward, position: 50);
        var stillBuffering = secondSeek.WithPlaybackEvent(Event(EmbeddedMediaPlaybackState.Loading,
            sequence: 3, commandSequence: secondSeek.PendingCommand!.Sequence, position: 50),
            completesPending: true);
        Assert.AreEqual(EmbeddedMediaPlaybackState.Playing, stillBuffering.SeekBufferingSemantic,
            "A later seek in the run inherits the semantic rather than erasing it.");

        var resumed = stillBuffering.WithPlaybackEvent(Event(EmbeddedMediaPlaybackState.Playing,
            sequence: 4, commandSequence: 0, position: 50, duration: 120),
            completesPending: false);
        Assert.IsNull(resumed.SeekBufferingSemantic,
            "Settling out of Loading retires the retained semantic.");
    }

    [TestMethod]
    public void AnyNonSeekCommandSettlesTheRetainedSeekSemantic()
    {
        var buffering = YouTubeWidgetState.Initial
            .WithCommittedLink($"https://youtu.be/{VideoId}", sequence: 1).Playback
            .WithPlaybackEvent(Event(EmbeddedMediaPlaybackState.Playing, sequence: 1,
                commandSequence: 1, position: 30, duration: 120),
                completesPending: true);
        buffering = buffering.WithQueuedCommand(2, EmbeddedMediaPlaybackCommandKind.Seek,
            VideoId, PendingMediaControl.SeekForward, position: 40);
        buffering = buffering.WithPlaybackEvent(Event(EmbeddedMediaPlaybackState.Loading,
            sequence: 2, commandSequence: buffering.PendingCommand!.Sequence, position: 40),
            completesPending: true);
        Assert.IsNotNull(buffering.SeekBufferingSemantic);

        var paused = buffering.WithQueuedCommand(3, EmbeddedMediaPlaybackCommandKind.Pause,
            VideoId, PendingMediaControl.TogglePlayback);
        Assert.IsNull(paused.SeekBufferingSemantic);
    }

    [TestMethod]
    public void TransportDeclarationOutlivesItsOwnPendingCommandButDispatchDoesNot()
    {
        var playing = YouTubeWidgetState.Initial
            .WithCommittedLink($"https://youtu.be/{VideoId}", sequence: 1)
            .WithPlayback(playback => playback.WithPlaybackEvent(
                Event(EmbeddedMediaPlaybackState.Playing, sequence: 1, commandSequence: 1,
                    position: 30, duration: 120), completesPending: true));
        Assert.IsTrue(playing.CanDeclareTransportAction(isActive: true));
        Assert.IsTrue(playing.CanDispatchTransportAction(isActive: true));

        var seeking = playing.WithPlayback(playback => playback.WithQueuedCommand(
            sequence: 2, EmbeddedMediaPlaybackCommandKind.Seek, VideoId,
            PendingMediaControl.SeekForward, position: 40));
        Assert.IsTrue(seeking.CanDeclareTransportAction(isActive: true),
            "Withdrawing the declaration would retract a held binding between repeats.");
        Assert.IsFalse(seeking.CanDispatchTransportAction(isActive: true),
            "One command at a time is the whole admission rule.");
    }

    [TestMethod]
    public void TransportIncludesTheLiveLinkPlayerAndFailsClosedElsewhere()
    {
        var playing = YouTubeWidgetState.Initial
            .WithCommittedLink($"https://youtu.be/{VideoId}", sequence: 1)
            .WithPlayback(playback => playback.WithPlaybackEvent(
                Event(EmbeddedMediaPlaybackState.Playing, sequence: 1, commandSequence: 1,
                    position: 30, duration: 120), completesPending: true));

        Assert.IsFalse(playing.CanDeclareTransportAction(isActive: false));
        Assert.IsFalse(playing.WithRoute(YouTubeRoute.Search)
            .CanDeclareTransportAction(isActive: true));
        Assert.IsTrue(playing.WithRoute(YouTubeRoute.Link)
            .CanDeclareTransportAction(isActive: true),
            "The Link route renders the live player transport and must admit it.");
        Assert.IsFalse(playing.WithRoute(YouTubeRoute.Setup)
            .CanDeclareTransportAction(isActive: true));

        var loading = YouTubeWidgetState.Initial.WithCommittedLink(
            $"https://youtu.be/{VideoId}", sequence: 1);
        Assert.IsFalse(loading.CanDeclareTransportAction(isActive: true),
            "An initial load is not a state the transport may act on.");

        var cuePending = playing.WithPlayback(playback => playback.WithQueuedCommand(
            sequence: 2, EmbeddedMediaPlaybackCommandKind.Cue, VideoId,
            PendingMediaControl.TogglePlayback));
        Assert.IsFalse(cuePending.CanDeclareTransportAction(isActive: true),
            "A pending Cue must keep transport declarations fail-closed.");

        var failed = playing.WithPlayback(playback => playback.WithPlaybackEvent(
            Event(EmbeddedMediaPlaybackState.Error, sequence: 2, commandSequence: 0,
                errorCode: "embedding-disabled"), completesPending: false));
        Assert.IsFalse(failed.CanDeclareTransportAction(isActive: true));
        Assert.AreEqual("The video owner does not allow embedded playback.",
            failed.Playback.Error);
    }

    [TestMethod]
    public void RouteLifecyclePlaybackAndPendingMatrixHasOneTransportPredicate()
    {
        var routes = Enum.GetValues<YouTubeRoute>();
        var playbackCases = new[]
        {
            (EmbeddedMediaPlaybackState.Ready, (EmbeddedMediaPlaybackState?)null, false),
            (EmbeddedMediaPlaybackState.Playing, (EmbeddedMediaPlaybackState?)null, false),
            (EmbeddedMediaPlaybackState.Paused, (EmbeddedMediaPlaybackState?)null, false),
            (EmbeddedMediaPlaybackState.Ended, (EmbeddedMediaPlaybackState?)null, false),
            (EmbeddedMediaPlaybackState.Loading, (EmbeddedMediaPlaybackState?)null, false),
            (EmbeddedMediaPlaybackState.Loading,
                (EmbeddedMediaPlaybackState?)EmbeddedMediaPlaybackState.Playing, false),
            (EmbeddedMediaPlaybackState.Error, (EmbeddedMediaPlaybackState?)null, true),
        };
        var pendingCases = Enum.GetValues<EmbeddedMediaPlaybackCommandKind>()
            .Select(kind => (EmbeddedMediaPlaybackCommandKind?)kind)
            .Prepend(null)
            .ToArray();

        foreach (var route in routes)
        foreach (var active in new[] { false, true })
        foreach (var (playbackState, semantic, hasError) in playbackCases)
        foreach (var pendingKind in pendingCases)
        {
            var playback = new YouTubePlaybackState
            {
                Link = $"https://youtu.be/{VideoId}",
                VideoId = VideoId,
                State = playbackState,
                PlaybackError = hasError ? "Playback failed." : null,
                SeekBufferingSemantic = semantic,
                HasPlaybackObservation = true,
                PendingCommand = pendingKind is null ? null : new EmbeddedMediaPlaybackCommand
                {
                    Sequence = 2,
                    Kind = pendingKind.Value,
                    MediaKey = VideoId,
                },
                PendingControl = pendingKind is null
                    ? PendingMediaControl.None
                    : PendingMediaControl.TogglePlayback,
            };
            var state = YouTubeWidgetState.Initial with
            {
                Route = route,
                Playback = playback,
            };
            var liveRoute = route is YouTubeRoute.Player or YouTubeRoute.Link;
            var expectedDeclaration = liveRoute && active && !hasError &&
                playbackState != EmbeddedMediaPlaybackState.Error &&
                pendingKind is not (EmbeddedMediaPlaybackCommandKind.Load or
                    EmbeddedMediaPlaybackCommandKind.Cue) &&
                (playbackState != EmbeddedMediaPlaybackState.Loading || semantic is not null);
            var label = $"route={route}, active={active}, state={playbackState}, " +
                $"semantic={semantic}, pending={pendingKind}";

            Assert.AreEqual(liveRoute, state.RendersLiveTransport, label);
            Assert.AreEqual(expectedDeclaration,
                state.CanDeclareTransportAction(active), label);
            Assert.AreEqual(expectedDeclaration && pendingKind is null,
                state.CanDispatchTransportAction(active), label);
        }

        var missingMedia = YouTubeWidgetState.Initial.WithRoute(YouTubeRoute.Player);
        Assert.IsFalse(missingMedia.CanDeclareTransportAction(isActive: true));
        Assert.IsFalse(missingMedia.CanDispatchTransportAction(isActive: true));
    }

    [TestMethod]
    public void DeactivationRetiresOnlyTransientPresentationState()
    {
        var buffering = YouTubeWidgetState.Initial
            .WithCommittedLink($"https://youtu.be/{VideoId}", sequence: 1)
            .WithPlayback(playback => playback.WithPlaybackEvent(
                Event(EmbeddedMediaPlaybackState.Playing, sequence: 1, commandSequence: 1,
                    position: 30, duration: 120), completesPending: true))
            .WithPlayback(playback => playback.WithQueuedCommand(
                sequence: 2, EmbeddedMediaPlaybackCommandKind.Seek, VideoId,
                PendingMediaControl.SeekForward, position: 40));
        var reported = buffering.Playback.PendingCommand!.Sequence;
        // The correlated Loading report retires the seek that caused it while the
        // retained semantic stays, so a held run queues its next step on top.
        buffering = buffering
            .WithPlayback(playback => playback.WithPlaybackEvent(Event(
                EmbeddedMediaPlaybackState.Loading, sequence: 2,
                commandSequence: reported, position: 40), completesPending: true))
            .WithPlayback(playback => playback.WithQueuedCommand(
                sequence: 3, EmbeddedMediaPlaybackCommandKind.Seek, VideoId,
                PendingMediaControl.SeekForward, position: 50));
        var inFlight = buffering.Playback.PendingCommand!;
        buffering = buffering.WithPlayback(
            playback => playback.WithPendingFeedback(inFlight.Sequence));
        Assert.IsNotNull(buffering.Playback.SeekBufferingSemantic);
        Assert.AreEqual(PendingMediaControl.SeekForward, buffering.Playback.BusyControl);

        var hidden = buffering.WithPlayback(playback => playback.WithTransientStateCleared());
        Assert.IsNull(hidden.Playback.SeekBufferingSemantic);
        Assert.IsFalse(hidden.Playback.PendingFeedbackVisible);
        Assert.AreEqual(inFlight, hidden.Playback.PendingCommand,
            "The command itself survives hiding; the adapter still owes a report.");
        Assert.AreEqual(40d, hidden.Playback.Position);
    }

    [TestMethod]
    public void ConfigurationOutcomesPickTheRouteAndClearOrRecordTheFailure()
    {
        var configured = YouTubeWidgetState.Initial.WithConfigurationSummary(configured: true);
        Assert.AreEqual(YouTubeRoute.Search, configured.Route);
        Assert.IsTrue(configured.Setup.ConfigurationKnown);
        Assert.IsNull(configured.Setup.Error);

        var unconfigured = YouTubeWidgetState.Initial.WithConfigurationSummary(configured: false);
        Assert.AreEqual(YouTubeRoute.Setup, unconfigured.Route);

        var failed = YouTubeWidgetState.Initial.WithConfigurationFailure("No key store.");
        Assert.AreEqual(YouTubeRoute.Setup, failed.Route);
        Assert.IsTrue(failed.Setup.ConfigurationKnown,
            "A failed probe is still an answer; the loading view must not persist.");
        Assert.AreEqual("No key store.", failed.Setup.Error);
    }

    [TestMethod]
    public void SetupMutationsProjectBusyThenCommitOrRecordAFailure()
    {
        var inFlight = YouTubeWidgetState.Initial
            .WithConfigurationFailure("Earlier failure.")
            .WithSetupInFlight();
        Assert.IsTrue(inFlight.Setup.Busy);
        Assert.IsNull(inFlight.Setup.Error, "Starting a mutation clears the previous failure.");

        var saved = inFlight.WithConfiguredKey();
        Assert.IsFalse(saved.Setup.Busy);
        Assert.IsTrue(saved.Setup.Configured);
        Assert.AreEqual(YouTubeRoute.Search, saved.Route);

        var deleted = saved
            .WithQueryDraft("  live coding  ")
            .WithActiveQuery("live coding")
            .WithSetupInFlight()
            .WithDeletedKey();
        Assert.IsFalse(deleted.Setup.Configured);
        Assert.IsFalse(deleted.Setup.Busy);
        Assert.AreEqual(string.Empty, deleted.Search.QueryDraft);
        Assert.AreEqual(string.Empty, deleted.Search.ActiveQuery);
        Assert.AreEqual(YouTubeRoute.Search, deleted.Route,
            "Deleting a key does not move the route by itself.");

        var failed = inFlight.WithSetupFailure(
            new WidgetCommandError("setup_unavailable", "API-key setup is unavailable."));
        Assert.IsFalse(failed.Setup.Busy);
        Assert.AreEqual("API-key setup is unavailable.", failed.Setup.Error);
    }

    [TestMethod]
    public void RouteTransitionsHonourWhetherSearchIsReachable()
    {
        var unconfigured = YouTubeWidgetState.Initial.WithRoute(YouTubeRoute.Link);
        Assert.AreEqual(YouTubeRoute.Setup, unconfigured.WithConfiguredRoute().Route,
            "Search is not reachable without a key.");

        var configured = YouTubeWidgetState.Initial
            .WithConfigurationSummary(configured: true)
            .WithRoute(YouTubeRoute.Link);
        Assert.AreEqual(YouTubeRoute.Search, configured.WithConfiguredRoute().Route);

        var reopened = configured.WithConfigurationFailure("Broken.").WithSetupRoute();
        Assert.AreEqual(YouTubeRoute.Setup, reopened.Route);
        Assert.IsNull(reopened.Setup.Error, "Opening setup clears the failure it last reported.");
    }

    [TestMethod]
    public void QueryDraftIsTrimmedAndSelectingAResultLoadsItAndRetainsReturnFocus()
    {
        var searching = YouTubeWidgetState.Initial
            .WithConfigurationSummary(configured: true)
            .WithQueryDraft("   synthwave   ");
        Assert.AreEqual("synthwave", searching.Search.QueryDraft);

        var opened = searching.WithSelectedResult(
            "youtube.result." + VideoId, VideoId, sequence: 1,
            playerFocusGroupId: "youtube.player.page");
        Assert.AreEqual(YouTubeRoute.Player, opened.Route);
        Assert.AreEqual("youtube.result." + VideoId, opened.Search.ReturnFocusId);
        Assert.AreEqual(VideoId, opened.Playback.VideoId);
        Assert.AreEqual($"https://www.youtube.com/watch?v={VideoId}", opened.Playback.Link);
        Assert.AreEqual(EmbeddedMediaPlaybackCommandKind.Load,
            opened.Playback.PendingCommand!.Kind);
        Assert.AreEqual("synthwave", opened.Search.QueryDraft,
            "Opening a result leaves the query that produced it intact.");
        Assert.AreEqual("youtube.player.page", opened.RootFocusGroupId);
        Assert.AreEqual(1L, opened.RootFocusRequestId);
    }

    [TestMethod]
    public void RootSectionsIssueMonotonicGroupEntryWithoutMirroringRouteState()
    {
        var search = YouTubeWidgetState.Initial.WithConfigurationSummary(configured: true);
        Assert.AreEqual(YouTubeRootSection.Discover, search.RootSection);

        var player = search.WithRootSection(
            YouTubeRootSection.Player, "youtube.player.page");
        Assert.AreEqual(YouTubeRoute.Link, player.Route);
        Assert.AreEqual(YouTubeRootSection.Player, player.RootSection);
        Assert.AreEqual(1L, player.RootFocusGroupEntryRequest!.RequestId);
        Assert.AreEqual("youtube.player.page", player.RootFocusGroupEntryRequest.GroupId);

        var discover = player.WithRootSection(
            YouTubeRootSection.Discover, "youtube.search.page");
        Assert.AreEqual(YouTubeRoute.Search, discover.Route);
        Assert.AreEqual(2L, discover.RootFocusGroupEntryRequest!.RequestId);
        Assert.AreEqual("youtube.search.page", discover.RootFocusGroupEntryRequest.GroupId);

        var selectedByHeader = discover.WithRootSection(YouTubeRootSection.Player);
        Assert.AreEqual(YouTubeRoute.Link, selectedByHeader.Route);
        Assert.IsNull(selectedByHeader.RootFocusGroupEntryRequest,
            "Header A selection must not publish a competing content-entry request.");
    }

    [TestMethod]
    public void SetupReturnOwnsExactOpenerAndDeleteFallsBackFromUnavailableDiscover()
    {
        var search = YouTubeWidgetState.Initial
            .WithConfigurationSummary(configured: true)
            .WithSetupRoute("youtube.search.query");
        Assert.AreEqual(YouTubeRoute.Search, search.SetupReturnRoute);
        Assert.AreEqual("youtube.search.query", search.SetupReturnFocusId);

        var deleted = search.WithSetupInFlight().WithDeletedKey();
        Assert.AreEqual(YouTubeRoute.Link, deleted.SetupReturnRoute,
            "Deleting the key must not return to unavailable Discover.");
        Assert.IsNull(deleted.SetupReturnFocusId);
        var player = deleted.WithSetupReturnRoute();
        Assert.AreEqual(YouTubeRoute.Link, player.Route);
        Assert.IsNull(player.RootInitialFocusId);

        var unconfiguredPlayer = YouTubeWidgetState.Initial
            .WithConfigurationSummary(configured: false)
            .WithRootSection(YouTubeRootSection.Player)
            .WithSetupRoute("youtube.link");
        Assert.AreEqual(YouTubeRoute.Link, unconfiguredPlayer.SetupReturnRoute);
        Assert.AreEqual("youtube.link", unconfiguredPlayer.SetupReturnFocusId);
        var returned = unconfiguredPlayer.WithSetupReturnRoute();
        Assert.AreEqual(YouTubeRoute.Link, returned.Route);
        Assert.AreEqual("youtube.link", returned.RootInitialFocusId);
    }

    private static EmbeddedMediaPlaybackEvent Event(
        EmbeddedMediaPlaybackState state,
        long sequence,
        long commandSequence,
        double position = 0,
        double duration = 0,
        double volume = 0.8,
        string? mediaKey = null,
        string? errorCode = null) => new()
    {
        SessionId = "youtube-video.primary",
        Sequence = sequence,
        CommandSequence = commandSequence,
        MediaKey = mediaKey ?? VideoId,
        State = state,
        PositionSeconds = position,
        DurationSeconds = duration,
        Volume = volume,
        ErrorCode = errorCode,
    };
}

[TestClass]
public sealed class YouTubeWidgetLifecycleTests
{
    [TestMethod]
    public async Task DeactivationCancelsSetupAndRollsBackOnlyItsBusyErrorProjection()
    {
        var application = new CancelableSetupApplicationService();
        var widget = WidgetTestHost.Attach(
            new YouTubeVideoWidget(application),
            new WidgetTestHostServicesBuilder().Build());

        await WidgetTestHost.InitializeAsync(widget);
        await WidgetTestHost.SetLifecycleStateAsync(widget, WidgetLifecycleState.Visible);
        await widget.OnActionAsync(new WidgetActionEvent(
            "youtube.setup.key.commit", "youtube.setup.key")
        {
            CommittedText = "deterministic-test-key",
        });
        await application.ConfigureStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));

        var busy = widget.RenderSnapshot("youtube-lifecycle-test", 1);
        Assert.IsTrue(Find(busy.Root, "youtube.setup.key").IsDisabled);

        await WidgetTestHost.SetLifecycleStateAsync(widget, WidgetLifecycleState.Background);
        await WidgetTestHost.SetLifecycleStateAsync(widget, WidgetLifecycleState.Visible);

        var restored = widget.RenderSnapshot("youtube-lifecycle-test", 2);
        Assert.IsFalse(Find(restored.Root, "youtube.setup.key").IsDisabled == true,
            "Canceling the active setup attempt must not leave setup permanently busy.");
        Assert.IsNull(FindOrNull(restored.Root, "youtube.setup.error"),
            "Cancellation restores the first baseline error instead of presenting a failure.");
        Assert.IsTrue(application.ConfigureCanceled,
            "The provider attempt must be canceled by the actual active-lifetime transition.");

        await WidgetTestHost.DestroyAsync(widget);
    }

    private static ViewNode Find(ViewNode root, string id) =>
        FindOrNull(root, id) ?? throw new AssertFailedException($"Missing node {id}.");

    private static ViewNode? FindOrNull(ViewNode node, string id)
    {
        if (node.Id == id) return node;
        foreach (var child in node.Children)
        {
            if (FindOrNull(child, id) is { } found) return found;
        }
        return null;
    }

    private sealed class CancelableSetupApplicationService : IYouTubeApplicationService
    {
        public TaskCompletionSource ConfigureStarted { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        public bool ConfigureCanceled { get; private set; }

        public ValueTask<YouTubeConfigurationSummary> GetConfigurationAsync(
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(new YouTubeConfigurationSummary(false));
        }

        public async ValueTask ConfigureApiKeyAsync(
            string apiKey, CancellationToken cancellationToken)
        {
            ConfigureStarted.TrySetResult();
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                ConfigureCanceled = true;
                throw;
            }
        }

        public ValueTask DeleteApiKeyAsync(CancellationToken cancellationToken) =>
            ValueTask.CompletedTask;

        public ValueTask OpenGoogleCloudConsoleAsync(CancellationToken cancellationToken) =>
            ValueTask.CompletedTask;

        public ValueTask OpenVideoInYouTubeAsync(
            string videoId, CancellationToken cancellationToken) =>
            ValueTask.CompletedTask;

        public ValueTask<YouTubeSearchPage> SearchAsync(
            string query,
            string? pageToken,
            int pageSize,
            CancellationToken cancellationToken) =>
            ValueTask.FromResult(new YouTubeSearchPage([], null, 0));

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
