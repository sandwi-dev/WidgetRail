using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace WidgetRail.OverlayFrontend.WinUI.Presentation;

/// <summary>Theme paint above artwork, below foreground content; never an input target.</summary>
internal sealed partial class WidgetArtworkOverlays : Grid
{
    private readonly Border tint = new();
    private readonly Border scrim = new();
    private NativeArtworkStyle style;

    internal WidgetArtworkOverlays()
    {
        IsHitTestVisible = false;
        RowDefinitions.Add(new() { Height = new(55, GridUnitType.Star) });
        RowDefinitions.Add(new() { Height = new(45, GridUnitType.Star) });
        SetRowSpan(tint, 2); SetRow(scrim, 1);
        Children.Add(tint); Children.Add(scrim);
    }

    internal void Update(NativeArtworkStyle value, bool hasArtwork)
    {
        if (style.Tint != value.Tint) tint.Background = value.Tint is { } color ? new SolidColorBrush(color) : null;
        if (style.Scrim != value.Scrim) scrim.Background = value.Scrim is { } bottom ? new SolidColorBrush(bottom) : null;
        style = value;
        Visibility = hasArtwork && (value.Tint is not null || value.Scrim is not null) ? Visibility.Visible : Visibility.Collapsed;
    }
}
