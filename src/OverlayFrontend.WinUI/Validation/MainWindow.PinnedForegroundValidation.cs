using WidgetRail.WindowsWindowActivation;
using WidgetRail.PlatformSettings;

namespace WidgetRail.OverlayFrontend.WinUI
{
    public sealed partial class MainWindow
    {
        private void EnablePinnedForegroundValidation(Shell.OverlayShellPage page, string path, nint peer, nint other, bool liveTarget)
        {
            var started = false;
            page.Loaded += async (_, _) =>
            {
                if (started) return;
                started = true;
                var checks = new List<string>();
                var observations = new List<object>();
                try
                {
                    await page.WaitForNativeStartupAsync().WaitAsync(TimeSpan.FromSeconds(45));
                    await Until(() => page.NativeStartupHasContent && input?.IsForeground == true);
                    await page.CreateForegroundValidationPinAsync();
                    var pin = page.ForegroundValidationPin ?? throw new InvalidOperationException("Choose a widget with full-widget pinning in the isolated shell configuration.");
                    foreach (var motion in new[] { MotionPreference.Full, MotionPreference.Reduced })
                    {
                        page.ApplyOverlayMotionValidationAppearance(page.Appearance with { Motion = motion });
                        await OpenFrom(peer);
                        HideOverlay();
                        await ClosedOn(peer, "main close " + motion);
                        await OpenFrom(peer);
                        await page.EnterForegroundValidationPinAsync();
                        Check(Foreground() == pin.Handle, "View entry owns native foreground " + motion);
                        ToggleOverlay();
                        await ClosedOn(peer, "pin close " + motion);
                        Check(pin.IsVisible && !pin.Interactive, "pin survives passive " + motion);
                    }

                    await OpenFrom(peer);
                    await page.EnterForegroundValidationPinAsync();
                    HideOverlayImmediately();
                    await ClosedOn(peer, "immediate pin close");

                    page.ApplyOverlayMotionValidationAppearance(page.Appearance with { Motion = MotionPreference.Full, WidgetAnimationSpeed = .5 });
                    await OpenFrom(peer);
                    await page.EnterForegroundValidationPinAsync();
                    HideOverlay();
                    ShowOverlay();
                    await Task.Delay(800);
                    Check(AppWindow.IsVisible && input!.IsForeground, "reopen cancels old return and hide");
                    HideOverlay();
                    await ClosedOn(peer, "close after cancelled exit retains original target");

                    await OpenFrom(peer);
                    await page.EnterForegroundValidationPinAsync();
                    HideOverlay();
                    WinUIEx.HwndExtensions.SetForegroundWindow(other);
                    await Until(() => Foreground() == other);
                    await ClosedOn(other, "external switch during exit is preserved");

                    await OpenFrom(other);
                    await page.EnterForegroundValidationPinAsync();
                    HideOverlay();
                    await ClosedOn(other, "next opening remembers the new application");

                    await OpenFrom(peer);
                    var remembered = foregroundReturnTarget ?? throw new InvalidOperationException("No return identity captured.");
                    var native = new WindowsTaskWindowActivation();
                    var beforeInvalid = Foreground();
                    Check(!native.TryReturnForeground(remembered with { ProcessCreated = remembered.ProcessCreated + 1 }) &&
                        !native.TryReturnForeground(remembered with { ClassName = "not-the-original-class" }) &&
                        !native.TryReturnForeground(remembered with { ProcessId = (uint)Environment.ProcessId }) &&
                        Foreground() == beforeInvalid, "stale process/class/owned identities cannot activate a target");

                    // Visibility mutations are only for the script-owned peer.
                    foreach (var command in liveTarget ? Array.Empty<int>() : new[] { 0 /* SW_HIDE */, 6 /* SW_MINIMIZE */ })
                    {
                        PinnedForegroundShowWindow(peer, command);
                        await Until(() => { var s = WindowsTaskWindowActivation.Observe(peer); return command == 0 ? !s.TargetVisible : s.TargetMinimized; });
                        Check(!native.TryReturnForeground(remembered) && Foreground() == beforeInvalid,
                            command == 0 ? "hidden target is not resurrected" : "minimized target is not restored by ordinary return");
                        PinnedForegroundShowWindow(peer, 4 /* SW_SHOWNOACTIVATE */);
                        await Until(() => { var s = WindowsTaskWindowActivation.Observe(peer); return s.TargetVisible && !s.TargetMinimized; });
                    }
                    HideOverlay();
                    await ClosedOn(peer, "valid identity remains usable after rejected returns");

                    await OpenFrom(peer);
                    input!.HandoffValidationButtons = 0;
                    Check(await OpenExternalAsync(page, () =>
                    {
                        WinUIEx.HwndExtensions.SetForegroundWindow(other);
                        return Task.FromResult(true);
                    }, CancellationToken.None), "external launch handoff completes");
                    await ClosedOn(other, "external launch destination is not replaced by ordinary return");

                    await OpenFrom(peer);
                    await page.EnterForegroundValidationPinAsync();
                    Check(await HideForCaptureAsync(page, CancellationToken.None), "capture preparation still completes from a pin");
                    Check(!AppWindow.IsVisible && !input.IsActive && foregroundReturnTarget is null,
                        "capture handoff retires input and ordinary return without another activation");
                    input.HandoffValidationButtons = null;

                    Write(true, null);
                }
                catch (Exception error) { Write(false, error.ToString()); }

                long Foreground() => WindowsTaskWindowActivation.Observe(0).Foreground;
                async Task OpenFrom(nint target)
                {
                    // Establish fixture permission before choosing a different
                    // app. Background test code cannot switch external apps on
                    // the user's behalf merely because an earlier close passed.
                    if (Foreground() != target)
                    {
                        ShowOverlay();
                        await Until(() => input!.IsForeground && page.ForegroundValidationReady && !overlayOpenPending && overlayMotion?.Playback is { IsCompleted: true });
                        // This is setup for a new external app, not the return
                        // under test. Match the existing task-handoff fixture's
                        // real last-input boundary; HWND ownership alone is not
                        // permission to select an unrelated app after attachment.
                        if (HandoffValidationGetAsyncKeyState(0x10) < 0) throw new InvalidOperationException("Release Shift before running native foreground tests.");
                        var previousInput = WindowsTaskWindowActivation.Observe(0).LastInputTick;
                        var key = new KeyboardValidationInput { Type = 1, Key = 0x10 };
                        try
                        {
                            if (KeyboardValidationSendInput(1, in key, System.Runtime.InteropServices.Marshal.SizeOf<KeyboardValidationInput>()) != 1)
                                throw new InvalidOperationException("Fixture setup input was not delivered.");
                        }
                        finally { key.Flags = 2; KeyboardValidationSendInput(1, in key, System.Runtime.InteropServices.Marshal.SizeOf<KeyboardValidationInput>()); }
                        await Until(() => WindowsTaskWindowActivation.Observe(0).LastInputTick != previousInput);
                    }
                    WinUIEx.HwndExtensions.SetForegroundWindow(target);
                    await Until(() => Foreground() == target && !AppWindow.IsVisible);
                    ShowOverlay();
                    await Until(() => AppWindow.IsVisible && input!.IsForeground && page.ForegroundValidationReady && !overlayOpenPending && overlayMotion?.Playback is { IsCompleted: true });
                }
                async Task ClosedOn(nint target, string label)
                {
                    await Until(() => !AppWindow.IsVisible);
                    await Task.Delay(150);
                    var observation = WindowsTaskWindowActivation.Observe(target);
                    observations.Add(new { label, observation });
                    Check(observation.Foreground == target && !input!.IsActive && desktopBackdrop?.IsVisible != true,
                        label + ": target foreground; overlay input and backdrop retired");
                }
                async Task Until(Func<bool> predicate)
                {
                    var until = Environment.TickCount64 + 8000;
                    while (!predicate())
                    {
                        if (Environment.TickCount64 >= until) throw new TimeoutException("Foreground validation: " + WindowsTaskWindowActivation.Observe(peer));
                        await Task.Delay(20);
                    }
                }
                void Check(bool condition, string message)
                { if (!condition) throw new InvalidOperationException(message); checks.Add(message); }
                void Write(bool passed, string? error)
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
                    File.WriteAllText(path, System.Text.Json.JsonSerializer.Serialize(new { passed, checks, observations, error }));
                }
            };
        }

        [System.Runtime.InteropServices.LibraryImport("user32.dll", EntryPoint = "ShowWindowAsync")]
        [System.Runtime.InteropServices.DefaultDllImportSearchPaths(System.Runtime.InteropServices.DllImportSearchPath.System32)]
        private static partial int PinnedForegroundShowWindow(nint window, int command);
    }
}

namespace WidgetRail.OverlayFrontend.WinUI.Shell
{
    internal sealed partial class OverlayShellPage
    {
        internal PinnedWidgetWindow? ForegroundValidationPin => pinned?.Window;
        internal bool ForegroundValidationReady => visible && foreground && !switching;
        internal Task CreateForegroundValidationPinAsync() => PinAsync(activeWidget!, WidgetRail.WidgetPresentationSession.WidgetPinnedProjection.FullWidgetLayoutId);
        internal async Task EnterForegroundValidationPinAsync()
        {
            await RouteButtonAsync(WidgetRail.WidgetProtocol.ControllerButton.View, WidgetRail.WidgetProtocol.ControllerEventPhase.Pressed);
            await RouteButtonAsync(WidgetRail.WidgetProtocol.ControllerButton.View, WidgetRail.WidgetProtocol.ControllerEventPhase.Released);
        }
    }
}
