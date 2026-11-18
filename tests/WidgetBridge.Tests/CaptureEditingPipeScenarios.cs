using WidgetRail.PlatformBroker;
using WidgetRail.WidgetBridge;
using WidgetRail.WidgetPresentationSession;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetRuntime;
using WidgetRail.WidgetSdk;

internal static class CaptureEditingPipeScenarios
{
    internal static async Task EndToEnd()
    {
        var root = Path.Combine(Path.GetTempPath(), "wrail-edit-pipe-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(25));
            var token = deadline.Token;
            var identity = new BrokerWidgetIdentity("example.editor", "example.publisher", "editor.instance");
            string[] permissions = [PlatformCapabilities.CaptureLibraryReadV1, PlatformCapabilities.CaptureLibrarySaveV1, PlatformCapabilities.WindowCaptureV1];
            var consent = new ConsentStore(Path.Combine(root, "consent"));
            foreach (var permission in permissions) await consent.SetDecisionAsync(identity, permission, ConsentDecision.Grant, token);
            using var store = new CaptureLibraryStore(Path.Combine(root, "library"), Path.Combine(root, "media"), null, null,
                (path, media, _) => Task.FromResult(media with { Width = 50, Height = 60, ByteLength = new FileInfo(path).Length, Properties = null }));
            var simulation = new SimulatedPlatformBrokerBackend();
            await using var backend = new CompositePlatformBrokerBackend(simulation, simulation, captureLibrary: store);
            var capabilityPipe = "edit-capability-" + Guid.NewGuid().ToString("N");
            await using var broker = new BrokerPipeServer(capabilityPipe, identity, permissions, consent, backend,
                new() { RequestTimeout = TimeSpan.FromMilliseconds(50) }, new string('e', 64));
            var brokerServing = broker.RunAsync(token);
            await using var client = new BrokerPipeClient(capabilityPipe, identity, broker.ChannelNonce,
                new() { RequestTimeout = TimeSpan.FromMilliseconds(50) });
            await client.ConnectAsync(token); broker.SetLifecycle(BrokerLifecycleState.Interactive);
            var services = new WidgetHostServices(new BrokerWidgetCapabilityClient(client));
            var configured = new ConfiguredWidget
            {
                Id = "editor", PackageId = identity.PackageId, PublisherId = identity.PublisherId, InstanceId = identity.InstanceId,
                Name = "Editor fixture", WorkerExecutable = "unused.exe", DeclaredCapabilities = permissions,
                WorkerFingerprint = new string('a', 64), CatalogFingerprint = new string('b', 64),
            };
            var hostPipe = "edit-host-" + Guid.NewGuid().ToString("N");
            await using var host = new WidgetBridgeServer(hostPipe, new([configured]), consentStore: consent, platformBackend: simulation);
            var hostServing = host.RunAsync(TimeSpan.FromSeconds(3), token);
            await using var session = await WidgetPresentationSession.ConnectAsync(hostPipe, cancellationToken: token);
            var originalPath = Path.Combine(root, "original.png");
            byte[] originalBytes = [137,80,78,71,13,10,26,10,0,0,0,0];
            await File.WriteAllBytesAsync(originalPath, originalBytes, token);
            var originalMedia = new CaptureAttachment(Guid.NewGuid().ToString("N"), "image/png", 100, 100, originalBytes.Length, 0, DateTimeOffset.UtcNow.AddMinutes(5).ToUnixTimeMilliseconds());
            var saved = await store.SaveAsync(new(originalMedia, originalPath), token);
            foreach (var sourceKind in new[] { "saved", "temporary" })
            {
                var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                var finish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                CaptureLibraryEditRequests.Configure(async (_, _, output, progress, cancellation) =>
                {
                    progress?.Report(.5); started.TrySetResult(); await finish.Task.WaitAsync(cancellation);
                    await output.WriteAsync(new byte[] {137,80,78,71,13,10,26,10,7,8,9,10,11}, cancellation);
                }, (_, _) => Task.CompletedTask, (input, _) => Task.FromResult(new CaptureEditPreview(new Uri(input.Path).AbsoluteUri)));
                CaptureAttachment? temporary = null;
                if (sourceKind == "temporary")
                {
                    var ticket = await services.Capture.RequestAsync(WindowCaptureKind.Screenshot, token);
                    var capture = (await session.TakeCaptureAsync(token)).Request ?? throw new Exception("Temporary capture was not queued.");
                    await File.WriteAllBytesAsync(capture.OutputPath, originalBytes, token);
                    await session.CompleteCaptureAsync(new(ticket.RequestId, 100, 100, 0), token);
                    temporary = (await services.Capture.GetStatusAsync(ticket, token)).Attachment!;
                }
                var operation = temporary is null ? services.CaptureLibrary.EditCopyAsync(new CaptureLibraryMutationReference(saved.Id, saved.Revision, saved.MetadataRevision), token).AsTask()
                    : services.CaptureLibrary.EditCopyAsync(temporary, token).AsTask();
                HostCaptureEditRequest? edit = null;
                while (edit is null) { edit = (await session.TakeCaptureAsync(token)).Edit; if (edit is null) await Task.Delay(10, token); }
                Check((await session.GetCaptureEditStatusAsync(edit.Id, token)).Current, "Host did not receive current editor.");
                Check(await session.CompleteCaptureEditAsync(edit.Id, new(new(0,0,0,50,60)), token), "Host recipe was rejected.");
                await started.Task.WaitAsync(token);
                var status = await session.GetCaptureEditStatusAsync(edit.Id, token).WaitAsync(TimeSpan.FromSeconds(1));
                Check(status is { Stage: HostCaptureEditStage.Saving, Progress: .5 }, "Encode blocked host status lane.");
                Check((await session.TakeCaptureAsync(token)).Edit is null, "Saving edit was offered again.");
                finish.TrySetResult();
                var result = await operation.WaitAsync(token);
                Check(result.Outcome == CaptureLibraryEditOutcome.Saved && result.Item?.Id == edit.ResultId, "SDK lost exact saved result.");
                Check((await session.CancelCaptureEditAsync(edit.Id, token)).Stage == HostCaptureEditStage.Saved, "Late host cancel hid committed result.");
                if (temporary is not null) await services.Capture.DiscardAsync(temporary, token);
            }
            Check(File.ReadAllBytes(originalPath).SequenceEqual(originalBytes), "Host editing changed original bytes.");
            await session.DisposeAsync(); await hostServing.WaitAsync(token);
            await client.DisposeAsync(); await broker.DisposeAsync(); await brokerServing.WaitAsync(token);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
}
