using Microsoft.VisualStudio.TestTools.UnitTesting;
using WidgetRail.OverlayFrontend.WinUI.Presentation;
using WidgetRail.WidgetProtocol;

namespace WinUiShell.Tests;

[TestClass]
public sealed class ControllerGuideModelTests
{
    private static ViewNode Node(string id, ControllerButton button, string? label, bool disabled = false) =>
        new() { Id = id, Kind = ViewNodeKind.Button, IsDisabled = disabled,
            Shortcuts = [new(button, "action-" + id, Label: label)] };
    [TestMethod]
    public void ShellRecoveryOwnsHintsOnlyWhileInteractive()
    {
        IReadOnlyList<ControllerGuideHint> Capture() => throw new InvalidOperationException("Widget hints must not be consulted here.");
        Assert.IsNull(ControllerGuideModel.ResolveShellHints(false, true, true, false, Capture));
        var recovery = ControllerGuideModel.ResolveShellHints(true, true, true, false, Capture)!;
        Assert.AreEqual("Retry", recovery.Single().Label);
        Assert.AreEqual(ControllerButton.A, recovery.Single().Button);
        Assert.IsEmpty(ControllerGuideModel.ResolveShellHints(true, true, false, false, Capture)!);
        CollectionAssert.AreEqual(new[] { "Retry", "Back", "Close" }, ControllerGuideModel.WithHost(recovery).Select(h => h.Label).ToArray());
    }
    [TestMethod]
    public void TrayMenuOwnsHintsBeforeBackgroundRecovery()
    {
        var hints = ControllerGuideModel.ResolveShellHints(false, true, true, true,
            () => throw new InvalidOperationException("The tray menu owns input."))!;
        CollectionAssert.AreEqual(new[] { "Select", "Back", "Close" }, ControllerGuideModel.WithHost(hints).Select(h => h.Label).ToArray());
        Assert.AreEqual(ControllerButton.A, hints.Single().Button);
    }
    [TestMethod]
    public void ClosingShellContextRestoresCurrentWidgetHints()
    {
        ControllerGuideHint[] widget = [new(ControllerPrompt.Y, "Refresh", ControllerButton.Y)];
        Assert.AreSame(widget, ControllerGuideModel.ResolveShellHints(true, false, false, false, () => widget));
        Assert.IsNull(ControllerGuideModel.ResolveShellHints(false, false, false, false, () => widget));
    }
    [TestMethod]
    public void NearestShortcutAndDisabledOwnershipMatchDispatcher()
    {
        var parent = Node("parent", ControllerButton.X, "Parent");
        var child = Node("child", ControllerButton.X, "Child");
        Assert.AreEqual("Child", ControllerGuideModel.Resolve([parent, child], new HashSet<ControllerButton>(), false).Single().Label);
        Assert.IsEmpty(ControllerGuideModel.Resolve([parent, child with { IsDisabled = true }], new HashSet<ControllerButton>(), false));
        Assert.IsEmpty(ControllerGuideModel.Resolve([parent, child with { IsBusy = true }], new HashSet<ControllerButton>(), false));
    }
    [TestMethod]
    public void UnlabeledNearestOwnerNeverAdvertisesParent()
    {
        Assert.IsEmpty(ControllerGuideModel.Resolve([Node("parent", ControllerButton.Y, "Parent"),
            Node("child", ControllerButton.Y, null)], new HashSet<ControllerButton>(), false));
    }
    [TestMethod]
    public void ContextMenuOwnsButtonBeforeShortcut()
    {
        var hints = ControllerGuideModel.Resolve([Node("root", ControllerButton.X, "Refresh")],
            new HashSet<ControllerButton> { ControllerButton.X }, false);
        Assert.AreEqual("Options", hints.Single().Label);
    }
    [TestMethod]
    public void UnavailableContextMenuBlocksUnrelatedShortcut()
    {
        Assert.IsEmpty(ControllerGuideModel.Resolve([Node("root", ControllerButton.X, "Refresh")],
            new HashSet<ControllerButton>(), false, new HashSet<ControllerButton> { ControllerButton.X }));
    }
    [TestMethod]
    public void IndexedRowUsesSameLogicalAncestryAsAdmission()
    {
        var hints = ControllerGuideModel.Resolve([Node("scope", ControllerButton.LeftTrigger, "Previous"),
            new() { Id = "collection", Kind = ViewNodeKind.IndexedCollection }, Node("row", ControllerButton.X, "Game options")],
            new HashSet<ControllerButton>(), true);
        CollectionAssert.AreEqual(new[] { ControllerButton.X, ControllerButton.LeftTrigger, ControllerButton.A }, hints.Select(h => h.Button!.Value).ToArray());
    }
    [TestMethod]
    public void OnlyPressedShortcutsAreAdvertised()
    {
        var node = Node("root", ControllerButton.Y, "Refresh");
        node = node with { Shortcuts = [node.Shortcuts[0] with { Phase = ControllerEventPhase.Released }] };
        Assert.IsEmpty(ControllerGuideModel.Resolve([node], new HashSet<ControllerButton>(), false));
    }
    [TestMethod]
    public void LabelsUseAuthoredFallbackAndNormalizeWhitespace()
    {
        var node = Node("root", ControllerButton.Y, null) with { AccessibilityLabel = "  Refresh\n library\t " };
        Assert.AreEqual("Refresh library", ControllerGuideModel.Resolve([node], new HashSet<ControllerButton>(), false).Single().Label);
    }
    [TestMethod]
    public void HostBackAndCloseSurviveWhenNoWidgetHintFits()
    {
        var hints = ControllerGuideModel.WithHost([new(ControllerPrompt.X, "Long label", ControllerButton.X)]);
        CollectionAssert.AreEqual(new[] { 1, 2 }, ControllerGuideModel.Fit(hints, [500, 60, 70], 150, 10).ToArray());
    }
    [TestMethod]
    public void PairedSectionHintsFitAsUnitAndAllowSmallerLaterHint()
    {
        ControllerGuideHint[] hints = [new(ControllerPrompt.LeftTrigger, "Previous", Group: "triggers"),
            new(ControllerPrompt.RightTrigger, "Next", Group: "triggers"), new(ControllerPrompt.Y, "Refresh"),
            new(ControllerPrompt.B, "Back", Required: true)];
        CollectionAssert.AreEqual(new[] { 2, 3 }, ControllerGuideModel.Fit(hints, [80, 80, 40, 50], 150, 10).ToArray());
        CollectionAssert.AreEqual(new[] { 0, 1, 2, 3 }, ControllerGuideModel.Fit(hints, [80, 80, 40, 50], 280, 10).ToArray());
    }
    [TestMethod]
    public void ContextMenuDoesNotPairWithUnrelatedShoulderShortcut()
    {
        var hints = ControllerGuideModel.Resolve([Node("root", ControllerButton.LeftBumper, "Previous")], new HashSet<ControllerButton>(), false);
        Assert.AreEqual("bumpers", hints.Single().Group);
        Assert.AreEqual(ControllerPrompt.LeftStickPress, ControllerGuideModel.Prompt(ControllerButton.LeftStick));
    }
}
