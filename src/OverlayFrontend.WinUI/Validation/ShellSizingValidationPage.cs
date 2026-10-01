using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using WidgetRail.OverlayFrontend.WinUI.Shell;
using Windows.Foundation;

namespace WidgetRail.OverlayFrontend.WinUI.Validation;

internal sealed partial class ShellSizingValidationPage : Page
{
    private readonly TextBlock status = new() { Text = "running" };
    internal ShellSizingValidationPage()
    {
        AutomationProperties.SetAutomationId(status, "Sizing.Result");
        var button = new Button { Content = "Scaled focus", Width = 120, Height = 40, HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top };
        AutomationProperties.SetAutomationId(button, "Sizing.Button");
        var body = new Grid();
        body.Children.Add(button);
        var scale = new OverlayScaleRoot { Width = 600, Height = 300 };
        scale.Children.Add(body);
        Content = new StackPanel { Spacing = 12, Children = { status, scale } };
        Loaded += async (_, _) =>
        {
            var checks = 0;
            try
            {
                foreach (var factor in new[] { 1d, 1.25, .5, 1d })
                {
                    scale.InterfaceScale = factor;
                    await Task.Delay(100);
                    Check(Math.Abs(body.ActualWidth - 600 / factor) < .1 && Math.Abs(body.ActualHeight - 300 / factor) < .1,
                        "zoom must measure and arrange the real design viewport");
                    var bounds = button.TransformToVisual(scale).TransformBounds(new Rect(0, 0, button.ActualWidth, button.ActualHeight));
                    Check(Math.Abs(bounds.Width - 120 * factor) < .1 && Math.Abs(bounds.Height - 40 * factor) < .1,
                        "paint and hit-test transform must match zoom");
                    Check(button.Focus(FocusState.Keyboard), "native focus remains on the same control after zoom");
                }
                scale.Width = 300;
                scale.InterfaceScale = 1.25;
                await Task.Delay(100);
                Check(Math.Abs(body.ActualWidth - 240) < .1, "constrained window reflows rather than fitting fixed canvas");
                status.Text = $"passed:{checks}";
            }
            catch (Exception error) { status.Text = $"failed:{error.Message}"; }
            void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); ++checks; }
        };
    }
}
