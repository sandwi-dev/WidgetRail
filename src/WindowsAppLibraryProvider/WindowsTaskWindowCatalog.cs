using WidgetRail.PlatformBroker;
using System.Diagnostics;

namespace WidgetRail.WindowsAppLibraryProvider;

public sealed record TaskWindowAppearance(string Identity, string ApplicationName, string? PngBase64);

/// <summary>Host-owned window list. Uses the same admission and close policy as the
/// broker provider without initializing game stores or exposing handles to widgets.</summary>
public sealed class WindowsTaskWindowCatalog
{
    private readonly IWindowsRunningAppObserver observer;
    private readonly IWindowsTaskWindowControl control;
    private readonly IShellStaExecutor executor;
    private readonly Func<WindowsRunningAppObservation, NativeWindowPreviewTarget?> resolveTarget;
    private readonly Dictionary<string, WindowsRunningAppObservation> targets = new(StringComparer.Ordinal);
    private readonly Dictionary<string, TaskWindowAppearance> appearances = new(StringComparer.Ordinal);
    private readonly WindowsAppIconSource icons = new();

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

    public Task<TaskWindowAppearance?> GetAppearanceAsync(string id, CancellationToken token) => executor.RunAsync<TaskWindowAppearance?>(cancellation =>
    {
        if (!targets.TryGetValue(id, out var item) || resolveTarget(item) is not { } target) return null;
        if (appearances.TryGetValue(item.InstanceEvidence, out var cached)) return cached;
        var name = Clean(item.DisplayName);
        string? png = null;
        try
        {
            var path = item.PortableAuthority?.CanonicalPath;
            if (path is null)
            {
                using var process = Process.GetProcessById(checked((int)target.ProcessId));
                path = process.MainModule?.FileName;
            }
            if (path is not null)
            {
                var metadata = FileVersionInfo.GetVersionInfo(path);
                var friendly = Clean(metadata.FileDescription ?? metadata.ProductName ?? "");
                if (friendly.Length > 0) name = friendly;
                png = icons.TryRasterizePngBase64(path, cancellation);
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception or InvalidOperationException or ArgumentException) { }
        cancellation.ThrowIfCancellationRequested();
        var appearance = new TaskWindowAppearance(item.InstanceEvidence, name, png);
        if (appearances.Count >= 128) appearances.Remove(appearances.Keys.First());
        appearances[item.InstanceEvidence] = appearance;
        return appearance;
    }, token);

    private static string Clean(string value)
    {
        var text = string.Join(' ', new string(value.Where(c => !char.IsControl(c)).ToArray())
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return text[..Math.Min(text.Length, 240)];
    }
}
