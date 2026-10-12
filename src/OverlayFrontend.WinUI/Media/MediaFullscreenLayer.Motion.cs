using System.Numerics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using WidgetRail.OverlayFrontend.WinUI.Motion;
using WidgetRail.PlatformSettings;
using Windows.Foundation;

namespace WidgetRail.OverlayFrontend.WinUI.Media;

internal sealed partial class MediaFullscreenLayer
{
    // Shared tuning point for images, native video and embedded media.
    // Widget animation speed scales this duration; reduced motion skips it.
    internal static readonly TimeSpan DefaultTransitionDuration = TimeSpan.FromMilliseconds(300);
    internal TimeSpan TransitionDuration { get; set; } = DefaultTransitionDuration;
    private AppearanceSettings motionAppearance = AppearanceSettings.Default;
    private bool systemAnimations = true;
    private readonly Border backdrop = new();
    private WidgetCompositionMotion? transition;
    private WidgetCompositionTarget? contentMotion, backdropMotion;
    private MediaFullscreenXamlMotion? xamlMotion;
    private readonly List<WidgetCompositionTarget> chromeMotion = [];
    private bool entranceQueued;

    internal void SetBackdrop(Brush brush) => backdrop.Background = brush;
    internal void ApplyMotion(AppearanceSettings appearance, bool animationsEnabled)
    {
        motionAppearance = appearance; systemAnimations = animationsEnabled;
        if (MotionDuration == TimeSpan.Zero) CancelMotion();
    }
    private TimeSpan MotionDuration => WidgetMotionOptions.From(motionAppearance, systemAnimations)
        .Duration(Math.Clamp(TransitionDuration.TotalMilliseconds, 0, 1000));

    private void InitializeTransition()
    {
        Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
        backdrop.Background = (Brush)Application.Current.Resources["ApplicationPageBackgroundThemeBrush"];
        Children.Insert(0, backdrop);
        SizeChanged += (_, _) => { if (IsTransitioning) CancelMotion(); };
        PreviewKeyDown += (_, args) =>
        {
            if (!IsTransitioning) return;
            args.Handled = true;
            if (args.Key == Windows.System.VirtualKey.Escape) Close();
        };
    }

    internal Rect? BoundsOf(FrameworkElement? element)
    {
        if (element is not { IsLoaded: true, ActualWidth: > 0, ActualHeight: > 0 } ||
            element.XamlRoot != XamlRoot || Parent is not UIElement root) return null;
        return element.TransformToVisual(root).TransformBounds(new(0, 0, element.ActualWidth, element.ActualHeight));
    }

    private WidgetMotionPose InlinePose(Presentation presentation)
    {
        var width = Math.Max(1, presentation.Content.ActualWidth);
        var height = Math.Max(1, presentation.Content.ActualHeight);
        if (presentation.Origin is not { Width: > 0, Height: > 0 } rect)
            return WidgetMotionPose.Identity with { Scale = new(.9f, .9f, 1), Translation = new((float)width * .05f, (float)height * .05f, 0), Opacity = 0 };
        // Uniform scaling keeps images/video undistorted when the inline card
        // has a different aspect ratio from the fullscreen viewport.
        var scale = (float)Math.Min(rect.Width / width, rect.Height / height);
        return WidgetMotionPose.Identity with { Scale = new(scale, scale, 1),
            Translation = new((float)(rect.X + (rect.Width - width * scale) / 2), (float)(rect.Y + (rect.Height - height * scale) / 2), 0) };
    }

    private void PrepareEntrance(Presentation presentation, long version)
    {
        entranceQueued = true;
        if (MotionDuration > TimeSpan.Zero)
        {
            CreateMotionTargets(presentation);
            contentMotion?.Set(InlinePose(presentation));
            xamlMotion?.Set(InlinePose(presentation));
            backdropMotion!.Set(WidgetMotionPose.Identity with { Opacity = 0 });
            foreach (var chrome in chromeMotion) chrome.Set(WidgetMotionPose.Identity with { Opacity = 0 });
        }
        // Seed before first paint, but let native scroll/media controls finish
        // reparenting before restoring zoom or beginning the timeline.
        if (!DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
        {
            if (version != generation || !ReferenceEquals(current, presentation)) return;
            entranceQueued = false;
            UpdateLayout();
            presentation.Prepare?.Invoke();
            _ = OpenTransitionAsync(presentation, version);
        })) CompleteClose(presentation, version, restoreFocus: false);
    }

    private void CreateMotionTargets(Presentation presentation)
    {
        if (transition is not null) return;
        var size = new Vector2((float)Math.Max(1, ActualWidth), (float)Math.Max(1, ActualHeight));
        if (presentation.UseXamlTransform) xamlMotion = new(presentation.Content);
        else
        {
            contentMotion = WidgetCompositionTarget.ForClippedDialog(presentation.Content, size);
            contentMotion.SetAnchor(Vector3.Zero);
        }
        backdropMotion = WidgetCompositionTarget.ForClippedDialog(backdrop, size);
        foreach (var chrome in presentation.Chrome ?? [])
            chromeMotion.Add(WidgetCompositionTarget.ForClippedDialog(chrome, size));
        transition = new(backdropMotion.Compositor, DispatcherQueue);
    }

    private async Task<WidgetMotionOutcome> AnimateAsync(Presentation presentation, bool opening)
    {
        var duration = MotionDuration;
        if (duration == TimeSpan.Zero) { CancelMotion(); return WidgetMotionOutcome.Completed; }
        CreateMotionTargets(presentation);
        var full = WidgetMotionPose.Identity;
        var hidden = full with { Opacity = 0 };
        var items = new List<WidgetMotionPlayback>
        {
            new(backdropMotion!, new(opening ? hidden : full, opening ? full : hidden, duration)),
        };
        if (contentMotion is not null)
        {
            var inline = InlinePose(presentation);
            items.Add(new(contentMotion, new(opening ? inline : full, opening ? full : inline, duration)));
        }
        foreach (var chrome in chromeMotion)
            items.Add(new(chrome, new(opening ? hidden : full, opening ? full : hidden, duration)));
        var compositorPlayback = transition!.PlayAsync(items);
        if (xamlMotion is not null)
            await Task.WhenAll(compositorPlayback, xamlMotion.PlayAsync(opening ? full : InlinePose(presentation), duration));
        return await compositorPlayback;
    }

    private async Task OpenTransitionAsync(Presentation presentation, long version)
    {
        try { await AnimateAsync(presentation, opening: true); }
        catch (Exception error) when (error is not OutOfMemoryException)
        { System.Diagnostics.Trace.WriteLine($"Fullscreen entrance animation skipped: {error.GetType().Name}"); }
        finally
        {
            if (version == generation && ReferenceEquals(current, presentation))
            {
                ResetTransition();
                // Match the no-animation path: focus after layout/reparenting.
                DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
                {
                    if (version != generation || !ReferenceEquals(current, presentation)) return;
                    IsTransitioning = false;
                    presentation.Content.IsHitTestVisible = true;
                    presentation.Settled?.Invoke();
                    Changed?.Invoke();
                    if (CanRestoreFocus?.Invoke() != false) presentation.Focus();
                });
            }
        }
    }

    private async Task CloseTransitionAsync(Presentation presentation, long version)
    {
        try { await AnimateAsync(presentation, opening: false); }
        catch (Exception error) when (error is not OutOfMemoryException)
        { System.Diagnostics.Trace.WriteLine($"Fullscreen exit animation skipped: {error.GetType().Name}"); }
        finally { CompleteClose(presentation, version, restoreFocus: true); }
    }

    private void ResetTransition()
    {
        entranceQueued = false;
        transition?.Dispose(); transition = null;
        contentMotion?.Dispose(); contentMotion = null;
        xamlMotion?.Dispose(); xamlMotion = null;
        backdropMotion?.Dispose(); backdropMotion = null;
        foreach (var chrome in chromeMotion) chrome.Dispose();
        chromeMotion.Clear();
    }

    private void CancelMotion() { transition?.Cancel(); xamlMotion?.Cancel(); }
}
