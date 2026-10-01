using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace WidgetRail.OverlayFrontend.WinUI.Validation;

internal sealed partial class WidgetMotionValidationPage : Page, IAsyncDisposable
{
    private readonly StackPanel stage = new() { Spacing = 8 };
    private readonly TextBlock status = new() { Text = "Motion checks pending" };
    private readonly CancellationTokenSource lifetime = new();
    private Task? running;

    internal WidgetMotionValidationPage()
    {
        AutomationProperties.SetAutomationId(status, "Motion.Result");
        stage.Children.Add(status); Content = stage;
        Loaded += (_, _) => running ??= RunAsync();
    }

    private async Task RunAsync()
    {
        try
        {
            var checks = await WidgetMotionValidation.RunAsync(stage, lifetime.Token);
            status.Text = $"Passed {checks.Count} native motion checks";
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        catch (Exception error) { status.Text = "Failed: " + error; }
    }

    public async ValueTask DisposeAsync()
    {
        lifetime.Cancel();
        if (running is not null) await running;
        lifetime.Dispose();
    }
}
