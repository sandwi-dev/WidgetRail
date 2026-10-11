using System.Globalization;

namespace WidgetRail.PerformanceTelemetry;

public sealed record ProcessFrameRate(int ProcessId, double FramesPerSecond, int SampleCount);

/// <summary>
/// Consumes PresentMon v1 CSV in memory and reports application-present FPS from
/// the busiest swap chain. Never persists application names or raw frame rows.
/// </summary>
public sealed class PresentFrameAccumulator
{
    public const int MaximumStreams = 256;
    public const int MaximumSamplesPerStream = 1024;
    private readonly object _gate = new();
    private readonly Dictionary<(int ProcessId, string SwapChain), Samples> _streams = [];
    private int _pid = -1, _chain = -1, _interval = -1;
    private double _lastNow;

    public bool Observe(string row, double nowSeconds)
    {
        if (!double.IsFinite(nowSeconds) || nowSeconds < 0 || row.Length > 8192) return false;
        var fields = ParseCsv(row);
        if (fields is null) return false;
        lock (_gate)
        {
            if (nowSeconds < _lastNow) _streams.Clear();
            _lastNow = nowSeconds;
            if (fields.Length > 2 && fields[0].Equals("Application", StringComparison.OrdinalIgnoreCase) &&
                fields.Contains("ProcessID", StringComparer.OrdinalIgnoreCase))
            {
                _pid = Array.FindIndex(fields, v => v.Equals("ProcessID", StringComparison.OrdinalIgnoreCase));
                _chain = Array.FindIndex(fields, v => v.Equals("SwapChainAddress", StringComparison.OrdinalIgnoreCase));
                _interval = Array.FindIndex(fields, v => v.Equals("MsBetweenPresents", StringComparison.OrdinalIgnoreCase));
                _streams.Clear();
                return _pid >= 0 && _chain >= 0 && _interval >= 0;
            }
            if (_pid < 0 || _chain < 0 || _interval < 0 || fields.Length <= Math.Max(_interval, Math.Max(_pid, _chain)) ||
                !int.TryParse(fields[_pid], NumberStyles.None, CultureInfo.InvariantCulture, out var processId) || processId <= 0 ||
                fields[_chain].Length is 0 or > 32 ||
                !double.TryParse(fields[_interval], NumberStyles.Float, CultureInfo.InvariantCulture, out var milliseconds) ||
                !double.IsFinite(milliseconds) || milliseconds <= 0 || milliseconds > 10_000) return false;
            var key = (processId, fields[_chain]);
            if (!_streams.TryGetValue(key, out var samples))
            {
                if (_streams.Count == MaximumStreams)
                    _streams.Remove(_streams.MinBy(pair => pair.Value.LastSeen).Key);
                _streams.Add(key, samples = new());
            }
            samples.LastSeen = nowSeconds;
            while (samples.Values.Count != 0 &&
                (samples.Values.Count >= MaximumSamplesPerStream || samples.Values.Peek().At < nowSeconds - 2))
                samples.Values.Dequeue();
            samples.Values.Enqueue((nowSeconds, milliseconds));
            return true;
        }
    }

    public IReadOnlyList<ProcessFrameRate> Snapshot(double nowSeconds)
    {
        lock (_gate)
        {
            foreach (var stale in _streams.Where(pair => pair.Value.LastSeen < nowSeconds - 2.5).Select(pair => pair.Key).ToArray())
                _streams.Remove(stale);
            return _streams.GroupBy(pair => pair.Key.ProcessId).Select(group =>
            {
                var dominant = group.MaxBy(pair => pair.Value.Values.Count).Value.Values;
                return new ProcessFrameRate(group.Key, Math.Clamp(1000 * dominant.Count / dominant.Sum(v => v.Milliseconds), 0, 10_000), dominant.Count);
            }).ToArray();
        }
    }

    public void Clear() { lock (_gate) { _streams.Clear(); _pid = _chain = _interval = -1; } }

    private sealed class Samples
    {
        internal double LastSeen;
        internal Queue<(double At, double Milliseconds)> Values { get; } = new();
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
