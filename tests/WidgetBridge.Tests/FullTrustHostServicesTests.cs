using WidgetRail.PlatformBroker;
using WidgetRail.WidgetBridge;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetRuntime;
using WidgetRail.WidgetSdk;

internal static class FullTrustHostServicesTests
{
    internal static async Task RunAsync()
    {
        var root = Path.Combine(Path.GetTempPath(), "wrail-fulltrust-services-" + Guid.NewGuid().ToString("N"));
        var identity = new BrokerWidgetIdentity("example.fulltrust", "example.publisher", "fulltrust-services.instance");
        var consent = new ConsentStore(root);
        var backend = new SimulatedPlatformBrokerBackend { TaskWindows = [new("fixture", "Owned fixture", "Synthetic", false)] };
        string[] capabilities = [PlatformCapabilities.TaskWindowsReadV1, PlatformCapabilities.ProviderDocumentsV1];
        var notification = new TaskCompletionSource<WidgetNotificationCommand>(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var client = new WidgetProcessClient(new WidgetProcessOptions
        {
            ExecutablePath = Environment.ProcessPath!, WidgetInstanceId = identity.InstanceId,
            IsolationPolicy = WidgetWorkerIsolationPolicy.FullTrustCommunity,
            ConnectTimeout = TimeSpan.FromSeconds(5), RequestTimeout = TimeSpan.FromSeconds(5),
            CompanionSessionFactory = context => new BrokerWidgetProcessCompanion(identity.PackageId, identity.PublisherId,
                identity.InstanceId, capabilities, consent, backend, context,
                notifications: (command, _) => { notification.TrySetResult(command); return ValueTask.FromResult(new WidgetNotificationReceipt(true)); })
        });
        try
        {
            await client.SetLifecycleStateAsync(WidgetLifecycleState.Interactive);
            await Probe("probe", "denied");
            foreach (var cap in capabilities) await consent.SetDecisionAsync(identity, cap, ConsentDecision.Grant);
            await Probe("probe", "allowed");
            await client.SendActionAsync(new("notify", "notify"));
            await client.SetLifecycleStateAsync(WidgetLifecycleState.Background);
            var delivered = await notification.Task.WaitAsync(TimeSpan.FromSeconds(5));
            if (delivered.Notification?.Title != "Background fixture") throw new InvalidOperationException("Full-trust notification did not cross its authenticated broker.");
            await client.SetLifecycleStateAsync(WidgetLifecycleState.Interactive);
            await Probe("probe", "allowed");
            await Probe("undeclared", "denied");
            await consent.SetDecisionAsync(identity, capabilities[0], ConsentDecision.Deny);
            await Probe("probe", "denied");
            await client.StopAsync();
            await consent.SetDecisionAsync(identity, capabilities[0], ConsentDecision.Grant);
            await client.SetLifecycleStateAsync(WidgetLifecycleState.Interactive);
            await Probe("probe", "allowed");
        }
        finally { await client.StopAsync(); if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
        async Task Probe(string action, string expected)
        {
            var deadline = Environment.TickCount64 + 5000;
            do
            {
                var before = (await client.GetSnapshotAsync()).Root.Children.Single(node => node.Id == "status").Text;
                await client.SendActionAsync(new(action, action));
                await Task.Delay(60);
                var snapshot = await client.GetSnapshotAsync();
                var current = snapshot.Root.Children.Single(node => node.Id == "status").Text;
                if (current != before && current?.StartsWith(expected + ":", StringComparison.Ordinal) == true) return;
            } while (Environment.TickCount64 < deadline);
            throw new InvalidOperationException("Full-trust host-service result did not become " + expected);
        }
    }

    internal sealed class ProbeWidget : Widget
    {
        private readonly WidgetModel<string> status;
        private int revision;
        public ProbeWidget() => status = CreateModel("ready");
        public override WidgetView Render() => new(UI.Stack("root", UI.Text(status.Value, "status"),
            UI.Button("Probe", "probe", "probe"), UI.Button("Undeclared", "undeclared", "undeclared"), UI.Button("Notify", "notify", "notify")));
        public override async ValueTask OnActionAsync(WidgetActionEvent action, CancellationToken token = default)
        {
            if (action.ActionId == "notify")
            {
                Operations.RunSingleFlight("notification", async context =>
                {
                    await Task.Delay(500, context.CancellationToken);
                    await HostServices.Notifications.ShowAsync(new("background", "Background fixture")
                    { Retention = WidgetNotificationRetention.BannerOnly }, context.CancellationToken);
                }, WidgetOperationLifetime.Widget);
                return;
            }
            try
            {
                if (action.ActionId == "undeclared")
                    await HostServices.PrivateSecrets.ExistsAsync("fixture", token);
                else
                {
                    var windows = await HostServices.TaskSwitcher.GetWindowsAsync(token);
                    if (windows.Count != 1 || windows[0].ApplicationName != "Owned fixture") throw new InvalidOperationException("Wrong window context.");
                    var document = await HostServices.Documents.CreateAsync(["<style>\r\n\t.card { color: red; }\r\n</style>\n<p>Fixture 日本語 🎮</p>"], token);
                    await HostServices.Documents.DiscardAsync(document, token);
                }
                status.Update(_ => "allowed:" + Interlocked.Increment(ref revision));
            }
            catch (WidgetCapabilityException) { status.Update(_ => "denied:" + Interlocked.Increment(ref revision)); }
        }
    }
}
