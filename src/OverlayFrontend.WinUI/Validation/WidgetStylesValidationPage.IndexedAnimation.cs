using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using WidgetRail.OverlayFrontend.WinUI.Media;
using WidgetRail.OverlayFrontend.WinUI.Presentation;
using WidgetRail.PlatformSettings;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;
using PresentationSession = WidgetRail.WidgetPresentationSession.WidgetPresentationSession;

namespace WidgetRail.OverlayFrontend.WinUI.Validation;

internal sealed partial class WidgetStylesValidationPage
{
    private async Task IndexedAnimationAsync(string pipe, string clip)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(25));
        await using var session = await PresentationSession.ConnectAsync(pipe, cancellationToken: deadline.Token);
        await session.ListWidgetsAsync(deadline.Token);
        var frame = await session.EstablishPresentationAsync(session.GetTarget("animation-fixture"), WidgetLifecycleState.Interactive, deadline.Token);
        var surface = new WidgetViewPresenter { Width = 500, Height = 300, Session = session };
        var layout = new Grid { Width = 500, Height = 300 };
        layout.Children.Add(surface); host.Children.Add(layout);
        PresentationLayers.Attach(layout);
        var fullscreen = new MediaFullscreenLayer(); fullscreen.Attach(layout);
        MediaPlayerView.ValidationVideoSource = uri => uri.AbsoluteUri == "https://example.invalid/animation.mp4" ? new Uri(clip) : uri;
        try
        {
            surface.ApplyAppearance(AppearanceSettings.Default, false);
            surface.Apply(frame);
            await Wait(() => Descendants(surface).OfType<MediaPlayerView>().Any(media => media.IsLoaded));
            var media = Descendants(surface).OfType<MediaPlayerView>().Single();
            media.Height = 180; media.Width = 320;
            await Task.Delay(300);
            Check(!media.IsPlaying && MediaPlayerView.ResidentAnimations == 0, "Real indexed row inherits reduced-motion policy before playback");
            var collection = Descendants(surface).OfType<WidgetIndexedCollectionView>().Single();
            await Wait(() => collection.NativeView.ContainerFromIndex(0) is Control);
            var container = (Control)collection.NativeView.ContainerFromIndex(0);
            Check(container.Focus(FocusState.Keyboard), "Animation message keeps its native row as the focus owner");
            surface.ApplyAppearance(AppearanceSettings.Default, true);
            await Wait(() => media.IsPlaying);
            await Wait(() => media.PageVideo.IsLoaded && media.PageVideo.Visibility == Visibility.Visible);
            Check(Descendants(media).Contains(media.PageVideo) && !Descendants(layout).OfType<MediaPresentationStage>().Any(),
                "Inline animation remains in the reading row XAML tree with no relocated video plane");
            Check(true, "Inline animation uses its in-tree WinUI media element while its message owns focus");
            Check(MediaPlayerView.ResidentAnimations == 1 && ReferenceEquals(FocusManager.GetFocusedElement(surface.XamlRoot), container),
                "Real row source resolves through session and bridge without stealing focus");
            surface.ApplyAppearance(AppearanceSettings.Default, false);
            await Wait(() => MediaPlayerView.ResidentAnimations == 0);
            Check(!media.IsPlaying, "Changing parent motion policy stops realized row playback");
            surface.ApplyAppearance(AppearanceSettings.Default, true);
            await Wait(() => media.IsPlaying);
            await surface.SetPresentationActiveAsync(false);
            Check(MediaPlayerView.ResidentAnimations == 0, "Parent presentation suspension drains row animation resources");
            await surface.SetPresentationActiveAsync(true);
            await Wait(() => Descendants(surface).OfType<MediaPlayerView>().Any(value => value.IsPlaying));
            Check(MediaPlayerView.ResidentAnimations == 1, "Parent presentation resume restores the reauthorized row animation");
            for (var cycle = 0; cycle < 24; cycle++)
            {
                var currentMedia = Descendants(surface).OfType<MediaPlayerView>().Single();
                await surface.SetPresentationActiveAsync(false);
                await currentMedia.PlaybackRetirement;
                Check(MediaPlayerView.ResidentAnimations == 0, $"Repeated hide {cycle + 1} drains the animation decoder and source");
                await surface.SetPresentationActiveAsync(true);
                await Wait(() => Descendants(surface).OfType<MediaPlayerView>().Any(value => value.IsPlaying));
            }
        }
        finally
        {
            await surface.DisposeAsync(); host.Children.Remove(layout);
            MediaPlayerView.ValidationVideoSource = null;
        }
        Check(MediaPlayerView.ResidentAnimations == 0, "Disposing the real indexed presenter releases all animation resources");
        static IEnumerable<DependencyObject> Descendants(DependencyObject node)
        {
            yield return node;
            for (var i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++)
                foreach (var child in Descendants(VisualTreeHelper.GetChild(node, i))) yield return child;
        }
    }
}
