using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using WidgetRail.PlatformBroker;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetRuntime;

namespace WidgetRail.WidgetBridge;

/// <summary>
/// Records bounded developer-only Bridge diagnostics through one serialized owner.
/// Provider responses, credentials, package-private paths, and widget-controlled
/// text never enter this persistent lane. User-facing Bridge failure copy never
/// reads from it.
/// </summary>
internal sealed class MediaSessionsDiagnosticLog : IAsyncDisposable
{
    internal const int MaximumTrackedTransitions = 256;
    internal const int MaximumPendingLines = 32;

    private readonly object _gate = new();
    private readonly string _path;
    private readonly long _bridgeSessionGeneration;
    private readonly Dictionary<string, TrackedTransition> _transitions =
        new(StringComparer.Ordinal);
    private readonly LinkedList<string> _recency = [];
    private readonly Channel<string> _lines = Channel.CreateBounded<string>(
        new BoundedChannelOptions(MaximumPendingLines)
        {
            SingleReader = true,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.DropWrite,
            AllowSynchronousContinuations = false,
        });
    private readonly Task _writer;
    private int _disposed;

    internal MediaSessionsDiagnosticLog(string path, long bridgeSessionGeneration = 1)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (bridgeSessionGeneration <= 0)
            throw new ArgumentOutOfRangeException(nameof(bridgeSessionGeneration));
        _path = Path.GetFullPath(path);
        _bridgeSessionGeneration = bridgeSessionGeneration;
        _writer = WriteAsync();
    }

    internal void RecordBridgeSessionStarted(int processId)
    {
        if (Volatile.Read(ref _disposed) != 0 || processId <= 0) return;
        _lines.Writer.TryWrite(
            $"{DateTimeOffset.UtcNow:O} Widget lifetime " +
            $"bridge-session={_bridgeSessionGeneration} bridge-pid={processId} " +
            $"event=bridge-session-started{Environment.NewLine}");
    }

    internal void RecordLifetime(BridgeClientLifetimeDiagnostic diagnostic)
    {
        if (Volatile.Read(ref _disposed) != 0 ||
            !IsSafe(diagnostic.WidgetId, 128) ||
            diagnostic.RegistryGeneration <= 0)
            return;
        var process = diagnostic.Process;
        var eventCode = process.FailureCode == "registry-retirement"
            ? "registry-retirement"
            : process.Kind switch
        {
            WidgetProcessLifetimeEventKind.WorkerStarted => "worker-started",
            WidgetProcessLifetimeEventKind.LifecycleRequested => "lifecycle-requested",
            WidgetProcessLifetimeEventKind.LifecycleCompleted => "lifecycle-completed",
            WidgetProcessLifetimeEventKind.LifecycleFailed => "lifecycle-failed",
            WidgetProcessLifetimeEventKind.CooperativeUnloadRequested =>
                "cooperative-unload-requested",
            WidgetProcessLifetimeEventKind.CooperativeUnloadCompleted =>
                "cooperative-unload-completed",
            WidgetProcessLifetimeEventKind.ProcessExited => "worker-exited",
            _ => "unknown",
        };
        var residency = diagnostic.ResidencyMode switch
        {
            WidgetResidencyMode.KeepAlive => "keep-alive",
            WidgetResidencyMode.SuspendWhenHidden => "suspend",
            WidgetResidencyMode.UnloadAfterIdle => "unload-after-idle",
            _ => "unknown",
        };
        var lifecycle = process.LifecycleState?.ToString().ToLowerInvariant() ?? "none";
        var failure = process.FailureCode is { } code && IsSafe(code, 64)
            ? code
            : "none";
        _lines.Writer.TryWrite(
            $"{DateTimeOffset.UtcNow:O} Widget lifetime " +
            $"bridge-session={_bridgeSessionGeneration} widget={diagnostic.WidgetId} " +
            $"registry-generation={diagnostic.RegistryGeneration} residency={residency} " +
            $"event={eventCode} start={process.StartOrdinal} " +
            $"pid={process.ProcessId?.ToString() ?? "none"} lifecycle={lifecycle} " +
            $"exit={process.ExitCode?.ToString() ?? "none"} failure={failure}" +
            Environment.NewLine);
    }

    internal void RecordRequestFailure(BridgeWidgetRequestDiagnostic diagnostic)
    {
        if (Volatile.Read(ref _disposed) != 0 ||
            !IsSafe(diagnostic.WidgetId, 128) ||
            !IsSafe(diagnostic.RequestType, 64) ||
            !IsSafe(diagnostic.WorkerErrorCode, 64))
            return;

        var structuralDiagnostic = BridgeWidgetRequestDiagnostic.NormalizeStructuralDiagnostic(
            diagnostic.WorkerErrorCode, diagnostic.StructuralDiagnostic);
        var detail = structuralDiagnostic is { } value
            ? $" detail={JsonSerializer.Serialize(value)}"
            : string.Empty;
        _lines.Writer.TryWrite(
            $"{DateTimeOffset.UtcNow:O} Widget request diagnostic " +
            $"bridge-session={_bridgeSessionGeneration} widget={diagnostic.WidgetId} " +
            $"request={diagnostic.RequestType} worker-code={diagnostic.WorkerErrorCode}" +
            detail +
            Environment.NewLine);
    }

    internal void Record(string widgetId, BrokerCapabilityDiagnostic diagnostic)
    {
        Record(
            widgetId,
            diagnostic.CapabilityId,
            diagnostic.OperationId,
            diagnostic.Stage,
            diagnostic.Code);
    }

    internal void Record(
        string widgetId,
        string capabilityId,
        string operationId,
        string diagnosticStage,
        string? code)
    {
        if (Volatile.Read(ref _disposed) != 0 ||
            capabilityId != PlatformCapabilities.MediaSessionsReadV1)
            return;
        if (!TryMapStage(operationId, diagnosticStage, out var stage)) return;

        string? line = null;
        lock (_gate)
        {
            if (code is null)
            {
                RemoveLocked(Key(widgetId, stage));
                if (stage == "subscription-open")
                    RemoveLocked(Key(widgetId, "subscription-read"));
                return;
            }
            if (!IsSafe(widgetId, 128) || !IsSafe(code, 64)) return;
            var key = Key(widgetId, stage);
            if (_transitions.TryGetValue(key, out var current) &&
                string.Equals(current.Code, code, StringComparison.Ordinal))
            {
                TouchLocked(key, current);
                return;
            }
            RemoveLocked(key);
            while (_transitions.Count >= MaximumTrackedTransitions)
                RemoveLocked(_recency.First!.Value);
            var node = _recency.AddLast(key);
            _transitions.Add(key, new TrackedTransition(code, node));
            line = $"{DateTimeOffset.UtcNow:O} Media Sessions diagnostic " +
                $"widget={widgetId} stage={stage} code={code}{Environment.NewLine}";
        }
        _lines.Writer.TryWrite(line!);
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _lines.Writer.TryComplete();
        try { await _writer.WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false); }
        catch (TimeoutException) { }
    }

    private async Task WriteAsync()
    {
        await foreach (var line in _lines.Reader.ReadAllAsync().ConfigureAwait(false))
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
                await File.AppendAllTextAsync(_path, line, Encoding.UTF8).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is IOException or
                UnauthorizedAccessException or NotSupportedException) { }
        }
    }

    private static bool TryMapStage(
        string operationId,
        string diagnosticStage,
        out string stage)
    {
        stage = diagnosticStage switch
        {
            "request" when operationId == PlatformCapabilities.MediaSessionsGet =>
                "snapshot-read",
            "subscription-open" when
                operationId == PlatformCapabilities.MediaSessionsChanged =>
                "subscription-open",
            "subscription-read" when
                operationId == PlatformCapabilities.MediaSessionsChanged =>
                "subscription-read",
            _ => string.Empty,
        };
        return stage.Length != 0;
    }

    private static bool IsSafe(string value, int maximumLength) =>
        value.Length is > 0 && value.Length <= maximumLength &&
        value.All(character => char.IsAsciiLetterOrDigit(character) ||
            character is '.' or '-' or '_');

    private static string Key(string widgetId, string stage) => widgetId + "\0" + stage;

    private void TouchLocked(string key, TrackedTransition transition)
    {
        _recency.Remove(transition.Node);
        _recency.AddLast(transition.Node);
        _transitions[key] = transition;
    }

    private void RemoveLocked(string key)
    {
        if (!_transitions.Remove(key, out var existing)) return;
        _recency.Remove(existing.Node);
    }

    private sealed record TrackedTransition(string Code, LinkedListNode<string> Node);
}
