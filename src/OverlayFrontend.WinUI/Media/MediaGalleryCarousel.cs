using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Media.Imaging;
using WidgetRail.OverlayFrontend.WinUI.Presentation;
using WidgetRail.WidgetProtocol;
using Windows.Foundation;

namespace WidgetRail.OverlayFrontend.WinUI.Media;

/// <summary>Inline gallery stage. Only Center contains a player. Side cells borrow strip art
/// immediately, then acquire bounded, anonymous stills while their occurrences remain adjacent.</summary>
internal sealed partial class MediaGalleryCarousel : Canvas
{
    private const double Gap = 8;
    private readonly Grid track = new() { ColumnSpacing = Gap };
    private readonly Image departing = new() { Stretch = Stretch.UniformToFill, Opacity = .8,
        IsHitTestVisible = false, Visibility = Visibility.Collapsed };
    private readonly TranslateTransform translation = new();
    private readonly RectangleGeometry clip = new();
    private readonly Neighbor previous, next;
    private readonly Func<string, ImageSource?> thumbnail;
    private readonly Func<bool> current;
    private Storyboard? slide;
    private IReadOnlyList<MediaPlayerGalleryItem>? items;
    private int selected = -1;
    private bool previewsEnabled, allowFetch;
    internal Grid Center { get; } = new();

    internal MediaGalleryCarousel(Action<string> select, Func<string, ImageSource?> thumbnail, Func<bool> current)
    {
        this.thumbnail = thumbnail;
        this.current = current;
        Clip = clip;
        AutomationProperties.SetAutomationId(this, "MediaPlayer.Carousel");
        track.RenderTransform = translation;
        for (var i = 0; i < 3; i++) track.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        previous = new(select, "MediaPlayer.Carousel.Previous");
        next = new(select, "MediaPlayer.Carousel.Next");
        track.Children.Add(previous.Surface);
        Grid.SetColumn(Center, 1); track.Children.Add(Center);
        Grid.SetColumn(next.Surface, 2); track.Children.Add(next.Surface);
        track.Children.Add(departing);
        Children.Add(track);
        AutomationProperties.SetAutomationId(Center, "MediaPlayer.Carousel.Current");
        SizeChanged += (_, _) => { StopSlide(); LayoutCells(); };
    }

    protected override Size MeasureOverride(Size available)
    {
        var desired = base.MeasureOverride(available);
        // An auto-height widget still needs a usable initial stage. A sized widget
        // such as Game Details supplies the exact remaining height of the player.
        return double.IsInfinity(available.Height)
            ? new(desired.Width, Math.Min(double.IsFinite(available.Width) ? available.Width : 640, 960) * 9 / 16)
            : desired;
    }

    internal void Update(IReadOnlyList<MediaPlayerGalleryItem> gallery, int index, bool enabled, bool interactive, bool inline, bool animate)
    {
        previous.Surface.IsHitTestVisible = next.Surface.IsHitTestVisible = interactive;
        if (ReferenceEquals(items, gallery) && selected == index && previewsEnabled == enabled && allowFetch == inline) return;
        var oldIndex = selected;
        var oldX = Canvas.GetLeft(track);
        var interrupted = translation.X;
        var departingStill = index > oldIndex ? previous.Picture : next.Picture;
        StopSlide();
        items = gallery;
        selected = index;
        previewsEnabled = enabled;
        allowFetch = inline;
        LayoutCells();
        if (animate && enabled && oldIndex >= 0 && Math.Abs(index - oldIndex) == 1 && double.IsFinite(oldX) && track.Width > 0)
        {
            var stride = (track.Width + Gap) / 3;
            // Keep the outgoing outer slice painted until it crosses the viewport
            // edge. This only borrows the still that was already on screen.
            if (departingStill is not null)
            {
                var forward = index > oldIndex;
                Grid.SetColumn(departing, forward ? 0 : 2);
                departing.RenderTransform = new TranslateTransform { X = forward ? -stride : stride };
                departing.Source = departingStill;
                departing.Visibility = Visibility.Visible;
            }
            var start = interrupted + oldX - Canvas.GetLeft(track) + (index - oldIndex) * stride;
            var animation = new DoubleAnimation { From = start, To = 0, Duration = TimeSpan.FromMilliseconds(180),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
            Storyboard.SetTarget(animation, translation);
            Storyboard.SetTargetProperty(animation, "X");
            slide = new(); slide.Children.Add(animation); slide.Completed += SlideCompleted; slide.Begin();
        }
    }

    private void SlideCompleted(object? sender, object e) => StopSlide();
    internal void StopSlide()
    {
        if (slide is not null) { slide.Completed -= SlideCompleted; slide.Stop(); slide = null; }
        departing.Source = null;
        departing.Visibility = Visibility.Collapsed;
    }

    private void LayoutCells()
    {
        var width = ActualWidth;
        var height = ActualHeight;
        clip.Rect = new(0, 0, width, height);
        if (items is null || width <= 0 || height <= 0) return;
        var cardWidth = Math.Min(width, height * 16 / 9);
        var spare = width - cardWidth;
        var left = selected == 0 ? 0 : selected == items.Count - 1 ? spare : spare / 2;
        track.Width = 3 * cardWidth + 2 * Gap; track.Height = height;
        Canvas.SetLeft(track, left - cardWidth - Gap);
        var pixels = Math.Clamp((int)Math.Ceiling(cardWidth * (XamlRoot?.RasterizationScale ?? 1) / 128) * 128, 128, 3840);
        previous.Set(previewsEnabled && left > Gap && selected > 0 ? items[selected - 1] : null, pixels, allowFetch, thumbnail, current);
        next.Set(previewsEnabled && spare - left > Gap && selected + 1 < items.Count ? items[selected + 1] : null, pixels, allowFetch, thumbnail, current);
    }

    internal ImageSource? PosterFor(string id) => previous.PosterFor(id) ?? next.PosterFor(id) ?? thumbnail(id);
    internal void ThumbnailReady(string id, ImageSource source)
    {
        previous.OfferThumbnail(id, source);
        next.OfferThumbnail(id, source);
    }
    internal void Clear()
    {
        StopSlide();
        items = null;
        selected = -1;
        previous.Clear(); next.Clear();
    }

    private sealed class Neighbor
    {
        private readonly Image picture = new() { Stretch = Stretch.UniformToFill };
        internal Button Surface { get; } = new() { Padding = new(0), BorderThickness = new(0), CornerRadius = new(0),
            IsTabStop = false, AllowFocusOnInteraction = false, HorizontalContentAlignment = HorizontalAlignment.Stretch,
            VerticalContentAlignment = VerticalAlignment.Stretch, Opacity = .8, Visibility = Visibility.Collapsed };
        private CancellationTokenSource? demand;
        private MediaPlayerGalleryItem? item;
        private int requestedWidth;
        internal ImageSource? Picture => picture.Source;

        internal Neighbor(Action<string> select, string automationId)
        {
            Surface.Content = picture;
            AutomationProperties.SetAccessibilityView(Surface, AccessibilityView.Raw);
            AutomationProperties.SetAutomationId(Surface, automationId);
            Surface.Click += (_, _) => { if (item is not null) select(item.Id); };
        }

        internal ImageSource? PosterFor(string id) => item?.Id == id ? picture.Source : null;
        internal void OfferThumbnail(string id, ImageSource source)
        {
            if (item?.Id == id && (picture.Source is null || source is BitmapImage thumb &&
                picture.Source is BitmapImage shown && thumb.PixelWidth > shown.PixelWidth))
                picture.Source = source;
        }

        internal void Set(MediaPlayerGalleryItem? member, int width, bool fetch, Func<string, ImageSource?> thumbnail, Func<bool> current)
        {
            if (item == member && (member is null || !fetch || requestedWidth == width)) return;
            var existing = item == member ? picture.Source : null;
            Clear();
            if (member is null) return;
            item = member; requestedWidth = fetch ? width : 0;
            Surface.Visibility = Visibility.Visible;
            AutomationProperties.SetName(Surface, member.Label);
            picture.Source = existing ?? thumbnail(member.Id);
            if (!fetch) return;
            var url = member.PublicPreviewImage ?? (member.Source is { IsPublicMedia: true, Kind: MediaPlayerSourceKind.WebImage }
                ? member.Source.Location : member.PublicThumbnail);
            demand = new();
            _ = LoadAsync(url!, width, current, demand.Token);
        }

        private async Task LoadAsync(string url, int width, Func<bool> current, CancellationToken token)
        {
            try
            {
                // Coalesce held-direction navigation before starting network or decode work.
                await Task.Delay(90, token);
                if (!current()) return;
                var payload = new NativeArtworkPayload(ReadOnlyMemory<byte>.Empty, new(url), IsPublicMedia: true);
                var decoded = await NativeArtworkDecoder.DecodeAsync(payload, new(width, width * 9 / 16), ImageFit.Contain, token, autoPlay: false);
                if (!token.IsCancellationRequested && current() && decoded is not null &&
                    (picture.Source is not BitmapImage shown || decoded.Decoded.Width >= shown.PixelWidth))
                    picture.Source = decoded.Image;
            }
            catch (Exception error) when (error is not OutOfMemoryException) { /* Keep the thumbnail on cancellation or unavailable artwork. */ }
        }

        internal void Clear()
        {
            demand?.Cancel(); demand?.Dispose(); demand = null;
            item = null; picture.Source = null; Surface.Visibility = Visibility.Collapsed;
        }
    }
}
