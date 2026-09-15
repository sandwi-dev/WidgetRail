using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text.Json;
using WidgetRail.PlatformBroker;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;
using WidgetRail.WindowsDisplayProvider;
using WidgetRail.FirstPartyWidgets.DisplayProfiles;
using WidgetRail.WidgetCatalog;
using WidgetRail.WidgetStyling;

if (args is ["--identity-read"] && OperatingSystem.IsWindows())
{
    var native = new WindowsDisplayNative();
    var current = native.Capture();
    var connected = native.ConnectedPaths();
    Check(DisplayProfileMatching.Remap(current, connected).Matches(current));
    foreach (var target in current.Targets)
        Console.WriteLine($"Monitor: {target.Name}; physical identity available={target.HardwareKey is not null}");
    var scale = DisplayScaleIdentity.Resolve(current.Targets.Select(target => target.DevicePath).ToArray());
    Check(scale is { Length: 64 }, "Shared display scale identity unavailable");
    Console.WriteLine("PASS live identities resolve without changing displays or saving profiles.");
    return;
}

if (args is ["--native-read"])
{
    await NativeRead();
    Console.WriteLine("PASS current desktop captured and validated without applying changes.");
    return;
}

if (args is ["--guard-probe", var guardExecutable])
{
    Console.WriteLine("Guard probe: starting");
    await using var connection = await DisplayGuardConnection.StartAsync(Path.GetFullPath(guardExecutable), default);
    Console.WriteLine("Guard probe: connected and authenticated");
    // Missing transaction identity is rejected before native capture or apply.
    await connection.Writer.WriteLineAsync("{}");
    Console.WriteLine("Guard probe: sent empty transaction");
    var reply = await connection.Reader.ReadToEndAsync().WaitAsync(TimeSpan.FromSeconds(10));
    Check(reply.Length == 0, "Guard failed independent startup: " + reply);
    Console.WriteLine("PASS packaged guard started with explicit breakaway, authenticated its pipe and rejected an empty transaction without display changes.");
    return;
}
if (args is ["--guard-parent-exit", var bridgeExecutable])
{
    var connection = await DisplayGuardConnection.StartAsync(Path.GetFullPath(bridgeExecutable), default);
    Console.WriteLine(connection.ProcessId);
    await Console.Out.FlushAsync();
    await Console.In.ReadLineAsync();
    Environment.Exit(0); // Deliberately bypass disposal: the child must observe OS pipe closure.
    return;
}

var checks = new (string Name, Func<Task> Run)[]
{
    ("Native structures and configuration serialization preserve display modes", Layout),
    ("VRR product changes use unique physical serial identities", HardwareIdentities),
    ("Monitor identities remap adapter IDs and preserve clone groups", Matching),
    ("Profile status agrees with remapped physical monitor identity", ProfileIdentityStatus),
    ("Profile writes are atomic and corrupt data is preserved", Storage),
    ("Guard reverts on timeout, parent EOF and partial apply failure", Guard),
    ("Sleeping displays stabilize and retry once with refreshed paths", WakeStabilization),
    ("Unstable displays and parent loss revert without confirmation", WakeFailures),
    ("Keep rechecks the deadline after reading the active setup", KeepReadDeadline),
    ("Diagnostics observe preview drift and Keep rejects the changed setup", DiagnosticObservations),
    ("Diagnostic failures and blocked readbacks cannot prevent rollback", DiagnosticFailures),
    ("Broker enforces consent, interactive state and closed request payloads", Authority),
    ("Widget supports naming, unavailable profile management and guarded focus", Widget),
    ("Widget stays responsive across slow restore and reopen", WidgetWakeOperation),
    ("Packaged manifest, monitor glyph and theme styles validate", Assets),
};
foreach (var (name, run) in checks) { await run(); Console.WriteLine("PASS " + name); }
Console.WriteLine($"{checks.Length}/{checks.Length} display profile checks passed. No real display changes were applied.");

static void Check(bool value, string reason = "Assertion failed") { if (!value) throw new Exception(reason); }
static async Task<T> Throws<T>(Func<Task> action) where T : Exception
{
    try { await action(); } catch (T error) { return error; }
    throw new Exception("Expected " + typeof(T).Name);
}
static Task Layout()
{
    Check(Marshal.SizeOf<NativeLuid>() == 8 && Marshal.SizeOf<NativePath>() == 72 && Marshal.SizeOf<NativeMode>() == 64);
    Check(Marshal.SizeOf<WindowsDisplayNative.TargetName>() == 420);
    var original = Fixture.Setup(1920, 2);
    var copy = JsonSerializer.Deserialize<DisplayConfiguration>(JsonSerializer.Serialize(original))!;
    Check(copy.Paths.SequenceEqual(original.Paths) && copy.Modes.SequenceEqual(original.Modes));
    Check(copy.Matches(original));
    return Task.CompletedTask;
}
static Task HardwareIdentities()
{
    static byte[] Edid(ushort product, string serial, uint number = 543210)
    {
        var data = new byte[128];
        new byte[] { 0,255,255,255,255,255,255,0 }.CopyTo(data, 0);
        var manufacturer = (19 << 10) | (1 << 5) | 13; // SAM
        data[8] = (byte)(manufacturer >> 8); data[9] = (byte)manufacturer;
        System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(10), product);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(12), number);
        data[57] = 255;
        System.Text.Encoding.ASCII.GetBytes(serial).CopyTo(data, 59);
        data[127] = unchecked((byte)-data.Sum(value => (int)value));
        return data;
    }
    var first = MonitorHardwareIdentity.FromEdid(Edid(0x749c, "ZX12345678"));
    var vrr = MonitorHardwareIdentity.FromEdid(Edid(0x7454, "ZX12345678"));
    var other = MonitorHardwareIdentity.FromEdid(Edid(0x7454, "ZX99999999"));
    Check(first is { Length: 64 } && first == vrr && first != other, "Product mode change altered physical identity");
    Check(MonitorHardwareIdentity.FromEdid(Edid(1, "00000000", 0)) is null);
    Check(MonitorHardwareIdentity.FromEdid(Edid(1, "UNKNOWN", 0)) is null);
    var corrupt = Edid(1, "ZX12345678"); corrupt[20]++;
    Check(MonitorHardwareIdentity.FromEdid(corrupt) is null);

    var saved = Fixture.Setup(1920, 1);
    saved = saved with { Targets = [saved.Targets[0] with { DevicePath = "old-product", HardwareKey = first }] };
    var current = saved with { Targets = [saved.Targets[0] with { DevicePath = "vrr-product" }] };
    var connected = new[] { new DisplayPathTarget(current.ReadPaths()[0], current.Targets[0]) };
    var remapped = DisplayProfileMatching.Remap(saved, connected);
    Check(remapped.Targets[0].DevicePath == "vrr-product" && remapped.Matches(current));
    var roundTrip = JsonSerializer.Deserialize<DisplayConfiguration>(JsonSerializer.Serialize(saved))!;
    Check(roundTrip.Targets[0].HardwareKey == first);
    Check(DisplayProfileMatching.Remap(roundTrip, connected).Matches(current));

    var duplicatePath = connected[0].Path; duplicatePath.Target.Id++;
    var duplicate = new DisplayPathTarget(duplicatePath, current.Targets[0] with { DevicePath = "another-monitor" });
    try { DisplayProfileMatching.Remap(saved, [connected[0], duplicate]); throw new Exception("Duplicate serial matched"); }
    catch (BrokerException error) { Check(error.Code == "display_monitor_ambiguous"); }
    // Exact connections can still distinguish duplicated manufacturer serials.
    Check(DisplayProfileMatching.Remap(current, [connected[0], duplicate]).Matches(current));
    try { DisplayProfileMatching.Remap(saved, [connected[0] with { Identity = current.Targets[0] with { HardwareKey = other } }]); throw new Exception("Wrong physical monitor matched"); }
    catch (BrokerException error) { Check(error.Code == "display_monitor_missing"); }
    try { DisplayProfileMatching.Remap(saved with { Targets = [saved.Targets[0] with { HardwareKey = null }] }, connected); throw new Exception("Name-only monitor match accepted"); }
    catch (BrokerException error) { Check(error.Code == "display_monitor_missing"); }

    var oldScale = DisplayScaleIdentity.Resolve(["old-product"], saved.Targets);
    var newScale = DisplayScaleIdentity.Resolve(["vrr-product"], current.Targets);
    Check(oldScale is not null && oldScale == newScale, "Scale key did not survive VRR");
    var pair = new[] { current.Targets[0], duplicate.Identity };
    Check(DisplayScaleIdentity.Resolve(["vrr-product"], pair) != DisplayScaleIdentity.Resolve(["another-monitor"], pair),
        "Duplicated serials shared a scale key");
    Check(DisplayScaleIdentity.Resolve(["vrr-product", "another-monitor"], pair) ==
        DisplayScaleIdentity.Resolve(["another-monitor", "vrr-product"], pair), "Clone key depended on order");
    return Task.CompletedTask;
}

static Task Matching()
{
    var saved = Fixture.Setup(1920, 2, clone: true);
    var inventory = saved.ReadPaths().Select((path, index) =>
    {
        path.Source.Adapter = path.Target.Adapter = new(88, 0);
        path.Source.Id += 10; path.Target.Id += 20;
        return new DisplayPathTarget(path, saved.Targets[index]);
    }).Reverse().ToArray();
    var restored = DisplayProfileMatching.Remap(saved, inventory);
    Check(restored.Mode == "Duplicated" && restored.Matches(saved));
    Check(restored.ReadPaths().All(path => path.Source.Adapter.Low == 88 && path.Source.Id == 10));
    Check(restored.ReadModes()[0].Adapter.Low == 88);
    try { DisplayProfileMatching.Remap(saved, inventory[..1]); throw new Exception("Missing monitor accepted"); }
    catch (BrokerException error) { Check(error.Code == "display_monitor_missing"); }
    return Task.CompletedTask;
}
static async Task ProfileIdentityStatus()
{
    using var temp = new TemporaryDirectory();
    var native = new FakeNative();
    native.Current = native.Current with { Targets = [native.Current.Targets[0] with { HardwareKey = new string('A', 64) }] };
    await using var backend = new WindowsDisplayProfilesBackend(temp.Path, native, new FakeLauncher());
    await backend.ChangeDisplayProfileAsync(DisplayProfileCommand.Save, new(Name: "Desk"), Fixture.Identity, default);
    native.Current = native.Current with { Targets = [native.Current.Targets[0] with { DevicePath = "vrr-product" }] };
    var profile = (await backend.GetDisplayProfilesAsync(default)).Profiles.Single();
    Check(profile.MatchesCurrent && profile.Available && profile.UnavailableReason is null,
        "VRR identity change left a false disconnected status");
    Check(native.Applies.Count == 0);
}

static async Task Storage()
{
    using var temp = new TemporaryDirectory();
    var native = new FakeNative();
    var launcher = new FakeLauncher();
    await using var backend = new WindowsDisplayProfilesBackend(temp.Path, native, launcher);
    var state = await backend.ChangeDisplayProfileAsync(DisplayProfileCommand.Save, new(Name: "Desk"), Fixture.Identity, default);
    var profile = state.Profiles.Single();
    Check(profile.MatchesCurrent && native.Applies.Count == 0);
    var path = Path.Combine(temp.Path, "profiles.json");
    var original = File.ReadAllBytes(path);
    File.SetAttributes(path, FileAttributes.ReadOnly);
    await Throws<BrokerException>(() => backend.ChangeDisplayProfileAsync(DisplayProfileCommand.Rename,
        new(profile.Id, "Renamed"), Fixture.Identity, default));
    Check(original.SequenceEqual(File.ReadAllBytes(path)), "Failed save replaced the original file");
    File.SetAttributes(path, FileAttributes.Normal);
    native.Current = Fixture.Setup(1280);
    state = await backend.ChangeDisplayProfileAsync(DisplayProfileCommand.Apply, new(profile.Id), Fixture.Identity, default);
    Check(state.PendingRestore is not null);
    await Throws<BrokerException>(() => backend.ChangeDisplayProfileAsync(DisplayProfileCommand.Keep,
        new(RestoreId: state.PendingRestore!.Id), new("dev.other.widget", "dev.other", "other"), default));
    await backend.ChangeDisplayProfileAsync(DisplayProfileCommand.Revert, new(RestoreId: state.PendingRestore!.Id), Fixture.Identity, default);
    Check(launcher.Session.Decision == false);
    File.WriteAllText(path, "{broken");
    await Throws<BrokerException>(() => backend.ChangeDisplayProfileAsync(DisplayProfileCommand.Save, new(Name: "New"), Fixture.Identity, default));
    Check(File.ReadAllText(path) == "{broken", "Corrupt user data was overwritten");
}
static async Task Guard()
{
    var target = Fixture.Setup(1920);
    foreach (var decision in new[] { "keep", "revert", "eof", "timeout", "late" })
    {
        using var temp = new TemporaryDirectory();
        var native = new FakeNative { Current = Fixture.Setup(1280) };
        var id = Guid.NewGuid().ToString("N");
        var reader = new DecisionReader(JsonSerializer.Serialize(new GuardRequest(id, target)));
        var writer = new GuardWriter();
        var clock = new ManualClock();
        var run = DisplayRestoreGuard.RunCoreAsync(reader, writer, native, clock, TimeSpan.FromSeconds(15),
            request => new DisplayRestoreDiagnosticLog(temp.Path, request.Id, native.Capture));
        await AdvanceUntil(writer.Applied.Task, run, clock);
        if (decision is "timeout" or "late") clock.Advance(TimeSpan.FromSeconds(16));
        if (decision != "timeout") reader.Decision.TrySetResult(decision == "eof" ? null :
            (decision is "keep" or "late" ? "keep:" : "revert:") + id);
        Check(await run.WaitAsync(TimeSpan.FromSeconds(2)) == 0);
        Check(native.Applies.Count == 2);
        Check(native.Applies[0].Width == 1920 && !native.Applies[0].Persist);
        Check(native.Applies[1] == (decision == "keep" ? (1920, true) : (1280, false)), decision);
        var log = File.ReadAllText(Path.Combine(temp.Path, "restore.log"));
        Check(log.Contains(decision == "keep" ? "completed-kept" : "completed-reverted"), "Missing terminal guard diagnostic");
        Check(log.Contains("decision-"), "Missing confirmation outcome");
    }
    var failed = new FakeNative { Current = Fixture.Setup(1280), FailApply = 1 };
    var request = JsonSerializer.Serialize(new GuardRequest(Guid.NewGuid().ToString("N"), target));
    Check(await DisplayRestoreGuard.RunCoreAsync(new DecisionReader(request), new StringWriter(),
        failed, TimeProvider.System, TimeSpan.FromSeconds(15)) == 1);
    Check(failed.Applies.Count == 2 && failed.Applies[1].Width == 1280, "Partial apply did not roll back");
}
static async Task WakeStabilization()
{
    var clock = new ManualClock();
    var baseline = Fixture.Setup(1280);
    var target = Fixture.Setup(1920);
    var native = new FakeNative { Current = baseline };
    var pathsNotReady = 0;
    native.CaptureOverride = () =>
    {
        if (native.Applies.Count != 1) return native.Current;
        var elapsed = clock.GetUtcNow() - DateTimeOffset.UnixEpoch;
        if (elapsed < TimeSpan.FromMilliseconds(500)) return target;
        if (elapsed < TimeSpan.FromSeconds(3)) throw new Win32Exception(1168);
        return baseline;
    };
    native.ConnectedOverride = () =>
    {
        if (native.Applies.Count == 1 && clock.GetUtcNow() - DateTimeOffset.UnixEpoch < TimeSpan.FromSeconds(6))
        { ++pathsNotReady; throw new BrokerException("display_monitor_missing", "Monitor still waking"); }
        var path = target.ReadPaths()[0];
        if (native.Applies.Count > 0) { path.Source.Id = 7; path.Target.Id = 40; }
        return [new DisplayPathTarget(path, target.Targets[0])];
    };
    var id = Guid.NewGuid().ToString("N");
    var reader = new DecisionReader(JsonSerializer.Serialize(new GuardRequest(id, target)));
    var writer = new GuardWriter();
    var run = DisplayRestoreGuard.RunCoreAsync(reader, writer, native, clock, TimeSpan.FromSeconds(15));
    await AdvanceUntil(writer.Applied.Task, run, clock);
    Check(native.Applies.SequenceEqual(new[] { (1920, false), (1920, false) }));
    Check(pathsNotReady > 0, "Wake path rediscovery was not exercised");
    Check(native.AppliedConfigurations[1].ReadPaths()[0].Target.Id == 40, "Retry used stale target IDs");
    var applied = JsonSerializer.Deserialize<GuardReply>(writer.ToString().Trim())!;
    Check(applied.Deadline - clock.GetUtcNow() >= TimeSpan.FromSeconds(14), "Wake time consumed confirmation countdown");
    Check(clock.GetUtcNow() - DateTimeOffset.UnixEpoch >= TimeSpan.FromSeconds(7), "Preview did not settle after wake-up");
    reader.Decision.SetResult("keep:" + id);
    Check(await run.WaitAsync(TimeSpan.FromSeconds(3)) == 0);
    Check(native.Applies.Count == 3 && native.Applies[2] == (1920, true));
}
static async Task WakeFailures()
{
    foreach (var mode in new[] { "never-ready", "second-revert", "parent-exit" })
    {
        var baseline = Fixture.Setup(1280);
        var native = new FakeNative { Current = baseline };
        native.CaptureOverride = () => native.Applies.Count == 0 ? baseline : mode == "never-ready"
            ? throw new Win32Exception(1168) : baseline;
        var reader = new DecisionReader(JsonSerializer.Serialize(new GuardRequest(Guid.NewGuid().ToString("N"), Fixture.Setup(1920))));
        var writer = new GuardWriter();
        var clock = new ManualClock();
        var run = DisplayRestoreGuard.RunCoreAsync(reader, writer, native, clock, TimeSpan.FromSeconds(15));
        if (mode == "parent-exit") reader.Decision.SetResult(null);
        await AdvanceUntil(run, run, clock);
        Check(await run == 1 && !writer.Applied.Task.IsCompleted, "Unstable/abandoned setup offered confirmation");
        Check(native.Applies.All(apply => !apply.Persist) && native.Applies[^1].Width == 1280);
        Check(native.Applies.Count == (mode == "second-revert" ? 3 : 2), "Unbounded retry or missing rollback");
        Check(clock.GetUtcNow() - DateTimeOffset.UnixEpoch <= TimeSpan.FromSeconds(13), "Wake deadline exceeded");
    }
}
static async Task KeepReadDeadline()
{
    var clock = new ManualClock();
    var native = new FakeNative { Current = Fixture.Setup(1280) };
    var id = Guid.NewGuid().ToString("N");
    var reader = new DecisionReader(JsonSerializer.Serialize(new GuardRequest(id, Fixture.Setup(1920))));
    var writer = new GuardWriter();
    var run = DisplayRestoreGuard.RunCoreAsync(reader, writer, native, clock, TimeSpan.FromSeconds(15));
    await AdvanceUntil(writer.Applied.Task, run, clock);
    native.CaptureOverride = () => { clock.Advance(TimeSpan.FromSeconds(16)); return native.Current; };
    reader.Decision.SetResult("keep:" + id);
    Check(await run.WaitAsync(TimeSpan.FromSeconds(3)) == 0);
    Check(native.Applies.SequenceEqual(new[] { (1920, false), (1280, false) }), "Expired read persisted an unconfirmed setup");
}
static async Task AdvanceUntil(Task awaited, Task run, ManualClock clock)
{
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(4));
    while (!awaited.IsCompleted && !run.IsCompleted)
    {
        clock.Advance(TimeSpan.FromMilliseconds(250));
        await Task.Delay(2, timeout.Token);
    }
    Check(awaited.IsCompleted, "Guard completed before the expected phase");
    await awaited;
}
static async Task DiagnosticObservations()
{
    using var temp = new TemporaryDirectory();
    var baseline = Fixture.Setup(1280);
    var target = Fixture.Setup(1920);
    var native = new FakeNative { Current = baseline };
    var id = Guid.NewGuid().ToString("N");
    var reader = new DecisionReader(JsonSerializer.Serialize(new GuardRequest(id, target)));
    var writer = new GuardWriter();
    var path = Path.Combine(temp.Path, "restore.log");
    var run = DisplayRestoreGuard.RunCoreAsync(reader, writer, native, TimeProvider.System, TimeSpan.FromSeconds(15),
        request => new DisplayRestoreDiagnosticLog(temp.Path, request.Id, native.Capture));
    await writer.Applied.Task.WaitAsync(TimeSpan.FromSeconds(3));
    await Until(() => ReadLog().Contains("\"MatchesTarget\":true"));
    native.Current = baseline; // Simulate the OS/driver changing the active setup during confirmation.
    await Until(() => ReadLog().Contains("\"MatchesBaseline\":true,\"MatchesTarget\":false"));
    Check(native.Applies.Count == 1 && !run.IsCompleted, "Observation changed display behavior");
    reader.Decision.SetResult("keep:" + id);
    Check(await run.WaitAsync(TimeSpan.FromSeconds(3)) == 1);
    Check(native.Applies.SequenceEqual(new[] { (1920, false), (1280, false) }), "Keep persisted a changed preview");
    var log = ReadLog();
    Check(log.Contains("\"Trigger\":\"sample\"") && log.Contains("decision-keep") && log.Contains("DisplayPreviewChangedException"));
    Check(writer.ToString().Contains("preview-changed"));
    foreach (var line in log.Split('\n', StringSplitOptions.RemoveEmptyEntries))
    {
        using var json = JsonDocument.Parse(line);
        Check(json.RootElement.GetProperty("Transaction").GetString() == id);
    }
    Check(!log.Contains("monitor-0") && !log.Contains("Monitor 0") && !log.Contains("DevicePath"), "Diagnostic exposed raw identity");
    string ReadLog()
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var input = new StreamReader(stream);
            return input.ReadToEnd();
        }
        catch (IOException) { return ""; }
    }
}
static async Task DiagnosticFailures()
{
    using var temp = new TemporaryDirectory();
    var blockedDirectory = Path.Combine(temp.Path, "file");
    File.WriteAllText(blockedDirectory, "preserve");
    foreach (var mode in new[] { "disk-failure", "capture-failure", "capture-blocked" })
    {
        var native = new FakeNative { Current = Fixture.Setup(1280) };
        var reader = new DecisionReader(JsonSerializer.Serialize(new GuardRequest(Guid.NewGuid().ToString("N"), Fixture.Setup(1920))));
        var writer = new GuardWriter();
        var clock = new ManualClock();
        using var release = new ManualResetEventSlim();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var exited = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        DisplayConfiguration Capture()
        {
            if (mode == "capture-failure") throw new Win32Exception(31, "private diagnostic message");
            if (mode == "capture-blocked") { entered.TrySetResult(); release.Wait(); exited.TrySetResult(); }
            return native.Capture();
        }
        var directory = mode == "disk-failure" ? blockedDirectory : Path.Combine(temp.Path, mode);
        DisplayRestoreDiagnosticLog? diagnostics = null;
        var run = DisplayRestoreGuard.RunCoreAsync(reader, writer, native, clock, TimeSpan.FromSeconds(15),
            request => diagnostics = new DisplayRestoreDiagnosticLog(directory, request.Id, Capture));
        try
        {
            await AdvanceUntil(writer.Applied.Task, run, clock);
            if (mode == "capture-blocked") await entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
            clock.Advance(TimeSpan.FromSeconds(16));
            Check(await run.WaitAsync(TimeSpan.FromSeconds(3)) == 0, mode);
            Check(native.Applies.SequenceEqual(new[] { (1920, false), (1280, false) }), "Diagnostics prevented timeout rollback");
        }
        finally
        {
            release.Set();
            if (mode == "capture-blocked") await exited.Task.WaitAsync(TimeSpan.FromSeconds(3));
            if (diagnostics is not null) await diagnostics.Completion.WaitAsync(TimeSpan.FromSeconds(3));
        }
        if (mode == "capture-failure")
        {
            var log = File.ReadAllText(Path.Combine(directory, "restore.log"));
            Check(log.Contains("readback-failed") && log.Contains("\"NativeError\":31"));
            Check(!log.Contains("private diagnostic message"));
        }
    }
    Check(File.ReadAllText(blockedDirectory) == "preserve");
    var rotation = Path.Combine(temp.Path, "rotation");
    Directory.CreateDirectory(rotation);
    var path = Path.Combine(rotation, "restore.log");
    File.WriteAllText(path, new string('x', DisplayRestoreDiagnosticLog.MaximumFileBytes));
    await using (var log = new DisplayRestoreDiagnosticLog(rotation, Guid.NewGuid().ToString("N"), () => Fixture.Setup()))
        log.Record("rotation-check");
    Check(new FileInfo(path + ".1").Length == DisplayRestoreDiagnosticLog.MaximumFileBytes);
    Check(new FileInfo(path).Length < DisplayRestoreDiagnosticLog.MaximumFileBytes);
}
static async Task Until(Func<bool> condition)
{
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(4));
    while (!condition()) await Task.Delay(20, timeout.Token);
}
static async Task Authority()
{
    using var temp = new TemporaryDirectory();
    var native = new FakeNative();
    await using var provider = new WindowsDisplayProfilesBackend(Path.Combine(temp.Path, "profiles"), native, new FakeLauncher());
    var simulator = new SimulatedPlatformBrokerBackend();
    await using var backend = new CompositePlatformBrokerBackend(simulator, simulator, displays: provider);
    var consent = new ConsentStore(Path.Combine(temp.Path, "consent"));
    await using var broker = new PlatformCapabilityBroker(Fixture.Identity,
        [PlatformCapabilities.DisplaysReadV1, PlatformCapabilities.DisplaysControlV1], consent, backend);
    long sequence = 0;
    Task Send(object payload) => broker.ExecuteAsync(new(BrokerJson.ProtocolVersion, ++sequence, Fixture.Identity,
        PlatformCapabilities.DisplaysControlV1, PlatformCapabilities.DisplayProfilesSave, BrokerJson.ToElement(payload)));
    broker.SetLifecycle(BrokerLifecycleState.Interactive);
    await Throws<BrokerException>(() => Send(new { name = "Desk" }));
    await consent.SetDecisionAsync(Fixture.Identity, PlatformCapabilities.DisplaysControlV1, ConsentDecision.Grant);
    broker.SetLifecycle(BrokerLifecycleState.Visible);
    await Throws<BrokerException>(() => Send(new { name = "Desk" }));
    broker.SetLifecycle(BrokerLifecycleState.Interactive);
    await Throws<BrokerException>(() => Send(new { name = "Desk", resolution = "custom" }));
    await Send(new { name = "Desk" });
    Check(native.Applies.Count == 0);
}
static async Task Widget()
{
    var profileId = Guid.NewGuid().ToString("N");
    var monitor = new WidgetDisplayProfileMonitor("one", "Monitor", 1920, 1080, 0, 0, 60, "Landscape", true);
    var state = new WidgetDisplayProfilesState("Single display", [monitor],
        [new(profileId, "TV gaming", "Single display", [monitor], false, false, "Connect TV.")], null, null);
    var saves = 0;
    var builder = new WidgetTestHostServicesBuilder()
        .WithHandler(WidgetDisplayProfilesCapabilities.Get, (_, _) => ValueTask.FromResult(state))
        .WithEvents(WidgetDisplayProfilesCapabilities.Changed, Array.Empty<WidgetCapabilityAcknowledgement>())
        .WithHandler(WidgetDisplayProfilesCapabilities.Save, (request, _) => { ++saves; Check(request.Name == "Desk"); return ValueTask.FromResult(state); })
        .WithHandler(WidgetDisplayProfilesCapabilities.Delete, (_, _) =>
            throw new WidgetCapabilityException("display_profile_io", "Private native file path"))
        .WithHandler(WidgetDisplayProfilesCapabilities.Apply, (_, _) =>
        {
            state = state with { PendingRestore = new(Guid.NewGuid().ToString("N"), "TV gaming", DateTimeOffset.UtcNow.AddSeconds(15)) };
            return ValueTask.FromResult(state);
        });
    var widget = WidgetTestHost.Attach(new DisplayProfilesWidget(), builder.Build());
    await WidgetTestHost.SetLifecycleStateAsync(widget, WidgetLifecycleState.Interactive);
    ViewSnapshot Snapshot() => widget.Render().CreateSnapshot("display.test", 1);
    IEnumerable<ViewNode> Nodes(ViewNode node) => new[] { node }.Concat(node.Children.SelectMany(Nodes));
    void Valid()
    {
        var snapshot = Snapshot();
        Check(ViewSnapshotValidator.Validate(snapshot).Count == 0, string.Join("; ", ViewSnapshotValidator.Validate(snapshot)));
    }
    Valid();
    var card = Nodes(Snapshot().Root).Single(node => node.Id == "display.profile." + profileId);
    Check(card.IsDisabled != true && card.ContextActions.Count == 3, "Unavailable profile cannot be managed");
    await widget.OnActionAsync(new("apply." + profileId, card.Id));
    Check(Nodes(Snapshot().Root).Any(node => node.Text == "Connect TV."));
    await widget.OnActionAsync(new("delete." + profileId, card.Id));
    await widget.OnActionAsync(new("confirm", "display.confirm"));
    Check(Nodes(Snapshot().Root).Any(node => node.Text?.Contains("Check disk space") == true));
    Check(!Nodes(Snapshot().Root).Any(node => node.Text?.Contains("Private native") == true));
    await widget.OnActionAsync(new("cancel", "display.cancel"));
    await widget.OnActionAsync(new("name-new", "display.save"));
    Check(Snapshot().InitialFocusId == "display.name"); Valid();
    await widget.OnActionAsync(new("commit-name", "display.name") { CommittedText = "Desk" });
    Check(saves == 1); Valid();
    state = state with { Profiles = [state.Profiles[0] with { Available = true, UnavailableReason = null }] };
    await widget.OnActionAsync(new("retry", "display.retry"));
    await widget.OnActionAsync(new("apply." + profileId, card.Id));
    Check(Snapshot().InitialFocusId == "display.revert"); Valid();
    await WidgetTestHost.SetLifecycleStateAsync(widget, WidgetLifecycleState.Background);
    Check(state.PendingRestore is not null, "Widget hiding cancelled independent confirmation");
}
static async Task WidgetWakeOperation()
{
    var profileId = Guid.NewGuid().ToString("N");
    var monitor = new WidgetDisplayProfileMonitor("one", "Monitor", 1920, 1080, 0, 0, 60, "Landscape", true);
    var state = new WidgetDisplayProfilesState("Single display", [monitor],
        [new(profileId, "Desk", "Single display", [monitor], false, true, null)], null, null);
    var release = new TaskCompletionSource<WidgetDisplayProfilesState>(TaskCreationOptions.RunContinuationsAsynchronously);
    var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var gets = 0; var applies = 0;
    var builder = new WidgetTestHostServicesBuilder()
        .WithHandler(WidgetDisplayProfilesCapabilities.Get, async (_, token) =>
            Interlocked.Increment(ref gets) == 1 ? state : await release.Task.WaitAsync(token))
        .WithEvents(WidgetDisplayProfilesCapabilities.Changed, Array.Empty<WidgetCapabilityAcknowledgement>())
        .WithHandler(WidgetDisplayProfilesCapabilities.Apply, async (_, token) =>
        {
            Interlocked.Increment(ref applies);
            entered.TrySetResult();
            return await release.Task.WaitAsync(token);
        });
    var widget = WidgetTestHost.Attach(new DisplayProfilesWidget(), builder.Build());
    await WidgetTestHost.SetLifecycleStateAsync(widget, WidgetLifecycleState.Interactive);
    var action = new WidgetActionEvent("apply." + profileId, "display.profile." + profileId);
    try
    {
        await widget.OnActionAsync(action).AsTask().WaitAsync(TimeSpan.FromMilliseconds(500));
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await widget.OnActionAsync(action);
        Check(applies == 1 && !release.Task.IsCompleted, "Slow restore blocked input or admitted a duplicate");
        await WidgetTestHost.SetLifecycleStateAsync(widget, WidgetLifecycleState.Background);
        await WidgetTestHost.SetLifecycleStateAsync(widget, WidgetLifecycleState.Interactive)
            .AsTask().WaitAsync(TimeSpan.FromMilliseconds(500));
        Check(JsonSerializer.Serialize(widget.Render().CreateSnapshot("display.test", 1)).Contains("display.applying"),
            "Reopening abandoned the admitted restore");
    }
    finally
    {
        release.TrySetResult(state with { PendingRestore = new(Guid.NewGuid().ToString("N"), "Desk", DateTimeOffset.UtcNow.AddSeconds(15)) });
    }
    await Until(() => widget.Render().InitialFocusId == "display.revert");
    Check(ViewSnapshotValidator.Validate(widget.Render().CreateSnapshot("display.test", 1)).Count == 0);
    await WidgetTestHost.SetLifecycleStateAsync(widget, WidgetLifecycleState.Background);
}
static Task NativeRead()
{
    var native = new WindowsDisplayNative();
    var state = native.Capture();
    native.Validate(DisplayProfileMatching.Remap(state, native.ConnectedPaths()));
    Console.WriteLine($"Read-only Windows probe: {state.Targets.Length} displays, {state.Mode}");
    return Task.CompletedTask;
}
static Task Assets()
{
    var root = Path.Combine(Environment.CurrentDirectory, "src/FirstPartyWidgets/DisplayProfilesWidget");
    var manifest = ManifestJson.Deserialize(File.ReadAllBytes(Path.Combine(root, "manifest.json")));
    Check(WidgetManifestValidator.Validate(manifest).Count == 0);
    Check(manifest.Permissions.SequenceEqual([PlatformCapabilities.DisplaysReadV1]));
    Check(manifest.OptionalPermissions.SequenceEqual([PlatformCapabilities.DisplaysControlV1]));
    foreach (var asset in manifest.IconAssets.Values)
        Check(SvgIconNormalizer.Normalize(File.ReadAllBytes(Path.Combine(root, asset.Path))).Bytes.Length > 0);
    var styles = Path.Combine(root, "styles");
    var compiled = WrssThemeCompiler.Compile(WrssPackageLoader.LoadFile(Path.Combine(styles, "default.wrss"), styles));
    Check(compiled.IsValid, string.Join("; ", compiled.Diagnostics));
    return Task.CompletedTask;
}

file static class Fixture
{
    public static readonly BrokerWidgetIdentity Identity = new("dev.test.displays", "dev.test", "default");
    public static DisplayConfiguration Setup(int width = 1920, int count = 1, bool clone = false)
    {
        var paths = new NativePath[count]; var modes = new NativeMode[count * 2]; var targets = new DisplayIdentity[count];
        for (var i = 0; i < count; ++i)
        {
            var sourceId = clone ? 0u : (uint)i;
            paths[i] = new() { Flags = 1,
                Source = new() { Adapter = new(42,0), Id = sourceId, ModeIndex = sourceId * 2 },
                Target = new() { Adapter = new(42,0), Id = (uint)i + 10, ModeIndex = (uint)i * 2 + 1,
                    Rotation = 1, Available = 1, Refresh = new() { Numerator = 60000, Denominator = 1000 } } };
            modes[i * 2] = new() { Type = 1, Id = sourceId, Adapter = new(42,0),
                Source = new() { Width = (uint)width, Height = 1080, PixelFormat = 4, X = (int)sourceId * width } };
            modes[i * 2 + 1] = new() { Type = 2, Id = (uint)i + 10, Adapter = new(42,0), Signal0 = 148500000 };
            targets[i] = new("monitor-" + i, "Monitor " + i);
        }
        return DisplayConfiguration.Create(paths, modes, targets);
    }
}
file sealed class FakeNative : IDisplayNative
{
    public DisplayConfiguration Current = Fixture.Setup();
    public List<(int Width, bool Persist)> Applies = [];
    public List<DisplayConfiguration> AppliedConfigurations = [];
    public Func<DisplayConfiguration>? CaptureOverride;
    public Func<IReadOnlyList<DisplayPathTarget>>? ConnectedOverride;
    public int FailApply;
    public DisplayConfiguration Capture() => CaptureOverride?.Invoke() ?? Current;
    public IReadOnlyList<DisplayPathTarget> ConnectedPaths() => ConnectedOverride?.Invoke() ?? Current.ReadPaths().Select((path, index) => new DisplayPathTarget(path, Current.Targets[index])).ToArray();
    public void Validate(DisplayConfiguration configuration) => configuration.Validate();
    public void Apply(DisplayConfiguration configuration, bool persist)
    {
        Applies.Add((configuration.Summaries()[0].Width, persist));
        AppliedConfigurations.Add(configuration);
        if (Applies.Count == FailApply) throw new Win32Exception(31);
        Current = configuration;
    }
}
file sealed class FakeLauncher : IDisplayRestoreLauncher
{
    public FakeSession Session = new();
    public Task<IDisplayRestoreSession> StartAsync(DisplayConfiguration configuration, CancellationToken token) => Task.FromResult<IDisplayRestoreSession>(Session);
}
file sealed class FakeSession : IDisplayRestoreSession
{
    public DisplayProfilePending Pending { get; } = new(Guid.NewGuid().ToString("N"), DateTimeOffset.UtcNow.AddSeconds(15));
    private readonly TaskCompletionSource<string> _result = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public Task<string> Completion => _result.Task;
    public bool? Decision;
    public Task DecideAsync(bool keep, CancellationToken token) { Decision = keep; _result.TrySetResult(keep ? "kept" : "reverted"); return Task.CompletedTask; }
    public ValueTask DisposeAsync() { _result.TrySetResult("reverted"); return ValueTask.CompletedTask; }
}
file sealed class DecisionReader(string setup) : TextReader
{
    private bool _read;
    public TaskCompletionSource<string?> Decision = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public override Task<string?> ReadLineAsync() { if (_read) return Decision.Task; _read = true; return Task.FromResult<string?>(setup); }
}
file sealed class GuardWriter : StringWriter
{
    public TaskCompletionSource Applied = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public override Task WriteLineAsync(string? value) { if (value?.Contains("applied") == true) Applied.TrySetResult(); return base.WriteLineAsync(value); }
}
file sealed class ManualClock : TimeProvider
{
    private readonly object _gate = new();
    private long _ticks;
    private readonly List<Timer> _timers = [];
    public override long TimestampFrequency => TimeSpan.TicksPerSecond;
    public override long GetTimestamp() => Interlocked.Read(ref _ticks);
    public override DateTimeOffset GetUtcNow() => DateTimeOffset.UnixEpoch.AddTicks(GetTimestamp());
    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        lock (_gate) { var timer = new Timer(callback, state, _ticks + dueTime.Ticks); _timers.Add(timer); return timer; }
    }
    public void Advance(TimeSpan duration)
    {
        Timer[] timers; long now;
        lock (_gate) { now = Interlocked.Add(ref _ticks, duration.Ticks); timers = _timers.ToArray(); }
        foreach (var timer in timers) timer.Fire(now);
    }
    private sealed class Timer(TimerCallback callback, object? state, long due) : ITimer
    {
        private int _disposed;
        public void Fire(long now) { if (now >= due && Interlocked.Exchange(ref _disposed, 1) == 0) callback(state); }
        public bool Change(TimeSpan dueTime, TimeSpan period) => throw new NotSupportedException();
        public void Dispose() => Interlocked.Exchange(ref _disposed, 1);
        public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
    }
}
file sealed class TemporaryDirectory : IDisposable
{
    public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "wrail-displays-test-" + Guid.NewGuid().ToString("N"));
    public TemporaryDirectory() => Directory.CreateDirectory(Path);
    public void Dispose() { if (Directory.Exists(Path)) Directory.Delete(Path, true); }
}
