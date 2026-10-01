using Microsoft.VisualStudio.TestTools.UnitTesting;
using WidgetRail.Samples.YouTubeWidget;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

namespace WidgetRail.YouTubeWidget.Tests;

public sealed partial class YouTubeWidgetTests
{
    [TestMethod]
    public async Task UserVolumeSurvivesRestartAndIsIncludedInFirstLoad()
    {
        var root = Path.Combine(Path.GetTempPath(), "widgetrail-youtube-volume-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, "volume.txt");
        try
        {
            await File.WriteAllTextAsync(path, "0.25");
            var widget = await CreateVolumeWidgetAsync(path);
            try
            {
                await CommitAsync(widget, $"https://youtu.be/{VideoId}");
                var load = widget.RenderSnapshot("volume-test", 1).EmbeddedMediaSession!.PendingCommand!;
                Assert.AreEqual(.25, load.Volume);
                await ObserveAsync(widget, load, EmbeddedMediaPlaybackState.Playing, 1, volume: 1);
                Assert.AreEqual("0.25", await File.ReadAllTextAsync(path), "Provider startup reports cannot persist a different volume.");
                await widget.OnActionAsync(new WidgetActionEvent(YouTubeVideoWidget.VolumeActionId, "youtube.volume") { RequestedValue = .6 });
                var change = widget.RenderSnapshot("volume-test", 2).EmbeddedMediaSession!.PendingCommand!;
                Assert.AreEqual(.6, change.Volume);
                await ObserveAsync(widget, change, EmbeddedMediaPlaybackState.Playing, 2, volume: .6);
                // A later unrelated provider report must not overwrite the preference.
                await ObserveAsync(widget, change, EmbeddedMediaPlaybackState.Playing, 3, volume: 1);
            }
            finally { await WidgetTestHost.DestroyAsync(widget); }
            var restarted = await CreateVolumeWidgetAsync(path);
            try
            {
                await CommitAsync(restarted, $"https://youtu.be/{VideoId}");
                Assert.AreEqual(.6, restarted.RenderSnapshot("volume-test", 1).EmbeddedMediaSession!.PendingCommand!.Volume);
            }
            finally { await WidgetTestHost.DestroyAsync(restarted); }
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private static async Task<YouTubeVideoWidget> CreateVolumeWidgetAsync(string path)
    {
        var widget = WidgetTestHost.Attach(new YouTubeVideoWidget(new FakeApplicationService(), path),
            new WidgetTestHostServicesBuilder().Build());
        var configured = NextInvalidation(widget);
        await WidgetTestHost.InitializeAsync(widget);
        await WidgetTestHost.SetLifecycleStateAsync(widget, WidgetLifecycleState.Visible);
        await configured;
        await widget.OnActionAsync(new WidgetActionEvent("youtube.link.open", "youtube.link.open"));
        return widget;
    }
}
