using Microsoft.VisualStudio.TestTools.UnitTesting;
using WidgetRail.OverlayFrontend.WinUI.Shell;
using WidgetRail.PlatformSettings;
using WidgetRail.WidgetProtocol;

namespace WinUiShell.Tests;

[TestClass]
public sealed class OverlayAppearancePolicyTests
{
    [TestMethod]
    public void ExactWidgetOverridePrecedesGlobalIncludingWidgetDeclaration()
    {
        var settings = AppearanceSettings.Default with { WidgetSurfaceAppearance = WidgetSurfaceAppearanceOverride.Solid,
            WidgetSurfaceAppearanceOverrides = new Dictionary<string, WidgetSurfaceAppearanceOverride> { ["clock"] = WidgetSurfaceAppearanceOverride.Widget } };
        Assert.AreEqual(WidgetSurfaceAppearance.Transparent, OverlayAppearancePolicy.Resolve(settings, "clock", WidgetSurfaceAppearance.Transparent, false).Effective);
        Assert.AreEqual(WidgetSurfaceAppearance.Solid, OverlayAppearancePolicy.Resolve(settings, "Clock", WidgetSurfaceAppearance.Transparent, false).Effective);
        Assert.AreEqual(WidgetSurfaceAppearance.Solid, OverlayAppearancePolicy.Resolve(settings, null, WidgetSurfaceAppearance.Transparent, false).Effective);
    }

    [TestMethod]
    public void AllDeclaredAndOverrideModesFollowNativePrecedence()
    {
        foreach (var declared in Enum.GetValues<WidgetSurfaceAppearance>())
        foreach (var selected in Enum.GetValues<WidgetSurfaceAppearanceOverride>())
        {
            var settings = AppearanceSettings.Default with { WidgetSurfaceAppearance = selected };
            var expected = selected == WidgetSurfaceAppearanceOverride.Widget ? declared : Enum.Parse<WidgetSurfaceAppearance>(selected.ToString());
            var actual = OverlayAppearancePolicy.Resolve(settings, "widget", declared, false);
            Assert.AreEqual(declared, actual.Declared); Assert.AreEqual(expected, actual.Requested); Assert.AreEqual(expected, actual.Effective);
            Assert.IsNull(actual.FallbackReason);
        }
    }

    [TestMethod]
    public void AccessibilityAndZeroBackdropFallbackDoNotMutateRequestedPreference()
    {
        var settings = AppearanceSettings.Default with { WidgetSurfaceAppearance = WidgetSurfaceAppearanceOverride.Transparent };
        var system = OverlayAppearancePolicy.Resolve(settings, "widget", WidgetSurfaceAppearance.Theme, true);
        Assert.AreEqual("high-contrast", system.FallbackReason); Assert.AreEqual(WidgetSurfaceAppearance.Transparent, system.Requested);
        Assert.AreEqual(WidgetSurfaceAppearance.Solid, system.Effective);
        Assert.AreEqual(WidgetSurfaceAppearance.Transparent, OverlayAppearancePolicy.Resolve(settings with { Contrast = ContrastPreference.Standard }, "widget", WidgetSurfaceAppearance.Theme, true).Effective);
        Assert.AreEqual("high-contrast", OverlayAppearancePolicy.Resolve(settings with { Contrast = ContrastPreference.High }, "widget", WidgetSurfaceAppearance.Theme, false).FallbackReason);
        Assert.AreEqual("reduced-transparency", OverlayAppearancePolicy.Resolve(settings with { Transparency = TransparencyPreference.Reduced }, "widget", WidgetSurfaceAppearance.Theme, false).FallbackReason);
        Assert.AreEqual("zero-backdrop-contrast", OverlayAppearancePolicy.Resolve(settings with { BackdropOpacity = 0 }, "widget", WidgetSurfaceAppearance.Theme, false).FallbackReason);
        Assert.AreEqual("unsupported-composition", OverlayAppearancePolicy.Resolve(settings, "widget", WidgetSurfaceAppearance.Theme, false, false).FallbackReason);
        Assert.AreEqual(WidgetSurfaceAppearance.Theme, OverlayAppearancePolicy.Resolve(settings with { BackdropOpacity = 0, WidgetSurfaceAppearance = WidgetSurfaceAppearanceOverride.Theme }, "widget", WidgetSurfaceAppearance.Theme, false).Effective);
    }
}
