using System.Globalization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using WidgetRail.OverlayFrontend.WinUI.Presentation;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetBridge;
using WidgetRail.WidgetPresentationSession;

namespace WidgetRail.OverlayFrontend.WinUI.Shell;

/// <summary>Shared notification hierarchy for the passive banner and pending list.</summary>
internal sealed partial class NotificationCard : ContentControl
{
    private readonly WidgetCatalogItemContent icon;
    private readonly Image artwork = new() { Stretch = Stretch.UniformToFill };
    private readonly SymbolIcon artworkFallback = new(Symbol.Pictures);
    private readonly Border artworkFrame = new() { Width = 64, Height = 64, CornerRadius = new(8), VerticalAlignment = VerticalAlignment.Top };
    private string? artworkUrl;
    private CancellationTokenSource? artworkRequest;
    private readonly TextBlock source = new() { MaxLines = 1, TextTrimming = TextTrimming.CharacterEllipsis, Opacity = .8 };
    private readonly TextBlock time = new() { VerticalAlignment = VerticalAlignment.Center, Opacity = .8,
        MaxLines = 1, TextTrimming = TextTrimming.CharacterEllipsis, TextAlignment = TextAlignment.Right };
    internal readonly TextBlock TitleText = new() { MaxLines = 2, TextWrapping = TextWrapping.Wrap,
        TextTrimming = TextTrimming.CharacterEllipsis, FontWeight = new() { Weight = 600 } };
    private readonly TextBlock body = new() { MaxLines = 2, TextWrapping = TextWrapping.Wrap, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly TextBlock stale = new() { Text = "Session ended", Opacity = .7 };

    internal NotificationCard(Func<BridgeWidgetDescriptor, string, CancellationToken, Task<WidgetPresentationPackageIcon>> resolve)
    {
        IsTabStop = false; IsHitTestVisible = false;
        HorizontalContentAlignment = HorizontalAlignment.Stretch;
        var layout = new Grid { ColumnSpacing = 12, Padding = new(4, 8, 4, 8) };
        layout.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        layout.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        icon = new(resolve) { ShowLabel = false, IconSize = 28, Width = 32, VerticalAlignment = VerticalAlignment.Top, Margin = new(0, 4, 0, 0) };
        var text = new StackPanel { Spacing = 4 };
        var metadata = new Grid { ColumnSpacing = 12 };
        metadata.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        metadata.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        metadata.Children.Add(source); metadata.Children.Add(time); Grid.SetColumn(time, 1);
        text.Children.Add(metadata); text.Children.Add(TitleText); text.Children.Add(body); text.Children.Add(stale);
        var artworkStage = new Grid(); artworkStage.Children.Add(artworkFallback); artworkStage.Children.Add(artwork);
        artworkFrame.Child = artworkStage;
        var leading = new StackPanel { Spacing = 8, Children = { artworkFrame, icon } };
        layout.Children.Add(leading); layout.Children.Add(text); Grid.SetColumn(text, 1);
        Loaded += (_, _) => LoadArtwork();
        Unloaded += (_, _) => { artworkRequest?.Cancel(); artworkRequest?.Dispose(); artworkRequest = null; artwork.Source = null; };
        Content = layout;
    }
    internal void Clear()
    {
        icon.DataContext = null;
        artworkUrl = null; artworkRequest?.Cancel(); artwork.Source = null;
        source.Text = time.Text = TitleText.Text = body.Text = "";
        stale.Visibility = Visibility.Collapsed;
    }
    internal void ApplyBannerPalette(ShellChromePalette palette)
    {
        Foreground = new SolidColorBrush(palette.Text);
        source.Opacity = time.Opacity = stale.Opacity = 1;
        source.Foreground = time.Foreground = stale.Foreground = new SolidColorBrush(palette.HighContrast
            ? palette.Text : ShellChromePalette.Blend(palette.Surface, palette.Text, .74));
        body.Foreground = new SolidColorBrush(palette.HighContrast
            ? palette.Text : ShellChromePalette.Blend(palette.Surface, palette.Text, .92));
    }
    internal void Update(NotificationEntry entry, BridgeWidgetDescriptor? widget, double textScale)
    {
        icon.DataContext = widget;
        icon.Visibility = widget is null || entry.Content?.ShowWidgetIcon == false ? Visibility.Collapsed : Visibility.Visible;
        artworkFrame.Visibility = entry.Content?.ArtworkUrl is null ? Visibility.Collapsed : Visibility.Visible;
        if (artworkUrl != entry.Content?.ArtworkUrl)
        {
            artworkUrl = entry.Content?.ArtworkUrl; artworkRequest?.Cancel(); artwork.Source = null;
            artworkFallback.Visibility = Visibility.Visible;
            if (IsLoaded) LoadArtwork();
        }
        var domain = Uri.TryCreate(entry.Origin, UriKind.Absolute, out var uri) ? uri.Host : "";
        source.Text = (widget?.Name ?? entry.Widget) + (domain.Length > 0 ? " · " + domain : "");
        time.Text = entry.Content?.Metadata ?? entry.ReceivedAt.LocalDateTime.ToString(entry.ReceivedAt.Date == DateTimeOffset.Now.Date ? "t" : "g", CultureInfo.CurrentCulture);
        TitleText.Text = entry.Title; body.Text = entry.Body;
        body.Visibility = string.IsNullOrWhiteSpace(entry.Body) ? Visibility.Collapsed : Visibility.Visible;
        stale.Visibility = entry.Available ? Visibility.Collapsed : Visibility.Visible;
        source.FontSize = time.FontSize = stale.FontSize = 12 * textScale;
        time.MaxWidth = 140 * textScale;
        TitleText.FontSize = 17 * textScale; body.FontSize = 14 * textScale;
    }
    private async void LoadArtwork()
    {
        if (artworkUrl is not { } url || artwork.Source is not null) return;
        artworkRequest?.Cancel(); artworkRequest?.Dispose();
        using var request = new CancellationTokenSource(TimeSpan.FromSeconds(5)); artworkRequest = request;
        try
        {
            var pixels = NativeArtworkDemand.TargetPixels(artworkFrame);
            var decoded = await NativeArtworkDecoder.DecodeAsync(new(ReadOnlyMemory<byte>.Empty, new Uri(url)),
                new(Math.Clamp(pixels.Width, 96, 512), Math.Clamp(pixels.Height, 96, 512)), ImageFit.Cover, request.Token);
            if (!request.IsCancellationRequested && ReferenceEquals(artworkRequest, request) && artworkUrl == url)
            { artwork.Source = decoded?.Image; artworkFallback.Visibility = decoded is null ? Visibility.Visible : Visibility.Collapsed; }
        }
        catch (Exception error) when (error is not OutOfMemoryException) { /* Artwork is optional; content remains readable. */ }
        finally { if (ReferenceEquals(artworkRequest, request)) artworkRequest = null; }
    }

}
