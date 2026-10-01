using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using WidgetRail.OverlayFrontend.WinUI.Presentation;

namespace WidgetRail.OverlayFrontend.WinUI.Validation;

internal sealed partial class WidgetStylesValidationPage
{
    private async Task NativeDepthStatesAsync()
    {
        var stock = new Button { Content = "Native reference", Width = 220, Height = 44 };
        var themed = new Button { Content = "Depth template", Width = 220, Height = 44,
            Template = (ControlTemplate)Application.Current.Resources["WidgetDepthButtonTemplate"] };
        var fixtures = new StackPanel { Orientation = Orientation.Horizontal, Children = { stock, themed } };
        host.Children.Add(fixtures);
        try
        {
            await Wait(() => Body(stock) is not null && Body(themed) is not null);
            var original = ColorOf(Body(themed)!.Background);
            foreach (var state in new[] { "Normal", "PointerOver", "Pressed" })
            {
                Check(VisualStateManager.GoToState(stock, state, false) && VisualStateManager.GoToState(themed, state, false),
                    $"depth Button retains native {state} visual state");
                await Task.Delay(100);
                Check(ColorOf(Body(stock)!.Background) == ColorOf(Body(themed)!.Background) &&
                    ColorOf(Body(stock)!.Foreground) == ColorOf(Body(themed)!.Foreground),
                    $"unstyled depth Button matches native {state} paint resources");
            }
            stock.IsEnabled = themed.IsEnabled = false;
            await Task.Delay(100);
            Check(ColorOf(Body(stock)!.Background) == ColorOf(Body(themed)!.Background) &&
                ColorOf(Body(stock)!.Foreground) == ColorOf(Body(themed)!.Foreground),
                "unstyled disabled depth Button retains native disabled feedback");
            using (var adapter = new NativeComputedStyleAdapter(themed))
            {
                adapter.Update(Compute("button { background: #884422; }", "partial", "button"));
                themed.IsEnabled = true;
                VisualStateManager.GoToState(themed, "PointerOver", false);
                await Task.Delay(100);
                Check(ColorOf(Body(themed)!.Background) == Windows.UI.Color.FromArgb(255, 136, 68, 34),
                    "partial authored background remains authoritative during native pointer hover");
                themed.IsEnabled = false;
                await Task.Delay(100);
                Check(ColorOf(Body(themed)!.Foreground) == ColorOf(Body(stock)!.Foreground),
                    "partial style retains native disabled foreground when author supplies no color");
                adapter.Update(null);
                themed.IsEnabled = true; themed.IsEnabled = false;
                await Task.Delay(100);
                Check(ColorOf(Body(themed)!.Background) == ColorOf(Body(stock)!.Background),
                    $"removing partial styles restores native disabled background resources [themed={ColorOf(Body(themed)!.Background)}; stock={ColorOf(Body(stock)!.Background)}]");
            }
        }
        finally { host.Children.Remove(fixtures); }

        foreach (var grid in new[] { false, true })
        {
            ListViewBase list = grid ? new GridView() : new ListView();
            list.Width = 320; list.Height = 120;
            list.Items.Add("Selectable native row"); host.Children.Add(list);
            try
            {
                await Wait(() => list.ContainerFromIndex(0) is SelectorItem { IsLoaded: true });
                var item = (SelectorItem)list.ContainerFromIndex(0);
                var body = Descendant<ListViewItemPresenter>(item);
                Check(body is not null && body.PointerOverBackground is not null && body.PressedBackground is not null &&
                    body.SelectedBackground is not null && body.SelectedDisabledBackground is not null,
                    $"{list.GetType().Name} retains native item presenter hover press selection and disabled resources");
                item.IsSelected = true;
                Check(item.IsSelected && ReferenceEquals(list.SelectedItem, list.Items[0]) && item.UseSystemFocusVisuals,
                    $"{list.GetType().Name} retains native selection ownership and system focus");
                item.IsEnabled = false;
                Check(body!.DisabledOpacity < 1 && body.CheckDisabledBrush is not null,
                    $"{list.GetType().Name} retains native disabled opacity and selection marks");
            }
            finally { host.Children.Remove(list); }
        }

        static ContentPresenter? Body(Button button) => Descendant<ContentPresenter>(button);
        static T? Descendant<T>(DependencyObject root) where T : DependencyObject
        {
            for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); ++i)
            {
                var child = VisualTreeHelper.GetChild(root, i);
                if (child is T target) return target;
                if (Descendant<T>(child) is { } found) return found;
            }
            return null;
        }
    }
}
