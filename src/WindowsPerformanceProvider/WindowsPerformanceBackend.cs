using WidgetRail.PlatformBroker;

namespace WidgetRail.WindowsPerformanceProvider;

public sealed class WindowsPerformanceBackend(IAppLibraryPlatformBrokerBackend applications, string collectorExecutable, Action<string>? diagnostic = null)
    : IPerformancePlatformBackend, IAsyncDisposable
{
    private readonly WindowsSystemSampler _system = new();
    private readonly FpsCollectorClient _fps = new(collectorExecutable, diagnostic);
    private readonly SemaphoreSlim _gate = new(1, 1);
    private IReadOnlyList<TaskWindowSummary> _windows = [];
    private long _windowsAt = long.MinValue / 2;

    public async Task<PerformanceSnapshot> GetPerformanceAsync(string? targetId, CancellationToken token)
    {
        await _gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            if (Environment.TickCount64 - _windowsAt >= 1000)
            {
                _windows = await applications.GetTaskWindowsAsync(token).ConfigureAwait(false);
                _windowsAt = Environment.TickCount64;
            }
            var system = await Task.Run(_system.Read, token).ConfigureAwait(false);
            var selected = targetId is null ? _windows.FirstOrDefault() : _windows.FirstOrDefault(w => w.WindowId == targetId);
            var collection = _fps.Read(selected is not null);
            var frames = selected?.PreviewTarget is { } target
                ? collection.Processes.FirstOrDefault(p => p.ProcessId == target.ProcessId) : null;
            var rate = frames?.FramesPerSecond;
            var state = collection.State == "collecting" && selected is null ? "no-target" :
                collection.State == "collecting" && rate is null ? "no-frames" : collection.State;
            return new(system.SampledAtUtc, system.CpuPercent, system.GpuPercent, system.MemoryUsedBytes, system.MemoryTotalBytes,
                system.DedicatedGpuMemoryBytes, collection.State == "collecting" ? rate : null, state, selected?.WindowId,
                _windows.Select(w => new PerformanceTarget(w.WindowId, w.ApplicationName, w.Title)).ToArray())
                { FrameStatistics = state == "collecting" ? frames?.FrameStatistics : null };
        }
        finally { _gate.Release(); }
    }

    public Task<PerformanceEnableResult> EnableFpsAsync(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        return Task.FromResult(new PerformanceEnableResult(_fps.Enable()));
    }

    public Task<PerformanceEnableResult> DisableFpsAsync(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        return Task.FromResult(new PerformanceEnableResult(_fps.Disable()));
    }

    public async ValueTask DisposeAsync()
    {
        await _fps.DisposeAsync().ConfigureAwait(false);
        await _gate.WaitAsync().ConfigureAwait(false);
        try { _system.Dispose(); }
        finally { _gate.Release(); }
    }
}
