using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WidgetRail.Samples.YouTubeWidget;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

namespace WidgetRail.YouTubeWidget.Tests;

[TestClass]
public sealed class YouTubeWidgetTests
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
                YouTubeVideoWidget.SeekBackwardActionId, "Seek backward 10 seconds"),
            new WidgetQuickAction(ControllerButton.RightTrigger,
                YouTubeVideoWidget.SeekForwardActionId, "Seek forward 10 seconds"),
        };
        var expectedShortcuts = new[]
        {
            new ControllerShortcut(ControllerButton.X, YouTubeVideoWidget.ToggleActionId),
            new ControllerShortcut(ControllerButton.LeftTrigger,
                YouTubeVideoWidget.SeekBackwardActionId),
            new ControllerShortcut(ControllerButton.RightTrigger,
                YouTubeVideoWidget.SeekForwardActionId),
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

        Assert.AreEqual("PLAYER", Find(ready.Root, "youtube.media.label").Text);
        Assert.AreEqual("16:9 embedded playback",
            Find(ready.Root, "youtube.media.hint").Text);
        Assert.IsNotNull(Find(ready.Root, "youtube.status").Text);

        var toggle = Find(ready.Root, "youtube.playback.toggle");
        Assert.AreEqual(WidgetGlyph.Play, toggle.Glyph);
        Assert.AreEqual(YouTubeVideoWidget.ToggleActionId, toggle.ActionId);
        Assert.AreEqual("Play", toggle.AccessibilityLabel);
        var rewind = Find(ready.Root, "youtube.playback.seek-backward");
        Assert.AreEqual(WidgetGlyph.Rewind, rewind.Glyph);
        Assert.AreEqual(YouTubeVideoWidget.SeekBackwardActionId, rewind.ActionId);
        Assert.AreEqual("Seek backward 10 seconds", rewind.AccessibilityLabel);
        var fastForward = Find(ready.Root, "youtube.playback.seek-forward");
        Assert.AreEqual(WidgetGlyph.FastForward, fastForward.Glyph);
        Assert.AreEqual(YouTubeVideoWidget.SeekForwardActionId, fastForward.ActionId);
        Assert.AreEqual("Seek forward 10 seconds", fastForward.AccessibilityLabel);

        CollectionAssert.AreEqual(
            new[]
            {
                "youtube.playback.toggle",
                "youtube.playback.seek-backward",
                "youtube.playback.seek-forward",
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
        Assert.AreEqual(ProtocolConstants.RetainedHiddenEmbeddedMediaVersion,
            hidden.ProtocolVersion);
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
        var adapter = File.ReadAllText(
            Path.Combine(AppContext.BaseDirectory, "media", "adapter.html"));
        StringAssert.Contains(adapter, "https://www.youtube.com/iframe_api");
        StringAssert.Contains(adapter, "new YT.Player");
        StringAssert.Contains(adapter, "autoplay:0");
        StringAssert.Contains(adapter, "controls:1");
        StringAssert.Contains(adapter, "fs:0");
        StringAssert.Contains(adapter,
            "commandId:message.commandId,commandSequence:0,playing:false,focus:'youtube-player'");
        StringAssert.Contains(adapter,
            "commandId,commandSequence,focus:'youtube-player',playing:state==='playing',bounds:bounds(),mediaKey:key,playbackState:state,positionSeconds:position(),durationSeconds:duration(),volume:volume()");
        Assert.AreEqual(2, CountOccurrences(adapter, "chrome.webview.postMessage("),
            "Only initialization and the shared closed event envelope may write page events.");
        StringAssert.Contains(adapter,
            "emit('media',operation.id,operation.sequence,errorCode,playbackState,operation.mediaKey)");
        StringAssert.Contains(adapter, "return}emit('media')");
        StringAssert.Contains(adapter, "emit('media',0,0,code,'error')");
        StringAssert.Contains(adapter, "lastProgressSecond=second;emit('media')");
        StringAssert.Contains(adapter,
            "emit('armed',operation.id,operation.sequence,undefined,playbackState,operation.mediaKey)");
        StringAssert.Contains(adapter,
            "if((operation.kind==='load'||operation.kind==='cue')&&event.data===5)complete(operation,undefined,'ready')");
        StringAssert.Contains(adapter,
            "if(message.command==='load'||message.command==='cue'){mediaKey=operation.mediaKey;playbackState='loading';player.cueVideoById");
        StringAssert.Contains(adapter, "client-identity-rejected");
        StringAssert.Contains(adapter, "embedding-disabled");
        Assert.DoesNotContain("fetch(", adapter);
        Assert.DoesNotContain("XMLHttpRequest", adapter);
        Assert.DoesNotContain("apiKey", adapter, StringComparison.OrdinalIgnoreCase);
    }

    [TestMethod]
    public void ManifestIsCredentialFreeSuspendedAndImmutableVersioned()
    {
        using var document = JsonDocument.Parse(
            File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "manifest.json")));
        var root = document.RootElement;
        Assert.AreEqual("widgetrail.samples.youtube-video", root.GetProperty("id").GetString());
        Assert.AreEqual("0.2.11", root.GetProperty("version").GetString());
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
            ErrorCode = errorCode,
        });

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
