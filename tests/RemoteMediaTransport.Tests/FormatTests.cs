using Microsoft.VisualStudio.TestTools.UnitTesting;
using WidgetRail.RemoteMediaTransport;

namespace WidgetRail.RemoteMediaTransport.Tests;

[TestClass]
public sealed class FormatTests
{
    [TestMethod]
    public void IdentificationUsesBytesRatherThanRemoteNames()
    {
        Assert.AreEqual("image/png", PublicMediaFormat.Identify(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }));
        Assert.AreEqual("image/gif", PublicMediaFormat.Identify("GIF89a"u8));
        Assert.AreEqual("image/jpeg", PublicMediaFormat.Identify(new byte[] { 255, 216, 255 }));
        Assert.AreEqual("image/webp", PublicMediaFormat.Identify("RIFFabcdWEBP"u8));
        Assert.AreEqual("video/mp4", PublicMediaFormat.Identify(new byte[] { 0, 0, 0, 32, 102, 116, 121, 112, 105, 115, 111, 109, 0, 0, 0, 0 }));
        Assert.AreEqual("video/webm", PublicMediaFormat.Identify(new byte[] { 0x1a, 0x45, 0xdf, 0xa3 }));
        Assert.AreEqual("audio/ogg", PublicMediaFormat.Identify("OggS"u8));
        Assert.AreEqual("audio/mpeg", PublicMediaFormat.Identify("ID3"u8));
        Assert.IsNull(PublicMediaFormat.Identify("#EXTM3U\nhttps://127.0.0.1/private"u8));
        Assert.IsNull(PublicMediaFormat.Identify("<svg><image href='file:///private'/></svg>"u8));
        Assert.IsNull(PublicMediaFormat.Identify("<html>sign in</html>"u8));
        Assert.IsNull(PublicMediaFormat.Identify("GIF89"u8));
        Assert.IsNull(PublicMediaFormat.Identify(new byte[] { 0, 0, 0, 8, 102, 116, 121, 112, 105, 115, 111, 109, 0, 0, 0, 0 }));
    }
}
