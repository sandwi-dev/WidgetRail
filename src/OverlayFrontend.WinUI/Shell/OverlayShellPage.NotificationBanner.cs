using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using WidgetRail.OverlayFrontend.WinUI.Browser;

namespace WidgetRail.OverlayFrontend.WinUI.Shell;

internal sealed partial class OverlayShellPage
{
    private readonly NotificationBannerState notificationBannerState = new();
    private PinnedWidgetWindow? notificationBanner;
    private DispatcherQueueTimer? notificationBannerTimer;
    private readonly TextBlock notificationSource = new() { MaxLines = 1, TextTrimming = TextTrimming.CharacterEllipsis, FontSize = 14 };
    private readonly TextBlock notificationTitle = new() { MaxLines = 1, TextTrimming = TextTrimming.CharacterEllipsis, FontSize = 18, FontWeight = new() { Weight = 600 } };
    private readonly TextBlock notificationBody = new() { MaxLines = 2, TextWrapping = TextWrapping.Wrap, TextTrimming = TextTrimming.CharacterEllipsis, FontSize = 14 };
    private void QueueNotificationBanner(NotificationEntry entry, bool allowNew)
    {
        // While navigating the overlay, use its quiet pending indicator. In game
        // this is a nonactivating click-through peer, using the same HWND rules
        // as pins rather than another native layering implementation.
        if (retired || visible || hostWindow == 0 || browserOwner is null || !entry.Available ||
            !notificationBannerState.Offer(entry, Environment.TickCount64, allowNew)) return;
        try
        {
            notificationBanner ??= CreateNotificationBanner();
            var palette = ShellChromePalette.Resolve(ShellPalette, Appearance);
            notificationBanner.InterfaceScale = Appearance.InterfaceScale;
            notificationBanner.SurfaceBackground.Background = new SolidColorBrush(palette.Surface);
            notificationBanner.SurfaceBackground.BorderBrush = new SolidColorBrush(palette.Muted);
            notificationBanner.SurfaceBackground.BorderThickness = new(1);
            notificationBanner.SurfaceBackground.CornerRadius = new(12);
            notificationSource.Foreground = new SolidColorBrush(palette.Muted);
            notificationTitle.Foreground = notificationBody.Foreground = new SolidColorBrush(palette.Text);
            notificationSource.Text = (catalogItems.FirstOrDefault(widget => widget.Id == entry.Widget)?.Name ?? entry.Widget) + " · " + new Uri(entry.Origin).Host;
            notificationTitle.Text = entry.Title;
            notificationBody.Text = entry.Body;
            var id = Microsoft.UI.Win32Interop.GetWindowIdFromWindow((nint)hostWindow);
            var area = OverlayDisplayArea.Resolve(id, id)?.WorkArea;
            if (area is not { } bounds) { HideNotificationBanner(); return; }
            var scale = (XamlRoot?.RasterizationScale ?? 1) * Appearance.InterfaceScale;
            var width = Math.Min((int)Math.Round(420 * scale), Math.Max(1, bounds.Width - 32));
            var height = Math.Min((int)Math.Round(132 * scale), Math.Max(1, bounds.Height - 32));
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
        window.SetContent(new StackPanel { Padding = new(16), Spacing = 4, Children = { notificationSource, notificationTitle, notificationBody } });
        notificationBannerTimer = DispatcherQueue.CreateTimer();
        notificationBannerTimer.Interval = TimeSpan.FromMilliseconds(100);
        notificationBannerTimer.Tick += (_, _) =>
        {
            notificationBannerState.Expire(Environment.TickCount64);
            if (retired || visible || notificationBannerState.EntryId is not { } id || browserOwner?.Notifications.Find(id) is not { Available: true }) HideNotificationBanner();
        };
        return window;
    }
    private void HideNotificationBanner()
    {
        notificationBannerState.Hide(); notificationBannerTimer?.Stop(); notificationBanner?.Hide();
        notificationSource.Text = notificationTitle.Text = notificationBody.Text = "";
    }
    private void DisposeNotificationBanner()
    { HideNotificationBanner(); notificationBanner?.Dispose(); notificationBanner = null; notificationBannerTimer = null; }
}
