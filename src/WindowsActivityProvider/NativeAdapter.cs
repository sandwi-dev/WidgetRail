namespace WidgetRail.WindowsActivityProvider;

internal enum NativeActivityEventKind
{
    Foreground,
    Destroyed,
}

internal readonly record struct NativeActivityEvent(
    NativeActivityEventKind Kind,
    nint Window);

internal sealed record NativeActivityCandidate(
    nint Window,
    uint ProcessId,
    string ProcessKey,
    string DisplayName);

internal interface IWindowsActivityNativeAdapter : IDisposable
{
    IDisposable Start(Action<NativeActivityEvent> handler);
    nint GetCurrentForegroundWindow();
    NativeActivityCandidate? InspectWindow(nint window);
    bool IsWindowAvailable(nint window, uint expectedProcessId);
}
