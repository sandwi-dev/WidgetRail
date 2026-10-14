using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace WidgetRail.OverlayFrontend.WinUI.Shell;

internal sealed partial class OverlayShellPage
{
    private ShellStatusView? systemStatus;
    private long statusVisibilityToken;

    private void InitializeSystemStatus(bool observeSystem)
    {
        // Layout-only validation must not read real radios or connectivity.
        systemStatus = new(observeSystem ? null : _ => Task.FromResult(ShellStatusSnapshot.Unknown));
        var statusRow = new Grid { ColumnSpacing = 4 };
        statusRow.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        statusRow.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        statusRow.Children.Add(systemStatus);
        statusRow.Children.Add(notificationBell); Grid.SetColumn(notificationBell, 1);
        statusRow.SizeChanged += (_, _) =>
        {
            if (systemStatus is { } status) status.Visibility = statusRow.ActualWidth >= 152
                ? Visibility.Visible : Visibility.Collapsed;
        };
        RailStatusHost.Content = statusRow;
        statusVisibilityToken = RailStatusHost.RegisterPropertyChangedCallback(VisibilityProperty,
            (_, _) => ReconcileSystemStatus());
        MediaPresentationChanged += ReconcileSystemStatus;
        ReconcileSystemStatus();
    }

    private void ReconcileSystemStatus() => systemStatus?.SetActive(
        !retired && visible && !IsMediaFullscreen && RailStatusHost.Visibility == Visibility.Visible);

    private void RefreshSystemStatusAppearance()
    {
        systemStatus?.ApplyAppearance(ShellPalette, Appearance, systemUi.AnimationsEnabled);
        PaintNotificationBell();
    }

    private async Task DisposeSystemStatusAsync()
    {
        MediaPresentationChanged -= ReconcileSystemStatus;
        RailStatusHost.UnregisterPropertyChangedCallback(VisibilityProperty, statusVisibilityToken);
        if (systemStatus is not { } status) return;
        systemStatus = null;
        RailStatusHost.Content = null;
        await status.DisposeAsync();
    }
}
