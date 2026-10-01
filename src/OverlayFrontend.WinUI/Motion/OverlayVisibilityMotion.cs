using System.Numerics;
using Microsoft.UI.Xaml;
using WidgetRail.PlatformSettings;

namespace WidgetRail.OverlayFrontend.WinUI.Motion;

/// <summary>One compositor batch for shell zoom/fade and the independent scrim fade.</summary>
internal sealed class OverlayVisibilityMotion : IDisposable
{
    private readonly WidgetCompositionTarget shell;
    private readonly WidgetCompositionTarget backdrop;
    private readonly WidgetCompositionMotion motion;
    private bool disposed;
    internal Task<WidgetMotionOutcome>? Playback { get; private set; }

    internal OverlayVisibilityMotion(UIElement shellLayer, UIElement backdropLayer)
    {
        shell = WidgetCompositionTarget.ForClippedDialog(shellLayer, Vector2.One);
        backdrop = WidgetCompositionTarget.ForClippedDialog(backdropLayer, Vector2.One);
        motion = new(shell.Compositor, shellLayer.DispatcherQueue);
        Snap(false);
    }

    internal void SetAnchor(double width, double height, OverlayPosition position) => shell.SetAnchor(new(
        (float)(Math.Max(0, width) * (position == OverlayPosition.BottomLeft ? 0 : position == OverlayPosition.BottomRight ? 1 : .5)),
        (float)Math.Max(0, height), 0));

    internal Task<WidgetMotionOutcome> Play(bool opening, AppearanceSettings appearance, bool systemAnimations)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        return Playback = motion.PlayAsync(new[]
        {
            new WidgetMotionPlayback(shell, WidgetMotionPolicy.OverlayVisibility(appearance, systemAnimations, opening)),
            new WidgetMotionPlayback(backdrop, WidgetMotionPolicy.OverlayVisibility(appearance, systemAnimations, opening, backdrop: true)),
        });
    }

    internal void Snap(bool visible)
    {
        motion.Cancel();
        shell.Set(visible ? WidgetMotionPose.Identity : WidgetMotionPose.Identity with { Opacity = 0, Scale = new(.88f, .88f, 1) });
        backdrop.Set(WidgetMotionPose.Identity with { Opacity = visible ? 1 : 0 });
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        motion.Dispose(); shell.Dispose(); backdrop.Dispose();
    }
}
