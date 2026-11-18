using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media;

namespace WidgetRail.OverlayFrontend.WinUI.Shell;

internal sealed partial class OverlayShellPage
{
    // Opt-in, bounded read-only diagnostics for the currently realized capture UI.
    internal void EnableCaptureInteractionDiagnostics(string output)
    {
        output = Path.GetFullPath(output); Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        var timer = DispatcherQueue.CreateTimer(); timer.Interval = TimeSpan.FromMilliseconds(250);
        var samples = 0;
        var replaying = false;
        timer.Tick += (_, _) =>
        {
            if (++samples > 1200 || retired) { timer.Stop(); return; }
            if (surface is not { IsLoaded: true } current) return;
            if (!replaying && File.Exists(output + ".request"))
            {
                var command = File.ReadAllText(output + ".request").Trim();
                File.Delete(output + ".request");
                if (command == "early-next") _ = ReplayAsync(WidgetRail.WidgetProtocol.ControllerButton.RightBumper, 8);
                else if (command == "next") _ = ReplayAsync(WidgetRail.WidgetProtocol.ControllerButton.RightBumper, 0);
                else if (command == "previous") _ = ReplayAsync(WidgetRail.WidgetProtocol.ControllerButton.LeftBumper, 0);
            }
            try
            {
                var fonts = Descendants(current).OfType<TextBlock>().Where(text =>
                    AutomationProperties.GetAutomationId(text).Length > 0).Take(samples == 8 ? 80 : 0).Select(text =>
                {
                    var parents = new List<object>();
                    for (DependencyObject? node = text; node is FrameworkElement element; node = VisualTreeHelper.GetParent(node))
                    {
                        var at = element.TransformToVisual(null).TransformPoint(new(0, 0));
                        var unit = element.TransformToVisual(null).TransformPoint(new(1, 1));
                        parents.Add(new { type = element.GetType().Name, x = at.X, y = at.Y, sx = unit.X - at.X, sy = unit.Y - at.Y,
                            scaleX = element.Scale.X, scaleY = element.Scale.Y, opacity = element.Opacity,
                            font = element is Control control ? control.FontFamily.Source : null });
                    }
                    return new { id = AutomationProperties.GetAutomationId(text), text.FontSize, font = text.FontFamily.Source,
                        weight = text.FontWeight.Weight, text.IsTextScaleFactorEnabled, text.UseLayoutRounding, parents };
                }).ToArray();
                File.AppendAllText(output, JsonSerializer.Serialize(new { sample = samples, time = Environment.TickCount64,
                    activeWidget, preview = current.FocusPreviewDiagnostics(), current.PageCacheHits, current.CachedPageCount, current.PageCacheStatus, scale = XamlRoot.RasterizationScale, fonts,
                    navigation = current.NavigationReviewForValidation(),
                    inspection = samples == 8 ? current.CaptureDeveloperInspection() : null }) + Environment.NewLine);
            }
            catch (Exception error) { File.AppendAllText(output, JsonSerializer.Serialize(new { error = error.Message }) + Environment.NewLine); timer.Stop(); }
        };
        Loaded += (_, _) => timer.Start(); Unloaded += (_, _) => timer.Stop();
        if (IsLoaded) timer.Start();
        async Task ReplayAsync(WidgetRail.WidgetProtocol.ControllerButton direction, int movements)
        {
            replaying = true;
            try
            {
                if (surface is not { } owner) return;
                await owner.HandleControllerButtonAsync(direction);
                for (var index = 0; index < movements; ++index)
                {
                    await Task.Delay(40);
                    owner.MoveFocus(Microsoft.UI.Xaml.Input.FocusNavigationDirection.Down, index != 0);
                }
            }
            finally { replaying = false; }
        }
        static IEnumerable<DependencyObject> Descendants(DependencyObject root)
        {
            for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); ++i)
            {
                var child = VisualTreeHelper.GetChild(root, i); yield return child;
                foreach (var nested in Descendants(child)) yield return nested;
            }
        }
    }
}
