using System.Globalization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WidgetRail.OverlayFrontend.WinUI.Browser;
using WidgetRail.WidgetBridge;
using WidgetRail.WidgetPresentationSession;

namespace WidgetRail.OverlayFrontend.WinUI.Shell;

/// <summary>Shared notification hierarchy for the passive banner and pending list.</summary>
internal sealed partial class NotificationCard : ContentControl
{
    private readonly WidgetCatalogItemContent icon;
    private readonly TextBlock source = new() { MaxLines = 1, TextTrimming = TextTrimming.CharacterEllipsis, Opacity = .8 };
    private readonly TextBlock time = new() { VerticalAlignment = VerticalAlignment.Center, Opacity = .8 };
    internal readonly TextBlock TitleText = new() { MaxLines = 2, TextWrapping = TextWrapping.Wrap,
        TextTrimming = TextTrimming.CharacterEllipsis, FontWeight = new() { Weight = 600 } };
    private readonly TextBlock body = new() { MaxLines = 2, TextWrapping = TextWrapping.Wrap, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly TextBlock stale = new() { Text = "Session ended", Opacity = .7 };

    internal NotificationCard(Func<BridgeWidgetDescriptor, string, CancellationToken, Task<WidgetPresentationPackageIcon>> resolve)
    {
        IsTabStop = false; IsHitTestVisible = false;
        HorizontalContentAlignment = HorizontalAlignment.Stretch;
        var layout = new Grid { ColumnSpacing = 12, Padding = new(4, 8, 4, 8) };
        layout.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        layout.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        icon = new(resolve) { ShowLabel = false, IconSize = 28, Width = 32, VerticalAlignment = VerticalAlignment.Top, Margin = new(0, 4, 0, 0) };
        var text = new StackPanel { Spacing = 4 };
        var metadata = new Grid { ColumnSpacing = 12 };
        metadata.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        metadata.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        metadata.Children.Add(source); metadata.Children.Add(time); Grid.SetColumn(time, 1);
        text.Children.Add(metadata); text.Children.Add(TitleText); text.Children.Add(body); text.Children.Add(stale);
        layout.Children.Add(icon); layout.Children.Add(text); Grid.SetColumn(text, 1);
        Content = layout;
    }
    internal void Clear()
    {
        icon.DataContext = null;
        source.Text = time.Text = TitleText.Text = body.Text = "";
        stale.Visibility = Visibility.Collapsed;
    }
    internal void Update(NotificationEntry entry, BridgeWidgetDescriptor? widget, double textScale)
    {
        icon.DataContext = widget;
        icon.Visibility = widget is null ? Visibility.Collapsed : Visibility.Visible;
        var domain = Uri.TryCreate(entry.Origin, UriKind.Absolute, out var uri) ? uri.Host : "";
        source.Text = (widget?.Name ?? entry.Widget) + (domain.Length > 0 ? " · " + domain : "");
        time.Text = entry.ReceivedAt.LocalDateTime.ToString(entry.ReceivedAt.Date == DateTimeOffset.Now.Date ? "t" : "g", CultureInfo.CurrentCulture);
        TitleText.Text = entry.Title; body.Text = entry.Body;
        body.Visibility = string.IsNullOrWhiteSpace(entry.Body) ? Visibility.Collapsed : Visibility.Visible;
        stale.Visibility = entry.Available ? Visibility.Collapsed : Visibility.Visible;
        source.FontSize = time.FontSize = stale.FontSize = 12 * textScale;
        TitleText.FontSize = 17 * textScale; body.FontSize = 14 * textScale;
    }
}
