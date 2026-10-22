using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace WidgetRail.OverlayFrontend.WinUI.Shell;

internal sealed partial class ShellUtilityActionRow : UserControl
{
    private readonly FontIcon icon = new() { FontFamily = new("Segoe Fluent Icons"), FontSize = 24 };
    private readonly TextBlock title = new() { FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock description = new() { TextWrapping = TextWrapping.Wrap };
    internal ShellUtilityActionRow(string label, string detail, string glyph)
    {
        IsTabStop = false;
        title.Text = label; description.Text = detail; icon.Glyph = glyph;
        var grid = new Grid { ColumnSpacing = 12 };
        grid.ColumnDefinitions.Add(new() { Width = new(32) });
        grid.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        grid.Children.Add(icon);
        var text = new StackPanel { Spacing = 2, Children = { title, description } };
        grid.Children.Add(text); Grid.SetColumn(text, 1);
        Content = grid;
    }
    internal void Apply(ShellChromePalette palette, double scale)
    {
        title.FontSize = 16 * scale; description.FontSize = 13 * scale;
        title.Foreground = icon.Foreground = new SolidColorBrush(palette.Text);
        description.Foreground = new SolidColorBrush(palette.Muted);
    }
}
