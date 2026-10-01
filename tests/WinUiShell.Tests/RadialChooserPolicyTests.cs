using Microsoft.VisualStudio.TestTools.UnitTesting;
using WidgetRail.OverlayFrontend.WinUI.Shell;
namespace WinUiShell.Tests;
[TestClass]
public sealed class RadialChooserPolicyTests
{
    [TestMethod]
    public void RailOnlyInputNeverGainsRadialNeutralRequirements()
    {
        var state = new RadialChooserPolicy(); state.Reset(); state.UpdateOwner(false, 32767, 0);
        Assert.IsTrue(state.AllowScroll);
        Assert.AreEqual(0, state.PageStep(32767, 100));
    }
    [TestMethod]
    public void NativeSectorOrderStartsAtUpAndTurnsClockwise()
    {
        (short X, short Y)[] axes = [(0, 32767), (30000, 30000), (32767, 0), (30000, -30000), (0, -32767), (-30000, -30000), (-32767, 0), (-30000, 30000)];
        CollectionAssert.AreEqual(Enumerable.Range(0, 8).ToArray(), axes.Select(axis => RadialChooserPolicy.Sector(axis.X, axis.Y)!.Value).ToArray());
        Assert.IsNull(RadialChooserPolicy.Sector(0, 20000));
    }
    [TestMethod]
    public void EnteringAndPagingRequireFreshNeutralLeftStick()
    {
        var state = new RadialChooserPolicy(); state.UpdateOwner(true, 0, 0);
        Assert.IsNull(state.SelectSector(0, 32767));
        Assert.IsNull(state.SelectSector(0, 0)); Assert.AreEqual(0, state.SelectSector(0, 32767));
        state.RebasePage(); Assert.IsNull(state.SelectSector(0, 32767));
        state.SelectSector(0, 0); Assert.AreEqual(2, state.SelectSector(32767, 0));
    }
    [TestMethod]
    public void BrowsedPageWrapsIndependentlyAndSelectionCyclesWithinItsItems()
    {
        Assert.AreEqual(2, RadialChooserPolicy.NextPage(18, 0, -1));
        Assert.AreEqual(0, RadialChooserPolicy.NextPage(18, 2, 1));
        Assert.AreEqual(16, RadialChooserPolicy.StepSelection(18, 2, 3, 1));
        Assert.AreEqual(17, RadialChooserPolicy.StepSelection(18, 2, 3, -1));
        Assert.AreEqual(16, RadialChooserPolicy.StepSelection(18, 2, 17, 1));
        Assert.AreEqual(-1, RadialChooserPolicy.StepSelection(0, 0, 0, 1));
    }
    [TestMethod]
    public void RightStickOwnershipCannotLeakScrollOrPageAcrossSurfaces()
    {
        var state = new RadialChooserPolicy(); state.UpdateOwner(true, 32767, 0);
        Assert.AreEqual(0, state.PageStep(32767, 0));
        state.UpdateOwner(true, 0, 0); Assert.AreEqual(1, state.PageStep(32767, 100));
        Assert.AreEqual(0, state.PageStep(32767, 459)); Assert.AreEqual(1, state.PageStep(32767, 460));
        Assert.AreEqual(0, state.PageStep(32767, 584)); Assert.AreEqual(1, state.PageStep(32767, 585));
        state.UpdateOwner(false, 32767, 0); Assert.IsFalse(state.AllowScroll);
        state.UpdateOwner(false, 0, 0); Assert.IsTrue(state.AllowScroll);
    }
    [TestMethod]
    public void MissedRepeatTicksCoalesceAndDirectionReversalIsImmediate()
    {
        var state = new RadialChooserPolicy(); state.UpdateOwner(true, 0, 0);
        Assert.AreEqual(1, state.PageStep(32767, 0)); Assert.AreEqual(1, state.PageStep(32767, 5000));
        Assert.AreEqual(0, state.PageStep(32767, 5000)); Assert.AreEqual(-1, state.PageStep(-32767, 5010));
        state.Reset(); state.UpdateOwner(true, -32767, 0); Assert.AreEqual(0, state.PageStep(-32767, 6000));
    }
}
