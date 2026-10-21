using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using WidgetRail.PlatformBroker;

namespace WidgetRail.OverlayFrontend.WinUI.Shell;

internal sealed partial class ShellTaskWindowRow : UserControl
{
    private TaskWindowSummary window;
    private string application = "";
    private readonly Image icon = new() { Width = 32, Height = 32, Stretch = Stretch.Uniform };
    private readonly FontIcon fallback = new() { Glyph = "\uE737", FontFamily = new("Segoe Fluent Icons"), FontSize = 24 };
    private readonly TextBlock title = new() { FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
        TextWrapping = TextWrapping.Wrap, MaxLines = 2, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly TextBlock detail = new() { TextTrimming = TextTrimming.CharacterEllipsis };

    internal ShellTaskWindowRow(TaskWindowSummary window)
    {
        this.window = window;
        IsTabStop = false;
        var grid = new Grid { ColumnSpacing = 12 };
        grid.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        grid.Children.Add(new Grid { Width = 32, Height = 32, VerticalAlignment = VerticalAlignment.Center, Children = { fallback, icon } });
        var text = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center, Children = { title, detail } };
        grid.Children.Add(text); Grid.SetColumn(text, 1);
        Content = grid;
        Update(window.ApplicationName, null);
    }

    internal void Update(string applicationName, ImageSource? source)
    {
        if (string.IsNullOrWhiteSpace(applicationName)) applicationName = window.ApplicationName;
        application = applicationName;
        title.Text = window.Title;
        var secondary = string.Equals(title.Text, applicationName, StringComparison.OrdinalIgnoreCase) ? "" : applicationName;
        detail.Text = secondary + (window.IsMinimized ? (secondary.Length > 0 ? " · " : "") + "Minimized" : "");
        detail.Visibility = detail.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        icon.Source = source;
        fallback.Visibility = source is null ? Visibility.Visible : Visibility.Collapsed;
    }

    internal void UpdateWindow(TaskWindowSummary value)
    { window = value; Update(application, icon.Source); }

    internal void Apply(ShellChromePalette palette, double scale)
    {
        title.FontSize = 16 * scale; detail.FontSize = 13 * scale;
        title.Foreground = fallback.Foreground = new SolidColorBrush(palette.Text);
        detail.Foreground = new SolidColorBrush(palette.Muted);
    }
}
