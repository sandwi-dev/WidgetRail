using Microsoft.VisualStudio.TestTools.UnitTesting;
using WidgetRail.OverlayFrontend.WinUI.Presentation;
using WidgetRail.WidgetProtocol;

namespace WinUiShell.Tests;

[TestClass]
public sealed class ControllerGuideStabilityTests
{
    private static readonly ControllerGuideHint[] Play = [new(ControllerPrompt.A, "Play", ControllerButton.A)];
    private static readonly ControllerGuideHint[] Pause = [new(ControllerPrompt.A, "Pause", ControllerButton.A)];

    [TestMethod]
    public void BriefBusyGapNeverChangesDisplayedHints()
    {
        var state = new ControllerGuideStability();
        Assert.IsTrue(state.Propose(Play, "widget", 0));
        Assert.IsFalse(state.Propose([], "widget", 10));
        CollectionAssert.AreEqual(Play, state.Displayed.ToArray());
        Assert.IsFalse(state.Commit(129));
        state.Propose(Play, "widget", 90);
        Assert.AreEqual(0, state.Remaining(200));
        Assert.IsFalse(state.Commit(200));
        CollectionAssert.AreEqual(Play, state.Displayed.ToArray());
    }

    [TestMethod]
    public void RepeatedIdenticalPublicationsDoNotPostponeStableChange()
    {
        var state = new ControllerGuideStability();
        state.Propose(Play, "widget", 0);
        state.Propose(Pause, "widget", 10);
        state.Propose(Pause.ToArray(), "widget", 100);
        Assert.IsFalse(state.Commit(129));
        Assert.IsTrue(state.Commit(130));
        CollectionAssert.AreEqual(Pause, state.Displayed.ToArray());
    }

    [TestMethod]
    public void NewCandidateRestartsQuietPeriodAndPersistentEmptyStateCommits()
    {
        var state = new ControllerGuideStability();
        state.Propose(Play, "widget", 0);
        state.Propose(Pause, "widget", 10);
        state.Propose([], "widget", 100);
        Assert.IsFalse(state.Commit(130));
        Assert.IsTrue(state.Commit(220));
        Assert.IsEmpty(state.Displayed);
    }

    [TestMethod]
    public void ContextChangeAndReloadReplaceHintsImmediately()
    {
        var state = new ControllerGuideStability();
        state.Propose(Play, ("widget", "root"), 0);
        state.Propose([], ("widget", "root"), 10);
        Assert.IsTrue(state.Propose(Pause, ("widget", "modal"), 20));
        Assert.AreEqual(0, state.Remaining(20));
        CollectionAssert.AreEqual(Pause, state.Displayed.ToArray());
        state.Reset();
        Assert.IsTrue(state.Propose(Play, ("widget", "modal"), 30));
        Assert.IsFalse(state.Commit(200));
    }
}
