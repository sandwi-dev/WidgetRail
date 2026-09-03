using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WidgetRail.Samples.YouTubeWidget;
using WidgetRail.EmbeddedMediaAdapterConformance;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

namespace WidgetRail.YouTubeWidget.Tests;

[TestClass]
public sealed partial class YouTubeWidgetTests
{
    private const string VideoId = "M7lc1UVf-VE";

    [TestMethod]
    public void StrictParserAcceptsSupportedYouTubeLinks()
    {
        var links = new[]
        {
            $"https://youtu.be/{VideoId}",
            $"https://www.youtube.com/watch?v={VideoId}",
            $"https://youtube.com/watch?feature=share&v={VideoId}&t=4",
            $"https://m.youtube.com/shorts/{VideoId}",
            $"https://www.youtube.com/live/{VideoId}",
            $"https://www.youtube.com/embed/{VideoId}",
        };
        foreach (var link in links)
        {
            Assert.IsTrue(YouTubeLinkParser.TryParse(link, out var parsed), link);
            Assert.AreEqual(VideoId, parsed, link);
        }
    }

    [TestMethod]
    public void StrictParserRejectsForeignMalformedAndUnboundedLinks()
    {
        var links = new[]
        {
            $"http://www.youtube.com/watch?v={VideoId}",
            $"https://youtube.example/watch?v={VideoId}",
            $"https://www.youtube.com.evil.invalid/watch?v={VideoId}",
            "https://youtu.be/not-valid",
            $"https://user@youtu.be/{VideoId}",
            $"https://youtu.be:444/{VideoId}",
            "https://youtu.be/" + new string('a', YouTubeLinkParser.MaximumLinkCharacters),
        };
        foreach (var link in links)
            Assert.IsFalse(YouTubeLinkParser.TryParse(link, out _), link);
    }

    [TestMethod]
    public async Task SnapshotUsesV26BoundedYouTubeAuthorityAndAccessibleNativeControls()
    {
        var widget = await CreateConfiguredLinkWidgetAsync();
        await CommitAsync(widget, $"https://youtu.be/{VideoId}");
        var snapshot = widget.RenderSnapshot("youtube-test", 1);
        Assert.IsGreaterThanOrEqualTo(
            ProtocolConstants.EmbeddedMediaFrameDomainFamiliesVersion,
            snapshot.ProtocolVersion);
        Assert.IsEmpty(ViewSnapshotValidator.Validate(snapshot));
        var media = snapshot.EmbeddedMedia!;
        Assert.AreEqual(200d, media.Surface.MinimumHeight);
        Assert.IsGreaterThanOrEqualTo(200d, media.Surface.MinimumWidth!.Value);
        CollectionAssert.AreEqual(
            new[] { "youtube.com", "googlevideo.com", "ytimg.com", "gstatic.com" },
            media.AllowedFrameDomainFamilies.ToArray());
        Assert.Contains("https://www.youtube.com", media.AllowedFrameOrigins);
        Assert.HasCount(8, media.AllowedFrameOrigins);
        Assert.IsEmpty(snapshot.PinnedLayouts);
        Assert.AreEqual("youtube.playback.toggle", snapshot.InitialFocusId);
        Assert.AreEqual("youtube.root", snapshot.ActiveInputScopeId);
        Assert.AreEqual(ViewNodeKind.MediaViewport,
            Find(snapshot.Root, "youtube.viewport").Kind);
        Assert.AreEqual(YouTubeVideoWidget.ToggleActionId,
            Find(snapshot.Root, "youtube.playback.toggle").ActionId);
        Assert.AreEqual(ViewNodeKind.Slider, Find(snapshot.Root, "youtube.timeline").Kind);
        Assert.AreEqual(ViewNodeKind.Slider, Find(snapshot.Root, "youtube.volume").Kind);
    }

    [TestMethod]
    public async Task AcceptedRoutesGateFocusQuickActionsAndPageShortcuts()
    {
        var widget = await CreateConfiguredSearchWidgetAsync();

        var search = widget.RenderSnapshot("youtube-test", 1);
        Assert.AreEqual("youtube.search.query", search.InitialFocusId);
        Assert.AreEqual("youtube.link.open",
            Find(search.Root, "youtube.search.routes").InitialChildFocusId);
        Assert.IsEmpty(search.QuickActions);
        Assert.IsEmpty(Find(search.Root, "youtube.search.root").Shortcuts);

        await widget.OnActionAsync(new WidgetActionEvent(
            "youtube.link.open", "youtube.link.open"));
        var link = widget.RenderSnapshot("youtube-test", 2);
        Assert.AreEqual("youtube.link", link.InitialFocusId);
        Assert.IsEmpty(link.QuickActions);
        Assert.IsEmpty(Find(link.Root, "youtube.root").Shortcuts);

        await CommitAsync(widget, $"https://youtu.be/{VideoId}");
        var loading = widget.RenderSnapshot("youtube-test", 3);
        Assert.AreEqual("youtube.playback.toggle", loading.InitialFocusId);
        Assert.IsEmpty(loading.QuickActions);
        Assert.IsEmpty(Find(loading.Root, "youtube.root").Shortcuts);
        var load = loading.EmbeddedMedia!.PendingCommand!;

        await ObserveAsync(widget, load, EmbeddedMediaPlaybackState.Ready, 1,
            duration: 120);
        var ready = widget.RenderSnapshot("youtube-test", 4);
        var expectedQuickActions = new[]
        {
            new WidgetQuickAction(ControllerButton.X,
                YouTubeVideoWidget.ToggleActionId, "Play or pause"),
            new WidgetQuickAction(ControllerButton.LeftTrigger,
                YouTubeVideoWidget.SeekBackwardActionId, "Seek backward 10 seconds",
                RepeatPolicy: ControllerActionRepeatPolicy.WhileHeld),
            new WidgetQuickAction(ControllerButton.RightTrigger,
                YouTubeVideoWidget.SeekForwardActionId, "Seek forward 10 seconds",
                RepeatPolicy: ControllerActionRepeatPolicy.WhileHeld),
        };
        var expectedShortcuts = new[]
        {
            new ControllerShortcut(ControllerButton.X, YouTubeVideoWidget.ToggleActionId),
            new ControllerShortcut(ControllerButton.LeftTrigger,
                YouTubeVideoWidget.SeekBackwardActionId,
                RepeatPolicy: ControllerActionRepeatPolicy.WhileHeld),
            new ControllerShortcut(ControllerButton.RightTrigger,
                YouTubeVideoWidget.SeekForwardActionId,
                RepeatPolicy: ControllerActionRepeatPolicy.WhileHeld),
        };
        Assert.AreEqual("youtube.playback.toggle", ready.InitialFocusId);
        CollectionAssert.AreEqual(expectedQuickActions, ready.QuickActions.ToArray());
        CollectionAssert.AreEqual(expectedShortcuts,
            Find(ready.Root, "youtube.root").Shortcuts.ToArray());

        await widget.OnActionAsync(new WidgetActionEvent(
            "youtube.back", "youtube.player.back"));
        var returnedSearch = widget.RenderSnapshot("youtube-test", 5);
        Assert.AreEqual("youtube.search.query", returnedSearch.InitialFocusId);
        Assert.IsEmpty(returnedSearch.QuickActions);
        Assert.IsEmpty(Find(returnedSearch.Root, "youtube.search.root").Shortcuts);
        await WidgetTestHost.DestroyAsync(widget);

        var errorWidget = await CreateConfiguredLinkWidgetAsync();
        await CommitAsync(errorWidget, $"https://youtu.be/{VideoId}");
        var errorLoad = errorWidget.RenderSnapshot("youtube-test", 1)
            .EmbeddedMedia!.PendingCommand!;
        await ObserveAsync(errorWidget, errorLoad, EmbeddedMediaPlaybackState.Error, 1,
            errorCode: "embedding-disabled");
        var error = errorWidget.RenderSnapshot("youtube-test", 2);
        Assert.IsEmpty(error.QuickActions);
        Assert.IsEmpty(Find(error.Root, "youtube.root").Shortcuts);
        await WidgetTestHost.DestroyAsync(errorWidget);
    }

    [TestMethod]
    public async Task RetainedVideoLinkRouteAndFullscreenStatesRemainValid()
    {
        var widget = await CreateConfiguredLinkWidgetAsync();
        await CommitAsync(widget, $"https://youtu.be/{VideoId}");

        var loading = widget.RenderSnapshot("youtube-test", 1);
        Assert.IsEmpty(ViewSnapshotValidator.Validate(loading));
        var loadingFullscreen = Find(loading.Root, "youtube.player.fullscreen");
        Assert.IsTrue(loadingFullscreen.IsDisabled);
        Assert.AreEqual("youtube.player.back", Find(loading.Root, "youtube.link").Focus!.Up);

        var load = loading.EmbeddedMedia!.PendingCommand!;
        await ObserveAsync(widget, load, EmbeddedMediaPlaybackState.Ready, 1,
            duration: 120);
        var ready = widget.RenderSnapshot("youtube-test", 2);
        Assert.IsEmpty(ViewSnapshotValidator.Validate(ready));
        Assert.IsTrue(Find(ready.Root, "youtube.player.fullscreen").IsDisabled is not true);
        Assert.AreEqual("youtube.player.back", Find(ready.Root, "youtube.link").Focus!.Up);

        // Fullscreen is host-owned: the player route declares only the capability
        // and offers the reserved entry action. The widget holds no fullscreen
        // state, so repeated renders cannot disagree about the mode, and no B
        // shortcut is needed - or possible - to leave it.
        Assert.IsTrue(ready.EmbeddedMedia!.OverlayFullscreenCapable);
        Assert.AreEqual("host.embeddedMedia.enterFullscreen",
            Find(ready.Root, "youtube.player.fullscreen").ActionId);
        Assert.IsEmpty(Find(ready.Root, "youtube.root").Shortcuts
            .Where(shortcut => shortcut.Button == ControllerButton.B));

        await widget.OnActionAsync(new WidgetActionEvent(
            YouTubeVideoWidget.EnterFullscreenActionId, "youtube.player.fullscreen"));
        var afterEnter = widget.RenderSnapshot("youtube-test", 3);
        Assert.IsEmpty(ViewSnapshotValidator.Validate(afterEnter));
        Assert.IsTrue(afterEnter.EmbeddedMedia!.OverlayFullscreenCapable);
        Assert.AreEqual("host.embeddedMedia.enterFullscreen",
            Find(afterEnter.Root, "youtube.player.fullscreen").ActionId);

        // Leaving the player route withdraws the capability, which is the exact
        // declaration the host requires to keep its activation alive.
        await widget.OnActionAsync(new WidgetActionEvent(
            "youtube.back", "youtube.player.back"));
        await widget.OnActionAsync(new WidgetActionEvent(
            "youtube.link.open", "youtube.link.open"));
        var retainedLink = widget.RenderSnapshot("youtube-test", 4);
        Assert.IsEmpty(ViewSnapshotValidator.Validate(retainedLink));
        Assert.IsNull(TryFind(retainedLink.Root, "youtube.player.fullscreen"));
        Assert.AreEqual("youtube.player.back",
            Find(retainedLink.Root, "youtube.link").Focus!.Up);
        Assert.IsFalse(retainedLink.EmbeddedMedia!.OverlayFullscreenCapable);
        await WidgetTestHost.DestroyAsync(widget);
    }

    [TestMethod]
    public async Task SetupAppBarDeclaresItsEnabledRouteAsRememberedGroupEntry()
    {
        var widget = await CreateConfiguredSearchWidgetAsync();
        await widget.OnActionAsync(new WidgetActionEvent(
            "youtube.setup.open", "youtube.search.setup"));

        var setup = widget.RenderSnapshot("youtube-test", 1);
        var appBar = Find(setup.Root, "youtube.setup.appbar");
        Assert.AreEqual("youtube.search.open-route", appBar.InitialChildFocusId);
        Assert.AreNotEqual(true,
            Find(setup.Root, "youtube.search.open-route").IsDisabled);
        Assert.IsEmpty(ViewSnapshotValidator.Validate(setup));
        await WidgetTestHost.DestroyAsync(widget);
    }

    [TestMethod]
    public async Task AcceptedPlayerSnapshotAndStylesStayCompactAccessibleAndStateful()
    {
        var widget = await CreateConfiguredLinkWidgetAsync();
        var empty = widget.RenderSnapshot("youtube-test", 1);
        var emptyLink = Find(empty.Root, "youtube.link");
        Assert.AreEqual("Enter youtube.com or youtu.be link here", emptyLink.Text);
        Assert.AreEqual(string.Empty, emptyLink.TextEntryValue);
        Assert.Contains("youtube-link", emptyLink.StyleClasses);
        Assert.Contains("is-empty", emptyLink.StyleClasses);
        Assert.DoesNotContain("has-value", emptyLink.StyleClasses);

        var link = $"https://youtu.be/{VideoId}";
        await CommitAsync(widget, link);
        var loading = widget.RenderSnapshot("youtube-test", 2);
        var load = loading.EmbeddedMedia!.PendingCommand!;
        await ObserveAsync(widget, load, EmbeddedMediaPlaybackState.Ready, 1,
            duration: 120);
        var ready = widget.RenderSnapshot("youtube-test", 3);
        var enteredLink = Find(ready.Root, "youtube.link");
        Assert.AreEqual(link, enteredLink.Text);
        Assert.AreEqual(link, enteredLink.TextEntryValue);
        Assert.Contains("youtube-link", enteredLink.StyleClasses);
        Assert.Contains("has-value", enteredLink.StyleClasses);
        Assert.DoesNotContain("is-empty", enteredLink.StyleClasses);

        Assert.AreEqual("Now playing", Find(ready.Root, "youtube.title").Text);
        Assert.IsNotNull(Find(ready.Root, "youtube.status").Text);

        var toggle = Find(ready.Root, "youtube.playback.toggle");
        Assert.AreEqual(WidgetGlyph.Play, toggle.Glyph);
        Assert.AreEqual(YouTubeVideoWidget.ToggleActionId, toggle.ActionId);
        Assert.AreEqual("Play", toggle.AccessibilityLabel);
        Assert.IsNull(TryFind(ready.Root, "youtube.playback.seek-backward"));
        Assert.IsNull(TryFind(ready.Root, "youtube.playback.seek-forward"));
        var fullscreen = Find(ready.Root, "youtube.player.fullscreen");
        Assert.AreEqual(YouTubeVideoWidget.EnterFullscreenActionId, fullscreen.ActionId);
        Assert.AreEqual("Fullscreen", fullscreen.AccessibilityLabel);
        var captions = Find(ready.Root, YouTubeVideoWidget.CaptionsActionId);
        Assert.AreEqual(YouTubeVideoWidget.CaptionsActionId, captions.ActionId);
        Assert.AreEqual("Captions and player settings", captions.AccessibilityLabel);

        CollectionAssert.AreEqual(
            new[]
            {
                "youtube.playback.toggle",
                "youtube.player.fullscreen",
                "youtube.player.captions",
                "youtube.timeline-group",
                "youtube.volume",
            },
            Find(ready.Root, "youtube.controls").Children.Select(child => child.Id).ToArray());
        CollectionAssert.AreEqual(
            new[] { "youtube.position", "youtube.timeline", "youtube.duration" },
            Find(ready.Root, "youtube.timeline-group").Children
                .Select(child => child.Id).ToArray());
        Assert.AreEqual(ViewNodeKind.Slider, Find(ready.Root, "youtube.timeline").Kind);
        Assert.AreEqual(ViewNodeKind.Slider, Find(ready.Root, "youtube.volume").Kind);

        await widget.OnActionAsync(new WidgetActionEvent(
            YouTubeVideoWidget.ToggleActionId, "youtube.playback.toggle"));
        var play = widget.RenderSnapshot("youtube-test", 4).EmbeddedMedia!.PendingCommand!;
        await ObserveAsync(widget, play, EmbeddedMediaPlaybackState.Playing, 2,
            position: 10, duration: 120);
        var playingToggle = Find(widget.RenderSnapshot("youtube-test", 5).Root,
            "youtube.playback.toggle");
        Assert.AreEqual(WidgetGlyph.Pause, playingToggle.Glyph);
        Assert.AreEqual(YouTubeVideoWidget.ToggleActionId, playingToggle.ActionId);
        Assert.AreEqual("Pause", playingToggle.AccessibilityLabel);

        var styles = File.ReadAllText(
            Path.Combine(AppContext.BaseDirectory, "styles", "default.wrss"));
        StringAssert.Contains(styles,
            ".youtube-link { width: 100%; min-height: 36px; padding: 3px 12px; color: var(--text);");
        StringAssert.Contains(styles,
            ".youtube-link.is-empty { color: var(--text-subdued); }");
        Assert.DoesNotContain(".youtube-link.has-value", styles);
        StringAssert.Contains(styles,
            ".youtube-controls { width: 100%; min-width: 0px; min-height: 36px;");
        StringAssert.Contains(styles,
            ".youtube-timeline-group { min-width: 156px; max-width: 360px;");
        StringAssert.Contains(styles,
            ".youtube-volume { width: 72px; min-width: 56px; height: 32px;");
        await WidgetTestHost.DestroyAsync(widget);
    }

    [TestMethod]
    public async Task PlayerSettingsPublishBoundedPreferencesAndRetainLastObservationOnRejection()
    {
        var widget = await CreateConfiguredLinkWidgetAsync();
        await CommitAsync(widget, $"https://youtu.be/{VideoId}");
        var load = widget.RenderSnapshot("youtube-test", 1).EmbeddedMedia!.PendingCommand!;
        await ObserveAsync(widget, load, EmbeddedMediaPlaybackState.Paused, 1,
            duration: 120, playbackRate: 1);

        await widget.OnActionAsync(new WidgetActionEvent(
            YouTubeVideoWidget.CaptionsActionId, YouTubeVideoWidget.CaptionsActionId));
        var settings = widget.RenderSnapshot("youtube-test", 2);
        Assert.IsEmpty(ViewSnapshotValidator.Validate(settings));
        Assert.AreEqual(WidgetSurfaceAxisMode.Preferred, settings.Surface!.HeightMode);
        var settingsRoot = Find(settings.Root, "youtube.player.settings");
        var settingsTitle = Find(settings.Root, "youtube.player.settings.title");
        var settingsScroll = Find(settings.Root, "youtube.player.settings.scroll");
        Assert.AreSame(settingsTitle, settingsRoot.Children[0]);
        Assert.AreSame(settingsScroll, settingsRoot.Children[1]);
        Assert.AreEqual(ViewNodeKind.Scroll, settingsScroll.Kind);
        Assert.AreEqual(ScrollAxis.Vertical, settingsScroll.ScrollAxis);
        Assert.Contains("youtube-player-settings-scroll", settingsScroll.StyleClasses);
        Assert.IsNull(TryFind(settingsScroll, "youtube.player.settings.title"));
        foreach (var (id, action) in new[]
                 {
                     ("youtube.player.settings.rate-row.action", YouTubeVideoWidget.PlaybackRateActionId),
                     ("youtube.player.settings.muted", YouTubeVideoWidget.MutedActionId),
                     ("youtube.player.settings.loop", YouTubeVideoWidget.LoopActionId),
                     ("youtube.player.settings.captions-row.action", YouTubeVideoWidget.CaptionsActionId),
                     ("youtube.player.settings.open-row.action", YouTubeVideoWidget.OpenInYouTubeActionId),
                 })
        {
            var actionNode = Find(settingsScroll, id);
            Assert.AreEqual(action, actionNode.ActionId, $"{id} lost its exact action authority.");
            Assert.AreNotEqual(true, actionNode.IsDisabled, $"{id} became unreachable.");
        }
        Assert.AreEqual(YouTubeVideoWidget.PlayerSettingsBackActionId,
            settingsRoot.Shortcuts.Single(shortcut => shortcut.Button == ControllerButton.B).ActionId);
        Assert.AreEqual("Auto · managed by YouTube",
            Find(settings.Root, "youtube.player.settings.quality.value").Text);
        var settingsStyles = File.ReadAllText(
            Path.Combine(AppContext.BaseDirectory, "styles", "default.wrss"));
        StringAssert.Contains(settingsStyles,
            ".youtube-player-settings { width: 100%; min-width: 0px; min-height: 0px;");
        Assert.DoesNotContain(".youtube-player-settings { width: 100%; height: 100%;",
            settingsStyles);
        StringAssert.Contains(settingsStyles,
            ".youtube-player-settings-scroll { width: 100%; min-width: 0px; min-height: 0px; flex-basis: 0px; flex-grow: 1; flex-shrink: 1;");

        await widget.OnActionAsync(new WidgetActionEvent(
            YouTubeVideoWidget.PlaybackRateActionId,
            "youtube.player.settings.rate-row.action"));
        var picker = widget.RenderSnapshot("youtube-test", 3);
        Assert.IsEmpty(ViewSnapshotValidator.Validate(picker));
        Assert.AreEqual(WidgetSurfaceAxisMode.Content, picker.Surface!.HeightMode);
        foreach (var rate in new[] { "0.5", "0.75", "1", "1.25", "1.5", "2" })
            Assert.IsNotNull(TryFind(picker.Root, $"youtube.player.settings.rate.{rate}"));

        await widget.OnActionAsync(new WidgetActionEvent(
            "youtube.player.settings.rate.1.5", "youtube.player.settings.rate.1.5"));
        var setRate = widget.RenderSnapshot("youtube-test", 4).EmbeddedMedia!.PendingCommand!;
        Assert.AreEqual(EmbeddedMediaPlaybackCommandKind.SetPlaybackRate, setRate.Kind);
        Assert.AreEqual(1.5, setRate.PlaybackRate);
        await ObserveAsync(widget, setRate, EmbeddedMediaPlaybackState.Paused, 2,
            duration: 120, playbackRate: 1.5);

        await widget.OnActionAsync(new WidgetActionEvent(
            YouTubeVideoWidget.PlaybackRateBackActionId,
            "youtube.player.settings.rate-picker"));
        await widget.OnActionAsync(new WidgetActionEvent(
            YouTubeVideoWidget.MutedActionId, "youtube.player.settings.muted"));
        var setMuted = widget.RenderSnapshot("youtube-test", 5).EmbeddedMedia!.PendingCommand!;
        Assert.AreEqual(EmbeddedMediaPlaybackCommandKind.SetMuted, setMuted.Kind);
        Assert.AreEqual(true, setMuted.Muted);
        await ObserveAsync(widget, setMuted, EmbeddedMediaPlaybackState.Paused, 3,
            duration: 120, playbackRate: 1.5, muted: true);

        await widget.OnActionAsync(new WidgetActionEvent(
            YouTubeVideoWidget.LoopActionId, "youtube.player.settings.loop"));
        var setLoop = widget.RenderSnapshot("youtube-test", 6).EmbeddedMedia!.PendingCommand!;
        Assert.AreEqual(EmbeddedMediaPlaybackCommandKind.SetLoop, setLoop.Kind);
        Assert.AreEqual(true, setLoop.Loop);
        await ObserveAsync(widget, setLoop, EmbeddedMediaPlaybackState.Paused, 4,
            duration: 120, playbackRate: 1.5, muted: true, loop: true);

        await widget.OnActionAsync(new WidgetActionEvent(
            YouTubeVideoWidget.PlaybackRateActionId,
            "youtube.player.settings.rate-row.action"));
        await widget.OnActionAsync(new WidgetActionEvent(
            "youtube.player.settings.rate.1.25", "youtube.player.settings.rate.1.25"));
        var rejected = widget.RenderSnapshot("youtube-test", 7).EmbeddedMedia!.PendingCommand!;
        await ObserveAsync(widget, rejected, EmbeddedMediaPlaybackState.Error, 5,
            duration: 120, playbackRate: 1.5, muted: true, loop: true,
            errorCode: "command-unsupported");
        await widget.OnActionAsync(new WidgetActionEvent(
            YouTubeVideoWidget.PlaybackRateBackActionId,
            "youtube.player.settings.rate-picker"));
        var afterRejection = widget.RenderSnapshot("youtube-test", 8);
        Assert.IsNull(afterRejection.EmbeddedMedia!.PendingCommand);
        Assert.AreEqual("That setting is unavailable for the current YouTube video.",
            Find(afterRejection.Root, "youtube.player.settings.error").Text);
        Assert.IsNotNull(TryFind(
            Find(afterRejection.Root, "youtube.player.settings.scroll"),
            "youtube.player.settings.error"));
        Assert.AreEqual("1.5×",
            Find(afterRejection.Root, "youtube.player.settings.rate-row.value").Text);
        await WidgetTestHost.DestroyAsync(widget);
    }

    [TestMethod]
    public async Task SearchRetainsOnlyAnEstablishedSessionAndPlayerReturnDoesNotReload()
    {
        var widget = WidgetTestHost.Attach(
            new YouTubeVideoWidget(new FakeApplicationService()),
            new WidgetTestHostServicesBuilder().Build());
        var configured = NextInvalidation(widget);
        await WidgetTestHost.InitializeAsync(widget);
        await WidgetTestHost.SetLifecycleStateAsync(widget, WidgetLifecycleState.Visible);
        await configured;

        var home = widget.RenderSnapshot("youtube-test", 1);
        Assert.IsNull(home.EmbeddedMedia);
        Assert.IsNotNull(TryFind(home.Root, "youtube.search.root"));

        await widget.OnActionAsync(new WidgetActionEvent(
            "youtube.link.open", "youtube.link.open"));
        await CommitAsync(widget, $"https://youtu.be/{VideoId}");
        var loading = widget.RenderSnapshot("youtube-test", 2);
        var load = loading.EmbeddedMedia!.PendingCommand!;
        await ObserveAsync(widget, load, EmbeddedMediaPlaybackState.Playing, 1,
            position: 37, duration: 120, volume: 0.55);

        await widget.OnActionAsync(new WidgetActionEvent(
            "youtube.back", "youtube.player.back"));
        var hidden = widget.RenderSnapshot("youtube-test", 3);
        var requiredProtocolVersion = Math.Max(
            ProtocolConstants.RetainedHiddenEmbeddedMediaVersion,
            ProtocolConstants.RememberedChildFocusGroupVersion);
        Assert.AreEqual(requiredProtocolVersion,
            hidden.ProtocolVersion);
        Assert.AreEqual("youtube.search.query",
            Find(hidden.Root, "youtube.search.controls").InitialChildFocusId);
        Assert.IsNotNull(hidden.EmbeddedMedia);
        Assert.IsTrue(hidden.EmbeddedMedia.RetainSessionWhenHidden);
        Assert.IsNull(hidden.EmbeddedMedia.PendingCommand);
        Assert.IsNull(TryFind(hidden.Root, "youtube.viewport"));
        Assert.IsNotNull(TryFind(hidden.Root, "youtube.player.return"));
        Assert.IsEmpty(ViewSnapshotValidator.Validate(hidden));

        await widget.OnActionAsync(new WidgetActionEvent(
            "youtube.player.return", "youtube.player.return"));
        var returned = widget.RenderSnapshot("youtube-test", 4);
        Assert.IsNotNull(returned.EmbeddedMedia);
        Assert.IsFalse(returned.EmbeddedMedia.RetainSessionWhenHidden);
        Assert.IsNull(returned.EmbeddedMedia.PendingCommand);
        Assert.IsNotNull(TryFind(returned.Root, "youtube.viewport"));
        Assert.AreEqual(hidden.EmbeddedMedia.EntryAsset, returned.EmbeddedMedia.EntryAsset);
        CollectionAssert.AreEqual(
            hidden.EmbeddedMedia.Resources.ToArray(),
            returned.EmbeddedMedia.Resources.ToArray());
        Assert.IsEmpty(ViewSnapshotValidator.Validate(returned));
        await WidgetTestHost.DestroyAsync(widget);
    }

    [TestMethod]
    public async Task CommittedLinkCuesWithoutAutoplayThenPlayUsesTypedCommand()
    {
        var widget = await CreateConfiguredLinkWidgetAsync();
        await CommitAsync(widget, $"https://youtu.be/{VideoId}");
        var loading = widget.RenderSnapshot("youtube-test", 1);
        var load = loading.EmbeddedMedia!.PendingCommand!;
        Assert.AreEqual(EmbeddedMediaPlaybackCommandKind.Load, load.Kind);
        Assert.AreEqual(VideoId, load.MediaKey);

        await ObserveAsync(widget, load, EmbeddedMediaPlaybackState.Ready, eventSequence: 1);
        var ready = widget.RenderSnapshot("youtube-test", 2);
        Assert.IsNull(ready.EmbeddedMedia!.PendingCommand);
        StringAssert.Contains(Find(ready.Root, "youtube.status").Text!, "press Play");

        await widget.OnActionAsync(new WidgetActionEvent(
            YouTubeVideoWidget.ToggleActionId, "youtube.playback.toggle"));
        var play = widget.RenderSnapshot("youtube-test", 3).EmbeddedMedia!.PendingCommand!;
        Assert.AreEqual(EmbeddedMediaPlaybackCommandKind.Play, play.Kind);
        Assert.IsGreaterThan(load.Sequence, play.Sequence);
    }

    [TestMethod]
    public async Task TypedPlaybackEventsDrivePauseSeekVolumeAndRejectStaleAuthority()
    {
        var widget = await CreateConfiguredLinkWidgetAsync();
        await CommitAsync(widget, $"https://www.youtube.com/watch?v={VideoId}");
        var load = widget.RenderSnapshot("youtube-test", 1).EmbeddedMedia!.PendingCommand!;
        await ObserveAsync(widget, load, EmbeddedMediaPlaybackState.Ready, 1);

        await widget.OnActionAsync(new WidgetActionEvent(
            YouTubeVideoWidget.ToggleActionId, "youtube.playback.toggle"));
        var play = widget.RenderSnapshot("youtube-test", 2).EmbeddedMedia!.PendingCommand!;
        await ObserveAsync(widget, play, EmbeddedMediaPlaybackState.Playing, 2,
            position: 14, duration: 120, volume: 0.65);
        var playing = widget.RenderSnapshot("youtube-test", 3);
        Assert.AreEqual("Playing", Find(playing.Root, "youtube.status").Text);
        Assert.AreEqual(14d, Find(playing.Root, "youtube.timeline").Value);
        Assert.AreEqual(0.65d, Find(playing.Root, "youtube.volume").Value);

        await widget.OnEmbeddedMediaPlaybackEventAsync(new EmbeddedMediaPlaybackEvent
        {
            SurfaceId = YouTubeVideoWidget.SurfaceId,
            Sequence = 3,
            CommandSequence = play.Sequence - 1,
            MediaKey = VideoId,
            State = EmbeddedMediaPlaybackState.Error,
            PositionSeconds = 0,
            DurationSeconds = 0,
            Volume = 0,
            ErrorCode = "embedding-disabled",
        });
        Assert.AreEqual("Playing",
            Find(widget.RenderSnapshot("youtube-test", 4).Root, "youtube.status").Text);
    }

    [TestMethod]
    public async Task Widge93CorrelatedSeekLoadingKeepsOtherControlsAvailableUntilPlaybackSettles()
    {
        var widget = await CreateConfiguredLinkWidgetAsync();
        await CommitAsync(widget, $"https://www.youtube.com/watch?v={VideoId}");
        var load = widget.RenderSnapshot("youtube-test", 1).EmbeddedMedia!.PendingCommand!;
        await ObserveAsync(widget, load, EmbeddedMediaPlaybackState.Ready, 1);

        await widget.OnActionAsync(new WidgetActionEvent(
            YouTubeVideoWidget.ToggleActionId, "youtube.playback.toggle"));
        var play = widget.RenderSnapshot("youtube-test", 2).EmbeddedMedia!.PendingCommand!;
        await ObserveAsync(widget, play, EmbeddedMediaPlaybackState.Playing, 2,
            position: 14, duration: 120, volume: 0.65);

        await widget.OnActionAsync(new WidgetActionEvent(
            YouTubeVideoWidget.SeekForwardActionId, "youtube.root"));
        var seek = widget.RenderSnapshot("youtube-test", 3).EmbeddedMedia!.PendingCommand!;
        await ObserveAsync(widget, seek, EmbeddedMediaPlaybackState.Loading, 3,
            position: 24, duration: 120, volume: 0.65);

        var buffering = widget.RenderSnapshot("youtube-test", 4);
        Assert.IsNull(buffering.EmbeddedMedia!.PendingCommand);
        Assert.AreEqual("Buffering YouTube video…", Find(buffering.Root, "youtube.status").Text);
        var bufferingToggle = Find(buffering.Root, "youtube.playback.toggle");
        Assert.AreEqual(WidgetGlyph.Pause, bufferingToggle.Glyph);
        Assert.AreEqual("Pause", bufferingToggle.AccessibilityLabel);
        Assert.IsTrue(bufferingToggle.IsDisabled is not true);
        Assert.IsNull(TryFind(buffering.Root, "youtube.playback.seek-backward"));
        Assert.IsNull(TryFind(buffering.Root, "youtube.playback.seek-forward"));
        AssertHeldSeekDeclared(buffering, "while buffering");
        Assert.IsTrue(Find(buffering.Root, "youtube.timeline").IsDisabled is not true);
        Assert.IsTrue(Find(buffering.Root, "youtube.volume").IsDisabled is not true);

        await widget.OnActionAsync(new WidgetActionEvent(
            "youtube.back", "youtube.player.back"));
        var hidden = widget.RenderSnapshot("youtube-test", 5);
        StringAssert.Contains(Find(hidden.Root, "youtube.player.return").Text!, "Playing",
            "The retained row must use the same settled playback semantic as the transport glyph.");
        await widget.OnActionAsync(new WidgetActionEvent(
            "youtube.player.return", "youtube.player.return"));

        await widget.OnEmbeddedMediaPlaybackEventAsync(new EmbeddedMediaPlaybackEvent
        {
            SurfaceId = YouTubeVideoWidget.SurfaceId,
            Sequence = 4,
            CommandSequence = 0,
            MediaKey = VideoId,
            State = EmbeddedMediaPlaybackState.Playing,
            PositionSeconds = 24,
            DurationSeconds = 120,
            Volume = 0.65,
        });
        var settled = widget.RenderSnapshot("youtube-test", 6);
        Assert.AreEqual("Playing", Find(settled.Root, "youtube.status").Text);
        Assert.IsTrue(Find(settled.Root, "youtube.playback.toggle").IsDisabled is not true);
        Assert.IsEmpty(ViewSnapshotValidator.Validate(settled));
    }

    [TestMethod]
    public async Task Widge93PausedSeekLoadingRetainsPlaySemanticUntilPlaybackSettles()
    {
        var widget = await CreateConfiguredLinkWidgetAsync();
        await CommitAsync(widget, $"https://www.youtube.com/watch?v={VideoId}");
        var load = widget.RenderSnapshot("youtube-test", 1).EmbeddedMedia!.PendingCommand!;
        await ObserveAsync(widget, load, EmbeddedMediaPlaybackState.Ready, 1,
            duration: 120, volume: 0.65);
        await widget.OnEmbeddedMediaPlaybackEventAsync(new EmbeddedMediaPlaybackEvent
        {
            SurfaceId = YouTubeVideoWidget.SurfaceId,
            Sequence = 2,
            CommandSequence = 0,
            MediaKey = VideoId,
            State = EmbeddedMediaPlaybackState.Paused,
            PositionSeconds = 14,
            DurationSeconds = 120,
            Volume = 0.65,
        });

        await widget.OnActionAsync(new WidgetActionEvent(
            YouTubeVideoWidget.SeekForwardActionId, "youtube.root"));
        var seek = widget.RenderSnapshot("youtube-test", 2).EmbeddedMedia!.PendingCommand!;
        await ObserveAsync(widget, seek, EmbeddedMediaPlaybackState.Loading, 3,
            position: 24, duration: 120, volume: 0.65);

        var buffering = widget.RenderSnapshot("youtube-test", 3);
        var toggle = Find(buffering.Root, "youtube.playback.toggle");
        Assert.AreEqual("Buffering YouTube video…", Find(buffering.Root, "youtube.status").Text);
        Assert.AreEqual(WidgetGlyph.Play, toggle.Glyph);
        Assert.AreEqual("Play", toggle.AccessibilityLabel);
        Assert.IsTrue(toggle.IsDisabled is not true);
        Assert.IsEmpty(ViewSnapshotValidator.Validate(buffering));

        await widget.OnEmbeddedMediaPlaybackEventAsync(new EmbeddedMediaPlaybackEvent
        {
            SurfaceId = YouTubeVideoWidget.SurfaceId,
            Sequence = 4,
            CommandSequence = 0,
            MediaKey = VideoId,
            State = EmbeddedMediaPlaybackState.Paused,
            PositionSeconds = 24,
            DurationSeconds = 120,
            Volume = 0.65,
        });
        var settled = widget.RenderSnapshot("youtube-test", 4);
        Assert.AreEqual("Paused", Find(settled.Root, "youtube.status").Text);
        Assert.AreEqual(WidgetGlyph.Play, Find(settled.Root, "youtube.playback.toggle").Glyph);
    }

    [TestMethod]
    public async Task Widge93TransportShortcutsDispatchExactCommandsInStableAndSeekBufferingStates()
    {
        var cases = new[]
        {
            (EmbeddedMediaPlaybackState.Playing, false),
            (EmbeddedMediaPlaybackState.Paused, false),
            (EmbeddedMediaPlaybackState.Playing, true),
            (EmbeddedMediaPlaybackState.Paused, true),
        };
        var routes = new[]
        {
            ControllerInputContext.DashboardQuickAction,
            ControllerInputContext.OpenWidget,
        };
        var buttons = new[]
        {
            ControllerButton.X,
            ControllerButton.LeftTrigger,
            ControllerButton.RightTrigger,
        };

        foreach (var (playbackState, seekBuffering) in cases)
        foreach (var route in routes)
        foreach (var button in buttons)
        {
            var (widget, snapshot, position) = await CreateTransportWidgetAsync(
                playbackState, seekBuffering);
            var expectedActionId = button switch
            {
                ControllerButton.X => YouTubeVideoWidget.ToggleActionId,
                ControllerButton.LeftTrigger => YouTubeVideoWidget.SeekBackwardActionId,
                ControllerButton.RightTrigger => YouTubeVideoWidget.SeekForwardActionId,
                _ => throw new InvalidOperationException(),
            };
            Assert.IsTrue(snapshot.QuickActions.Any(action =>
                action.Button == button && action.ActionId == expectedActionId));
            Assert.IsTrue(Find(snapshot.Root, "youtube.root").Shortcuts.Any(shortcut =>
                shortcut.Button == button && shortcut.ActionId == expectedActionId));

            var invalidated = NextInvalidation(widget);
            var handled = await widget.OnControllerInputAsync(new ControllerInputEvent(
                button,
                ControllerEventPhase.Pressed,
                route,
                FocusedElementId: route == ControllerInputContext.OpenWidget
                    ? "youtube.playback.toggle" : null,
                Sequence: 91,
                ActiveInputScopeId: route == ControllerInputContext.OpenWidget
                    ? snapshot.ActiveInputScopeId : null,
                SnapshotSequence: snapshot.Sequence));
            Assert.IsTrue(handled,
                $"{route} {button} was not admitted from {playbackState}, buffering={seekBuffering}.");
            await invalidated;

            var command = widget.RenderSnapshot("youtube-test", snapshot.Sequence + 1)
                .EmbeddedMedia!.PendingCommand!;
            if (button == ControllerButton.X)
            {
                Assert.AreEqual(
                    playbackState == EmbeddedMediaPlaybackState.Playing
                        ? EmbeddedMediaPlaybackCommandKind.Pause
                        : EmbeddedMediaPlaybackCommandKind.Play,
                    command.Kind,
                    $"{route} X selected the wrong toggle command from {playbackState}, buffering={seekBuffering}.");
                Assert.IsNull(command.PositionSeconds);
            }
            else
            {
                Assert.AreEqual(EmbeddedMediaPlaybackCommandKind.Seek, command.Kind);
                Assert.AreEqual(
                    button == ControllerButton.LeftTrigger ? position - 10 : position + 10,
                    command.PositionSeconds,
                    $"{route} {button} selected the wrong bounded seek target from {playbackState}, buffering={seekBuffering}.");
            }
            Assert.AreEqual(VideoId, command.MediaKey);
            await WidgetTestHost.DestroyAsync(widget);
        }
    }

    [TestMethod]
    public async Task Widge93TransportShortcutsFailClosedOutsideCurrentActionablePlayer()
    {
        var search = await CreateConfiguredSearchWidgetAsync();
        await AssertTransportInputsRejectedAsync(
            search, search.RenderSnapshot("youtube-test", 1), "Search route");
        await WidgetTestHost.DestroyAsync(search);

        var loading = await CreateConfiguredLinkWidgetAsync();
        await CommitAsync(loading, $"https://youtu.be/{VideoId}");
        await AssertTransportInputsRejectedAsync(
            loading, loading.RenderSnapshot("youtube-test", 1), "Initial Load");
        await WidgetTestHost.DestroyAsync(loading);

        var error = await CreateConfiguredLinkWidgetAsync();
        await CommitAsync(error, $"https://youtu.be/{VideoId}");
        var errorLoad = error.RenderSnapshot("youtube-test", 1).EmbeddedMedia!.PendingCommand!;
        await ObserveAsync(error, errorLoad, EmbeddedMediaPlaybackState.Error, 1,
            errorCode: "embedding-disabled");
        await AssertTransportInputsRejectedAsync(
            error, error.RenderSnapshot("youtube-test", 2), "Error state");
        await WidgetTestHost.DestroyAsync(error);

        var wrongRoute = await CreateTransportWidgetAsync(
            EmbeddedMediaPlaybackState.Paused, seekBuffering: false);
        await wrongRoute.Widget.OnActionAsync(new WidgetActionEvent(
            "youtube.back", "youtube.player.back"));
        await AssertTransportInputsRejectedAsync(
            wrongRoute.Widget,
            wrongRoute.Widget.RenderSnapshot("youtube-test", 21),
            "Non-player route");
        await WidgetTestHost.DestroyAsync(wrongRoute.Widget);

        var inactive = await CreateTransportWidgetAsync(
            EmbeddedMediaPlaybackState.Playing, seekBuffering: false);
        await WidgetTestHost.SetLifecycleStateAsync(
            inactive.Widget, WidgetLifecycleState.Background);
        await AssertTransportInputsRejectedAsync(
            inactive.Widget,
            inactive.Widget.RenderSnapshot("youtube-test", 21),
            "Deactivated player");
        await WidgetTestHost.DestroyAsync(inactive.Widget);

        var stale = await CreateTransportWidgetAsync(
            EmbeddedMediaPlaybackState.Playing, seekBuffering: false);
        var staleSnapshot = stale.Widget.RenderSnapshot("youtube-test", 21);
        _ = stale.Widget.RenderSnapshot("youtube-test", 22);
        await AssertTransportInputsRejectedAsync(
            stale.Widget, staleSnapshot, "Stale snapshot authority",
            expectDeclarationsAbsent: false);
        await WidgetTestHost.DestroyAsync(stale.Widget);
    }

    [TestMethod]
    public async Task RetainedPlayerLifecycleUsesSdkActivityAndRepublishesBothTransportSurfaces()
    {
        var fields = typeof(YouTubeVideoWidget).GetFields(
                System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.NonPublic |
                System.Reflection.BindingFlags.DeclaredOnly)
            .Select(field => field.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();
        CollectionAssert.AreEqual(
            new[]
            {
                "_application", "_model", "_playbackCommand", "_searchResults",
                "_setupCommand", "_timeProvider",
            },
            fields,
            "WidgetModel, SDK command facilities, the search cursor, provider, and clock are " +
            "the only package owners; no pre-WidgetModel route, lifecycle, playback, task, " +
            "sequence, or gate field may remain.");

        var (widget, _, _) = await CreateTransportWidgetAsync(
            EmbeddedMediaPlaybackState.Playing, seekBuffering: false);
        await WidgetTestHost.SetLifecycleStateAsync(widget, WidgetLifecycleState.Background);
        await AssertTransportInputsRejectedAsync(
            widget, widget.RenderSnapshot("youtube-test", 21), "Background player");

        await WidgetTestHost.SetLifecycleStateAsync(widget, WidgetLifecycleState.Visible);
        await WidgetTestHost.SetLifecycleStateAsync(widget, WidgetLifecycleState.Interactive);
        await widget.OnEmbeddedMediaPlaybackEventAsync(new EmbeddedMediaPlaybackEvent
        {
            SurfaceId = YouTubeVideoWidget.SurfaceId,
            Sequence = 3,
            CommandSequence = 0,
            MediaKey = VideoId,
            State = EmbeddedMediaPlaybackState.Playing,
            PositionSeconds = 51,
            DurationSeconds = 120,
            Volume = 0.65,
        });

        var reactivated = widget.RenderSnapshot("youtube-test", 22);
        var root = Find(reactivated.Root, "youtube.root");
        foreach (var (button, actionId) in new[]
                 {
                     (ControllerButton.X, YouTubeVideoWidget.ToggleActionId),
                     (ControllerButton.LeftTrigger, YouTubeVideoWidget.SeekBackwardActionId),
                     (ControllerButton.RightTrigger, YouTubeVideoWidget.SeekForwardActionId),
                 })
        {
            Assert.IsTrue(reactivated.QuickActions.Any(action =>
                    action.Button == button && action.ActionId == actionId),
                $"Reactivated dashboard omitted {button} -> {actionId}.");
            Assert.IsTrue(root.Shortcuts.Any(shortcut =>
                    shortcut.Button == button && shortcut.ActionId == actionId),
                $"Reactivated page scope omitted {button} -> {actionId}.");
        }

        var pauseInvalidated = NextInvalidation(widget);
        Assert.IsTrue(await widget.OnControllerInputAsync(new ControllerInputEvent(
            ControllerButton.X,
            ControllerEventPhase.Pressed,
            ControllerInputContext.DashboardQuickAction,
            Sequence: 94,
            SnapshotSequence: reactivated.Sequence)));
        await pauseInvalidated;
        var pause = widget.RenderSnapshot("youtube-test", 23).EmbeddedMedia!.PendingCommand!;
        Assert.AreEqual(EmbeddedMediaPlaybackCommandKind.Pause, pause.Kind);
        await ObserveAsync(widget, pause, EmbeddedMediaPlaybackState.Paused, 4,
            position: 51, duration: 120, volume: 0.65);

        var paused = widget.RenderSnapshot("youtube-test", 24);
        var seekInvalidated = NextInvalidation(widget);
        Assert.IsTrue(await widget.OnControllerInputAsync(new ControllerInputEvent(
            ControllerButton.RightTrigger,
            ControllerEventPhase.Pressed,
            ControllerInputContext.OpenWidget,
            FocusedElementId: "youtube.playback.toggle",
            Sequence: 95,
            ActiveInputScopeId: paused.ActiveInputScopeId,
            SnapshotSequence: paused.Sequence)));
        await seekInvalidated;
        var seek = widget.RenderSnapshot("youtube-test", 25).EmbeddedMedia!.PendingCommand!;
        Assert.AreEqual(EmbeddedMediaPlaybackCommandKind.Seek, seek.Kind);
        Assert.AreEqual(61d, seek.PositionSeconds);
        Assert.AreEqual(VideoId, seek.MediaKey);
        await WidgetTestHost.DestroyAsync(widget);
    }

    [TestMethod]
    public async Task DashboardYieldPreservesSearchAndLiveLinkRouteAuthority()
    {
        var (widget, _, _) = await CreateTransportWidgetAsync(
            EmbeddedMediaPlaybackState.Playing, seekBuffering: false);
        await WidgetTestHost.SetLifecycleStateAsync(
            widget, WidgetLifecycleState.Interactive);

        for (var cycle = 0; cycle < 2; cycle++)
        {
            await widget.OnActionAsync(new WidgetActionEvent(
                "youtube.back", "youtube.player.back"));

            var search = widget.RenderSnapshot("youtube-test", 30 + cycle * 10);
            Assert.IsNotNull(TryFind(search.Root, "youtube.search.root"));
            Assert.IsNull(TryFind(search.Root, "youtube.viewport"));
            Assert.IsEmpty(search.QuickActions);
            Assert.IsEmpty(Find(search.Root, "youtube.search.root").Shortcuts);
            Assert.IsTrue(search.EmbeddedMedia!.RetainSessionWhenHidden);

            await WidgetTestHost.SetLifecycleStateAsync(
                widget, WidgetLifecycleState.Visible);
            var dashboardSearch = widget.RenderSnapshot("youtube-test", 31 + cycle * 10);
            Assert.IsNotNull(TryFind(dashboardSearch.Root, "youtube.search.root"),
                "Yielding Search to the dashboard must not rewrite the authored route.");
            Assert.IsEmpty(dashboardSearch.QuickActions);

            await WidgetTestHost.SetLifecycleStateAsync(
                widget, WidgetLifecycleState.Interactive);
            await widget.OnActionAsync(new WidgetActionEvent(
                "youtube.link.open", "youtube.link.open"));
            var link = widget.RenderSnapshot("youtube-test", 32 + cycle * 10);
            Assert.AreEqual("youtube.link", link.InitialFocusId);
            Assert.IsNull(TryFind(link.Root, "youtube.player.fullscreen"));
            Assert.IsFalse(link.EmbeddedMedia!.OverlayFullscreenCapable);

            await WidgetTestHost.SetLifecycleStateAsync(
                widget, WidgetLifecycleState.Visible);
            var dashboardLink = widget.RenderSnapshot("youtube-test", 33 + cycle * 10);
            Assert.AreEqual("youtube.link", dashboardLink.InitialFocusId,
                "Yielding Link-player to the dashboard must preserve its route.");
            Assert.IsNull(TryFind(dashboardLink.Root, "youtube.player.fullscreen"));
            Assert.IsFalse(dashboardLink.EmbeddedMedia!.OverlayFullscreenCapable);

            var root = Find(dashboardLink.Root, "youtube.root");
            foreach (var (button, actionId) in new[]
                     {
                         (ControllerButton.X, YouTubeVideoWidget.ToggleActionId),
                         (ControllerButton.LeftTrigger, YouTubeVideoWidget.SeekBackwardActionId),
                         (ControllerButton.RightTrigger, YouTubeVideoWidget.SeekForwardActionId),
                     })
            {
                Assert.IsTrue(dashboardLink.QuickActions.Any(action =>
                        action.Button == button && action.ActionId == actionId),
                    $"Dashboard Link-player omitted {button} -> {actionId} on cycle {cycle}.");
                Assert.IsTrue(root.Shortcuts.Any(shortcut =>
                        shortcut.Button == button && shortcut.ActionId == actionId),
                    $"Visible Link-player omitted {button} -> {actionId} on cycle {cycle}.");
            }

            var toggleInvalidated = NextInvalidation(widget);
            Assert.IsTrue(await widget.OnControllerInputAsync(new ControllerInputEvent(
                ControllerButton.X,
                ControllerEventPhase.Pressed,
                ControllerInputContext.DashboardQuickAction,
                Sequence: 96 + cycle,
                SnapshotSequence: dashboardLink.Sequence)));
            await toggleInvalidated;
            var toggle = widget.RenderSnapshot("youtube-test", 34 + cycle * 10)
                .EmbeddedMedia!.PendingCommand!;
            var expectedState = cycle == 0
                ? EmbeddedMediaPlaybackState.Paused
                : EmbeddedMediaPlaybackState.Playing;
            Assert.AreEqual(cycle == 0
                    ? EmbeddedMediaPlaybackCommandKind.Pause
                    : EmbeddedMediaPlaybackCommandKind.Play,
                toggle.Kind);
            await ObserveAsync(widget, toggle, expectedState, 3 + cycle,
                position: 50, duration: 120, volume: 0.65);

            await WidgetTestHost.SetLifecycleStateAsync(
                widget, WidgetLifecycleState.Interactive);
        }
        await WidgetTestHost.DestroyAsync(widget);
    }

    [TestMethod]
    public async Task LiveLinkPlayerAdmitsVisibleAndScopedTransportWithoutFullscreen()
    {
        var (widget, _, _) = await CreateTransportWidgetAsync(
            EmbeddedMediaPlaybackState.Playing, seekBuffering: false);
        await WidgetTestHost.SetLifecycleStateAsync(
            widget, WidgetLifecycleState.Interactive);
        await widget.OnActionAsync(new WidgetActionEvent(
            "youtube.back", "youtube.player.back"));
        await widget.OnActionAsync(new WidgetActionEvent(
            "youtube.link.open", "youtube.link.open"));

        var link = widget.RenderSnapshot("youtube-test", 40);
        Assert.AreEqual("youtube.link", link.InitialFocusId);
        Assert.IsNull(TryFind(link.Root, "youtube.player.fullscreen"));
        Assert.IsFalse(link.EmbeddedMedia!.OverlayFullscreenCapable);
        foreach (var id in new[]
                 {
                     "youtube.playback.toggle",
                     "youtube.timeline",
                 })
            Assert.IsTrue(Find(link.Root, id).IsDisabled is not true,
                $"The live Link-player control {id} must be enabled.");

        var expected = new[]
        {
            (ControllerButton.X, YouTubeVideoWidget.ToggleActionId),
            (ControllerButton.LeftTrigger, YouTubeVideoWidget.SeekBackwardActionId),
            (ControllerButton.RightTrigger, YouTubeVideoWidget.SeekForwardActionId),
        };
        var root = Find(link.Root, "youtube.root");
        foreach (var (button, actionId) in expected)
        {
            Assert.IsTrue(link.QuickActions.Any(action =>
                    action.Button == button && action.ActionId == actionId),
                $"Live Link-player dashboard action {button} -> {actionId} was omitted.");
            Assert.IsTrue(root.Shortcuts.Any(shortcut =>
                    shortcut.Button == button && shortcut.ActionId == actionId),
                $"Live Link-player scoped shortcut {button} -> {actionId} was omitted.");
        }

        var pauseInvalidated = NextInvalidation(widget);
        Assert.IsTrue(await widget.OnControllerInputAsync(new ControllerInputEvent(
            ControllerButton.A,
            ControllerEventPhase.Pressed,
            ControllerInputContext.OpenWidget,
            FocusedElementId: "youtube.playback.toggle",
            Sequence: 101,
            ActiveInputScopeId: link.ActiveInputScopeId,
            SnapshotSequence: link.Sequence)));
        await pauseInvalidated;
        var pause = widget.RenderSnapshot("youtube-test", 41).EmbeddedMedia!.PendingCommand!;
        Assert.AreEqual(EmbeddedMediaPlaybackCommandKind.Pause, pause.Kind);
        await ObserveAsync(widget, pause, EmbeddedMediaPlaybackState.Paused, 3,
            position: 50, duration: 120, volume: 0.65);

        var paused = widget.RenderSnapshot("youtube-test", 42);
        var seekInvalidated = NextInvalidation(widget);
        Assert.IsTrue(await widget.OnControllerInputAsync(new ControllerInputEvent(
            ControllerButton.RightTrigger,
            ControllerEventPhase.Pressed,
            ControllerInputContext.OpenWidget,
            FocusedElementId: "youtube.playback.toggle",
            Sequence: 102,
            ActiveInputScopeId: paused.ActiveInputScopeId,
            SnapshotSequence: paused.Sequence)));
        await seekInvalidated;
        var seek = widget.RenderSnapshot("youtube-test", 43).EmbeddedMedia!.PendingCommand!;
        Assert.AreEqual(EmbeddedMediaPlaybackCommandKind.Seek, seek.Kind);
        Assert.AreEqual(60d, seek.PositionSeconds);
        await ObserveAsync(widget, seek, EmbeddedMediaPlaybackState.Paused, 4,
            position: 60, duration: 120, volume: 0.65);

        var settled = widget.RenderSnapshot("youtube-test", 44);
        var playInvalidated = NextInvalidation(widget);
        Assert.IsTrue(await widget.OnControllerInputAsync(new ControllerInputEvent(
            ControllerButton.X,
            ControllerEventPhase.Pressed,
            ControllerInputContext.DashboardQuickAction,
            Sequence: 103,
            SnapshotSequence: settled.Sequence)));
        await playInvalidated;
        var play = widget.RenderSnapshot("youtube-test", 45).EmbeddedMedia!.PendingCommand!;
        Assert.AreEqual(EmbeddedMediaPlaybackCommandKind.Play, play.Kind);
        await ObserveAsync(widget, play, EmbeddedMediaPlaybackState.Playing, 5,
            position: 60, duration: 120, volume: 0.65);

        var playing = widget.RenderSnapshot("youtube-test", 46);
        var backInvalidated = NextInvalidation(widget);
        Assert.IsTrue(await widget.OnControllerInputAsync(new ControllerInputEvent(
            ControllerButton.LeftTrigger,
            ControllerEventPhase.Pressed,
            ControllerInputContext.OpenWidget,
            FocusedElementId: "youtube.playback.toggle",
            Sequence: 104,
            ActiveInputScopeId: playing.ActiveInputScopeId,
            SnapshotSequence: playing.Sequence)));
        await backInvalidated;
        var back = widget.RenderSnapshot("youtube-test", 47).EmbeddedMedia!.PendingCommand!;
        Assert.AreEqual(EmbeddedMediaPlaybackCommandKind.Seek, back.Kind);
        Assert.AreEqual(50d, back.PositionSeconds);
        await ObserveAsync(widget, back, EmbeddedMediaPlaybackState.Playing, 6,
            position: 50, duration: 120, volume: 0.65);

        var afterBack = widget.RenderSnapshot("youtube-test", 48);
        var forwardInvalidated = NextInvalidation(widget);
        Assert.IsTrue(await widget.OnControllerInputAsync(new ControllerInputEvent(
            ControllerButton.RightTrigger,
            ControllerEventPhase.Pressed,
            ControllerInputContext.DashboardQuickAction,
            Sequence: 105,
            SnapshotSequence: afterBack.Sequence)));
        await forwardInvalidated;
        var forward = widget.RenderSnapshot("youtube-test", 49).EmbeddedMedia!.PendingCommand!;
        Assert.AreEqual(EmbeddedMediaPlaybackCommandKind.Seek, forward.Kind);
        Assert.AreEqual(60d, forward.PositionSeconds);
        await WidgetTestHost.DestroyAsync(widget);
    }

    [TestMethod]
    public async Task ProviderErrorsRemainFixedClearAndNonSecret()
    {
        var expected = new Dictionary<string, string>
        {
            ["invalid-video"] = "rejected this video ID",
            ["video-private-or-missing"] = "private, unavailable",
            ["embedding-disabled"] = "does not allow embedded playback",
            ["client-identity-rejected"] = "verify this desktop client",
        };
        foreach (var pair in expected)
        {
            var widget = await CreateConfiguredLinkWidgetAsync();
            await CommitAsync(widget, $"https://youtu.be/{VideoId}");
            var command = widget.RenderSnapshot("youtube-test", 1).EmbeddedMedia!.PendingCommand!;
            await ObserveAsync(widget, command, EmbeddedMediaPlaybackState.Error, 1,
                errorCode: pair.Key);
            var text = Find(widget.RenderSnapshot("youtube-test", 2).Root, "youtube.status").Text!;
            StringAssert.Contains(text, pair.Value);
            Assert.DoesNotContain(VideoId, text);
        }
    }

    [TestMethod]
    public async Task RetainedControllerReactivationPreservesPlaybackAndAcceptsNextCommand()
    {
        var widget = WidgetTestHost.Attach(
            new YouTubeVideoWidget(), new WidgetTestHostServicesBuilder().Build());
        await WidgetTestHost.InitializeAsync(widget);
        await WidgetTestHost.SetLifecycleStateAsync(widget, WidgetLifecycleState.Visible);
        await CommitAsync(widget, $"https://youtu.be/{VideoId}");
        var load = widget.RenderSnapshot("youtube-test", 1).EmbeddedMedia!.PendingCommand!;
        await ObserveAsync(widget, load, EmbeddedMediaPlaybackState.Ready, 1);
        await widget.OnActionAsync(new WidgetActionEvent(
            YouTubeVideoWidget.ToggleActionId, "youtube.playback.toggle"));
        var play = widget.RenderSnapshot("youtube-test", 2).EmbeddedMedia!.PendingCommand!;
        await ObserveAsync(widget, play, EmbeddedMediaPlaybackState.Playing, 2,
            position: 37, duration: 120, volume: 0.55);

        await WidgetTestHost.SetLifecycleStateAsync(widget, WidgetLifecycleState.Background);
        var hidden = widget.RenderSnapshot("youtube-test", 3);
        Assert.IsNull(hidden.EmbeddedMedia!.PendingCommand);
        Assert.AreEqual(37d, Find(hidden.Root, "youtube.timeline").Value);
        Assert.AreEqual("Playing", Find(hidden.Root, "youtube.status").Text);

        await WidgetTestHost.SetLifecycleStateAsync(widget, WidgetLifecycleState.Visible);
        var retained = widget.RenderSnapshot("youtube-test", 5);
        Assert.IsNull(retained.EmbeddedMedia!.PendingCommand);
        Assert.AreEqual(37d, Find(retained.Root, "youtube.timeline").Value);
        Assert.AreEqual("Playing", Find(retained.Root, "youtube.status").Text);

        await widget.OnActionAsync(new WidgetActionEvent(
            YouTubeVideoWidget.VolumeActionId, "youtube.volume") { RequestedValue = 0.6 });
        var volume = widget.RenderSnapshot("youtube-test", 6).EmbeddedMedia!.PendingCommand!;
        Assert.AreEqual(EmbeddedMediaPlaybackCommandKind.SetVolume, volume.Kind);
        await WidgetTestHost.SetLifecycleStateAsync(widget, WidgetLifecycleState.Background);
        Assert.AreEqual(volume,
            widget.RenderSnapshot("youtube-test", 7).EmbeddedMedia!.PendingCommand);
        await WidgetTestHost.SetLifecycleStateAsync(widget, WidgetLifecycleState.Visible);
        Assert.AreEqual(volume,
            widget.RenderSnapshot("youtube-test", 8).EmbeddedMedia!.PendingCommand);
        await ObserveAsync(widget, volume, EmbeddedMediaPlaybackState.Playing, 3,
            position: 38, duration: 120, volume: 0.6);
        Assert.IsNull(widget.RenderSnapshot("youtube-test", 9).EmbeddedMedia!.PendingCommand);
        await WidgetTestHost.DestroyAsync(widget);
    }

    [TestMethod]
    public void SealedAdapterUsesOfficialApiAndFixedErrorVocabulary()
    {
        var mediaDirectory = Path.Combine(AppContext.BaseDirectory, "media");
        var adapter = File.ReadAllText(Path.Combine(mediaDirectory, "adapter.html"));
        var runtime = File.ReadAllText(
            Path.Combine(mediaDirectory, "adapter-runtime.js"));
        StringAssert.Contains(adapter, "<script src=\"adapter-runtime.js\"></script>");
        StringAssert.Contains(adapter, "https://www.youtube.com/iframe_api");
        StringAssert.Contains(adapter, "new YT.Player");
        StringAssert.Contains(adapter, "autoplay: 0");
        StringAssert.Contains(adapter, "controls: 0");
        StringAssert.Contains(adapter, "fs: 0");
        StringAssert.Contains(adapter, "WidgetRailEmbeddedMediaAdapter.create({");
        Assert.AreEqual(0, CountOccurrences(adapter, "chrome.webview.postMessage("),
            "The provider driver must not publish host envelopes directly.");
        Assert.AreEqual(1, CountOccurrences(runtime, "global.chrome.webview.postMessage("),
            "The shared runtime must remain the sole host-envelope writer.");
        StringAssert.Contains(runtime, "if (mediaKey.length > 0)");
        StringAssert.Contains(runtime, "emit(\"ready\", initial)");
        StringAssert.Contains(runtime, "\"arm-activate\": \"activate\"");
        StringAssert.Contains(adapter, "async load(command)");
        StringAssert.Contains(adapter, "async cue(command)");
        StringAssert.Contains(adapter, "async toggle(command)");
        StringAssert.Contains(adapter, "onPlaybackRateChange");
        StringAssert.Contains(adapter, "getAvailablePlaybackRates()");
        StringAssert.Contains(adapter, "waitForPlaybackRate(command)");
        StringAssert.Contains(adapter, "waitForMuted(command)");
        StringAssert.Contains(adapter, "player-operation-timeout");
        StringAssert.Contains(adapter, "client-identity-rejected");
        StringAssert.Contains(adapter, "embedding-disabled");
        Assert.DoesNotContain("fetch(", adapter);
        Assert.DoesNotContain("XMLHttpRequest", adapter);
        Assert.DoesNotContain("apiKey", adapter, StringComparison.OrdinalIgnoreCase);
    }

    [TestMethod]
    public async Task DeclaredAdapterCapabilitiesCloseCorrelatedOperations()
    {
        var widget = await CreateConfiguredLinkWidgetAsync();
        await CommitAsync(widget, $"https://youtu.be/{VideoId}");
        var snapshot = widget.RenderSnapshot("youtube-test", 1);
        var adapterPath = Path.Combine(AppContext.BaseDirectory, "media", "adapter.html");
        await EmbeddedMediaAdapterConformanceGate.VerifyAsync(
            snapshot,
            adapterPath,
            EmbeddedMediaFakePlayerProfile.StateCallbackPlayer,
            [
                EmbeddedMediaPlaybackCommandKind.Load,
                EmbeddedMediaPlaybackCommandKind.Cue,
                EmbeddedMediaPlaybackCommandKind.Play,
                EmbeddedMediaPlaybackCommandKind.Pause,
                EmbeddedMediaPlaybackCommandKind.Seek,
                EmbeddedMediaPlaybackCommandKind.SetVolume,
                EmbeddedMediaPlaybackCommandKind.SetPlaybackRate,
                EmbeddedMediaPlaybackCommandKind.SetMuted,
                EmbeddedMediaPlaybackCommandKind.SetLoop,
            ]);

        var missingToggleRoot = Path.Combine(
            Path.GetTempPath(), $"wrail-youtube-adapter-{Guid.NewGuid():N}");
        var missingTogglePath = Path.Combine(missingToggleRoot, "adapter.html");
        try
        {
            Directory.CreateDirectory(missingToggleRoot);
            var adapter = await File.ReadAllTextAsync(adapterPath);
            const string toggleHandler = "async toggle(command)";
            Assert.IsTrue(adapter.Contains(toggleHandler, StringComparison.Ordinal));
            await File.WriteAllTextAsync(
                missingTogglePath,
                adapter.Replace(toggleHandler, "async removedToggle(command)",
                    StringComparison.Ordinal));
            File.Copy(
                Path.Combine(AppContext.BaseDirectory, "media", "adapter-runtime.js"),
                Path.Combine(missingToggleRoot, "adapter-runtime.js"));
            var failure = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
                EmbeddedMediaAdapterConformanceGate.VerifyAsync(
                    snapshot,
                    missingTogglePath,
                    EmbeddedMediaFakePlayerProfile.StateCallbackPlayer,
                    [
                        EmbeddedMediaPlaybackCommandKind.Load,
                        EmbeddedMediaPlaybackCommandKind.Cue,
                        EmbeddedMediaPlaybackCommandKind.Play,
                        EmbeddedMediaPlaybackCommandKind.Pause,
                        EmbeddedMediaPlaybackCommandKind.Seek,
                        EmbeddedMediaPlaybackCommandKind.SetVolume,
                        EmbeddedMediaPlaybackCommandKind.SetPlaybackRate,
                        EmbeddedMediaPlaybackCommandKind.SetMuted,
                        EmbeddedMediaPlaybackCommandKind.SetLoop,
                    ]));
            StringAssert.Contains(failure.Message,
                "command-unsupported: declared:togglePlayback requires adapter message 'toggle'");
        }
        finally
        {
            if (Directory.Exists(missingToggleRoot))
            {
                Directory.Delete(missingToggleRoot, recursive: true);
            }
        }
        await WidgetTestHost.DestroyAsync(widget);
    }

    [TestMethod]
    public void ManifestIsCredentialFreeSuspendedAndImmutableVersioned()
    {
        using var document = JsonDocument.Parse(
            File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "manifest.json")));
        var root = document.RootElement;
        Assert.AreEqual("widgetrail.samples.youtube-video", root.GetProperty("id").GetString());
        // Installed versions are immutable, so every package change bumps this.
        // Pin the shape, not the number: a literal here only ever records the
        // last bump someone remembered to mirror.
        var version = root.GetProperty("version").GetString();
        Assert.IsNotNull(version);
        StringAssert.Matches(version, VersionPattern(),
            "The manifest version must be a real three-part version.");
        Assert.IsTrue(root.GetProperty("pinningSupported").GetBoolean());
        Assert.AreEqual("full-trust-application-v1",
            root.GetProperty("entrypoint").GetProperty("runtime").GetString());
        Assert.AreEqual(0, root.GetProperty("permissions").GetArrayLength());
        Assert.AreEqual(0, root.GetProperty("optionalPermissions").GetArrayLength());
        Assert.AreEqual("keep-alive",
            root.GetProperty("residencyPolicy").GetProperty("mode").GetString());
    }

    private static Task NextInvalidation(Widget widget)
    {
        var completion = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        EventHandler<WidgetInvalidatedEventArgs>? handler = null;
        handler = (_, _) =>
        {
            widget.Invalidated -= handler;
            completion.TrySetResult();
        };
        widget.Invalidated += handler;
        return completion.Task.WaitAsync(TimeSpan.FromSeconds(2));
    }

    private sealed class FakeApplicationService : IYouTubeApplicationService
    {
        public ValueTask<YouTubeConfigurationSummary> GetConfigurationAsync(
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(new YouTubeConfigurationSummary(true));
        }

        public ValueTask ConfigureApiKeyAsync(
            string apiKey, CancellationToken cancellationToken) =>
            ValueTask.CompletedTask;

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

    private static async Task CommitAsync(YouTubeVideoWidget widget, string link) =>
        await widget.OnActionAsync(new WidgetActionEvent(
            YouTubeVideoWidget.LinkActionId, "youtube.link") { CommittedText = link });

    private static async Task<YouTubeVideoWidget> CreateConfiguredLinkWidgetAsync()
    {
        var widget = await CreateConfiguredSearchWidgetAsync();
        await widget.OnActionAsync(new WidgetActionEvent(
            "youtube.link.open", "youtube.link.open"));
        return widget;
    }

    private static async Task<YouTubeVideoWidget> CreateConfiguredSearchWidgetAsync()
    {
        var widget = WidgetTestHost.Attach(
            new YouTubeVideoWidget(new FakeApplicationService()),
            new WidgetTestHostServicesBuilder().Build());
        var configured = NextInvalidation(widget);
        await WidgetTestHost.InitializeAsync(widget);
        await WidgetTestHost.SetLifecycleStateAsync(widget, WidgetLifecycleState.Visible);
        await configured;
        return widget;
    }

    private static int CountOccurrences(string value, string token)
    {
        var count = 0;
        for (var index = 0; (index = value.IndexOf(token, index, StringComparison.Ordinal)) >= 0;
             index += token.Length)
            count++;
        return count;
    }

    private static async Task ObserveAsync(
        YouTubeVideoWidget widget,
        EmbeddedMediaPlaybackCommand command,
        EmbeddedMediaPlaybackState state,
        long eventSequence,
        double position = 0,
        double duration = 0,
        double volume = 0.8,
        double playbackRate = 1,
        bool muted = false,
        bool loop = false,
        string? errorCode = null) =>
        await widget.OnEmbeddedMediaPlaybackEventAsync(new EmbeddedMediaPlaybackEvent
        {
            SurfaceId = YouTubeVideoWidget.SurfaceId,
            Sequence = eventSequence,
            CommandSequence = command.Sequence,
            MediaKey = command.MediaKey,
            State = state,
            PositionSeconds = position,
            DurationSeconds = duration,
            Volume = volume,
            PlaybackRate = playbackRate,
            Muted = muted,
            Loop = loop,
            ErrorCode = errorCode,
        });

    private static async Task<(YouTubeVideoWidget Widget, ViewSnapshot Snapshot, double Position)>
        CreateTransportWidgetAsync(
            EmbeddedMediaPlaybackState playbackState,
            bool seekBuffering)
    {
        var widget = await CreateConfiguredLinkWidgetAsync();
        await CommitAsync(widget, $"https://youtu.be/{VideoId}");
        var load = widget.RenderSnapshot("youtube-test", 1).EmbeddedMedia!.PendingCommand!;
        await ObserveAsync(widget, load, EmbeddedMediaPlaybackState.Ready, 1,
            position: 50, duration: 120, volume: 0.65);
        await widget.OnEmbeddedMediaPlaybackEventAsync(new EmbeddedMediaPlaybackEvent
        {
            SurfaceId = YouTubeVideoWidget.SurfaceId,
            Sequence = 2,
            CommandSequence = 0,
            MediaKey = VideoId,
            State = playbackState,
            PositionSeconds = 50,
            DurationSeconds = 120,
            Volume = 0.65,
        });
        var position = 50d;
        if (seekBuffering)
        {
            await widget.OnActionAsync(new WidgetActionEvent(
                YouTubeVideoWidget.SeekForwardActionId, "youtube.root"));
            var seek = widget.RenderSnapshot("youtube-test", 2).EmbeddedMedia!.PendingCommand!;
            await ObserveAsync(widget, seek, EmbeddedMediaPlaybackState.Loading, 3,
                position: 60, duration: 120, volume: 0.65);
            position = 60;
        }
        return (widget, widget.RenderSnapshot("youtube-test", 20), position);
    }

    private static async Task AssertTransportInputsRejectedAsync(
        YouTubeVideoWidget widget,
        ViewSnapshot snapshot,
        string state,
        bool expectDeclarationsAbsent = true)
    {
        if (expectDeclarationsAbsent)
            Assert.IsEmpty(snapshot.QuickActions, $"{state} exposed dashboard QuickActions.");
        foreach (var button in new[]
                 {
                     ControllerButton.X,
                     ControllerButton.LeftTrigger,
                     ControllerButton.RightTrigger,
                 })
        {
            Assert.IsFalse(await widget.OnControllerInputAsync(new ControllerInputEvent(
                    button,
                    ControllerEventPhase.Pressed,
                    ControllerInputContext.DashboardQuickAction,
                    Sequence: 92,
                    SnapshotSequence: snapshot.Sequence)),
                $"{state} admitted dashboard {button}.");
            Assert.IsFalse(await widget.OnControllerInputAsync(new ControllerInputEvent(
                    button,
                    ControllerEventPhase.Pressed,
                    ControllerInputContext.OpenWidget,
                    Sequence: 93,
                    ActiveInputScopeId: snapshot.ActiveInputScopeId,
                    SnapshotSequence: snapshot.Sequence)),
                $"{state} admitted page-scoped {button}.");
        }
    }

    /// <summary>
    /// A held button repeats only while the host can still resolve the exact
    /// binding it captured on the press. Any snapshot that drops the LT/RT
    /// declaration, or disables the node that declares it, retires the hold --
    /// so the transport declaration must survive a pending command and the
    /// buffering the seek itself causes.
    /// </summary>
    [TestMethod]
    public async Task HeldSeekBindingsSurviveTheirOwnPendingCommandAndBuffering()
    {
        var widget = await CreateConfiguredLinkWidgetAsync();
        await CommitAsync(widget, $"https://youtu.be/{VideoId}");
        var load = widget.RenderSnapshot("youtube-test", 1).EmbeddedMedia!.PendingCommand!;
        await ObserveAsync(widget, load, EmbeddedMediaPlaybackState.Playing, 1,
            position: 50, duration: 600, volume: 0.65);

        AssertHeldSeekDeclared(widget.RenderSnapshot("youtube-test", 2), "at rest");

        // First repeat: a seek is queued and still in flight.
        await widget.OnActionAsync(new WidgetActionEvent(
            YouTubeVideoWidget.SeekForwardActionId, "youtube.root"));
        var pendingSnapshot = widget.RenderSnapshot("youtube-test", 3);
        Assert.IsNotNull(pendingSnapshot.EmbeddedMedia!.PendingCommand,
            "The seek must actually be in flight for this to prove anything.");
        AssertHeldSeekDeclared(pendingSnapshot, "with a command pending");

        // The seek drives the player into Loading. This is the state that used
        // to withdraw the declaration and end the hold after one step.
        var seek = pendingSnapshot.EmbeddedMedia!.PendingCommand!;
        await ObserveAsync(widget, seek, EmbeddedMediaPlaybackState.Loading, 2,
            position: 60, duration: 600, volume: 0.65);
        AssertHeldSeekDeclared(widget.RenderSnapshot("youtube-test", 4), "while buffering");

        // Second repeat while already Loading: the widget records no buffering
        // sentinel on this path, which is exactly where the hold used to die.
        await widget.OnActionAsync(new WidgetActionEvent(
            YouTubeVideoWidget.SeekForwardActionId, "youtube.root"));
        var second = widget.RenderSnapshot("youtube-test", 5);
        AssertHeldSeekDeclared(second, "on a second seek while still loading");
        if (second.EmbeddedMedia!.PendingCommand is { } queued)
            await ObserveAsync(widget, queued, EmbeddedMediaPlaybackState.Loading, 3,
                position: 70, duration: 600, volume: 0.65);
        AssertHeldSeekDeclared(
            widget.RenderSnapshot("youtube-test", 6), "after repeated seeking");
    }

    /// <summary>
    /// The seek binding is declared on the scope root, not on the focused
    /// control. A transiently disabled or busy sibling must not be able to
    /// speak for it.
    /// </summary>
    [TestMethod]
    public async Task HeldSeekOwnerStaysActionableWhileSiblingControlsSettle()
    {
        var widget = await CreateConfiguredLinkWidgetAsync();
        await CommitAsync(widget, $"https://youtu.be/{VideoId}");
        var load = widget.RenderSnapshot("youtube-test", 1).EmbeddedMedia!.PendingCommand!;
        await ObserveAsync(widget, load, EmbeddedMediaPlaybackState.Playing, 1,
            position: 50, duration: 600, volume: 0.65);
        await widget.OnActionAsync(new WidgetActionEvent(
            YouTubeVideoWidget.SeekForwardActionId, "youtube.root"));
        var seek = widget.RenderSnapshot("youtube-test", 2).EmbeddedMedia!.PendingCommand!;
        await ObserveAsync(widget, seek, EmbeddedMediaPlaybackState.Loading, 2,
            position: 60, duration: 600, volume: 0.65);

        var snapshot = widget.RenderSnapshot("youtube-test", 3);
        var owner = Find(snapshot.Root, snapshot.ActiveInputScopeId);
        Assert.IsTrue(
            owner.Shortcuts.Any(shortcut =>
                shortcut.Button == ControllerButton.RightTrigger &&
                shortcut.RepeatPolicy == ControllerActionRepeatPolicy.WhileHeld),
            "The scope root must own the held seek binding.");
        Assert.IsFalse(owner.IsDisabled is true, "The binding owner must stay actionable.");
        Assert.IsFalse(owner.IsBusy is true, "The binding owner must stay actionable.");

        Assert.IsNull(TryFind(snapshot.Root, "youtube.playback.seek-backward"));
        Assert.IsNull(TryFind(snapshot.Root, "youtube.playback.seek-forward"));
        Assert.IsFalse(Find(snapshot.Root, "youtube.timeline").IsDisabled is true,
            "The visible timeline must stay enabled while a relative seek settles.");
    }

    private static void AssertHeldSeekDeclared(ViewSnapshot snapshot, string stage)
    {
        var root = Find(snapshot.Root, "youtube.root");
        foreach (var button in new[]
                 { ControllerButton.LeftTrigger, ControllerButton.RightTrigger })
        {
            Assert.IsTrue(
                root.Shortcuts.Any(shortcut =>
                    shortcut.Button == button &&
                    shortcut.Phase == ControllerEventPhase.Pressed &&
                    shortcut.RepeatPolicy == ControllerActionRepeatPolicy.WhileHeld),
                $"{button} held shortcut was withdrawn {stage}.");
            Assert.IsTrue(
                (snapshot.QuickActions ?? []).Any(action =>
                    action.Button == button &&
                    action.RepeatPolicy == ControllerActionRepeatPolicy.WhileHeld),
                $"{button} held quick action was withdrawn {stage}.");
        }
    }

    [System.Text.RegularExpressions.GeneratedRegex(@"^\d+\.\d+\.\d+$")]
    private static partial System.Text.RegularExpressions.Regex VersionPattern();

    private static ViewNode Find(ViewNode node, string id)
    {
        if (string.Equals(node.Id, id, StringComparison.Ordinal)) return node;
        foreach (var child in node.Children)
        {
            var found = TryFind(child, id);
            if (found is not null) return found;
        }
        Assert.Fail($"Missing node '{id}'.");
        throw new InvalidOperationException();
    }

    private static ViewNode? TryFind(ViewNode node, string id)
    {
        if (string.Equals(node.Id, id, StringComparison.Ordinal)) return node;
        foreach (var child in node.Children)
        {
            var found = TryFind(child, id);
            if (found is not null) return found;
        }
        return null;
    }
}
