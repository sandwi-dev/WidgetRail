using Microsoft.UI.Xaml;

namespace WidgetRail.OverlayFrontend.WinUI.Shell;

internal sealed partial class OverlayShellPage
{
    private ShellStatusView? systemStatus;
    private long statusVisibilityToken;

    private void InitializeSystemStatus(bool observeSystem)
    {
        // Layout-only validation must not read real radios or connectivity.
        systemStatus = new(observeSystem ? null : _ => Task.FromResult(ShellStatusSnapshot.Unknown));
        RailStatusHost.Content = systemStatus;
        statusVisibilityToken = RailStatusHost.RegisterPropertyChangedCallback(VisibilityProperty,
            (_, _) => ReconcileSystemStatus());
        MediaPresentationChanged += ReconcileSystemStatus;
        ReconcileSystemStatus();
    }

    private void ReconcileSystemStatus() => systemStatus?.SetActive(
        !retired && visible && !IsMediaFullscreen && RailStatusHost.Visibility == Visibility.Visible);

    private void RefreshSystemStatusAppearance() =>
        systemStatus?.ApplyAppearance(ShellPalette, Appearance, systemUi.AnimationsEnabled);

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
