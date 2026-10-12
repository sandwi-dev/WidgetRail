using System.Diagnostics;
using System.Text.Json;
using WidgetRail.PerformanceTelemetry;
using WidgetRail.WindowsPerformanceProvider;

if (args is ["--probe-helper", var helper]) return await CollectorProbe.RunAsync(helper);
if (args is ["--probe-runner", var presentMon, var targetPid, var output])
    return await RunnerProbe.RunAsync(presentMon, int.Parse(targetPid), output);

if (args is ["--probe-system"])
{
    using var sampler = new WindowsSystemSampler();
    using var self = Process.GetCurrentProcess();
    var cpu = self.TotalProcessorTime;
    var clock = Stopwatch.StartNew();
    var readings = new List<SystemPerformanceReading>();
    TimeSpan warmCpu = default;
    double warmStart = 0;
    for (var i = 0; i < 12; i++)
    {
        readings.Add(sampler.Read());
        if (i == 1) { self.Refresh(); warmCpu = self.TotalProcessorTime; warmStart = clock.Elapsed.TotalMilliseconds; }
        if (i != 11) await Task.Delay(1050);
    }
    self.Refresh();
    Console.WriteLine(JsonSerializer.Serialize(new { readings, elapsedMs = clock.Elapsed.TotalMilliseconds,
        collectorCpuMs = (self.TotalProcessorTime - cpu).TotalMilliseconds,
        warmCollectorCpuMs = (self.TotalProcessorTime - warmCpu).TotalMilliseconds,
        warmElapsedMs = clock.Elapsed.TotalMilliseconds - warmStart,
        workingSetBytes = self.WorkingSet64 }, new JsonSerializerOptions { WriteIndented = true }));
    return 0;
}

var tests = new (string Name, Func<Task> Run)[]
{
    ("Frames are measured from intervals, not row arrival cadence", Intervals),
    ("One dominant swap chain is counted per process", DominantChain),
    ("Stale processes and collector restarts lose old FPS", StaleAndRestart),
    ("Malformed CSV and non-finite metrics are rejected", Malformed),
    ("Frame storage remains bounded", Bounded),
    ("Collector protocol enforces numeric-only result bounds", Protocol),
    ("Collector diagnostics are bounded sanitized and rotated without privileged writes", Diagnostics),
    ("Rolling lows use the slowest individual frames, not sampled FPS", FrameStatistics),
    ("The sixty-second window removes old stutters", HistoryExpiry),
    ("Batched frames retain event times and cannot refresh stale FPS", EventTimestamps),
    ("Trace options and failures preserve continuous collection and accurate errors", SessionContract),
    ("On-demand native sampling is cached and warms again after idle", NativeSampling),
};
var failures = 0;
foreach (var test in tests)
{
    try { await test.Run(); Console.WriteLine("PASS " + test.Name); }
    catch (Exception error) { failures++; Console.Error.WriteLine("FAIL " + test.Name + ": " + error.Message); }
}
Console.WriteLine($"{tests.Length - failures}/{tests.Length} tests passed.");
return failures == 0 ? 0 : 1;

static PresentFrameAccumulator Create()
{
    var accumulator = new PresentFrameAccumulator();
    Check(accumulator.Observe("Application,ProcessID,SwapChainAddress,MsBetweenPresents,QPCTime", 0), "Header rejected");
    return accumulator;
}
static bool Feed(PresentFrameAccumulator frames, string row, double now) =>
    frames.Observe(row + "," + ((long)(now * Stopwatch.Frequency)).ToString(System.Globalization.CultureInfo.InvariantCulture), now);

static Task EventTimestamps()
{
    var frames = Create();
    static string Row(double at, double interval = 10) => FormattableString.Invariant($"game,123,0x1,{interval},{(long)(at * Stopwatch.Frequency)}");
    // Five seconds arrive in one batch. The current window must only contain
    // frames actually presented in the last two seconds, not the entire batch.
    for (var i = 0; i < 500; i++) Check(frames.Observe(Row(i / 100d), 5), "Batch frame rejected");
    var rate = frames.Snapshot(5).Single();
    Check(rate.SampleCount == 200 && rate.FrameStatistics!.SampleCount == 500, "Arrival time defined the sample windows");
    Check(frames.Snapshot(6.2).Single().FrameStatistics!.RecentWorstFrameMilliseconds == 10, "Delivery lag fabricated zero frame time");
    Check(!frames.Observe(Row(1), 5), "Out-of-order frame admitted into ordered queue");
    Check(!frames.Observe(Row(7), 5), "Future frame admitted");
    Check(frames.Snapshot(8).Count == 0, "Old event retained fresh FPS");
    Check(frames.Observe(Row(4), 8), "Recent history rejected");
    Check(frames.Snapshot(8).Count == 0, "Late delivery revived expired FPS");
    Check(!frames.Observe(Row(1), 70), "Expired history admitted");
    var malformed = new PresentFrameAccumulator();
    Check(!malformed.Observe("Application,ProcessID,SwapChainAddress,MsBetweenPresents", 0), "Missing timestamp accepted");
    Check(!frames.Observe("game,123,0x1,10,NaN", 9), "Bad timestamp accepted");
    return Task.CompletedTask;
}

static Task SessionContract()
{
    var info = PresentMonRunner.CollectionStartInfo("PresentMon.exe", "WidgetRail-FPS-test");
    Check(info.ArgumentList.Contains("--qpc_time") && info.ArgumentList.Contains("--no_track_input") &&
        info.ArgumentList.Contains("--no_track_gpu") && !info.ArgumentList.Contains("--no_track_display") &&
        !info.ArgumentList.Contains("--timed") && !info.ArgumentList.Contains("--terminate_after_timed"), "Incorrect trace configuration");
    Check(PresentMonRunner.FailureState(6, "error: failed to start trace session: access denied.\r\n") == "permission-required", "Lost permission diagnosis");
    Check(PresentMonRunner.FailureState(6, "error: a trace session named test is already running.") == "failed", "Session collision mislabeled as permission");
    Check(PresentMonRunner.FailureState(6, "error: failed to start trace session: error code 1450.") == "failed", "Resource failure mislabeled as permission");
    Check(PresentMonRunner.FailureState(0, "") == "failed", "Unexpected clean exit silently restarts");
    return Task.CompletedTask;
}
static Task Intervals()
{
    var frames = Create();
    for (var i = 0; i < 60; i++) Check(Feed(frames, "\"game, special.exe\",123,0x1,16.6666667", i / 120d), "Frame rejected");
    var result = frames.Snapshot(1).Single();
    Check(Math.Abs(result.FramesPerSecond - 60) < .001 && result.SampleCount == 60, "Arrival rate was confused with actual FPS");
    return Task.CompletedTask;
}
static Task DominantChain()
{
    var frames = Create();
    for (var i = 0; i < 60; i++) Feed(frames, "game,123,0x1,16.6666667", i / 60d);
    for (var i = 0; i < 30; i++) Feed(frames, "game,123,0x2,33.3333333", 1 + i / 60d);
    Check(Math.Abs(frames.Snapshot(1.6).Single().FramesPerSecond - 60) < .001, "Swap chains were added together");
    return Task.CompletedTask;
}
static Task StaleAndRestart()
{
    var frames = Create(); Feed(frames, "game,1,0x1,10", .1);
    Check(frames.Snapshot(3).Count == 0, "Frozen FPS survived a stopped process");
    Feed(frames, "game,1,0x1,10", 3);
    frames.Clear(); Check(frames.Snapshot(3).Count == 0, "Collector restart retained old values");
    return Task.CompletedTask;
}
static Task FrameStatistics()
{
    var frames = Create();
    for (var i = 0; i < 1000; i++) Feed(frames, $"game,123,0x1,{(i < 10 ? 100 : 10)}", i / 100d);
    var rate = frames.Snapshot(10).Single();
    var summary = rate.FrameStatistics!;
    Check(summary.IsWellFormed(), "Generated invalid statistics");
    Check(Math.Abs(summary.AverageFramesPerSecond - 1000 / 10.9) < .001, "Mean FPS was not interval-weighted");
    Check(Math.Abs(summary.OnePercentLowFramesPerSecond!.Value - 10) < .001, "Slowest one percent was not used");
    Check(Math.Abs(rate.FramesPerSecond - 100) < .001, "Current FPS no longer uses recent intervals");
    Check(summary.RecentWorstFrameMilliseconds == 10, "Old stutter leaked into recent graph sample");
    var warming = Create(); Feed(warming, "game,1,0x1,10", .1);
    Check(warming.Snapshot(1).Single().FrameStatistics!.OnePercentLowFramesPerSecond is null, "Low fabricated from too few frames");
    return Task.CompletedTask;
}
static Task HistoryExpiry()
{
    var frames = Create();
    for (var i = 0; i < 7000; i++) Feed(frames, $"game,123,0x1,{(i < 100 ? 100 : 10)}", i / 100d);
    var stats = frames.Snapshot(70).Single().FrameStatistics!;
    Check(stats.SampleCount is >= 6000 and <= 6001 && Math.Abs(stats.WindowSeconds - 60) < .02, "Window was not pruned to sixty seconds");
    Check(stats.OnePercentLowFramesPerSecond == 100, "Expired slow frames still affect lows");
    frames.Clear();
    Check(frames.Snapshot(70).Count == 0, "Cleared collection retains history");
    return Task.CompletedTask;
}
static Task Malformed()
{
    var frames = Create();
    foreach (var row in new[] { "game,1,0x1,NaN", "game,1,0x1,Infinity", "game,0,0x1,20", "game,1,0x1,-20", "game,1,0x1,0", "\"unterminated,1,0x1,20", new string('x', 8193) })
        Check(!Feed(frames, row, 1), "Invalid frame admitted");
    Check(frames.Snapshot(1).Count == 0, "Bad data became a measured zero");
    return Task.CompletedTask;
}
static Task Bounded()
{
    var frames = Create();
    for (var i = 1; i <= 500; i++) Feed(frames, $"game,{i},0x1,10", i / 1000d);
    Check(frames.Snapshot(1).Count == PresentFrameAccumulator.MaximumStreams, "Stream bound exceeded");
    for (var i = 0; i < 1500; i++) Feed(frames, "game,500,0x1,1", 1);
    Check(frames.Snapshot(1).Single(p => p.ProcessId == 500).SampleCount == PresentFrameAccumulator.MaximumSamplesPerStream, "Sample bound exceeded");
    return Task.CompletedTask;
}
static async Task Protocol()
{
    var valid = new CollectorReading("collecting", [new(123, 60, 60)]);
    using var memory = new MemoryStream();
    await CollectorProtocol.WriteAsync(memory, valid, default);
    memory.Position = 0;
    var parsed = await CollectorProtocol.ReadAsync<CollectorReading>(memory, default);
    CollectorProtocol.Validate(parsed);
    foreach (var bad in new[] { valid with { State = "private/path" }, valid with { Processes = [new(0, 60, 1)] },
        valid with { Processes = [new(123, double.NaN, 1)] }, valid with { Processes = [new(123, 60, 1), new(123, 30, 1)] },
        valid with { Diagnostics = [new string('x', 1025)] }, valid with { Diagnostics = ["bad\nline"] },
        valid with { Diagnostics = Enumerable.Repeat("bounded", 17).ToArray() } })
    {
        try { CollectorProtocol.Validate(bad); throw new Exception("Bad numeric report accepted"); }
        catch (IOException) { }
    }
}
static async Task Diagnostics()
{
    var buffer = new FpsDiagnosticBuffer();
    for (var i = 0; i < 30; i++) buffer.Write($"event={i} error at C:\\private\\secret-file\r\n");
    var messages = buffer.Drain();
    Check(messages.Length == 16 && messages[0].StartsWith("event=14", StringComparison.Ordinal), "Diagnostic queue is not bounded");
    Check(messages.All(message => !message.Contains("private") && !message.Any(char.IsControl)), "Private paths or newlines leaked");
    Check(buffer.Drain().Length == 0, "Diagnostics replayed after draining");
    var reading = new CollectorReading("collecting", []) { Diagnostics = messages };
    using var memory = new MemoryStream();
    await CollectorProtocol.WriteAsync(memory, reading, default); memory.Position = 0;
    var restored = await CollectorProtocol.ReadAsync<CollectorReading>(memory, default);
    CollectorProtocol.Validate(restored); Check(restored.Diagnostics.SequenceEqual(messages), "Diagnostic transport changed entries");
    var folder = Path.Combine(Path.GetTempPath(), "wrail-fps-log-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(folder);
    var path = Path.Combine(folder, "fps.log");
    await File.WriteAllTextAsync(path, new string('x', 128 * 1024));
    new FpsDiagnosticLog(path).Write("bounded entry");
    Check(new FileInfo(path).Length < 2048 && new FileInfo(path + ".previous").Length == 128 * 1024, "Log rotation failed");
    File.Delete(path); File.Delete(path + ".previous"); Directory.Delete(folder);
}
static async Task NativeSampling()
{
    using var sampler = new WindowsSystemSampler();
    var first = sampler.Read();
    Check(ReferenceEquals(first, sampler.Read()), "Repeated readers resampled the hardware");
    if (!OperatingSystem.IsWindows()) return;
    Check(first.CpuPercent is null && first.MemoryTotalBytes > 0 && first.MemoryUsedBytes >= 0, "Initial CPU/real memory semantics are wrong");
    await Task.Delay(1050);
    Check(sampler.Read().CpuPercent is >= 0 and <= 100, "CPU did not warm up");
    await Task.Delay(3100);
    Check(sampler.Read().CpuPercent is null, "Idle interval was presented as current CPU usage");
}
static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
