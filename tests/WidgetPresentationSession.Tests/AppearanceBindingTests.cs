using Microsoft.VisualStudio.TestTools.UnitTesting;
using WidgetRail.OverlayFrontend.WinUI.Presentation;
using WidgetRail.WidgetBridge;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.WidgetPresentationSession.Tests;

[TestClass]
public sealed class AppearanceBindingTests
{
    [TestMethod]
    public void AppearanceAdmissionRequiresExactDeclarationsAndUnchangedSemanticAuthority()
    {
        var descriptor = new BridgeWidgetDescriptor { Id = "theme", Name = "Theme", InstanceId = "theme.instance",
            RuntimeGeneration = "runtime", PresentationGeneration = "presentation", Icon = WidgetGlyph.Connection, PackageContentDigest = "" };
        var snapshot = new ViewSnapshot { WidgetInstanceId = descriptor.InstanceId, Sequence = 1, ActiveInputScopeId = "root",
            Root = new() { Id = "root", Kind = ViewNodeKind.Stack } };
        var frame = new WidgetPresentationFrame(new(descriptor.Id, descriptor.RuntimeGeneration, descriptor.PresentationGeneration,
            1, descriptor.InstanceId, snapshot.Sequence, snapshot.ActiveInputScopeId), descriptor, snapshot, new Dictionary<string, BridgeNodeRenderStyles>());
        var before = WidgetPresentationBinding.ForMain(frame);
        var styled = frame with { AppearanceRevision = 1 };
        Assert.IsTrue(WidgetPresentationBinding.ForMain(styled).IsAppearanceUpdateOf(before));
        Assert.IsFalse(before.IsAppearanceUpdateOf(before));
        Assert.IsFalse(WidgetPresentationBinding.ForMain(styled with { Snapshot = snapshot with { } }).IsAppearanceUpdateOf(before));
        Assert.IsFalse(WidgetPresentationBinding.ForMain(styled with { Authority = frame.Authority with { SnapshotSequence = 2 } }).IsAppearanceUpdateOf(before));
        Assert.IsFalse(WidgetPresentationBinding.ForMain(styled with { Authority = frame.Authority with { ActiveInputScopeId = "dialog" } }).IsAppearanceUpdateOf(before));
        Assert.IsFalse(WidgetPresentationBinding.ForMain(styled with { Authority = frame.Authority with { SessionGeneration = 2 } }).IsAppearanceUpdateOf(before));
    }
}
