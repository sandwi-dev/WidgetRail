using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using WidgetRail.OverlayFrontend.WinUI.Capture;
using WidgetRail.PlatformBroker;
using WidgetRail.WidgetBridge;
using WidgetRail.WidgetProtocol;

if (args.Length != 3) throw new ArgumentException("Expected native directory, owned WindowCaptureFixture.exe and fresh evidence directory.");
var native = Path.GetFullPath(args[0]); var fixturePath = Path.GetFullPath(args[1]); var evidence = Path.GetFullPath(args[2]);
if (Path.GetFileName(fixturePath) != "WindowCaptureFixture.exe" || Directory.Exists(evidence)) throw new ArgumentException("Use the owned fixture and a fresh output.");
Directory.CreateDirectory(evidence);
NativeLibrary.SetDllImportResolver(typeof(Program).Assembly, (name, _, _) => name == "ReplayWorkerHost.dll" ? NativeLibrary.Load(Path.Combine(native, name)) : 0);
var checks = new List<string>(); var failures = new ConcurrentQueue<Exception>();
using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(40));
using var fixture = Process.Start(new ProcessStartInfo(fixturePath) { UseShellExecute = false, CreateNoWindow = true,
    Arguments = "--audio --width=1280 --height=720", WindowStyle = ProcessWindowStyle.Hidden }) ?? throw new InvalidOperationException("Fixture did not start");
try
{
    await Wait(() => { fixture.Refresh(); return fixture.MainWindowHandle != 0; });
    var target = new NativeWindowPreviewTarget(fixture.MainWindowHandle.ToString("X"), checked((uint)fixture.Id),
        fixture.StartTime.ToUniversalTime().ToFileTimeUtc().ToString("X16"), "WidgetRailCaptureFixture");
    var actor = new BrokerWidgetIdentity("test.replay.integration", "test.replay.publisher", "replay.integration.instance");
    var configuration = new CaptureReplayConfiguration(0, new() { ReplayDurationSeconds = 15, MemoryBudgetBytes = 32L * 1024 * 1024,
        Recording = new(1280, 720, 30, 6_000_000, Encoder: CaptureEncoderMode.Hardware, Audio: CaptureAudioMode.Application) });
    using var library = new CaptureLibraryStore(Path.Combine(evidence, "index"), Path.Combine(evidence, "captures"),
        WindowsCaptureImageCodec.CreateContextAsync, WindowsCaptureImageCodec.CreateThumbnailAsync, WindowsCaptureImageCodec.InspectAsync);
    var granted = true;
    await using var service = new CaptureReplayService(library, configuration,
        (next, expected, token) => { token.ThrowIfCancellationRequested(); Check(configuration.Revision == expected, "configuration compares revision");
            configuration = next; return Task.FromResult(next); },
        (_, _, _) => Task.FromResult(Volatile.Read(ref granted) ? CaptureReplayIssue.None : CaptureReplayIssue.PermissionRevoked));
    var host = new IntegrationHost(service);
    var factory = new ObservedFactory();
    await using var coordinator = new ReplayCaptureCoordinator(host, factory,
        (_, token) => { token.ThrowIfCancellationRequested(); return Task.FromResult(new ReplayCaptureDecision(true, target, new string('a', 64), "Owned replay fixture")); }, failures.Enqueue);
    coordinator.Start(); await Wait(() => host.Polls > 0);
    var start = await service.StartAsync(new object(), actor, deadline.Token);
    Check(start.Outcome == CaptureReplayOutcome.Accepted && start.Status.Phase == CaptureReplayPhase.Starting, "real native launch remains Starting until first report");
    await Wait(() => service.Status.Phase == CaptureReplayPhase.Recording && service.Status.BufferedDurationSeconds >= 2.5);
    Check(service.Status.ResidentBytes is > 0 and <= 32L * 1024 * 1024, "real encoded buffer stays bounded");
    var saveRequest = new CaptureReplaySaveRequest(start.Status.SessionId!, Guid.NewGuid().ToString("N"));
    var saveClock = Stopwatch.StartNew();
    var saved = service.SaveAsync(actor, saveRequest, deadline.Token);
    var stop = await service.StopAsync(start.Status.SessionId, deadline.Token);
    Check(stop.Outcome == CaptureReplayOutcome.Accepted && !stop.Status.Settings.Enabled, "accepted save coexists with disabling recording");
    var result = await saved;
    var saveMilliseconds = saveClock.Elapsed.TotalMilliseconds;
    Check(result.Outcome == CaptureReplayOutcome.Accepted && result.IsWellFormed() && result.ActualDurationSeconds is > 0 and <= 15,
        "real Windows-inspected MP4 commits after native save and stop");
    Check(result.Item!.Media.Properties is { Audio: CaptureAudioMode.Application, Encoder: CaptureEncoderMode.Hardware }, "saved metadata preserves actual application audio and hardware encoding");
    await Wait(() => service.Status.SessionId is null && factory.Disposals == 1);
    Check((await library.ListAsync(0, deadline.Token)).TotalCount == 1 && await service.SaveAsync(actor, saveRequest, deadline.Token) == result,
        "native completion deduplicates one published library item");
    var second = await service.StartAsync(new object(), actor, deadline.Token);
    Check(second.Outcome == CaptureReplayOutcome.Accepted && second.Status.SessionId != start.Status.SessionId, "new recording has distinct session authority");
    await Wait(() => service.Status.BufferedDurationSeconds >= 1);
    Volatile.Write(ref granted, false);
    await Wait(() => service.Status.SessionId is null && factory.Disposals == 2);
    Check(!service.Status.Settings.Enabled && service.Status.Issue == CaptureReplayIssue.PermissionRevoked, "live grant revocation closes actual worker and disables recording");
    Check(failures.IsEmpty, "expected native lifecycle generated no coordinator errors");
    await File.WriteAllTextAsync(Path.Combine(evidence, "result.json"), JsonSerializer.Serialize(new { passed = checks.Count, checks,
        capture = result.Item, saveMilliseconds, polls = host.Polls, factory.Starts, factory.Disposals }, new JsonSerializerOptions { WriteIndented = true }));
}
catch (Exception error)
{
    await File.WriteAllTextAsync(Path.Combine(evidence, "result.json"), JsonSerializer.Serialize(new { passed = checks.Count, checks, failure = error.ToString(),
        coordinatorErrors = failures.Select(item => item.ToString()).ToArray() }, new JsonSerializerOptions { WriteIndented = true }));
    throw;
}
finally
{
    if (!fixture.HasExited)
    {
        fixture.CloseMainWindow();
        if (!fixture.WaitForExit(2000)) { fixture.Kill(entireProcessTree: true); await fixture.WaitForExitAsync(); }
    }
}
return;

void Check(bool condition, string label)
{ if (!condition) throw new InvalidOperationException(label); checks.Add(label); Console.WriteLine("PASS " + label); }
async Task Wait(Func<bool> condition)
{ while (!condition()) await Task.Delay(25, deadline.Token); }

internal sealed class IntegrationHost(CaptureReplayService service) : IReplayCaptureHost
{
    internal int Polls;
    public async Task<HostReplayWork> PollAsync(HostReplayReport? report, CancellationToken token)
    { var result = await service.PollHostAsync(report, token); Interlocked.Increment(ref Polls); return result; }
    public Task<bool> IsCurrentAsync(string id, CancellationToken token) => service.IsHostCommandCurrentAsync(id, token);
    public Task<bool> CompleteAsync(HostReplayCompletion result, CancellationToken token) => service.CompleteHostAsync(result, token);
    public Task SetPowerAsync(long revision, bool suspended, CancellationToken token) => service.SetHostPowerStateAsync(revision, suspended, token);
}
internal sealed class ObservedFactory : IReplayCaptureBackendFactory
{
    internal int Starts, Disposals;
    public async Task<IReplayCaptureBackend> StartAsync(NativeWindowPreviewTarget target, CaptureReplaySettings settings, CancellationToken token)
    { var backend = await new NativeReplayCaptureBackendFactory().StartAsync(target, settings, token); Interlocked.Increment(ref Starts); return new ObservedBackend(this, backend); }
    private sealed class ObservedBackend(ObservedFactory owner, IReplayCaptureBackend backend) : IReplayCaptureBackend
    {
        private int disposed;
        public Task<ReplayBackendState> ReadStateAsync(CancellationToken token) => backend.ReadStateAsync(token);
        public void RenewAuthority() => backend.RenewAuthority();
        public Task<ReplayBackendSaveResult> SaveAsync(string path, int seconds, CancellationToken token) => backend.SaveAsync(path, seconds, token);
        public Task StopAsync(CancellationToken token) => backend.StopAsync(token);
        public async ValueTask DisposeAsync()
        { if (Interlocked.Exchange(ref disposed, 1) == 0) { await backend.DisposeAsync(); Interlocked.Increment(ref owner.Disposals); } }
    }
}
