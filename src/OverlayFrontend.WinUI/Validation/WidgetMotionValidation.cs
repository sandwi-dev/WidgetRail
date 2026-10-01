using System.Numerics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media;
using WidgetRail.OverlayFrontend.WinUI.Motion;
using WidgetRail.PlatformSettings;

namespace WidgetRail.OverlayFrontend.WinUI.Validation;

/// <summary>Opt-in checks on real XAML layers. No startup hook, package registration or controller owner.</summary>
internal static class WidgetMotionValidation
{
    internal static async Task<IReadOnlyList<string>> RunAsync(Panel host, CancellationToken token = default)
    {
        if (!host.DispatcherQueue.HasThreadAccess || host.XamlRoot is null) throw new InvalidOperationException("Motion checks require a loaded UI host.");
        var results = new List<string>();
        var size = new Vector2(320, 160);
        var stage = new Grid { Width = size.X, Height = size.Y };
        var outgoingViewport = new Grid();
        var incomingViewport = new Grid();
        var outgoing = new Border { Background = (Brush)Application.Current.Resources["CardBackgroundFillColorDefaultBrush"],
            Child = new Button { Content = "Outgoing page", IsEnabled = false } };
        var incoming = new Border { Background = (Brush)Application.Current.Resources["CardBackgroundFillColorDefaultBrush"],
            Child = new Button { Content = "Incoming page" } };
        outgoingViewport.Children.Add(outgoing); incomingViewport.Children.Add(incoming);
        stage.Children.Add(outgoingViewport); stage.Children.Add(incomingViewport); host.Children.Add(stage);
        stage.UpdateLayout();
        var outgoingVisual = ElementCompositionPreview.GetElementVisual(outgoing);
        var incomingVisual = ElementCompositionPreview.GetElementVisual(incoming);
        using var incomingTarget = WidgetCompositionTarget.ForElement(incoming, incomingViewport, size);
        using var outgoingTarget = WidgetCompositionTarget.ForElement(outgoing, outgoingViewport, size);
        using var motion = new WidgetCompositionMotion(incomingVisual.Compositor, host.DispatcherQueue);
        var options = WidgetMotionOptions.From(AppearanceSettings.Default with { WidgetAnimationSpeed = 2 }, true);
        try
        {
            foreach (var section in Enum.GetValues<WidgetSectionAnimation>())
            {
                var plan = WidgetMotionPolicy.Section(options with { Section = section }, size, 1);
                var result = await motion.PlayAsync((WidgetMotionPlayback[])[new(incomingTarget, plan.Incoming), new(outgoingTarget, plan.Outgoing)], token)
                    .WaitAsync(TimeSpan.FromSeconds(5), token);
                Check(result == WidgetMotionOutcome.Completed && incomingVisual.Opacity == 1 && incomingVisual.Scale == Vector3.One,
                    "section " + section + " completes at its native target");
            }
            var slow = WidgetMotionPolicy.Dialog(options, true) with { Duration = TimeSpan.FromSeconds(2) };
            var first = motion.PlayAsync((WidgetMotionPlayback[])[new(incomingTarget, slow)], token);
            await Task.Delay(30, token);
            var replacement = motion.PlayAsync((WidgetMotionPlayback[])[new(incomingTarget, WidgetMotionPolicy.Dialog(options, false))], token);
            Check(await first == WidgetMotionOutcome.Superseded, "interruption retires prior completion");
            Check(await replacement.WaitAsync(TimeSpan.FromSeconds(5), token) == WidgetMotionOutcome.Completed && incomingVisual.Opacity == 0,
                "replacement owns its final state");
            using (var cancel = CancellationTokenSource.CreateLinkedTokenSource(token))
            {
                var pending = motion.PlayAsync((WidgetMotionPlayback[])[new(incomingTarget, slow)], cancel.Token);
                await Task.Run(cancel.Cancel, token);
                Check(await pending.WaitAsync(TimeSpan.FromSeconds(5), token) == WidgetMotionOutcome.Canceled && incomingVisual.Opacity == 1,
                    "cross-thread cancellation settles on the UI owner");
            }
            var reduced = WidgetMotionPolicy.Dialog(options with { Reduced = true }, true);
            var instant = motion.PlayAsync((WidgetMotionPlayback[])[new(incomingTarget, reduced)], token);
            Check(instant.IsCompletedSuccessfully && await instant == WidgetMotionOutcome.Completed,
                "reduced motion settles synchronously");
            var disposing = motion.PlayAsync((WidgetMotionPlayback[])[new(incomingTarget, slow)], token);
            motion.Dispose();
            Check(await disposing == WidgetMotionOutcome.Disposed, "owner disposal retires active completion");
            incomingTarget.Dispose(); outgoingTarget.Dispose();
            Check(incomingVisual.Scale == Vector3.One && incomingVisual.Opacity == 1 &&
                ElementCompositionPreview.GetElementVisual(incomingViewport).Clip is null,
                "layer retirement restores original transform opacity and clipping");
            Check(incoming.ActualWidth == size.X && incoming.ActualHeight == size.Y, "motion never changes XAML layout size");
            var focusCell = new Grid { Width = 200, Height = 60 };
            var focusButton = new Button { Content = "Focus target", HorizontalAlignment = HorizontalAlignment.Stretch };
            var otherButton = new Button { Content = "Move focus" };
            var decoration = new Border { BorderThickness = new Thickness(2),
                BorderBrush = (Brush)Application.Current.Resources["AccentFillColorDefaultBrush"] };
            focusCell.Children.Add(focusButton);
            using var focusMotion = new WidgetFocusMotion(focusButton, decoration,
                AppearanceSettings.Default with { FocusAnimation = WidgetFocusAnimation.None }, true);
            focusCell.Children.Add(focusMotion.Adornment);
            host.Children.Add(focusCell); host.Children.Add(otherButton);
            try
            {
                host.UpdateLayout();
                await Task.Delay(20, token);
                Check(focusButton.Focus(Microsoft.UI.Xaml.FocusState.Programmatic), "native control accepts focus without pointer activation");
                var focusVisual = ElementCompositionPreview.GetElementVisual(focusMotion.Adornment.Children[0]);
                await UntilAsync(() => focusVisual.Opacity == 1, "focus decoration entering");
                Check(focusVisual.Opacity == 1 && !focusMotion.Adornment.IsHitTestVisible,
                    "native focus event shows only the noninteractive decoration");
                focusMotion.ApplyAppearance(AppearanceSettings.Default with { Motion = MotionPreference.Reduced }, true);
                Check(ElementCompositionPreview.GetElementVisual(focusButton).Scale == Vector3.One &&
                    ElementCompositionPreview.GetElementVisual(focusButton).Opacity == 1,
                    "focus policy never changes control text artwork or base opacity");
                Check(otherButton.Focus(Microsoft.UI.Xaml.FocusState.Programmatic), "native control accepts focus transfer");
                await UntilAsync(() => focusVisual.Opacity == 0, "focus decoration leaving");
                Check(focusVisual.Opacity == 0,
                    "native focus loss clears the decoration under reduced motion");
            }
            finally { focusMotion.Dispose(); host.Children.Remove(otherButton); host.Children.Remove(focusCell); }
            var readyContent = new Border { Width = 240, Height = 80,
                Child = new TextBlock { Text = "Ready widget content" } };
            host.Children.Add(readyContent);
            var fill = new Border { Width = 240, Height = 80 };
            host.Children.Add(fill);
            using var reveal = new WidgetSurfaceResizeMotion(readyContent);
            try
            {
                await UntilAsync(() => readyContent.IsLoaded && readyContent.ActualWidth > 0, "ready widget resize layout");
                var appearance = AppearanceSettings.Default with { Motion = MotionPreference.Full, Contrast = ContrastPreference.Standard,
                    AnimateWidgetSwitching = true, WidgetAnimationSpeed = .5 };
                reveal.Play(appearance, true, new(480, 160), fill);
                Check(await reveal.Playback!.WaitAsync(TimeSpan.FromSeconds(5), token) == WidgetMotionOutcome.Completed && reveal.Starts == 1,
                    "global widget-switch preference starts and completes a native surface resize");
                var revealVisual = ElementCompositionPreview.GetElementVisual(readyContent);
                Check(revealVisual.Opacity == 1 && readyContent.ActualWidth == 240,
                    "widget resize finishes opaque without changing native layout");
                reveal.Play(appearance with { AnimateWidgetSwitching = false }, true, new(480, 160), fill);
                reveal.Play(appearance with { Motion = MotionPreference.Reduced }, true, new(480, 160), fill);
                Check(reveal.Starts == 1, "disabled widget switching and reduced motion create no compositor playback");
                reveal.Play(appearance, true, new(480, 160), fill);
                var interrupted = reveal.Playback!;
                reveal.Play(appearance, true, new(480, 160), fill);
                var latest = reveal.Playback!;
                Check(await interrupted == WidgetMotionOutcome.Canceled &&
                    await latest.WaitAsync(TimeSpan.FromSeconds(5), token) == WidgetMotionOutcome.Completed && revealVisual.Opacity == 1,
                    "rapid widget resize replacement cannot let old completion reset the new owner");
                reveal.Play(appearance, true, new(480, 160), fill);
                var unloaded = reveal.Playback!;
                host.Children.Remove(readyContent);
                await UntilAsync(() => !readyContent.IsLoaded, "widget resize unload");
                Check(await unloaded == WidgetMotionOutcome.Canceled && revealVisual.Opacity == 1,
                    "unloading settles widget resize and releases its compositor channels");
            }
            finally { reveal.Dispose(); host.Children.Remove(readyContent); host.Children.Remove(fill); }
            return results.AsReadOnly();
        }
        finally { motion.Dispose(); incomingTarget.Dispose(); outgoingTarget.Dispose(); host.Children.Remove(stage); }

        void Check(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException("Native motion check failed: " + message);
            results.Add(message);
        }
        async Task UntilAsync(Func<bool> condition, string name)
        {
            var deadline = Environment.TickCount64 + 2000;
            while (!condition())
            {
                if (Environment.TickCount64 >= deadline) throw new TimeoutException(name);
                await Task.Delay(10, token);
            }
        }
    }
}
