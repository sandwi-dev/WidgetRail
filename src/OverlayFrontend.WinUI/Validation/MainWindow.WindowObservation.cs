namespace WidgetRail.OverlayFrontend.WinUI;

public sealed partial class MainWindow
{
    private Validation.ValidationWindowObserver? validationWindowObserver;
    private void ConfigureValidationWindowObservation(IReadOnlyList<string> arguments)
    {
        if (Shell.FrontendArguments.Value(arguments, "--validation-window-observation") is not { } path) return;
        var target = Shell.FrontendArguments.Value(arguments, "--validation-window-target") ?? "DesktopSurface.Run";
        validationWindowObserver = new(this, ShellRoot, target, path);
    }
}
