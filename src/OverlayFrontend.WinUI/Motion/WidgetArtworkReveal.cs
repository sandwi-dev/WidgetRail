using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Hosting;
using WidgetRail.PlatformSettings;

namespace WidgetRail.OverlayFrontend.WinUI.Motion;

/// <summary>Reveals freshly decoded poster pixels on an independent opacity layer.</summary>
internal sealed class WidgetArtworkReveal(FrameworkElement layer) : IDisposable
{
    private WidgetCompositionTarget? target;
    private WidgetCompositionMotion? motion;
    private long epoch;
    internal Task<WidgetMotionOutcome>? Playback { get; private set; }
    internal int Starts { get; private set; }
    internal void Play(AppearanceSettings appearance, bool systemAnimationsEnabled)
    {
        Cancel();
        var duration = WidgetMotionOptions.From(appearance, systemAnimationsEnabled).Duration(220);
        if (appearance.Transparency == TransparencyPreference.Reduced || appearance.Contrast == ContrastPreference.High ||
            appearance.Contrast == ContrastPreference.System && new Windows.UI.ViewManagement.AccessibilitySettings().HighContrast ||
            duration == TimeSpan.Zero || !layer.IsLoaded || layer.ActualWidth <= 0 || layer.ActualHeight <= 0) return;
        layer.Unloaded += Unloaded;
        target = WidgetCompositionTarget.ForClippedDialog(layer, new((float)layer.ActualWidth, (float)layer.ActualHeight));
        motion = new(ElementCompositionPreview.GetElementVisual(layer).Compositor, layer.DispatcherQueue);
        var recipe = new WidgetMotionRecipe(WidgetMotionPose.Identity with { Opacity = 0 }, WidgetMotionPose.Identity, duration);
        ++Starts;
        Playback = motion.PlayAsync((WidgetMotionPlayback[])[new(target, recipe)]);
        _ = FinishAsync(Playback, epoch);
    }
    private async Task FinishAsync(Task<WidgetMotionOutcome> playback, long version)
    {
        await playback;
        if (version == epoch) Cancel();
    }
    private void Unloaded(object sender, RoutedEventArgs args) { if (!layer.IsLoaded) Cancel(); }
    internal void Cancel()
    {
        ++epoch;
        layer.Unloaded -= Unloaded;
        motion?.Dispose(); motion = null;
        target?.Dispose(); target = null;
    }
    public void Dispose() => Cancel();
}
