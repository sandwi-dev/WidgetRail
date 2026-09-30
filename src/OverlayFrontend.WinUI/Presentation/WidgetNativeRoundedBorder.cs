using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;

namespace WidgetRail.OverlayFrontend.WinUI.Presentation;

/// <summary>A native antialiased vector stroke for an authored uniform rounded
/// button border. Keeps the ContentPresenter's border thickness for measurement;
/// square/asymmetric/native unstyled borders retain their original renderer.</summary>
internal sealed class WidgetNativeRoundedBorder(Button button)
{
    private ContentPresenter? content;
    private Rectangle? stroke;
    private bool active;
    private static readonly SolidColorBrush clear = new(Microsoft.UI.Colors.Transparent);
    internal bool IsActive => active;

    internal void Update(Brush? brush, Thickness? thickness, CornerRadius? radius)
    {
        if (stroke is null)
        {
            button.ApplyTemplate();
            if (VisualTreeHelper.GetChildrenCount(button) == 0 || VisualTreeHelper.GetChild(button, 0) is not Grid root) return;
            stroke = root.Children.OfType<Rectangle>().FirstOrDefault(child => child.Name == "WidgetRoundedBorder");
            content = root.Children.OfType<ContentPresenter>().FirstOrDefault();
        }
        if (stroke is null || content is null) return;
        var width = thickness?.Left ?? 0;
        var corner = radius?.TopLeft ?? 0;
        var eligible = brush is not null && width > 0 && corner > 0 &&
            thickness == new Thickness(width) && radius == new CornerRadius(corner) &&
            brush is not SolidColorBrush { Color.A: 0 };
        if (!eligible) { Restore(); return; }
        // Half-stroke inset keeps all edge coverage inside the original border
        // bounds without clipping the antialiased outer half of a centered line.
        stroke.Margin = new(width / 2);
        stroke.RadiusX = stroke.RadiusY = Math.Max(0, corner - width / 2);
        stroke.StrokeThickness = width; stroke.Stroke = brush;
        stroke.Visibility = Visibility.Visible;
        content.BorderBrush = clear; active = true;
    }
    internal void Restore()
    {
        if (!active) return;
        if (stroke is not null) { stroke.Visibility = Visibility.Collapsed; stroke.Stroke = null; }
        content!.SetBinding(ContentPresenter.BorderBrushProperty, new Binding { Source = button, Path = new PropertyPath(nameof(Button.BorderBrush)), Mode = BindingMode.OneWay });
        active = false;
    }
}
