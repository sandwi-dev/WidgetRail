using WidgetRail.PlatformBroker;

internal static class DisplayRestorePipeTests
{
    internal static async Task RunAsync()
    {
        var directory = Path.Combine(Path.GetTempPath(), "wrail-display-pipe-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var identity = new BrokerWidgetIdentity("display.test", "display.test", "display.instance");
            var store = new ConsentStore(directory);
            await store.SetDecisionAsync(identity, PlatformCapabilities.DisplaysControlV1, ConsentDecision.Grant);
            await store.SetDecisionAsync(identity, PlatformCapabilities.DisplaysReadV1, ConsentDecision.Grant);
            var simulator = new SimulatedPlatformBrokerBackend();
            var displays = new SlowDisplays();
            await using var backend = new CompositePlatformBrokerBackend(simulator, simulator, displays: displays);
            var options = new BrokerPipeTransportOptions { RequestTimeout = TimeSpan.FromMilliseconds(50) };
            var pipe = "wrail-display-pipe-" + Guid.NewGuid().ToString("N");
            await using var server = new BrokerPipeServer(pipe, identity,
                [PlatformCapabilities.DisplaysReadV1, PlatformCapabilities.DisplaysControlV1], store, backend,
                options, new string('D', 64));
            var running = server.RunAsync();
            await using var client = new BrokerPipeClient(pipe, identity, server.ChannelNonce, options);
            await client.ConnectAsync();
            server.SetLifecycle(BrokerLifecycleState.Interactive);
            foreach (var operation in new[] { PlatformCapabilities.DisplayProfilesGet, PlatformCapabilities.DisplayProfilesApply,
                         PlatformCapabilities.DisplayProfilesKeep, PlatformCapabilities.DisplayProfilesRevert })
            {
                object payload = operation == PlatformCapabilities.DisplayProfilesGet ? new { } :
                    operation == PlatformCapabilities.DisplayProfilesApply ? new DisplayProfileRequest(ProfileId: Guid.NewGuid().ToString("N")) :
                    new DisplayProfileRequest(RestoreId: Guid.NewGuid().ToString("N"));
                var result = await client.RequestAsync(operation == PlatformCapabilities.DisplayProfilesGet
                    ? PlatformCapabilities.DisplaysReadV1 : PlatformCapabilities.DisplaysControlV1, operation, payload);
                if (!result.Succeeded) throw new Exception("Slow display operation failed: " + result.ErrorCode);
            }
            foreach (var pair in new[] {
                (PlatformCapabilities.DisplaysControlV1, PlatformCapabilities.DisplayProfilesSave),
                (PlatformCapabilities.DisplaysControlV1, PlatformCapabilities.DisplayProfilesGet),
                (PlatformCapabilities.DisplaysReadV1, PlatformCapabilities.DisplayProfilesApply) })
            {
                var request = new BrokerRequestEnvelope(BrokerJson.ProtocolVersion, 1, identity, pair.Item1, pair.Item2, BrokerJson.ToElement(new { }));
                if (BrokerPipeRequestTimeoutPolicy.Resolve(options, request) != options.RequestTimeout)
                    throw new Exception("Display restore deadline leaked to an unrelated operation");
            }
            await using var subscription = await client.SubscribeAsync(
                PlatformCapabilities.DisplaysReadV1, PlatformCapabilities.DisplayProfilesChanged);
            displays.State = displays.State with { PendingRestore = new(Guid.NewGuid().ToString("N"), "Desk", DateTimeOffset.UtcNow.AddSeconds(-1)) };
            // The deadline reconciliation can run while native rollback is still
            // finishing. Its read legitimately returns an expired pending entry.
            var waiting = await client.RequestAsync(PlatformCapabilities.DisplaysReadV1, PlatformCapabilities.DisplayProfilesGet, new { });
            if (waiting.Payload is not { } waitingState || !waitingState.TryGetProperty("pendingRestore", out var pending) ||
                pending.ValueKind == System.Text.Json.JsonValueKind.Null)
                throw new Exception("Rollback-pending race was not exercised");
            displays.FinishRollback();
            var changed = await subscription.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(2));
            if (changed.Payload.EnumerateObject().Count() != 1 || !changed.Payload.GetProperty("acknowledged").GetBoolean())
                throw new Exception("Display event leaked provider data or did not acknowledge invalidation");
            var restored = await client.RequestAsync(PlatformCapabilities.DisplaysReadV1, PlatformCapabilities.DisplayProfilesGet, new { });
            if (restored.Payload is not { } restoredState ||
                (restoredState.TryGetProperty("pendingRestore", out var stillPending) && stillPending.ValueKind != System.Text.Json.JsonValueKind.Null) ||
                restoredState.GetProperty("outcome").GetString() != "reverted")
                throw new Exception("Rollback event did not lead to the terminal display state");
            await subscription.DisposeAsync();
            await client.DisposeAsync();
            await running.WaitAsync(TimeSpan.FromSeconds(2));
        }
        finally { Directory.Delete(directory, true); }
    }

    private sealed class SlowDisplays : IDisplayProfilesPlatformBackend
    {
        public event EventHandler<BrokerPlatformEvent>? EventPublished;
        public DisplayProfilesState State = new("Single display", [], [], null, null);
        public void FinishRollback()
        {
            State = State with { PendingRestore = null, Outcome = "reverted" };
            EventPublished?.Invoke(this, new(PlatformCapabilities.DisplaysReadV1, PlatformCapabilities.DisplayProfilesChanged,
                new { acknowledged = true, privateDevicePath = "must-not-cross-broker" }));
        }
        public async Task<DisplayProfilesState> GetDisplayProfilesAsync(CancellationToken token)
        { await Task.Delay(200, token); return State; }
        public Task<DisplayProfilesState> ChangeDisplayProfileAsync(DisplayProfileCommand command, DisplayProfileRequest request,
            BrokerWidgetIdentity identity, CancellationToken token) => GetDisplayProfilesAsync(token);
    }
}
