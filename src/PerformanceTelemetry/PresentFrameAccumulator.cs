using System.Globalization;
using System.Buffers;
using System.Diagnostics;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.PerformanceTelemetry;

public sealed record ProcessFrameRate(int ProcessId, double FramesPerSecond, int SampleCount)
{
    public FrameTimingStatistics? FrameStatistics { get; init; }
}

/// <summary>
/// Consumes PresentMon v1 CSV in memory and reports application-present FPS from
/// the busiest swap chain. Never persists application names or raw frame rows.
/// </summary>
public sealed class PresentFrameAccumulator
{
    public const int MaximumStreams = 256;
    public const int MaximumSamplesPerStream = 1024;
    public const int MaximumHistorySamplesPerStream = 65536;
    public const int MaximumHistorySamplesTotal = 262144;
    private readonly object _gate = new();
    private readonly Dictionary<(int ProcessId, string SwapChain), Samples> _streams = [];
    private int _pid = -1, _chain = -1, _interval = -1, _timestamp = -1;
    private double _lastNow;
    private int _historyCount;

    public bool Observe(string row, double nowSeconds)
    {
        if (!double.IsFinite(nowSeconds) || nowSeconds < 0 || row.Length > 8192) return false;
        var fields = ParseCsv(row);
        if (fields is null) return false;
        lock (_gate)
        {
            if (nowSeconds < _lastNow) { _streams.Clear(); _historyCount = 0; }
            _lastNow = nowSeconds;
            if (fields.Length > 2 && fields[0].Equals("Application", StringComparison.OrdinalIgnoreCase) &&
                fields.Contains("ProcessID", StringComparer.OrdinalIgnoreCase))
            {
                _pid = Array.FindIndex(fields, v => v.Equals("ProcessID", StringComparison.OrdinalIgnoreCase));
                _chain = Array.FindIndex(fields, v => v.Equals("SwapChainAddress", StringComparison.OrdinalIgnoreCase));
                _interval = Array.FindIndex(fields, v => v.Equals("MsBetweenPresents", StringComparison.OrdinalIgnoreCase));
                _timestamp = Array.FindIndex(fields, v => v.Equals("QPCTime", StringComparison.OrdinalIgnoreCase));
                _streams.Clear(); _historyCount = 0;
                return _pid >= 0 && _chain >= 0 && _interval >= 0 && _timestamp >= 0;
            }
            if (_pid < 0 || _chain < 0 || _interval < 0 || _timestamp < 0 ||
                fields.Length <= Math.Max(_timestamp, Math.Max(_interval, Math.Max(_pid, _chain))) ||
                !int.TryParse(fields[_pid], NumberStyles.None, CultureInfo.InvariantCulture, out var processId) || processId <= 0 ||
                fields[_chain].Length is 0 or > 32 ||
                !double.TryParse(fields[_interval], NumberStyles.Float, CultureInfo.InvariantCulture, out var milliseconds) ||
                !double.IsFinite(milliseconds) || milliseconds < .1 || milliseconds > 10_000) return false;
            // The QPC clock is shared with PresentMon. Arrival time can be a
            // second or more later, or many historical frames can arrive at once.
            if (!long.TryParse(fields[_timestamp], NumberStyles.None, CultureInfo.InvariantCulture, out var ticks) || ticks < 0)
                return false;
            var frameSeconds = ticks / (double)Stopwatch.Frequency;
            if (frameSeconds > nowSeconds || frameSeconds < nowSeconds - 60) return false;
            var key = (processId, fields[_chain]);
            if (!_streams.TryGetValue(key, out var samples))
            {
                if (_streams.Count == MaximumStreams)
                {
                    var retired = _streams.MinBy(pair => pair.Value.LastSeen);
                    _historyCount -= retired.Value.History.Count;
                    _streams.Remove(retired.Key);
                }
                _streams.Add(key, samples = new());
            }
            if (samples.History.TryPeek(out _) && frameSeconds < samples.LastSeen) return false;
            samples.LastSeen = frameSeconds;
            while (samples.Values.Count != 0 &&
                (samples.Values.Count >= MaximumSamplesPerStream || samples.Values.Peek().At <= frameSeconds - 2))
                samples.Values.Dequeue();
            samples.Values.Enqueue((frameSeconds, milliseconds));
            while (samples.History.Count != 0 && (samples.History.Count >= MaximumHistorySamplesPerStream ||
                samples.History.Peek().At <= frameSeconds - 60)) { samples.History.Dequeue(); --_historyCount; }
            if (_historyCount >= MaximumHistorySamplesTotal)
            {
                // A global bound protects against applications with many busy swap chains.
                var oldest = _streams.Values.Where(s => s.History.Count > 0).MinBy(s => s.History.Peek().At)!;
                oldest.History.Dequeue(); --_historyCount;
                oldest.TrimHistory();
            }
            samples.History.Enqueue((frameSeconds, milliseconds)); ++_historyCount;
            return true;
        }
    }

    public IReadOnlyList<ProcessFrameRate> Snapshot(double nowSeconds)
    {
        lock (_gate)
        {
            foreach (var stale in _streams.Where(pair => pair.Value.LastSeen < nowSeconds - 2.5).Select(pair => pair.Key).ToArray())
            { _historyCount -= _streams[stale].History.Count; _streams.Remove(stale); }
            foreach (var stream in _streams.Values)
            {
                // Measure windows ending at the latest delivered frame. Delivery
                // lag must not fabricate an empty one-second graph sample. Wall
                // clock age above still expires streams that stop producing data.
                while (stream.Values.TryPeek(out var first) && first.At <= stream.LastSeen - 2) stream.Values.Dequeue();
                while (stream.History.TryPeek(out var first) && first.At <= stream.LastSeen - 60) { stream.History.Dequeue(); --_historyCount; }
                stream.TrimHistory();
            }
            return _streams.Where(p => p.Value.Values.Count > 0).GroupBy(pair => pair.Key.ProcessId).Select(group =>
            {
                var stream = group.MaxBy(pair => pair.Value.Values.Count).Value;
                var dominant = stream.Values;
                var history = stream.History;
                var window = history.Count == 0 ? 0 : Math.Min(60, Math.Max(0, stream.LastSeen - history.Peek().At));
                FrameTimingStatistics? statistics = null;
                if (history.Count > 0)
                {
                    var intervals = ArrayPool<double>.Shared.Rent(history.Count);
                    try
                    {
                        var index = 0; double sum = 0;
                        foreach (var frame in history) { intervals[index++] = frame.Milliseconds; sum += frame.Milliseconds; }
                        var mean = sum / history.Count;
                        double? low = null;
                        if (history.Count >= 1000 && window >= 5)
                        {
                            Array.Sort(intervals, 0, history.Count);
                            var slowCount = (int)Math.Ceiling(history.Count * .01);
                            double slowSum = 0;
                            for (var i = history.Count - slowCount; i < history.Count; i++) slowSum += intervals[i];
                            low = 1000 * slowCount / slowSum;
                        }
                        statistics = new(1000 / mean, low, mean,
                            dominant.Where(v => v.At > stream.LastSeen - 1).Select(v => v.Milliseconds).DefaultIfEmpty(0).Max(),
                            history.Count, window);
                    }
                    finally { ArrayPool<double>.Shared.Return(intervals); }
                }
                return new ProcessFrameRate(group.Key, Math.Clamp(1000 * dominant.Count / dominant.Sum(v => v.Milliseconds), 0, 10_000), dominant.Count)
                    { FrameStatistics = statistics };
            }).ToArray();
        }
    }

    public void Clear() { lock (_gate) { _streams.Clear(); _historyCount = 0; _pid = _chain = _interval = _timestamp = -1; } }

    private sealed class Samples
    {
        internal double LastSeen;
        internal Queue<(double At, double Milliseconds)> Values { get; } = new();
        internal Queue<(double At, double Milliseconds)> History { get; } = new();
        internal void TrimHistory()
        {
            // Bound retained capacity too, not only live entries, as the global
            // budget moves between swap chains over a long-running session.
            var capacity = History.EnsureCapacity(0);
            if (capacity > 256 && History.Count < capacity / 4) History.TrimExcess();
        }
    }

    private static string[]? ParseCsv(string row)
    {
        var fields = new List<string>();
        var value = new System.Text.StringBuilder();
        var quoted = false;
        for (var i = 0; i < row.Length; i++)
        {
            var c = row[i];
            if (c == '"')
            {
                if (quoted && i + 1 < row.Length && row[i + 1] == '"') { value.Append('"'); i++; }
                else quoted = !quoted;
            }
            else if (c == ',' && !quoted)
            {
                fields.Add(value.ToString()); value.Clear();
                if (fields.Count >= 128) return null;
            }
            else value.Append(c);
        }
        if (quoted) return null;
        fields.Add(value.ToString());
        return fields.ToArray();
    }
}
