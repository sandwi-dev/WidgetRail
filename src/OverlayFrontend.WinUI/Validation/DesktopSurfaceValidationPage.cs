using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace WidgetRail.OverlayFrontend.WinUI.Validation;

// Deliberately separate from the existing WebView2 ExternalSurfacePage fixture.
// This page supplies only a placement region; it never claims to embed an HWND.
internal sealed partial class DesktopSurfaceValidationPage : Grid
{
    private readonly TextBlock status = new() { Text = "Ready. Run borrows and restores the generated Electron window.", TextWrapping = TextWrapping.Wrap };
    private readonly Button run = new() { Content = "Run generated-window check" };
    internal Border SurfaceSlot { get; } = new()
    {
        BorderThickness = new Thickness(1), Padding = new Thickness(12),
        BorderBrush = (Brush)Application.Current.Resources["CardStrokeColorDefaultBrush"],
        Child = new TextBlock { Text = "Generated desktop window placement", TextWrapping = TextWrapping.Wrap }
    };
    internal event Func<Task>? RunRequested;

    internal DesktopSurfaceValidationPage()
    {
        RowSpacing = 12;
        RowDefinitions.Add(new() { Height = GridLength.Auto });
        RowDefinitions.Add(new() { Height = GridLength.Auto });
        RowDefinitions.Add(new() { Height = new GridLength(1, GridUnitType.Star) });
        AutomationProperties.SetAutomationId(status, "DesktopSurface.Status");
        AutomationProperties.SetAutomationId(run, "DesktopSurface.Run");
        AutomationProperties.SetAutomationId(SurfaceSlot, "DesktopSurface.Slot");
        Grid.SetRow(run, 1); Grid.SetRow(SurfaceSlot, 2);
        Children.Add(status); Children.Add(run); Children.Add(SurfaceSlot);
        run.Click += async (_, _) =>
        {
            run.IsEnabled = false;
            try { if (RunRequested is { } action) await action(); }
            catch (Exception) { status.Text = "Fixture failed before completion. Inspect the isolated result file."; }
        };
    }

    internal void Report(string value) => status.Text = value;
}
