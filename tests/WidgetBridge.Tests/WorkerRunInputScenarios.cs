using WidgetRail.WidgetBridge;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetPresentationSession;
using WidgetRail.WidgetRuntime;
using WidgetRail.WidgetSdk;

internal static class WorkerRunInputScenarios
{
    internal static async Task ExactInputNeverStartsWorker()
    {
        await using var client = new WidgetProcessClient(new WidgetProcessOptions
            { ExecutablePath = Environment.ProcessPath!, WidgetInstanceId = "no-input-restart" });
        var action = new WidgetActionEvent("save", "entry", InputScopeId: "root") { CommittedText = "value" };
        var input = new ControllerInputEvent(ControllerButton.A, ControllerEventPhase.Pressed,
            ControllerInputContext.OpenWidget, FocusedElementId: "button", ActiveInputScopeId: "root", SnapshotSequence: 7);
        await RegistryAssert.ThrowsAsync<WidgetInputWorkerRetiredException>(() => client.AdmitActionForWorkerAsync(action, default, 1));
        await RegistryAssert.ThrowsAsync<WidgetInputWorkerRetiredException>(() => client.SendControllerInputForWorkerAsync(input, null, default, 1));
        await RegistryAssert.ThrowsAsync<WidgetInputWorkerRetiredException>(() => client.SendRevalidatedControllerInputAsync(input, default, "play", 1));
        await RegistryAssert.ThrowsAsync<WidgetInputWorkerRetiredException>(() => client.AdmitPinnedActionAsync(
            new(1, WidgetPinnedProjection.FullWidgetLayoutId, 7, action), default, 1));
        var mediaEvent = MediaObservation();
        await RegistryAssert.ThrowsAsync<WidgetInputWorkerRetiredException>(() => client.SendEmbeddedMediaPlaybackEventAsync(mediaEvent));
        await RegistryAssert.ThrowsAsync<WidgetInputWorkerRetiredException>(() => client.SendEmbeddedMediaPlaybackEventForWorkerAsync(mediaEvent, default, 1));
        RegistryAssert.Equal(0, client.Starts);
        RegistryAssert.True(!client.IsRunning);
    }

    private static EmbeddedMediaPlaybackEvent MediaObservation() => new()
    {
        SessionId = "player", Sequence = 1, CommandSequence = 3, MediaKey = "track",
        State = EmbeddedMediaPlaybackState.Playing, PositionSeconds = 1, DurationSeconds = 10, Volume = 1, PlaybackRate = 1,
    };

    internal static async Task MediaObservationsNeverStartOrCrossWorkerRuns()
    {
        var configured = new ConfiguredWidget
        {
            Id = "media-run", InstanceId = "media-run.instance", Name = "Media run", PackageId = "dev.media", PublisherId = "dev",
            WorkerExecutable = Environment.ProcessPath!, WorkerFingerprint = new('a', 64), CatalogFingerprint = new('b', 64),
        };
        var media = new EmbeddedMediaSession
        {
            Id = "player", AccessibleName = "Player", EntryAsset = "media/index.html", AspectRatio = 16d / 9,
            Resources = [new() { Path = "media/index.html", ContentType = "text/html" }],
            Surface = new() { PreferredWidth = 640, PreferredHeight = 360, MinimumWidth = 320, MinimumHeight = 180 },
            PendingCommand = new() { Sequence = 3, MediaKey = "track", Kind = EmbeddedMediaPlaybackCommandKind.Play },
        };
        await using var fixture = new RegistryFixture(new([configured]), configure: (_, client) => client.SnapshotFactory = _ => new()
        {
            Sequence = 7, WidgetInstanceId = configured.InstanceId, ActiveInputScopeId = "root",
            Root = new() { Id = "root", Kind = ViewNodeKind.Stack }, EmbeddedMediaSession = media,
        });
        var descriptor = configured.PublicDescriptor();
        var request = new BridgeEmbeddedMediaPlaybackEventRequest(configured.Id, configured.InstanceId,
            descriptor.RuntimeGeneration, descriptor.PresentationGeneration, 7, MediaObservation());
        await Stale(request);
        RegistryAssert.Equal(0, fixture.Clients.Count); // An event cannot create a worker registration.
        await fixture.SetLifecycleAsync(configured.Id, WidgetLifecycleState.Visible);
        var first = await fixture.GetSnapshotAsync(configured.Id);
        var client = fixture.Clients.Single();
        request = request with { WorkerRun = first.WorkerRun };
        await fixture.Registry.PublishEmbeddedMediaPlaybackEventAsync(request, default);
        RegistryAssert.Equal(1, client.EmbeddedMediaPlaybackEvents.Count);

        await client.UnloadAsync(default);
        await Stale(request);
        await Stale(request with { WorkerRun = null }); // Legacy callers still cannot revive unloaded workers.
        RegistryAssert.Equal(1, client.Starts);
        await fixture.SetLifecycleAsync(configured.Id, WidgetLifecycleState.Visible);
        var replacement = await fixture.GetSnapshotAsync(configured.Id);
        RegistryAssert.Equal(first.Snapshot.Sequence, replacement.Snapshot.Sequence);
        RegistryAssert.True(first.WorkerRun != replacement.WorkerRun);
        await Stale(request);
        await Stale(request with { Event = request.Event with { CommandSequence = 0 } });
        // The same command ID in a new run starts fresh terminal bookkeeping.
        request = request with { WorkerRun = replacement.WorkerRun };
        await fixture.Registry.PublishEmbeddedMediaPlaybackEventAsync(request, default);
        RegistryAssert.Equal(2, client.EmbeddedMediaPlaybackEvents.Count);

        client.BeforeMediaSend = () => client.UnloadAsync(default).GetAwaiter().GetResult();
        await Stale(request with { Event = request.Event with { CommandSequence = 0, Sequence = 2 } });
        RegistryAssert.Equal(2, client.Starts);
        RegistryAssert.Equal(2, client.EmbeddedMediaPlaybackEvents.Count);

        async Task Stale(BridgeEmbeddedMediaPlaybackEventRequest value)
        {
            try { await fixture.Registry.PublishEmbeddedMediaPlaybackEventAsync(value, default); }
            catch (BridgeStaleMediaAuthorityException exception)
            {
                RegistryAssert.Equal("embedded_media_stale", WidgetBridgeServer.CreateRequestFailure(exception).Code);
                return;
            }
            throw new InvalidOperationException("Retired media worker event was accepted.");
        }
    }

    internal static async Task MediaGateWaitRetainsRegistrationUntilCancelledOrRejected()
    {
        var configured = new ConfiguredWidget
        {
            Id = "media-wait", InstanceId = "media-wait.instance", Name = "Media wait", PackageId = "dev.media", PublisherId = "dev",
            WorkerExecutable = Environment.ProcessPath!, WorkerFingerprint = new('a', 64), CatalogFingerprint = new('b', 64),
        };
        await using var fixture = new RegistryFixture(new([configured]));
        await fixture.SetLifecycleAsync(configured.Id, WidgetLifecycleState.Visible);
        var origin = await fixture.GetSnapshotAsync(configured.Id);
        var descriptor = configured.PublicDescriptor();
        var request = new BridgeEmbeddedMediaPlaybackEventRequest(configured.Id, configured.InstanceId,
            descriptor.RuntimeGeneration, descriptor.PresentationGeneration, origin.Snapshot.Sequence,
            MediaObservation(), origin.WorkerRun);
        var client = fixture.Clients.Single();
        client.BlockSnapshots = true;
        var snapshot = fixture.GetSnapshotAsync(configured.Id);
        await client.SnapshotEntered.WaitAsync(TimeSpan.FromSeconds(2));
        try
        {
            using var cancellation = new CancellationTokenSource();
            var cancelled = fixture.Registry.PublishEmbeddedMediaPlaybackEventAsync(request, cancellation.Token);
            RegistryAssert.True(!cancelled.IsCompleted);
            RegistryAssert.Equal(1, fixture.Registry.NotificationStatus(configured.Id).ActivePublications);
            cancellation.Cancel();
            await RegistryAssert.ThrowsAsync<OperationCanceledException>(() => cancelled);
            RegistryAssert.Equal(0, fixture.Registry.NotificationStatus(configured.Id).ActivePublications);

            var retired = fixture.Registry.PublishEmbeddedMediaPlaybackEventAsync(request, default);
            RegistryAssert.True(!retired.IsCompleted);
            RegistryAssert.Equal(1, fixture.Registry.NotificationStatus(configured.Id).ActivePublications);
            fixture.Registry.ApplyCatalog(new([]), revision: 1);
            var status = fixture.Registry.NotificationStatus(configured.Id);
            RegistryAssert.True(status.IsRetiring);
            RegistryAssert.Equal(1, status.ActivePublications);
            RegistryAssert.Equal(0, client.DisposeCount);
            client.ReleaseSnapshot();
            await RegistryAssert.ThrowsAsync<BridgeProtocolException>(() => snapshot);
            await RegistryAssert.ThrowsAsync<BridgeStaleMediaAuthorityException>(() => retired);
            await client.Disposed.WaitAsync(TimeSpan.FromSeconds(2));
            RegistryAssert.Equal(1, client.DisposeCount);
            RegistryAssert.Equal(0, client.EmbeddedMediaPlaybackEvents.Count);
        }
        finally { client.ReleaseSnapshot(); }
    }

    internal static async Task InputGateWaitRetainsRegistrationUntilCancelledOrRejected()
    {
        foreach (var kind in new[] { "action", "controller", "pinned" })
        {
            var configured = new ConfiguredWidget
            {
                Id = "input-wait", InstanceId = "input-wait.instance", Name = "Input wait", PackageId = "dev.input", PublisherId = "dev",
                WorkerExecutable = Environment.ProcessPath!, WorkerFingerprint = new('a', 64), CatalogFingerprint = new('b', 64),
                PinningSupported = true, FullWidgetPinningSupported = true,
            };
            await using var fixture = new RegistryFixture(new([configured]));
            await fixture.SetLifecycleAsync(configured.Id, WidgetLifecycleState.Interactive);
            var origin = await fixture.GetSnapshotAsync(configured.Id);
            var descriptor = configured.PublicDescriptor();
            var action = new WidgetActionEvent("activate", "button", InputScopeId: "root");
            var input = new ControllerInputEvent(ControllerButton.A, ControllerEventPhase.Pressed,
                ControllerInputContext.OpenWidget, FocusedElementId: "button", ActiveInputScopeId: "root", SnapshotSequence: origin.Snapshot.Sequence);
            var pinned = new BridgePinnedActionRequest(configured.Id, configured.InstanceId, descriptor.RuntimeGeneration,
                descriptor.PresentationGeneration, new(1, WidgetPinnedProjection.FullWidgetLayoutId, origin.Snapshot.Sequence, action), origin.WorkerRun);
            var client = fixture.Clients.Single();
            client.BlockSnapshots = true;
            var snapshot = fixture.GetSnapshotAsync(configured.Id);
            await client.SnapshotEntered.WaitAsync(TimeSpan.FromSeconds(2));
            try
            {
                using var cancellation = new CancellationTokenSource();
                var cancelled = Invoke(cancellation.Token);
                RegistryAssert.True(!cancelled.IsCompleted, kind + " did not wait for the operation gate.");
                RegistryAssert.Equal(1, fixture.Registry.NotificationStatus(configured.Id).ActivePublications);
                cancellation.Cancel();
                await RegistryAssert.ThrowsAsync<OperationCanceledException>(() => cancelled);
                RegistryAssert.Equal(0, fixture.Registry.NotificationStatus(configured.Id).ActivePublications);

                var retired = Invoke(default);
                RegistryAssert.Equal(1, fixture.Registry.NotificationStatus(configured.Id).ActivePublications);
                fixture.Registry.ApplyCatalog(new([]), revision: 1);
                var status = fixture.Registry.NotificationStatus(configured.Id);
                RegistryAssert.True(status.IsRetiring);
                RegistryAssert.Equal(1, status.ActivePublications);
                RegistryAssert.Equal(0, client.DisposeCount);
                client.ReleaseSnapshot();
                await RegistryAssert.ThrowsAsync<BridgeProtocolException>(() => snapshot);
                if (kind == "pinned") await RegistryAssert.ThrowsAsync<BridgeStalePinnedInputAuthorityException>(() => retired);
                else await RegistryAssert.ThrowsAsync<BridgeStaleControllerInputAuthorityException>(() => retired);
                await client.Disposed.WaitAsync(TimeSpan.FromSeconds(2));
                RegistryAssert.Equal(1, client.DisposeCount);
                RegistryAssert.Equal(0, client.ActionEvents.Count);
                RegistryAssert.Equal(0, client.ControllerInputs.Count);
                RegistryAssert.Equal(0, client.PinnedActions.Count);
            }
            finally { client.ReleaseSnapshot(); }

            async Task Invoke(CancellationToken token)
            {
                if (kind == "action")
                { using var result = await fixture.Registry.AdmitActionAsync(configured.Id, action, default, token, origin.WorkerRun); }
                else if (kind == "controller")
                { using var result = await fixture.Registry.SendControllerInputAsync(configured.Id, input, descriptor.RuntimeGeneration, default, token, expectedWorkerRun: origin.WorkerRun); }
                else
                { using var result = await fixture.Registry.AdmitPinnedActionAsync(pinned, default, token); }
            }
        }
    }

    internal static async Task CollidingSequencesRejectRetiredWorker()
    {
        var configured = new ConfiguredWidget
        {
            Id = "input-run", InstanceId = "input-run.instance", Name = "Input run", PackageId = "dev.input", PublisherId = "dev",
            WorkerExecutable = Environment.ProcessPath!, WorkerFingerprint = new('a', 64), CatalogFingerprint = new('b', 64),
            PinningSupported = true, FullWidgetPinningSupported = true,
        };
        await using var fixture = new RegistryFixture(new([configured]), configure: (_, client) =>
            client.SnapshotFactory = _ => new ViewSnapshot
            {
                Sequence = 7, WidgetInstanceId = configured.InstanceId, ActiveInputScopeId = "root",
                Root = new ViewNode
                {
                    Id = "root", Kind = ViewNodeKind.Stack, InputScopeId = "root",
                    Children = [new ViewNode { Id = "button", Kind = ViewNodeKind.Button, ActionId = "play", Text = "Play" },
                        new ViewNode { Id = "entry", Kind = ViewNodeKind.TextEntry, ActionId = "save", TextEntryMaximumLength = 20 }],
                },
            });
        await fixture.SetLifecycleAsync(configured.Id, WidgetLifecycleState.Interactive);
        var first = await fixture.GetSnapshotAsync(configured.Id);
        var client = fixture.Clients.Single();
        await client.UnloadAsync(default);
        await fixture.SetLifecycleAsync(configured.Id, WidgetLifecycleState.Interactive);
        var next = await fixture.GetSnapshotAsync(configured.Id);
        RegistryAssert.Equal(first.Snapshot.Sequence, next.Snapshot.Sequence);
        RegistryAssert.True(first.WorkerRun != next.WorkerRun);
        var descriptor = configured.PublicDescriptor();
        var action = new WidgetActionEvent("save", "entry", InputScopeId: "root") { CommittedText = "value" };
        var input = new ControllerInputEvent(ControllerButton.A, ControllerEventPhase.Pressed,
            ControllerInputContext.OpenWidget, FocusedElementId: "button", ActiveInputScopeId: "root", SnapshotSequence: 7);
        var pinned = new BridgePinnedActionRequest(configured.Id, configured.InstanceId, descriptor.RuntimeGeneration,
            descriptor.PresentationGeneration, new(1, WidgetPinnedProjection.FullWidgetLayoutId, 7, action), first.WorkerRun);

        await RegistryAssert.ThrowsAsync<BridgeStaleControllerInputAuthorityException>(() =>
            fixture.Registry.AdmitActionAsync(configured.Id, action, default, default, first.WorkerRun));
        await RegistryAssert.ThrowsAsync<BridgeStaleControllerInputAuthorityException>(() =>
            fixture.Registry.SendControllerInputAsync(configured.Id, input, descriptor.RuntimeGeneration,
                default, default, expectedWorkerRun: first.WorkerRun));
        await RegistryAssert.ThrowsAsync<BridgeStalePinnedInputAuthorityException>(() =>
            fixture.Registry.AdmitPinnedActionAsync(pinned, default, default));
        RegistryAssert.Equal(0, client.ActionEvents.Count);
        RegistryAssert.Equal(0, client.ControllerInputs.Count);
        RegistryAssert.Equal(0, client.PinnedActions.Count);

        using (var accepted = await fixture.Registry.AdmitActionAsync(configured.Id, action, default, default, next.WorkerRun))
            RegistryAssert.Equal(WidgetOperationAdmission.Enqueued, accepted.Value);
        using (var accepted = await fixture.Registry.SendControllerInputAsync(configured.Id, input, descriptor.RuntimeGeneration,
                   default, default, expectedWorkerRun: next.WorkerRun)) RegistryAssert.True(accepted.Value);
        using (var accepted = await fixture.Registry.AdmitPinnedActionAsync(pinned with { WorkerRun = next.WorkerRun }, default, default))
            RegistryAssert.Equal<WidgetOperationAdmission?>(WidgetOperationAdmission.Enqueued, accepted.Value);
        RegistryAssert.Equal(1, client.ActionEvents.Count);
        RegistryAssert.Equal(1, client.ControllerInputs.Count);
        RegistryAssert.Equal(1, client.PinnedActions.Count);

        // Even a matching receipt cannot start an unloaded worker to deliver input.
        await client.UnloadAsync(default);
        await RegistryAssert.ThrowsAsync<BridgeStaleControllerInputAuthorityException>(() =>
            fixture.Registry.AdmitActionAsync(configured.Id, action, default, default, next.WorkerRun));
        RegistryAssert.Equal(2, client.Starts);

        using (var restarted = await fixture.Registry.RestartAsync(configured.Id, default)) { }
        var replacement = await fixture.GetSnapshotAsync(configured.Id);
        RegistryAssert.True(replacement.WorkerRun!.RegistryGeneration != next.WorkerRun!.RegistryGeneration);
        RegistryAssert.Equal(7L, replacement.Snapshot.Sequence);
        await RegistryAssert.ThrowsAsync<BridgeStaleControllerInputAuthorityException>(() =>
            fixture.Registry.AdmitActionAsync(configured.Id, action, default, default, next.WorkerRun));
        using (var accepted = await fixture.Registry.AdmitActionAsync(configured.Id, action, default, default, replacement.WorkerRun))
            RegistryAssert.Equal(WidgetOperationAdmission.Enqueued, accepted.Value);
    }
}
