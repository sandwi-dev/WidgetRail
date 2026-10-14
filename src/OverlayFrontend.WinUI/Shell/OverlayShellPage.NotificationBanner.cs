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
    private int bannerWidth, bannerHeight;
    private void QueueNotificationBanner(NotificationEntry entry, bool allowNew)
    {
        // Source visibility policy and visible obstacles vary; both modes use
        // the same nonactivating click-through peer and shared placement policy.
        var previous = notificationBannerState.EntryId;
        if (retired || visible && !entry.ShowWithOverlay || hostWindow == 0 || notificationCenter is null || !entry.Available ||
            !notificationBannerState.Offer(entry, Environment.TickCount64, allowNew))
        { notificationCenter?.Inbox.ReleaseBanner(entry.Id); return; }
        if (previous is { } old && old != entry.Id) notificationCenter.Inbox.ReleaseBanner(old);
        try
        {
            var entering = notificationBanner?.IsVisible != true;
            notificationBanner ??= CreateNotificationBanner();
            bannerFading = false;
            var palette = ShellChromePalette.Resolve(ShellPalette, Appearance);
            notificationBanner.InterfaceScale = Appearance.InterfaceScale;
            notificationBanner.SurfaceBackground.Background = new SolidColorBrush(palette.HighContrast ? palette.Surface : palette.Surface with { A = (byte)(255 * (entry.Content?.BackgroundOpacityPercent ?? 100) / 100) });
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
            bannerWidth = width; bannerHeight = height;
            if (!PlaceNotificationBanner()) { HideNotificationBanner(); return; }
            notificationBanner.Show();
            AnimateNotificationBanner(entering, false);
            notificationCenter.Inbox.MarkShown(entry.Id);
            notificationBannerTimer!.Start();
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            HideNotificationBanner();
            // Never include title/body/media from a provider in diagnostic text.
            Diagnostics.FrontendFailureLog.Current.Write("notification-banner", null, $"Presentation failed: {error.GetType().Name}");
        }
    }
    private bool PlaceNotificationBanner()
    {
        if (notificationBanner is null) return false;
        var id = Microsoft.UI.Win32Interop.GetWindowIdFromWindow((nint)hostWindow);
        if (OverlayDisplayArea.Resolve(id, id)?.WorkArea is not { } area) return false;
        var obstacles = new List<PinnedBounds>();
        if (pinned is { Window.IsVisible: true } pin) obstacles.Add(pin.Window.Bounds);
        if (visible || retainingExitPresentation)
        {
            if (SystemFilePickerOpen || nativeBrowserPickerActive || LocalInstallActive) return false;
            var origin = Microsoft.UI.Windowing.AppWindow.GetFromWindowId(id).Position;
            var dpi = XamlRoot?.RasterizationScale ?? 1;
            void Avoid(FrameworkElement element)
            {
                if (!element.IsLoaded || element.Visibility != Visibility.Visible || element.ActualWidth <= 0 || element.ActualHeight <= 0) return;
                var rect = element.TransformToVisual(null).TransformBounds(new(0, 0, element.ActualWidth, element.ActualHeight));
                obstacles.Add(new(origin.X + (int)Math.Floor(rect.X * dpi), origin.Y + (int)Math.Floor(rect.Y * dpi),
                    (int)Math.Ceiling(rect.Width * dpi), (int)Math.Ceiling(rect.Height * dpi)));
            }
            Avoid(WidgetSurface); Avoid(RailRegion); Avoid(TrayHelp);
            if (radialView is not null) Avoid(radialView);
            foreach (var popup in VisualTreeHelper.GetOpenPopupsForXamlRoot(XamlRoot))
                if (popup.Child is FrameworkElement child) Avoid(child);
            if (IsMediaFullscreen) return false;
        }
        var placement = NotificationPlacement.Choose(new(area.X, area.Y, area.Width, area.Height), bannerWidth, bannerHeight,
            obstacles, notificationBanner.IsVisible ? notificationBanner.Bounds : null);
        if (placement is not { } bounds) return false;
        if (notificationBanner.Bounds != bounds) notificationBanner.Place(bounds);
        return true;
    }
    private PinnedWidgetWindow CreateNotificationBanner()
    {
        var window = new PinnedWidgetWindow("notification", (nint)hostWindow, PeerWindowLayer.Notification);
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
            if (retired || notificationBannerState.EntryId is not { } id || notificationCenter?.Inbox.Find(id) is not { Available: true } entry || visible && !entry.ShowWithOverlay || !PlaceNotificationBanner())
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
        if (notificationBannerState.EntryId is { } id) notificationCenter?.Inbox.ReleaseBanner(id);
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
