using System.Diagnostics;
using System.Text.Json;
using WidgetRail.PerformanceTelemetry;

internal static class RunnerProbe
{
    // Explicit opt-in hardware probe; never part of the automatic test suite.
    internal static async Task<int> RunAsync(string executable, int targetPid, string output)
    {
        var messages = new System.Collections.Concurrent.ConcurrentQueue<string>();
        var runner = new PresentMonRunner(executable, messages.Enqueue);
        using var lifetime = new CancellationTokenSource();
        var demand = 0;
        var work = runner.RunAsync(() => Volatile.Read(ref demand) != 0, lifetime.Token);
        var readings = new List<object>();
        var missing = 0;
        var emptyFrameTimes = 0;
        double longestWindow = 0;
        try
        {
            await Task.Delay(300);
            if (runner.Read().State != "idle" || !messages.IsEmpty) throw new Exception("Collection started without demand");
            Volatile.Write(ref demand, 1);
            var clock = Stopwatch.StartNew();
            while (clock.Elapsed.TotalSeconds < 145)
            {
                await Task.Delay(1000);
                var reading = runner.Read();
                CollectorProtocol.Validate(reading);
                var target = reading.Processes.SingleOrDefault(p => p.ProcessId == targetPid);
                if (clock.Elapsed.TotalSeconds > 10 && target is null) missing++;
                if (clock.Elapsed.TotalSeconds > 10 && target?.FrameStatistics?.RecentWorstFrameMilliseconds == 0) emptyFrameTimes++;
                longestWindow = Math.Max(longestWindow, target?.FrameStatistics?.WindowSeconds ?? 0);
                readings.Add(new { seconds = clock.Elapsed.TotalSeconds, reading.State, target });
                await File.WriteAllTextAsync(output + ".progress", JsonSerializer.Serialize(readings[^1]));
                if (reading.State is "failed" or "permission-required" or "missing") throw new Exception("Collector failed: " + reading.State);
            }
            Volatile.Write(ref demand, 0);
            var stop = Stopwatch.StartNew();
            while (runner.Read().State != "idle" && stop.Elapsed < TimeSpan.FromSeconds(12)) await Task.Delay(100);
            if (runner.Read().State != "idle" || runner.Read().Processes.Count != 0) throw new Exception("Stop demand did not retire collection");
            if (messages.Count(m => m.StartsWith("session-start ", StringComparison.Ordinal)) != 1) throw new Exception("Collection restarted during probe");
            if (missing != 0 || emptyFrameTimes != 0 || longestWindow < 55)
                throw new Exception($"Incomplete live readings: missing={missing} emptyFrameTimes={emptyFrameTimes} window={longestWindow}");
            return 0;
        }
        finally
        {
            lifetime.Cancel();
            try { await work; } catch (OperationCanceledException) { }
            await File.WriteAllTextAsync(output, JsonSerializer.Serialize(new { targetPid, missing, emptyFrameTimes, longestWindow, readings, diagnostics = messages.ToArray() },
                new JsonSerializerOptions { WriteIndented = true }));
        }
    }
}
