using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WidgetRail.OverlayFrontend.WinUI.Motion;

namespace WidgetRail.OverlayFrontend.WinUI.Presentation;

internal sealed partial class WidgetPresentationSurface
{
    private Grid? foregroundPaint;
    private Border? canvasPaint;
    private MainWindow? openingOwner;
    private string? defaultProcess, selectedProcess, publishedArtKey, requestedArtKey;
    private long observedOpening = -1;
    private bool paintHidden, wantsForeground, selectedBackground;
    internal bool IsForegroundRevealed => paintHidden;
#if ENABLE_WIDGET_VALIDATION
    internal bool HasForegroundPaint => foregroundPaint is not null;
    internal TimeSpan ForegroundTransitionDuration => foregroundPaint?.OpacityTransition?.Duration ?? TimeSpan.Zero;
#endif
    internal event Action<TimeSpan>? ForegroundPaintChanged;
    private WallpaperMotionStyle foregroundMotion = WallpaperMotionStyle.Default;

    internal Border? ConfigureForegroundBackground(string? key, WallpaperMotionStyle motionStyle)
    {
        foregroundMotion = motionStyle;
        defaultProcess = key;
        if (key != null && foregroundPaint == null)
        {
            foregroundPaint = new Grid { IsHitTestVisible = false };
            canvasPaint = new Border();
            root.Children.Remove(artwork.View);
            root.Children.Remove(artworkOverlays);
            foregroundPaint.Children.Add(canvasPaint);
            foregroundPaint.Children.Add(artwork.View);
            foregroundPaint.Children.Add(artworkOverlays);
            root.Children.Insert(0, foregroundPaint);
            Loaded += ForegroundLoaded;
            Unloaded += ForegroundUnloaded;
            if (XamlRoot != null) AttachForegroundBackground();
        }
        return canvasPaint;
    }

    private void ForegroundLoaded(object sender, RoutedEventArgs args) => AttachForegroundBackground();
    private void ForegroundUnloaded(object sender, RoutedEventArgs args)
    {
        if (!IsLoaded) DetachForegroundBackground();
    }
    private void AttachForegroundBackground()
    {
        if (openingOwner != null || App.Window is not MainWindow main || !main.OwnsBackgroundRoot(XamlRoot)) return;
        openingOwner = main;
        main.ForegroundBackgroundChanged += ReconcileForeground;
        ReconcileForeground();
    }
    private void DetachForegroundBackground()
    {
        if (openingOwner != null) openingOwner.ForegroundBackgroundChanged -= ReconcileForeground;
        openingOwner = null;
        observedOpening = -1;
    }
    internal void SelectBackground(string? process, string? artKey)
    {
        selectedBackground = true;
        selectedProcess = process;
        requestedArtKey = artKey;
        ReconcileForeground();
    }
    private void ReconcileForeground(bool animate = true)
    {
        if (openingOwner is not { } main || foregroundPaint == null) return;
        // On a warm open, the retained surface can win the match before the
        // first frame and before native focus has returned. A late match uses
        // the current foreground selection and the normal artwork fade.
        var firstPresentation = observedOpening < 0;
        var initial = observedOpening != main.BackgroundOpening && main.BeforeFirstBackgroundFrame;
        observedOpening = main.BackgroundOpening;
        var eligible = main.MatchesForegroundBackground(defaultProcess);
        wantsForeground = eligible && (!selectedBackground || selectedProcess == defaultProcess);
        if (wantsForeground || !eligible || requestedArtKey == null || requestedArtKey == publishedArtKey)
            SetPaintHidden(wantsForeground, animate && eligible && !firstPresentation && !initial && !main.BeforeFirstBackgroundFrame && IsLoaded);
    }
    private void ShowPaint(bool animate)
    {
        if (foregroundPaint != null) SetPaintHidden(false, animate);
    }
    internal void ForegroundArtworkFailed()
    {
        if (!wantsForeground) ShowPaint(true);
    }
    private void SetPaintHidden(bool hidden, bool animate)
    {
        if (paintHidden == hidden || foregroundPaint is null) return;
        paintHidden = hidden;
        var duration = animate && foregroundMotion.Transition != "none"
            ? WidgetArtworkCrossfade.Duration(foregroundMotion.FadeMs) : TimeSpan.Zero;
        // The XAML facade starts an interrupted scalar transition from its
        // current displayed value. Layout, hit testing and card opacity are independent.
        foregroundPaint.OpacityTransition = duration > TimeSpan.Zero ? new() { Duration = duration } : null;
        foregroundPaint.Opacity = hidden ? 0 : 1;
        ForegroundPaintChanged?.Invoke(duration);
    }
}
