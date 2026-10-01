using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WidgetRail.Samples.Browser;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

namespace WidgetRail.BrowserWidget.Tests;

[TestClass]
public sealed class BrowserWidgetTests
{
    [TestMethod]
    public async Task UserNavigationIsLazyBoundedAndNotReplayedByRendering()
    {
        var widget = WidgetTestHost.Attach(new Samples.Browser.BrowserWidget(), new WidgetTestHostServicesBuilder().Build());
        try
        {
            await WidgetTestHost.InitializeAsync(widget);
            await WidgetTestHost.SetLifecycleStateAsync(widget, WidgetLifecycleState.Visible);
            Assert.IsNull(Browser(widget.RenderSnapshot("browser.test", 1)));
            var request = WidgetIntentRequest.Create(WidgetIntentContracts.Web,
                JsonSerializer.SerializeToElement(new { url = "https://example.com/guide" }));
            Assert.AreEqual(WidgetIntentResult.Accepted, await widget.OnIntentAsync(request));
            var first = Browser(widget.RenderSnapshot("browser.test", 2))!;
            Assert.AreEqual("https://example.com/guide", first.Url);
            Assert.AreEqual(first, Browser(widget.RenderSnapshot("browser.test", 3)));
            Assert.AreEqual(WidgetIntentResult.Accepted, await widget.OnIntentAsync(request));
            var second = Browser(widget.RenderSnapshot("browser.test", 4))!;
            Assert.AreEqual(first.NavigationId + 1, second.NavigationId);
            Assert.AreEqual(first.Id, second.Id);
            Assert.AreEqual(WidgetIntentResult.Rejected, await widget.OnIntentAsync(request with
                { Payload = JsonSerializer.SerializeToElement(new { url = "file:///C:/unsafe" }) }));
            Assert.AreEqual(second, Browser(widget.RenderSnapshot("browser.test", 5)));
            await widget.OnActionAsync(new("browser.open", "browser.address") { CommittedText = "https://example.com/next" });
            Assert.AreEqual("https://example.com/next", Browser(widget.RenderSnapshot("browser.test", 6))!.Url);
        }
        finally { await WidgetTestHost.DestroyAsync(widget); }
    }

    [TestMethod]
    public void ManifestOptsOnlyItsWebMappingIntoPassiveDelivery()
    {
        var manifest = ManifestJson.Deserialize(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "manifest.json")));
        Assert.AreEqual(0, WidgetManifestValidator.Validate(manifest).Count);
        Assert.IsTrue(manifest.FullWidgetPinningSupported);
        var handler = manifest.Intents!.Handles.Single();
        Assert.IsTrue(handler.SupportsPassiveDelivery);
        Assert.AreEqual(CompiledWidgetIntentContract.Create(WidgetIntentContracts.Web).SchemaDigest,
            CompiledWidgetIntentContract.Create(handler).SchemaDigest);
    }

    private static WebBrowserDocument? Browser(ViewSnapshot snapshot) => Walk(snapshot.Root).SingleOrDefault(node => node.WebBrowser is not null)?.WebBrowser;
    private static IEnumerable<ViewNode> Walk(ViewNode node) => new[] { node }.Concat(node.Children.SelectMany(Walk));
}
