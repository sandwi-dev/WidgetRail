using Microsoft.VisualStudio.TestTools.UnitTesting;
using WidgetRail.Samples.YouTubeWidget;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;
using WidgetRail.WidgetStyling;

namespace WidgetRail.YouTubeWidget.Tests;

public sealed partial class YouTubeWidgetTests
{
    [TestMethod]
    public async Task AcrylicBasesPreserveVideoAuthorityAndContentFillsAcrossRoutes()
    {
        var parsed = WrssParser.Parse(File.ReadAllText(Path.Combine(
            AppContext.BaseDirectory, "styles", "default.wrss")), "styles/default.wrss");
        var compiled = WrssThemeCompiler.Compile([parsed.Document]);
        Assert.IsTrue(compiled.IsValid, string.Join("; ", compiled.Diagnostics));
        var theme = compiled.Theme!;
        var widget = await CreateConfiguredSearchWidgetAsync();
        try
        {
            AssertBase("youtube.root");
            await widget.OnActionAsync(new("youtube.setup.open", "youtube.root"));
            AssertBase("youtube.application.root");
            await widget.OnActionAsync(new("youtube.setup.back", "youtube.application.root"));
            await widget.OnActionAsync(new("youtube.link.open", "youtube.link.open"));
            AssertBase("youtube.root");
            await CommitAsync(widget, $"https://youtu.be/{VideoId}");
            var load = widget.RenderSnapshot("youtube.acrylic", 1).EmbeddedMediaSession!.PendingCommand!;
            await ObserveAsync(widget, load, EmbeddedMediaPlaybackState.Paused, 1, duration: 120);
            AssertBase("youtube.root");
            await widget.OnActionAsync(new(YouTubeVideoWidget.CaptionsActionId, YouTubeVideoWidget.CaptionsActionId));
            AssertBase("youtube.player.settings");
            await widget.OnActionAsync(new(YouTubeVideoWidget.PlaybackRateActionId, YouTubeVideoWidget.PlaybackRateActionId));
            AssertBase("youtube.player.settings.rate-picker");
            await widget.OnActionAsync(new(YouTubeVideoWidget.PlaybackRateBackActionId, YouTubeVideoWidget.PlaybackRateBackActionId));
            await widget.OnActionAsync(new(YouTubeVideoWidget.CaptionsActionId, YouTubeVideoWidget.CaptionsActionId));
            AssertBase("youtube.player.captions.sheet");

            foreach (var panel in new[] { "youtube-card", "youtube-state-card", "youtube-result-row", "youtube-player-shell" })
                Assert.IsTrue(Background(panel) is not null and not "transparent",
                    $"The {panel} content fill must remain visible over acrylic.");
            Assert.AreEqual("#000000", Background("youtube-viewport"));
        }
        finally { await WidgetTestHost.DestroyAsync(widget); }

        string? Background(params string[] classes) => theme.Resolve(new WrssElement(
            "stack", StyleClasses: new HashSet<string>(classes))).Get("background")?.Text;

        void AssertBase(string expectedRoot)
        {
            var snapshot = widget.RenderSnapshot("youtube.acrylic", 1);
            Assert.AreEqual(expectedRoot, snapshot.Root.Id);
            Assert.AreEqual(WidgetSurfaceAppearance.DesktopAcrylic, snapshot.Surface!.Appearance);
            Assert.IsTrue(snapshot.ProtocolVersion >= ProtocolConstants.DesktopAcrylicVersion);
            Assert.IsEmpty(ViewSnapshotValidator.Validate(snapshot));
            Assert.IsTrue(ViewSnapshotValidator.Validate(snapshot with { ProtocolVersion = ProtocolConstants.DesktopAcrylicVersion - 1 })
                .Any(error => error.Path == "$.surface" && error.Code == "feature_requires_version"));
            Assert.AreEqual("transparent", Background(snapshot.Root.StyleClasses.ToArray()));
            Assert.IsEmpty(snapshot.PinnedLayouts, "Compact video pins remain host owned.");
            if (snapshot.EmbeddedMediaSession is { } media)
            {
                Assert.AreEqual(WidgetSurfaceAppearance.Theme, media.Surface.Appearance);
                Assert.AreEqual(356d, media.Surface.MinimumWidth);
                Assert.AreEqual(200d, media.Surface.MinimumHeight);
            }
            var roundTrip = SnapshotJson.Deserialize(SnapshotJson.Serialize(snapshot));
            Assert.AreEqual(WidgetSurfaceAppearance.DesktopAcrylic, roundTrip.Surface!.Appearance);
            Assert.AreEqual(snapshot.EmbeddedMediaSession?.Surface.Appearance,
                roundTrip.EmbeddedMediaSession?.Surface.Appearance);
        }
    }
}
