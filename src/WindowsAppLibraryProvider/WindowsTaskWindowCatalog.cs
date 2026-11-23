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

    // Validate a remembered foreground window using the same eligibility rules as the task list.
    // A one-window screenshot should neither enumerate other apps nor replace the task switcher's targets.
    public Task<string?> GetCaptureApplicationNameAsync(NativeWindowPreviewTarget target, CancellationToken token) =>
        executor.RunAsync<string?>(cancellation =>
        {
            cancellation.ThrowIfCancellationRequested();
            if (!ulong.TryParse(target.Handle, System.Globalization.NumberStyles.AllowHexSpecifier,
                    System.Globalization.CultureInfo.InvariantCulture, out var handle) || handle == 0) return null;
            var app = WindowsRunningAppObserver.InspectWindow((nint)(nuint)handle);
            if (app?.Window is not { IsMinimized: false } window || resolveTarget(app) != target) return null;
            var name = Clean(app.DisplayName);
            return name.Length > 0 ? name : Clean(window.Title) is { Length: > 0 } title ? title : "Application";
        }, token);

    public Task<IReadOnlyList<TaskWindowSummary>> GetWindowsAsync(CancellationToken token, bool requireComplete = false) =>
        executor.RunAsync<IReadOnlyList<TaskWindowSummary>>(cancellation =>
        {
            var next = requireComplete ? null : new Dictionary<string, WindowsRunningAppObservation>(StringComparer.Ordinal);
            var result = new List<TaskWindowSummary>();
            var observed = requireComplete ? observer.ObserveComplete(cancellation) : observer.Observe(cancellation);
            var distinct = observed.Where(item => item.Window is not null).DistinctBy(item => item.Window!.Handle);
            foreach (var item in requireComplete ? distinct : distinct.Take(64))
            {
                cancellation.ThrowIfCancellationRequested();
                var target = resolveTarget(item);
                if (target is null) continue;
                var id = item.InstanceEvidence + ":" + item.Window!.Handle.ToString("X");
                var title = Clean(item.Window.Title);
                var name = Clean(item.DisplayName);
                if (name.Length == 0) name = title.Length == 0 ? "Application" : title;
                next?.Add(id, item);
                result.Add(new(id, name, title.Length == 0 ? name : title, item.Window.IsMinimized)
                {
                    PreviewTarget = target,
                    ApplicationIdentity = string.IsNullOrWhiteSpace(item.RegistrationIdentity) ? null :
                        Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(item.RegistrationIdentity))),
                });
            }
            if (next is null) return result; // A recorder scan never changes task-switcher authority.
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
