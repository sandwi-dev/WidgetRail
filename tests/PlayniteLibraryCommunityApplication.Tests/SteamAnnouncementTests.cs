using System.Text;
using System.Xml;
using WidgetRail.Samples.PlayniteLibrary;

namespace PlayniteLibraryCommunityApplication.Tests;

[TestClass]
public sealed class SteamAnnouncementTests
{
    [TestMethod]
    public void ReaderKeepsOnlyThisGamesPostsAndConvertsMarkupToText()
    {
        var item = """
          <item><title>Release &amp; fixes</title><pubDate>Mon, 16 Mar 2026 03:24:05 +0000</pubDate>
          <link>https://store.steampowered.com/news/app/123/view/777</link>
          <description><![CDATA[<div>Patch notes</div><p>One &amp; two</p><script>ignore</script>
          <img src="https://example.com/tracker.png"><img src="https://shared.fastly.steamstatic.com/pic.jpg">]]></description></item>
          """;
        var feed = Parse("<rss><channel>" + item + item + item.Replace("/123/", "/999/") +
            item.Replace("/777", "/888?tracking=x") + "</channel></rss>");
        Assert.AreEqual(1, feed.Items.Count);
        var article = feed.Items.Single();
        Assert.AreEqual("Release & fixes", article.Title);
        StringAssert.Contains(article.Text, "One & two");
        Assert.IsFalse(article.Text.Contains("ignore"));
        Assert.AreEqual("https://shared.fastly.steamstatic.com/pic.jpg", article.Artwork);
        Assert.AreEqual("https://store.steampowered.com/news/app/123/view/777", feed.Original(article));
    }

    [TestMethod]
    public void EmptyFeedAndMalformedXmlRemainDistinct()
    {
        Assert.AreEqual(0, Parse("<rss><channel/></rss>").Items.Count);
        Assert.ThrowsExactly<XmlException>(() => Parse("<!DOCTYPE rss [<!ENTITY e 'external'>]><rss/>"));
    }

    private static SteamAnnouncements Parse(string xml) =>
        PlayniteLibraryApplicationService.ParseAnnouncements(123, Encoding.UTF8.GetBytes(xml));
}
