using System.Runtime.InteropServices;
using System.Text.Json;
using WidgetRail.PlatformSettings;

namespace WidgetRail.OverlayFrontend.WinUI;

public sealed partial class MainWindow
{
    private void EnableKeyboardShortcutValidation(Shell.OverlayShellPage page, string path)
    {
        var started = false;
        page.Loaded += async (_, _) =>
        {
            if (started) return;
            started = true;
            var checks = new List<string>();
            try
            {
                await page.WaitForNativeStartupAsync().WaitAsync(TimeSpan.FromSeconds(40));
                await Until(() => overlayMotion?.Playback is { IsCompleted: true } && input?.IsForeground == true);
                ApplyKeyboardShortcutSettings(new());
                Check(keyboardShortcut is null, "default settings do not register global F1");
                ApplyKeyboardShortcutSettings(new() { F1ShortcutEnabled = true });
                Check(keyboardShortcut?.IsRegistered == true, "enabled preference registers global F1");
                // No widget actions: the only injected key is confirmed registered
                // to this owned window, so Windows routes it even while hidden.
                var before = overlayVisibilityVersion;
                if (PostMessageW(WinRT.Interop.WindowNative.GetWindowHandle(this), 0x0312, 2, 0) == 0)
                    throw new System.ComponentModel.Win32Exception(Marshal.GetLastPInvokeError());
                await Task.Delay(60);
                Check(overlayVisibilityVersion == before, "unrelated hotkey messages cannot toggle the overlay");
                InjectF1(released: false);
                await Until(() => !AppWindow.IsVisible);
                Check(!overlayRequestedVisible && input?.IsActive == false, "Windows-delivered F1 hides and releases visible input");
                var hiddenVersion = overlayVisibilityVersion;
                InjectF1(released: false);
                await Task.Delay(160);
                Check(!AppWindow.IsVisible && overlayVisibilityVersion == hiddenVersion, "holding F1 does not repeat the toggle");
                InjectF1(released: true);
                InjectF1(released: false);
                await Until(() => AppWindow.IsVisible && input?.IsForeground == true && overlayRequestedVisible);
                InjectF1(released: true);
                Check(input?.IsActive == true, "global F1 reopens the hidden overlay and restores controller admission");
                await Until(() => overlayMotion?.Playback is { IsCompleted: true });

                ApplyKeyboardShortcutSettings(new());
                Check(keyboardShortcut is null, "disabling F1 retires its message admission immediately");
                var reclaimed = ValidationRegisterHotKey(0, 77, 0x4000, 0x70) != 0;
                try { Check(reclaimed, "shortcut disposal releases the operating-system registration"); }
                finally { if (reclaimed) ValidationUnregisterHotKey(0, 77); }
                ApplyKeyboardShortcutSettings(new() { F1ShortcutEnabled = true });
                Check(keyboardShortcut!.IsRegistered, "a fresh owner can register F1 after orderly retirement");
                Write(true, null);
            }
            catch (Exception error) { Write(false, error.ToString()); }
            finally { InjectF1(released: true); }

            async Task Until(Func<bool> condition)
            {
                var deadline = Environment.TickCount64 + 8000;
                while (!condition())
                {
                    if (Environment.TickCount64 >= deadline) throw new TimeoutException("F1 validation did not settle.");
                    await Task.Delay(15);
                }
            }
            void Check(bool condition, string message)
            { if (!condition) throw new InvalidOperationException(message); checks.Add(message); }
            void Write(bool passed, string? error)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
                File.WriteAllText(path, JsonSerializer.Serialize(new { passed, checks, error }));
            }
        };
    }

    // INPUT is 40 bytes on the supported x64/ARM64 Windows ABI. Explicit union
    // offsets retain the 32-byte mouse member even though this probe uses keys.
    [StructLayout(LayoutKind.Explicit, Size = 40)]
    private struct KeyboardValidationInput
    {
        [FieldOffset(0)] internal uint Type;
        [FieldOffset(8)] internal ushort Key;
        [FieldOffset(12)] internal uint Flags;
    }
    private static void InjectF1(bool released)
    {
        var value = new KeyboardValidationInput { Type = 1, Key = 0x70, Flags = released ? 2u : 0u };
        if (KeyboardValidationSendInput(1, in value, Marshal.SizeOf<KeyboardValidationInput>()) != 1)
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastPInvokeError(), "The isolated F1 input was not delivered.");
    }
    [LibraryImport("user32.dll", EntryPoint = "SendInput", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial uint KeyboardValidationSendInput(uint count, in KeyboardValidationInput input, int size);
    [LibraryImport("user32.dll", EntryPoint = "RegisterHotKey", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial int ValidationRegisterHotKey(nint window, int id, uint modifiers, uint key);
    [LibraryImport("user32.dll", EntryPoint = "UnregisterHotKey")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial int ValidationUnregisterHotKey(nint window, int id);
    [LibraryImport("user32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial int PostMessageW(nint window, uint message, nuint wParam, nint lParam);
}
