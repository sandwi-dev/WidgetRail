using Microsoft.VisualStudio.TestTools.UnitTesting;
using WidgetRail.OverlayFrontend.WinUI.Browser;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.WinUiShell.Tests;

[TestClass]
public sealed class BrowserProfileIdentityTests
{
    private static WebBrowserDocument App(string key = "default") => new("app", 1, "https://example.com", "Example")
    {
        InteractionMode = BrowserInteractionMode.InteractOnFocus,
        Options = WebBrowserOptions.WebApp with { ProfileKey = key },
    };

    [TestMethod]
    public void IdentitySurvivesNavigationAndSeparatesWidgetsAccountsAndPrivateStorage()
    {
        var app = App();
        var name = BrowserProfileIdentity.Name("example.first", app);
        Assert.AreEqual(name, BrowserProfileIdentity.Name("example.first", app with { Id = "other", NavigationId = 7, Url = "https://example.com/chat" }));
        Assert.AreNotEqual(name, BrowserProfileIdentity.Name("example.second", app));
        Assert.AreNotEqual(name, BrowserProfileIdentity.Name("example.first", App("work")));
        Assert.AreNotEqual(BrowserProfileIdentity.LibraryFileName("example.first", app), BrowserProfileIdentity.LibraryFileName("example.first", App("work")));
        Assert.AreNotEqual(name, BrowserProfileIdentity.Name("example.first", app with { Options = app.Options with { Storage = BrowserStorage.Private } }));
        Assert.IsFalse(name.Contains("example.first", StringComparison.Ordinal));
        Assert.IsTrue(name.All(c => char.IsAsciiLetterOrDigit(c) || c == '-'));
    }

    [TestMethod]
    public void NavigationScopesCompareOriginsRatherThanUrlPrefixes()
    {
        var app = App();
        Assert.IsFalse(BrowserNavigationPolicy.OpenExternally(app, "https://EXAMPLE.com:443/messages"));
        Assert.IsTrue(BrowserNavigationPolicy.OpenExternally(app, "https://example.com.attacker.test/"));
        Assert.IsTrue(BrowserNavigationPolicy.OpenExternally(app, "https://example.com:8443/"));
        Assert.IsTrue(BrowserNavigationPolicy.OpenExternally(app, "http://example.com/"));
        Assert.IsFalse(BrowserNavigationPolicy.OpenExternally(app with { Options = new() }, "https://other.test"));
    }

    [TestMethod]
    public void ConsentIdentityOmitsPathsCredentialsAndUnicodeConfusables()
    {
        Assert.AreEqual("https://example.com", BrowserSiteIdentity.Origin("https://example.com:443/login?token=secret"));
        Assert.AreEqual("https://example.com:8443", BrowserSiteIdentity.Origin("https://example.com:8443/"));
        Assert.AreEqual("https://xn--bcher-kva.example", BrowserSiteIdentity.Origin("https://bücher.example/"));
        Assert.IsNull(BrowserSiteIdentity.Origin("https://user:secret@example.com"));
        Assert.IsNull(BrowserSiteIdentity.Origin("http://example.com"));
        Assert.IsNull(BrowserSiteIdentity.Origin("file:///C:/private"));
        Assert.AreEqual("https://[::1]", BrowserSiteIdentity.Origin("https://[::1]/"));
    }

    [TestMethod]
    public void StorageChangesRequireControllerReplacementButChromeChangesDoNot()
    {
        var app = App();
        Assert.IsTrue(BrowserProfileIdentity.SameProfile(app, app with { Options = app.Options with { Chrome = BrowserChrome.Toolbar } }));
        Assert.IsFalse(BrowserProfileIdentity.SameProfile(app, App("work")));
        Assert.IsFalse(BrowserProfileIdentity.SameProfile(app, app with { Options = app.Options with { Storage = BrowserStorage.Private } }));
    }
}
