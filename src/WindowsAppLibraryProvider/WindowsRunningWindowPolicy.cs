namespace WidgetRail.WindowsAppLibraryProvider;

internal sealed record RunningWindowInfo(
    nint Handle, nint Root, nint Owner, uint ProcessId, string ClassName,
    bool Visible = true, bool Cloaked = false, long ExtendedStyle = 0,
    string Title = "", bool IsMinimized = false);

internal interface IRunningWindowNative
{
    nint ShellWindow { get; }
    RunningWindowInfo? Read(nint window);
    nint LastActivePopup(nint window);
    bool IsApplicationFrameHost(uint processId);
    IReadOnlyList<nint>? Descendants(nint window);
    WindowsRunningAppObservation? Process(uint processId, bool packagedOnly);
}

/// <summary>Supported window metadata approximates ordinary Alt+Tab apps, not Shell tabs.</summary>
internal static class WindowsRunningWindowPolicy
{
    internal const int MaximumRelatedWindows = 128;
    internal const long ToolWindow = 0x80;
    internal const long AppWindow = 0x40000;
    internal const long NoActivate = 0x08000000;

    internal static WindowsRunningAppObservation? Inspect(
        nint handle, IRunningWindowNative native, uint currentProcessId)
    {
        var window = native.Read(handle);
        if (!Eligible(window, native.ShellWindow, currentProcessId) ||
            !IsRepresentative(window!, native)) return null;

        WindowsRunningAppObservation? result;
        if (window!.ClassName == "ApplicationFrameWindow")
        {
            // The frame host owns the top-level HWND, but the packaged app owns
            // the CoreWindow below it. Never expose the frame host as a portable app.
            if (!native.IsApplicationFrameHost(window.ProcessId)) return null;
            var children = native.Descendants(handle);
            if (children is null || children.Count > MaximumRelatedWindows) return null;
            var candidates = new Dictionary<string, WindowsRunningAppObservation>(StringComparer.Ordinal);
            var processes = new HashSet<uint>();
            foreach (var child in children)
            {
                var info = native.Read(child);
                if (info is null || info.Root != handle || !info.Visible ||
                    info.ClassName != "Windows.UI.Core.CoreWindow" ||
                    info.ProcessId == window.ProcessId || info.ProcessId == currentProcessId ||
                    !processes.Add(info.ProcessId)) continue;
                var observed = native.Process(info.ProcessId, packagedOnly: true);
                // Reject a window that disappeared or changed ownership during inspection.
                if (observed is null || native.Read(child) != info) return null;
                candidates.TryAdd(observed.RegistrationIdentity, observed);
            }
            // An ambiguous or partially constructed hosted window is not an app identity.
            if (candidates.Count != 1) return null;
            result = candidates.Values.Single();
        }
        else
        {
            result = native.Process(window.ProcessId, packagedOnly: false);
        }

        return native.Read(handle) == window && result is not null ? result with { Window = window } : null;
    }

    private static bool Eligible(RunningWindowInfo? window, nint shell, uint currentProcessId) =>
        window is not null && window.Handle != 0 && window.Root == window.Handle &&
        window.Handle != shell && window.Visible && !window.Cloaked &&
        window.ProcessId != 0 && window.ProcessId != currentProcessId &&
        window.ClassName is not ("" or "Progman" or "WorkerW" or
            "Shell_TrayWnd" or "Shell_SecondaryTrayWnd") &&
        (window.ExtendedStyle & ToolWindow) == 0 &&
        ((window.ExtendedStyle & NoActivate) == 0 || (window.ExtendedStyle & AppWindow) != 0);

    private static bool IsRepresentative(RunningWindowInfo window, IRunningWindowNative native)
    {
        if ((window.ExtendedStyle & AppWindow) != 0) return true;
        var root = window;
        var seen = new HashSet<nint>();
        while (root.Owner != 0 && (root.ExtendedStyle & AppWindow) == 0)
        {
            if (seen.Count >= MaximumRelatedWindows || !seen.Add(root.Handle)) return false;
            var owner = native.Read(root.Owner);
            if (owner is null) return false;
            root = owner;
        }

        // Pick the visible active popup in an owner group. Grouping by application
        // identity later collapses multiple independent windows of the same app.
        seen.Clear();
        var representative = root;
        while (seen.Count < MaximumRelatedWindows && seen.Add(representative.Handle))
        {
            var popupHandle = native.LastActivePopup(representative.Handle);
            if (popupHandle == representative.Handle) return root.Handle == window.Handle;
            if (popupHandle == 0) return false;
            var popup = native.Read(popupHandle);
            if (popup is null) return false;
            if (popup.Visible && !popup.Cloaked &&
                (popup.ExtendedStyle & (ToolWindow | NoActivate)) == 0)
                return popup.Handle == window.Handle;
            // Hidden/tool popups do not remove the visible owner from the switcher.
            var next = native.LastActivePopup(popup.Handle);
            if (next == popup.Handle) return root.Handle == window.Handle;
            representative = popup;
        }
        return false;
    }
}

