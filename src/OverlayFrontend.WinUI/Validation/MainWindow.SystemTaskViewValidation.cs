using WidgetRail.OverlayFrontend.WinUI.Input;
using WidgetRail.OverlayFrontend.WinUI.Shell;

namespace WidgetRail.OverlayFrontend.WinUI;

public sealed partial class MainWindow
{
    private void EnableSystemTaskViewValidation(OverlayShellPage page, string path)
    {
        var requests = 0;
        systemTaskViewLauncher = () =>
        {
            if (AppWindow.IsVisible || input?.IsActive == true || taskHandoff?.Committed != true)
                throw new InvalidOperationException("System shortcut was invoked before hide and input release.");
            requests++;
            return WindowsTaskViewResult.Requested;
        };
        page.EnableSystemTaskViewValidation(path, () => requests);
    }
}
