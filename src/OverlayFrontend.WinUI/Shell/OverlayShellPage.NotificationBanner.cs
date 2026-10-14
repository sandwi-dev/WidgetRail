using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Hosting;
using WidgetRail.OverlayFrontend.WinUI.Motion;
using WidgetRail.WidgetProtocol;
using WidgetRail.OverlayFrontend.WinUI.Browser;

namespace WidgetRail.OverlayFrontend.WinUI.Shell;

internal sealed partial class OverlayShellPage
{
    private readonly NotificationBannerState notificationBannerState = new();
    private PinnedWidgetWindow? notificationBanner;
    private DispatcherQueueTimer? notificationBannerTimer;
    private NotificationCard? notificationCard;
    private Grid? notificationBannerContent;
    private bool bannerFading;
    private long bannerFadeEnds;
    private void QueueNotificationBanner(NotificationEntry entry, bool allowNew)
    {
        // While navigating the overlay, use its quiet pending indicator. In game
        // this is a nonactivating click-through peer, using the same HWND rules
        // as pins rather than another native layering implementation.
        if (retired || visible || hostWindow == 0 || browserOwner is null || !entry.Available ||
            !notificationBannerState.Offer(entry, Environment.TickCount64, allowNew)) return;
        try
        {
            var entering = notificationBanner?.IsVisible != true;
            notificationBanner ??= CreateNotificationBanner();
            bannerFading = false;
            var palette = ShellChromePalette.Resolve(ShellPalette, Appearance);
            notificationBanner.InterfaceScale = Appearance.InterfaceScale;
            notificationBanner.SurfaceBackground.Background = new SolidColorBrush(palette.Surface);
            notificationBanner.SurfaceBackground.BorderBrush = new SolidColorBrush(palette.HighContrast ? palette.Focus : ShellChromePalette.Blend(palette.Surface, palette.Text, .16));
            notificationBanner.SurfaceBackground.BorderThickness = new(1);
            notificationBanner.SurfaceBackground.CornerRadius = new(12);
            notificationCard!.Foreground = new SolidColorBrush(palette.Text);
            notificationCard.Update(entry, catalogItems.FirstOrDefault(widget => widget.Id == entry.Widget), Appearance.TextScale);
            var id = Microsoft.UI.Win32Interop.GetWindowIdFromWindow((nint)hostWindow);
            var area = OverlayDisplayArea.Resolve(id, id)?.WorkArea;
            if (area is not { } bounds) { HideNotificationBanner(); return; }
            var scale = (XamlRoot?.RasterizationScale ?? 1) * Appearance.InterfaceScale;
            var width = Math.Min((int)Math.Round(420 * scale), Math.Max(1, bounds.Width - 32));
            notificationBannerContent!.Measure(new(width / scale, double.PositiveInfinity));
            var height = Math.Min((int)Math.Ceiling(Math.Clamp(notificationBannerContent.DesiredSize.Height, 92, 240 * Appearance.TextScale) * scale), Math.Max(1, bounds.Height - 32));
            var placements = new PinnedBounds[]
            {
                new(bounds.X + bounds.Width - width - 16, bounds.Y + 16, width, height),
                new(bounds.X + 16, bounds.Y + 16, width, height),
                new(bounds.X + bounds.Width - width - 16, bounds.Y + bounds.Height - height - 16, width, height),
                new(bounds.X + 16, bounds.Y + bounds.Height - height - 16, width, height),
            };
            var placement = placements.FirstOrDefault(candidate => pinned is not { Window.IsVisible: true } pin || !Overlaps(candidate, pin.Window.Bounds));
            if (placement.Width == 0) { HideNotificationBanner(); return; }
            notificationBanner.Place(placement);
            notificationBanner.Show();
            AnimateNotificationBanner(entering, false);
            browserOwner.Notifications.MarkShown(entry.Id);
            notificationBannerTimer!.Start();
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            HideNotificationBanner();
            // Never include title/body/media from a provider in diagnostic text.
            Diagnostics.FrontendFailureLog.Current.Write("notification-banner", null, $"Presentation failed: {error.GetType().Name}");
        }
    }
    private static bool Overlaps(PinnedBounds left, PinnedBounds right) => left.X < right.X + right.Width && left.X + left.Width > right.X &&
        left.Y < right.Y + right.Height && left.Y + left.Height > right.Y;
    private PinnedWidgetWindow CreateNotificationBanner()
    {
        var window = new PinnedWidgetWindow("notification", (nint)hostWindow);
        window.SetHostIdentity("WidgetRail notification", "Overlay.NotificationBanner");
        notificationCard = new(ResolveTrayIconAsync);
        notificationBannerContent = new Grid { Padding = new(16, 8, 16, 8) };
        notificationBannerContent.Children.Add(notificationCard);
        window.SetContent(notificationBannerContent);
        notificationBannerTimer = DispatcherQueue.CreateTimer();
        notificationBannerTimer.Interval = TimeSpan.FromMilliseconds(100);
        notificationBannerTimer.Tick += (_, _) =>
        {
            var now = Environment.TickCount64;
            if (retired || visible || notificationBannerState.EntryId is not { } id || browserOwner?.Notifications.Find(id) is not { Available: true })
            { HideNotificationBanner(); return; }
            if (now >= notificationBannerState.ExpiresAt)
            {
                if (WidgetMotionOptions.From(Appearance, systemUi.AnimationsEnabled).Reduced || bannerFading && now >= bannerFadeEnds)
                { HideNotificationBanner(); return; }
                if (!bannerFading) { bannerFading = true; bannerFadeEnds = now + 160; AnimateNotificationBanner(false, true); }
            }
        };
        return window;
    }
    private void HideNotificationBanner()
    {
        notificationBannerState.Hide(); notificationBannerTimer?.Stop(); notificationBanner?.Hide();
        bannerFading = false; notificationCard?.Clear();
        if (notificationBanner is not null) { var visual = ElementCompositionPreview.GetElementVisual(notificationBanner.SurfaceBackground); visual.StopAnimation("Opacity"); visual.Opacity = 1; }
    }
    private void AnimateNotificationBanner(bool entering, bool leaving)
    {
        if (notificationBanner is null) return;
        var visual = ElementCompositionPreview.GetElementVisual(notificationBanner.SurfaceBackground);
        visual.StopAnimation("Opacity"); visual.Opacity = 1;
        var motion = WidgetMotionOptions.From(Appearance, systemUi.AnimationsEnabled);
        if (motion.Reduced || (!entering && !leaving)) return;
        var fade = visual.Compositor.CreateScalarKeyFrameAnimation();
        fade.InsertKeyFrame(0, leaving ? 1 : 0); fade.InsertKeyFrame(1, leaving ? 0 : 1);
        fade.Duration = TimeSpan.FromMilliseconds(leaving ? 160 : 180);
        visual.StartAnimation("Opacity", fade);
        fade.Dispose();
    }
    private void DisposeNotificationBanner()
    { HideNotificationBanner(); notificationBanner?.Dispose(); notificationBanner = null; notificationBannerTimer = null; }
}
