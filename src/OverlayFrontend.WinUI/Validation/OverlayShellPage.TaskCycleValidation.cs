using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using WidgetRail.WidgetProtocol;
using WidgetRail.WindowsWindowActivation;

namespace WidgetRail.OverlayFrontend.WinUI.Shell;

internal sealed partial class OverlayShellPage
{
    internal void EnableTaskCycleValidation(string path, string targetsPath, Func<Task> reopen, Func<Task> externalInput)
    {
        Loaded += async (_, _) =>
        {
            var checks = new List<string>(); var trace = new List<string>(); string? error = null;
            var diagnostic = WindowActivationDiagnostic;
            WindowActivationDiagnostic = value => { trace.Add(value); diagnostic?.Invoke(value); };
            try
            {
                var targets = JsonSerializer.Deserialize<TaskWindowTarget[]>(File.ReadAllText(targetsPath))!;
                if (targets.Length != 2) throw new InvalidDataException("Two exact live targets are required.");
                if (startup is not null) await startup;
                var native = new WindowsTaskWindowActivation();
                for (var cycle = 0; cycle < 6; cycle++)
                {
                    if (cycle > 0) await reopen();
                    await Until(() => visible && foreground && !switching);
                    FocusUtility(ShellUtility.Tasks);
                    var target = targets[cycle % targets.Length];
                    await Until(() => utilityPanel is { IsLoaded: true } && utilityWindows.Any(item => item.PreviewTarget?.Handle == target.Handle.ToString("X")));
                    var window = utilityWindows.Single(item => item.PreviewTarget?.Handle == target.Handle.ToString("X"));
                    var list = FindControl<ListView>(utilityPanel!, "Overlay.Utility.List")!;
                    var row = list.Items.Cast<ListViewItem>().Single(item => (string)item.Tag == window.WindowId);
                    NavigateTray(FocusNavigationDirection.Up); list.ScrollIntoView(row);
                    await Until(() => row.IsLoaded);
                    if (!row.Focus(FocusState.Keyboard)) throw new InvalidOperationException("Target could not receive focus.");
                    await RouteButtonAsync(ControllerButton.A, ControllerEventPhase.Pressed);
                    await RouteButtonAsync(ControllerButton.A, ControllerEventPhase.Released);
                    await Until(() => !visible && native.IsTargetForeground(target));
                    checks.Add($"Switch {cycle + 1} activated HWND {target.Handle} in the same host process");
                    await externalInput();
                    checks.Add($"Switch {cycle + 1} received pointer input outside the hidden host without clicking or moving the pointer");
                }
                await reopen();
            }
            catch (Exception failure) { error = failure.ToString(); }
            finally
            {
                WindowActivationDiagnostic = diagnostic;
                File.WriteAllText(path, JsonSerializer.Serialize(new { passed = error is null, checks, trace, error }));
            }
            static async Task Until(Func<bool> ready)
            {
                var until = Environment.TickCount64 + 8000;
                while (!ready()) { if (Environment.TickCount64 >= until) throw new TimeoutException("Persistent host handoff did not settle."); await Task.Delay(20); }
            }
        };
    }
}
