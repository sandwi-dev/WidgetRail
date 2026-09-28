using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.OverlayFrontend.WinUI.Presentation;

/// <summary>Native layout for a noninteractive presentation consumer.</summary>
internal sealed class WidgetPresentationSurface : ContentControl, IAsyncDisposable
{
    internal Grid ContentPanel { get; } = new();
    private readonly Grid root = new();
    private readonly ImageBrush artwork = new() { Stretch = Stretch.UniformToFill };
    internal WidgetViewPresenter? Fragment { get; }
    internal ImageSource? ArtworkSource => artwork.ImageSource;
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
        else root.Background = artwork;
        root.Children.Add(ContentPanel);
        Content = root;
    }
    internal void SetArtwork(ImageSource? source, ImageFit? fit)
    {
        SetArtworkFit(fit);
        artwork.ImageSource = source;
    }
    internal void SetArtworkFit(ImageFit? fit) => artwork.Stretch = fit == ImageFit.Contain ? Stretch.Uniform : fit == ImageFit.Fill ? Stretch.Fill : Stretch.UniformToFill;
    public async ValueTask DisposeAsync()
    {
        artwork.ImageSource = null;
        ContentPanel.Children.Clear();
        if (Fragment is not null) await Fragment.DisposeAsync();
        root.Children.Clear(); Content = null;
    }
}
