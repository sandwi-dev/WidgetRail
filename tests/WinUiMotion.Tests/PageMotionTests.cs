using System.Numerics;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WidgetRail.OverlayFrontend.WinUI.Motion;
using WidgetRail.PlatformSettings;
using WidgetRail.WidgetProtocol;

namespace WinUiMotion.Tests;

[TestClass]
public sealed class PageMotionTests
{
    [TestMethod]
    public void NavigationDirectionIsExplicitAndDataUpdatesDoNotReplay()
    {
        var groups = new WidgetMotionGroups();
        Assert.AreEqual(0, groups.Update([Page("home", WidgetNavigationOperation.Replace)]).Count);
        Assert.AreEqual(1, groups.Update([Page("detail", WidgetNavigationOperation.Forward)])[0].Direction);
        Assert.AreEqual(0, groups.Update([Page("detail", WidgetNavigationOperation.Forward)]).Count);
        Assert.AreEqual(-1, groups.Update([Page("home", WidgetNavigationOperation.Back)])[0].Direction);
        Assert.AreEqual(0, groups.Update([Page("elsewhere", WidgetNavigationOperation.Replace)])[0].Direction);
        groups.Reset();
        Assert.AreEqual(0, groups.Update([Page("elsewhere", WidgetNavigationOperation.Back)]).Count);
    }

    [TestMethod]
    public void PagePreferenceIsIndependentFromSectionsAndReplaceIsNeutral()
    {
        foreach (var effect in Enum.GetValues<WidgetPageAnimation>())
        {
            var options = WidgetMotionOptions.From(AppearanceSettings.Default with { Motion = MotionPreference.Full,
                SectionAnimation = WidgetSectionAnimation.None, PageAnimation = effect }, true);
            var forward = WidgetMotionPolicy.Page(options, new(640, 480), 1);
            var back = WidgetMotionPolicy.Page(options, new(640, 480), -1);
            Assert.AreEqual(effect == WidgetPageAnimation.None ? 0d : 200d, forward.Incoming.Duration.TotalMilliseconds);
            Assert.AreEqual(-forward.Incoming.From.Translation, back.Incoming.From.Translation);
            var replace = WidgetMotionPolicy.Page(options, new(640, 480), 0);
            Assert.AreEqual(Vector3.Zero, replace.Incoming.From.Translation);
            Assert.AreEqual(Vector3.One, replace.Incoming.From.Scale);
        }
    }

    [TestMethod]
    public void PagesHonorSharedSpeedAndReducedMotion()
    {
        var appearance = AppearanceSettings.Default with { Motion = MotionPreference.Full, WidgetAnimationSpeed = .5 };
        Assert.AreEqual(400d, WidgetMotionPolicy.Page(WidgetMotionOptions.From(appearance, true), new(640, 480), 1).Incoming.Duration.TotalMilliseconds);
        foreach (var effect in Enum.GetValues<WidgetPageAnimation>())
            Assert.AreEqual(TimeSpan.Zero, WidgetMotionPolicy.Page(WidgetMotionOptions.From(appearance with
                { PageAnimation = effect, Motion = MotionPreference.Reduced }, true), new(640, 480), 1).Incoming.Duration);
    }

    private static WidgetTransition Page(string key, WidgetNavigationOperation operation) => new("pages", key, 0, WidgetTransitionKind.Page, operation);
}
