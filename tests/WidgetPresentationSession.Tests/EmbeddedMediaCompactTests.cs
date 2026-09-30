using Microsoft.VisualStudio.TestTools.UnitTesting;
using WidgetRail.WidgetBridge;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

namespace WidgetRail.WidgetPresentationSession.Tests;

public sealed partial class EmbeddedMediaSessionTests
{
    [TestMethod]
    [DataRow("capability")][DataRow("scope")][DataRow("document")]
    public async Task CompactReceiptRejectsRetiredOriginAndNeverResurrects(string change)
    {
        var media = Media with { SupportedPresentations = [MediaPresentationKind.CompactPinned],
            Commands = [EmbeddedMediaCommand.TogglePlayback, EmbeddedMediaCommand.SeekBackward, EmbeddedMediaCommand.SeekForward] };
        await Run(async channel =>
        {
            var request = await Read(channel); await Reply(channel, request.RequestId, BridgeMessageTypes.EmbeddedMediaSession, Bundle(media));
            request = await Read(channel); await Snapshot(channel, request.RequestId, media with { AspectRatio = 1 }, 2);
            request = await Read(channel); await Snapshot(channel, request.RequestId,
                change == "document" ? null : change == "capability" ? Media : media, 3, scope: change == "scope" ? "other" : "root");
            request = await Read(channel); await Snapshot(channel, request.RequestId, media, 4);
        }, async (session, frame) =>
        {
            var document = await session.ResolveEmbeddedMediaAsync(frame.Authority);
            var receipt = session.EnterEmbeddedMediaCompact(frame, document);
            Assert.AreEqual(MediaPresentationKind.CompactPinned, receipt.Kind);
            Assert.AreSame(document, receipt.Document);
            Assert.Throws<WidgetPresentationSessionException>(() => session.EnterEmbeddedMediaCompact(frame with { }, document));
            await session.EstablishPresentationAsync(session.GetTarget("media"), WidgetLifecycleState.Interactive);
            Assert.IsTrue(session.IsMediaPresentationCurrent(receipt));
            await session.EstablishPresentationAsync(session.GetTarget("media"), WidgetLifecycleState.Interactive);
            Assert.IsFalse(session.IsMediaPresentationCurrent(receipt));
            await session.EstablishPresentationAsync(session.GetTarget("media"), WidgetLifecycleState.Interactive);
            Assert.IsFalse(session.IsMediaPresentationCurrent(receipt));
            Assert.Throws<WidgetPresentationSessionException>(() => session.EnterEmbeddedMediaCompact(frame, document));
        }, media);
    }
}
