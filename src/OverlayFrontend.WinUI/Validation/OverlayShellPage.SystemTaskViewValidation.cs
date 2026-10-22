using System.Text.Json;
using Microsoft.UI.Xaml.Input;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.OverlayFrontend.WinUI.Shell;

internal sealed partial class OverlayShellPage
{
    internal void EnableSystemTaskViewValidation(string path, Func<int> requests)
    {
        Loaded += async (_, _) =>
        {
            var checks = new List<string>(); string? error = null;
            try
            {
                if (startup is not null) await startup;
                await Until(() => visible && foreground && !switching);
                FocusUtility(ShellUtility.Tasks);
                await Until(() => utilityPanel is { IsLoaded: true });
                NavigateTray(FocusNavigationDirection.Up);
                await Until(() => utilityPanel!.ContainsFocus && SystemTaskViewFocused);
                checks.Add("Up focuses Windows Task View as the first row");
                await RouteButtonAsync(ControllerButton.A, ControllerEventPhase.Pressed);
                await RouteButtonAsync(ControllerButton.A, ControllerEventPhase.Released);
                await Until(() => !visible && requests() == 1);
                checks.Add("One A selection invokes the system shortcut only after overlay hide and controller release");
                await Task.Delay(150);
                if (requests() != 1) throw new InvalidOperationException("Release submitted a second shortcut.");
                checks.Add("Release does not invoke the system shortcut twice");
            }
            catch (Exception failure) { error = failure.ToString(); }
            File.WriteAllText(path, JsonSerializer.Serialize(new { passed = error is null, checks, error,
                scope = "Native UI and handoff; OS shortcut submission replaced by an observer to avoid opening system UI during regression tests." }));
            static async Task Until(Func<bool> ready)
            {
                var until = Environment.TickCount64 + 10000;
                while (!ready()) { if (Environment.TickCount64 >= until) throw new TimeoutException("System Task View handoff did not settle."); await Task.Delay(20); }
            }
        };
    }
}
