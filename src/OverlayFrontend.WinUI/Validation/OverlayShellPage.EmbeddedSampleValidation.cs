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
    internal void EnableEmbeddedSampleValidation(string path, bool fullscreenOnly = false)
    {
        Loaded += async (_, _) =>
        {
            const string id = "embedded-media-sample";
            var checks = new List<string>();
            try
            {
                if (startup is not null) await startup;
                await UnpinAsync(save: true);
                await SelectAsync(id, true);
                await Until(() => mediaOwner?.IsReady(id) == true);
                var browser = mediaOwner!.BrowserForValidation(id)!;
#pragma warning disable WUI4001 // Owner readiness above awaits the sealed document's initialization.
                var core = browser.CoreWebView2;
#pragma warning restore WUI4001
                var creations = mediaOwner!.BrowserCreationCount;
                if (Environment.GetCommandLineArgs().Contains("--embedded-sample-pin-only"))
                {
                    await Click("media-shell.play");
                    await MediaUntil("!m.paused");
                    await PinMediaAsync(id);
                    await Until(() => pinned?.Media is not null);
                    Check(pinned!.IsCurrent && ReferenceEquals(browser.Parent, pinned.MediaView!.SurfaceHost),
                        "Cold compact pin retains the original playing browser");
                    Check(pinned.Window.SurfaceBackground.Background is SolidColorBrush {Color.A:0},
                        "Compact media padding is transparent instead of theme-filled");
                    ImmediateHideRequested?.Invoke();
                    await Until(() => !App.Window.AppWindow.IsVisible);
                    await CapturePhase("compact-passive", pinned.Window.Handle.ToInt64());
                    ((MainWindow)App.Window).ShowOverlay();
                    await Until(() => foreground && MainFocusEnabled);
                    Write(true, null);
                    return;
                }
                Check(MediaFullscreenLayer.ValidateLetterboxedEndpoints(), "Letterboxed animation endpoints match video bounds in wide and tall containers");
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
                await Click("media-shell.play"); await MediaUntil("!m.paused");
                var inlineHost = Walk(surface!).OfType<WidgetMediaViewport>().Single().SurfaceHost;
                Check(Math.Abs(inlineHost.ActualWidth / inlineHost.ActualHeight - 16d / 9) < .01,
                    "Inline browser viewport keeps the declared video aspect ratio before expansion");
                var inlineVideo = MediaTransitionGeometry.Fit(fullscreenLayer.BoundsOf(inlineHost)!.Value, 16d / 9);
                var renderSize = browser.ActualSize;
                var renderParent = browser.Parent;
                var unloads = 0;
                void Unloaded(object sender, RoutedEventArgs args) { if (!browser.IsLoaded) ++unloads; }
                browser.Unloaded += Unloaded;
                await Click("media-shell.fullscreen");
                await CheckStableExternalTransition("Playing embedded video entrance");
                await Until(() => IsMediaFullscreen && !fullscreenLayer.IsTransitioning);
                Check(fullscreenLayer.MediaEndpointMatches(inlineVideo), "Embedded video animation meets the exact fitted inline rectangle");
                Check(Math.Abs(WidgetSurface.Width - widgetExtent.Width) < 1 && Math.Abs(WidgetSurface.Height - widgetExtent.Height) < 1,
                    "fullscreen keeps the retained widget at its authored dimensions on an ultrawide viewport");
                Check(ReferenceEquals(browser.Parent, renderParent), "fullscreen retains the attached browser in its resident layer");
                await CheckHeldSeek(ControllerButton.RightTrigger, "fullscreen RT");
                await CheckHeldSeek(ControllerButton.LeftTrigger, "fullscreen LT");
                await core.ExecuteScriptAsync("document.getElementById('media').play()");
                await MediaUntil("!m.paused");
                await RouteButtonAsync(ControllerButton.B, ControllerEventPhase.Pressed);
                await RouteButtonAsync(ControllerButton.B, ControllerEventPhase.Released);
                await CheckStableExternalTransition("Playing embedded video exit");
                await Until(() => !IsMediaFullscreen);
                await Task.Delay(1200);
                browser.Unloaded -= Unloaded;
                Check(unloads == 0 && browser.ActualSize == renderSize, "fullscreen does not unload or resize the live browser");
                Check(ReferenceEquals(browser.Parent, mediaOwner.MediaHostForValidation(id)) &&
                    mediaOwner.IsReady(id), "Returned playing WebView remains in its ordinary viewport after delayed observations");
                ProductionLayout.UnregisterPropertyChangedCallback(VisibilityProperty, visibilityToken);
                Check(!wideReveal && Math.Abs(WidgetSurface.Width - widgetExtent.Width) < 1 &&
                    Math.Abs(WidgetSurface.Height - widgetExtent.Height) < 1,
                    "fullscreen return never exposes the widget at fullscreen dimensions");
                Check(surface.WidgetResizeStarts == widgetResizeStarts,
                    "fullscreen handoff does not start an ordinary widget resizing animation");
                Check(mediaOwner.BrowserCreationCount == creations, "fullscreen return retains one browser");
                if (fullscreenOnly)
                {
                    await File.WriteAllTextAsync(path, JsonSerializer.Serialize(new { passed = true, checks, error = (string?)null }));
                    return;
                }
                // Inline Play/Pause were exercised above. Establish sustained
                // playback for the longer pin-transfer exercise, independently
                // of the earlier seek positions and Loop-off command.
                await core.ExecuteScriptAsync("(()=>{const m=document.getElementById('media');m.loop=true;m.currentTime=0;void m.play();adapter.observe();})()");
                await MediaUntil("!m.paused");
                await Until(() => !mediaOwner.CommandPendingForValidation(id) && Walk(surface!).OfType<Control>().Any(control =>
                    AutomationProperties.GetAutomationId(control) == "Widget.media-shell.play" && AutomationProperties.GetName(control) == "Pause local media"));
                await PinMediaAsync(id); await Until(() => pinned?.Media is not null);
                var pin = pinned!;
                Check(pin.Window.SurfaceBackground.Background is SolidColorBrush {Color.A:0},
                    "Compact media padding is transparent instead of theme-filled");
                Check(pin.IsCurrent && !pin.Window.Interactive && ReferenceEquals(browser.Parent, pin.MediaView!.SurfaceHost),
                    "production compact pin transfers the same browser into a passive peer");
                await Click("media-shell.manage");
                await Until(()=>PinnedAdjustmentActive&&ReferenceEquals(managedPin,pin));
                Check(!HostChoiceActive&&pinAdjustmentToolbar?.Window.IsVisible==true&&pin.Window.FocusIndicator.Visibility==Visibility.Visible,
                    "Caller-scoped SDK management adjusts its host-owned compact player directly and outlines it");
                NeutralForDialog();
                await RouteButtonAsync(ControllerButton.B,ControllerEventPhase.Pressed);
                await RouteButtonAsync(ControllerButton.B,ControllerEventPhase.Released);
                await Until(()=>!PinnedAdjustmentActive&&MainFocusEnabled);
                Check(ReferenceEquals(browser.Parent,pin.MediaView!.SurfaceHost)&&mediaOwner.BrowserCreationCount==creations,
                    "Cancelling media management preserves the resident player");
                await MediaUntil("!m.paused");
                await Task.Delay(500); // Observe native first paint without activation or forced raster.
                await CheckPinReturn("media-shell.mute", ControllerButton.B);
                await CheckPinReturn("media-shell.timeline.slider", ControllerButton.View);
                ImmediateHideRequested?.Invoke();
                await Until(() => !App.Window.AppWindow.IsVisible);
                await CapturePhase("compact-passive", pin.Window.Handle.ToInt64());
                ((MainWindow)App.Window).ShowOverlay();
                await Until(() => foreground && MainFocusEnabled);
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
                await CapturePhase("compact-interactive", pin.Window.Handle.ToInt64());
                await RouteButtonAsync(ControllerButton.X, ControllerEventPhase.Pressed);
                await RouteButtonAsync(ControllerButton.X, ControllerEventPhase.Released);
                await MediaUntil("m.paused");
                await Until(() => !mediaOwner.CompactCommandPending && mediaOwner.GetCompactPlayback(pin.Media!)?.State == EmbeddedMediaPlaybackState.Paused);
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
                ImmediateHideRequested?.Invoke();
                await Until(() => !App.Window.AppWindow.IsVisible);
                await CapturePhase("compact-passive-return", pin.Window.Handle.ToInt64());
                ((MainWindow)App.Window).ShowOverlay();
                await Until(() => foreground && MainFocusEnabled);
                var bounds = pin.Window.Bounds;
                await BeginPinnedAdjustmentAsync(); StepPinnedPlacement(PinnedPlacementDirection.Left, false);
                Check(pin.Window.Bounds != bounds, "media pin reuses shared move controls");
                CancelPinnedAdjustment(); Check(pin.Window.Bounds == bounds, "media move cancellation restores exact bounds");
                await BeginPinnedAdjustmentAsync(); StepPinnedOpacity(PinnedPlacementDirection.Left);
                Check(pin.Window.OpacityPercent == 95, "media pin reuses opacity controls");
                CancelPinnedAdjustment();
                await VerifyAdjustmentOutsideClicksAsync(pin,Check);
                await SelectAsync(id, true); await UnpinAsync(save: true);
                await Until(() => pinned is null && mediaOwner.BrowserForValidation(id) is not null);
                Check(ReferenceEquals(browser, mediaOwner.BrowserForValidation(id)) && mediaOwner.BrowserCreationCount == creations,
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
                if (catalogItems.Any(item => item.Id == "embedded-media-peer"))
                {
                    await PinMediaAsync(id);
                    var firstPin = pins.Single(item => item.WidgetId == id);
                    await SelectAsync("embedded-media-peer", true);
                    await Until(()=>mediaOwner.IsReady("embedded-media-peer"));
                    await Click("media-shell.manage");
                    await Until(()=>HostChoiceActive);
                    Check(managedPin is null&&hostChoiceDialog!.ChoiceIdsForValidation.Count==0&&firstPin.IsCurrent,
                        "A widget with no own pin cannot manage another widget's media pin");
                    NeutralForDialog();
                    await RouteButtonAsync(ControllerButton.B,ControllerEventPhase.Pressed);
                    await RouteButtonAsync(ControllerButton.B,ControllerEventPhase.Released);
                    await Until(()=>!HostChoiceActive&&MainFocusEnabled);
                    await Until(() => mediaOwner.IsReady("embedded-media-peer"));
                    var peerBrowser = mediaOwner.BrowserForValidation("embedded-media-peer")!;
#pragma warning disable WUI4001 // The isolated second sample has completed initialization above.
                    var peerCore = peerBrowser.CoreWebView2;
#pragma warning restore WUI4001
                    await core.ExecuteScriptAsync("(()=>{const m=document.getElementById('media');m.loop=true;m.currentTime=0;void m.play();})()");
                    await peerCore.ExecuteScriptAsync("(()=>{const m=document.getElementById('media');m.loop=true;m.currentTime=0;void m.play();})()");
                    await PinMediaAsync("embedded-media-peer");
                    await Until(() => pins.Count == 2 && pins.All(item => item.IsCurrent));
                    var secondPin = pins.Single(item => item.WidgetId == "embedded-media-peer");
                    await Task.Delay(1200);
                    Check(await core.ExecuteScriptAsync("document.getElementById('media').currentTime > 0.5") == "true" &&
                        await peerCore.ExecuteScriptAsync("document.getElementById('media').currentTime > 0.5") == "true",
                        "Two compact native media pins advance independently");
                    Check(ReferenceEquals(browser.Parent, firstPin.MediaView!.SurfaceHost) &&
                        ReferenceEquals(peerBrowser.Parent, secondPin.MediaView!.SurfaceHost),
                        "Each compact pin retains its own original browser");
                    await ClickPinForValidationAsync(secondPin,Check);
                    await Until(() => PinnedInputActive && ReferenceEquals(pinned, secondPin));
                    await RouteButtonAsync(ControllerButton.X, ControllerEventPhase.Pressed);
                    await RouteButtonAsync(ControllerButton.X, ControllerEventPhase.Released);
                    await Until(() => mediaOwner.GetCompactPlayback(secondPin.Media!)?.State == EmbeddedMediaPlaybackState.Paused);
                    Check(await core.ExecuteScriptAsync("!document.getElementById('media').paused") == "true",
                        "Pausing the selected media pin preserves playback in its neighbor");
                    await UnpinAsync(save: true, firstPin);
                    Check(secondPin.IsCurrent && PinnedInputActive && ReferenceEquals(pinned, secondPin) && secondPin.Window.Interactive,
                        "Closing a passive media pin preserves the selected player's native input ownership");
                    Check(ReferenceEquals(peerBrowser.Parent, secondPin.MediaView!.SurfaceHost),
                        "Closing a peer preserves the surviving compact browser");
                    await UnpinAsync(save: true, secondPin);
                    await Until(() => pins.Count == 0 && foreground);
                    Check(ReferenceEquals(browser, mediaOwner.BrowserForValidation(id)) &&
                        ReferenceEquals(peerBrowser, mediaOwner.BrowserForValidation("embedded-media-peer")),
                        "Both compact releases retain their own ordinary media sessions");
                }
                Write(true, null);

                async Task CheckStableExternalTransition(string label)
                {
                    await Task.Delay(100);
                    await MediaUntil("!m.paused");
                    Check(mediaOwner!.BrowserCreationCount == creations, label + " retains the playing WebView");
                }
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
            async Task CapturePhase(string phase, long peerHwnd)
            {
                var current = pinned!;
                var browser = mediaOwner!.BrowserForValidation(current.WidgetId)!;
#pragma warning disable WUI4001 // This phase follows the ready sample's completed placement.
                var core = browser.CoreWebView2;
#pragma warning restore WUI4001
                var video = await core.ExecuteScriptAsync("(()=>{const m=document.getElementById('media');return {time:m.currentTime,paused:m.paused,ready:m.readyState,width:m.videoWidth,height:m.videoHeight,frames:m.getVideoPlaybackQuality().totalVideoFrames,visibility:document.visibilityState,viewportWidth:innerWidth,viewportHeight:innerHeight};})()");
                await File.WriteAllTextAsync(path + "." + phase + ".media.json", JsonSerializer.Serialize(new {
                    video, rootVisible=browser.XamlRoot?.IsHostVisible, browserVisible=browser.Visibility.ToString(),
                    rootOpacity=current.Window.AutomationRoot.Opacity, browserOpacity=browser.Opacity,
                    browserCompositionOpacity=Microsoft.UI.Xaml.Hosting.ElementCompositionPreview.GetElementVisual(browser).Opacity,
                    browserBounds=browser.TransformToVisual(current.Window.AutomationRoot).TransformBounds(new(0,0,browser.ActualWidth,browser.ActualHeight)).ToString(),
                    windowOpacity=current.Window.OpacityPercent,
                    windowVisible=current.Window.IsVisible, mainVisible=App.Window.AppWindow.IsVisible }));
                WritePhase(phase, peerHwnd);
                if (Environment.GetCommandLineArgs().Contains("--embedded-sample-capture-ack"))
                    await Until(() => File.Exists(path + "." + phase + ".captured"));
                else await Task.Delay(1800);
            }
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
            void NeutralForDialog()
            {
                var frame=WidgetRail.OverlayPlatformClient.ControllerFrame.Create();frame.Connected=1;Receive(frame);
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
