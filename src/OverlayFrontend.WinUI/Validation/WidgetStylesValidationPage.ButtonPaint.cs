using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using WidgetRail.OverlayFrontend.WinUI.Presentation;

namespace WidgetRail.OverlayFrontend.WinUI.Validation;

internal sealed partial class WidgetStylesValidationPage
{
    private async Task NativeButtonPaintAsync()
    {
        foreach (var button in new Button[] { new(), new WidgetValueButton(), new WidgetDisclosureButton() })
        {
            button.Template = (ControlTemplate)Application.Current.Resources["WidgetDepthButtonTemplate"];
            button.Content = "Paint continuity";
            button.Width = 240; button.Height = 48;
            button.ApplyTemplate();
            var template = (FrameworkElement)VisualTreeHelper.GetChild(button, 0);
            var body = (ContentPresenter)template.FindName("ContentPresenter");
            var originalTransition = body.BackgroundTransition;
            Check(originalTransition is not null, $"{button.GetType().Name}: unstyled button retains the native background transition");
            using var adapter = new NativeComputedStyleAdapter(button);
            var authored = Compute("button { background: #232323; color: #ffffff; }", "paint", "button");
            adapter.Update(authored);
            Check(body.BackgroundTransition is null, $"{button.GetType().Name}: authored paint takes ownership before first realization");
            host.Children.Add(button);
            await Wait(() => button.IsLoaded);
            button.Focus(FocusState.Keyboard);
            foreach (var enabled in new[] { false, true })
            {
                button.IsEnabled = enabled;
                adapter.Update(authored);
                await Task.Delay(20);
                Check(body.BackgroundTransition is null && ColorOf(button.Background) == Windows.UI.Color.FromArgb(255, 35, 35, 35),
                    $"{button.GetType().Name}: enabled={enabled} keeps authored fill without a native intermediate fade");
            }
            adapter.Update(Compute("button { color: #ffffff; }", "paint", "button"));
            Check(ReferenceEquals(body.BackgroundTransition, originalTransition), $"{button.GetType().Name}: removing only authored background restores the original native transition");
            adapter.Update(authored);
            adapter.Dispose();
            Check(ReferenceEquals(body.BackgroundTransition, originalTransition), $"{button.GetType().Name}: retiring the style releases background-transition ownership");
            host.Children.Remove(button);
        }
    }
}
