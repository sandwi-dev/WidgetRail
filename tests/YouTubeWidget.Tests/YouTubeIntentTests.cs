using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WidgetRail.Samples.YouTubeWidget;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

namespace WidgetRail.YouTubeWidget.Tests;

public sealed partial class YouTubeWidgetTests
{
    [TestMethod]
    public async Task VideoIntentLoadsValidatedVideoAtRequestedTime()
    {
        var widget = await CreateConfiguredLinkWidgetAsync();
        try
        {
            var request = WidgetIntentRequest.Create(WidgetIntentContracts.Video,
                JsonSerializer.SerializeToElement(new { provider = "youtube", videoId = VideoId, startTimeSeconds = 42.5 }));
            Assert.AreEqual(WidgetIntentResult.Accepted, await widget.OnIntentAsync(request));
            var snapshot = widget.RenderSnapshot("youtube.intent", 1);
            var command = snapshot.EmbeddedMediaSession!.PendingCommand!;
            Assert.AreEqual(EmbeddedMediaPlaybackCommandKind.Load, command.Kind);
            Assert.AreEqual(VideoId, command.MediaKey);
            Assert.AreEqual(42.5, command.PositionSeconds);
            Assert.AreEqual(0, ViewSnapshotValidator.Validate(snapshot).Count);
            var rejected = request with { Payload = JsonSerializer.SerializeToElement(new { provider = "other", videoId = VideoId }) };
            Assert.AreEqual(WidgetIntentResult.Rejected, await widget.OnIntentAsync(rejected));
            rejected = request with { Payload = JsonSerializer.SerializeToElement(new { provider = "youtube", videoId = "invalid" }) };
            Assert.AreEqual(WidgetIntentResult.Rejected, await widget.OnIntentAsync(rejected));
            rejected = request with { Payload = JsonSerializer.SerializeToElement(new { provider = "youtube", videoId = VideoId, startTimeSeconds = -1 }) };
            Assert.AreEqual(WidgetIntentResult.Rejected, await widget.OnIntentAsync(rejected));
            rejected = request with { Payload = JsonSerializer.SerializeToElement(new { provider = "youtube", videoId = VideoId, startTimeSeconds = 86401 }) };
            Assert.AreEqual(WidgetIntentResult.Rejected, await widget.OnIntentAsync(rejected));
            Assert.AreEqual(command.Sequence, widget.RenderSnapshot("youtube.intent", 2).EmbeddedMediaSession!.PendingCommand!.Sequence);
        }
        finally { await WidgetTestHost.DestroyAsync(widget); }
    }

    [TestMethod]
    public async Task VideoIntentOpensBeforeSearchConfigurationCompletes()
    {
        var gate = new TaskCompletionSource<YouTubeConfigurationSummary>(TaskCreationOptions.RunContinuationsAsynchronously);
        var widget = WidgetTestHost.Attach(new YouTubeVideoWidget(new FakeApplicationService { Configuration = gate.Task }),
            new WidgetTestHostServicesBuilder().Build());
        try
        {
            await WidgetTestHost.InitializeAsync(widget);
            await WidgetTestHost.SetLifecycleStateAsync(widget, WidgetLifecycleState.Visible);
            Assert.AreEqual(WidgetIntentResult.Accepted, await widget.OnIntentAsync(WidgetIntentRequest.Create(WidgetIntentContracts.Video,
                JsonSerializer.SerializeToElement(new { provider = "youtube", videoId = VideoId }))));
            Assert.IsNotNull(widget.RenderSnapshot("youtube.cold-intent", 1).EmbeddedMediaSession);
            var updated = NextInvalidation(widget);
            gate.TrySetResult(new(false));
            await updated.WaitAsync(TimeSpan.FromSeconds(2));
            Assert.IsNotNull(widget.RenderSnapshot("youtube.cold-intent", 2).EmbeddedMediaSession);
        }
        finally { await WidgetTestHost.DestroyAsync(widget); }
    }

    [TestMethod]
    public void LateSearchConfigurationDoesNotReplaceIntentPlayer()
    {
        var state = YouTubeWidgetState.Initial.WithPlayback(playback => playback.WithSelectedVideo(VideoId, 1, 25))
            .WithRootSection(YouTubeRootSection.Player);
        Assert.AreEqual(YouTubeRoute.Player, state.WithConfigurationSummary(false).Route);
        Assert.AreEqual(YouTubeRoute.Player, state.WithConfigurationSummary(true).Route);
        Assert.AreEqual(YouTubeRoute.Player, state.WithConfigurationFailure("Search unavailable").Route);
        Assert.AreEqual(0, state.Playback.Position);
        Assert.AreEqual(25, state.Playback.PendingCommand!.PositionSeconds);
    }

    [TestMethod]
    public void ManifestDeclaresMatchingVideoIntentHandler()
    {
        var manifest = ManifestJson.Deserialize(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "manifest.json")));
        var handler = manifest.Intents!.Handles.Single();
        Assert.AreEqual(WidgetIntentContracts.OpenVideo, handler.Id);
        Assert.IsTrue(handler.SupportsPassiveDelivery);
        Assert.AreEqual(CompiledWidgetIntentContract.Create(WidgetIntentContracts.Video).SchemaDigest,
            CompiledWidgetIntentContract.Create(handler).SchemaDigest);
        Assert.AreEqual(0, WidgetManifestValidator.Validate(manifest).Count);
    }
}
