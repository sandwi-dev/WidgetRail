using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using WidgetRail.OverlayFrontend.WinUI.Presentation;
using WidgetRail.WidgetBridge;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.OverlayFrontend.WinUI.Validation;

internal sealed partial class WidgetStylesValidationPage
{
    private async Task RoundedBordersAsync()
    {
        var root = new ViewNode { Id = "rounded-border", Kind = ViewNodeKind.Button, Text = "Restart", ActionId = "restart" };
        var pill = Compute("button { width: 101px; height: 43px; shape: pill; border-width: 1px; border-color: #ff22aa; background: #382841; color: #ff22aa; }", root.Id, "button");
        presenter.Apply(CreateFrame(root, new Dictionary<string, BridgeNodeRenderStyles> { [root.Id] = pill }));
        await Wait(() => Find<Button>("Widget.rounded-border") is { IsLoaded: true } button && NativeComputedStyleAdapter.For(button)?.UsesVectorBorder == true);
        var button = Find<Button>("Widget.rounded-border")!;
        var adapter = NativeComputedStyleAdapter.For(button)!;
        var template = (Grid)VisualTreeHelper.GetChild(button, 0);
        var stroke = template.Children.OfType<Rectangle>().Single(element => element.Name == "WidgetRoundedBorder");
        var body = template.Children.OfType<ContentPresenter>().Single();
        Check(button.BorderThickness == new Thickness(1) && stroke.StrokeThickness == 1 && stroke.Margin == new Thickness(.5) &&
            body.BorderBrush is SolidColorBrush { Color.A: 0 }, "rounded vector border keeps native layout thickness and paints one inset antialiased stroke");
        Check(Math.Abs(stroke.RadiusX - (Math.Min(button.ActualWidth, button.ActualHeight) - 1) / 2) < .01,
            "odd-size pill stroke follows the actual control radius without clipping its outer half");
        adapter.Update(Compute("button { border-width: 1px; border-color: #ff22aa; corner-radius: 0px; }", root.Id, "button"));
        Check(!adapter.UsesVectorBorder && stroke.Visibility == Visibility.Collapsed && ColorOf(body.BorderBrush) == ColorOf(button.BorderBrush),
            "switching to square borders restores native paint without changing the button template or dimensions");
        adapter.Update(null);
        Check(!adapter.UsesVectorBorder && stroke.Stroke is null, "removing authored border releases vector paint");
    }
}
