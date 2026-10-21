using System.Globalization;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

namespace WidgetRail.FirstPartyWidgets.PerformanceMonitor;

public sealed partial class PerformanceMonitorWidget : Widget
{
    private readonly object _gate = new();
    private readonly SemaphoreSlim _refresh = new(1, 1);
    private readonly PinnedLayoutHandle _compact;
    private readonly PerformanceHistory _history = new();
    private WidgetPerformanceSnapshot? _snapshot;
    // Picker identity outlives a reading: activation and transient errors clear
    // metrics, but must not remove the user's currently selected option.
    private IReadOnlyList<WidgetPerformanceTarget> _targets = [];
    private Task _loop = Task.CompletedTask;
    private string? _selection, _error;
    private bool _busy;
    private long _generation;
    private long _revision;

    public PerformanceMonitorWidget()
    {
        _compact = CreatePinnedLayoutHandle("performance.compact", "Compact performance", new()
        {
            Mode = WidgetSurfaceMode.Standard, WidthMode = WidgetSurfaceAxisMode.Preferred, HeightMode = WidgetSurfaceAxisMode.Preferred,
            PreferredWidth = 320, PreferredHeight = 210, MinimumWidth = 240, MinimumHeight = 180,
        }, activeInputScopeId: "performance.pin");
    }

    protected override ValueTask OnActivatedAsync(CancellationToken activeLifetime)
    {
        long generation;
        lock (_gate) { generation = ++_generation; _snapshot = null; _error = null; _busy = false; }
        _loop = PollAsync(generation, activeLifetime);
        return ValueTask.CompletedTask;
    }

    protected override async ValueTask OnDeactivatedAsync(CancellationToken transitionToken)
    {
        lock (_gate) { ++_generation; _busy = false; }
        await _loop.ConfigureAwait(false);
    }

    private async Task PollAsync(long generation, CancellationToken token)
    {
        try
        {
            await Task.Yield();
            while (true)
            {
                await RefreshAsync(generation, token).ConfigureAwait(false);
                await Task.Delay(1000, token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
    }

    private async Task RefreshAsync(long generation, CancellationToken token, bool allowBusy = false)
    {
        await _refresh.WaitAsync(token).ConfigureAwait(false);
        long revision = -1;
        try
        {
            string? selection;
            lock (_gate)
            {
                if (_busy && !allowBusy) return;
                selection = _selection; revision = _revision;
            }
            var snapshot = await HostServices.Performance.GetAsync(selection, token).ConfigureAwait(false);
            lock (_gate)
            {
                if (generation != _generation || revision != _revision || selection != _selection) return;
                _snapshot = snapshot; _targets = snapshot.Targets; _error = null;
                _history.Observe(snapshot, DateTimeOffset.UtcNow);
                if (_selection is not null && !snapshot.Targets.Any(t => t.Id == _selection)) _selection = null;
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            lock (_gate) if (generation == _generation && revision == _revision)
            {
                var code = (error as WidgetCapabilityException)?.ErrorCode;
                if (code == "performance_target_unavailable") _selection = null;
                _snapshot = null;
                _error = code is "permission_denied" or "consent_required" or "capability_not_granted"
                    ? "Select Refresh to approve performance monitoring here."
                    : "Performance readings are unavailable. Check permissions or try again.";
            }
        }
        finally { _refresh.Release(); Invalidate(); }
    }

    public override ValueTask OnActionAsync(WidgetActionEvent action, CancellationToken cancellationToken = default) =>
        WidgetRail.Widgets.WidgetPermissionAction.RunAsync(HostServices.Permissions, Operations, ActionPermissions(action),
            token => OnAuthorizedActionAsync(action, token), PermissionFeedback, cancellationToken);

    private async ValueTask OnAuthorizedActionAsync(WidgetActionEvent action, CancellationToken cancellationToken = default)
    {
        long generation;
        bool fpsEnabled;
        lock (_gate)
        {
            if (_busy) return;
            generation = _generation;
            fpsEnabled = Enabled(_snapshot?.FpsState);
            if (action.ActionId == "performance.auto") { _selection = null; _history.ResetTarget(); }
            else if (action.ActionId.StartsWith("performance.target.", StringComparison.Ordinal))
            {
                var id = action.ActionId["performance.target.".Length..];
                if (!_targets.Any(t => t.Id == id)) return;
                _selection = id;
                _history.ResetTarget();
            }
            else if (action.ActionId is not ("performance.refresh" or "performance.fps")) return;
            ++_revision;
            _busy = true;
        }
        Invalidate();
        try
        {
            if (action.ActionId == "performance.fps")
            {
                var result = fpsEnabled
                    ? await HostServices.Performance.DisableFpsAsync(cancellationToken).ConfigureAwait(false)
                    : await HostServices.Performance.EnableFpsAsync(cancellationToken).ConfigureAwait(false);
                lock (_gate) if (generation == _generation && _snapshot is { } current)
                    _snapshot = current with { FpsState = result.State, FramesPerSecond = null };
            }
            else await RefreshAsync(generation, cancellationToken, allowBusy: true).ConfigureAwait(false);
        }
        catch (WidgetCapabilityException error)
        {
            lock (_gate) if (generation == _generation)
                _error = error.ErrorCode == "permission_denied"
                    ? "Select Enable FPS again to request access."
                    : "The FPS request could not complete. Check its status and try again.";
        }
        finally
        {
            lock (_gate) if (generation == _generation) _busy = false;
            Invalidate();
        }
    }

    public override WidgetView Render()
    {
        WidgetPerformanceSnapshot? snapshot; string? selection, error; bool busy;
        IReadOnlyList<WidgetPerformanceTarget> targets;
        LineGraphDefinition fpsGraph, frameGraph, cpuGraph, gpuGraph, ramGraph, vramGraph;
        lock (_gate)
        {
            snapshot = _snapshot; selection = _selection; error = _error; busy = _busy;
            targets = _targets;
            var now = DateTimeOffset.UtcNow;
            fpsGraph = _history.Graph(s => s.FramesPerSecond, now, "FPS", application: true);
            frameGraph = _history.Graph(s => s.FrameStatistics?.RecentWorstFrameMilliseconds is > 0 ? s.FrameStatistics.RecentWorstFrameMilliseconds : null,
                now, "ms", application: true);
            cpuGraph = _history.Graph(s => s.CpuPercent, now, "%", 100);
            gpuGraph = _history.Graph(s => s.GpuPercent, now, "%", 100);
            ramGraph = _history.Graph(s => s.MemoryUsedBytes / 1073741824d, now, "GiB", snapshot?.MemoryTotalBytes / 1073741824d);
            vramGraph = _history.Graph(s => s.DedicatedGpuMemoryBytes / 1073741824d, now, "GiB");
        }
        var statistics = snapshot?.FrameStatistics;
        var target = snapshot?.Targets.FirstOrDefault(t => t.Id == snapshot.SelectedTargetId);
        var choices = new List<SelectOption> { new("auto", "Automatic · top application", "performance.auto", IsSelected: selection is null) };
        choices.AddRange(targets.Select(t =>
            new SelectOption(t.Id, t.ApplicationName + " · " + t.Title, "performance.target." + t.Id, IsSelected: selection == t.Id)));
        var items = new List<WidgetElement>
        {
            UI.Text("Performance Monitor", "performance.heading").Classes("performance-title"),
            UI.Select("Application", choices, "performance.target").Busy(busy),
            UI.Button(Enabled(snapshot?.FpsState) ? "Stop FPS" : "Enable FPS", "performance.fps", "performance.fps.control")
                .Busy(busy || snapshot?.FpsState == "enabling"),
            UI.Text(target?.ApplicationName ?? "No application selected", "performance.app").Classes("performance-subtitle"),
            UI.ResponsiveGrid("performance.frame-metrics", 170, 3,
                Metric("FPS", Number(snapshot?.FramesPerSecond), "performance.fps.metric", "Application presents per second"),
                Metric("Average FPS", Number(statistics?.AverageFramesPerSecond), "performance.average", "Up to 60s"),
                Metric("1% low", statistics?.OnePercentLowFramesPerSecond is { } low ? Number(low) : statistics is null ? "—" : "Collecting…", "performance.low",
                    "Slowest 1% · 1,000+ frames")),
            UI.Text(statistics is null ? "Frame history is unavailable until FPS collection starts." :
                $"{statistics.SampleCount:N0} frames · {statistics.WindowSeconds:0}s available · mean frame {statistics.AverageFrameMilliseconds:0.0} ms",
                "performance.frame-window").Classes("performance-help"),
            UI.ResponsiveGrid("performance.frame-graphs", 260, 2,
                Chart("FPS · last 60 seconds", fpsGraph, "performance.fps.graph"),
                Chart("Worst frame per sample · last 60 seconds", frameGraph, "performance.frame.graph")),
            UI.Text("System usage · last 60 seconds", "performance.system.heading").Classes("performance-subtitle"),
            UI.ResponsiveGrid("performance.metrics", 220, 2,
                Metric("CPU", Percent(snapshot?.CpuPercent), "performance.cpu", "System usage"),
                Metric("GPU", Percent(snapshot?.GpuPercent), "performance.gpu", "Busiest GPU engine"),
                Metric("RAM", Bytes(snapshot?.MemoryUsedBytes) + (snapshot?.MemoryTotalBytes is { } total ? " / " + Bytes(total) : ""), "performance.ram", "System memory used"),
                Metric("VRAM", Bytes(snapshot?.DedicatedGpuMemoryBytes), "performance.vram", "Dedicated GPU memory used")),
            UI.ResponsiveGrid("performance.system-graphs", 260, 2,
                Chart("CPU", cpuGraph, "performance.cpu.graph"), Chart("GPU", gpuGraph, "performance.gpu.graph"),
                Chart("RAM", ramGraph, "performance.ram.graph"), Chart("VRAM", vramGraph, "performance.vram.graph")),
        };
        if ((error ?? Status(snapshot?.FpsState)) is { } status)
            items.Add(UI.Text(status, "performance.status").Classes("performance-help"));
        items.Add(UI.Button("Refresh", "performance.refresh", "performance.refresh").Busy(busy));
        var root = UI.VerticalScroll("performance.page", items.ToArray()).InputScope("performance.page")
            .Shortcut(ControllerButton.Y, "performance.refresh", label: "Refresh").Classes("performance-widget");
        var pin = UI.Stack("performance.pin",
            UI.Text(target?.ApplicationName ?? "Performance", "performance.pin.name").Classes("performance-subtitle"),
            UI.Text(Number(snapshot?.FramesPerSecond) + " FPS", "performance.pin.fps").Classes("performance-value"),
            UI.Text("1% low " + Number(statistics?.OnePercentLowFramesPerSecond), "performance.pin.low"),
            UI.Text("CPU " + Percent(snapshot?.CpuPercent) + "    GPU " + Percent(snapshot?.GpuPercent), "performance.pin.usage"),
            UI.Text("RAM " + Bytes(snapshot?.MemoryUsedBytes) + "    VRAM " + Bytes(snapshot?.DedicatedGpuMemoryBytes), "performance.pin.memory"))
            .InputScope("performance.pin").Shortcut(ControllerButton.Y, "performance.refresh", label: "Refresh").Classes("performance-widget", "performance-pin");
        return new(root, "performance.target", ActiveInputScopeId: "performance.page", Surface: new()
        {
            Mode = WidgetSurfaceMode.Standard, WidthMode = WidgetSurfaceAxisMode.Preferred, HeightMode = WidgetSurfaceAxisMode.Preferred,
            PreferredWidth = 720, PreferredHeight = 660, MinimumWidth = 380, MinimumHeight = 380,
        }) { PinnedLayouts = [_compact.Present(pin)] };
    }

    private static WidgetElement Chart(string title, LineGraphDefinition graph, string id) => UI.Stack(id + ".card",
        UI.Text(title, id + ".title").Classes("performance-subtitle"), UI.LineGraph(title, graph, id)).Classes("performance-metric");

    private static StackElement Metric(string title, string value, string id, string description) => UI.Stack(id,
        UI.Text(title, id + ".title").Classes("performance-subtitle"),
        UI.Text(value, id + ".value").Classes("performance-value"),
        UI.Text(description, id + ".description").Classes("performance-help")).Classes("performance-metric");
    private static string Number(double? value) => value?.ToString("0", CultureInfo.CurrentCulture) ?? "—";
    private static string Percent(double? value) => value is null ? "—" : value.Value.ToString("0", CultureInfo.CurrentCulture) + "%";
    private static string Bytes(long? value) => value is null ? "—" : (value.Value / 1073741824d).ToString("0.0", CultureInfo.CurrentCulture) + " GiB";
    private static bool Enabled(string? state) => state is "collecting" or "idle" or "warming" or "no-frames" or "no-target" or "enabling";
    private static string? Status(string? state) => state switch
    {
        null => "Reading performance…",
        "enabling" => "Approve the Windows prompt to enable FPS collection.",
        "declined" => "Windows approval was cancelled. Other metrics remain available.",
        "not-enabled" => "Enable FPS requests Windows approval for frame-rate collection. Collection pauses when hidden unless pinned.",
        "missing" => "The FPS collector is missing or failed its integrity check. Repair WidgetRail to restore it.",
        "permission-required" => "Windows did not allow frame-rate collection. Select Enable FPS to try again.",
        "failed" or "unavailable" => "FPS collection is unavailable. Other metrics remain available.",
        "no-target" => "Open a game or choose an application to monitor.",
        "no-frames" => "Waiting for frames. The application may be idle, minimized or unsupported.",
        "warming" or "idle" => "Warming up FPS collection…",
        "collecting" => null,
        _ => "Reading performance…",
    };
}
