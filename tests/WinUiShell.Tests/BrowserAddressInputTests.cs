using Microsoft.VisualStudio.TestTools.UnitTesting;
using WidgetRail.OverlayFrontend.WinUI.Browser;

[TestClass]
public sealed class BrowserAddressInputTests
{
    [TestMethod]
    [DataRow("https://example.com/path?q=one#two", "https://example.com/path?q=one#two")]
    [DataRow(" http://localhost:8080/test ", "http://localhost:8080/test")]
    [DataRow("example.com/path?q=one#two", "https://example.com/path?q=one#two")]
    [DataRow("localhost:8080", "https://localhost:8080")]
    [DataRow("127.0.0.1:8080", "https://127.0.0.1:8080")]
    [DataRow("[::1]:8080", "https://[::1]:8080")]
    public void AddressesNavigate(string input, string expected) => Assert.AreEqual(expected, BrowserAddressInput.Resolve(input));

    [TestMethod]
    [DataRow("elden ring boss")]
    [DataRow("weather")]
    [DataRow("site:youtube.com elden ring")]
    [DataRow("boss tips & tricks #2 + more?")]
    [DataRow("日本語の攻略")]
    public void PlainTextSearchesPreserveQuery(string input) =>
        Assert.AreEqual("https://www.google.com/search?q=" + Uri.EscapeDataString(input), BrowserAddressInput.Resolve(" " + input + " "));

    [TestMethod]
    [DataRow("")]
    [DataRow("   ")]
    [DataRow("javascript:alert(1)")]
    [DataRow("file:///C:/private.txt")]
    [DataRow("data:text/html,test")]
    [DataRow("custom://launch")]
    [DataRow("https://")]
    [DataRow("https://user:password@example.com")]
    [DataRow("line\nline")]
    public void InvalidAddressesDoNotNavigateOrSearch(string input) => Assert.IsNull(BrowserAddressInput.Resolve(input));

    [TestMethod]
    public void LongEncodedSearchIsRejected() => Assert.IsNull(BrowserAddressInput.Resolve(new string('界', 300)));

    [TestMethod]
    public void InitialEditorIsEmptyButExistingAddressIsPreserved()
    {
        Assert.AreEqual(string.Empty, BrowserAddressInput.EditorValue("about:blank"));
        Assert.AreEqual("https://example.com/", BrowserAddressInput.EditorValue("https://example.com/"));
    }
}
