using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using WidgetRail.OverlayPlatformClient;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

namespace WidgetRail.OverlayFrontend.WinUI.Shell;

internal sealed partial class OverlayShellPage
{
    // Explicit opt-in real-provider probe: cues the public iframe API demo video
    // and changes only that video's volume. It never starts playback.
    internal void EnableYouTubeVolumeValidation(string path)
    {
        Loaded += async (_, _) =>
        {
            var samples = new List<object>(); var requests = new List<object>();
            var start = Environment.TickCount64;
            try
            {
                if (startup is not null) await startup;
                await UnpinAsync(save: true);
                await SelectAsync("widgetrail.samples.youtube-video", true);
                await Until(() => surface?.CurrentBinding is not null);
                if (FindNode(surface!.CurrentBinding!.View.Root, "youtube.link") is null)
                    await Send("youtube.link.open");
                await Until(() => FindNode(surface!.CurrentBinding!.View.Root, "youtube.link") is not null);
                await Send("youtube.link.commit", "https://youtu.be/M7lc1UVf-VE");
                await Until(() => mediaOwner?.IsReady("widgetrail.samples.youtube-video") == true);
                await Until(() => Walk(surface!).OfType<WebView2>().Any());
                var browser = Walk(surface!).OfType<WebView2>().Single();
#pragma warning disable WUI4001 // Readiness is awaited above.
                var core = browser.CoreWebView2;
#pragma warning restore WUI4001
                await UntilAsync(async () => await core.ExecuteScriptAsync("player.getPlayerState() === 5") == "true");
                var slider = Walk(surface!).OfType<Slider>().Single(control => AutomationProperties.GetAutomationId(control) == "Widget.youtube.volume");
                await Until(() => slider.IsEnabled && !switching);
                var dispatch = surface!.DispatchActionAsync!;
                surface.DispatchActionAsync = request =>
                {
                    requests.Add(new { ms = Environment.TickCount64 - start, request.Action.ActionId, request.Action.RequestedValue });
                    return dispatch(request);
                };
                slider.Focus(FocusState.Keyboard);
                await RouteButtonAsync(ControllerButton.A, ControllerEventPhase.Pressed);
                await RouteButtonAsync(ControllerButton.A, ControllerEventPhase.Released);
                var input = ControllerFrame.Create(); input.Connected = 1;
                input.DpadNavigation.Direction = NavigationDirection.Left;
                input.DpadNavigation.Phase = NavigationPhase.Pressed;
                var prior = slider.Value; var reversed = false;
                for (var i = 0; i < 48; ++i)
                {
                    Receive(input);
                    var declaration = FindNode(surface.CurrentBinding!.View.Root, "youtube.volume")!;
                    samples.Add(new { ms = Environment.TickCount64 - start, value = slider.Value, slider.IsFocusEngaged,
                        declared = declaration.Value, declaration.IsBusy,
                        provider = await core.ExecuteScriptAsync("JSON.stringify({volume:player.getVolume(),state:player.getPlayerState()})") });
                    if (slider.Value > prior + .001) reversed = true;
                    prior = slider.Value;
                    await Task.Delay(i == 0 ? 360 : 125);
                    input.DpadNavigation.Phase = NavigationPhase.Repeated;
                }
                await RouteButtonAsync(ControllerButton.A, ControllerEventPhase.Pressed);
                await RouteButtonAsync(ControllerButton.A, ControllerEventPhase.Released);
                await Task.Delay(600);
                var final = await core.ExecuteScriptAsync("JSON.stringify({volume:player.getVolume(),state:player.getPlayerState()})");
                var volumePassed = !reversed && slider.Value < .001;
                await core.ExecuteScriptAsync("player.mute()");
                SetInteractive(false); FocusTray(); await Task.Delay(200);
                await RouteButtonAsync(ControllerButton.X, ControllerEventPhase.Pressed);
                await RouteButtonAsync(ControllerButton.X, ControllerEventPhase.Released);
                await UntilAsync(async () => await core.ExecuteScriptAsync("player.getPlayerState() === 1") == "true");
                if (interactive) throw new InvalidOperationException("Dashboard Play stole widget focus.");
                await RouteButtonAsync(ControllerButton.X, ControllerEventPhase.Pressed);
                await RouteButtonAsync(ControllerButton.X, ControllerEventPhase.Released);
                await UntilAsync(async () => await core.ExecuteScriptAsync("player.getPlayerState() === 2") == "true");
                await PinMediaAsync("widgetrail.samples.youtube-video");
                await Until(() => pinned?.Media is not null);
                await RouteButtonAsync(ControllerButton.X, ControllerEventPhase.Pressed);
                await RouteButtonAsync(ControllerButton.X, ControllerEventPhase.Released);
                await UntilAsync(async () => await core.ExecuteScriptAsync("player.getPlayerState() === 1") == "true");
                if (interactive || PinnedInputActive) throw new InvalidOperationException("Dashboard Play took input from the switcher.");
                File.WriteAllText(path, JsonSerializer.Serialize(new { passed = true, volumePassed, dashboardMainPassed = true,
                    dashboardPinnedPassed = true, samples, requests, final }));
            }
            catch (Exception error) { File.WriteAllText(path, JsonSerializer.Serialize(new { passed = false, samples, requests, error = error.ToString() })); }

            async Task Send(string action, string? text = null)
            {
                var frame = surface!.CurrentBinding!.Frame;
                var node = Nodes(frame.Snapshot.Root).First(value => value.ActionId == action);
                await InvokeAsync(new(frame, new WidgetActionEvent(action, node.Id, InputScopeId: frame.Snapshot.ActiveInputScopeId)
                    { CommittedText = text, FocusedElementId = node.Id }));
                await Task.Delay(250);
            }
            async Task Until(Func<bool> condition) => await UntilAsync(() => Task.FromResult(condition()));
            async Task UntilAsync(Func<Task<bool>> condition)
            {
                var until = Environment.TickCount64 + 25000;
                while (!await condition()) { if (Environment.TickCount64 > until) throw new TimeoutException("YouTube volume probe did not become ready."); await Task.Delay(50); }
            }
        };
        static IEnumerable<ViewNode> Nodes(ViewNode node) { yield return node; foreach (var child in node.Children) foreach (var value in Nodes(child)) yield return value; }
        static ViewNode? FindNode(ViewNode node, string id) => Nodes(node).FirstOrDefault(value => value.Id == id);
        static IEnumerable<DependencyObject> Walk(DependencyObject root)
        { yield return root; for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); ++i) foreach (var child in Walk(VisualTreeHelper.GetChild(root, i))) yield return child; }
    }
}
