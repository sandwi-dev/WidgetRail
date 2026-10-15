using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
namespace WidgetRail.OverlayFrontend.WinUI.Shell;

/// <summary>Non-interactive adornments. The containing widget remains the single focus target.</summary>
internal sealed partial class WidgetStatusBadges : Grid
{
    private (WidgetStatus Status, ShellChromePalette Palette, bool Compact)? rendered;
    private readonly Border primary = Badge(VerticalAlignment.Top);
    private readonly Border secondary = Badge(VerticalAlignment.Bottom);
    internal WidgetStatusBadges()
    {
        IsHitTestVisible = false;
        AutomationProperties.SetAccessibilityView(this, AccessibilityView.Raw);
        AutomationProperties.SetAccessibilityView(primary, AccessibilityView.Raw);
        AutomationProperties.SetAccessibilityView(secondary, AccessibilityView.Raw);
        Children.Add(primary); Children.Add(secondary);
    }
    private static Border Badge(VerticalAlignment vertical) => new()
    {
        MinWidth = 18, Height = 18, Padding = new(3, 0, 3, 0), CornerRadius = new(9), BorderThickness = new(1),
        HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = vertical, Visibility = Visibility.Collapsed,
    };
    internal void Update(WidgetStatus status, ShellChromePalette palette, bool compact)
    {
        if (rendered == (status, palette, compact)) return;
        rendered = (status, palette, compact);
        Paint(primary, compact ? status.Radial : status.Pending > 0 ? WidgetStatusKind.Notifications : WidgetStatusKind.None, status.Count, palette);
        Paint(secondary, compact ? WidgetStatusKind.None : status.Activity, "", palette);
    }
    private static void Paint(Border badge, WidgetStatusKind kind, string count, ShellChromePalette palette)
    {
        badge.Visibility = kind == WidgetStatusKind.None ? Visibility.Collapsed : Visibility.Visible;
        if (kind == WidgetStatusKind.None) return;
        // Semantic contrast from the shell palette, not brand colors or flashing states.
        var emphasized = kind is WidgetStatusKind.Notifications or WidgetStatusKind.Recording or WidgetStatusKind.Failed;
        badge.Background = ShellChromePalette.Brush(emphasized ? palette.Selected : palette.Surface);
        badge.BorderBrush = ShellChromePalette.Brush(palette.HighContrast ? palette.Text : palette.Surface);
        var foreground = ShellChromePalette.Brush(emphasized ? palette.SelectedText : palette.Text);
        if (kind == WidgetStatusKind.Notifications)
            badge.Child = new TextBlock { Text = count, FontSize = 10, FontWeight = new() { Weight = 600 },
                Foreground = foreground, TextAlignment = TextAlignment.Center, TextLineBounds = TextLineBounds.Tight,
                VerticalAlignment = VerticalAlignment.Center };
        else badge.Child = new FontIcon { FontFamily = new("Segoe Fluent Icons"), FontSize = 10, Foreground = foreground,
            Glyph = kind switch { WidgetStatusKind.Audio => "\uE767", WidgetStatusKind.RemotePlayback => "\uE768",
                WidgetStatusKind.Stopped => "\uE71A", WidgetStatusKind.Failed => "\uE7BA", WidgetStatusKind.Recording => "\uE7C8", _ => "" } };
        AutomationProperties.SetAccessibilityView(badge.Child, AccessibilityView.Raw);
    }
}
