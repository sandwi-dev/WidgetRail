using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using WidgetRail.WidgetProtocol;
using WidgetRail.OverlayFrontend.WinUI.Motion;
using WidgetRail.PlatformSettings;

namespace WidgetRail.OverlayFrontend.WinUI.Presentation;

/// <summary>Native layout for a noninteractive presentation consumer.</summary>
internal sealed partial class WidgetPresentationSurface : ContentControl, IAsyncDisposable
{
    internal Grid ContentPanel { get; } = new();
    // Box styles paint behind decoded artwork; demand remains with the presenter.
    // Opacity here applies once to this surface subtree.
    internal Grid StylePanel { get; } = new();
    private readonly Grid root = new();
    private readonly WidgetArtworkCrossfade artwork = new();
    // Separate paint layers keep overlays out of the image cache and underneath
    // authored foreground content. Native star rows express the original scrim.
    private readonly WidgetArtworkOverlays artworkOverlays = new() { Visibility = Visibility.Collapsed };
    private NativeArtworkStyle artworkStyle = new(Stretch.UniformToFill);
    internal WidgetViewPresenter? Fragment { get; }
    internal ImageSource? ArtworkSource => artwork.Source;
    private bool retainArtwork;
    private ImageSource? latestArtwork;
    private Stretch latestFit = Stretch.UniformToFill;
    private Stretch paintedFit = Stretch.UniformToFill;
    private Stretch requestedFit = Stretch.UniformToFill;
    internal void SetArtworkStyle(NativeArtworkStyle style)
    {
        requestedFit = latestFit = paintedFit = style.Stretch;
        artwork.SetFit(style.Stretch);
        artwork.SetAlignment(style.AlignmentX, style.AlignmentY);
        artworkStyle = style;
        UpdateArtworkOverlays();
    }
    private void UpdateArtworkOverlays() => artworkOverlays.Update(artworkStyle, artwork.Source is not null);
    internal void PublishArtwork(ImageSource? image) => SetArtworkCore(image, requestedFit);
    internal void RetainArtwork(WidgetPresentationSurface outgoing)
    { paintedFit = outgoing.paintedFit; artwork.SetSource(outgoing.ArtworkSource, paintedFit); UpdateArtworkOverlays(); }
    internal void SetArtworkRetention(bool retain)
    {
        if (retainArtwork && !retain) { paintedFit = latestFit; artwork.SetSource(latestArtwork, paintedFit); }
        retainArtwork = retain;
        UpdateArtworkOverlays();
    }
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
        else
        {
            root.Children.Add(artwork.View);
            root.Children.Add(artworkOverlays);
        }
        root.Children.Add(ContentPanel);
        StylePanel.Children.Add(root);
        Content = StylePanel;
    }
    internal void ConfigureFocusLayout(bool horizontal, string? justify, string? align, double gap, bool contentGrows)
    {
        if (Fragment is null) return;
        // Presentation fragments are an implicit first child of the declared
        // surface. Honor its layout just like ordinary WRSS rows/columns; a
        // bounded poster rail must not consume all remaining vertical space.
        var fragmentVisible = Fragment.Visibility == Visibility.Visible;
        var contentVisible = ContentPanel.Visibility == Visibility.Visible;
        var grows = new List<double>();
        if (fragmentVisible) grows.Add(0);
        if (contentVisible) grows.Add(contentGrows ? 1 : 0);
        var plan = NativeFlowTracks.Create(grows, gap, justify);
        var fragmentSlot = fragmentVisible ? plan.Slots[0] : 0;
        var contentSlot = contentVisible ? plan.Slots[fragmentVisible ? 1 : 0] : 0;
        root.RowDefinitions.Clear(); root.ColumnDefinitions.Clear();
        foreach (var track in plan.Tracks)
        {
            if (horizontal) root.ColumnDefinitions.Add(new() { Width = track });
            else root.RowDefinitions.Add(new() { Height = track });
        }
        Grid.SetRow(Fragment, horizontal ? 0 : fragmentSlot);
        Grid.SetColumn(Fragment, horizontal ? fragmentSlot : 0);
        Grid.SetRow(ContentPanel, horizontal ? 0 : contentSlot);
        Grid.SetColumn(ContentPanel, horizontal ? contentSlot : 0);
        Fragment.HorizontalAlignment = horizontal ? HorizontalAlignment.Stretch : align switch
        { "start" => HorizontalAlignment.Left, "center" => HorizontalAlignment.Center, "end" => HorizontalAlignment.Right, _ => HorizontalAlignment.Stretch };
        Fragment.VerticalAlignment = !horizontal ? VerticalAlignment.Stretch : align switch
        { "start" => VerticalAlignment.Top, "center" => VerticalAlignment.Center, "end" => VerticalAlignment.Bottom, _ => VerticalAlignment.Stretch };
    }
    internal void SetArtwork(ImageSource? source, ImageFit? fit)
        => SetArtworkCore(source, NativeArtworkStyle.Resolve(null, fit, ImageFit.Cover).Stretch);
    private void SetArtworkCore(ImageSource? source, Stretch fit)
    {
        latestArtwork = source; latestFit = fit;
        if (source is null && retainArtwork) return;
        paintedFit = fit;
        artwork.SetSource(source, fit);
        UpdateArtworkOverlays();
    }
    public async ValueTask DisposeAsync()
    {
        artwork.Dispose();
        latestArtwork = null;
        ContentPanel.Children.Clear();
        if (Fragment is not null) await Fragment.DisposeAsync();
        root.Children.Clear(); StylePanel.Children.Clear(); Content = null;
    }
}
