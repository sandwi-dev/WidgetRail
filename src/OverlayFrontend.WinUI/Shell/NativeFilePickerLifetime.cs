using System.Runtime.InteropServices;
using Microsoft.UI;
using Microsoft.UI.Dispatching;

namespace WidgetRail.OverlayFrontend.WinUI.Shell;

/// <summary>Cancel the owned Windows common dialog, then join its actual completion.</summary>
internal static partial class NativeFilePickerLifetime
{
#if ENABLE_WIDGET_VALIDATION
    internal static Action<string>? TraceForValidation { get; set; }
#endif
    internal static async Task<T> RunAsync<T>(WindowId ownerId, Func<Task<T>> show, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var queue = DispatcherQueue.GetForCurrentThread() ?? throw new InvalidOperationException("File pickers require the UI thread.");
        var owner = Win32Interop.GetWindowFromWindowId(ownerId);
        var timer = queue.CreateTimer();
        timer.Interval = TimeSpan.FromMilliseconds(75);
        var finished = false;
        timer.Tick += (_, _) => { if (!finished && token.IsCancellationRequested) CloseOwnedPicker(owner); };
        // Cancelling AsTask alone can leave the actual common dialog alive.
        // Keep ownership until its native operation has returned, including the
        // case where cancellation arrives before Windows creates the dialog.
        using var registration = token.Register(() => queue.TryEnqueue(() =>
        {
            if (finished) return;
#if ENABLE_WIDGET_VALIDATION
            TraceForValidation?.Invoke("cancellation requested");
#endif
            CloseOwnedPicker(owner); timer.Start();
        }));
        try
        {
            var result = await show();
            token.ThrowIfCancellationRequested();
            return result;
        }
        finally { finished = true; timer.Stop(); }
    }

    private static void CloseOwnedPicker(nint owner)
    {
        var popup = GetWindow(owner, 6); // GW_ENABLEDPOPUP
        if (TryCloseOwnedPicker(owner, popup)) return;
        // The mouse guard can precede the common dialog in the owned-window
        // chain. Keep the exact owner/process/class checks for every candidate.
        var window = GetWindow(owner, 0); // GW_HWNDFIRST
        for (var count = 0; window != 0 && count < 512; count++, window = GetWindow(window, 2))
            if (window != popup && TryCloseOwnedPicker(owner, window)) return;
    }

    private static unsafe bool TryCloseOwnedPicker(nint owner, nint popup)
    {
        if (popup == 0 || popup == owner) return false;
        GetWindowThreadProcessId(popup, out var process);
        if (process != (uint)Environment.ProcessId) return false;
        var ancestor = popup;
        for (var depth = 0; depth < 8 && ancestor != owner && ancestor != 0; depth++) ancestor = GetWindow(ancestor, 4); // GW_OWNER
        if (ancestor != owner) return false;
        char* name = stackalloc char[64];
        var length = GetClassNameW(popup, name, 64);
        if (length != 6 || !new ReadOnlySpan<char>(name, length).SequenceEqual("#32770")) return false;
        return PostMessageW(popup, 0x0010, 0, 0) != 0; // WM_CLOSE follows native Cancel.
    }

    // Borrowed HWNDs; this helper never destroys or takes ownership of them.
    [LibraryImport("user32.dll")]
    private static partial nint GetWindow(nint window, uint command);
    [LibraryImport("user32.dll")]
    private static partial uint GetWindowThreadProcessId(nint window, out uint process);
    [LibraryImport("user32.dll", EntryPoint = "GetClassNameW")]
    private static unsafe partial int GetClassNameW(nint window, char* name, int maximum);
    [LibraryImport("user32.dll", EntryPoint = "PostMessageW")]
    private static partial int PostMessageW(nint window, uint message, nuint wParam, nint lParam);
}
