using System.Diagnostics;
using System.Text.Json;
using WidgetRail.PerformanceTelemetry;
using WidgetRail.WindowsPerformanceProvider;

if (args is ["--probe-helper", var helper]) return await CollectorProbe.RunAsync(helper);

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
    Check(accumulator.Observe("Application,ProcessID,SwapChainAddress,MsBetweenPresents", 0), "Header rejected");
    return accumulator;
}
static Task Intervals()
{
    var frames = Create();
    for (var i = 0; i < 60; i++) Check(frames.Observe("\"game, special.exe\",123,0x1,16.6666667", i / 120d), "Frame rejected");
    var result = frames.Snapshot(1).Single();
    Check(Math.Abs(result.FramesPerSecond - 60) < .001 && result.SampleCount == 60, "Arrival rate was confused with actual FPS");
    return Task.CompletedTask;
}
static Task DominantChain()
{
    var frames = Create();
    for (var i = 0; i < 60; i++) frames.Observe("game,123,0x1,16.6666667", i / 60d);
    for (var i = 0; i < 30; i++) frames.Observe("game,123,0x2,33.3333333", 1 + i / 60d);
    Check(Math.Abs(frames.Snapshot(1.6).Single().FramesPerSecond - 60) < .001, "Swap chains were added together");
    return Task.CompletedTask;
}
static Task StaleAndRestart()
{
    var frames = Create(); frames.Observe("game,1,0x1,10", .1);
    Check(frames.Snapshot(3).Count == 0, "Frozen FPS survived a stopped process");
    frames.Observe("game,1,0x1,10", 3);
    frames.Clear(); Check(frames.Snapshot(3).Count == 0, "Collector restart retained old values");
    return Task.CompletedTask;
}
static Task Malformed()
{
    var frames = Create();
    foreach (var row in new[] { "game,1,0x1,NaN", "game,1,0x1,Infinity", "game,0,0x1,20", "game,1,0x1,-20", "game,1,0x1,0", "\"unterminated,1,0x1,20", new string('x', 8193) })
        Check(!frames.Observe(row, 1), "Invalid frame admitted");
    Check(frames.Snapshot(1).Count == 0, "Bad data became a measured zero");
    return Task.CompletedTask;
}
static Task Bounded()
{
    var frames = Create();
    for (var i = 1; i <= 500; i++) frames.Observe($"game,{i},0x1,10", i / 1000d);
    Check(frames.Snapshot(1).Count == PresentFrameAccumulator.MaximumStreams, "Stream bound exceeded");
    for (var i = 0; i < 1500; i++) frames.Observe("game,500,0x1,1", 1);
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
        valid with { Processes = [new(123, double.NaN, 1)] }, valid with { Processes = [new(123, 60, 1), new(123, 30, 1)] } })
    {
        try { CollectorProtocol.Validate(bad); throw new Exception("Bad numeric report accepted"); }
        catch (IOException) { }
    }
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
