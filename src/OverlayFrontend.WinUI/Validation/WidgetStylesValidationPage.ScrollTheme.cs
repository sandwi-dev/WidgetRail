using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using WidgetRail.OverlayFrontend.WinUI.Presentation;
using Windows.UI;

namespace WidgetRail.OverlayFrontend.WinUI.Validation;

internal sealed partial class WidgetStylesValidationPage
{
    private async Task NativeScrollThemeAsync()
    {
        var scroll = new ScrollViewer { Width = 240, Height = 100,
            VerticalScrollBarVisibility = ScrollBarVisibility.Visible,
            Content = new Border { Height = 800, Width = 200 } };
        var resources = scroll.Resources;
        host.Children.Add(scroll);
        using var adapter = new NativeComputedStyleAdapter(scroll, false);
        try
        {
            adapter.Update(Compute("#scroll { scrollbar-thumb-color: #ff3355; scrollbar-track-color: #112244; scrollbar-width: 5px; }", "scroll", "scroll"));
            await Wait(() => scroll.ScrollableHeight > 0 && ScrollParts(scroll).OfType<Thumb>().Any());
            var thumb = ScrollParts(scroll).OfType<Thumb>().First(value => value.Name == "VerticalThumb");
            var track = ScrollParts(scroll).OfType<Rectangle>().First(value => value.Name == "VerticalTrackRect");
            Check(ColorOf(thumb.Background) == Color.FromArgb(255,255,51,85) && ColorOf(track.Fill) == Color.FromArgb(255,17,34,68),
                "native scrollbar template resolves authored thumb and track colors");
            scroll.ChangeView(null, 200, null, true);
            await Wait(() => scroll.VerticalOffset == 200);
            adapter.Update(Compute("#scroll { scrollbar-thumb-color: #33ff55; scrollbar-track-color: #442211; scrollbar-width: 5px; }", "scroll", "scroll"));
            await Wait(() => ColorOf(thumb.Background) == Color.FromArgb(255,51,255,85) && ColorOf(track.Fill) == Color.FromArgb(255,68,34,17));
            Check(ColorOf(track.Fill) == Color.FromArgb(255,68,34,17) && scroll.VerticalOffset == 200 &&
                ReferenceEquals(thumb, ScrollParts(scroll).OfType<Thumb>().First(value => value.Name == "VerticalThumb")),
                $"live scrollbar theme update preserves native thumb and viewport (offset={scroll.VerticalOffset}, track={ColorOf(track.Fill)})");
            WidgetViewPresenter.SetHighContrastStyleOverride(true);
            await Wait(() => ColorOf(thumb.Background) != Color.FromArgb(255,51,255,85));
            Check(ColorOf(thumb.Background).A == 255, "scrollbar ink follows opaque high contrast palette");
            WidgetViewPresenter.SetHighContrastStyleOverride(false);
            adapter.Update(null);
            Check(ReferenceEquals(resources, scroll.Resources), "removed scrollbar styling restores original resources");
        }
        finally { WidgetViewPresenter.SetHighContrastStyleOverride(false); host.Children.Remove(scroll); }
        var slider = new Slider { Width = 240, Value = 40 };
        host.Children.Add(slider);
        using var sliderStyle = new NativeComputedStyleAdapter(slider);
        try
        {
            sliderStyle.Update(Compute("#slider { color: #ff3355; background: #112244; }", "slider", "slider"));
            await Wait(() => ScrollParts(slider).OfType<Rectangle>().Any(value => value.Name == "HorizontalDecreaseRect"));
            var fill = ScrollParts(slider).OfType<Rectangle>().First(value => value.Name == "HorizontalDecreaseRect");
            VisualStateManager.GoToState(slider, "PointerOver", false);
            await Task.Delay(30);
            Check(ColorOf(fill.Fill) == Color.FromArgb(255,255,51,85), "slider pointer state retains authored accent instead of Windows accent");
            sliderStyle.Update(Compute("#slider { color: #33ff55; background: #442211; }", "slider", "slider"));
            await Wait(() => ColorOf(fill.Fill) == Color.FromArgb(255,51,255,85));
            Check(slider.Value == 40, "live slider paint updates retain its value");
        }
        finally { host.Children.Remove(slider); }
    }

    private static IEnumerable<DependencyObject> ScrollParts(DependencyObject element)
    {
        yield return element;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(element); ++i)
            foreach (var child in ScrollParts(VisualTreeHelper.GetChild(element, i))) yield return child;
    }
}
