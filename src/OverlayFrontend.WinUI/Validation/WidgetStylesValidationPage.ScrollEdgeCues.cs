using System.Text.Json;
using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using WidgetRail.OverlayFrontend.WinUI.Presentation;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetBridge;
using Windows.UI;

namespace WidgetRail.OverlayFrontend.WinUI.Validation;

internal sealed partial class WidgetStylesValidationPage
{
    private async Task ScrollCueRevealAlignmentAsync()
    {
        presenter.Width = 600; presenter.Height = 320;
        presenter.ApplyAppearance(WidgetRail.PlatformSettings.AppearanceSettings.Default with
            { Motion = WidgetRail.PlatformSettings.MotionPreference.Reduced }, false);
        var root = new ViewNode { Id = "cue.reveal.root", Kind = ViewNodeKind.Grid,
            GridLayout = new() { Rows = (GridTrackDefinition[])[new() { Sizing = GridTrackSizing.Pixel, Value = 50 }, new() { Sizing = GridTrackSizing.Star }] },
            Children = (ViewNode[])[new() { Id = "cue.keep", Kind = ViewNodeKind.Button, Text = "Keep focus", ActionId = "keep" },
                new() { Id = "cue.viewport", Kind = ViewNodeKind.Scroll, ScrollAxis = ScrollAxis.Vertical, GridCell = new() { Row = 1 },
                    Children = (ViewNode[])[new() { Id = "cue.leading", Kind = ViewNodeKind.Spacer },
                        new() { Id = "cue.target", Kind = ViewNodeKind.Button, Text = "Reveal this item", ActionId = "target" },
                        new() { Id = "cue.trailing", Kind = ViewNodeKind.Spacer }] }] };
        var styles = new Dictionary<string, BridgeNodeRenderStyles>
        {
            ["cue.reveal.root"] = Compute("#x { padding: 0px; gap: 0px; }", "x", "grid"),
            ["cue.viewport"] = Compute("#x { padding: 12px; gap: 0px; }", "x", "scroll"),
            ["cue.leading"] = Compute("#x { height: 200px; }", "x", "spacer"),
            ["cue.trailing"] = Compute("#x { height: 500px; }", "x", "spacer"),
        };
        presenter.Apply(CreateFrame(root, styles));
        await Wait(() => Find<Button>("Widget.cue.target") is { IsLoaded: true });
        var keep = Find<Button>("Widget.cue.keep")!;
        var target = Find<Button>("Widget.cue.target")!;
        var scroll = Find<ScrollViewer>("Widget.cue.viewport")!;
        keep.Focus(FocusState.Keyboard);
        var frame = CreateFrame(root, styles);
        presenter.Apply(frame with { Snapshot = frame.Snapshot with { ScrollRevealRequest = new(++sequence, "cue.viewport", "cue.target") } });
        var tolerance = 1 / XamlRoot.RasterizationScale;
        await Wait(() => Math.Abs(target.TransformToVisual(scroll).TransformPoint(new()).Y - WidgetScrollViewport.ContentBounds(scroll).Y) <= tolerance);
        Check(scroll.VerticalOffset > 0 && WidgetScrollViewport.ContentBounds(scroll).Y > WidgetScrollEdgeCues.StripHeight,
            "Shared reveal aligns the target with the actual padded content viewport, excluding cue strips");
        Check(ReferenceEquals(FocusManager.GetFocusedElement(XamlRoot), keep), "Viewport-correct reveal does not change focus");
    }

    private async Task ScrollEdgeCuesAsync(string directory)
    {
        Directory.CreateDirectory(directory);
        Exception? failure = null;
        try
        {
            var content = new StackPanel();
            for (var i = 0; i < 16; i++) content.Children.Add(new Button { Content = "Reading option " + i, Height = 44 });
            var scroll = new ScrollViewer { Width = 340, Height = 240, Content = content,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollMode = ScrollMode.Disabled };
            host.Children.Add(scroll);
            var first = (Button)content.Children[0];
            await Wait(() => first.IsLoaded);
            first.Focus(FocusState.Keyboard);
            var nativeUnloadCount = 0;
            first.Unloaded += (_, _) => nativeUnloadCount++;
            using var style = new NativeComputedStyleAdapter(scroll);
            try
            {
                style.Update(Compute("#cue { scrollbar-thumb-color: #8195b8; color: #ffffff; background: #112244; }", "cue", "scroll"));
                await Wait(() => style.ScrollEdgeCues?.State == new ScrollEdgeCueState(false, true) && style.ScrollEdgeCues.BelowCue?.ActualWidth > 0);
                var cue = style.ScrollEdgeCues!;
                Check(cue.AboveCue?.IsHitTestVisible == false && cue.BelowCue?.IsHitTestVisible == false,
                    "Scroll cues never intercept input");
                var physicalPixel = 1 / XamlRoot.RasterizationScale;
                Check(cue.BelowCue?.Width == 48 && cue.BelowCue.Height == 2 &&
                    Math.Abs(cue.BelowCue.ActualWidth - 48) <= physicalPixel && Math.Abs(cue.BelowCue.ActualHeight - 2) <= physicalPixel,
                    $"Narrow viewports use 48 by 2 cues within physical-pixel rounding ({cue.BelowCue?.ActualWidth}x{cue.BelowCue?.ActualHeight}, scale={XamlRoot.RasterizationScale})");
                scroll.Width = 800;
                await Wait(() => scroll.ViewportWidth > 700 &&
                    Math.Abs(cue.BelowCue!.Width - scroll.ViewportWidth * .08) < .1);
                Check(cue.BelowCue!.Width is > 48 and < 80, "Cue width follows eight percent of the actual enlarged viewport");
                scroll.Width = 1100;
                await Wait(() => cue.BelowCue!.Width == 80);
                Check(cue.BelowCue!.Width == 80, "Wide viewport cue stops at 80 DIP");
                scroll.Width = 340;
                await Wait(() => cue.BelowCue!.Width == 48);
                Check(nativeUnloadCount == 0 && ReferenceEquals(FocusManager.GetFocusedElement(XamlRoot), first),
                    "Attaching scroll cues never reparents content or changes focused control");
                first.Focus(FocusState.Keyboard);
                var originalViewport = scroll.ViewportHeight;
                scroll.ChangeView(null, first.ActualHeight + 2, null, true);
                await Wait(() => Math.Abs(scroll.VerticalOffset - first.ActualHeight - 2) < physicalPixel);
                var edgeBounds = first.TransformToVisual(scroll).TransformBounds(new(0, 0, first.ActualWidth, first.ActualHeight));
                Check(edgeBounds.Bottom > 0 && edgeBounds.Bottom < WidgetScrollViewport.ContentBounds(scroll).Top &&
                    !WidgetReadingScrollFocus.IsVisible(first, scroll), "Content hidden beneath the top cue strip is not a visible navigation target");
                scroll.ChangeView(null, 180, null, true);
                await Wait(() => cue.State == new ScrollEdgeCueState(true, true));
                Check(ReferenceEquals(FocusManager.GetFocusedElement(XamlRoot), first), "Scroll cue updates retain native focus");
                Check(Math.Abs(scroll.ViewportHeight - originalViewport) < .1, "Cue visibility never changes viewport geometry");
                await Capture(scroll, "dark.png");
                var bounds = cue.AboveCue!.TransformToVisual(scroll).TransformBounds(new(0, 0, cue.AboveCue.Width, 2));
                var nativePresenter = ScrollParts(scroll).OfType<ScrollContentPresenter>().First();
                var contentBounds = nativePresenter.TransformToVisual(scroll).TransformBounds(new(0, 0, nativePresenter.ActualWidth, nativePresenter.ActualHeight));
                Check(bounds.Bottom <= contentBounds.Top + physicalPixel, "Top cue is outside the clipped content viewport");
                var bottomBounds = cue.BelowCue!.TransformToVisual(scroll).TransformBounds(new(0, 0, cue.BelowCue.Width, 2));
                Check(bottomBounds.Top + physicalPixel >= contentBounds.Bottom, "Bottom cue is outside the clipped content viewport");
                scroll.ChangeView(null, scroll.ScrollableHeight, null, true);
                await Wait(() => cue.State == new ScrollEdgeCueState(true, false));
                Check(scroll.VerticalOffset > 0, "Native scrolling reaches the bottom boundary");
                var originalPresenter = nativePresenter;
                host.Children.Remove(scroll);
                await Task.Delay(40);
                host.Children.Add(scroll);
                await Wait(() => cue.Scroll is not null && cue.BelowCue?.IsLoaded == true);
                Check(ReferenceEquals(originalPresenter, ScrollParts(scroll).OfType<ScrollContentPresenter>().First()),
                    "Unload and reload preserve the native presenter");
                Check(Math.Abs(scroll.ViewportHeight - originalViewport) < .1, "Reload does not accumulate edge padding");
                style.Update(Compute("#cue { scrollbar-thumb-color: #53627a; color: #172331; background: #ffffff; }", "cue", "scroll"));
                // These raw native fixture buttons have no widget WRSS adapter;
                // match their foreground to the light palette as production does.
                foreach (var button in content.Children.OfType<Button>()) button.Foreground = new SolidColorBrush(Color.FromArgb(255, 23, 35, 49));
                Check(ColorOf(((Border)cue.AboveCue!).Background) == Color.FromArgb(255, 83, 98, 122), "Light theme updates cue ink");
                await Capture(scroll, "light.png");
                WidgetViewPresenter.SetHighContrastStyleOverride(true);
                await Wait(() => cue.AboveCue?.Opacity == 1);
                Check(ColorOf(((Border)cue.AboveCue!).Background).A == 255, "High contrast uses opaque system-aware cue ink");
                WidgetViewPresenter.SetHighContrastStyleOverride(false);
                content.Children.Clear(); content.Children.Add(new TextBlock { Text = "Short note", Height = 30 });
                await Wait(() => cue.State == default);
                Check(!cue.State.Above && !cue.State.Below, "Dynamic content that fits removes both cues");
                scroll.Height = 320;
                await Wait(() => scroll.ViewportHeight > originalViewport);
                Check(cue.State == default, "Resizing a non-overflowing viewport keeps cues hidden");
            }
            finally { WidgetViewPresenter.SetHighContrastStyleOverride(false); host.Children.Remove(scroll); }

            var inner = new ScrollViewer { Height = 100, Width = 280, Content = new Border { Height = 700 } };
            var outer = new ScrollViewer { Height = 200, Width = 330, Content = inner };
            host.Children.Add(outer);
            using var outerStyle = new NativeComputedStyleAdapter(outer);
            using var innerStyle = new NativeComputedStyleAdapter(inner);
            try
            {
                outerStyle.Update(null); innerStyle.Update(null);
                await Wait(() => innerStyle.ScrollEdgeCues?.State.Below == true);
                Check(outerStyle.ScrollEdgeCues?.State == default(ScrollEdgeCueState), "Non-scrolling outer wrapper does not duplicate nested cues");
                Check(!ReferenceEquals(outerStyle.ScrollEdgeCues?.Scroll, innerStyle.ScrollEdgeCues?.Scroll), "Nested scrollers own independent cue state");
            }
            finally { host.Children.Remove(outer); }

            var list = new ListView { Width = 340, Height = 220, ItemsSource = Enumerable.Range(0, 150).Select(i => "Indexed-style row " + i).ToArray(),
                SelectionMode = ListViewSelectionMode.None };
            host.Children.Add(list);
            using var listStyle = new NativeComputedStyleAdapter(list);
            try
            {
                listStyle.Update(null);
                await Wait(() => listStyle.ScrollEdgeCues?.State.Below == true);
                var cue = listStyle.ScrollEdgeCues!;
                Check(cue.Scroll is not null, "ListView cues resolve its own native virtualized viewport");
                list.ScrollIntoView(list.Items[100]);
                await Wait(() => cue.State.Above && list.ContainerFromIndex(100) is FrameworkElement { IsLoaded: true } target &&
                    WidgetReadingScrollFocus.IsVisible(target, cue.Scroll!));
                // WinUI may retain its focused first row even far outside the
                // viewport. Require bounded realization, not eviction of that row.
                var realized = Enumerable.Range(0, list.Items.Count).Count(index => list.ContainerFromIndex(index) is not null);
                Check(realized < 40 && list.ContainerFromIndex(40) is null,
                    $"Cue decoration preserves list virtualization ({realized}/{list.Items.Count} resident)");
                ScrollViewer.SetVerticalScrollMode(list, ScrollMode.Disabled);
                await Wait(() => cue.State == default);
                Check(!cue.State.Above && !cue.State.Below, "Disabling vertical scrolling removes vertical cues");
            }
            finally { host.Children.Remove(list); }
            await ScrollCueRevealAlignmentAsync();
        }
        catch (Exception error) { failure = error; }
        await File.WriteAllTextAsync(Path.Combine(directory, "result.json"), JsonSerializer.Serialize(new { passed = failure is null, checks, error = failure?.ToString() }));
        if (failure is not null) throw failure;

        async Task Capture(FrameworkElement element, string name)
        {
            var bitmap = new Microsoft.UI.Xaml.Media.Imaging.RenderTargetBitmap();
            await bitmap.RenderAsync(element);
            using var stream = new Windows.Storage.Streams.InMemoryRandomAccessStream();
            var encoder = await Windows.Graphics.Imaging.BitmapEncoder.CreateAsync(Windows.Graphics.Imaging.BitmapEncoder.PngEncoderId, stream);
            encoder.SetPixelData(Windows.Graphics.Imaging.BitmapPixelFormat.Bgra8, Windows.Graphics.Imaging.BitmapAlphaMode.Premultiplied,
                (uint)bitmap.PixelWidth, (uint)bitmap.PixelHeight, 96, 96, (await bitmap.GetPixelsAsync()).ToArray());
            await encoder.FlushAsync();
            stream.Seek(0);
            using var output = File.Create(Path.Combine(directory, name));
            await stream.AsStreamForRead().CopyToAsync(output);
        }
    }
}
