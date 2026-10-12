using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using WidgetRail.OverlayFrontend.WinUI.Media;
using WidgetRail.OverlayFrontend.WinUI.Presentation;
using WidgetRail.PlatformSettings;
using WidgetRail.WidgetBridge;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.OverlayFrontend.WinUI.Validation;

internal sealed partial class WidgetStylesValidationPage
{
    private async Task PageNavigationAsync(string directory)
    {
        var root = new Grid { Width = 760, Height = 600 };
        var page = new WidgetViewPresenter { Width = 700, Height = 540 };
        root.Children.Add(page); new MediaFullscreenLayer().Attach(root); host.Children.Add(root);
        Exception? failure = null;
        page.Failed = error => failure = error;
        var appearance = AppearanceSettings.Default with { Motion = MotionPreference.Full, WidgetAnimationSpeed = .5 };
        var mp4 = Path.Combine(directory, "pattern.mp4"); var png = Path.Combine(directory, "pattern.png");
        var video = new MediaPlayerDefinition(MediaPlayerSource.LocalFile(mp4), new(Muted: true, Loop: true, AutoPlay: true));
        var image = new MediaPlayerDefinition(MediaPlayerSource.LocalFile(png), new());
        var result = Path.Combine(directory, "native-pages.json");
        try
        {
            foreach (var effect in Enum.GetValues<WidgetPageAnimation>())
            {
                page.ApplyAppearance(appearance with { PageAnimation = effect }, true);
                Render("home", WidgetNavigationOperation.Replace);
                await Settle();
                var home = Find<Button>("Widget.route.open", page)!;
                home.Focus(FocusState.Keyboard);
                var scroll = Find<ScrollViewer>("Widget.route.scroll", page)!;
                scroll.ChangeView(null, 180, null, true); await Task.Delay(70);
                var rememberedOffset = scroll.VerticalOffset;
                Check(rememberedOffset > 0, effect + ": home is scrolled before entering the preview");
                Render("video", WidgetNavigationOperation.Forward);
                var media = Find<MediaPlayerView>("Widget.route.media", page)!;
                media.Apply(_ => Task.FromResult(new HostMediaPlayerSource(video, new Uri(mp4).AbsoluteUri)), video, () => true);
                if (effect != WidgetPageAnimation.None)
                {
                    await Task.Delay(40);
                    Check(MediaPlayerView.ResidentPlayers == 0, effect + ": decoder startup waits for the incoming page animation");
                }
                await Wait(() => media.IsPlaying);
                await Settle();
                Check(media.IsPlaying, effect + ": incoming native video plays");
                var unloads = 0;
                var nativeVideo = media.PageVideo;
                nativeVideo.Unloaded += (_, _) => ++unloads;
                var nativeParent = nativeVideo.Parent;
                var playback = page.TransitionPlayback;
                Render("video", WidgetNavigationOperation.Forward);
                Check(ReferenceEquals(page.TransitionPlayback, playback), effect + ": same-page update does not animate again");
                Render("home", WidgetNavigationOperation.Back);
                if (effect != WidgetPageAnimation.None)
                {
                    Check(page.OutgoingTransitionCount == 1 && media.IsPlaying, effect + ": outgoing video survives page replacement");
                    Check(!media.Handle(ControllerButton.A, ControllerEventPhase.Pressed), effect + ": outgoing media refuses input");
                    await Task.Delay(90);
                    Check(media.PageVideoMatchesSlot() && unloads == 0 && ReferenceEquals(nativeVideo.Parent, nativeParent),
                        effect + $": video follows the moving page without native reattachment (unloads={unloads}; sameParent={ReferenceEquals(nativeVideo.Parent, nativeParent)}; {media.PageVideoDiagnostics})");
                }
                await Settle();
                Check(MediaPlayerView.ResidentPlayers == 0, effect + ": outgoing decoder retires after animation");
                Check(AutomationProperties.GetAutomationId(Microsoft.UI.Xaml.Input.FocusManager.GetFocusedElement(XamlRoot) as DependencyObject) == "Widget.route.open",
                    effect + ": Back restores the original home control");
                Check(Math.Abs(Find<ScrollViewer>("Widget.route.scroll", page)!.VerticalOffset - rememberedOffset) < 1,
                    effect + ": Back restores the home scroll offset");
            }
            page.ApplyAppearance(appearance, true);
            Render("image", WidgetNavigationOperation.Forward);
            var still = Find<MediaPlayerView>("Widget.route.media", page)!;
            still.Apply(_ => Task.FromResult(new HostMediaPlayerSource(image, new Uri(png).AbsoluteUri, true, 1920, 1080)), image, () => true);
            await Wait(() => still.CanControl); await Settle();
            Render("home", WidgetNavigationOperation.Back); await Settle();
            Check(MediaPlayerView.ResidentPlayers == 0, "Screenshot navigation uses no video decoder");
            Render("video", WidgetNavigationOperation.Forward);
            Render("home", WidgetNavigationOperation.Back);
            await Settle(); Check(page.OutgoingTransitionCount == 0, "Rapid navigation releases superseded trees");
            Render("video", WidgetNavigationOperation.Forward);
            await page.SetPresentationActiveAsync(false);
            Check(page.OutgoingTransitionCount == 0 && MediaPlayerView.ResidentPlayers == 0, "Hiding cancels page retention");
            await page.SetPresentationActiveAsync(true);
            page.ApplyAppearance(appearance with { Motion = MotionPreference.Reduced }, true);
            Render("home", WidgetNavigationOperation.Back);
            Check(page.OutgoingTransitionCount == 0, "Reduced motion changes pages without retention");
            page.ApplyAppearance(appearance, true);
            RenderPair("home", WidgetNavigationOperation.Replace); await Settle();
            Find<Button>("Widget.pair.left.item.4", page)!.Focus(FocusState.Keyboard);
            Find<ScrollViewer>("Widget.pair.left.scroll", page)!.ChangeView(null, 80, null, true);
            Find<ScrollViewer>("Widget.pair.right.scroll", page)!.ChangeView(null, 120, null, true);
            await Task.Delay(80);
            var leftOffset = Find<ScrollViewer>("Widget.pair.left.scroll", page)!.VerticalOffset;
            var rightOffset = Find<ScrollViewer>("Widget.pair.right.scroll", page)!.VerticalOffset;
            Check(leftOffset > 0 && rightOffset > 0, "Independent page regions have different scroll positions");
            RenderPair("detail", WidgetNavigationOperation.Forward); await Settle();
            RenderPair("home", WidgetNavigationOperation.Back); await Settle();
            Check(Math.Abs(Find<ScrollViewer>("Widget.pair.left.scroll", page)!.VerticalOffset - leftOffset) < 1 &&
                Math.Abs(Find<ScrollViewer>("Widget.pair.right.scroll", page)!.VerticalOffset - rightOffset) < 1,
                "Concurrent Back navigation restores both page regions");
            if (failure is not null) throw failure;
            await File.WriteAllTextAsync(result, JsonSerializer.Serialize(new { passed = true, checks }));
        }
        catch (Exception error) { await File.WriteAllTextAsync(result, JsonSerializer.Serialize(new { passed = false, checks, error = error.ToString() })); throw; }
        finally { await page.DisposeAsync(); host.Children.Remove(root); }

        void Render(string key, WidgetNavigationOperation operation)
        {
            var content = key == "home" ? new ViewNode { Id = "route.scroll", Kind = ViewNodeKind.Scroll, ScrollAxis = WidgetProtocol.ScrollAxis.Vertical,
                Children = Enumerable.Range(0, 15).Select(i => new ViewNode { Id = i == 5 ? "route.open" : "route.item." + i,
                    Kind = ViewNodeKind.Button, Text = "Open capture " + i, ActionId = "open" }).ToArray() }
                : new ViewNode { Id = "route.media", Kind = ViewNodeKind.MediaPlayer, MediaPlayer = key == "video" ? video : image, AccessibilityLabel = "Capture preview" };
            var region = new ViewNode { Id = "route", Kind = ViewNodeKind.Stack, InputScopeId = "route", Transition = new("pages", key, 0, WidgetTransitionKind.Page, operation),
                Children = (ViewNode[])[content, new() { Id = "route.back", Kind = ViewNodeKind.Button, Text = "Back", ActionId = "back" }] };
            page.Apply(CreateFrame(region, new Dictionary<string, BridgeNodeRenderStyles>
            {
                ["route"] = Compute("stack { width: 700px; height: 540px; background: #302040; padding: 16px; gap: 12px; }", "route", "stack"),
                ["route.media"] = Compute("media-player { height: 380px; }", "route.media", "media-player"),
                ["route.scroll"] = Compute("scroll { height: 380px; }", "route.scroll", "scroll"),
            }));
            page.UpdateLayout();
        }
        async Task Settle()
        {
            page.UpdateLayout(); await Task.Delay(35);
            if (page.TransitionPlayback is { } playback) await playback;
            await Task.Delay(70);
            if (failure is not null) throw failure;
        }
        void RenderPair(string key, WidgetNavigationOperation operation)
        {
            var styles = new Dictionary<string, BridgeNodeRenderStyles>();
            ViewNode Region(string name)
            {
                var id = "pair." + name;
                styles[id] = Compute("stack { width: 330px; height: 400px; }", id, "stack");
                styles[id + ".scroll"] = Compute("scroll { height: 300px; }", id + ".scroll", "scroll");
                var body = key == "home" ? new ViewNode { Id = id + ".scroll", Kind = ViewNodeKind.Scroll, ScrollAxis = WidgetProtocol.ScrollAxis.Vertical,
                    Children = Enumerable.Range(0, 20).Select(i => new ViewNode { Id = id + ".item." + i, Kind = ViewNodeKind.Button, Text = "Item " + i, ActionId = "open" }).ToArray() }
                    : new ViewNode { Id = id + ".back", Kind = ViewNodeKind.Button, Text = "Back", ActionId = "back" };
                return new() { Id = id, Kind = ViewNodeKind.Stack, Transition = new(id, key, 0, WidgetTransitionKind.Page, operation), Children = (ViewNode[])[body] };
            }
            var regions = new ViewNode { Id = "pair", Kind = ViewNodeKind.Row, Children = (ViewNode[])[Region("left"), Region("right")] };
            page.Apply(CreateFrame(regions, styles)); page.UpdateLayout();
        }
    }
}
