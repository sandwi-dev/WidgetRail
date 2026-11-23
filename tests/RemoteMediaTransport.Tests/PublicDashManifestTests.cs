using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WidgetRail.RemoteMediaTransport;

namespace WidgetRail.Tests.RemoteMediaTransport;

[TestClass]
public sealed class PublicDashManifestTests
{
    private static readonly Uri Origin = new("https://example.com/trailers/master.mpd");
    private const string Manifest = """
        <MPD xmlns="urn:mpeg:dash:schema:mpd:2011" type="static" mediaPresentationDuration="PT7S">
          <Period><AdaptationSet contentType="video"><Representation id="9" mimeType="video/mp4"
              codecs="avc1.640029" bandwidth="2100000" width="1280" height="720">
            <SegmentTemplate timescale="1" duration="3" initialization="av/init-$RepresentationID$.m4s"
                media="av/seg-$RepresentationID$-$Number%05d$.m4s" startNumber="1"/>
          </Representation></AdaptationSet></Period>
        </MPD>
        """;

    [TestMethod]
    public void NativeManifestHasOnlyKnownSyntheticResources()
    {
        var result = PublicDashManifest.Parse(Origin, Encoding.UTF8.GetBytes(Manifest));
        var document = Encoding.UTF8.GetString(result.Manifest);
        Assert.IsFalse(document.Contains("example.com") || document.Contains("av/"));
        Assert.IsTrue(result.TryResolve(new(PublicDashManifest.PlaybackUri, "r1/3.m4s"), out var source));
        Assert.AreEqual(new Uri(Origin, "av/seg-9-00003.m4s"), source);
        Assert.IsFalse(result.TryResolve(new(PublicDashManifest.PlaybackUri, "r1/4.m4s"), out _));
        Assert.IsFalse(result.TryResolve(new(Origin, "r1/1.m4s"), out _));
    }

    [TestMethod]
    [DataRow("av/init-", "https://another.example/init-")]
    [DataRow("av/seg-", "../seg-")]
    [DataRow("av/seg-", "%2e%2e/seg-")]
    [DataRow("PT7S", "PT24H")]
    [DataRow("1280", "8192")]
    [DataRow("avc1.640029", "unknown")]
    [DataRow("<Period>", "<BaseURL>https://another.example/</BaseURL><Period>")]
    [DataRow("<Period>", "<Period><ContentProtection/>")]
    public void UnsupportedSourcesAndOutOfBoundsTracksAreRejected(string from, string to) =>
        Assert.ThrowsExactly<InvalidDataException>(() => PublicDashManifest.Parse(Origin,
            Encoding.UTF8.GetBytes(Manifest.Replace(from, to))));
}
