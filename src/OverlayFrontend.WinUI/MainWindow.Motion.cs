using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using WidgetRail.OverlayFrontend.WinUI.Motion;
using WidgetRail.OverlayFrontend.WinUI.Shell;

namespace WidgetRail.OverlayFrontend.WinUI;

public sealed partial class MainWindow
{
    private OverlayVisibilityMotion? overlayMotion;
    private bool overlayRequestedVisible;
    private bool overlayOpenPending;
    private long overlayVisibilityVersion;
    private WidgetMotionRecipe? overlayMotionRecipe;
    private bool overlayMotionUnavailable;

    private void EnsureOverlayMotion()
    {
        if (overlayMotion is not null || desktopBackdrop is null || overlayMotionUnavailable) return;
        try { overlayMotion = new(ShellRoot, desktopBackdrop.MotionLayer); }
        catch (Exception error)
        {
            overlayMotionUnavailable = true;
            Diagnostics.FrontendFailureLog.Current.Write("overlay-motion-initialization", error);
            return;
        }
        ShellRoot.Loaded += OverlayMotionLoaded;
        ShellRoot.SizeChanged += OverlayMotionSizeChanged;
        ShellRoot.PreviewKeyDown += OverlayExitKey;
        ShellRoot.PreviewKeyUp += OverlayExitKey;
        UpdateOverlayMotionAnchor();
    }

    private void OverlayMotionLoaded(object sender, RoutedEventArgs args) => QueueOverlayOpen();
    private void OverlayExitKey(object sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs args)
    { if (!overlayRequestedVisible) args.Handled = true; }
    private void OverlayMotionSizeChanged(object sender, SizeChangedEventArgs args) => UpdateOverlayMotionAnchor();
    private void UpdateOverlayMotionAnchor()
    {
        if (RootFrame.Content is OverlayShellPage page)
            overlayMotion?.SetAnchor(ShellRoot.ActualWidth, ShellRoot.ActualHeight, page.Appearance.OverlayPosition);
    }

    private void QueueOverlayOpen()
    {
        if (!overlayOpenPending || !ShellRoot.IsLoaded || cleanupStarted) return;
        var version = overlayVisibilityVersion;
        // Loaded/native layout owns readiness. No worker wait or managed frame
        // loop; even a cold widget can show its existing loading presentation.
        DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () =>
        {
            if (!overlayOpenPending || !overlayRequestedVisible || version != overlayVisibilityVersion || cleanupStarted) return;
            overlayOpenPending = false;
            UpdateOverlayMotionAnchor();
            _ = PlayOverlayVisibilityAsync(version, opening: true);
        });
    }

    private async Task PlayOverlayVisibilityAsync(long version, bool opening)
    {
        // Appearance updates can start/restart entrance directly, superseding
        // its queued Loaded callback. Every actual entrance consumes that intent.
        if (opening && version == overlayVisibilityVersion && overlayRequestedVisible) overlayOpenPending = false;
        try
        {
            if (overlayMotion is not null && RootFrame.Content is OverlayShellPage page)
            {
                overlayMotionRecipe = WidgetMotionPolicy.OverlayVisibility(page.Appearance, page.SystemAnimationsEnabled, opening);
                var outcome = await overlayMotion.Play(opening, page.Appearance, page.SystemAnimationsEnabled);
                if (outcome == WidgetMotionOutcome.Superseded) return;
            }
        }
        catch (Exception error)
        {
            Diagnostics.FrontendFailureLog.Current.Write("overlay-motion", error);
            if (version == overlayVisibilityVersion) overlayMotion?.Snap(opening);
        }
        if (cleanupStarted || version != overlayVisibilityVersion || overlayRequestedVisible != opening) return;
        if (!opening)
        {
            if (taskHandoff is { } handoff && handoff.Version == version) handoff.MotionFinished.TrySetResult();
            else CompleteOverlayHide();
        }
    }

    private void RefreshOverlayMotionPolicy()
    {
        UpdateOverlayMotionAnchor();
        if (taskHandoff is { MotionStarted: false }) return;
        // In particular, Reduced/System-off settles an in-flight exit and
        // executes its physical hide. Old completions cannot win after reopen.
        if (!cleanupStarted && overlayMotion?.Playback is { IsCompleted: false } && RootFrame.Content is OverlayShellPage page &&
            overlayMotionRecipe != WidgetMotionPolicy.OverlayVisibility(page.Appearance, page.SystemAnimationsEnabled, overlayRequestedVisible))
            _ = PlayOverlayVisibilityAsync(taskHandoff is null ? ++overlayVisibilityVersion : overlayVisibilityVersion, overlayRequestedVisible);
    }

    private void HideOverlayImmediately()
    {
        CancelTaskHandoff();
        overlayRequestedVisible = false;
        overlayOpenPending = false;
        ++overlayVisibilityVersion;
        ShellRoot.IsHitTestVisible = false;
        overlayMotion?.Snap(false);
        (RootFrame.Content as OverlayShellPage)?.SetVisible(false);
        input?.SetVisible(false);
        CompleteOverlayHide();
    }

    private void CompleteOverlayHide()
    {
        AppWindow.Hide();
        (RootFrame.Content as OverlayShellPage)?.CompleteExitPresentation();
    }

    private void RetireOverlayMotion()
    {
        ++overlayVisibilityVersion;
        ShellRoot.Loaded -= OverlayMotionLoaded;
        ShellRoot.SizeChanged -= OverlayMotionSizeChanged;
        ShellRoot.PreviewKeyDown -= OverlayExitKey;
        ShellRoot.PreviewKeyUp -= OverlayExitKey;
        overlayMotion?.Dispose(); overlayMotion = null;
    }
}
