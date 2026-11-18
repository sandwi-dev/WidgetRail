using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Hosting;
using WidgetRail.OverlayFrontend.WinUI.Motion;

namespace WidgetRail.OverlayFrontend.WinUI.Media;

/// <summary>Shared non-layout-changing chrome visibility policy for media surfaces.</summary>
internal sealed class MediaControlsVisibility(FrameworkElement[] controls, Func<bool> canHide, Action beforeHide,
    Func<bool>? ownsInteraction = null, Func<TimeSpan>? fadeDuration = null)
{
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromSeconds(3) };
    private bool pointerOverControls;
    private WidgetCompositionTarget[]? targets;
    private WidgetCompositionMotion? motion;
    private long visibilityVersion;
    internal bool Visible => controls[0].Visibility == Visibility.Visible &&
        (fadeDuration is null || controls[0].IsHitTestVisible);
    internal void Initialize()
    {
        timer.Tick += (_, _) => { if (!pointerOverControls && canHide()) Hide(); };
        foreach (var control in controls)
        {
            control.GettingFocus += (_, args) => { if (fadeDuration is not null && !Visible) args.TryCancel(); };
            control.PointerEntered += (_, _) => { pointerOverControls = true; timer.Stop(); };
            control.PointerExited += (_, _) => { pointerOverControls = false; Reveal(); };
        }
    }
    internal void Reveal()
    {
        var wasVisible = Visible;
        foreach (var control in controls) control.IsHitTestVisible = true;
        if (fadeDuration is null) foreach (var control in controls) control.Visibility = Visibility.Visible;
        else if (!wasVisible) SetVisibility(true);
        timer.Stop();
        if (!pointerOverControls && canHide()) timer.Start();
    }
    internal void Hide()
    {
        if (fadeDuration is not null && !Visible || ownsInteraction?.Invoke() == true) return;
        timer.Stop(); beforeHide();
        foreach (var control in controls) control.IsHitTestVisible = false;
        SetVisibility(false);
    }
    private void SetVisibility(bool visible)
    {
        var version = ++visibilityVersion;
        var duration = fadeDuration?.Invoke() ?? TimeSpan.Zero;
        if (duration <= TimeSpan.Zero || controls.Any(control => !control.IsLoaded || control.ActualWidth <= 0 || control.ActualHeight <= 0))
        {
            ReleaseMotion();
            foreach (var control in controls) control.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
            return;
        }
        foreach (var control in controls) control.Visibility = Visibility.Visible;
        // Callers opting into motion supply controls with an independent opacity
        // channel. Fullscreen placement owns their outer wrapper, not this layer.
        targets ??= controls.Select(control => WidgetCompositionTarget.ForClippedDialog(control,
            new((float)control.ActualWidth, (float)control.ActualHeight))).ToArray();
        motion ??= new(ElementCompositionPreview.GetElementVisual(controls[0]).Compositor, controls[0].DispatcherQueue);
        var recipe = new WidgetMotionRecipe(WidgetMotionPose.Identity with { Opacity = visible ? 0 : 1 },
            WidgetMotionPose.Identity with { Opacity = visible ? 1 : 0 }, duration);
        _ = FinishVisibilityAsync(motion.PlayAsync(targets.Select(target => new WidgetMotionPlayback(target, recipe))), version, visible);
    }
    private async Task FinishVisibilityAsync(Task<WidgetMotionOutcome> playback, long version, bool visible)
    {
        try { await playback; }
        catch (Exception error) { Diagnostics.FrontendFailureLog.Current.Write("media-controls-motion", error); }
        finally
        {
            if (version == visibilityVersion)
            {
                foreach (var control in controls) control.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
                ReleaseMotion();
            }
        }
    }
    private void ReleaseMotion()
    {
        motion?.Dispose(); motion = null;
        if (targets is not null) foreach (var target in targets) target.Dispose();
        targets = null;
    }
    internal void Stop()
    {
        timer.Stop(); pointerOverControls = false;
        if (fadeDuration is null) return;
        var visible = Visible;
        ++visibilityVersion; ReleaseMotion();
        foreach (var control in controls) control.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
    }
}
