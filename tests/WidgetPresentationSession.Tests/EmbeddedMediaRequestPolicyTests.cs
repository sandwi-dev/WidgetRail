using Microsoft.VisualStudio.TestTools.UnitTesting;
using WidgetRail.OverlayFrontend.WinUI.Media;
using WidgetRail.WidgetBridge;

namespace WidgetRail.WidgetPresentationSession.Tests;

public sealed partial class EmbeddedMediaSessionTests
{
    [TestMethod]
    public async Task DesktopHostIdentityAllowsPlayerApiWithoutBroadeningNetworkAuthority()
    {
        await Run(async channel =>
        {
            var request = await Read(channel);
            await Reply(channel, request.RequestId, BridgeMessageTypes.EmbeddedMediaSession, Bundle(Media));
        }, async (session, frame) =>
        {
            var document = await session.ResolveEmbeddedMediaAsync(frame.Authority);
            var policy = new EmbeddedMediaRequestPolicy(document, Media);
            Assert.AreEqual("https://" + typeof(EmbeddedMediaRequestPolicy).Assembly.GetName().Name!.ToLowerInvariant() + "/", policy.ApplicationReferer);
            Assert.IsTrue(policy.AllowsRemoteResource("https://www.youtube.com/iframe_api"));
            Assert.IsTrue(policy.AllowsRemoteResource("https://r1.googlevideo.com/video"));
            Assert.IsFalse(policy.AllowsFrame("https://r1.googlevideo.com/video"));
            foreach (var url in new[] { "http://www.youtube.com/iframe_api", "https://www.youtube.com.evil.test/iframe_api",
                "https://www.youtube.com@evil.test/", "https://www.youtube.com:444/iframe_api", "https://unlisted.test/script.js" })
                Assert.IsFalse(policy.AllowsRemoteResource(url), url);
            Assert.IsFalse(new EmbeddedMediaRequestPolicy(document, Media, null).AllowsRemoteResource("https://www.youtube.com/iframe_api"));
            Assert.IsNull(EmbeddedMediaRequestPolicy.CreateApplicationReferer("bad/identity"));
        });
    }
}
