using System.Numerics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using WidgetRail.OverlayFrontend.WinUI.Motion;
using WidgetRail.PlatformSettings;

namespace WidgetRail.OverlayFrontend.WinUI.Validation;

internal static class WidgetPreviewMotionValidation
{
    internal static async Task RunAsync(Panel host, List<string> checks)
    {
        var appearance = AppearanceSettings.Default with { Motion = MotionPreference.Full, WidgetAnimationSpeed = 1 };
        var target = new Border { Width = 320, Height = 160, Child = new TextBlock { Text = "Preview" } };
        var scrim = new Grid { Width = 360, Height = 200 };
        var visual = ElementCompositionPreview.GetElementVisual(target);
        WidgetDialogMotion? owner = null;
        try
        {
            owner = new(target, null, appearance, true, WidgetDialogMotionRole.Preview);
            Check(!owner.Ready.IsCompleted && owner.Opening is null, "Preview entry waits for real layout without a scrim");
            host.Children.Add(target); host.UpdateLayout();
            await Until(() => owner.Opening is not null);
            var opening = owner.Opening!;
            Check(await owner.Ready.WaitAsync(TimeSpan.FromSeconds(5)) == WidgetMotionOutcome.Completed &&
                visual.Scale == Vector3.One && visual.Opacity == 1,
                "Preview entry completes once at full opacity and scale");
            ((TextBlock)target.Child).Text = "Updated preview"; target.UpdateLayout(); owner.ApplyAppearance(appearance, true);
            Check(ReferenceEquals(opening, owner.Opening), "Content and unchanged appearance updates do not replay entry");
            owner.Dispose(); host.Children.Remove(target);
            Check(visual.Scale == Vector3.One && visual.Opacity == 1, "Preview disposal restores compositor ownership immediately");

            owner = new(target, null, appearance, true, WidgetDialogMotionRole.Preview);
            host.Children.Add(target); host.UpdateLayout(); await Until(() => owner.Opening is not null);
            Check(!ReferenceEquals(opening, owner.Opening), "A reopened preview owns a fresh entrance lifetime");
            owner.Dispose();
            Check(owner.Ready.IsCompleted && visual.Scale == Vector3.One && visual.Opacity == 1,
                "Closing during entry settles without delaying preview teardown");
            host.Children.Remove(target);

            owner = new(target, null, appearance with { Motion = MotionPreference.Reduced }, true, WidgetDialogMotionRole.Preview);
            host.Children.Add(target); host.UpdateLayout(); await Until(() => owner.Opening is not null);
            Check(owner.Ready.IsCompletedSuccessfully && owner.Opening!.IsCompletedSuccessfully && visual.Scale == Vector3.One && visual.Opacity == 1,
                "Reduced motion presents preview immediately at identity");
            owner.Dispose(); host.Children.Remove(target);

            owner = new(target, null, appearance, true, WidgetDialogMotionRole.MediaPreview);
            host.Children.Add(target); host.UpdateLayout(); await Until(() => owner.Opening is not null);
            Check(visual.Scale == Vector3.One, "Media preview entrance never scales its separate native plane");
            owner.ApplyAppearance(appearance with { Motion = MotionPreference.Reduced }, true);
            await owner.Ready.WaitAsync(TimeSpan.FromSeconds(5));
            Check(visual.Scale == Vector3.One && visual.Opacity == 1, "Changing to reduced motion settles the current entry without replay");
            owner.Dispose(); host.Children.Remove(target);

            owner = new(target, null, appearance, true, WidgetDialogMotionRole.Preview);
            owner.Dispose();
            Check(owner.Opening is null && await owner.Ready == WidgetMotionOutcome.Disposed,
                "Retiring before layout releases pending media readiness");

            owner = new(target, scrim, appearance, true);
            host.Children.Add(scrim); host.Children.Add(target); host.UpdateLayout();
            await Until(() => owner.Opening is not null);
            Check(await owner.Ready.WaitAsync(TimeSpan.FromSeconds(5)) == WidgetMotionOutcome.Completed &&
                visual.Opacity == 1 && ElementCompositionPreview.GetElementVisual(scrim).Opacity == 1,
                "Ordinary modal retains dialog and scrim entrance");
            Check(await owner.CloseAsync().WaitAsync(TimeSpan.FromSeconds(5)) == WidgetMotionOutcome.Completed &&
                visual.Opacity == 0 && ElementCompositionPreview.GetElementVisual(scrim).Opacity == 0,
                "Ordinary modal retains its existing animated close");
        }
        finally { owner?.Dispose(); host.Children.Remove(target); host.Children.Remove(scrim); }
        void Check(bool condition, string label) { if (!condition) throw new InvalidOperationException(label); checks.Add(label); }
        static async Task Until(Func<bool> condition)
        {
            var deadline = Environment.TickCount64 + 5000;
            while (!condition()) { if (Environment.TickCount64 > deadline) throw new TimeoutException("Preview motion layout did not settle."); await Task.Delay(10); }
        }
    }
}
