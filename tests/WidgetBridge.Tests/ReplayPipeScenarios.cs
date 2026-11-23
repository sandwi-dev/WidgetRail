using WidgetRail.PlatformBroker;
using WidgetRail.WidgetBridge;
using WidgetRail.WidgetPresentationSession;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetRuntime;
using WidgetRail.WidgetSdk;

internal static class ReplayPipeScenarios
{
    internal static async Task EndToEnd()
    {
        var root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "wrail-replay-pipe-" + Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(root);
        try
        {
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30)); var token = deadline.Token;
            var actor = new BrokerWidgetIdentity("widgetrail.firstparty.capture-library", "widgetrail.firstparty", "replay.instance");
            string[] permissions = [PlatformCapabilities.CaptureLibraryReadV1, PlatformCapabilities.CaptureLibrarySaveV1, PlatformCapabilities.WindowCaptureV1];
            var consent = new ConsentStore(Path.Combine(root, "consent"));
            await consent.SetDecisionsAsync(actor, permissions, ConsentDecision.Grant, token);
            using var library = new CaptureLibraryStore(Path.Combine(root, "library"), Path.Combine(root, "media"), null, null,
                (path, media, _) => Task.FromResult(media with { ByteLength = new FileInfo(path).Length, Properties = null }));
            WidgetBridgeServer? host = null;
            var configuration = CaptureReplayConfiguration.Default;
            await using var replay = new CaptureReplayService(library, configuration,
                (next, expected, cancellation) => { cancellation.ThrowIfCancellationRequested(); Check(expected == configuration.Revision, "Settings revision lost");
                    configuration = next; return Task.FromResult(next); },
                (identity, capability, cancellation) => host!.AuthorizeReplayFeatureAsync(identity, capability, cancellation));
            var simulation = new SimulatedPlatformBrokerBackend();
            await using var backend = new CompositePlatformBrokerBackend(simulation, simulation, captureLibrary: library) { CaptureReplay = replay };
            var configured = new ConfiguredWidget { Id = "replay", PackageId = actor.PackageId, PublisherId = actor.PublisherId, InstanceId = actor.InstanceId,
                Name = "Replay fixture", WorkerExecutable = "unused.exe", DeclaredCapabilities = permissions, GrantsBundledPermissionDefaults = true,
                WorkerFingerprint = new string('a', 64), CatalogFingerprint = new string('b', 64) };
            var hostPipe = "replay-host-" + Guid.NewGuid().ToString("N");
            await using var server = new WidgetBridgeServer(hostPipe, new([configured]), consentStore: consent, platformBackend: backend);
            host = server;
            var hostServing = host.RunAsync(TimeSpan.FromSeconds(3), token);
            await using var session = await WidgetPresentationSession.ConnectAsync(hostPipe, cancellationToken: token);
            var capabilityPipe = "replay-capability-" + Guid.NewGuid().ToString("N");
            await using var broker = new BrokerPipeServer(capabilityPipe, actor, permissions, consent, backend,
                new() { RequestTimeout = TimeSpan.FromSeconds(1) }, new string('e', 64));
            var brokerServing = broker.RunAsync(token);
            await using var client = new BrokerPipeClient(capabilityPipe, actor, broker.ChannelNonce, new() { RequestTimeout = TimeSpan.FromSeconds(1) });
            await client.ConnectAsync(token); broker.SetLifecycle(BrokerLifecycleState.Interactive);
            var sdk = new WidgetHostServices(new BrokerWidgetCapabilityClient(client)).Capture.Replay;
            var initial = await sdk.GetAsync(token);
            Check(initial.Phase == CaptureReplayPhase.Off && (await session.GetReplayStatusAsync(token)).Phase == initial.Phase, "Replay reads did not cross both pipes");
            _ = await session.TakeReplayWorkAsync(cancellationToken: token);
            broker.SetLifecycle(BrokerLifecycleState.Background);
            broker.GrantDashboardGestureAuthority(PlatformCapabilities.WindowCaptureV1, "capture.replay.start", 1, 1,
                TimeSpan.FromSeconds(2), null, new TrayCaptureCommand("capture.replay.start", Revision: initial.Revision));
            var trayStart = client.RequestWithGestureAsync(PlatformCapabilities.WindowCaptureV1, "capture.replay.start",
                new CaptureReplayReadRequest { EncodingDetails = true }, 1, 1, token);
            var choice = await Pending(HostReplayCommandKind.Start);
            Check(choice.RequestedFromTray && !choice.Automatic && !trayStart.IsCompleted, "A tray gesture did not wait for the source picker");
            Check(await session.CompleteReplayCommandAsync(new(choice.Id, CaptureReplayOutcome.Cancelled, CaptureReplayIssue.Cancelled)
                { SessionId = choice.SessionId }, token), "Could not dismiss the tray source picker");
            Check(BrokerJson.ParsePayload<CaptureReplayControlResult>((await trayStart).Payload ?? throw new Exception("Missing replay admission")).Outcome == CaptureReplayOutcome.Cancelled && (await session.GetReplayStatusAsync(token)).Phase == CaptureReplayPhase.Off,
                "A cancelled host quick action started recording");
            broker.SetLifecycle(BrokerLifecycleState.Interactive);
            // The cancelled start generated status events before the configuration check below.
            await using var changes = await sdk.OpenChangesSubscriptionAsync(token);
            await using var events = changes.ReadAllAsync(token).GetAsyncEnumerator(token);
            var notification = events.MoveNextAsync().AsTask();
            var configuring = sdk.ConfigureAsync(initial.Settings with { Enabled = true, ReplayDurationSeconds = 15 }, initial.SettingsRevision, token).AsTask();
            var approval = await Pending(HostReplayCommandKind.ApproveSettings);
            Check(await session.IsReplayCommandCurrentAsync(approval.Id, token), "Configure approval lost current authority");
            Check(await session.CompleteReplayCommandAsync(new(approval.Id, CaptureReplayOutcome.Accepted), token), "Configure completion rejected");
            Check((await configuring).Outcome == CaptureReplayOutcome.Accepted && await notification.WaitAsync(token) && events.Current.Settings.Enabled,
                "Replay configure or subscription did not publish enabled state");
            var starting = sdk.StartAsync(token).AsTask(); var start = await Pending(HostReplayCommandKind.Start);
            Check(await session.IsReplayCommandCurrentAsync(start.Id, token), "Start lost native admission authority");
            Check(await session.CompleteReplayCommandAsync(new(start.Id, CaptureReplayOutcome.Accepted) { SessionId = start.SessionId,
                Target = new("123", 456, "789", "FixtureWindow"), ApplicationIdentity = new string('a', 64), ApplicationName = "Fixture game" }, token),
                "Host start completion rejected");
            Check((await starting).Status.Phase == CaptureReplayPhase.Starting, "Start incorrectly claimed decoded frames");
            _ = await session.TakeReplayWorkAsync(new(start.SessionId!, CaptureReplayPhase.Recording, 3, 1024, 0), token);
            Check((await sdk.GetAsync(token)).Phase == CaptureReplayPhase.Recording, "Native report did not reach SDK state");
            Check((await session.StopReplayAsync(null, token)).Outcome == CaptureReplayOutcome.Conflict &&
                (await sdk.StopAsync(null, token)).Outcome == CaptureReplayOutcome.Conflict, "Waiting-menu stop redirected to new session");
            var firstSave = sdk.SaveAsync(start.SessionId!, token).AsTask();
            var firstOutput = await Pending(HostReplayCommandKind.Save);
            broker.SetLifecycle(BrokerLifecycleState.Background);
            await CompleteOutput(firstOutput);
            Check((await firstSave).Outcome == CaptureReplayOutcome.Accepted, "SDK save failed after source hid");
            // A later quick action can use a fresh authenticated worker channel to control
            // the host session. A stale press cannot redirect its save to a new session.
            await using (var fresh = new PlatformCapabilityBroker(actor, permissions, consent, backend))
            {
                fresh.SetLifecycle(BrokerLifecycleState.Background);
                var op = "capture.replay.save";
                Task<System.Text.Json.JsonElement> Save(string id, long sequence) => fresh.ExecuteAsync(new BrokerRequestEnvelope(
                    BrokerJson.ProtocolVersion, sequence, actor, PlatformCapabilities.WindowCaptureV1, op,
                    BrokerJson.ToElement(new CaptureReplaySaveRequest(id, Guid.NewGuid().ToString("N"))))
                    { GestureInputSequence = sequence, GestureSnapshotSequence = 8 }, token);
                void Grant(long sequence) => fresh.GrantDashboardGestureAuthority(PlatformCapabilities.WindowCaptureV1,
                    op, sequence, 8, TimeSpan.FromSeconds(2), null, new TrayCaptureCommand(op, start.SessionId));
                Grant(1);
                try { await Save(Guid.NewGuid().ToString("N"), 1); throw new Exception("Replay save redirected to another session"); }
                catch (BrokerException error) when (error.Code == "capture_stale") { }
                Grant(2);
                var traySave = Save(start.SessionId!, 2);
                await CompleteOutput(await Pending(HostReplayCommandKind.Save));
                Check(BrokerJson.ParsePayload<CaptureReplaySaveResult>(await traySave).Outcome == CaptureReplayOutcome.Accepted,
                    "Fresh background worker failed to save the active replay");
            }
            broker.SetLifecycle(BrokerLifecycleState.Interactive);
            var secondSave = session.SaveReplayAsync(start.SessionId!, token);
            var secondOutput = await Pending(HostReplayCommandKind.Save);
            Check((await sdk.StopAsync(start.SessionId, token)).Outcome == CaptureReplayOutcome.Accepted, "SDK stop failed");
            var draining = await session.TakeReplayWorkAsync(new(start.SessionId!, CaptureReplayPhase.Stopping, 3, 1024, 0, Ended: true), token);
            Check(draining.StopRequested && draining.Status.Saving, "Host stop discarded accepted save");
            await CompleteOutput(secondOutput);
            Check((await secondSave).Outcome == CaptureReplayOutcome.Accepted && (await library.ListAsync(0, token)).TotalCount == 3,
                "The three independent saves did not produce exactly three clips");
            Check((await session.StopReplayAsync(null, token)).Outcome == CaptureReplayOutcome.Accepted, "Host idle stop failed");
            await session.SetReplayPowerStateAsync(1, true, token); await session.SetReplayPowerStateAsync(2, false, token);
            Check((await session.GetReplayStatusAsync(token)).SessionId is null, "Power messages restored a retired session");
            Check(!await session.IsReplayCommandCurrentAsync(start.Id, token), "Completed start command could replay");
            await events.DisposeAsync(); await changes.DisposeAsync();
            await client.DisposeAsync(); await broker.DisposeAsync(); await brokerServing.WaitAsync(token);
            await session.DisposeAsync(); await hostServing.WaitAsync(token);

            async Task<HostReplayCommand> Pending(HostReplayCommandKind kind)
            {
                while (true)
                {
                    var work = await session.TakeReplayWorkAsync(cancellationToken: token);
                    if (work.Command is { } command && command.Kind == kind)
                    { Check(work.WidgetId == configured.Id && command.Widget == actor, "Host work lost widget identity"); return command; }
                    await Task.Delay(10, token);
                }
            }
            async Task CompleteOutput(HostReplayCommand command)
            {
                Check(await session.IsReplayCommandCurrentAsync(command.Id, token), "Save reservation lost current authority");
                var bytes = new byte[256]; await File.WriteAllBytesAsync(command.OutputPath!, bytes, token);
                Check(await session.CompleteReplayCommandAsync(new(command.Id, CaptureReplayOutcome.Accepted) { SessionId = command.SessionId,
                    Width = 1280, Height = 720, ByteLength = bytes.Length, ActualDurationSeconds = 2,
                    Properties = new(1280, 720, 30, 60, 0, CaptureEncoderMode.Hardware, CaptureAudioMode.Application) }, token), "Encoded completion rejected");
            }
        }
        finally
        {
            var temporary = Path.GetFullPath(Path.GetTempPath());
            if (!root.StartsWith(temporary, StringComparison.OrdinalIgnoreCase) || !Path.GetFileName(root).StartsWith("wrail-replay-pipe-", StringComparison.Ordinal))
                throw new InvalidOperationException("Unsafe fixture cleanup path");
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }
    private static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
}
