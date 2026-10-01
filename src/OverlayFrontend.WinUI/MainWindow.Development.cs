using Microsoft.UI.Xaml.Input;
using WidgetRail.OverlayFrontend.WinUI.Development;
using WidgetRail.OverlayFrontend.WinUI.Shell;

namespace WidgetRail.OverlayFrontend.WinUI;

public sealed partial class MainWindow
{
    private DeveloperInspectorWindow? developerInspector;
    private KeyboardAccelerator? inspectorShortcut;

    private void ConfigureDevelopment(OverlayShellPage page, OverlayShellOptions options)
    {
        if (options.Development?.Inspector != true) return;
        page.PrepareDeveloperInspectorAsync = async token =>
        {
            developerInspector ??= new(page.CaptureDeveloperInspection, () => page.LastDeveloperNavigation);
            await developerInspector.OpenAsync(token);
        };
        inspectorShortcut = new() { Key = Windows.System.VirtualKey.F12 };
        inspectorShortcut.Invoked += async (_, args) =>
        {
            args.Handled = true;
            if (cleanupStarted || developerInspector is null) return;
            try { await developerInspector.OpenAsync(CancellationToken.None); }
            catch (Exception error) { Diagnostics.FrontendFailureLog.Current.Write("development-inspector-open", error); }
        };
        ShellRoot.KeyboardAccelerators.Add(inspectorShortcut);
    }

    private void RetireDevelopment()
    {
        if (inspectorShortcut is not null) ShellRoot.KeyboardAccelerators.Remove(inspectorShortcut);
        inspectorShortcut = null;
        developerInspector?.Retire(); developerInspector = null;
    }
}
