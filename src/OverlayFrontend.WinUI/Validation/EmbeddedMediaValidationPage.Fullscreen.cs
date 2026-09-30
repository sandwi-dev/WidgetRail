using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using WidgetRail.WidgetPresentationSession;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;
using WidgetRail.OverlayFrontend.WinUI.Media;
using WidgetRail.OverlayFrontend.WinUI.Shell;
using WidgetRail.PlatformSettings;
using WidgetRail.WidgetBridge;

namespace WidgetRail.OverlayFrontend.WinUI.Validation;

internal sealed partial class EmbeddedMediaValidationPage
{
    private async Task FaultFullscreenOwnerAsync(WebView2 nativeBrowser,
        Action<Microsoft.Web.WebView2.Core.CoreWebView2>? injectFailure = null, string expectedFailure = "media-navigation-failed")
    {
        var owner = mediaOwner!;
        var created = owner.BrowserCreationCount;
        var resolved = resolveCount;
        var commands = events.Count(value => value.SessionId == "owner-player" && value.CommandSequence != 0);
        var fullscreen = new MediaFullscreenView();
        var exits = 0;
        var ordinaryRestoredBeforeFailure = false;
        viewport.Children.Add(fullscreen);
        void Changed()
        {
            if (owner.FullscreenState is { } state)
            {
                fullscreen.Show(state.Declaration);
                ownerPresenter!.Visibility = Visibility.Collapsed;
            }
            else
            {
                ++exits;
                fullscreen.Hide();
                ownerPresenter!.Visibility = Visibility.Visible;
                ordinaryRestoredBeforeFailure = owner.GetFailure(Descriptor.Id) is null;
            }
        }
        owner.FullscreenChanged += Changed;
        owner.SetFullscreenHost(fullscreen.SurfaceHost);
        try
        {
            await Until(() => fullscreen.SurfaceHost.XamlRoot is not null);
            var frame = session!.GetState(Descriptor.Id)!.LastGood!;
            var action = new WidgetActionEvent(WidgetRail.WidgetPresentationSession.WidgetPresentationSession.EnterMediaFullscreenAction,
                "native.fullscreen", InputScopeId: "page");
            Check(owner.EnterFullscreen(frame, action), "fault regression enters fullscreen on the existing admitted browser");
            await Until(() => ReferenceEquals(nativeBrowser.Parent, fullscreen.SurfaceHost) && nativeBrowser.ActualWidth > 0);
#pragma warning disable WUI4001 // EmbeddedMediaSurface owns EnsureCoreWebView2Async; the owner is ready above.
            if (injectFailure is null) nativeBrowser.CoreWebView2.Navigate("https://wrail-forbidden.invalid/");
            else injectFailure(nativeBrowser.CoreWebView2);
#pragma warning restore WUI4001
            await Until(() => owner.GetFailure(Descriptor.Id) is not null);
            Check(owner.FullscreenWidgetId is null && exits == 1 && fullscreen.Visibility == Visibility.Collapsed &&
                ownerPresenter!.Visibility == Visibility.Visible && ordinaryRestoredBeforeFailure,
                "native fullscreen fault exits once and restores ordinary recovery placement before publishing failure");
            Check(session.GetState(Descriptor.Id)!.LastGood!.Snapshot.EmbeddedMediaSession == frame.Snapshot.EmbeddedMediaSession &&
                session.GetState(Descriptor.Id)!.Failure is null,
                "browser fault exits fullscreen independently of the still-current widget document declaration");
            owner.Refresh();
            await Task.Delay(150);
            Check(exits == 1 && owner.GetFailure(Descriptor.Id) == expectedFailure &&
                owner.BrowserCreationCount == created && resolveCount == resolved &&
                events.Count(value => value.SessionId == "owner-player" && value.CommandSequence != 0) == commands,
                "fault refresh preserves the failure without repeated fullscreen exit, browser admission, or command replay");
        }
        finally
        {
            owner.SetFullscreenHost(null);
            owner.FullscreenChanged -= Changed;
            ownerPresenter!.Visibility = Visibility.Visible;
            viewport.Children.Remove(fullscreen);
        }
    }

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
            await Until(() => fullscreen.ActualWidth < 421 && fullscreen.SurfaceHost.ActualWidth <= 420);
            fullscreen.Enter();
            var guideButtons = Descendants(fullscreen).OfType<Button>()
                .Where(button => !Microsoft.UI.Xaml.Automation.AutomationProperties.GetAutomationId(button).EndsWith(".VideoFocus", StringComparison.Ordinal)).ToArray();
            Check(guideButtons.Length == 4 && guideButtons.All(button =>
            {
                var point = button.TransformToVisual(fullscreen).TransformPoint(new(0, 0));
                return button.ActualWidth > 0 && button.ActualHeight > 0 && point.X >= 0 &&
                    point.X + button.ActualWidth <= fullscreen.ActualWidth && point.Y >= 0 &&
                    point.Y + button.ActualHeight <= fullscreen.ActualHeight;
            }), "fullscreen controls reflow within a narrow viewport without clipping labels or focus targets");
            fullscreen.Width = double.NaN;
            await Until(() => fullscreen.ActualWidth > 600);
            Check(guideButtons.All(button =>
            {
                var point = button.TransformToVisual(button.XamlRoot.Content).TransformPoint(new(0, 0));
                return point.X >= 0 && point.Y >= 0 && point.X + button.ActualWidth <= button.XamlRoot.Size.Width &&
                    point.Y + button.ActualHeight <= button.XamlRoot.Size.Height;
            }), "all fullscreen guide controls are inside the actual native window");
            await CheckFullscreenAppearanceAsync(fullscreen);
            fullscreen.Enter();
            var videoSize = fullscreen.SurfaceHost.ActualSize;
            await Task.Delay(3300);
            Check(!fullscreen.ControlsVisible && fullscreen.SurfaceHost.ActualSize == videoSize,
                "idle fullscreen controls hide without resizing the video");
            Check(fullscreen.Background is SolidColorBrush { Color.A: 0 }, "fullscreen outside the video stays transparent");
            fullscreen.RevealControls(); // Pointer/transport can reveal chrome while focus stays on video.
            fullscreen.ActivateFocused();
            Check(fullscreen.ControlsVisible && owner.FullscreenWidgetId is not null,
                "A restores control focus after reveal without executing the remembered Back command");
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
            var fullscreenParent = nativeBrowser.Parent;
            owner.SetHostState(Descriptor.Id, true, false, retainFullscreenDuringExit: true);
            Check(owner.FullscreenWidgetId == Descriptor.Id && ReferenceEquals(nativeBrowser.Parent, fullscreenParent) &&
                !owner.DispatchFullscreen(EmbeddedMediaHostCommand.TogglePlayback),
                "overlay exit retains fullscreen pixels at their current parent while revoking transport input");
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

    private async Task CheckFullscreenAppearanceAsync(MediaFullscreenView fullscreen)
    {
        using var chrome = new ShellChromeStyles();
        fullscreen.RegisterChrome(chrome);
        var palette = new Dictionary<string, BridgeNodeRenderStyles>
        {
            ["title"] = Typography(24),
            ["body"] = Typography(16),
            ["controller-glyph"] = Typography(28),
        };
        var text = Descendants(fullscreen).OfType<TextBlock>().First(value => value.Text == "Play / Pause");
        var symbols = Descendants(fullscreen).OfType<FontIcon>().ToArray();
        try
        {
            chrome.Update(palette, AppearanceSettings.Default with { TextScale = 1.5, BoldText = true }, false);
            await Until(() => Math.Abs(text.FontSize - 24) < .1 && text.FontWeight.Weight >= 600);
            Check(symbols.Length == 4 && symbols.All(symbol => symbol.FontSize >= 28 && symbol.FontFamily.Source.Contains("Kenney")),
                "fullscreen uses shell controller-glyph sizing while retaining semantic controller fonts");
            Check(text.FontWeight.Weight >= 600 && Math.Abs(text.FontSize - 24) < .1,
                "fullscreen native labels honor text scale and Bold Text through the shared shell adapter");
            chrome.Update(palette, AppearanceSettings.Default with { TextScale = 1, BoldText = false }, false);
            await Until(() => Math.Abs(text.FontSize - 16) < .1 && text.FontWeight.Weight < 600);
            Check(true, "fullscreen accessibility updates restore base typography without recreating controls or media");
        }
        finally { chrome.Update(palette, AppearanceSettings.Default, true); }
        static BridgeNodeRenderStyles Typography(int size)
        {
            var values = new Dictionary<string, BridgeComputedStyleValue>
            {
                ["font-size"] = new() { Kind = WidgetRail.WidgetStyling.WrssValueKind.Number,
                    Number = size, Text = size.ToString(System.Globalization.CultureInfo.InvariantCulture) },
            };
            return new() { Base = values, Focused = values, Pressed = values };
        }
    }
}
