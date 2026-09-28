using Microsoft.VisualStudio.TestTools.UnitTesting;
using WidgetRail.WidgetBridge;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

namespace WidgetRail.WidgetPresentationSession.Tests;

public sealed partial class EmbeddedMediaSessionTests
{
    private static EmbeddedMediaSession FullscreenMedia => Media with { SupportedPresentations = [MediaPresentationKind.OverlayFullscreen] };
    private static ViewNode FullscreenRoot => new() { Id = "root", Kind = ViewNodeKind.Stack,
        Children = [new() { Id = "fullscreen", Kind = ViewNodeKind.Button, Text = "Fullscreen", ActionId = WidgetPresentationSession.EnterMediaFullscreenAction }],
        Shortcuts = [new(ControllerButton.Y, WidgetPresentationSession.EnterMediaFullscreenAction)] };
    private static WidgetActionEvent FullscreenAction => new(WidgetPresentationSession.EnterMediaFullscreenAction, "fullscreen", InputScopeId: "root");

    [TestMethod]
    public async Task FullscreenAdmissionPreservesExactDocumentAcrossCompatiblePublications()
    {
        await Run(async channel =>
        {
            var request = await Read(channel); await Reply(channel, request.RequestId, BridgeMessageTypes.EmbeddedMediaSession, Bundle(FullscreenMedia));
            request = await Read(channel); await Snapshot(channel, request.RequestId, FullscreenMedia with { AspectRatio = 1 }, 2, root: FullscreenRoot);
        }, async (session, frame) =>
        {
            var document = await session.ResolveEmbeddedMediaAsync(frame.Authority);
            var receipt = session.EnterEmbeddedMediaFullscreen(frame, FullscreenAction, document);
            Assert.IsTrue(session.IsMediaPresentationCurrent(receipt));
            await session.EstablishPresentationAsync(session.GetTarget("media"), WidgetLifecycleState.Interactive);
            Assert.IsTrue(session.IsMediaPresentationCurrent(receipt));
            Assert.AreSame(document, receipt.Document);
            Assert.IsTrue(session.IsMediaPresentationCurrent(session.EnterEmbeddedMediaFullscreen(frame, FullscreenAction, document)));
            await session.DisposeAsync();
            Assert.IsFalse(session.IsMediaPresentationCurrent(receipt));
        }, FullscreenMedia, root: FullscreenRoot);
    }

    [TestMethod]
    [DataRow("capability")][DataRow("scope")][DataRow("document")]
    public async Task FullscreenReceiptsNeverResurrectAfterRetirement(string change)
    {
        await Run(async channel =>
        {
            var request = await Read(channel); await Reply(channel, request.RequestId, BridgeMessageTypes.EmbeddedMediaSession, Bundle(FullscreenMedia));
            request = await Read(channel);
            await Snapshot(channel, request.RequestId, change == "capability" ? Media : change == "document" ? null : FullscreenMedia,
                2, change == "scope" ? "other" : "root", change == "scope" ? null : FullscreenRoot);
            request = await Read(channel); await Snapshot(channel, request.RequestId, FullscreenMedia, 3, root: FullscreenRoot);
        }, async (session, frame) =>
        {
            var document = await session.ResolveEmbeddedMediaAsync(frame.Authority);
            var receipt = session.EnterEmbeddedMediaFullscreen(frame, FullscreenAction, document);
            await session.EstablishPresentationAsync(session.GetTarget("media"), WidgetLifecycleState.Interactive);
            Assert.IsFalse(session.IsMediaPresentationCurrent(receipt));
            var returned = await session.EstablishPresentationAsync(session.GetTarget("media"), WidgetLifecycleState.Interactive);
            Assert.IsFalse(session.IsMediaPresentationCurrent(receipt));
            Assert.Throws<WidgetPresentationSessionException>(() => session.EnterEmbeddedMediaFullscreen(frame, FullscreenAction, document));
            if (change != "document")
            {
                Assert.IsNotNull(session.GetEmbeddedMediaState(document));
                Assert.IsTrue(session.IsMediaPresentationCurrent(session.EnterEmbeddedMediaFullscreen(returned, FullscreenAction, document)));
            }
        }, FullscreenMedia, root: FullscreenRoot);
    }

    [TestMethod]
    [DataRow("clone")][DataRow("action")][DataRow("source")][DataRow("scope")][DataRow("release")]
    public async Task FullscreenRejectsForgedOrMismatchedDisplayedAction(string change)
    {
        await Run(async channel =>
        {
            var request = await Read(channel); await Reply(channel, request.RequestId, BridgeMessageTypes.EmbeddedMediaSession, Bundle(FullscreenMedia));
        }, async (session, frame) =>
        {
            var document = await session.ResolveEmbeddedMediaAsync(frame.Authority);
            var action = change switch
            {
                "action" => FullscreenAction with { ActionId = "play" },
                "source" => FullscreenAction with { SourceElementId = "root" },
                "scope" => FullscreenAction with { InputScopeId = "other" },
                "release" => FullscreenAction with { Phase = ControllerEventPhase.Released },
                _ => FullscreenAction,
            };
            Assert.Throws<WidgetPresentationSessionException>(() => session.EnterEmbeddedMediaFullscreen(change == "clone" ? frame with { } : frame, action, document));
        }, FullscreenMedia, root: FullscreenRoot);
    }

    [TestMethod]
    [DataRow("disabled")][DataRow("action")][DataRow("capability")]
    public async Task FullscreenRequiresCurrentAvailableBindingAndCapability(string change)
    {
        await Run(async channel =>
        {
            var request = await Read(channel); await Reply(channel, request.RequestId, BridgeMessageTypes.EmbeddedMediaSession, Bundle(FullscreenMedia));
            var node = FullscreenRoot.Children[0];
            request = await Read(channel); await Snapshot(channel, request.RequestId, change == "capability" ? Media : FullscreenMedia, 2,
                root: FullscreenRoot with { Children = [change == "disabled" ? node with { IsDisabled = true } :
                    change == "action" ? node with { ActionId = "different" } : node] });
        }, async (session, frame) =>
        {
            var document = await session.ResolveEmbeddedMediaAsync(frame.Authority);
            await session.EstablishPresentationAsync(session.GetTarget("media"), WidgetLifecycleState.Interactive);
            Assert.Throws<WidgetPresentationSessionException>(() => session.EnterEmbeddedMediaFullscreen(frame, FullscreenAction, document));
        }, FullscreenMedia, root: FullscreenRoot);
    }

    [TestMethod]
    public async Task HostShortcutUsesOrdinaryResolutionAndNeverSendsWorkerInput()
    {
        await Run(async channel =>
        {
            var request = await Read(channel); await Reply(channel, request.RequestId, BridgeMessageTypes.EmbeddedMediaSession, Bundle(FullscreenMedia));
        }, async (session, frame) =>
        {
            var document = await session.ResolveEmbeddedMediaAsync(frame.Authority);
            var input = new ControllerInputEvent(ControllerButton.Y, ControllerEventPhase.Pressed, ControllerInputContext.OpenWidget,
                "fullscreen", ActiveInputScopeId: "root", SnapshotSequence: frame.Authority.SnapshotSequence);
            var action = session.ResolveEmbeddedMediaFullscreenInput(frame, input);
            Assert.IsNotNull(action);
            Assert.AreEqual("root", action.SourceElementId);
            Assert.IsTrue(session.IsMediaPresentationCurrent(session.EnterEmbeddedMediaFullscreen(frame, action, document)));
            Assert.IsNull(session.ResolveEmbeddedMediaFullscreenInput(frame, input with { Button = ControllerButton.X }));
            Assert.IsNull(session.ResolveEmbeddedMediaFullscreenInput(frame, input with { Phase = ControllerEventPhase.Released }));
            Assert.Throws<WidgetPresentationSessionException>(() => session.ResolveEmbeddedMediaFullscreenInput(frame, input with { SnapshotSequence = 2 }));
        }, FullscreenMedia, root: FullscreenRoot);
    }
}
