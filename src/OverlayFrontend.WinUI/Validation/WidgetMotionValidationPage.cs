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

    private readonly string? resultPath;
    internal WidgetMotionValidationPage(string? resultPath = null)
    {
        this.resultPath = resultPath;
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
            if(resultPath is not null)await File.WriteAllTextAsync(resultPath,System.Text.Json.JsonSerializer.Serialize(new{passed=true,checks}));
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        catch (Exception error)
        {
            status.Text = "Failed: " + error;
            if(resultPath is not null)await File.WriteAllTextAsync(resultPath,System.Text.Json.JsonSerializer.Serialize(new{passed=false,error=error.ToString()}));
        }
    }

    public async ValueTask DisposeAsync()
    {
        lifetime.Cancel();
        if (running is not null) await running;
        lifetime.Dispose();
    }
}
