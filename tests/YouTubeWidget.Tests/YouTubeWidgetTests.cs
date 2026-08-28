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
        Assert.AreEqual(ProtocolConstants.EmbeddedMediaFrameDomainFamiliesVersion,
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
        Assert.AreEqual("youtube.link", snapshot.InitialFocusId);
        Assert.AreEqual("youtube.root", snapshot.ActiveInputScopeId);
        Assert.AreEqual(ViewNodeKind.MediaViewport,
            Find(snapshot.Root, "youtube.viewport").Kind);
        Assert.AreEqual(YouTubeVideoWidget.ToggleActionId,
            Find(snapshot.Root, "youtube.playback.toggle").ActionId);
        Assert.AreEqual(ViewNodeKind.Slider, Find(snapshot.Root, "youtube.timeline").Kind);
        Assert.AreEqual(ViewNodeKind.Slider, Find(snapshot.Root, "youtube.volume").Kind);
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
        Assert.AreEqual("0.2.2", root.GetProperty("version").GetString());
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
        var widget = WidgetTestHost.Attach(
            new YouTubeVideoWidget(new FakeApplicationService()),
            new WidgetTestHostServicesBuilder().Build());
        var configured = NextInvalidation(widget);
        await WidgetTestHost.InitializeAsync(widget);
        await WidgetTestHost.SetLifecycleStateAsync(widget, WidgetLifecycleState.Visible);
        await configured;
        await widget.OnActionAsync(new WidgetActionEvent(
            "youtube.link.open", "youtube.link.open"));
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
