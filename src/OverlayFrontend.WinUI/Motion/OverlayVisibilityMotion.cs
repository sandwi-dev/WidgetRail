using Microsoft.UI.Xaml;
using WidgetRail.PlatformSettings;

namespace WidgetRail.OverlayFrontend.WinUI.Motion;

/// <summary>Fades the shell and scrim together, owning only their opacity.</summary>
internal sealed class OverlayVisibilityMotion : IDisposable
{
    private readonly WidgetCompositionTarget shell;
    private readonly WidgetCompositionTarget backdrop;
    private readonly WidgetCompositionMotion motion;
    private bool disposed;
    internal Task<WidgetMotionOutcome>? Playback { get; private set; }

    internal OverlayVisibilityMotion(UIElement shellLayer, UIElement backdropLayer)
    {
        shell = WidgetCompositionTarget.ForOpacity(shellLayer);
        backdrop = WidgetCompositionTarget.ForOpacity(backdropLayer);
        motion = new(shell.Compositor, shellLayer.DispatcherQueue);
        Snap(false);
    }

    internal Task<WidgetMotionOutcome> Play(bool opening, AppearanceSettings appearance, bool systemAnimations)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        var fade = WidgetMotionPolicy.OverlayVisibility(appearance, systemAnimations, opening);
        return Playback = motion.PlayAsync(new[]
        {
            new WidgetMotionPlayback(shell, fade),
            new WidgetMotionPlayback(backdrop, fade),
        });
    }

    internal void Snap(bool visible)
    {
        motion.Cancel();
        var pose = WidgetMotionPose.Identity with { Opacity = visible ? 1 : 0 };
        shell.Set(pose);
        backdrop.Set(pose);
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        motion.Dispose(); shell.Dispose(); backdrop.Dispose();
    }
}
