using Microsoft.VisualStudio.TestTools.UnitTesting;
using WidgetRail.OverlayFrontend.WinUI.Shell;
using WidgetRail.PlatformSettings;
using WidgetRail.WidgetProtocol;

namespace WinUiShell.Tests;

[TestClass]
public sealed class OverlaySurfaceSizingTests
{
    private static readonly SurfaceExtent Chrome = new(72, 178);
    private static readonly SurfaceExtent Desktop = new(1920, 1080);

    [TestMethod]
    public void ModeDefaultsPreserveNativeAuthoredContentExtents()
    {
        foreach (var (mode, expected) in new[] { (WidgetSurfaceMode.Compact, new SurfaceExtent(632, 598)),
            (WidgetSurfaceMode.Standard, new SurfaceExtent(952, 698)), (WidgetSurfaceMode.Adaptive, new SurfaceExtent(952, 698)),
            (WidgetSurfaceMode.Wide, new SurfaceExtent(1192, 798)) })
            Assert.AreEqual(expected, OverlaySurfaceSizing.Resolve(new() { Mode = mode }, Desktop, Chrome, 1));
        Assert.AreEqual(new SurfaceExtent(1180, 700), OverlaySurfaceSizing.Resolve(null, Desktop, Chrome, 1));
    }

    [TestMethod]
    public void FillAvailableRespectsIndependentAxesAndMeasuredChrome()
    {
        var hints = new WidgetSurfaceHints { PreferredWidth = 960, PreferredHeight = 760, WidthMode = WidgetSurfaceAxisMode.FillAvailable };
        Assert.AreEqual(new SurfaceExtent(1920, 938), OverlaySurfaceSizing.Resolve(hints, Desktop, Chrome, 1));
        Assert.AreEqual(new SurfaceExtent(1000, 850), OverlaySurfaceSizing.Resolve(hints with { HeightMode = WidgetSurfaceAxisMode.FillAvailable },
            new(1000, 850), new(66, 242), 1));
    }

    [TestMethod]
    public void ContentMeasuresOnceWithinFiniteAdmissionThenClampsToMinimumAndPreferred()
    {
        var calls = 0;
        var hints = new WidgetSurfaceHints { Mode = WidgetSurfaceMode.Wide, WidthMode = WidgetSurfaceAxisMode.Content,
            HeightMode = WidgetSurfaceAxisMode.Content, MinimumWidth = 300, MinimumHeight = 200 };
        var result = OverlaySurfaceSizing.Resolve(hints, Desktop, Chrome, 1, constraint =>
        {
            ++calls;
            Assert.AreEqual(new SurfaceExtent(1120, 620), constraint);
            return new(290, 4000);
        });
        Assert.AreEqual(1, calls);
        Assert.AreEqual(new SurfaceExtent(372, 798), result);
        Assert.AreEqual(new SurfaceExtent(1192, 798), OverlaySurfaceSizing.Resolve(hints, Desktop, Chrome, 1, _ => new(double.NaN, 0)));
    }

    [TestMethod]
    public void TinyHighDpiWorkAreaNeverProducesNegativeOrOverflowingExtent()
    {
        foreach (var dpi in new[] { 96, 120, 144, 192 })
        foreach (var zoom in new[] { .5, 1, 1.25 })
        foreach (var width in new[] { 1d, 64, 320, 1920 })
        foreach (var mode in Enum.GetValues<WidgetSurfaceAxisMode>())
        {
            var available = new SurfaceExtent(width / (dpi / 96d * zoom), 180 / (dpi / 96d * zoom));
            var result = OverlaySurfaceSizing.Resolve(new() { WidthMode = mode, HeightMode = mode }, available, Chrome, 1.5, _ => new(0, 0));
            Assert.IsTrue(result.Width >= 0 && result.Width <= available.Width);
            Assert.IsTrue(result.Height >= 0 && result.Height <= available.Height);
        }
    }

    [TestMethod]
    public void TextScaleAddsReflowRoomAndInvalidAtomicHintPairsFailToSafeDefaults()
    {
        var hints = new WidgetSurfaceHints { PreferredWidth = 1000, PreferredHeight = 800 };
        Assert.AreEqual(new SurfaceExtent(1322, 1378), OverlaySurfaceSizing.Resolve(hints, new(3000, 2000), Chrome, 1.5));
        Assert.AreEqual(new SurfaceExtent(952, 698), OverlaySurfaceSizing.Resolve(new() { PreferredWidth = 500 }, Desktop, Chrome, 1));
        Assert.AreEqual(new SurfaceExtent(952, 698), OverlaySurfaceSizing.Resolve(new() { MinimumWidth = 2000, MinimumHeight = 300 }, Desktop, Chrome, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => OverlaySurfaceSizing.Resolve(hints, new(double.PositiveInfinity, 1), Chrome, 1));
    }

    [TestMethod]
    public void DisplayScaleUsesSavedPhysicalIdentityAndFallsBackGlobally()
    {
        var appearance = DisplayScalePolicy.Set(AppearanceSettings.Default with { InterfaceScale = .75, TextScale = .9 }, "physical.id", new(1.25, 1.5));
        Assert.AreEqual(new DisplayScaleSettings(1.25, 1.5), OverlaySurfaceSizing.Scale(appearance, "physical.id"));
        Assert.AreEqual(new DisplayScaleSettings(.75, .9), OverlaySurfaceSizing.Scale(appearance, "connection.token"));
        Assert.AreEqual(new DisplayScaleSettings(.75, .9), OverlaySurfaceSizing.Scale(appearance, null));
    }
}
