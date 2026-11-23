using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;

namespace WidgetRail.OverlayFrontend.WinUI.Presentation;

/// <summary>Playback only; callers retain source authority and decode ownership.</summary>
internal sealed class NativeAnimatedArtwork : IDisposable
{
    private readonly NativeMotionVisibility visibility;
    private readonly WidgetArtworkView? artworkHost;
    private readonly Border? viewport;
    internal NativeAnimatedArtwork(FrameworkElement target, BitmapImage bitmap, Func<bool> allowed)
    {
        if (!bitmap.IsAnimatedBitmap) throw new ArgumentException("Animated artwork is required.", nameof(bitmap));
        if (target is WidgetArtworkView host)
        {
            // WinUI does not replay an unchanged effective viewport when a
            // listener attaches late. Observe a new, unpainted stretched child
            // before its first layout. Static images never need this element.
            artworkHost = host;
            viewport = new Border { IsHitTestVisible = false };
            AutomationProperties.SetAccessibilityView(viewport, AccessibilityView.Raw);
            target = viewport;
        }
        visibility = new(target, allowed, play =>
        {
            if (play && !bitmap.IsPlaying) bitmap.Play();
            else if (!play && bitmap.IsPlaying) bitmap.Stop();
        });
        if (artworkHost is not null) artworkHost.Children.Add(viewport!);
        visibility.Refresh();
    }
    internal void Refresh() => visibility.Refresh();
    public void Dispose()
    {
        visibility.Dispose();
        if (artworkHost is not null) artworkHost.Children.Remove(viewport!);
    }
}
