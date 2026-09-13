using System.ComponentModel;
using System.Text.Json;
using WidgetRail.FirstPartyWidgets.Power;
using WidgetRail.PlatformBroker;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;
using WidgetRail.WidgetStyling;
using WidgetRail.WindowsPowerProvider;

var tests = new (string, Func<Task>)[]
{
    ("Confirmation, cancel, matching action and return to tray", Confirm),
    ("Confirmation cannot survive hiding or switch to another command", Lifecycle),
    ("Sleep acts immediately; duplicate requests do not queue", Sleep),
    ("Unavailable options and failed reads remain usable", Availability),
    ("Power failures are themed and clear the busy state", Failure),
    ("Broker requires consent, interactive state and an empty payload", Authority),
    ("Native commands are non-forcing, scoped and cancellation aware", Native),
    ("Shipped manifest, icons and style validate", Assets),
};
foreach (var (name, run) in tests) { await run(); Console.WriteLine("PASS " + name); }
Console.WriteLine($"Passed: {tests.Length}, Failed: 0, Skipped: 0");
return;

static async Task Confirm()
{
    var fake = new Fake(); var widget = fake.Create(); await Open(widget);
    Check(Snapshot(widget).InitialFocusId == "power.sleep");
    foreach (var command in new[] { "shutdown", "restart" })
    {
        await Action(widget, "confirm." + command); Check(fake.Commands.Count == 0);
        await Action(widget, command);
        Check(fake.Commands.Count == 0 && Snapshot(widget).InitialFocusId == "power.cancel");
        Check(Snapshot(widget).Root.Shortcuts.Single().ActionId == "cancel");
        await Action(widget, "cancel"); Check(Snapshot(widget).ActiveInputScopeId == "power");
    }
    await Action(widget, "shutdown"); await Action(widget, "confirm.restart"); Check(fake.Commands.Count == 0);
    await Action(widget, "confirm.shutdown"); Check(fake.Commands.SequenceEqual(["shutdown"]));
    await Action(widget, "confirm.shutdown"); Check(fake.Commands.Count == 1);
    await Action(widget, "restart"); await Action(widget, "confirm.restart");
    Check(fake.Commands.SequenceEqual(["shutdown", "restart"]));
    Check(!await widget.OnControllerInputAsync(new(ControllerButton.B, ControllerEventPhase.Pressed, ControllerInputContext.OpenWidget)));
    await Close(widget);
}

static async Task Lifecycle()
{
    var fake = new Fake(); var widget = fake.Create(); await Open(widget);
    await Action(widget, "shutdown"); await Action(widget, "restart"); await Action(widget, "sleep");
    Check(fake.Commands.Count == 0 && Snapshot(widget).ActiveInputScopeId == "power.confirm.shutdown");
    await Close(widget); await Open(widget); await Action(widget, "confirm.shutdown");
    Check(fake.Commands.Count == 0 && Snapshot(widget).ActiveInputScopeId == "power");
    await Close(widget);
}

static async Task Sleep()
{
    var fake = new Fake { Pending = new(TaskCreationOptions.RunContinuationsAsynchronously) };
    var widget = fake.Create(); await Open(widget);
    var first = Action(widget, "sleep");
    await Action(widget, "sleep"); await Action(widget, "shutdown");
    Check(fake.Commands.SequenceEqual(["sleep"]));
    fake.Pending.SetResult(new(true)); await first; await Close(widget);
}

static async Task Availability()
{
    var fake = new Fake { Availability = new(false, true, false) }; var widget = fake.Create(); await Open(widget);
    Check(Snapshot(widget).InitialFocusId == "power.restart");
    await Action(widget, "sleep"); await Action(widget, "shutdown"); Check(fake.Commands.Count == 0);
    Check(Nodes(Snapshot(widget).Root).Single(x => x.Id == "power.sleep").IsDisabled == true);
    fake.ReadError = true; await Action(widget, "check");
    Check(Snapshot(widget).InitialFocusId == "power.check");
    fake.ReadError = false; fake.Availability = new(true, true, true); await Action(widget, "check");
    Check(Snapshot(widget).InitialFocusId == "power.sleep"); await Close(widget);
}

static async Task Failure()
{
    var fake = new Fake { Error = new WidgetCapabilityException("power_denied", "Sensitive native failure") };
    var widget = fake.Create(); await Open(widget); await Action(widget, "restart"); await Action(widget, "confirm.restart");
    var nodes = Nodes(Snapshot(widget).Root).ToArray();
    Check(nodes.Single(x => x.Id == "power.toast").StyleClasses.Contains("wrail-toast--danger"));
    Check(!nodes.Any(x => x.Text?.Contains("Sensitive") == true));
    fake.Error = null; await Action(widget, "sleep"); Check(fake.Commands.SequenceEqual(["sleep"])); await Close(widget);
}

static async Task Authority()
{
    var directory = Path.Combine(Path.GetTempPath(), "wrail-power-test-" + Guid.NewGuid().ToString("N"));
    try
    {
        var native = new FakeNative();
        var provider = new WindowsPowerPlatformBackend(native);
        var simulator = new SimulatedPlatformBrokerBackend();
        await using var backend = new CompositePlatformBrokerBackend(simulator, simulator, power: provider);
        var identity = new BrokerWidgetIdentity("dev.test.power", "dev.test", "default");
        var consent = new ConsentStore(directory);
        await using var broker = new PlatformCapabilityBroker(identity,
            [PlatformCapabilities.PowerReadV1, PlatformCapabilities.PowerControlV1], consent, backend);
        broker.SetLifecycle(BrokerLifecycleState.Interactive);
        long sequence = 0;
        Task Send(string operation, object payload) => broker.ExecuteAsync(new(BrokerJson.ProtocolVersion,
            ++sequence, identity, PlatformCapabilities.PowerControlV1, operation,
            JsonSerializer.SerializeToElement(payload)));
        await Throws<BrokerException>(() => Send(PlatformCapabilities.PowerShutDown, new {}));
        Check(native.Writes == 0);
        await consent.SetDecisionAsync(identity, PlatformCapabilities.PowerControlV1, ConsentDecision.Grant);
        broker.SetLifecycle(BrokerLifecycleState.Visible);
        await Throws<BrokerException>(() => Send(PlatformCapabilities.PowerShutDown, new {}));
        broker.SetLifecycle(BrokerLifecycleState.Interactive);
        await Throws<BrokerException>(() => Send(PlatformCapabilities.PowerShutDown, new { force = true }));
        await Throws<BrokerException>(() => Send("power.invalid", new {})); Check(native.Writes == 0);
        await Send(PlatformCapabilities.PowerRestart, new {}); Check(native.Writes == 1 && native.Flags == 2);
        await consent.SetDecisionAsync(identity, PlatformCapabilities.PowerControlV1, ConsentDecision.Deny);
        await Throws<BrokerException>(() => Send(PlatformCapabilities.PowerSleep, new {})); Check(native.Writes == 1);
    }
    finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
}

static async Task Native()
{
    var fake = new FakeNative(); var provider = new WindowsPowerPlatformBackend(fake);
    Check((await provider.GetPowerAvailabilityAsync(default)).CanSleep && fake.Writes == 0 && !fake.Privileged);
    await provider.ExecutePowerAsync(PowerCommand.ShutDown, default); Check(fake.Flags == 8 && !fake.Privileged);
    await provider.ExecutePowerAsync(PowerCommand.Restart, default); Check(fake.Flags == 2 && !fake.Privileged);
    await provider.ExecutePowerAsync(PowerCommand.Sleep, default); Check(fake.Slept && !fake.Privileged);
    var count = fake.Writes;
    using var canceled = new CancellationTokenSource(); canceled.Cancel();
    await Throws<OperationCanceledException>(() => provider.ExecutePowerAsync(PowerCommand.ShutDown, canceled.Token));
    await Throws<BrokerException>(() => provider.ExecutePowerAsync((PowerCommand)99, default));
    fake.CanSleep = false;
    await Throws<BrokerException>(() => provider.ExecutePowerAsync(PowerCommand.Sleep, default));
    Check(fake.Writes == count);
    fake.Error = new Win32Exception(1314);
    var error = await Throws<BrokerException>(() => provider.ExecutePowerAsync(PowerCommand.Restart, default));
    Check(error.Code == "power_denied" && !fake.Privileged);
    Check(!(await provider.GetPowerAvailabilityAsync(default)).CanShutDown);
    fake.Error = null; await provider.ExecutePowerAsync(PowerCommand.Restart, default);
    // Read-only native probe: tests never invoke real ExitWindowsEx or SetSuspendState.
    var actual = await new WindowsPowerPlatformBackend().GetPowerAvailabilityAsync(default);
    Console.WriteLine($"Windows availability: shutdown={actual.CanShutDown}, restart={actual.CanRestart}, sleep={actual.CanSleep}");
}

static async Task Assets()
{
    var root = Path.Combine(Environment.CurrentDirectory, "src/FirstPartyWidgets/PowerWidget");
    var manifest = ManifestJson.Deserialize(await File.ReadAllBytesAsync(Path.Combine(root, "manifest.json")));
    Check(WidgetManifestValidator.Validate(manifest).Count == 0);
    Check(manifest.Permissions.SequenceEqual(["system.power.read.v1"]));
    Check(manifest.OptionalPermissions.SequenceEqual(["system.power.control.v1"]));
    var compiled = WrssThemeCompiler.Compile(WrssPackageLoader.LoadFile(Path.Combine(root, "styles/default.wrss"),
        Path.Combine(root, "styles")));
    Check(compiled.IsValid, string.Join("\n", compiled.Diagnostics));
}

static ValueTask Action(PowerWidget widget, string action) => widget.OnActionAsync(new(action, "power.test"));
static Task Open(PowerWidget widget) => WidgetTestHost.SetLifecycleStateAsync(widget, WidgetLifecycleState.Visible).AsTask();
static Task Close(PowerWidget widget) => WidgetTestHost.SetLifecycleStateAsync(widget, WidgetLifecycleState.Background).AsTask();
static ViewSnapshot Snapshot(PowerWidget widget) => widget.Render().CreateSnapshot("power.test", 1);
static IEnumerable<ViewNode> Nodes(ViewNode root) => new[] { root }.Concat(root.Children.SelectMany(Nodes));
static void Check(bool condition, string message = "Power assertion failed") { if (!condition) throw new Exception(message); }
static async Task<T> Throws<T>(Func<Task> run) where T : Exception
{
    try { await run(); } catch (T error) { return error; }
    throw new Exception("Expected " + typeof(T).Name);
}

file sealed class Fake
{
    public WidgetPowerAvailability Availability = new(true, true, true);
    public bool ReadError;
    public Exception? Error;
    public TaskCompletionSource<WidgetCapabilityAcknowledgement>? Pending;
    public List<string> Commands = [];
    private ValueTask<WidgetCapabilityAcknowledgement> Send(string command)
    {
        if (Error is not null) throw Error;
        Commands.Add(command);
        return Pending is null ? ValueTask.FromResult(new WidgetCapabilityAcknowledgement(true)) : new(Pending.Task);
    }
    public PowerWidget Create() => WidgetTestHost.Attach(new PowerWidget(), new WidgetTestHostServicesBuilder()
        .WithHandler(WidgetPowerCapabilities.Get, (_, _) => ReadError ?
            throw new WidgetCapabilityException("permission_denied", "denied") : ValueTask.FromResult(Availability))
        .WithHandler(WidgetPowerCapabilities.ShutDown, (_, _) => Send("shutdown"))
        .WithHandler(WidgetPowerCapabilities.Restart, (_, _) => Send("restart"))
        .WithHandler(WidgetPowerCapabilities.Sleep, (_, _) => Send("sleep")).Build());
}

file sealed class FakeNative : IPowerNative
{
    public bool CanSleep { get; set; } = true;
    public bool Privileged;
    public bool Slept;
    public uint Flags;
    public int Writes;
    public Exception? Error;
    public void WithShutdownPrivilege(Action action)
    {
        Privileged = true;
        try { if (Error is not null) throw Error; action(); }
        finally { Privileged = false; }
    }
    public void ExitWindows(uint flags, uint reason)
    {
        if (!Privileged || (flags != 2 && flags != 8) || reason != 0x80040000) throw new Exception("Unsafe native call");
        Flags = flags; Writes++;
    }
    public void Sleep() { if (!Privileged) throw new Exception("No privilege"); Slept = true; Writes++; }
}
