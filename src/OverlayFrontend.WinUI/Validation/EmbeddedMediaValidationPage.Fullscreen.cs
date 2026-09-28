using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using WidgetRail.WidgetPresentationSession;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;
using WidgetRail.OverlayFrontend.WinUI.Media;

namespace WidgetRail.OverlayFrontend.WinUI.Validation;

internal sealed partial class EmbeddedMediaValidationPage
{
    private async Task RunFullscreenOwnerChecksAsync()
    {
        var owner = mediaOwner!;
        var nativeBrowser = OwnerBrowser();
        var oldParent = nativeBrowser.Parent;
        var created = owner.BrowserCreationCount;
        var resolved = resolveCount;
        var sequence = declaration!.PendingCommand!.Sequence;
        var fullscreen = new MediaFullscreenView();
        viewport.Children.Add(fullscreen);
        void Changed() { if (owner.FullscreenState is { } state) fullscreen.Show(state.Declaration); else fullscreen.Hide(); }
        owner.FullscreenChanged += Changed;
        fullscreen.ExitRequested += () => owner.ExitFullscreen();
        fullscreen.CommandRequested += command => owner.DispatchFullscreen(command);
        owner.SetFullscreenHost(fullscreen.SurfaceHost);
        try
        {
            await Until(() => fullscreen.SurfaceHost.XamlRoot is not null);
            var action = new WidgetActionEvent(WidgetRail.WidgetPresentationSession.WidgetPresentationSession.EnterMediaFullscreenAction,
                "native.fullscreen", InputScopeId: "page");
            var refused = false;
            try { owner.EnterFullscreen(session!.GetState(Descriptor.Id)!.LastGood!, action); }
            catch (WidgetPresentationSessionException) { refused = true; }
            Check(refused && ReferenceEquals(nativeBrowser.Parent, oldParent), "fullscreen without declared capability cannot move resident media");
            declaration = declaration with { SupportedPresentations = (MediaPresentationKind[])[MediaPresentationKind.OverlayFullscreen] };
            var frame = await SnapshotAsync(); ownerPresenter!.Apply(frame);
            Check(owner.EnterFullscreen(frame, action), "genuine displayed host action admits resident fullscreen presentation");
            await Until(() => ReferenceEquals(nativeBrowser.Parent, fullscreen.SurfaceHost) && nativeBrowser.ActualWidth > 400);
            Check(owner.FullscreenWidgetId == Descriptor.Id && nativeBrowser.ActualWidth <= fullscreen.SurfaceHost.ActualWidth + 1 &&
                nativeBrowser.ActualHeight <= fullscreen.SurfaceHost.ActualHeight + 1 &&
                Math.Abs(nativeBrowser.ActualWidth / nativeBrowser.ActualHeight - declaration.AspectRatio) < .02,
                "fullscreen moves the same native browser into its bounded aspect-fit host viewport");
            owner.Refresh();
            Check(owner.BrowserCreationCount == created && resolveCount == resolved && declaration.PendingCommand!.Sequence == sequence,
                "fullscreen entry and refresh neither recreate resources nor issue package playback commands");
            var observationCount = events.Count;
            Check(owner.DispatchFullscreen(EmbeddedMediaHostCommand.TogglePlayback), "fullscreen host directly consumes playback input");
            await Until(() => events.Skip(observationCount).Any(value => value.SessionId == "owner-player" && value.CommandSequence == 0 && value.State == EmbeddedMediaPlaybackState.Paused));
            observationCount = events.Count;
            Check(owner.DispatchFullscreen(EmbeddedMediaHostCommand.TogglePlayback), "fullscreen resume enters the existing trusted activation handshake");
            await Until(() => events.Skip(observationCount).Any(value => value.SessionId == "owner-player" && value.CommandSequence == 0 && value.State == EmbeddedMediaPlaybackState.Playing));
            Check(declaration.PendingCommand!.Sequence == sequence, "host fullscreen playback controls leave the widget command lineage untouched");
            fullscreen.Width = 420;
            await Until(() => fullscreen.ActualWidth < 421 && fullscreen.SurfaceHost.ActualWidth <= 388);
            var guideButtons = Descendants(fullscreen).OfType<Button>().ToArray();
            Check(guideButtons.Length == 4 && guideButtons.All(button =>
            {
                var point = button.TransformToVisual(fullscreen).TransformPoint(new(0, 0));
                return button.ActualWidth > 0 && button.ActualHeight > 0 && point.X >= 15 &&
                    point.X + button.ActualWidth <= fullscreen.ActualWidth - 15 && point.Y >= 0 &&
                    point.Y + button.ActualHeight <= fullscreen.ActualHeight - 15;
            }), "fullscreen controls reflow within a narrow viewport without clipping labels or focus targets");
            fullscreen.Width = double.NaN;
            await Until(() => fullscreen.ActualWidth > 600);
            Check(guideButtons.All(button =>
            {
                var point = button.TransformToVisual(button.XamlRoot.Content).TransformPoint(new(0, 0));
                return point.X >= 0 && point.Y >= 0 && point.X + button.ActualWidth <= button.XamlRoot.Size.Width &&
                    point.Y + button.ActualHeight <= button.XamlRoot.Size.Height;
            }), "all fullscreen guide controls are inside the actual native window");
            fullscreen.Enter();
            Write(new { passed = true, phase = "owner-fullscreen", checks, diagnostics });
            await Task.Delay(1500);
            fullscreen.ActivateFocused();
            Check(owner.FullscreenWidgetId is null && !owner.ExitFullscreen() && ReferenceEquals(nativeBrowser.Parent, oldParent),
                "fullscreen exit is idempotent and returns the existing browser to its authored viewport");

            Check(owner.EnterFullscreen(frame, action), "same current declaration permits a new explicit fullscreen opening");
            declaration = declaration with { SupportedPresentations = [] };
            await SnapshotAsync(); // Do not update the presenter: session retirement wins independently.
            await Until(() => owner.FullscreenWidgetId is null);
            Check(owner.IsReady(Descriptor.Id) && owner.BrowserCreationCount == created,
                "capability removal exits fullscreen while preserving the admitted document and audio");
            declaration = declaration with { SupportedPresentations = (MediaPresentationKind[])[MediaPresentationKind.OverlayFullscreen] };
            frame = await SnapshotAsync(); ownerPresenter.Apply(frame);
            Check(owner.FullscreenWidgetId is null && owner.EnterFullscreen(frame, action), "returning capability does not reopen fullscreen without a fresh action");
            owner.SetHostState(Descriptor.Id, true, false);
            Check(owner.FullscreenWidgetId is null && owner.IsReady(Descriptor.Id), "foreground/input deactivation exits presentation without retiring media");
            owner.SetHostState(Descriptor.Id, true, true);
            Check(owner.EnterFullscreen(frame, action), "restored input can explicitly reenter fullscreen");
            owner.SetHostState(Descriptor.Id, false, false);
            Check(owner.FullscreenWidgetId is null && owner.IsParked(Descriptor.Id), "overlay hide exits fullscreen and parks the same browser");
            owner.SetHostState(Descriptor.Id, true, true);
            Check(ReferenceEquals(nativeBrowser.Parent, oldParent) && owner.BrowserCreationCount == created && resolveCount == resolved,
                "overlay reopen restores authored media geometry without fullscreen or playback replay");
        }
        finally
        {
            owner.SetFullscreenHost(null);
            owner.FullscreenChanged -= Changed;
            viewport.Children.Remove(fullscreen);
        }
    }
}
