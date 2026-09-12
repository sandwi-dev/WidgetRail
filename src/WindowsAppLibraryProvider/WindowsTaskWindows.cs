using System.Runtime.InteropServices;
using WidgetRail.PlatformBroker;

namespace WidgetRail.WindowsAppLibraryProvider;

internal interface IWindowsTaskWindowControl
{
    void Switch(WindowsRunningAppObservation expected, CancellationToken cancellationToken);
    void Close(WindowsRunningAppObservation expected, CancellationToken cancellationToken);
}

public sealed partial class WindowsAppLibraryProvider
{
    private Dictionary<string, WindowsRunningAppObservation> _taskWindowTargets = new(StringComparer.Ordinal);
    internal IWindowsTaskWindowControl TaskWindowControl { get; init; } = new WindowsTaskWindowControl();

    public async Task<IReadOnlyList<TaskWindowSummary>> GetTaskWindowsAsync(CancellationToken cancellationToken)
    {
        await GetAppsAsync(cancellationToken).ConfigureAwait(false);
        using var operation = await EnterObservationOperationAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await _shellSta.RunAsync<IReadOnlyList<TaskWindowSummary>>(token =>
            {
                var windows = _runningApps.Observe(token).Where(item => item.Window is not null)
                    .DistinctBy(item => item.Window!.Handle).Take(64).ToArray();
                var priorIds = _taskWindowTargets.ToDictionary(
                    pair => WindowKey(pair.Value), pair => pair.Key, StringComparer.Ordinal);
                GameLibrarySourceItem[] registrations;
                lock (_stateGate) registrations = _registrationsByOpaqueId.Values.ToArray();
                var next = new Dictionary<string, WindowsRunningAppObservation>(StringComparer.Ordinal);
                var summaries = new List<TaskWindowSummary>();
                foreach (var window in windows)
                {
                    token.ThrowIfCancellationRequested();
                    var name = registrations.FirstOrDefault(item =>
                        string.Equals(item.StableIdentity, window.RegistrationIdentity, StringComparison.OrdinalIgnoreCase))?.DisplayName;
                    name = SanitizeDisplayName(name ?? window.DisplayName) ?? "Application";
                    var title = CleanWindowTitle(window.Window!.Title, name);
                    var id = priorIds.GetValueOrDefault(WindowKey(window)) ?? "target-" + Guid.NewGuid().ToString("N");
                    next.Add(id, window);
                    summaries.Add(new(id, name, title, window.Window.IsMinimized)
                    {
                        PreviewTarget = WindowsRunningAppObserver.PreviewTarget(window),
                    });
                }
                _taskWindowTargets = next;
                return summaries;
            }, operation.Token).ConfigureAwait(false);
        }
        finally { operation.Release(_observationGate); }
    }

    public Task SwitchTaskWindowAsync(string windowId, CancellationToken cancellationToken) =>
        ControlTaskWindowAsync(windowId, close: false, cancellationToken);
    public Task CloseTaskWindowAsync(string windowId, CancellationToken cancellationToken) =>
        ControlTaskWindowAsync(windowId, close: true, cancellationToken);

    private async Task ControlTaskWindowAsync(string id, bool close, CancellationToken cancellationToken)
    {
        using var operation = await EnterObservationOperationAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await _shellSta.RunAsync(token =>
            {
                if (!_taskWindowTargets.TryGetValue(id, out var expected))
                    throw WindowUnavailable();
                var live = _runningApps.Observe(token).FirstOrDefault(item => SameWindow(expected, item));
                if (live is null) throw WindowUnavailable();
                token.ThrowIfCancellationRequested();
                if (close) TaskWindowControl.Close(live, token);
                else TaskWindowControl.Switch(live, token);
                return true;
            }, operation.Token).ConfigureAwait(false);
        }
        finally { operation.Release(_observationGate); }
    }

    private static string WindowKey(WindowsRunningAppObservation window) =>
        window.InstanceEvidence + ":" + window.Window!.Handle.ToString("X");

    internal static bool SameWindow(WindowsRunningAppObservation expected, WindowsRunningAppObservation current) =>
        expected.Window is { } a && current.Window is { } b &&
        a.Handle == b.Handle && a.ProcessId == b.ProcessId && a.ClassName == b.ClassName &&
        expected.InstanceEvidence == current.InstanceEvidence &&
        expected.RegistrationIdentity == current.RegistrationIdentity;

    internal static BrokerException WindowUnavailable() =>
        new("window_unavailable", "This window is no longer available.");

    private static string CleanWindowTitle(string title, string fallback)
    {
        var result = string.Join(' ', new string(title.Where(character => !char.IsControl(character)).ToArray())
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return result.Length == 0 ? fallback : result[..Math.Min(result.Length, 240)];
    }
}

internal sealed class WindowsTaskWindowControl : IWindowsTaskWindowControl
{
    public void Switch(WindowsRunningAppObservation expected, CancellationToken cancellationToken)
    {
        var handle = Revalidate(expected, cancellationToken);
        if (IsIconic(handle) && !ShowWindowAsync(handle, 9))
            throw new BrokerException("window_switch_denied", "Windows could not restore this window.");
        cancellationToken.ThrowIfCancellationRequested();
        _ = SetForegroundWindow(handle);
        // Restoration can be asynchronous; this only observes the one switch request.
        for (var attempt = 0; attempt < 25; ++attempt)
        {
            if (GetForegroundWindow() == handle) return;
            cancellationToken.ThrowIfCancellationRequested();
            Thread.Sleep(10);
        }
        throw new BrokerException("window_switch_denied", "Windows did not allow this window to become active.");
    }

    public void Close(WindowsRunningAppObservation expected, CancellationToken cancellationToken)
    {
        var handle = Revalidate(expected, cancellationToken);
        // A posted WM_CLOSE lets the app show save prompts and never waits on its UI thread.
        if (!PostMessageW(handle, 0x0010, 0, 0))
            throw new BrokerException("window_close_denied", "Windows did not allow this window to close.");
    }

    private static nint Revalidate(WindowsRunningAppObservation expected, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var current = WindowsRunningAppObserver.InspectWindow(expected.Window!.Handle);
        if (current is null || !WindowsAppLibraryProvider.SameWindow(expected, current))
            throw WindowsAppLibraryProvider.WindowUnavailable();
        return current.Window!.Handle;
    }

    [DllImport("user32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsIconic(nint window);
    [DllImport("user32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindowAsync(nint window, int command);
    [DllImport("user32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(nint window);
    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern nint GetForegroundWindow();
    [DllImport("user32.dll", ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostMessageW(nint window, uint message, nuint wParam, nint lParam);
}
