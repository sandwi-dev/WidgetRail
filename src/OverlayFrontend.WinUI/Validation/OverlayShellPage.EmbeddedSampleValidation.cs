using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using WidgetRail.OverlayFrontend.WinUI.Media;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.OverlayFrontend.WinUI.Shell;

internal sealed partial class OverlayShellPage
{
    // Opt-in sealed local sample only. Never drives a remote provider or user media.
    internal void EnableEmbeddedSampleValidation(string path)
    {
        Loaded += async (_, _) =>
        {
            const string id = "widgetrail.samples.embedded-media";
            var checks = new List<string>();
            try
            {
                if (startup is not null) await startup;
                await UnpinAsync(save: true);
                await SelectAsync(id, true);
                await Until(() => mediaOwner?.IsReady(id) == true);
                var browser = Walk(surface!).OfType<WebView2>().Single();
#pragma warning disable WUI4001 // Owner readiness above awaits the sealed document's initialization.
                var core = browser.CoreWebView2;
#pragma warning restore WUI4001
                var creations = mediaOwner!.BrowserCreationCount;
                await Click("media-shell.play");
                await MediaUntil("!m.paused"); Check(true, "real sample Play starts local video through normalized A");
                await Click("media-shell.play");
                await MediaUntil("m.paused"); Check(true, "real sample Pause updates the same video");
                await Click("media-shell.seek-forward"); await MediaUntil("m.currentTime >= 1.9");
                Check(true, "real sample relative seek moves the paused video");
                await Click("media-shell.timeline.slider");
                surface!.MoveFocus(FocusNavigationDirection.Right);
                surface.MoveFocus(FocusNavigationDirection.Right);
                await RouteButtonAsync(ControllerButton.A, ControllerEventPhase.Pressed);
                await RouteButtonAsync(ControllerButton.A, ControllerEventPhase.Released);
                await MediaUntil("m.currentTime >= 5.9");
                await Task.Delay(500);
                await MediaUntil("m.currentTime >= 5.9");
                Check(true, "sample slider commits the final value without remote-update rollback");
                await Click("media-shell.rate"); await MediaUntil("m.playbackRate !== 1");
                await Click("media-shell.mute"); await MediaUntil("m.muted");
                await Click("media-shell.loop"); await MediaUntil("!m.loop");
                Check(true, "rate mute and loop commands reach the real adapter");
                await Click("media-shell.next"); await MediaUntil("m.currentSrc.endsWith('horizon.mp4')");
                await Click("media-shell.previous"); await MediaUntil("m.currentSrc.endsWith('sample.mp4')");
                Check(true, "previous and next load both sealed sample assets");
                var widgetExtent = new SurfaceExtent(WidgetSurface.Width, WidgetSurface.Height);
                var widgetResizeStarts = surface!.WidgetResizeStarts;
                var wideReveal = false;
                var visibilityToken = ProductionLayout.RegisterPropertyChangedCallback(VisibilityProperty, (_, _) =>
                {
                    if (ProductionLayout.Visibility == Visibility.Visible &&
                        (WidgetSurface.Width > widgetExtent.Width + 1 || WidgetSurface.Height > widgetExtent.Height + 1)) wideReveal = true;
                });
                await Click("media-shell.fullscreen"); await Until(() => IsMediaFullscreen);
                Check(Math.Abs(WidgetSurface.Width - widgetExtent.Width) < 1 && Math.Abs(WidgetSurface.Height - widgetExtent.Height) < 1,
                    "fullscreen keeps the retained widget at its authored dimensions on an ultrawide viewport");
                Check(ReferenceEquals(browser.Parent, fullscreenView.SurfaceHost), "fullscreen moves the existing browser");
                await CheckHeldSeek(ControllerButton.RightTrigger, "fullscreen RT");
                await CheckHeldSeek(ControllerButton.LeftTrigger, "fullscreen LT");
                await RouteButtonAsync(ControllerButton.B, ControllerEventPhase.Pressed);
                await RouteButtonAsync(ControllerButton.B, ControllerEventPhase.Released);
                await Until(() => !IsMediaFullscreen);
                ProductionLayout.UnregisterPropertyChangedCallback(VisibilityProperty, visibilityToken);
                Check(!wideReveal && Math.Abs(WidgetSurface.Width - widgetExtent.Width) < 1 &&
                    Math.Abs(WidgetSurface.Height - widgetExtent.Height) < 1,
                    "fullscreen return never exposes the widget at fullscreen dimensions");
                Check(surface.WidgetResizeStarts == widgetResizeStarts,
                    "fullscreen handoff does not start an ordinary widget resizing animation");
                Check(mediaOwner.BrowserCreationCount == creations, "fullscreen return retains one browser");
                await Click("media-shell.play"); await MediaUntil("!m.paused");
                await PinMediaAsync(id); await Until(() => pinned?.Media is not null);
                var pin = pinned!;
                Check(pin.IsCurrent && !pin.Window.Interactive && ReferenceEquals(browser.Parent, pin.MediaView!.SurfaceHost),
                    "production compact pin transfers the same browser into a passive peer");
                await MediaUntil("!m.paused");
                await Task.Delay(500); // Observe native first paint without activation or forced raster.
                await CheckPinReturn("media-shell.mute", ControllerButton.B);
                await CheckPinReturn("media-shell.timeline.slider", ControllerButton.View);
                WritePhase("compact-passive", pin.Window.Handle.ToInt64());
                await Task.Delay(1800);
                await SelectAsync("settings", true);
                await MediaUntil("!m.paused");
                Check(pin.IsCurrent && pin.Window.IsVisible && ReferenceEquals(browser.Parent, pin.MediaView!.SurfaceHost),
                    "pinned playback remains visible while switching widgets");
                await Until(() => Walk(surface!).OfType<Control>().Any(control =>
                    AutomationProperties.GetAutomationId(control) == "Widget.category.accessibility" && control.IsLoaded));
                var returnTarget = Walk(surface!).OfType<Control>().Single(control =>
                    AutomationProperties.GetAutomationId(control) == "Widget.category.accessibility");
                returnTarget.Focus(FocusState.Keyboard);
                await Until(() => ReferenceEquals(FocusManager.GetFocusedElement(XamlRoot), returnTarget));
                await RouteButtonAsync(ControllerButton.View, ControllerEventPhase.Pressed);
                await RouteButtonAsync(ControllerButton.View, ControllerEventPhase.Released);
                await Until(() => PinnedInputActive);
                var compactSize = pin.MediaView!.SurfaceHost.ActualSize;
                await Task.Delay(3300);
                Check(!pin.MediaView.ControlsVisible && pin.MediaView.SurfaceHost.ActualSize == compactSize,
                    "interactive compact controls auto-hide without reducing the video viewport");
                await RouteButtonAsync(ControllerButton.A, ControllerEventPhase.Pressed);
                await RouteButtonAsync(ControllerButton.A, ControllerEventPhase.Released);
                Check(PinnedInputActive && pin.MediaView.ControlsVisible,
                    "compact A reveals controls without invoking the hidden Back command");
                WritePhase("compact-interactive", pin.Window.Handle.ToInt64());
                await Task.Delay(1800);
                await RouteButtonAsync(ControllerButton.X, ControllerEventPhase.Pressed);
                await RouteButtonAsync(ControllerButton.X, ControllerEventPhase.Released);
                await MediaUntil("m.paused");
                await Until(() => !mediaOwner.CompactCommandPending && mediaOwner.CompactPlayback?.State == EmbeddedMediaPlaybackState.Paused);
                Check(true, "interactive pin X pauses the one resident document");
                await CheckHeldSeek(ControllerButton.RightTrigger, "compact RT");
                await CheckHeldSeek(ControllerButton.LeftTrigger, "compact LT");
                await RouteButtonAsync(ControllerButton.RightBumper, ControllerEventPhase.Pressed);
                await RouteButtonAsync(ControllerButton.RightBumper, ControllerEventPhase.Released);
                await MediaUntil("m.currentSrc.endsWith('horizon.mp4')");
                Check(true, "interactive pin bumper advances local media");
                await RouteButtonAsync(ControllerButton.B, ControllerEventPhase.Pressed);
                await RouteButtonAsync(ControllerButton.B, ControllerEventPhase.Released);
                await Until(() => !PinnedInputActive && foreground);
                Check(!pin.Window.Interactive, "B restores passive click-through pin interaction");
                await Until(() => interactive && ReferenceEquals(FocusManager.GetFocusedElement(XamlRoot), returnTarget));
                Check(true, "B from compact media restores the exact noninitial widget control that owned focus before View");
                WritePhase("compact-passive-return", pin.Window.Handle.ToInt64());
                await Task.Delay(1800);
                var bounds = pin.Window.Bounds;
                await BeginPinnedAdjustmentAsync(); StepPinnedPlacement(PinnedPlacementDirection.Left, false);
                Check(pin.Window.Bounds != bounds, "media pin reuses shared move controls");
                CancelPinnedAdjustment(); Check(pin.Window.Bounds == bounds, "media move cancellation restores exact bounds");
                await BeginPinnedAdjustmentAsync(opacityOnly: true); StepPinnedOpacity(PinnedPlacementDirection.Left);
                Check(pin.Window.OpacityPercent == 95, "media pin reuses opacity controls");
                CancelPinnedAdjustment();
                await SelectAsync(id, true); await UnpinAsync(save: true);
                await Until(() => pinned is null && Walk(surface!).OfType<WebView2>().Any());
                Check(ReferenceEquals(browser, Walk(surface!).OfType<WebView2>().Single()) && mediaOwner.BrowserCreationCount == creations,
                    "unpin returns the original browser before peer root destruction");
                for (var pass = 0; pass < 6; ++pass)
                {
                    await SelectAsync("settings", true); await SelectAsync(id, true);
                    await Until(() => mediaOwner.IsReady(id));
                }
                Check(mediaOwner.BrowserCreationCount == creations && validationFailure is null && !RecoveryVisible,
                    "six real widget round trips retain media without recovery or browser recreation");
                await Click("media-shell.back"); await Until(() => !interactive);
                Check(true, "sample Back button follows root Back into the configured switcher");
                await SelectAsync(id, true);
                Write(true, null);

                async Task MediaUntil(string predicate)
                {
                    var deadline = Environment.TickCount64 + 10000;
                    while (await core.ExecuteScriptAsync("(()=>{const m=document.getElementById('media');return !!m && (" + predicate + ");})()") != "true")
                    { if (Environment.TickCount64 > deadline) throw new TimeoutException("Sample media did not reach " + predicate); await Task.Delay(40); }
                }
                async Task CheckHeldSeek(ControllerButton button, string label)
                {
                    var forward = button == ControllerButton.RightTrigger;
                    await core.ExecuteScriptAsync("(()=>{const m=document.getElementById('media');m.pause();m.currentTime=" + (forward ? "0" : "10") + ";})()");
                    await Task.Delay(250);
                    var input = WidgetRail.OverlayPlatformClient.ControllerFrame.Create();
                    input.Connected = 1;
                    if (forward) { input.State.RightTrigger = 255; input.RightTriggerPressed = 1; }
                    else { input.State.LeftTrigger = 255; input.LeftTriggerPressed = 1; }
                    Receive(input);
                    input.LeftTriggerPressed = input.RightTriggerPressed = 0;
                    var end = Environment.TickCount64 + 800;
                    while (Environment.TickCount64 < end) { await Task.Delay(20); Receive(input); }
                    input.State.LeftTrigger = input.State.RightTrigger = 0;
                    if (forward) input.RightTriggerReleased = 1; else input.LeftTriggerReleased = 1;
                    Receive(input);
                    await MediaUntil(forward ? "m.currentTime >= 5.9" : "m.currentTime <= 4.1");
                    Check(!heldAction.Active, label + " repeats seek through normalized frames and retires on release");
                }
            }
            catch (Exception error) { Write(false, error.ToString()); }
            void Write(bool passed, string? error)
            { Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!); File.WriteAllText(path, JsonSerializer.Serialize(new { passed, checks, error })); }
            void WritePhase(string phase, long peerHwnd)
            {
                var media = pinned!.MediaView!;
                var state = new { phase, peerHwnd, checks, width = media.ActualWidth, height = media.ActualHeight,
                    hostWidth = media.SurfaceHost.ActualWidth, hostHeight = media.SurfaceHost.ActualHeight,
                    browserWidth = media.SurfaceHost.Children.OfType<FrameworkElement>().FirstOrDefault()?.ActualWidth,
                    browserHeight = media.SurfaceHost.Children.OfType<FrameworkElement>().FirstOrDefault()?.ActualHeight };
                File.WriteAllText(path, JsonSerializer.Serialize(state));
                File.WriteAllText(path + "." + phase + ".json", JsonSerializer.Serialize(state));
            }
            void Check(bool value, string name)
            { if (!value) throw new InvalidOperationException(name); checks.Add(name); }
            async Task Until(Func<bool> condition)
            {
                var deadline = Environment.TickCount64 + 15000;
                while (!condition())
                { if (validationFailure is not null) throw validationFailure; if (Environment.TickCount64 > deadline) throw new TimeoutException("Sample workflow did not settle"); await Task.Delay(25); }
            }
            async Task Click(string name)
            {
                await Until(() => Walk(surface!).OfType<Control>().Any(element => AutomationProperties.GetAutomationId(element) == "Widget." + name && element.IsLoaded));
                var control = Walk(surface!).OfType<Control>().Single(element => AutomationProperties.GetAutomationId(element) == "Widget." + name);
                control.Focus(FocusState.Keyboard);
                await RouteButtonAsync(ControllerButton.A, ControllerEventPhase.Pressed);
                await RouteButtonAsync(ControllerButton.A, ControllerEventPhase.Released);
                await Task.Delay(200);
            }
            async Task CheckPinReturn(string id, ControllerButton exit)
            {
                var target = Walk(surface!).OfType<Control>().Single(control => AutomationProperties.GetAutomationId(control) == "Widget." + id);
                target.Focus(FocusState.Keyboard);
                await Until(() => ReferenceEquals(FocusManager.GetFocusedElement(XamlRoot), target));
                await RouteButtonAsync(ControllerButton.View, ControllerEventPhase.Pressed);
                await RouteButtonAsync(ControllerButton.View, ControllerEventPhase.Released);
                await Until(() => PinnedInputActive);
                await RouteButtonAsync(exit, ControllerEventPhase.Pressed);
                // A queued duplicate press from the exiting gesture must not
                // become a new Back action in the window that just regained focus.
                await RouteButtonAsync(exit, ControllerEventPhase.Pressed);
                await RouteButtonAsync(exit, ControllerEventPhase.Released);
                Check(interactive && !PinnedInputActive, "the pin's complete " + exit + " gesture stays consumed after return");
                await Until(() => !PinnedInputActive && foreground && interactive && ReferenceEquals(FocusManager.GetFocusedElement(XamlRoot), target));
                Check(true, "same-widget pin " + exit + " restores " + id);
            }
        };
        static IEnumerable<DependencyObject> Walk(DependencyObject root)
        { yield return root; for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); ++i) foreach (var child in Walk(VisualTreeHelper.GetChild(root, i))) yield return child; }
    }
}
