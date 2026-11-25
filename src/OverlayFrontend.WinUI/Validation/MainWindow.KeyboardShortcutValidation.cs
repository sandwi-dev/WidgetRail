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
                await CheckCustomShortcutsAsync();
                Write(true, null);
            }
            catch (Exception error) { Write(false, error.ToString()); }
            finally { InjectF1(released: true); }

            async Task CheckCustomShortcutsAsync()
            {
                var root = Path.Combine(Path.GetTempPath(), "wrail-native-hotkey-" + Guid.NewGuid().ToString("N"));
                var store = new PlatformSettingsStore(new(root));
                var first = new OverlayShortcutSettings { Enabled = true };
                var custom = new OverlayShortcutSettings { Enabled = true, Key = 0x86,
                    Modifiers = OverlayShortcutModifiers.Control | OverlayShortcutModifiers.Alt };
                await store.UpdateAsync(document => document with { Controllers = new() { F1ShortcutEnabled = true } });
                try
                {
                    SetKeyboardEditorOpen(true);
                    var captured = 0;
                    keyboardCapture = chord => { if (chord.Key == 0x70) captured++; };
                    var visibility = overlayVisibilityVersion;
                    InjectF1(false); InjectF1(true);
                    await Until(() => captured == 1);
                    Check(overlayVisibilityVersion == visibility, "recording the current shortcut captures its chord without toggling the overlay");
                    var error = await SaveKeyboardShortcutAsync(root, first, custom, default);
                    Check(error is null && keyboardShortcut?.Shortcut == custom, "a custom modified key reserves and commits before replacing F1");
                    Check((await store.LoadAsync()).Controllers.EffectiveOverlayShortcut == custom, "custom shortcut is durable across settings reload");
                    var oldReclaimed = ValidationRegisterHotKey(0, 78, 0x4000, 0x70) != 0;
                    try { Check(oldReclaimed, "successful rebind releases F1"); }
                    finally { if (oldReclaimed) ValidationUnregisterHotKey(0, 78); }
                    var same = keyboardShortcut;
                    Check(await SaveKeyboardShortcutAsync(root, custom, custom, default) is null && ReferenceEquals(same, keyboardShortcut),
                        "saving the current combination keeps its registration rather than conflicting with itself");
                    var blocked = ValidationRegisterHotKey(0, 79, 0x4003, 0x87) != 0;
                    try
                    {
                        Check(blocked, "native conflict fixture reserves its own unrelated combination");
                        var conflict = await SaveKeyboardShortcutAsync(root, custom, custom with { Key = 0x87 }, default);
                        Check(conflict is not null && ReferenceEquals(same, keyboardShortcut) &&
                            (await store.LoadAsync()).Controllers.EffectiveOverlayShortcut == custom,
                            "Windows conflict preserves both the active registration and saved preference");
                    }
                    finally { if (blocked) ValidationUnregisterHotKey(0, 79); }
                    var invalidRoot = Path.Combine(root, "not-a-directory");
                    await File.WriteAllTextAsync(invalidRoot, "fixture");
                    var saveFailure = await SaveKeyboardShortcutAsync(invalidRoot, custom, custom with { Key = 0x85 }, default);
                    Check(saveFailure is not null && ReferenceEquals(same, keyboardShortcut), "storage failure preserves the previous registered shortcut");
                    var candidateReleased = ValidationRegisterHotKey(0, 80, 0x4003, 0x85) != 0;
                    try { Check(candidateReleased, "storage failure releases its provisional registration"); }
                    finally { if (candidateReleased) ValidationUnregisterHotKey(0, 80); }
                    using (var cancelled = new CancellationTokenSource())
                    {
                        cancelled.Cancel();
                        try { await SaveKeyboardShortcutAsync(root, custom, custom with { Key = 0x85 }, cancelled.Token); Check(false, "cancelled save rejected"); }
                        catch (OperationCanceledException) { Check(ReferenceEquals(same, keyboardShortcut), "cancelled save leaves current registration intact"); }
                    }
                    SetKeyboardEditorOpen(false);
                    InjectKey(0x11, false); InjectKey(0x12, false); InjectKey(0x86, false);
                    InjectKey(0x86, true); InjectKey(0x12, true); InjectKey(0x11, true);
                    await Until(() => !AppWindow.IsVisible);
                    Check(!overlayRequestedVisible && input?.IsActive == false, "modified global shortcut hides the overlay and releases controller admission");
                    InjectKey(0x11, false); InjectKey(0x12, false); InjectKey(0x86, false);
                    InjectKey(0x86, true); InjectKey(0x12, true); InjectKey(0x11, true);
                    await Until(() => AppWindow.IsVisible && input?.IsForeground == true && overlayRequestedVisible);
                    Check(input?.IsActive == true, "modified global shortcut reopens from hidden state");
                    await Until(() => overlayMotion?.Playback is { IsCompleted: true });
                    Check(await SaveKeyboardShortcutAsync(root, custom, custom with { Enabled = false }, default) is null && keyboardShortcut is null,
                        "disabling a custom binding releases it after persistence");
                    var overlay=custom with{Enabled=false};
                    Check(await SaveKeyboardShortcutAsync(root,overlay,custom,default) is null,"Overlay binding can be restored alongside replay");
                    var replayDefault=OverlayShortcutSettings.ReplayDefault;
                    var replayConflict=await SaveKeyboardShortcutAsync(root,replayDefault,custom,default,HostKeyboardShortcutKind.SaveReplay);
                    Check(replayConflict is not null&&keyboardShortcut?.Shortcut==custom&&replaySaveShortcut is null,
                        "Replay cannot steal the overlay chord or change either saved preference on conflict");
                    var replay=replayDefault with{Enabled=true,Key=0x84,Modifiers=OverlayShortcutModifiers.Control|OverlayShortcutModifiers.Alt};
                    Check(await SaveKeyboardShortcutAsync(root,replayDefault,replay,default,HostKeyboardShortcutKind.SaveReplay) is null&&replaySaveShortcut?.IsRegistered==true,
                        "Replay gets an independent reserved native hotkey");
                    var replayCalls=0;replaySaveCommand=()=>replayCalls++;
                    var unchangedVisibility=overlayVisibilityVersion;
                    InjectKey(0x11,false);InjectKey(0x12,false);InjectKey(0x84,false);InjectKey(0x84,true);InjectKey(0x12,true);InjectKey(0x11,true);
                    await Until(()=>replayCalls==1);
                    Check(overlayVisibilityVersion==unchangedVisibility&&input?.IsActive==true,"Replay shortcut invokes only its command without toggling overlay input");
                    SetKeyboardEditorOpen(true);
                    var replayCaptured=0;keyboardCapture=chord=>{if(chord.Key==0x84)replayCaptured++;};
                    InjectKey(0x11,false);InjectKey(0x12,false);InjectKey(0x84,false);InjectKey(0x84,true);InjectKey(0x12,true);InjectKey(0x11,true);
                    await Until(()=>replayCaptured==1);
                    Check(replayCalls==1,"Keyboard recording suppresses replay saves as well as overlay toggles");
                    Check(await SaveKeyboardShortcutAsync(root,replay,replay with{Enabled=false},default,HostKeyboardShortcutKind.SaveReplay) is null&&replaySaveShortcut is null&&keyboardShortcut?.Shortcut==custom,
                        "Disabling replay releases only its own registration");
                    SetKeyboardEditorOpen(false);
                    var received = new List<HostKeyboardShortcutKind>();
                    var originalCommand = trayShortcutCommand;
                    try
                    {
                        trayShortcutCommand = received.Add;
                        foreach (var kind in new[] { HostKeyboardShortcutKind.TrayUp, HostKeyboardShortcutKind.TrayRight,
                            HostKeyboardShortcutKind.TrayDown, HostKeyboardShortcutKind.TrayLeft })
                        {
                            var previous = (await store.LoadAsync()).Controllers.GetShortcut(kind);
                            var chord = previous with { Enabled = true, Key = 0x7C + received.Count };
                            var conflict = await SaveKeyboardShortcutAsync(root, previous, custom, default, kind);
                            Check(conflict is not null && KeyboardRegistration(kind) is null, kind + " cannot steal the overlay shortcut");
                            Check(await SaveKeyboardShortcutAsync(root, previous, chord, default, kind) is null, kind + " reserved independently");
                            InjectKey(0x11,false); InjectKey(0x12,false); InjectKey((ushort)chord.Key,false);
                            InjectKey((ushort)chord.Key,true); InjectKey(0x12,true); InjectKey(0x11,true);
                            await Until(() => received.LastOrDefault() == kind);
                            Check(overlayVisibilityVersion == unchangedVisibility && (await store.LoadAsync()).Controllers.GetShortcut(kind) == chord,
                                kind + " dispatches the assigned direction without toggling visibility and persists");
                        }
                        Check(received.Count == 4, "Exactly one dispatch for each direction");
                    }
                    finally { trayShortcutCommand = originalCommand; }
                }
                finally
                {
                    InjectKey(0x86, true); InjectKey(0x12, true); InjectKey(0x11, true);
                    InjectKey(0x84,true);replaySaveCommand=null;
                    SetKeyboardEditorOpen(false);
                    ApplyKeyboardShortcutSettings(new());
                    if (Directory.Exists(root)) Directory.Delete(root, true);
                }
            }

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
        => InjectKey(0x70, released);

    private static void InjectKey(ushort key, bool released)
    {
        var value = new KeyboardValidationInput { Type = 1, Key = key, Flags = released ? 2u : 0u };
        if (KeyboardValidationSendInput(1, in value, Marshal.SizeOf<KeyboardValidationInput>()) != 1)
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastPInvokeError(), "The isolated shortcut input was not delivered.");
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
