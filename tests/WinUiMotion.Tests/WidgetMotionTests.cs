using System.Numerics;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WidgetRail.OverlayFrontend.WinUI.Motion;
using WidgetRail.PlatformSettings;
using WidgetRail.WidgetProtocol;

namespace WinUiMotion.Tests;

[TestClass]
public sealed class WidgetMotionTests
{
    private static WidgetMotionOptions Options(AppearanceSettings? appearance = null, bool system = true) =>
        WidgetMotionOptions.From(appearance ?? AppearanceSettings.Default, system);

    [TestMethod]
    public void DefaultsAndSpeedKeepExistingSettingMeaning()
    {
        var options = Options();
        Assert.AreEqual(WidgetSectionAnimation.Slide, options.Section);
        Assert.AreEqual(WidgetModalAnimation.Zoom, options.Modal);
        Assert.AreEqual(WidgetFocusAnimation.Fade, options.Focus);
        Assert.AreEqual(208, options.Duration(208).TotalMilliseconds);
        Assert.AreEqual(104, Options(AppearanceSettings.Default with { WidgetAnimationSpeed = 100 }).Duration(208).TotalMilliseconds);
        Assert.AreEqual(416, Options(AppearanceSettings.Default with { WidgetAnimationSpeed = -1 }).Duration(208).TotalMilliseconds);
        Assert.AreEqual(208, Options(AppearanceSettings.Default with { WidgetAnimationSpeed = double.NaN }).Duration(208).TotalMilliseconds);
        Assert.AreEqual(208 / 1.25, Options(AppearanceSettings.Default with { WidgetAnimationSpeed = 1.25 }).Duration(208).TotalMilliseconds, .01);
    }

    [TestMethod]
    public void ReducedMotionUsesSystemOnlyWhenRequestedAndSettlesEveryFamily()
    {
        Assert.IsTrue(Options(AppearanceSettings.Default with { Motion = MotionPreference.System }, false).Reduced);
        Assert.IsFalse(Options(AppearanceSettings.Default with { Motion = MotionPreference.Full }, false).Reduced);
        var reduced = Options(AppearanceSettings.Default with { Motion = MotionPreference.Reduced });
        Assert.AreEqual(TimeSpan.Zero, WidgetMotionPolicy.Section(reduced, new(300, 200), 1).Incoming.Duration);
        Assert.AreEqual(TimeSpan.Zero, WidgetMotionPolicy.Dialog(reduced, true).Duration);
        Assert.AreEqual(TimeSpan.Zero, WidgetMotionPolicy.Focus(reduced, new(100, 40), true).Duration);
        Assert.AreEqual(TimeSpan.Zero, WidgetMotionPolicy.Layout(reduced, new(20, 0), new(100, 20), new(200, 20), false).Duration);
    }

    [TestMethod]
    public void SectionsNeverCrossfadeAndDirectionsRemainPredictable()
    {
        foreach (var style in Enum.GetValues<WidgetSectionAnimation>())
        {
            var plan = WidgetMotionPolicy.Section(Options(AppearanceSettings.Default with { SectionAnimation = style }), new(300, 200), 1);
            Assert.AreEqual(1f, plan.Incoming.From.Opacity);
            Assert.AreEqual(1f, plan.Outgoing.To.Opacity);
            Assert.AreEqual(plan.Incoming.Duration, plan.Outgoing.Duration);
        }
        var slide = WidgetMotionPolicy.Section(Options(), new(300, 200), -1);
        Assert.AreEqual(-300, slide.Incoming.From.Translation.X);
        Assert.AreEqual(300, slide.Outgoing.To.Translation.X);
        var vertical = WidgetMotionPolicy.Section(Options(AppearanceSettings.Default with { SectionAnimation = WidgetSectionAnimation.VerticalSlide }), new(300, 200), -1);
        Assert.AreEqual(-200, vertical.Incoming.From.Translation.Y);
        Assert.AreEqual(200, vertical.Outgoing.To.Translation.Y);
    }

    [TestMethod]
    public void RevealAndCoverUseComplementaryViewportClips()
    {
        foreach (var style in new[] { WidgetSectionAnimation.Reveal, WidgetSectionAnimation.CoverSlide })
        foreach (var direction in new[] { -1, 1 })
        {
            var plan = WidgetMotionPolicy.Section(Options(AppearanceSettings.Default with { SectionAnimation = style }), new(300, 200), direction);
            foreach (var progress in new[] { 0f, .1f, .5f, .9f, 1f })
            {
                var incoming = Vector4.Lerp(plan.Incoming.From.Insets, plan.Incoming.To.Insets, progress);
                var outgoing = Vector4.Lerp(plan.Outgoing.From.Insets, plan.Outgoing.To.Insets, progress);
                Assert.AreEqual(300, 600 - incoming.X - incoming.Z - outgoing.X - outgoing.Z, .001);
            }
        }
        var paging = WidgetMotionPolicy.Section(Options(AppearanceSettings.Default with { SectionAnimation = WidgetSectionAnimation.Paging }), new(300, 200), 1);
        Assert.AreEqual(new Vector3(.96f, .96f, 1), paging.Outgoing.To.Scale);
        Assert.AreEqual(200, paging.Outgoing.To.Insets.W);
    }

    [TestMethod]
    public void DialogAndScrimShareTimingWithoutScalingTheScrim()
    {
        var zoom = WidgetMotionPolicy.Dialog(Options(), true);
        var scrim = WidgetMotionPolicy.Dialog(Options(), true, true);
        Assert.AreEqual(260, zoom.Duration.TotalMilliseconds);
        Assert.AreEqual(zoom.Duration, scrim.Duration);
        Assert.AreEqual(new Vector3(.9f, .9f, 1), zoom.From.Scale);
        Assert.AreEqual(Vector3.One, scrim.From.Scale);
        Assert.AreEqual(Vector3.Zero, scrim.From.Translation);
        Assert.AreEqual(zoom.From, WidgetMotionPolicy.Dialog(Options(), false).To);
        Assert.AreEqual(Vector3.One, WidgetMotionPolicy.Dialog(Options(AppearanceSettings.Default with { ModalAnimation = WidgetModalAnimation.Lift }), true).From.Scale);
        Assert.AreEqual(TimeSpan.Zero, WidgetMotionPolicy.Dialog(Options(AppearanceSettings.Default with { AnimateWidgetModals = false }), true).Duration);
    }

    [TestMethod]
    public void FocusAnimatesOnlyDecorationAndSettleStaysInsideBounds()
    {
        var fade = WidgetMotionPolicy.Focus(Options(), new(200, 40), true);
        Assert.AreEqual(Vector3.One, fade.From.Scale);
        Assert.AreEqual(Vector3.Zero, fade.From.Translation);
        Assert.AreEqual(0, fade.From.Opacity);
        Assert.AreEqual(240, fade.Duration.TotalMilliseconds);
        var settle = WidgetMotionPolicy.Focus(Options(AppearanceSettings.Default with { FocusAnimation = WidgetFocusAnimation.Settle }), new(200, 40), true);
        Assert.AreEqual(.94f, settle.From.Scale.X, .001);
        Assert.AreEqual(.7f, settle.From.Scale.Y, .001);
        Assert.AreEqual(.65f, settle.From.Opacity);
        Assert.AreEqual(TimeSpan.Zero, WidgetMotionPolicy.Focus(Options(AppearanceSettings.Default with { FocusAnimation = WidgetFocusAnimation.None }), new(200, 40), true).Duration);
    }

    [TestMethod]
    public void LayoutLabelsDoNotStretchAndSelectionUsesTheSharedTimeline()
    {
        var layout = WidgetMotionPolicy.Layout(Options(), new(20, -8), new(50, 20), new(100, 40), false);
        var selection = WidgetMotionPolicy.Layout(Options(), new(20, -8), new(50, 20), new(100, 40), true);
        Assert.AreEqual(Vector3.One, layout.From.Scale);
        Assert.AreEqual(new Vector3(.5f, .5f, 1), selection.From.Scale);
        Assert.AreEqual(new Vector3(-5, -18, 0), selection.From.Translation, "Centered scaling must preserve the old selection's top-left.");
        Assert.AreEqual(new Vector3(20, -8, 0), layout.From.Translation);
        Assert.AreEqual(layout.Duration, WidgetMotionPolicy.Section(Options(), new(300, 200), 1).Incoming.Duration);
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => WidgetMotionPolicy.Section(Options(), new(float.NaN, 20), 1));
    }

    [TestMethod]
    public void GroupKeyChangesCoordinateContentHeaderAndSelectionWithoutReplay()
    {
        var groups = new WidgetMotionGroups();
        WidgetTransition[] Page(string key, int order) => Enum.GetValues<WidgetTransitionKind>().Select(kind => new WidgetTransition("group", key, order, kind)).ToArray();
        Assert.AreEqual(0, groups.Update(Page("home", 0)).Count);
        var forward = groups.Update(Page("library", 1)).Single();
        Assert.AreEqual(1, forward.Direction);
        Assert.AreEqual(0, groups.Update(Page("library", 1)).Count);
        Assert.AreEqual(-1, groups.Update(Page("home", 0)).Single().Direction);
        Assert.AreEqual(0, groups.Update([]).Count);
        Assert.AreEqual(0, groups.Update(Page("home", 0)).Count, "Recreated groups start settled.");
        groups.Reset();
        Assert.AreEqual(0, groups.Update(Page("library", 1)).Count);
    }

    [TestMethod]
    public void InvalidGroupPublicationDoesNotReplaceCommittedState()
    {
        var groups = new WidgetMotionGroups();
        groups.Update([new("group", "home", 0)]);
        Assert.ThrowsExactly<InvalidOperationException>(() => groups.Update([new("group", "a", 1), new("group", "b", 2)]));
        Assert.AreEqual("home", groups.Update([new("group", "library", 1)]).Single().PreviousKey);
        Assert.ThrowsExactly<InvalidOperationException>(() => groups.Update(Enumerable.Range(0, 8).Select(i => new WidgetTransition("group" + i, "key", i))));
    }
}
