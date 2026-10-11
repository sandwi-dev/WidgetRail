using WidgetRail.WidgetSdk;
using WidgetRail.WidgetProtocol;

internal static class PermissionServiceTests
{
    internal static async Task Contract()
    {
        var id = new string('a', 32); var polls = 0; var cancel = 0; var outcome = WidgetPermissionOutcome.Pending;
        var client = new WidgetTestHostServicesBuilder()
            .WithResponse(WidgetPermissionCapabilities.Request, new WidgetPermissionRequestState(WidgetPermissionOutcome.Pending, id))
            .WithHandler(WidgetPermissionCapabilities.Status, (request, _) => {
                if (request.RequestId != id) throw new Exception("Foreign request ticket");
                polls++; return ValueTask.FromResult(new WidgetPermissionRequestState(outcome, id)); })
            .WithHandler(WidgetPermissionCapabilities.Cancel, (request, _) => { if (request.RequestId != id) throw new Exception("Foreign cancel"); cancel++; return ValueTask.FromResult(new WidgetCapabilityAcknowledgement(true)); }).Build();
        var widget = WidgetTestHost.Attach(new Probe(), client);
        var request = widget.Request(default);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (polls == 0) await Task.Delay(10, deadline.Token);
        if (request.IsCompleted) throw new Exception("Caller continued without consent");
        outcome = WidgetPermissionOutcome.Granted;
        if (await request != WidgetPermissionOutcome.Granted || cancel != 0) throw new Exception("Consent did not resume caller");
        outcome = WidgetPermissionOutcome.Pending;
        using var cancelled = new CancellationTokenSource();
        var abandoned = widget.Request(cancelled.Token); cancelled.Cancel();
        try { await abandoned; throw new Exception("Cancelled request returned a grant"); } catch (OperationCanceledException) { }
        if (cancel != 1) throw new Exception("Abandoned dialog was not cancelled");
        if (!new WidgetPermissionQuery(["network.loopback:13091"]).IsWellFormed() || new WidgetPermissionQuery(["x", "x"]).IsWellFormed()) throw new Exception("Permission identifier bounds changed");
        await WidgetTestHost.DestroyAsync(widget);
    }
    private sealed class Probe : Widget
    {
        public override WidgetView Render() => new(UI.Text("Permission test", "test"));
        internal Task<WidgetPermissionOutcome> Request(CancellationToken token) => HostServices.Permissions.RequestAsync([WidgetCaptureLibraryCapabilities.ReadPermission], token).AsTask();
    }
}
