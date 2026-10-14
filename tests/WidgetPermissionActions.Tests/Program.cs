using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;
using WidgetRail.Widgets;

var count = 0;
foreach (var outcome in new[] { WidgetPermissionOutcome.Granted, WidgetPermissionOutcome.Denied, WidgetPermissionOutcome.Cancelled, WidgetPermissionOutcome.Unavailable })
{
    var fixture = new Fixture();
    var widget = await fixture.Open();
    await widget.Act();
    await Until(() => fixture.Requests == 1);
    Check(widget.Executions == 0, "Action executed before consent");
    await widget.Act();
    Check(fixture.Requests == 1, "Repeated input created another prompt");
    fixture.Outcome = outcome;
    await Until(() => !widget.Busy);
    Check(widget.Executions == (outcome == WidgetPermissionOutcome.Granted ? 1 : 0), "Wrong post-consent action count");
    Check(outcome == WidgetPermissionOutcome.Granted || widget.Feedback is not null, "Missing denial/cancel feedback");
    await WidgetTestHost.DestroyAsync(widget);
    Console.WriteLine("PASS " + outcome); count++;
}
{
    var fixture = new Fixture { Granted = true }; var widget = await fixture.Open();
    await widget.Act(); Check(widget.Executions == 1 && fixture.Requests == 0, "Granted action prompted or was blocked");
    fixture.Granted = false; await widget.Act(); await Until(() => fixture.Requests == 1);
    Check(widget.Executions == 1, "Revoked access reused a stale grant");
    await WidgetTestHost.SetLifecycleStateAsync(widget, WidgetLifecycleState.Background);
    await Until(() => !widget.Busy);
    Check(fixture.Cancellations == 1 && widget.Executions == 1, "Leaving widget did not cancel pending consent");
    await widget.Act();
    Check(fixture.Requests == 1 && widget.Executions == 1 && widget.Feedback is not null, "Inactive action was admitted or silently dropped");
    await WidgetTestHost.DestroyAsync(widget); Console.WriteLine("PASS current grants, revocation and lifecycle cancellation"); count++;
}
{
    var fixture = new Fixture(); var widget = await fixture.Open();
    await widget.Act([]); Check(widget.Executions == 1 && fixture.Checks == 0, "Local navigation requested permission");
    await WidgetTestHost.DestroyAsync(widget); Console.WriteLine("PASS local navigation bypasses consent"); count++;
}
Console.WriteLine($"Passed: {count}, Failed: 0");
static void Check(bool result, string message) { if (!result) throw new Exception(message); }
static async Task Until(Func<bool> ready)
{
    using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
    while (!ready()) await Task.Delay(10, deadline.Token);
}
sealed class Fixture
{
    internal volatile bool Granted;
    internal volatile WidgetPermissionOutcome Outcome = WidgetPermissionOutcome.Pending;
    internal int Checks, Requests, Cancellations;
    private readonly string ticket = new('a', 32);
    internal async Task<Probe> Open()
    {
        var services = new WidgetTestHostServicesBuilder()
            .WithHandler(WidgetPermissionCapabilities.Check, (query, _) => {
                Interlocked.Increment(ref Checks);
                return ValueTask.FromResult(new WidgetPermissionStatuses(query.CapabilityIds.Select(id => new WidgetPermissionStatus(id,
                    Granted ? WidgetPermissionState.Granted : WidgetPermissionState.NotDecided)).ToArray())); })
            .WithHandler(WidgetPermissionCapabilities.Request, (_, _) => { Interlocked.Increment(ref Requests); return ValueTask.FromResult(new WidgetPermissionRequestState(WidgetPermissionOutcome.Pending, ticket)); })
            .WithHandler(WidgetPermissionCapabilities.Status, (_, _) => ValueTask.FromResult(new WidgetPermissionRequestState(Outcome, ticket)))
            .WithHandler(WidgetPermissionCapabilities.Cancel, (_, _) => { Interlocked.Increment(ref Cancellations); return ValueTask.FromResult(new WidgetCapabilityAcknowledgement(true)); }).Build();
        var widget = WidgetTestHost.Attach(new Probe(), services);
        await WidgetTestHost.SetLifecycleStateAsync(widget, WidgetLifecycleState.Visible);
        await WidgetTestHost.SetLifecycleStateAsync(widget, WidgetLifecycleState.Interactive);
        return widget;
    }
}
sealed class Probe : Widget
{
    internal int Executions;
    internal string? Feedback;
    internal bool Busy => Operations.IsBusy("permissions.action");
    public override WidgetView Render() => new(UI.Text("Permission fixture", "permission.fixture"));
    internal ValueTask Act(string[]? ids = null) => WidgetPermissionAction.RunAsync(HostServices.Permissions, Operations,
        ids ?? ["system.performance.fps.control.v1"], _ => { Interlocked.Increment(ref Executions); return ValueTask.CompletedTask; },
        text => Feedback = text, default);
}
