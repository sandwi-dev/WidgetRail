using System.Numerics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Media;
using WidgetRail.OverlayFrontend.WinUI.Motion;
using WidgetRail.PlatformSettings;

namespace WidgetRail.OverlayFrontend.WinUI.Shell;

internal sealed partial class ShellControllerGuide
{
    private AppearanceSettings motionAppearance = AppearanceSettings.Default;
    private bool systemAnimations = true;
    private WidgetCompositionMotion? fade;
    private readonly Dictionary<GuideLayer, WidgetCompositionTarget> fadeTargets = [];
    private long fadeVersion;
    internal long FadeCount { get; private set; }
    internal bool IsFading => fade is not null;
    internal TimeSpan LastFadeDuration { get; private set; }
    internal Task<WidgetMotionOutcome>? FadePlayback { get; private set; }
    internal Exception? FadeFailure { get; private set; }
    internal bool OutgoingIsPassive => !back.Panel.Interactive && back.Cells.All(cell =>
        !cell.Button.IsHitTestVisible && AutomationProperties.GetAccessibilityView(cell.Button) == AccessibilityView.Raw);
    private WidgetSectionMotion FadeRecipe => WidgetMotionPolicy.GuideCrossfade(motionAppearance, systemAnimations, palette?.HighContrast == true);

    private void PrepareFade()
    {
        var size = new Vector2((float)stage.ActualWidth, (float)stage.ActualHeight);
        foreach (var layer in new[] { front, back })
            if (!fadeTargets.ContainsKey(layer))
                fadeTargets.Add(layer, WidgetCompositionTarget.ForClippedDialog(layer.Panel, size));

        // Only the incoming guide participates in layout/input/accessibility.
        // The outgoing guide keeps its arranged extent in a non-measuring Canvas.
        // Two reusable banks bound retention, including interrupted transitions.
        outgoing.Children.Clear();
        stage.Width = stage.ActualWidth; stage.Height = stage.ActualHeight;
        stage.Interactive = false;
        foreach (var cell in cells)
        {
            cell.Button.IsHitTestVisible = false;
            AutomationProperties.SetAccessibilityView(cell.Button, AccessibilityView.Raw);
        }
        layers.Children.Remove(stage);
        outgoing.Children.Add(stage);
        (front, back) = (back, front);
        stage.Width = stage.Height = double.NaN;
        stage.Interactive = true;
        layers.Children.Add(stage);
        // Recycle the obsolete outgoing bank for the latest destination. The
        // previous incoming bank continues fading from its compositor value.
        fadeTargets[front].Set(WidgetMotionPose.Identity with { Opacity = 0 });
    }

    private void StartFade()
    {
        var recipe = FadeRecipe;
        LastFadeDuration = recipe.Incoming.Duration;
        var version = ++fadeVersion;
        fade ??= new(CompositionTarget.GetCompositorForCurrentThread(), DispatcherQueue);
        ++FadeCount;
        FadePlayback = fade.PlayAsync((WidgetMotionPlayback[])[new(fadeTargets[front], recipe.Incoming), new(fadeTargets[back], recipe.Outgoing)]);
        _ = CompleteFadeAsync(FadePlayback, version);
    }

    private async Task CompleteFadeAsync(Task<WidgetMotionOutcome> playback, long version)
    {
        try
        {
            await playback;
            if (!disposed && version == fadeVersion) StopFade();
        }
        catch (Exception error)
        {
            FadeFailure = error;
            if (!disposed && version == fadeVersion) StopFade();
        }
    }

    private void StopFade()
    {
        ++fadeVersion;
        fade?.Dispose(); fade = null;
        foreach (var target in fadeTargets.Values) target.Dispose();
        fadeTargets.Clear();
        outgoing.Children.Clear();
        back.Panel.Width = back.Panel.Height = double.NaN;
    }
}
