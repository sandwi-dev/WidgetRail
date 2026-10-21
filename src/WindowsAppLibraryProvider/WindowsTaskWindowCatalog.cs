using WidgetRail.PlatformBroker;

namespace WidgetRail.WindowsAppLibraryProvider;

/// <summary>Host-owned window list. Uses the same admission and close policy as the
/// broker provider without initializing game stores or exposing handles to widgets.</summary>
public sealed class WindowsTaskWindowCatalog
{
    private readonly IWindowsRunningAppObserver observer;
    private readonly IWindowsTaskWindowControl control;
    private readonly IShellStaExecutor executor;
    private readonly Func<WindowsRunningAppObservation, NativeWindowPreviewTarget?> resolveTarget;
    private readonly Dictionary<string, WindowsRunningAppObservation> targets = new(StringComparer.Ordinal);

    public WindowsTaskWindowCatalog() : this(new WindowsRunningAppObserver(), new WindowsTaskWindowControl(), ShellStaExecutor.Shared) { }

    internal WindowsTaskWindowCatalog(IWindowsRunningAppObserver observer, IWindowsTaskWindowControl control, IShellStaExecutor executor,
        Func<WindowsRunningAppObservation, NativeWindowPreviewTarget?>? resolveTarget = null)
    { this.observer = observer; this.control = control; this.executor = executor; this.resolveTarget = resolveTarget ?? WindowsRunningAppObserver.PreviewTarget; }

    public Task<IReadOnlyList<TaskWindowSummary>> GetWindowsAsync(CancellationToken token) =>
        executor.RunAsync<IReadOnlyList<TaskWindowSummary>>(cancellation =>
        {
            var next = new Dictionary<string, WindowsRunningAppObservation>(StringComparer.Ordinal);
            var result = new List<TaskWindowSummary>();
            foreach (var item in observer.Observe(cancellation).Where(item => item.Window is not null)
                .DistinctBy(item => item.Window!.Handle).Take(64))
            {
                cancellation.ThrowIfCancellationRequested();
                var target = resolveTarget(item);
                if (target is null) continue;
                var id = item.InstanceEvidence + ":" + item.Window!.Handle.ToString("X");
                var title = Clean(item.Window.Title);
                var name = Clean(item.DisplayName);
                if (name.Length == 0) name = title.Length == 0 ? "Application" : title;
                next.Add(id, item);
                result.Add(new(id, name, title.Length == 0 ? name : title, item.Window.IsMinimized) { PreviewTarget = target });
            }
            targets.Clear();
            foreach (var item in next) targets.Add(item.Key, item.Value);
            return result;
        }, token);

    public Task CloseAsync(string id, CancellationToken token) => executor.RunAsync(cancellation =>
    {
        if (!targets.TryGetValue(id, out var expected)) throw WindowsAppLibraryProvider.WindowUnavailable();
        control.Close(expected, cancellation);
        return true;
    }, token);

    private static string Clean(string value)
    {
        var text = string.Join(' ', new string(value.Where(c => !char.IsControl(c)).ToArray())
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return text[..Math.Min(text.Length, 240)];
    }
}
