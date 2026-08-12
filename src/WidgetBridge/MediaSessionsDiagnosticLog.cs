using System.Text;
using System.Threading.Channels;
using GameBarAlternative.PlatformBroker;

namespace GameBarAlternative.WidgetBridge;

/// <summary>
/// Records only transition-owned, correlation-safe Media Sessions failures.
/// Provider/player identities, response bodies, process details, and credentials
/// never enter this bounded lane.
/// </summary>
internal sealed class MediaSessionsDiagnosticLog : IAsyncDisposable
{
    internal const int MaximumTrackedTransitions = 256;
    internal const int MaximumPendingLines = 32;

    private readonly object _gate = new();
    private readonly string _path;
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

    internal MediaSessionsDiagnosticLog(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        _path = Path.GetFullPath(path);
        _writer = WriteAsync();
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
