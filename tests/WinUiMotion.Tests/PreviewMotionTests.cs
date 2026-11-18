using System.Numerics;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WidgetRail.OverlayFrontend.WinUI.Motion;
using WidgetRail.PlatformSettings;

namespace WinUiMotion.Tests;

[TestClass]
public sealed class PreviewMotionTests
{
    [TestMethod]
    public void PreviewHasIndependentShortEntranceWithNoTranslation()
    {
        var options = WidgetMotionOptions.From(AppearanceSettings.Default with { Motion = MotionPreference.Full }, true);
        var preview = WidgetMotionPolicy.Preview(options);
        Assert.AreEqual(TimeSpan.FromMilliseconds(140), preview.Duration);
        Assert.AreEqual(new Vector3(.97f, .97f, 1), preview.From.Scale);
        Assert.AreEqual(0f, preview.From.Opacity);
        Assert.AreEqual(Vector3.Zero, preview.From.Translation);
        Assert.AreEqual(WidgetMotionPose.Identity, preview.To);
        Assert.AreEqual(WidgetMotionEasing.EaseOut, preview.Easing);
        Assert.AreEqual(TimeSpan.FromMilliseconds(70), WidgetMotionPolicy.Preview(options with { Speed = 2 }).Duration);
        Assert.AreEqual(TimeSpan.FromMilliseconds(260), WidgetMotionPolicy.Dialog(options, true).Duration);
    }

    [TestMethod]
    public void MediaPreviewFadesWithoutMovingOrScalingTheVideoPlane()
    {
        var options = WidgetMotionOptions.From(AppearanceSettings.Default with { Motion = MotionPreference.Full }, true);
        var media = WidgetMotionPolicy.Preview(options, media: true);
        Assert.AreEqual(Vector3.One, media.From.Scale);
        Assert.AreEqual(Vector3.Zero, media.From.Translation);
        Assert.AreEqual(0f, media.From.Opacity);
        Assert.AreEqual(WidgetMotionPose.Identity, media.To);
    }

    [TestMethod]
    public void ReducedAndSystemDisabledPreviewsPresentImmediatelyWithoutHiddenPose()
    {
        foreach (var preference in new[] { MotionPreference.Reduced, MotionPreference.System })
            foreach (var media in new[] { false, true })
            {
                var options = WidgetMotionOptions.From(AppearanceSettings.Default with { Motion = preference }, false);
                var preview = WidgetMotionPolicy.Preview(options, media);
                Assert.AreEqual(TimeSpan.Zero, preview.Duration);
                Assert.AreEqual(WidgetMotionPose.Identity, preview.From);
                Assert.AreEqual(WidgetMotionPose.Identity, preview.To);
            }
    }
}
