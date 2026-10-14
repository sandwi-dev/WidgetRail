using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using WidgetRail.PlatformSettings;

namespace WidgetRail.OverlayFrontend.WinUI.Shell;

internal sealed partial class OverlayShellPage
{
    private readonly Button notificationBell = new() { Width = 48, Height = 48, Padding = new(4),
        CornerRadius = new(12), VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock notificationCount = new() { FontSize = 11, FontWeight = new() { Weight = 600 } };
    private readonly Border notificationBadge = new() { MinWidth = 18, Height = 18, Padding = new(4, 0, 4, 0),
        CornerRadius = new(9), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top };

    private void InitializeNotificationBell()
    {
        var content = new Grid { Width = 36, Height = 36 };
        content.Children.Add(new FontIcon { Glyph = "\uE7F4", FontFamily = new("Segoe Fluent Icons"), FontSize = 22 });
        notificationBadge.Child = notificationCount;
        content.Children.Add(notificationBadge); notificationBell.Content = content;
        AutomationProperties.SetAutomationId(notificationBell, "Overlay.Notifications");
        notificationBell.Click += async (_, _) => { SetInteractive(false); await ShowNotificationsAsync(); };
        notificationBell.GettingFocus += (_, args) =>
        {
            // Native teardown may temporarily focus the bell. Only explicit
            // pointer/keyboard navigation transfers ownership from a widget.
            if (visible && !switching && !HostChoiceActive &&
                (args.FocusState == FocusState.Pointer || args.Direction != FocusNavigationDirection.None))
            { ClearTrayFocus(); SetInteractive(false); }
        };
        notificationBell.GotFocus += (_, _) => PublishControllerGuide();
        notificationBell.LostFocus += (_, _) => PublishControllerGuide();
        RefreshNotificationBell(); PaintNotificationBell();
    }
    private void RefreshNotificationBell()
    {
        var count = browserOwner?.Notifications.Entries.Count ?? 0;
        notificationCount.Text = count > 99 ? "99+" : count.ToString(System.Globalization.CultureInfo.CurrentCulture);
        notificationBadge.Visibility = count == 0 ? Visibility.Collapsed : Visibility.Visible;
        var label = count == 0 ? "Notifications. Nothing pending" : $"Notifications. {count} pending";
        AutomationProperties.SetName(notificationBell, label);
        ToolTipService.SetToolTip(notificationBell, label);
    }
    private void PaintNotificationBell()
    {
        var palette = ShellChromePalette.Resolve(ShellPalette, Appearance);
        notificationBell.Foreground = new SolidColorBrush(palette.Text);
        notificationBell.Background = new SolidColorBrush(palette.Surface);
        notificationBell.BorderBrush = new SolidColorBrush(palette.HighContrast ? palette.Focus :
            ShellChromePalette.Blend(palette.Surface, palette.Text, .18));
        notificationBell.FocusVisualPrimaryBrush = new SolidColorBrush(palette.Focus);
        notificationBadge.Background = new SolidColorBrush(palette.Selected);
        notificationCount.Foreground = new SolidColorBrush(palette.SelectedText);
    }
    private bool NavigateNotificationBell(FocusNavigationDirection direction)
    {
        if (RailStatusHost.Visibility != Visibility.Visible) return false;
        var left = Appearance.OverlayPosition == OverlayPosition.BottomLeft;
        if (notificationBell.FocusState != FocusState.Unfocused)
        {
            if (direction == (left ? FocusNavigationDirection.Right : FocusNavigationDirection.Left)) FocusTray();
            return true;
        }
        if (direction == (left ? FocusNavigationDirection.Left : FocusNavigationDirection.Right) &&
            FocusedTrayWidget() is { } widget && catalogItems.IndexOf(widget) == (left ? 0 : catalogItems.Count - 1))
        { notificationBell.Focus(FocusState.Keyboard); return true; }
        return false;
    }
}
