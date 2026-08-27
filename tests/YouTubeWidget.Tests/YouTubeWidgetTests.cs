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
    public void SnapshotUsesV26BoundedYouTubeAuthorityAndAccessibleNativeControls()
    {
        var snapshot = new YouTubeVideoWidget().RenderSnapshot("youtube-test", 1);
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
    public async Task CommittedLinkCuesWithoutAutoplayThenPlayUsesTypedCommand()
    {
        var widget = new YouTubeVideoWidget();
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
        var widget = new YouTubeVideoWidget();
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
            var widget = new YouTubeVideoWidget();
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
    public async Task DeactivationRetiresPendingWorkAndReactivationOnlyCues()
    {
        var widget = WidgetTestHost.Attach(
            new YouTubeVideoWidget(), new WidgetTestHostServicesBuilder().Build());
        await WidgetTestHost.InitializeAsync(widget);
        await WidgetTestHost.SetLifecycleStateAsync(widget, WidgetLifecycleState.Visible);
        await CommitAsync(widget, $"https://youtu.be/{VideoId}");
        Assert.AreEqual(EmbeddedMediaPlaybackCommandKind.Load,
            widget.RenderSnapshot("youtube-test", 1).EmbeddedMedia!.PendingCommand!.Kind);

        await WidgetTestHost.SetLifecycleStateAsync(widget, WidgetLifecycleState.Background);
        Assert.IsNull(widget.RenderSnapshot("youtube-test", 2).EmbeddedMedia!.PendingCommand);
        await WidgetTestHost.SetLifecycleStateAsync(widget, WidgetLifecycleState.Visible);
        Assert.AreEqual(EmbeddedMediaPlaybackCommandKind.Cue,
            widget.RenderSnapshot("youtube-test", 3).EmbeddedMedia!.PendingCommand!.Kind);
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
        Assert.AreEqual("0.1.2", root.GetProperty("version").GetString());
        Assert.AreEqual(0, root.GetProperty("permissions").GetArrayLength());
        Assert.AreEqual(0, root.GetProperty("optionalPermissions").GetArrayLength());
        Assert.AreEqual("suspend-when-hidden",
            root.GetProperty("residencyPolicy").GetProperty("mode").GetString());
    }

    private static async Task CommitAsync(YouTubeVideoWidget widget, string link) =>
        await widget.OnActionAsync(new WidgetActionEvent(
            YouTubeVideoWidget.LinkActionId, "youtube.link") { CommittedText = link });

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
