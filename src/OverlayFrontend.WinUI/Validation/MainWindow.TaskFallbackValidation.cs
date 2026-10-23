using System.Runtime.InteropServices;
using System.Text.Json;
using WidgetRail.OverlayFrontend.WinUI.Input;
using WidgetRail.OverlayFrontend.WinUI.Shell;
using WidgetRail.WidgetPresentationSession;
using WidgetRail.WindowsWindowActivation;

namespace WidgetRail.OverlayFrontend.WinUI;

public sealed partial class MainWindow
{
    private void EnableTaskFallbackValidation(OverlayShellPage page, string path, string targetPath, string otherPath, string? onlyCase)
    {
        var started = false;
        page.Loaded += async (_, _) =>
        {
            if (started) return;
            started = true;
            var checks = new List<string>();
            var outcomes = new List<object>();
            var trace = new List<string>();
            var originalDiagnostic = page.WindowActivationDiagnostic;
            page.WindowActivationDiagnostic = message => { trace.Add(message); originalDiagnostic?.Invoke(message); };
            var originalActivation = hostTaskActivator;
            var originalTaskView = systemTaskViewLauncher;
            try
            {
                using var targetJson = JsonDocument.Parse(File.ReadAllText(targetPath));
                using var otherJson = JsonDocument.Parse(File.ReadAllText(otherPath));
                var target = targetJson.RootElement.GetProperty("target").Deserialize<TaskWindowTarget>()!;
                var other = otherJson.RootElement.GetProperty("target").Deserialize<TaskWindowTarget>()!;
                var native = new WindowsTaskWindowActivation();
                await page.WaitForNativeStartupAsync().WaitAsync(TimeSpan.FromSeconds(45));
                foreach (var scenario in new[] { "timeout", "denied", "late-success", "reopen", "keyboard-busy", "input-rejected", "stale-target" })
                {
                    if (onlyCase is not null && scenario != onlyCase) continue;
                    ShowOverlay();
                    await Until(() => input!.IsForeground && page.ForegroundValidationReady && !overlayOpenPending && overlayMotion?.Playback is { IsCompleted: true });
                    await Until(() => unchecked((uint)Environment.TickCount64 - WindowsTaskWindowActivation.Observe(0).LastInputTick) >= 750);
                    // Real input eligibility for these owned test windows only.
                    if (HandoffValidationGetAsyncKeyState(0x10) < 0) throw new InvalidOperationException("Release Shift before native tests.");
                    var beforeInput = WindowsTaskWindowActivation.Observe(0).LastInputTick;
                    var key = new KeyboardValidationInput { Type = 1, Key = 0x10 };
                    try { Check(KeyboardValidationSendInput(1, in key, Marshal.SizeOf<KeyboardValidationInput>()) == 1, scenario + ": fixture input delivered"); }
                    finally { key.Flags = 2; KeyboardValidationSendInput(1, in key, Marshal.SizeOf<KeyboardValidationInput>()); }
                    await Until(() => WindowsTaskWindowActivation.Observe(0).LastInputTick != beforeInput && HandoffValidationGetAsyncKeyState(0x10) >= 0);
                    input!.HandoffValidationButtons = 0;
                    var requests = 0;
                    var activationCompletedAt = 0L;
                    Task lateAction = Task.CompletedTask;
                    hostTaskActivator = (_, _, _) =>
                    {
                        Check(!AppWindow.IsVisible && !input.IsActive, scenario + ": activation executes after hide and input release");
                        Check(native.RequestActivation(other), scenario + ": controlled non-target foreground selected");
                        if (scenario is "late-success" or "reopen") lateAction = ChangeDuringConfirmationAsync();
                        activationCompletedAt = Environment.TickCount64;
                        return Task.FromResult(new WidgetTaskActivationResult(scenario == "denied" ? "Denied" : "Requested", "fixture", 0, "controlled activation result"));
                    };
                    systemTaskViewLauncher = () =>
                    {
                        Check(!AppWindow.IsVisible && !input.IsActive && taskHandoff is { Committed: true }, scenario + ": fallback retains closed overlay and retired input");
                        Check(!native.IsTargetForeground(target) && Environment.TickCount64 - activationCompletedAt >= TaskSwitchConfirmation.ConfirmationMilliseconds,
                            scenario + ": fallback waits for failed foreground confirmation");
                        requests++;
                        return WindowsTaskView.Request(keyCode => scenario == "keyboard-busy" && keyCode == 0x10,
                            events => scenario == "input-rejected" ? 0u : (uint)events.Length);
                    };
                    var requestedTarget = scenario == "stale-target" ? target with { ProcessCreated = target.ProcessCreated + 1 } : target;
                    var result = await RunHostTaskHandoffAsync(page, new(requestedTarget, Environment.TickCount64, 0, () => true));
                    await lateAction;
                    outcomes.Add(new { scenario, activation = result.Activation.ToString(), fallback = result.Fallback?.ToString(), requests,
                        observed = WindowsTaskWindowActivation.Observe((nint)(nuint)target.Handle) });
                    switch (scenario)
                    {
                        case "late-success":
                            Check(result.Activation == TaskWindowActivationResult.Requested && result.Fallback is null && requests == 0 && native.IsTargetForeground(target), "late activation prevents fallback"); break;
                        case "reopen":
                            Check(requests == 0 && AppWindow.IsVisible && overlayRequestedVisible && input.IsForeground, "reopening cancels fallback without closing the reopened overlay"); break;
                        case "stale-target":
                            Check(requests == 0 && AppWindow.IsVisible && result.Activation == TaskWindowActivationResult.Rejected, "rejected identity never closes or opens Task View"); break;
                        default:
                            var expected = scenario == "keyboard-busy" ? WindowsTaskViewResult.KeyboardBusy : scenario == "input-rejected" ? WindowsTaskViewResult.InputRejected : WindowsTaskViewResult.Requested;
                            Check(requests == 1 && result.Activation == TaskWindowActivationResult.Denied && result.Fallback == expected, scenario + ": distinct fallback outcome, exactly one request"); break;
                    }

                    async Task ChangeDuringConfirmationAsync()
                    {
                        await Task.Delay(100);
                        if (scenario == "reopen") ShowOverlay();
                        else Check(native.RequestActivation(target), "late target activation accepted");
                    }
                }
                Check(outcomes.Count > 0, "at least one named scenario executed");
                Write(true, null);
            }
            catch (Exception error) { Write(false, error.ToString()); }
            finally
            {
                hostTaskActivator = originalActivation; systemTaskViewLauncher = originalTaskView;
                page.WindowActivationDiagnostic = originalDiagnostic;
                if (input is not null) input.HandoffValidationButtons = null;
            }
            async Task Until(Func<bool> ready)
            {
                var until = Environment.TickCount64 + 8000;
                while (!ready()) { if (Environment.TickCount64 >= until) throw new TimeoutException("Task fallback fixture did not settle."); await Task.Delay(20); }
            }
            void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); checks.Add(message); }
            void Write(bool passed, string? error)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
                File.WriteAllText(path, JsonSerializer.Serialize(new { passed, checks, outcomes, trace, error,
                    scope = "Real native window ownership and production handoff; activation failures controlled; Task View chord observed without opening system UI." }));
            }
        };
    }
}
