using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using WidgetRail.OverlayFrontend.WinUI.Media;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

namespace WidgetRail.OverlayFrontend.WinUI.Shell;

internal sealed partial class OverlayShellPage
{
    // Explicit real-provider opt-in, excluded from shipping. Use an isolated profile.
    internal void EnableYouTubeFullscreenValidation(string path)
    {
        Loaded += async (_, _) =>
        {
            const string id = "widgetrail.samples.youtube-video";
            var checks = new List<string>(); var frames = new List<object>();
            var phase = "loading"; var start = Environment.TickCount64;
            WebView2? browser = null;
            var unloads = 0;
            long visibilityToken = 0;
            void Lifetime(object sender, RoutedEventArgs args)
            {
                if (browser?.IsLoaded == false) ++unloads;
                frames.Add(new { ms = Environment.TickCount64 - start, phase,
                    lifetime = "layout", loaded = browser?.IsLoaded, visibility = browser?.Visibility.ToString() });
            }
            void Sample(object? sender, object args)
            {
                if (browser is not { IsLoaded: true }) return;
                var bounds = browser.TransformToVisual(this).TransformBounds(new(0, 0, browser.ActualWidth, browser.ActualHeight));
                var visual = ElementCompositionPreview.GetElementVisual(fullscreenView);
                frames.Add(new { ms = Environment.TickCount64 - start, phase, IsMediaFullscreen, fullscreenLayer.IsTransitioning,
                    parent = IsMediaFullscreen ? "fullscreen" : "inline",
                    width = browser.ActualWidth, height = browser.ActualHeight,
                    x = bounds.X, y = bounds.Y, transformedWidth = bounds.Width, transformedHeight = bounds.Height,
                    scale = visual.Scale.X, translation = visual.Offset.X,
                    widgetWidth = WidgetSurface.ActualWidth, widgetHeight = WidgetSurface.ActualHeight });
            }
            try
            {
                if (startup is not null) await startup;
                await SelectAsync(id, true);
                await Until(() => surface?.CurrentBinding is not null);
                if (!Nodes(surface!.CurrentBinding!.Frame.Snapshot.Root).Any(n => n.Id == "youtube.link"))
                    await Send("youtube.link.open");
                await Send("youtube.link.commit", "https://youtu.be/M7lc1UVf-VE");
                await Until(() => mediaOwner?.IsReady(id) == true && mediaOwner.BrowserForValidation(id) is not null);
                browser = mediaOwner!.BrowserForValidation(id)!;
                browser.Loaded += Lifetime; browser.Unloaded += Lifetime;
                visibilityToken = browser.RegisterPropertyChangedCallback(VisibilityProperty, (_, _) => frames.Add(new { ms = Environment.TickCount64 - start,
                    phase, lifetime = "visibility", visibility = browser.Visibility.ToString() }));
#pragma warning disable WUI4001 // Production owner readiness awaited above.
                var core = browser.CoreWebView2;
#pragma warning restore WUI4001
                await UntilAsync(async () => await core.ExecuteScriptAsync("!!player && player.getPlayerState() === 5") == "true");
                await core.ExecuteScriptAsync("player.mute()");
                await Toggle();
                await UntilAsync(async () => await core.ExecuteScriptAsync("player.getPlayerState() === 1") == "true");
                await Task.Delay(1000);
                await Toggle();
                await UntilAsync(async () => await core.ExecuteScriptAsync("player.getPlayerState() === 2") == "true");
                var creations = mediaOwner!.BrowserCreationCount;
                var renderSize = browser.ActualSize;
                var parent = browser.Parent;
                File.WriteAllText(path + ".ready", "ready");
                await Until(() => File.Exists(path + ".go"));
                start = Environment.TickCount64;
                CompositionTarget.Rendering += Sample;
                await Task.Delay(700);
                for (var cycle = 0; cycle < 4; ++cycle)
                {
                    if (cycle == 2) { await Toggle(); await UntilAsync(async () => await core.ExecuteScriptAsync("player.getPlayerState() === 1") == "true"); }
                    phase = $"{cycle}-enter";
                    await Send("host.embeddedMediaSession.enterFullscreen");
                    await Until(() => IsMediaFullscreen && !fullscreenLayer.IsTransitioning);
                    checks.Add($"Cycle {cycle}: fullscreen settles");
                    phase = $"{cycle}-full"; await Task.Delay(1000);
                    phase = $"{cycle}-exit"; await Button(ControllerButton.B);
                    await Until(() => !IsMediaFullscreen && !fullscreenLayer.IsTransitioning);
                    phase = $"{cycle}-inline"; await Task.Delay(1000);
                    if (mediaOwner.BrowserCreationCount != creations || !ReferenceEquals(browser.Parent, parent) ||
                        browser.ActualSize != renderSize || unloads != 0)
                        throw new InvalidOperationException("Fullscreen changed the browser attachment or render viewport.");
                    checks.Add($"Cycle {cycle}: original browser stays attached at one render size without unloads");
                    var slot = Walk(surface!).OfType<WidgetMediaViewport>().Single().SurfaceHost;
                    var expected = fullscreenLayer.BoundsOf(slot)!.Value;
                    var actual = fullscreenLayer.BoundsOf(browser)!.Value;
                    if (Math.Abs(expected.X - actual.X) > 1 || Math.Abs(expected.Y - actual.Y) > 1 ||
                        Math.Abs(expected.Width - actual.Width) > 1 || Math.Abs(expected.Height - actual.Height) > 1)
                        throw new InvalidOperationException("Returned pixels do not match the authored slot.");
                    checks.Add($"Cycle {cycle}: returned video fits the widget's slot");
                }
                await core.ExecuteScriptAsync("player.pauseVideo()");
                var inlineSlot = Walk(surface!).OfType<WidgetMediaViewport>().Single().SurfaceHost;
                var oldClip = inlineSlot.Clip;
                var oldTransform = inlineSlot.ReadLocalValue(RenderTransformProperty);
                try
                {
                    phase = "clip-half";
                    inlineSlot.Clip = new RectangleGeometry { Rect = new(0, 0, inlineSlot.ActualWidth / 2, inlineSlot.ActualHeight) };
                    await Task.Delay(450);
                    var stage = (EmbeddedMediaStage)((FrameworkElement)browser.Parent).Parent;
                    var clipped = stage.TransformToVisual(this).TransformBounds(((RectangleGeometry)stage.Clip).Rect);
                    if (Math.Abs(clipped.Width - inlineSlot.ActualWidth / 2) > 1) throw new InvalidOperationException("Resident video does not follow slot clipping.");
                    checks.Add("Resident pixels follow the authored slot's clip");
                    phase = "move-slot";
                    inlineSlot.RenderTransform = new TranslateTransform { Y = 24 };
                    await Task.Delay(450);
                    var expected = fullscreenLayer.BoundsOf(inlineSlot)!.Value;
                    var actual = fullscreenLayer.BoundsOf(browser)!.Value;
                    if (Math.Abs(expected.Y - actual.Y) > 1) throw new InvalidOperationException("Resident video does not follow slot movement.");
                    checks.Add("Resident pixels follow slot movement without native reattachment");
                }
                finally
                {
                    inlineSlot.Clip = oldClip;
                    if (ReferenceEquals(oldTransform, DependencyProperty.UnsetValue)) inlineSlot.ClearValue(RenderTransformProperty);
                    else inlineSlot.SetValue(RenderTransformProperty, oldTransform);
                }
                phase = "restored"; await Task.Delay(450);
                phase = "radial-over-media";
                PrepareRadialBackEntry(); SetInteractive(false); FocusTray();
                await Until(() => RadialOpen && radialView is { IsLoaded: true, Visibility: Visibility.Visible });
                await Task.Delay(600);
                if (!ReferenceEquals(radialView!.Parent, ShellOverlayLayer) || Canvas.GetZIndex(ShellOverlayLayer) <= Canvas.GetZIndex(fullscreenLayer))
                    throw new InvalidOperationException("Shell chooser is not above the resident media layer.");
                var wheelBounds = radialView.TransformToVisual(this).TransformBounds(new(0, 0, radialView.ActualWidth, radialView.ActualHeight));
                var videoBounds = browser.TransformToVisual(this).TransformBounds(new(0, 0, browser.ActualWidth, browser.ActualHeight));
                wheelBounds.Intersect(videoBounds);
                if (wheelBounds.IsEmpty || wheelBounds.Width <= 0 || wheelBounds.Height <= 0 || unloads != 0 || !ReferenceEquals(browser.Parent, parent))
                    throw new InvalidOperationException("Radial layering probe must overlap uninterrupted resident video.");
                checks.Add("Radial chooser overlaps above resident video without reattaching the browser");
                Write(true, null);
            }
            catch (Exception error) { Write(false, error.ToString()); }
            finally
            {
                CompositionTarget.Rendering -= Sample;
                if (browser is not null)
                { browser.Loaded -= Lifetime; browser.Unloaded -= Lifetime; browser.UnregisterPropertyChangedCallback(VisibilityProperty, visibilityToken); }
            }

            void Write(bool passed, string? error) => File.WriteAllText(path, JsonSerializer.Serialize(new { passed, checks, frames, error }));
            async Task Button(ControllerButton button)
            { await RouteButtonAsync(button, ControllerEventPhase.Pressed); await RouteButtonAsync(button, ControllerEventPhase.Released); }
            async Task Toggle()
            {
                await Until(() => !switching && Walk(surface!).OfType<Button>().Any(b => AutomationProperties.GetAutomationId(b) == "Widget.youtube.playback.toggle" && b.IsEnabled));
                var button = Walk(surface!).OfType<Button>().Single(b => AutomationProperties.GetAutomationId(b) == "Widget.youtube.playback.toggle");
                button.Focus(FocusState.Keyboard);
                await Button(ControllerButton.A);
            }
            async Task Send(string action, string? text = null)
            {
                await Until(() => surface?.CurrentBinding is { } b && Nodes(b.Frame.Snapshot.Root).Any(n => n.ActionId == action));
                var frame = surface!.CurrentBinding!.Frame;
                var node = Nodes(frame.Snapshot.Root).First(n => n.ActionId == action);
                await InvokeAsync(new(frame, new WidgetActionEvent(action, node.Id, InputScopeId: frame.Snapshot.ActiveInputScopeId)
                    { CommittedText = text, FocusedElementId = node.Id }));
            }
            async Task Until(Func<bool> condition) => await UntilAsync(() => Task.FromResult(condition()));
            async Task UntilAsync(Func<Task<bool>> condition)
            {
                var deadline = Environment.TickCount64 + 60000;
                while (!await condition()) { if (Environment.TickCount64 > deadline) throw new TimeoutException("YouTube fullscreen probe: " + phase); await Task.Delay(40); }
            }
        };
        static IEnumerable<ViewNode> Nodes(ViewNode node) { yield return node; foreach (var child in node.Children) foreach (var n in Nodes(child)) yield return n; }
        static IEnumerable<DependencyObject> Walk(DependencyObject root)
        { yield return root; for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); ++i) foreach (var c in Walk(VisualTreeHelper.GetChild(root, i))) yield return c; }
    }
}
