using WidgetRail.PlatformSettings;

namespace WidgetRail.OverlayFrontend.WinUI;

public sealed partial class MainWindow
{
    private bool keyboardEditorOpen;
    private Action<OverlayShortcutSettings>? keyboardCapture;
    private Input.OverlayKeyboardShortcut? replaySaveShortcut;
    private Action? replaySaveCommand;
    private readonly SemaphoreSlim keyboardUpdateGate = new(1, 1);

    private readonly Dictionary<HostKeyboardShortcutKind, Input.OverlayKeyboardShortcut> trayKeyboardShortcuts = [];
    private Action<HostKeyboardShortcutKind>? trayShortcutCommand;
    private Input.OverlayKeyboardShortcut? KeyboardRegistration(HostKeyboardShortcutKind kind) => kind switch {
        HostKeyboardShortcutKind.Overlay => keyboardShortcut, HostKeyboardShortcutKind.SaveReplay => replaySaveShortcut,
        _ => trayKeyboardShortcuts.GetValueOrDefault(kind)
    };
    private void SetKeyboardRegistration(HostKeyboardShortcutKind kind, Input.OverlayKeyboardShortcut? value)
    {
        if (kind == HostKeyboardShortcutKind.Overlay) keyboardShortcut = value;
        else if (kind == HostKeyboardShortcutKind.SaveReplay) replaySaveShortcut = value;
        else if (value is null) trayKeyboardShortcuts.Remove(kind);
        else trayKeyboardShortcuts[kind] = value;
    }
    private void DisposeTrayShortcuts()
    {
        foreach (var shortcut in trayKeyboardShortcuts.Values) shortcut.Dispose();
        trayKeyboardShortcuts.Clear();
        trayShortcutCommand = null;
    }
    private Input.OverlayKeyboardShortcut CreateKeyboardShortcut(OverlayShortcutSettings settings,HostKeyboardShortcutKind kind=HostKeyboardShortcutKind.Overlay)
    {
        var registration = new Input.OverlayKeyboardShortcut(WinRT.Interop.WindowNative.GetWindowHandle(this), () =>
        {
            if (cleanupStarted || keyboardEditorOpen) return;
            input?.TraceInput("Keyboard shortcut received: "+kind);
            if (kind == HostKeyboardShortcutKind.Overlay) ToggleOverlay();
            else if (kind == HostKeyboardShortcutKind.SaveReplay) replaySaveCommand?.Invoke();
            else trayShortcutCommand?.Invoke(kind);
        }, settings);
        registration.CapturedWhileSuppressed = value => keyboardCapture?.Invoke(value);
        registration.SetSuppressed(keyboardEditorOpen);
        return registration;
    }

    private void SetKeyboardEditorOpen(bool value)
    {
        keyboardEditorOpen = value;
        keyboardShortcut?.SetSuppressed(value);
        replaySaveShortcut?.SetSuppressed(value);
        foreach (var shortcut in trayKeyboardShortcuts.Values) shortcut.SetSuppressed(value);
        if (!value) keyboardCapture = null;
    }

    private async Task<string?> SaveKeyboardShortcutAsync(string root, OverlayShortcutSettings expected,
        OverlayShortcutSettings requested, CancellationToken token,HostKeyboardShortcutKind kind=HostKeyboardShortcutKind.Overlay)
    {
        if (!requested.IsWellFormed()) return "Choose a supported keyboard key and modifiers.";
        await keyboardUpdateGate.WaitAsync(token);
        Input.OverlayKeyboardShortcut? candidate = null;
        try
        {
            if (cleanupStarted) return "WidgetRail is closing.";
            var sameRegistration = KeyboardRegistration(kind) is { IsRegistered: true } current &&
                current.Shortcut.Key == requested.Key && current.Shortcut.Modifiers == requested.Modifiers;
            if (requested.Enabled && !sameRegistration)
            {
                candidate = CreateKeyboardShortcut(requested,kind);
                candidate.SetSuppressed(true);
                if (!candidate.IsRegistered)
                    return $"{requested.DisplayName} could not be registered. Windows or another application may be using it (error {candidate.ErrorCode}). Your previous shortcut is unchanged.";
            }
            await new PlatformSettingsStore(new(root)).UpdateAsync(document =>
            {
                if (document.Controllers.GetShortcut(kind) != expected)
                    throw new InvalidOperationException("The shortcut changed while this editor was open. Cancel and open it again.");
                return document with { Controllers = document.Controllers.WithShortcut(kind, requested) };
            }, token);
            // Save is the commit point. Even if dismissal races completion, adopt the
            // committed preference so disk and Windows never disagree about our owner.
            ++keyboardSettingsVersion;
            if (!requested.Enabled || candidate is not null)
            {
                var previous = KeyboardRegistration(kind);
                SetKeyboardRegistration(kind,cleanupStarted ? null : candidate);
                if (!cleanupStarted) candidate = null;
                previous?.Dispose();
            }
            if (cleanupStarted) { KeyboardRegistration(kind)?.Dispose(); SetKeyboardRegistration(kind,null); }
            else KeyboardRegistration(kind)?.SetSuppressed(keyboardEditorOpen);
            return null;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (Exception error) when (error is PlatformSettingsException or IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            Diagnostics.FrontendFailureLog.Current.Write("keyboard-shortcut-save", error);
            return error is InvalidOperationException ? error.Message : "The shortcut could not be saved. Your previous shortcut is unchanged.";
        }
        finally { candidate?.Dispose(); keyboardUpdateGate.Release(); }
    }
}
