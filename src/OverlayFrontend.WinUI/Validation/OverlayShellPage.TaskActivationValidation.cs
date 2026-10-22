using WidgetRail.PlatformBroker;
using Microsoft.UI.Xaml;
using System.Text.Json;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetPresentationSession;
using WidgetRail.WindowsWindowActivation;

namespace WidgetRail.OverlayFrontend.WinUI.Shell;

internal sealed partial class OverlayShellPage
{
    internal void EnableTaskActivationValidation(string resultPath, string peerPath,
        Func<Task>? beforeInvoke = null, Func<List<string>, Task>? verifyHandoff = null, Action? cleanup = null, bool expectCancellation = false,
        Func<List<string>, Task>? afterSuccess = null)
    {
        Loaded += async (_, _) =>
        {
            var checks = new List<string>();
            bool issued = false;
            object? observed = null;
            string? error = null;
            var trace = new List<string>();
            var originalDiagnostic = WindowActivationDiagnostic;
            WindowActivationDiagnostic = message => { trace.Add(message); originalDiagnostic?.Invoke(message); };
            try
            {
                using var peer = JsonDocument.Parse(File.ReadAllText(peerPath));
                var target = peer.RootElement.GetProperty("target").Deserialize<TaskWindowTarget>()!;
                var native = new WindowsTaskWindowActivation();
                Check(native.IsCurrent(target) && WindowsTaskWindowActivation.Observe((nint)(nuint)target.Handle).TargetVisible,
                    "Disposable peer is live and visible with exact identity");
                if (startup is not null) await startup;
                SetInteractive(false);
                FocusDirectionalTray();
                var trayDeadline = Environment.TickCount64 + 5000;
                while (FocusedTrayWidget() is null || switching)
                { if (Environment.TickCount64 >= trayDeadline) throw new TimeoutException("Initial tray focus did not settle."); await Task.Delay(20); }
                await RouteButtonAsync(ControllerButton.Y, ControllerEventPhase.Pressed);
                await RunTrayHoldAsync(trayHold.Tick(TrayGestureIdentity, true, Environment.TickCount64 + TrayHoldGesture.HoldMilliseconds));
                await RouteButtonAsync(ControllerButton.Y, ControllerEventPhase.Released);
                Check(shownUtility == ShellUtility.Tasks && UtilityIconFocused, "Hold Y opens Task Switcher from settled tray focus");
                var deadline = Environment.TickCount64 + 15000;
                TaskWindowSummary? window;
                do
                {
                    window = utilityWindows.FirstOrDefault(item => item.PreviewTarget is { } value &&
                        value.ProcessId == target.ProcessId && value.Handle == target.Handle.ToString("X"));
                    if (window is not null) break;
                    await Task.Delay(50);
                } while (Environment.TickCount64 < deadline);
                Check(window is not null, "Host window observer publishes exact disposable target without a widget");
                while (utilityPanel?.IsLoaded != true)
                { if (Environment.TickCount64 >= deadline) throw new TimeoutException("Task list did not finish loading."); await Task.Delay(20); }
                Check(native.IsOverlayForeground && visible, "Frontend owns foreground before direct handoff");
                var taskList = FindControl<Microsoft.UI.Xaml.Controls.ListView>(utilityPanel!, "Overlay.Utility.List")!;
                var targetRow = taskList.Items.Cast<Microsoft.UI.Xaml.Controls.ListViewItem>().Single(item => (string)item.Tag == window!.WindowId);
                NavigateTray(Microsoft.UI.Xaml.Input.FocusNavigationDirection.Up);
                taskList.ScrollIntoView(targetRow);
                await Task.Delay(100);
                Check(targetRow.Focus(FocusState.Keyboard), "Selected target receives native focus before the first A press");
                if (Environment.GetCommandLineArgs().Contains("--task-close-only"))
                {
                    var list = FindControl<Microsoft.UI.Xaml.Controls.ListView>(utilityPanel!, "Overlay.Utility.List")!;
                    var row = list.Items.Cast<Microsoft.UI.Xaml.Controls.ListViewItem>().Single(item => (string)item.Tag == window!.WindowId);
                    list.ScrollIntoView(row);
                    await Task.Delay(100);
                    Check(row.Focus(FocusState.Keyboard), "Disposable target receives focus before close");
                    await RouteButtonAsync(ControllerButton.X, ControllerEventPhase.Pressed);
                    await RouteButtonAsync(ControllerButton.X, ControllerEventPhase.Released);
                    deadline = Environment.TickCount64 + 3000;
                    while (native.IsCurrent(target) && Environment.TickCount64 < deadline) await Task.Delay(20);
                    Check(!native.IsCurrent(target), "X closes only the disposable target through normal WM_CLOSE");
                    await RefreshUtilityTasksAsync(utilityPanel!, lifetime.Token);
                    await Task.Delay(100);
                    Check(visible && native.IsOverlayForeground && (UtilityListFocused || UtilityIconFocused),
                        "Removing the closed target keeps overlay focus and input usable");
                    return;
                }
                if (beforeInvoke is not null) await beforeInvoke();
                var operation = RouteButtonAsync(ControllerButton.A, ControllerEventPhase.Pressed);
                issued = pendingHostTask is not null;
                Check(issued, "Tray, hold Y, Up, then one A press starts the selected window handoff");
                if (verifyHandoff is not null) await verifyHandoff(checks);
                if (expectCancellation)
                {
                    await operation;
                    await Task.Delay(200);
                    Check(issued && visible && native.IsOverlayForeground,
                        "Cancelled handoff preserves the current overlay and foreground");
                    Check(!trace.Any(line => line.Contains("phase=request-activation", StringComparison.Ordinal)),
                        "Cancelled handoff never submits activation to Windows");
                    await VerifyUtilityReentryAsync();
                    Check(utilityPanel is { Busy: false } && utilityCancellation?.IsCancellationRequested == false,
                        "Cancelled handoff leaves utility navigation and refresh usable");
                    return;
                }
                await operation;
                deadline = Environment.TickCount64 + 3000;
                while (Environment.TickCount64 < deadline && (visible || !native.IsTargetForeground(target)))
                    await Task.Delay(20);
                await Task.Delay(200);
                var observation = WindowsTaskWindowActivation.Observe((nint)(nuint)target.Handle);
                observed = observation;
                Check(issued && !visible, "Host handoff hides the overlay before completion");
                Check(native.IsTargetForeground(target), "Exact target or its owned dialog owns foreground after direct activation and settles for 200ms");
                if (afterSuccess is not null) await afterSuccess(checks);
            }
            catch (Exception exception) { error = exception.ToString(); }
            finally
            {
                WindowActivationDiagnostic = originalDiagnostic;
                cleanup?.Invoke();
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(resultPath))!);
                File.WriteAllText(resultPath, JsonSerializer.Serialize(new { checks, observed, trace, error, passed = error is null }));
            }
            void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); checks.Add(message); }
        };
    }
}
