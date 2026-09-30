using WidgetRail.OverlayFrontend.WinUI.Shell;
using WidgetRail.PlatformSettings;
using System.Runtime.InteropServices;
using WidgetRail.WindowsWindowActivation;

namespace WidgetRail.OverlayFrontend.WinUI;

public sealed partial class MainWindow
{
    private void EnableReleaseTaskActivationValidation(OverlayShellPage page, string result, string peer, bool reduced, string? cancel, bool primeInput)
    {
        AppearanceSettings? original = null;
        page.EnableTaskActivationValidation(result, peer, async () =>
        {
            if (input is null) throw new InvalidOperationException("Task handoff validation requires the native input adapter.");
            original = page.Appearance;
            page.ApplyOverlayMotionValidationAppearance(original with { Motion = reduced ? MotionPreference.Reduced : MotionPreference.Full });
            input.HandoffValidationButtons = 0x1000;
            if (primeInput)
            {
                // Establish real last-input eligibility only on this owned
                // fixture. A neutral Shift tap executes no widget command.
                if (!input.IsForeground || (HandoffValidationGetAsyncKeyState(0x10) & 0x8000) != 0)
                    throw new InvalidOperationException("Cannot prime input outside the owned fixture or while Shift is held.");
                var previous = WindowsTaskWindowActivation.Observe(0).LastInputTick;
                var key = new KeyboardValidationInput { Type = 1, Key = 0x10 };
                try
                {
                    if (KeyboardValidationSendInput(1, in key, Marshal.SizeOf<KeyboardValidationInput>()) != 1)
                        throw new InvalidOperationException("Fixture input eligibility tap failed.");
                }
                finally
                {
                    key.Flags = 2;
                    _ = KeyboardValidationSendInput(1, in key, Marshal.SizeOf<KeyboardValidationInput>());
                }
                await Until(() => WindowsTaskWindowActivation.Observe(0).LastInputTick != previous);
            }
        }, async checks =>
        {
            await Until(() => taskHandoff is { Started: true });
            await Task.Delay(60);
            Check(AppWindow.IsVisible && input!.IsActive && input.IsForeground && taskHandoff is { MotionStarted: false },
                "Held selecting A retains native foreground/read ownership before closing");
            Check(!ShellRoot.IsHitTestVisible && !page.OverlayMotionValidationVisible && page.OverlayMotionValidationRetaining,
                "Pending handoff retires widget actions while preserving its presentation");
            if (cancel is "reopen" or "expiry")
            {
                if (cancel == "reopen") ShowOverlay();
                await Until(() => taskHandoff is null, 3000);
                input!.HandoffValidationButtons = 0;
                Check(AppWindow.IsVisible && overlayRequestedVisible && page.OverlayMotionValidationVisible && ShellRoot.IsHitTestVisible,
                    cancel == "reopen" ? "Reopen cancels the old handoff without a later hide" : "Held gesture expiry restores the still-visible presentation");
                return;
            }
            input!.HandoffValidationButtons = 0;
            await Until(() => taskHandoff is { MotionStarted: true });
            if (!reduced)
            {
                Check(overlayMotion?.Playback is { IsCompleted: false }, "Task switch runs the existing full close animation");
                input.HandoffValidationButtons = 0x1000;
                await overlayMotion!.Playback!.WaitAsync(TimeSpan.FromSeconds(1));
                await Task.Delay(45);
                Check(AppWindow.IsVisible && taskHandoff is { Committed: false }, "New A during close cannot leak past animation completion");
                input.HandoffValidationButtons = 0;
            }
            else Check(overlayMotion?.Playback is { IsCompleted: true }, "Reduced motion settles without adding an animation delay");
            void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); checks.Add(message); }
        }, () =>
        {
            if (input is not null) input.HandoffValidationButtons = null;
            if (original is not null) page.ApplyOverlayMotionValidationAppearance(original);
        }, expectCancellation: cancel is "reopen" or "expiry");
        static async Task Until(Func<bool> condition, int timeout = 1500)
        {
            var deadline = Environment.TickCount64 + timeout;
            while (!condition())
            {
                if (Environment.TickCount64 >= deadline) throw new TimeoutException("Handoff validation stage did not arrive.");
                await Task.Delay(5);
            }
        }
    }
    [LibraryImport("user32.dll", EntryPoint = "GetAsyncKeyState")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial short HandoffValidationGetAsyncKeyState(int key);
}
