using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace WidgetRail.OverlayFrontend.WinUI.Media;

internal sealed partial class MediaPlayerView
{
    private readonly Grid feedback = new()
    {
        HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
        MaxWidth = 480, Margin = new(16), IsHitTestVisible = false, Visibility = Visibility.Collapsed
    };
    private readonly ProgressRing loading = new()
    {
        Width = 32, Height = 32, IsIndeterminate = true, IsActive = false,
        IsTabStop = false, IsHitTestVisible = false, Visibility = Visibility.Collapsed,
        HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
    };

    private void ShowLoading(bool visible = true)
    {
        status.Text = string.Empty;
        status.Visibility = Visibility.Collapsed;
        loading.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        feedback.Visibility = loading.Visibility;
        RefreshLoadingActivity();
    }

    private void HideMediaStatus()
    {
        feedback.Visibility = status.Visibility = loading.Visibility = Visibility.Collapsed;
        loading.IsActive = false;
    }

    private void ShowMediaStatus(string text, bool visible = true)
    {
        HideMediaStatus();
        status.Text = text;
        status.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        feedback.Visibility = status.Visibility;
    }

    private void RefreshLoadingActivity() => loading.IsActive = !disposed && active &&
        !retainedForPageExit && IsLoaded && loading.Visibility == Visibility.Visible;
}
