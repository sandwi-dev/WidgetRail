using System.Text;
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
    internal const long MaximumFileBytes = 4L * 1024L * 1024L;
    internal const int RetainedGenerationCount = 2;
    private const int CrossProcessWaitMilliseconds = 50;
    private const string CrossProcessMutexName =
        @"Local\WidgetRail.OverlayDiagnosticLog.v1";

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

    internal void RecordBluetoothPairing(WidgetRail.WindowsBluetoothProvider.BluetoothPairingDiagnostic diagnostic)
    {
        if (Volatile.Read(ref _disposed) != 0 || !Enum.IsDefined(diagnostic.Stage) || !Enum.IsDefined(diagnostic.Outcome)) return;
        _lines.Writer.TryWrite(
            $"{DateTimeOffset.UtcNow:O} Bluetooth pairing bridge-session={_bridgeSessionGeneration} " +
            $"stage={diagnostic.Stage} outcome={diagnostic.Outcome} " +
            $"windows-status={diagnostic.WindowsStatus?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "none"} " +
            $"hresult={(diagnostic.HResult is { } code ? $"0x{code:X8}" : "none")} " +
            $"elapsed-ms={Math.Clamp(diagnostic.ElapsedMilliseconds, 0, 300_000)}{Environment.NewLine}");
    }

    internal void RecordBridgeSessionStarted(int processId)
    {
        if (Volatile.Read(ref _disposed) != 0 || processId <= 0) return;
        _lines.Writer.TryWrite(
            $"{DateTimeOffset.UtcNow:O} Widget lifetime " +
            $"bridge-session={_bridgeSessionGeneration} bridge-pid={processId} " +
            $"event=bridge-session-started{Environment.NewLine}");
    }

    internal void RecordBridgeStartupPhase(string stage, long elapsedMilliseconds = 0)
    {
        if (Volatile.Read(ref _disposed) != 0 || stage is not (
                "trusted-catalog-ready" or "control-plane-created" or
                "installed-catalog-pending"))
            return;
        _lines.Writer.TryWrite(
            $"{DateTimeOffset.UtcNow:O} Bridge startup " +
            $"bridge-session={_bridgeSessionGeneration} stage={stage} " +
            $"elapsed-ms={Math.Clamp(elapsedMilliseconds, 0, 300_000)}" +
            Environment.NewLine);
    }

    internal void RecordInstalledCatalogLoad(BridgeInstalledCatalogObservation observation)
    {
        if (Volatile.Read(ref _disposed) != 0) return;
        _lines.Writer.TryWrite(
            $"{DateTimeOffset.UtcNow:O} Bridge startup " +
            $"bridge-session={_bridgeSessionGeneration} stage=installed-catalog-terminal " +
            $"result={(observation.Succeeded ? "validated" : "rejected")} " +
            $"packages={Math.Clamp(observation.PackageCount, 0, 256)} " +
            $"versions={Math.Clamp(observation.VersionCount, 0, 512)} " +
            $"files={Math.Clamp(observation.FileCount, 0, 32_768)} " +
            $"bytes={Math.Clamp(observation.ByteCount, 0, 2L * 1024L * 1024L * 1024L)} " +
            $"elapsed-ms={Math.Clamp(observation.ElapsedMilliseconds, 0, 300_000)}" +
            Environment.NewLine);
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

        _lines.Writer.TryWrite(
            $"{DateTimeOffset.UtcNow:O} Widget request diagnostic " +
            $"bridge-session={_bridgeSessionGeneration} widget={diagnostic.WidgetId} " +
            $"request={diagnostic.RequestType} worker-code={diagnostic.WorkerErrorCode}" +
            (diagnostic.BridgeRequestId > 0
                ? $" bridge-request={diagnostic.BridgeRequestId}"
                : string.Empty) +
            (diagnostic.WorkerRequestId > 0
                ? $" worker-request={diagnostic.WorkerRequestId}"
                : string.Empty) +
            (diagnostic.ValidationPath is { } validationPath &&
             diagnostic.ValidationCode is { } validationCode &&
             BridgeWidgetRequestDiagnostic.IsSafeValidationPath(validationPath) &&
             BridgeWidgetRequestDiagnostic.IsSafeValidationCode(validationCode)
                ? $" validation-path={validationPath} validation-code={validationCode}"
                : string.Empty) +
            (diagnostic.ValidationField is { } validationField &&
             diagnostic.ValidationState is { } validationState &&
             BridgeWidgetRequestDiagnostic.IsSafeValidationField(validationField) &&
             BridgeWidgetRequestDiagnostic.IsSafeValidationState(validationState) &&
             (diagnostic.ValidationIdentifier is null ||
              ProtocolValidationIdentifierContext.IsSafeIdentifier(
                  diagnostic.ValidationIdentifier)) &&
             (diagnostic.ValidationIdentifier is not null ||
              validationState is "missing" or "unsafe_value")
                ? $" validation-field={validationField} validation-state={validationState}" +
                  (diagnostic.ValidationIdentifier is { } validationIdentifier
                      ? $" validation-identifier={validationIdentifier}"
                      : string.Empty)
                : string.Empty) +
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
                AppendBounded(line);
            }
            catch (Exception exception) when (exception is IOException or
                UnauthorizedAccessException or NotSupportedException) { }
        }
    }

    internal void AppendBounded(string line)
    {
        using var processMutex = new Mutex(
            initiallyOwned: false,
            OperatingSystem.IsWindows() ? CrossProcessMutexName : null);
        var acquired = false;
        try
        {
            try { acquired = processMutex.WaitOne(CrossProcessWaitMilliseconds); }
            catch (AbandonedMutexException) { acquired = true; }
            if (!acquired) return;

            var incomingBytes = Encoding.UTF8.GetByteCount(line);
            var currentLength = File.Exists(_path) ? new FileInfo(_path).Length : 0;
            if (currentLength + incomingBytes > MaximumFileBytes)
            {
                var oldest = GenerationPath(RetainedGenerationCount);
                if (File.Exists(oldest)) File.Delete(oldest);
                for (var generation = RetainedGenerationCount - 1;
                     generation >= 0;
                     generation--)
                {
                    var source = generation == 0 ? _path : GenerationPath(generation);
                    if (!File.Exists(source)) continue;
                    MoveBounded(source, GenerationPath(generation + 1));
                }
            }
            File.AppendAllText(_path, line, Encoding.UTF8);
        }
        finally
        {
            if (acquired) processMutex.ReleaseMutex();
        }
    }

    private string GenerationPath(int generation) =>
        Path.Combine(
            Path.GetDirectoryName(_path)!,
            $"{Path.GetFileNameWithoutExtension(_path)}.{generation}{Path.GetExtension(_path)}");

    private static void MoveBounded(string source, string destination)
    {
        if (new FileInfo(source).Length <= MaximumFileBytes)
        {
            File.Move(source, destination, overwrite: true);
            return;
        }

        var temporary = destination + $".{Guid.NewGuid():N}.tmp";
        try
        {
            using (var input = new FileStream(
                       source, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (var output = new FileStream(
                       temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                input.Seek(-MaximumFileBytes, SeekOrigin.End);
                while (input.Position < input.Length)
                    if (input.ReadByte() == (byte)'\n') break;
                input.CopyTo(output);
                output.Flush(flushToDisk: true);
            }
            File.Move(temporary, destination, overwrite: true);
            File.Delete(source);
        }
        finally
        {
            try { File.Delete(temporary); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
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
