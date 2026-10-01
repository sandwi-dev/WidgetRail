using System.Numerics;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WidgetRail.OverlayFrontend.WinUI.Motion;
using WidgetRail.PlatformSettings;
using WidgetRail.WidgetProtocol;

namespace WinUiMotion.Tests;

[TestClass]
public sealed class WidgetMotionTests
{
    [TestMethod]
    public void OverlayEntranceUsesOriginalZoomAndDirectionalEasingWithSharedSpeed()
    {
        var appearance = AppearanceSettings.Default with { Motion = MotionPreference.Full,
            AnimateWidgetSwitching = false, SectionAnimation = WidgetSectionAnimation.None };
        var opening = WidgetMotionPolicy.OverlayVisibility(appearance, true, true);
        var closing = WidgetMotionPolicy.OverlayVisibility(appearance, true, false);
        Assert.AreEqual(new Vector3(.88f, .88f, 1), opening.From.Scale);
        Assert.AreEqual(0f, opening.From.Opacity);
        Assert.AreEqual(Vector3.Zero, opening.From.Translation);
        Assert.AreEqual(WidgetMotionPose.Identity, opening.To);
        Assert.AreEqual(opening.From, closing.To);
        Assert.AreEqual(opening.To, closing.From);
        Assert.AreEqual(140d, opening.Duration.TotalMilliseconds);
        Assert.AreEqual(100d, closing.Duration.TotalMilliseconds);
        Assert.AreEqual(WidgetMotionEasing.EaseOut, opening.Easing);
        Assert.AreEqual(WidgetMotionEasing.EaseIn, closing.Easing);
        Assert.AreEqual(70d, WidgetMotionPolicy.OverlayVisibility(appearance with { WidgetAnimationSpeed = 2 }, true, true).Duration.TotalMilliseconds);
        Assert.AreEqual(200d, WidgetMotionPolicy.OverlayVisibility(appearance with { WidgetAnimationSpeed = .5 }, true, false).Duration.TotalMilliseconds);
    }

    [TestMethod]
    public void OverlayBackdropFadesWithoutZoomAndReducedMotionSettlesBothDirections()
    {
        foreach (var opening in new[] { true, false })
        {
            var appearance = AppearanceSettings.Default with { Motion = MotionPreference.Full };
            var shell = WidgetMotionPolicy.OverlayVisibility(appearance, true, opening);
            var backdrop = WidgetMotionPolicy.OverlayVisibility(appearance, true, opening, backdrop: true);
            Assert.AreEqual(shell.Duration, backdrop.Duration);
            Assert.AreEqual(shell.Easing, backdrop.Easing);
            Assert.AreEqual(shell.From.Opacity, backdrop.From.Opacity);
            Assert.AreEqual(shell.To.Opacity, backdrop.To.Opacity);
            Assert.AreEqual(Vector3.One, backdrop.From.Scale);
            Assert.AreEqual(Vector3.One, backdrop.To.Scale);
            foreach (var preference in new[] { MotionPreference.Reduced, MotionPreference.System })
            {
                var reduced = WidgetMotionPolicy.OverlayVisibility(appearance with { Motion = preference }, false, opening);
                Assert.AreEqual(TimeSpan.Zero, reduced.Duration);
                Assert.AreEqual(Vector3.One, reduced.From.Scale);
                Assert.AreEqual(Vector3.One, reduced.To.Scale);
                Assert.AreEqual(opening ? 1f : 0f, reduced.To.Opacity);
            }
            Assert.AreEqual(shell.Duration, WidgetMotionPolicy.OverlayVisibility(appearance, false, opening).Duration,
                "Explicit full motion overrides the system preference as elsewhere in the host.");
        }
    }

    [TestMethod]
    public void GuideCrossfadeUsesSharedSpeedAndAccessibilityWithoutMovingContent()
    {
        var appearance = AppearanceSettings.Default with { Motion = MotionPreference.Full,
            SectionAnimation = WidgetSectionAnimation.Slide, Transparency = TransparencyPreference.Full };
        WidgetSectionMotion Fade(AppearanceSettings value, bool system = true, bool contrast = false) =>
            WidgetMotionPolicy.GuideCrossfade(value, system, contrast);
        var fade = Fade(appearance);
        Assert.AreEqual(140, fade.Incoming.Duration.TotalMilliseconds);
        Assert.AreEqual(fade.Incoming.Duration, fade.Outgoing.Duration);
        Assert.AreEqual(WidgetMotionPose.Identity with { Opacity = 0 }, fade.Incoming.From);
        Assert.AreEqual(WidgetMotionPose.Identity, fade.Incoming.To);
        Assert.AreEqual(WidgetMotionPose.Identity, fade.Outgoing.From);
        Assert.AreEqual(fade.Incoming.From, fade.Outgoing.To);
        Assert.AreEqual(70, Fade(appearance with { WidgetAnimationSpeed = 2 }).Incoming.Duration.TotalMilliseconds);
        Assert.AreEqual(TimeSpan.Zero, Fade(appearance with { Motion = MotionPreference.Reduced }).Incoming.Duration);
        Assert.AreEqual(TimeSpan.Zero, Fade(appearance with { Motion = MotionPreference.System }, false).Incoming.Duration);
        Assert.AreEqual(TimeSpan.Zero, Fade(appearance with { SectionAnimation = WidgetSectionAnimation.None }).Incoming.Duration);
        Assert.AreEqual(TimeSpan.Zero, Fade(appearance with { Transparency = TransparencyPreference.Reduced }).Incoming.Duration);
        Assert.AreEqual(TimeSpan.Zero, Fade(appearance, contrast: true).Incoming.Duration);
    }

    [TestMethod]
    public void ContentResizeIsIndependentOfSwitchToggleButHonorsMotionPreferences()
    {
        var appearance = AppearanceSettings.Default with { AnimateWidgetSwitching = false,
            Motion = MotionPreference.Full, Contrast = ContrastPreference.Standard };
        WidgetMotionRecipe Resize(AppearanceSettings value, bool system = true) =>
            WidgetMotionPolicy.WidgetResize(value, system, new(980, 700), new(760, 440),
                reason: WidgetResizeReason.ContentSizeChanged);
        Assert.AreEqual(140, Resize(appearance).Duration.TotalMilliseconds);
        Assert.AreEqual(70, Resize(appearance with { WidgetAnimationSpeed = 2 }).Duration.TotalMilliseconds);
        Assert.AreEqual(TimeSpan.Zero, Resize(appearance with { Motion = MotionPreference.Reduced }).Duration);
        Assert.AreEqual(TimeSpan.Zero, Resize(appearance with { Motion = MotionPreference.System }, false).Duration);
        Assert.AreEqual(TimeSpan.Zero, Resize(appearance with { Transparency = TransparencyPreference.Reduced }).Duration);
        Assert.AreEqual(TimeSpan.Zero, Resize(appearance with { Contrast = ContrastPreference.High }).Duration);
    }

    [TestMethod]
    public void WidgetResizeUsesActualExtentsWithoutSlideOrFade()
    {
        var appearance = AppearanceSettings.Default with { AnimateWidgetSwitching = true, Motion = MotionPreference.Full, Contrast = ContrastPreference.Standard };
        WidgetMotionRecipe Resize(AppearanceSettings value, bool system = true, bool highContrast = false) =>
            WidgetMotionPolicy.WidgetResize(value, system, new(240, 160), new(720, 400), highContrast);
        foreach (var section in Enum.GetValues<WidgetSectionAnimation>())
        {
            var reveal = Resize(appearance with { SectionAnimation = section });
            Assert.AreEqual(140, reveal.Duration.TotalMilliseconds);
            Assert.AreEqual(1f, reveal.From.Opacity);
            Assert.AreEqual(Vector3.Zero, reveal.From.Translation);
            Assert.AreEqual(WidgetMotionPose.Identity, reveal.To);
            Assert.AreEqual(new Vector3(1f / 3, .4f, 1), reveal.From.Scale);
        }
        Assert.AreEqual(70, Resize(appearance with { WidgetAnimationSpeed = 2 }).Duration.TotalMilliseconds);
        Assert.AreEqual(TimeSpan.Zero, Resize(appearance with { AnimateWidgetSwitching = false }).Duration);
        Assert.AreEqual(TimeSpan.Zero, Resize(appearance with { Motion = MotionPreference.Reduced }).Duration);
        Assert.AreEqual(TimeSpan.Zero, Resize(appearance with { Motion = MotionPreference.System }, false).Duration);
        Assert.AreEqual(TimeSpan.Zero, Resize(appearance with { Transparency = TransparencyPreference.Reduced }).Duration);
        Assert.AreEqual(TimeSpan.Zero, Resize(appearance with { Contrast = ContrastPreference.System }, true, true).Duration);
        Assert.AreEqual(TimeSpan.Zero, WidgetMotionPolicy.WidgetResize(appearance, true, new(720, 400), new(720, 400)).Duration);
        Assert.AreEqual(new Vector3(3, 2.5f, 1), WidgetMotionPolicy.WidgetResize(appearance, true, new(720, 400), new(240, 160)).From.Scale);
    }

    [TestMethod]
    public void BackgroundBlendPreservesCoverageUntilTheIncomingPixelsReplaceIt()
    {
        var blend = WidgetMotionPolicy.ArtworkCrossfade(TimeSpan.FromMilliseconds(400));
        foreach (var fraction in new[] { 0f, .1f, .25f, .5f, .75f, .9f, 1f })
        {
            var incoming = blend.Incoming.From.Opacity + fraction * (blend.Incoming.To.Opacity - blend.Incoming.From.Opacity);
            var outgoing = blend.Outgoing.From.Opacity + fraction * (blend.Outgoing.To.Opacity - blend.Outgoing.From.Opacity);
            Assert.AreEqual(1, incoming + (1 - incoming) * outgoing, .0001, "Opaque artwork must not expose the backdrop while blending.");
        }
    }

    private static WidgetMotionOptions Options(AppearanceSettings? appearance = null, bool system = true) =>
        WidgetMotionOptions.From(appearance ?? AppearanceSettings.Default, system);

    [TestMethod]
    public void DefaultsAndSpeedKeepExistingSettingMeaning()
    {
        var options = Options();
        Assert.AreEqual(WidgetSectionAnimation.Slide, options.Section);
        Assert.AreEqual(WidgetModalAnimation.Zoom, options.Modal);
        Assert.AreEqual(WidgetFocusAnimation.Settle, options.Focus);
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
        var fade = WidgetMotionPolicy.Focus(Options(AppearanceSettings.Default with { FocusAnimation = WidgetFocusAnimation.Fade }), new(200, 40), true);
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
