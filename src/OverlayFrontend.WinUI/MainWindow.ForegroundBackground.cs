using System.Runtime.InteropServices;
using Microsoft.UI.Xaml;
using WidgetRail.WindowsWindowActivation;
using WidgetRail.Internal;

namespace WidgetRail.OverlayFrontend.WinUI;

public sealed partial class MainWindow
{
    internal event Action<bool>? ForegroundBackgroundChanged;
    internal long BackgroundOpening { get; private set; }
    internal bool ForegroundBackgroundAllowed { get; private set; }
    internal bool BeforeFirstBackgroundFrame { get; private set; }
    internal bool OwnsBackgroundRoot(XamlRoot? root) => root != null && ReferenceEquals(root, ShellRoot.XamlRoot);
    private string? backgroundProcess;
    private nint backgroundHook;
    private BackgroundWindowEvent? backgroundEvent;

    private void BeginForegroundBackground()
    {
        ReleaseBackgroundHook();
        BackgroundOpening++;
        backgroundProcess = foregroundReturnTarget is { } target
            ? CaptureProcessKey.Create(target.ProcessId, target.ProcessCreated) : null;
        // Only the initial paint is time-sensitive. A cold widget may publish
        // its process key later, while the original opening still owns the window.
        BeforeFirstBackgroundFrame = backgroundProcess != null;
        if (BeforeFirstBackgroundFrame) Microsoft.UI.Xaml.Media.CompositionTarget.Rendering += OnFirstBackgroundFrame;
        ForegroundBackgroundChanged?.Invoke(true);
    }

    private void OnFirstBackgroundFrame(object? sender, object args)
    {
        Microsoft.UI.Xaml.Media.CompositionTarget.Rendering -= OnFirstBackgroundFrame;
        BeforeFirstBackgroundFrame = false;
    }

    internal bool MatchesForegroundBackground(string? key)
    {
        if (!ForegroundBackgroundAllowed || key is null || key != backgroundProcess || foregroundReturnTarget is not { } target) return false;
        if (backgroundHook != 0) return true;
        if (!BackgroundWindowVisible(target)) return false;
        backgroundEvent ??= BackgroundWindowChanged;
        // Process-scoped object events include hide, destroy and location changes (minimize).
        backgroundHook = SetWinEventHook(0x8001, 0x800B, 0, backgroundEvent, target.ProcessId, 0, 0);
        if (backgroundHook == 0 || !BackgroundWindowVisible(target))
        {
            ReleaseBackgroundHook();
            return false;
        }
        return true;
    }

    private bool BackgroundWindowVisible(TaskWindowTarget target)
    {
        var window = (nint)(nuint)target.Handle;
        var display = MonitorFromWindow(window, 0);
        return display != 0 && display == MonitorFromWindow(WinRT.Interop.WindowNative.GetWindowHandle(this), 0) &&
            IsWindowVisible(window) != 0 && IsIconic(window) == 0 && new WindowsTaskWindowActivation().IsCurrent(target);
    }

    private void BackgroundWindowChanged(nint hook, uint kind, nint window, int objectId, int child, uint thread, uint time)
    {
        if (hook != backgroundHook || objectId != 0 || child != 0 || foregroundReturnTarget is not { } target ||
            window != (nint)(nuint)target.Handle || kind is not (0x8001 or 0x8003 or 0x800B)) return;
        var opening = BackgroundOpening;
        DispatcherQueue.TryEnqueue(() =>
        {
            if (BackgroundOpening == opening && backgroundHook == hook &&
                (kind is 0x8001 or 0x8003 || !BackgroundWindowVisible(target))) EndForegroundBackground();
        });
    }

    private void EndForegroundBackground()
    {
        backgroundProcess = null;
        ReleaseBackgroundHook();
        ForegroundBackgroundChanged?.Invoke(true);
    }

    private void ReleaseBackgroundHook()
    {
        Microsoft.UI.Xaml.Media.CompositionTarget.Rendering -= OnFirstBackgroundFrame;
        BeforeFirstBackgroundFrame = false;
        if (backgroundHook != 0) { UnhookWinEvent(backgroundHook); backgroundHook = 0; }
    }

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate void BackgroundWindowEvent(nint hook, uint kind, nint window, int objectId, int child, uint thread, uint time);
    [DllImport("user32.dll"), DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern nint SetWinEventHook(uint first, uint last, nint module, BackgroundWindowEvent callback, uint process, uint thread, uint flags);
    [LibraryImport("user32.dll"), DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial int UnhookWinEvent(nint hook);
    [LibraryImport("user32.dll"), DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial nint MonitorFromWindow(nint window, uint flags);
    [LibraryImport("user32.dll"), DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial int IsWindowVisible(nint window);
    [LibraryImport("user32.dll"), DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial int IsIconic(nint window);
}
