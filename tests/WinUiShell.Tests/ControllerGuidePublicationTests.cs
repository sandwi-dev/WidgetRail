using Microsoft.VisualStudio.TestTools.UnitTesting;
using WidgetRail.OverlayFrontend.WinUI.Presentation;
using WidgetRail.WidgetProtocol;

namespace WinUiShell.Tests;

[TestClass]
public sealed class ControllerGuidePublicationTests
{
    private static readonly ControllerGuideHint Play = new(ControllerPrompt.A, "Play", ControllerButton.A);
    private static readonly ControllerGuideHint Pause = new(ControllerPrompt.A, "Pause", ControllerButton.A);
    private static readonly ControllerGuideHint Back = new(ControllerPrompt.B, "Back", ControllerButton.B, Required: true);
    private static readonly ControllerGuideHint Close = new(ControllerPrompt.Guide, "Close", Required: true);
    private static readonly ControllerGuideHint[] Host = [Back, Close];

    [TestMethod]
    public void PendingContextNeverPublishesByElapsedTimeAndReadyPublishesImmediately()
    {
        var state = new ControllerGuidePublication();
        state.Update("widget", false, [Play], Host, 0);
        AssertHints(state.Displayed, Back, Close);
        Assert.AreEqual(0, state.Remaining(500));
        Assert.IsFalse(state.Commit(500));
        AssertHints(state.Latest, Back, Close);
        Assert.IsTrue(state.Update("widget", true, [Play], Host, 1000));
        AssertHints(state.Displayed, Play, Back, Close);
        Assert.AreEqual(0, state.Remaining(1000));
    }

    [TestMethod]
    public void PendingSameContextRetainsDisplayedButCancelsCandidateAndStaleClicks()
    {
        var state = new ControllerGuidePublication();
        state.Update("widget", true, [Play], Host, 0);
        state.Update("widget", true, [Pause], Host, 10);
        state.Update("widget", false, [Pause], Host, 20);
        AssertHints(state.Displayed, Play, Back, Close);
        AssertHints(state.Latest, Back, Close);
        Assert.IsFalse(state.Latest.Contains(Play));
        Assert.IsFalse(state.Latest.Contains(Pause));
        Assert.IsFalse(state.Commit(1000));
        state.Update("widget", false, [], Host, 1010);
        AssertHints(state.Displayed, Play, Back, Close);
        Assert.IsTrue(state.Update("widget", true, [Pause], Host, 2000));
        AssertHints(state.Displayed, Pause, Back, Close);
        Assert.AreEqual(0, state.Remaining(2000));
    }

    [TestMethod]
    public void PendingNewOwnerRetainsCompleteGuideButSupersedesOldActions()
    {
        var state = new ControllerGuidePublication();
        state.Update(("widget-a", "root"), true, [Play], Host, 0);
        state.Update(("widget-a", "root"), true, [Pause], Host, 10);
        state.Update(("widget-b", "root"), false, [Pause], Host, 20);
        AssertHints(state.Displayed, Play, Back, Close);
        AssertHints(state.Latest, Back, Close);
        Assert.IsFalse(state.Commit(1000));
        state.Update(("widget-c", "root"), true, [], Host, 1010);
        AssertHints(state.Displayed, Back, Close);
        Assert.AreEqual(0, state.Remaining(1010));
    }

    [TestMethod]
    public void CompleteCandidateMustSettleWithoutPublishingMixedHostAndWidgetHints()
    {
        var state = new ControllerGuidePublication();
        var exit = Back with { Label = "Exit" };
        state.Update("widget", true, [Play], Host, 0);
        state.Update("widget", true, [Pause], Host, 10);
        Assert.IsFalse(state.Update("widget", true, [Pause], [exit, Close], 100));
        AssertHints(state.Displayed, Play, Back, Close);
        AssertHints(state.Latest, Pause, exit, Close);
        Assert.IsFalse(state.Commit(129));
        Assert.IsFalse(state.Commit(130));
        Assert.IsTrue(state.Commit(220));
        AssertHints(state.Displayed, Pause, exit, Close);
    }

    [TestMethod]
    public void PendingHostChangesAffectInputButCannotCreateMixedVisibleGuide()
    {
        var state = new ControllerGuidePublication();
        var menu = new ControllerGuideHint(ControllerPrompt.Menu, "Commands", ControllerButton.Menu);
        state.Update("widget", true, [Play], Host, 0);
        state.Update("widget", false, [], Host, 10);
        Assert.IsFalse(state.Update("widget", false, [], [menu, Back, Close], 20));
        AssertHints(state.Displayed, Play, Back, Close);
        AssertHints(state.Latest, menu, Back, Close);
    }

    [TestMethod]
    public void SameReadyContextUsesQuietPeriodAndLegitimateEmptyStateCommits()
    {
        var state = new ControllerGuidePublication();
        state.Update("widget", true, [Play], Host, 0);
        Assert.IsFalse(state.Update("widget", true, [], Host, 10));
        AssertHints(state.Latest, Back, Close);
        AssertHints(state.Displayed, Play, Back, Close);
        state.Update("widget", true, [], Host, 100);
        Assert.IsFalse(state.Commit(129));
        Assert.IsTrue(state.Commit(130));
        AssertHints(state.Displayed, Back, Close);
        Assert.IsFalse(state.Update("widget", true, [], Host, 200));
        Assert.AreEqual(0, state.Remaining(200));
    }

    [TestMethod]
    public void ContextualBackAndGuideOverrideDefaultsWithoutDuplicateButtonsOrPrompts()
    {
        var state = new ControllerGuidePublication();
        var back = Back with { Label = "Cancel" };
        var close = Close with { Label = "Hide" };
        state.Update("widget", true, [back, close, Play], [Back, Close, Pause], 0);
        AssertHints(state.Displayed, back, close, Play);
        AssertHints(state.Latest, back, close, Play);
        state.Update("widget", false, [], [Back, Close, Pause], 10);
        AssertHints(state.Latest, Back, Close, Pause);
    }

    [TestMethod]
    public void PendingRetainedLabelsCannotInvokeChangedHostActions()
    {
        var state = new ControllerGuidePublication();
        var cancel = Back with { Label = "Cancel edit" };
        var exit = Back with { Label = "Return to widget" };
        state.Update("widget", true, [Play, cancel], Host, 0);
        AssertHints(state.Displayed, Play, cancel, Close);
        state.Update("widget", false, [], [exit, Close], 10);
        AssertHints(state.Displayed, Play, cancel, Close);
        AssertHints(state.Latest, exit, Close);
        Assert.IsFalse(state.Latest.Contains(cancel));
        Assert.IsFalse(state.Latest.Contains(Play));
        state.Update("widget", true, [Play, cancel], Host, 20);
        AssertHints(state.Displayed, Play, cancel, Close);
    }

    [TestMethod]
    public void ResetDropsRetainedHintsAndOutstandingDeadline()
    {
        var state = new ControllerGuidePublication();
        state.Update("widget", true, [Play], Host, 0);
        state.Update("widget", true, [Pause], Host, 10);
        state.Reset();
        Assert.IsEmpty(state.Displayed);
        Assert.IsEmpty(state.Latest);
        Assert.AreEqual(0, state.Remaining(1000));
        Assert.IsFalse(state.Commit(1000));
        state.Update("widget", false, [], Host, 1010);
        AssertHints(state.Displayed, Back, Close);
        state.Update("widget", true, [Pause], Host, 1020);
        AssertHints(state.Displayed, Pause, Back, Close);
    }

    private static void AssertHints(IReadOnlyList<ControllerGuideHint> actual, params ControllerGuideHint[] expected) =>
        CollectionAssert.AreEqual(expected, actual.ToArray());
}
