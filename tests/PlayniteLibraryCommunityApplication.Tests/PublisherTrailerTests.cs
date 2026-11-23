using System.Text;
using WidgetRail.Samples.PlayniteLibrary;

namespace PlayniteLibraryCommunityApplication.Tests;

[TestClass]
public sealed class PublisherTrailerTests
{
    private const string Name = "Hollow Knight: Silksong";
    private const string Id = "6XGeJwsUP9c";
    private const string Frame = """<iframe src="https://www.youtube-nocookie.com/embed/6XGeJwsUP9c" title="Hollow Knight: Silksong - Release Trailer"></iframe>""";

    [TestMethod]
    public void IdentifiesOneGameSpecificPublisherTrailer()
    {
        static string? Find(string html) => PlaynitePublisherTrailers.Find(Name, Encoding.UTF8.GetBytes(html));
        Assert.AreEqual(Id, Find(Frame));
        Assert.AreEqual(Id, Find($"<div data-html='{System.Net.WebUtility.HtmlEncode(Frame)}'></div>"));
        Assert.AreEqual(Id, Find(Frame + Frame));
        Assert.IsNull(Find(Frame + Frame.Replace(Id, "abcdefghijk")));
        Assert.IsNull(Find(Frame.Replace(Name, "Hollow Knight")));
        Assert.IsNull(Find(Frame.Replace(Name, Name + " Deluxe Edition")));
        Assert.IsNull(Find(Frame.Replace("Release Trailer", "Fan Trailer")));
        Assert.IsNull(Find(Frame.Replace("www.youtube-nocookie.com", "unknown.example")));
        Assert.AreEqual("https://game.example/", PlaynitePublisherTrailers.Website("http://game.example/")?.AbsoluteUri);
        Assert.IsNull(PlaynitePublisherTrailers.Website("file:///game.html"));
    }
}
