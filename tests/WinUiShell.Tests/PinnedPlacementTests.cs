using Microsoft.VisualStudio.TestTools.UnitTesting;
using WidgetRail.OverlayFrontend.WinUI.Shell;

namespace WinUiShell.Tests;

[TestClass]
public sealed class PinnedPlacementTests
{
    private static readonly PinnedPlacementLimits Limits = PinnedPlacementLimits.Default;
    [TestMethod]
    public void DefaultUsesDipsAndTopRightMarginWithinWorkArea()
    {
        var monitor = new PinnedMonitor("left", new(-1920, -200, 1920, 1040), 1.25, true);
        var result = PinnedPlacementPolicy.Resolve([monitor], null, Limits)!;
        Assert.AreEqual(new PinnedBounds(-620, -180, 600, 338), result.Bounds);
        Assert.IsFalse(result.UsedFallback);
    }

    [TestMethod]
    public void CaptureRoundTripPreservesWorkAreaAnchorAcrossDpiAndRotation()
    {
        var old = new PinnedMonitor("display", new(-2560, 0, 2560, 1400), 1.25);
        var original = new PinnedBounds(-1260, 500, 600, 375);
        var saved = PinnedPlacementPolicy.Capture(original, old, Limits, "compact", 65);
        Assert.AreEqual(original, PinnedPlacementPolicy.Resolve([old], saved, Limits)!.Bounds);
        var current = old with { WorkArea = new(0, 0, 1440, 2500), Scale = 1.5 };
        var changed = PinnedPlacementPolicy.Resolve([current], saved, Limits)!;
        Assert.AreEqual(720, changed.Bounds.Width);
        Assert.AreEqual(450, changed.Bounds.Height);
        var recaptured = PinnedPlacementPolicy.Capture(changed.Bounds, current, Limits, "compact", 65);
        Assert.AreEqual(saved.AnchorX, recaptured.AnchorX, .002);
        Assert.AreEqual(saved.AnchorY, recaptured.AnchorY, .002);
        Assert.AreEqual(65, recaptured.OpacityPercent);
    }

    [TestMethod]
    public void MissingMonitorInvalidValuesAndSmallScreensFailPredictably()
    {
        var monitor = new PinnedMonitor("current", new(0, 0, 800, 600), 1, true);
        var saved = new PinnedPlacement("missing", 1, 1, 960, 540, "compact");
        var found = PinnedPlacementPolicy.Resolve([monitor], saved, Limits)!;
        Assert.IsTrue(found.UsedFallback);
        Assert.AreEqual(new PinnedBounds(0, 60, 800, 540), found.Bounds);
        Assert.IsTrue(PinnedPlacementPolicy.Resolve([monitor], saved with { WidthDip = double.NaN }, Limits)!.UsedFallback);
        Assert.IsNull(PinnedPlacementPolicy.Resolve([monitor with { WorkArea = new(0, 0, 100, 100) }], saved, Limits));
        Assert.IsNull(PinnedPlacementPolicy.Resolve([monitor with { Scale = double.PositiveInfinity }], null, Limits));
        Assert.AreEqual(new PinnedBounds(0, 0, 240, 135), PinnedPlacementPolicy.Constrain(new(-500, -500, 0, 0), monitor, Limits));
    }

    [TestMethod]
    public async Task PersistenceIsBoundedAtomicAndProfileIsolated()
    {
        var path = Path.Combine(Path.GetTempPath(), "wrail-pin-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        try
        {
            var store = new PinnedPreferencesStore(path);
            var placement = new PinnedPlacement("display", .25, .75, 480, 270, "compact", 45);
            var first = new PinnedPreferences(1, "widget", new Dictionary<string, PinnedPlacement> { ["widget"] = placement });
            var last = first with { WidgetId = null };
            await Task.WhenAll(store.SaveAsync(first), store.SaveAsync(last));
            var loaded = await store.LoadAsync();
            Assert.IsNull(loaded.WidgetId);
            Assert.AreEqual(placement, loaded.Placements["widget"]);
            Assert.AreEqual(0, Directory.GetFiles(path, "*.tmp").Length);
            Assert.AreEqual(0, (await new PinnedPreferencesStore(Path.Combine(path, "other")).LoadAsync()).Placements.Count);
            await File.WriteAllTextAsync(Path.Combine(path, "winui-pinned-state.json"), "{\"Version\":1,\"WidgetId\":\"missing\",\"Placements\":{}}");
            Assert.IsNull((await store.LoadAsync()).WidgetId);
            await File.WriteAllTextAsync(Path.Combine(path, "winui-pinned-state.json"), new string('x', 1024 * 1024 + 1));
            Assert.AreEqual(0, (await store.LoadAsync()).Placements.Count);
        }
        finally { Directory.Delete(path, recursive: true); }
    }
}
