using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using WidgetRail.WidgetProtocol;
using WidgetRail.OverlayFrontend.WinUI.Motion;
using WidgetRail.PlatformSettings;

namespace WidgetRail.OverlayFrontend.WinUI.Presentation;

/// <summary>Native layout for a noninteractive presentation consumer.</summary>
internal sealed class WidgetPresentationSurface : ContentControl, IAsyncDisposable
{
    internal Grid ContentPanel { get; } = new();
    // Box styles paint behind decoded artwork; demand remains with the presenter.
    // Opacity here applies once to this surface subtree.
    internal Grid StylePanel { get; } = new();
    private readonly Grid root = new();
    private readonly WidgetArtworkCrossfade artwork = new();
    internal WidgetViewPresenter? Fragment { get; }
    internal ImageSource? ArtworkSource => artwork.Source;
    internal WidgetArtworkCrossfade ArtworkMotion => artwork;
    internal static void SetMotionAppearance(AppearanceSettings value, bool animationsEnabled) => WidgetArtworkCrossfade.SetAppearance(value, animationsEnabled);
    internal static void SetSystemHighContrast(bool value) => WidgetArtworkCrossfade.SetHighContrast(value);
    internal WidgetPresentationSurface(ViewNodeKind kind)
    {
        IsTabStop = false;
        HorizontalContentAlignment = HorizontalAlignment.Stretch;
        VerticalContentAlignment = VerticalAlignment.Stretch;
        if (kind == ViewNodeKind.FocusPresentationSurface)
        {
            Fragment = new(presentationOnly: true) { IsHitTestVisible = false };
            root.RowDefinitions.Add(new() { Height = GridLength.Auto });
            root.RowDefinitions.Add(new() { Height = new(1, GridUnitType.Star) });
            root.Children.Add(Fragment);
            Grid.SetRow(ContentPanel, 1);
        }
        else root.Children.Add(artwork.View);
        root.Children.Add(ContentPanel);
        StylePanel.Children.Add(root);
        Content = StylePanel;
    }
    internal void SetArtwork(ImageSource? source, ImageFit? fit)
    {
        artwork.SetSource(source, Fit(fit));
    }
    internal void SetArtworkFit(ImageFit? fit) => artwork.SetFit(Fit(fit));
    private static Stretch Fit(ImageFit? fit) => fit == ImageFit.Contain ? Stretch.Uniform : fit == ImageFit.Fill ? Stretch.Fill : Stretch.UniformToFill;
    public async ValueTask DisposeAsync()
    {
        artwork.Dispose();
        ContentPanel.Children.Clear();
        if (Fragment is not null) await Fragment.DisposeAsync();
        root.Children.Clear(); StylePanel.Children.Clear(); Content = null;
    }
}
