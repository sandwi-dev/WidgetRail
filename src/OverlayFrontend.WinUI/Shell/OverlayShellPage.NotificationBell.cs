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
    private readonly TextBlock notificationCount = new() { FontSize = 11, FontWeight = new() { Weight = 600 },
        HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
        TextAlignment = TextAlignment.Center, TextLineBounds = TextLineBounds.Tight };
    private readonly Border notificationBadge = new() { MinWidth = 18, Height = 18, Padding = new(4, 0, 4, 0),
        CornerRadius = new(9), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top };

    private void InitializeNotificationBell()
    {
        var content = new Grid { Width = 36, Height = 36 };
        content.Children.Add(new FontIcon { Glyph = "\uEA8F", FontFamily = new("Segoe Fluent Icons"), FontSize = 22 }); // Ringer (bell)
        notificationBadge.Child = notificationCount;
        content.Children.Add(notificationBadge); notificationBell.Content = content;
        AutomationProperties.SetAutomationId(notificationBell, "Overlay.Notifications");
        InitializeUtilities();
        RefreshNotificationBell(); PaintNotificationBell();
    }
    private void RefreshNotificationBell()
    {
        var count = browserOwner?.Notifications.Entries.Count ?? 0;
        notificationCount.Text = count > 99 ? "99+" : count.ToString(System.Globalization.CultureInfo.CurrentCulture);
        notificationBadge.Visibility = count == 0 ? Visibility.Collapsed : Visibility.Visible;
        var label = count == 0 ? "Notifications. Nothing pending" : $"Notifications. {count} pending";
        AutomationProperties.SetName(notificationBell, label);
    }
    private void PaintNotificationBell()
    {
        var palette = ShellChromePalette.Resolve(ShellPalette, Appearance);
        foreach (var icon in new[] { notificationBell, taskSwitcherButton })
        {
            icon.Foreground = new SolidColorBrush(palette.Text);
            icon.Background = new SolidColorBrush(palette.HighContrast ? palette.Surface : ShellChromePalette.Blend(palette.Surface, palette.Text, .025));
            icon.BorderBrush = new SolidColorBrush(palette.HighContrast ? palette.Text : ShellChromePalette.Blend(palette.Surface, palette.Text, .08));
            icon.FocusVisualPrimaryBrush = new SolidColorBrush(palette.Focus);
        }
        utilityPlacement = null;
        PositionUtilityPanel();
        notificationBadge.Background = new SolidColorBrush(palette.Selected);
        notificationCount.Foreground = new SolidColorBrush(palette.SelectedText);
    }
    private bool NavigateNotificationBell(FocusNavigationDirection direction)
    {
        if (direction == FocusNavigationDirection.Up && utilityPanel is not null)
            WindowActivationDiagnostic?.Invoke($"Utility Up rail={RailStatusHost.Visibility} listFocus={UtilityListFocused} iconFocus={UtilityIconFocused} rows={utilityWindows.Count}");
        if (RailStatusHost.Visibility != Visibility.Visible) return false;
        var left = Appearance.OverlayPosition == OverlayPosition.BottomLeft;
        if (UtilityListFocused)
        {
            if (direction is FocusNavigationDirection.Up or FocusNavigationDirection.Down)
                utilityPanel?.Move(direction == FocusNavigationDirection.Up ? -1 : 1);
            return true;
        }
        if (UtilityIconFocused)
        {
            if (direction == FocusNavigationDirection.Up) EnterUtility();
            else if (direction is FocusNavigationDirection.Left or FocusNavigationDirection.Right)
            {
                var next = taskSwitcherButton.FocusState != FocusState.Unfocused ?
                    (direction == FocusNavigationDirection.Left ? notificationBell : null) :
                    (direction == FocusNavigationDirection.Right ? taskSwitcherButton : null);
                if (next is not null) FocusUtility(ReferenceEquals(next, taskSwitcherButton) ? ShellUtility.Tasks : ShellUtility.Notifications);
                else if (direction == (left ? FocusNavigationDirection.Right : FocusNavigationDirection.Left))
                { CloseUtilityPanel(); FocusTray(); }
            }
            return true;
        }
        if (direction == (left ? FocusNavigationDirection.Left : FocusNavigationDirection.Right) &&
            FocusedTrayWidget() is { } widget && catalogItems.IndexOf(widget) == (left ? 0 : catalogItems.Count - 1))
        { (left ? taskSwitcherButton : notificationBell).Focus(FocusState.Keyboard); return true; }
        return false;
    }
}
