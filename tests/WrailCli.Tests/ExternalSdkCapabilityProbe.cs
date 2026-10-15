using WidgetRail.PlatformBroker;
using WidgetRail.WidgetRuntime;
using WidgetRail.WidgetSdk;

internal static class ExternalSdkCapabilityProbe
{
    // Compiled exclusively against the exported package, never repository projects.
    internal const string Source = """
        using WidgetRail.WidgetRuntime;
        using WidgetRail.WidgetSdk;

        return await WidgetApplicationBootstrap.RunAsync(args, () => new CapabilityProbe());

        sealed class CapabilityProbe : Widget
        {
            private readonly WidgetModel<string> status;
            private int revision;
            public CapabilityProbe() => status = CreateModel("ready");
            public override WidgetView Render() => new(UI.Stack("root",
                UI.Text(status.Value, "status"), UI.Button("Read", "read", "read"),
                UI.Button("Undeclared", "undeclared", "undeclared")));
            public override async ValueTask OnActionAsync(WidgetActionEvent action,
                CancellationToken token = default)
            {
                string result;
                try
                {
                    if (action.ActionId == "undeclared")
                        await HostServices.PrivateSecrets.ExistsAsync("fixture", token);
                    else
                    {
                        var windows = await HostServices.TaskSwitcher.GetWindowsAsync(token);
                        if (windows.Count != 1 || windows[0].ApplicationName != "Synthetic fixture")
                            throw new InvalidOperationException("Unexpected synthetic response.");
                    }
                    result = "allowed";
                }
                catch (WidgetCapabilityException) { result = "denied"; }
                status.Update(_ => result + ":" + ++revision);
            }
        }
        """;

    internal static async Task RunAsync(string executable, string consentRoot)
    {
        var identity = new BrokerWidgetIdentity("dev.external.capabilities", "dev.external", "external.capabilities");
        var consent = new ConsentStore(consentRoot);
        var backend = new SimulatedPlatformBrokerBackend
        {
            TaskWindows = [new("synthetic", "Synthetic fixture", "Fixture", false)],
        };
        await using var client = new WidgetProcessClient(new WidgetProcessOptions
        {
            ExecutablePath = executable,
            WidgetInstanceId = identity.InstanceId,
            IsolationPolicy = WidgetWorkerIsolationPolicy.FullTrustCommunity,
            MaximumRestartAttempts = 1,
            ConnectTimeout = TimeSpan.FromSeconds(10),
            RequestTimeout = TimeSpan.FromSeconds(5),
            CompanionSessionFactory = _ => new Companion(identity, consent, backend),
        });
        await client.SetLifecycleStateAsync(WidgetLifecycleState.Interactive);
        await ProbeAsync("read", "denied");
        await consent.SetDecisionAsync(identity, PlatformCapabilities.TaskWindowsReadV1, ConsentDecision.Grant);
        await ProbeAsync("read", "allowed");
        await ProbeAsync("undeclared", "denied");
        await consent.SetDecisionAsync(identity, PlatformCapabilities.TaskWindowsReadV1, ConsentDecision.Deny);
        await ProbeAsync("read", "denied");
        await client.StopAsync();
        await consent.SetDecisionAsync(identity, PlatformCapabilities.TaskWindowsReadV1, ConsentDecision.Grant);
        await client.SetLifecycleStateAsync(WidgetLifecycleState.Interactive);
        await ProbeAsync("read", "allowed");
        await client.StopAsync();

        async Task ProbeAsync(string action, string expected)
        {
            var before = (await client.GetSnapshotAsync()).Root.Children.Single(node => node.Id == "status").Text;
            await client.SendActionAsync(new(action, action));
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            while (true)
            {
                var current = (await client.GetSnapshotAsync(timeout.Token)).Root.Children.Single(node => node.Id == "status").Text;
                if (current != before)
                {
                    if (current?.StartsWith(expected + ":", StringComparison.Ordinal) != true)
                        throw new InvalidOperationException($"External capability {action}: expected {expected}, received {current}.");
                    return;
                }
                await Task.Delay(20, timeout.Token);
            }
        }
    }

    private sealed class Companion : IWidgetProcessCompanionSession
    {
        private readonly BrokerPipeServer server;
        internal Companion(BrokerWidgetIdentity identity, ConsentStore consent, IPlatformBrokerBackend backend)
        {
            var pipe = "wrail-external-sdk-" + Guid.NewGuid().ToString("N");
            server = new BrokerPipeServer(pipe, identity, [PlatformCapabilities.TaskWindowsReadV1], consent, backend);
            WorkerArguments = ["--broker-pipe", pipe, "--broker-package", identity.PackageId,
                "--broker-publisher", identity.PublisherId, "--broker-instance", identity.InstanceId,
                "--broker-nonce", server.ChannelNonce];
        }
        public IReadOnlyList<string> WorkerArguments { get; }
        public void BindWorkerProcess(int processId) => server.BindExpectedClientProcess(processId);
        public Task RunAsync(CancellationToken token) => server.RunAsync(token);
        public Task SetLifecycleStateAsync(WidgetLifecycleState state, CancellationToken token = default)
        {
            token.ThrowIfCancellationRequested();
            server.SetLifecycle(state switch
            {
                WidgetLifecycleState.Created => BrokerLifecycleState.Created,
                WidgetLifecycleState.Background => BrokerLifecycleState.Background,
                WidgetLifecycleState.Visible => BrokerLifecycleState.Visible,
                WidgetLifecycleState.Interactive => BrokerLifecycleState.Interactive,
                _ => BrokerLifecycleState.Destroying,
            });
            return Task.CompletedTask;
        }
        public ValueTask DisposeAsync() => server.DisposeAsync();
    }
}
