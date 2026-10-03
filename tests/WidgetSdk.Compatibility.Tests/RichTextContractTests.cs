using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

namespace WidgetRail.WidgetSdk.Compatibility.Tests;

[TestClass]
public sealed class RichTextContractTests
{
    private static WidgetIntentRequest Intent(string url = "https://example.com/guide") => WidgetIntentRequest.Create(
        WidgetIntentContracts.Web, JsonSerializer.SerializeToElement(new { url }), WidgetIntentPresentation.PreferExistingSurface);

    [TestMethod]
    public void RichTextPreservesSpanOrderFocusAndVersionedIntentAuthority()
    {
        var snapshot = Snapshot(UI.RichText("paragraph", UI.Text("Look by the door.", "copy"), UI.InlineLink("¹", "source", Intent(), "Source 1")));
        Assert.AreEqual(ProtocolConstants.RichTextVersion, snapshot.ProtocolVersion);
        Assert.AreEqual(0, ViewSnapshotValidator.Validate(snapshot).Count);
        Assert.AreEqual(0, WinUiPresentationContract.Validate(snapshot).Count);
        var roundtrip = SnapshotJson.Deserialize(SnapshotJson.Serialize(snapshot));
        var paragraph = roundtrip.Root.Children.Single();
        Assert.AreEqual(ViewNodeKind.RichText, paragraph.Kind);
        CollectionAssert.AreEqual(new[] { "copy", "source" }, paragraph.Children.Select(node => node.Id).ToArray());
        Assert.IsFalse(paragraph.IsFocusable);
        Assert.IsTrue(paragraph.Children[1].IsFocusable);
        Assert.AreEqual("https://example.com/guide", paragraph.Children[1].Intent!.Payload.GetProperty("url").GetString());
        Assert.IsTrue(ViewSnapshotValidator.Validate(snapshot with { ProtocolVersion = 75 }).Count > 0);
    }

    [TestMethod]
    public void RichTextRejectsNestedLayoutsImagesAndNonIntentActions()
    {
        Assert.Throws<ArgumentException>(() => UI.RichText("rich", UI.Stack("nested", UI.Text("Text", "text"))));
        Assert.Throws<ArgumentException>(() => UI.RichText("rich", UI.Button("Run", "run", "button")));
        Assert.Throws<ProtocolValidationException>(() => Snapshot(UI.RichText("rich", UI.InlineLink("Open", "link", Intent()).Icon(WidgetGlyph.Connection))));
    }

    [TestMethod]
    public void RichTextOrdinaryTextAndButtonsKeepTheirExistingProtocolVersion()
    {
        var snapshot = Snapshot(UI.Text("Unchanged", "text"));
        Assert.IsTrue(snapshot.ProtocolVersion < ProtocolConstants.RichTextVersion);
        Assert.AreEqual(0, ViewSnapshotValidator.Validate(snapshot).Count);
    }

    private static ViewSnapshot Snapshot(WidgetElement child) => new Sample(child).RenderSnapshot("fixture", 1);
    private sealed class Sample(WidgetElement child) : Widget
    { public override WidgetView Render() => new(UI.Stack("root", child)); }
}
