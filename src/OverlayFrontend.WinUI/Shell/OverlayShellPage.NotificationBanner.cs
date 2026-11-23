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
    private NotificationBannerState? bannerState;
    private NotificationBannerState notificationBannerState => bannerState ??= new(id => notificationCenter?.Inbox.ReleaseBanner(id));
    private PinnedWidgetWindow? notificationBanner;
    private DispatcherQueueTimer? notificationBannerTimer;
    private readonly Canvas bannerStack = new() { IsHitTestVisible = false };
    private sealed class BannerView(NotificationCard card)
    {
        internal NotificationCard Card { get; } = card;
        internal NotificationBannerChrome Chrome { get; } = new(card);
        internal long Id, Revision = -1;
        internal int Additional = -1;
        internal double Top;
        internal double Height;
    }
    private readonly Dictionary<NotificationBannerState.Slot, BannerView> bannerViews = [];
    private NotificationCard? notificationCard => notificationBannerState.Visible.FirstOrDefault() is { } top &&
        bannerViews.TryGetValue(top, out var view) ? view.Card : null;
    private int bannerWidth, renderedBannerCount;
    private long bannerRenderRevision = -1;
    private bool renderingBanners;

    private void RefreshNotificationBannerAppearance()
    {
        foreach (var view in bannerViews.Values) view.Revision = -1;
        bannerRenderRevision = -1;
        if (notificationBanner is not null) PumpNotificationBanners();
    }
    private bool NotificationSourceVisible(string widget) =>
        visible && activeWidget == widget && WidgetSurface.Visibility == Visibility.Visible ||
        pins.Any(pin=>pin.Window.IsVisible && pin.IsCurrent && pin.WidgetId==widget);
    private bool NotificationBannerEligible(NotificationEntry entry) => entry.BannerVisibility switch
    {
        WidgetNotificationVisibility.Always => true,
        WidgetNotificationVisibility.SourceHidden =>
            !NotificationSourceVisible(entry.Widget),
        _ => !visible,
    };
    private void QueueNotificationBanner(NotificationEntry entry, bool allowNew)
    {
        if (retired || !NotificationBannerEligible(entry) || hostWindow == 0 || notificationCenter is null || !entry.Available)
        { notificationCenter?.Inbox.ReleaseBanner(entry.Id); return; }
        if (!notificationBannerState.Offer(entry, Environment.TickCount64, allowNew))
            notificationCenter.Inbox.ReleaseBanner(entry.Id);
        PumpNotificationBanners();
    }

    private void PumpNotificationBanners()
    {
        if (renderingBanners || notificationCenter is null) return;
        renderingBanners = true;
        try
        {
            if (retired) { HideNotificationBanner(); return; }
            var state = notificationBannerState;
            if (state.Visible.Count == 0 && state.Waiting.Count == 0) { HideNotificationBanner(); return; }
            notificationBanner ??= CreateNotificationBanner();
            var now = Environment.TickCount64;
            var id = Microsoft.UI.Win32Interop.GetWindowIdFromWindow((nint)hostWindow);
            if (OverlayDisplayArea.Resolve(id, id)?.WorkArea is not { } area)
            {
                state.Expire(now);
                notificationBanner.Hide();
                notificationBannerTimer!.Start();
                return;
            }
            notificationBanner.InterfaceScale = Appearance.InterfaceScale;
            var scale = (XamlRoot?.RasterizationScale ?? 1) * Appearance.InterfaceScale;
            const double overlap = 2 * NotificationBannerChrome.ShadowGutter - 8;
            var width = Math.Min((int)Math.Round((420 + 2 * NotificationBannerChrome.ShadowGutter) * scale), Math.Max(1, area.Width - 32));
            var logicalWidth = width / scale;
            if (bannerWidth != width || bannerStack.Width != logicalWidth) RefreshNotificationBannerAppearance();
            bannerWidth = width;
            bannerStack.Width = logicalWidth;

            BannerView Measure(NotificationBannerState.Slot slot)
            {
                if (!bannerViews.TryGetValue(slot, out var view))
                    bannerViews.Add(slot, view = new(new NotificationCard(ResolveTrayIconAsync)));
                var entry = slot.Entry;
                if (view.Revision != entry.ContentRevision || view.Id != entry.Id || view.Additional != slot.Additional)
                {
                    var palette = ShellChromePalette.Resolve(ShellPalette, Appearance);
                    var opacity = notificationCenter.Preferences.Category(entry.Widget, entry.Content?.CategoryId ?? WidgetNotificationContract.DefaultCategoryId).Opacity
                        ?? entry.Content?.BackgroundOpacityPercent ?? 100;
                    view.Chrome.Apply(palette, opacity, Appearance.Transparency == PlatformSettings.TransparencyPreference.Reduced, Appearance.AcrylicEffects);
                    view.Card.ApplyBannerPalette(palette);
                    view.Card.Update(entry, catalogItems.FirstOrDefault(widget => widget.Id == entry.Widget), Appearance.TextScale);
                    view.Card.SetAdditionalCount(slot.Additional);
                    view.Chrome.Width = logicalWidth;
                    view.Chrome.Measure(new(logicalWidth, double.PositiveInfinity));
                    view.Height = Math.Ceiling(view.Chrome.DesiredSize.Height);
                    view.Id = entry.Id;
                    view.Revision = entry.ContentRevision;
                    view.Additional = slot.Additional;
                }
                return view;
            }
            double StackHeight() => state.Visible.Sum(slot => Measure(slot).Height) - Math.Max(0, state.Visible.Count - 1) * overlap;
            state.Advance(now, entry => entry.Available && ReferenceEquals(notificationCenter.Inbox.Find(entry.Id), entry) && NotificationBannerEligible(entry),
                slot => FindNotificationPlacement(width, (int)Math.Ceiling(scale * (StackHeight() + Measure(slot).Height -
                    (state.Visible.Count == 0 ? 0 : overlap)))) is not null);
            foreach (var slot in bannerViews.Keys.Where(slot => !state.Visible.Contains(slot) && !state.Waiting.Contains(slot)).ToArray())
            {
                var expired = bannerViews[slot];
                bannerStack.Children.Remove(expired.Chrome);
                expired.Card.Clear(); expired.Chrome.Dispose(); bannerViews.Remove(slot);
            }
            var displayCount = state.Visible.Count;
            var stackHeight = StackHeight();
            PinnedBounds? placement = null;
            while (displayCount > 0)
            {
                placement = FindNotificationPlacement(width, (int)Math.Ceiling(stackHeight * scale));
                if (placement.HasValue) break;
                stackHeight -= Measure(state.Visible[--displayCount]).Height - overlap;
            }
            if (displayCount == 0)
            {
                notificationBanner.Hide();
                if (state.Visible.Count == 0 && state.Waiting.Count == 0) notificationBannerTimer!.Stop();
                else notificationBannerTimer!.Start();
                return;
            }
            if (bannerRenderRevision != state.Revision || renderedBannerCount != displayCount)
            {
                var top = 0d;
                var motion = WidgetMotionOptions.From(Appearance, systemUi.AnimationsEnabled);
                for (var i = 0; i < state.Visible.Count; i++)
                {
                    var view = Measure(state.Visible[i]);
                    if (i >= displayCount)
                    {
                        bannerStack.Children.Remove(view.Chrome);
                        continue;
                    }
                    var entering = !bannerStack.Children.Contains(view.Chrome);
                    if (entering) bannerStack.Children.Add(view.Chrome);
                    Canvas.SetTop(view.Chrome, top);
                    if (!motion.Reduced && (entering || view.Top != top))
                    {
                        ElementCompositionPreview.SetIsTranslationEnabled(view.Chrome, true);
                        var visual = ElementCompositionPreview.GetElementVisual(view.Chrome);
                        using var slide = visual.Compositor.CreateVector3KeyFrameAnimation();
                        slide.SetVector3Parameter("Delta", new(0, entering ? -10 : (float)(view.Top - top), 0));
                        slide.InsertExpressionKeyFrame(0, "this.StartingValue + Delta");
                        slide.InsertKeyFrame(1, System.Numerics.Vector3.Zero);
                        slide.Duration = motion.Duration(180);
                        visual.Properties.StartAnimation("Translation", slide);
                    }
                    view.Top = top;
                    top += view.Height - overlap;
                }
                bannerStack.Height = top + overlap;
                renderedBannerCount = displayCount;
                bannerRenderRevision = state.Revision;
            }
            if (notificationBanner.Bounds != placement!.Value) notificationBanner.Place(placement.Value);
            if (!notificationBanner.IsVisible) notificationBanner.Show();
            for (var i = 0; i < displayCount; i++)
                if (!state.Visible[i].Entry.WasShown) notificationCenter.Inbox.MarkShown(state.Visible[i].Entry.Id);
            notificationBannerTimer!.Start();
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            HideNotificationBanner();
            Diagnostics.FrontendFailureLog.Current.Write("notification-banner", null, $"Presentation failed: {error.GetType().Name}");
        }
        finally { renderingBanners = false; }
    }

    private PinnedBounds? FindNotificationPlacement(int width, int height)
    {
#if ENABLE_WIDGET_VALIDATION
        if (height > validationBannerHeightLimit) return null;
#endif
        var id = Microsoft.UI.Win32Interop.GetWindowIdFromWindow((nint)hostWindow);
        if (OverlayDisplayArea.Resolve(id, id)?.WorkArea is not { } area) return null;
        var obstacles = new List<PinnedBounds>();
        foreach(var pin in pins)if(pin.Window.IsVisible)obstacles.Add(pin.Window.Bounds);
        if(pinAdjustmentToolbar?.Window.IsVisible==true)obstacles.Add(pinAdjustmentToolbar.Window.Bounds);
        var workArea = new PinnedBounds(area.X, area.Y, area.Width, area.Height);
        PinnedBounds? previous = notificationBanner?.IsVisible == true ? notificationBanner.Bounds : null;
        if (visible || retainingExitPresentation)
        {
            var origin = Microsoft.UI.Windowing.AppWindow.GetFromWindowId(id).Position;
            var dpi = XamlRoot?.RasterizationScale ?? 1;
            void Avoid(FrameworkElement element, PinnedSurface? pin = null)
            {
                if (!element.IsLoaded || element.Visibility != Visibility.Visible || element.ActualWidth <= 0 || element.ActualHeight <= 0) return;
                var rect = element.TransformToVisual(null).TransformBounds(new(0, 0, element.ActualWidth, element.ActualHeight));
                var scale = pin?.Window.AutomationRoot.XamlRoot?.RasterizationScale ?? dpi;
                var x = pin?.Window.Bounds.X ?? origin.X;
                var y = pin?.Window.Bounds.Y ?? origin.Y;
                obstacles.Add(new(x + (int)Math.Floor(rect.X * scale), y + (int)Math.Floor(rect.Y * scale),
                    (int)Math.Ceiling(rect.Width * scale), (int)Math.Ceiling(rect.Height * scale)));
            }
            Avoid(RailRegion); Avoid(GuideRegion);
            if (utilityPanel is not null) Avoid(utilityPanel);
            if (radialView is not null) Avoid(radialView);
            foreach (var popup in VisualTreeHelper.GetOpenPopupsForXamlRoot(XamlRoot))
                if (popup.Child is FrameworkElement child) Avoid(child);
            foreach (var pin in pins.Where(pin => pin.Window.IsVisible))
                if (pin.Window.AutomationRoot.XamlRoot is { } root)
                    foreach (var popup in VisualTreeHelper.GetOpenPopupsForXamlRoot(root))
                        if (popup.Child is FrameworkElement child) Avoid(child, pin);
            // Prefer clear desktop space on compact layouts. An expanded widget is
            // a background for banners when its bounds leave no such space.
            var controls = obstacles.Count;
            Avoid(WidgetSurface);
            if (obstacles.Count > controls)
            {
                if (NotificationPlacement.Choose(workArea, width, height, obstacles, previous) is { } clear) return clear;
                obstacles.RemoveAt(controls);
            }
        }
        return NotificationPlacement.Choose(workArea, width, height, obstacles, previous)
            ?? NotificationPlacement.Choose(workArea, width, height, [], previous);
    }
    private PinnedWidgetWindow CreateNotificationBanner()
    {
        var window = new PinnedWidgetWindow("notification", (nint)hostWindow, PeerWindowLayer.Notification);
        window.ThemeChanged += RefreshNotificationBannerAppearance;
        window.SetHostIdentity("WidgetRail notification", "Overlay.NotificationBanner");
        window.SetContent(bannerStack);
        notificationBannerTimer = DispatcherQueue.CreateTimer();
        notificationBannerTimer.Interval = TimeSpan.FromMilliseconds(100);
        notificationBannerTimer.Tick += (_, _) => PumpNotificationBanners();
        return window;
    }
    private void HideNotificationBanner()
    {
        notificationBannerState.Hide();
        notificationBannerTimer?.Stop();
        notificationBanner?.Hide();
        bannerStack.Children.Clear();
        foreach (var view in bannerViews.Values) { view.Card.Clear(); view.Chrome.Dispose(); }
        bannerViews.Clear();
        bannerRenderRevision = -1;
    }
    private void DisposeNotificationBanner()
    { HideNotificationBanner(); notificationBanner?.Dispose(); notificationBanner = null; notificationBannerTimer = null; }
}
