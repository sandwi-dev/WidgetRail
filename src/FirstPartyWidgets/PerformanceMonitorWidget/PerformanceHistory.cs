using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

namespace WidgetRail.FirstPartyWidgets.PerformanceMonitor;

/// <summary>One-minute UI history; frame statistics themselves come from individual collector intervals.</summary>
internal sealed class PerformanceHistory
{
    private readonly Queue<(DateTimeOffset At, WidgetPerformanceSnapshot Sample, int Epoch)> samples = new();
    private string? target;
    private int epoch;

    internal void Observe(WidgetPerformanceSnapshot sample, DateTimeOffset now)
    {
        var changed = target != sample.SelectedTargetId;
        if (changed) { target = sample.SelectedTargetId; ++epoch; }
        if (samples.Count > 0 && now <= samples.Last().At) samples.Clear();
        if (!changed && samples.Count > 0 && (now - samples.Last().At).TotalSeconds < .9) return;
        while (samples.TryPeek(out var first) && (samples.Count >= 61 || (now - first.At).TotalSeconds > 60)) samples.Dequeue();
        samples.Enqueue((now, sample, epoch));
    }

    internal void ResetTarget() { target = null; ++epoch; }

    internal LineGraphDefinition Graph(Func<WidgetPerformanceSnapshot, double?> value, DateTimeOffset now,
        string unit, double? maximum = null, bool application = false)
    {
        var points = new List<GraphPoint>();
        double? previous = null;
        foreach (var sample in samples)
        {
            var at = (sample.At - now).TotalSeconds;
            if (at is < -60 or > 0) continue;
            if (previous is { } prior && at - prior > 2.5) points.Add(new(prior + .001, null));
            points.Add(new(at, application && sample.Epoch != epoch ? null : value(sample.Sample)));
            previous = at;
        }
        if (previous is { } last && last < -2.5) points.Add(new(0, null));
        return new(points.TakeLast(LineGraphDefinition.MaximumPoints).ToArray(), -60, 0, 0, maximum, unit);
    }
}
