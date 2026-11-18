using System.Text.Json;
using WidgetRail.PlatformBroker;
using WidgetRail.WidgetProtocol;

internal static class FileReferenceTests
{
    internal static Task ContentIdentity()
    {
        if (!OperatingSystem.IsWindows()) return Task.CompletedTask;
        var path = Path.GetTempFileName(); var otherPath = Path.GetTempFileName();
        try
        {
            File.WriteAllBytes(path, [1, 2, 3, 4]); File.WriteAllBytes(otherPath, [1, 2, 3, 4]);
            using var registry = new FileReferenceRegistry();
            using var sender = registry.CreateOwner(Identity("source"));
            using var receiver = registry.CreateOwner(Identity("target"));
            using var otherReceiver = registry.CreateOwner(Identity("other"));
            var first = registry.RegisterSelected(sender, [path], default).Files.Single();
            var repeat = registry.RegisterSelected(sender, [path], default).Files.Single();
            var sameBytes = registry.RegisterSelected(sender, [otherPath], default, name:first.Name).Files.Single();
            Check(first.ContentIdentity is not null && first.ContentIdentity == repeat.ContentIdentity && first.Id != repeat.Id,
                "Repeated original/version needs stable identity despite independent grants");
            Check(first.ContentIdentity != sameBytes.ContentIdentity, "Name/length/content similarity must not conflate distinct originals");
            WidgetFileReference Deliver(FileReferenceRegistry.Owner target, string id)
            {
                var offer = registry.OfferFiles(sender, new([id]));
                using var delivery = registry.AuthorizeShare(sender.Identity, target.Identity, offer.Id);
                var file = registry.AcceptShare(target, offer.Id).Files.Single();
                Check(delivery.Complete(true), "Identity fixture transfer failed");
                return file;
            }
            var received = Deliver(receiver, first.Id); var receivedAgain = Deliver(receiver, repeat.Id);
            var foreign = Deliver(otherReceiver, first.Id);
            Check(received.ContentIdentity == receivedAgain.ContentIdentity && received.ContentIdentity != first.ContentIdentity &&
                received.ContentIdentity != foreign.ContentIdentity, "File identity must correlate only within the same recipient lifetime");
            Check(received.IsWellFormed() && !(received with { ContentIdentity = "path/to/file" }).IsWellFormed(), "Identity token validation failed");
            registry.Release(receiver, received.Id); registry.Release(receiver, receivedAgain.Id); registry.Release(otherReceiver, foreign.Id);
            registry.Release(sender, first.Id); registry.Release(sender, repeat.Id);
            File.WriteAllBytes(path, [5, 6, 7, 8]); File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddMinutes(1));
            var changed = registry.RegisterSelected(sender, [path], default).Files.Single();
            Check(changed.ContentIdentity != first.ContentIdentity, "Changed original must not deduplicate against an old draft attachment");
            using var independent = new FileReferenceRegistry(); using var newOwner = independent.CreateOwner(sender.Identity);
            var restarted = independent.RegisterSelected(newOwner, [path], default).Files.Single();
            Check(restarted.ContentIdentity != changed.ContentIdentity, "Identity must not track files across broker/owner lifetimes");
        }
        finally { File.Delete(path); File.Delete(otherPath); }
        return Task.CompletedTask;
    }

    private static BrokerWidgetIdentity Identity(string id) => new("example." + id, "example.publisher", id);
    internal static async Task CaptureAuthority()
    {
        using var captures = new WindowCaptureRegistry();
        captures.SetCapabilities(new(true, [CaptureImageFormat.Png], [], [CaptureAudioMode.None], true, 120));
        var captureOwner = new object();
        var sender = Identity("capture");
        var ticket = captures.EnqueueConfigured(captureOwner, sender, new CaptureSessionRequest { Screenshot = new(), MaximumBytes = 4096 });
        var work = captures.Take()!;
        var bytes = new byte[256]; new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }.CopyTo(bytes, 0);
        await File.WriteAllBytesAsync(work.OutputPath, bytes);
        captures.Complete(new(ticket.RequestId, 1, 1, 0) { Properties = new(1, 1) });
        var attachment = captures.Status(captureOwner, ticket.RequestId).Attachment!;
        using var registry = new FileReferenceRegistry();
        using var domain = new FileCapabilityDomain(sender, expected => captures.ResolveOwned(captureOwner, expected), registry);
        using var receiver = registry.CreateOwner(Identity("receiver"));
        var forged = attachment with { ByteLength = attachment.ByteLength - 1 };
        await RejectAsync(async () => { await domain.ExecuteAsync("files.from-capture", BrokerJson.ToElement(new WidgetFileFromCaptureRequest(forged)), default); });
        var fileJson = await domain.ExecuteAsync("files.from-capture", BrokerJson.ToElement(new WidgetFileFromCaptureRequest(attachment, "capture.png")), default);
        var file = BrokerJson.ParsePayload<WidgetFileReference>(fileJson);
        var offerJson = await domain.ExecuteAsync("files.offer", BrokerJson.ToElement(new WidgetFileOfferRequest([file.Id])), default);
        var offer = BrokerJson.ParsePayload<WidgetFileShareOffer>(offerJson);
        using var delivery = registry.AuthorizeShare(sender, receiver.Identity, offer.Id);
        var received = registry.AcceptShare(receiver, offer.Id).Files.Single();
        Check(delivery.Complete(true), "Capture handoff was not accepted");
        captures.Discard(captureOwner, attachment.Id);
        Check(!File.Exists(work.OutputPath), "Attachment handle prevented temporary capture cleanup");
        domain.Dispose();
        Check((await registry.ReadAsync(receiver, new(received.Id, 0, 8), default)).Bytes.SequenceEqual(bytes.Take(8)), "Cleaned-up capture could not be read by accepted recipient");
        registry.Release(receiver, received.Id);
    }

    internal static async Task Handoff()
    {
        var path = Path.GetTempFileName();
        try
        {
            await File.WriteAllBytesAsync(path, [1, 2, 3, 4]);
            using var registry = new FileReferenceRegistry();
            using var sender = registry.CreateOwner(Identity("source"));
            using var receiver = registry.CreateOwner(Identity("target"));
            using var foreign = registry.CreateOwner(Identity("foreign"));
            var current = true;
            var file = registry.RegisterSelected(sender, [path], default, sourceCurrent: () => current).Files.Single();
            var offer = registry.OfferFiles(sender, new([file.Id]));
            Reject(() => registry.AcceptShare(receiver, offer.Id));
            Reject(() => registry.AuthorizeShare(foreign.Identity, receiver.Identity, offer.Id));
            using var delivery = registry.AuthorizeShare(sender.Identity, receiver.Identity, offer.Id);
            var received = registry.AcceptShare(receiver, offer.Id).Files.Single();
            Check(received.Id != file.Id, "Recipient reused the sender token");
            Reject(() => registry.Get(foreign, received.Id));
            Reject(() => registry.AcceptShare(foreign, offer.Id));
            Reject(() => registry.AuthorizeShare(sender.Identity, foreign.Identity, offer.Id));
            Check(delivery.Complete(true), "Delivery did not commit");
            current = false; // Linked-folder revocation after explicit handoff.
            sender.Dispose();
            Check((await registry.ReadAsync(receiver, new(received.Id, 1, 2), default)).Bytes.SequenceEqual(new byte[] { 2, 3 }), "Accepted custody died with sender");
            // A recipient may explicitly share its independent grant onward.
            var onward = registry.OfferFiles(receiver, new([received.Id]));
            using var second = registry.AuthorizeShare(receiver.Identity, foreign.Identity, onward.Id);
            var third = registry.AcceptShare(foreign, onward.Id).Files.Single();
            Check(second.Complete(true), "Independent onward grant failed");
            receiver.Dispose();
            Check((await registry.ReadAsync(foreign, new(third.Id, 3, 1), default)).Bytes.Single() == 4, "Onward share lost its handle");
            registry.Release(foreign, third.Id);
            Check(File.ReadAllBytes(path).SequenceEqual(new byte[] { 1, 2, 3, 4 }), "Sharing modified the original");
        }
        finally { File.Delete(path); }
    }

    internal static Task Revocation()
    {
        var path = Path.GetTempFileName();
        try
        {
            using var registry = new FileReferenceRegistry();
            using var sender = registry.CreateOwner(Identity("source"));
            using var receiver = registry.CreateOwner(Identity("target"));
            var current = true;
            var file = registry.RegisterSelected(sender, [path], default, sourceCurrent: () => current).Files.Single();
            var offer = registry.OfferFiles(sender, new([file.Id]));
            using (var rejected = registry.AuthorizeShare(sender.Identity, receiver.Identity, offer.Id))
            {
                var provisional = registry.AcceptShare(receiver, offer.Id).Files.Single();
                rejected.Complete(false);
                Reject(() => registry.Get(receiver, provisional.Id));
            }
            var revoked = registry.OfferFiles(sender, new([file.Id]));
            using (var pending = registry.AuthorizeShare(sender.Identity, receiver.Identity, revoked.Id))
            {
                var provisional = registry.AcceptShare(receiver, revoked.Id).Files.Single();
                current = false;
                Check(!pending.Complete(true), "Revoked source offer committed");
                Reject(() => registry.Get(receiver, provisional.Id));
            }
            current = true;
            file = registry.RegisterSelected(sender, [path], default, sourceCurrent: () => current).Files.Single();
            var stale = registry.OfferFiles(sender, new([file.Id]));
            using var replacement = registry.CreateOwner(sender.Identity);
            Reject(() => registry.Get(replacement, file.Id));
            Reject(() => registry.AuthorizeShare(replacement.Identity, receiver.Identity, stale.Id));
            return Task.CompletedTask;
        }
        finally { File.Delete(path); }
    }

    internal static async Task BoundedRead()
    {
        var path = Path.GetTempFileName();
        try
        {
            // Read only the tail of a file larger than a transport message.
            // SDK tests separately exercise offsets beyond Int32.MaxValue.
            const long length = 3L * 1024 * 1024;
            using (var output = new FileStream(path, FileMode.Open, FileAccess.Write)) { output.SetLength(length); output.Position = length - 1; output.WriteByte(77); }
            var clock = new TestClock();
            using var registry = new FileReferenceRegistry(clock);
            using var owner = registry.CreateOwner(Identity("source"));
            var file = registry.RegisterSelected(owner, [path], default).Files.Single();
            Check(file.ByteLength == length, "Large file length narrowed");
            var tail = await registry.ReadAsync(owner, new(file.Id, length - 1, 64), default);
            Check(tail.EndOfFile && tail.Bytes.SequenceEqual(new byte[] { 77 }), "Large-file bounded tail failed");
            await RejectAsync(() => registry.ReadAsync(owner, new(file.Id, 0, WidgetFileContract.ChunkBytes + 1), default));
            await RejectAsync(() => registry.ReadAsync(owner, new(file.Id, length + 1, 1), default));
            using var canceled = new CancellationTokenSource(); canceled.Cancel();
            try { await registry.ReadAsync(owner, new(file.Id, 0, 1), canceled.Token); throw new Exception("Canceled read succeeded"); } catch (OperationCanceledException) { }
            var offer = registry.OfferFiles(owner, new([file.Id]));
            clock.Now += TimeSpan.FromSeconds(61);
            using var target = registry.CreateOwner(Identity("target"));
            Reject(() => registry.AuthorizeShare(owner.Identity, target.Identity, offer.Id));
            clock.Now += TimeSpan.FromMinutes(16);
            Reject(() => registry.Get(owner, file.Id));
            using var exclusive = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        }
        finally { File.Delete(path); }
    }

    internal static async Task Picker()
    {
        using var token = new CancellationTokenSource();
        var current = true;
        var wait = FilePickerRequests.ChooseAsync(Identity("source"), new(), () => current, token.Token);
        var pending = FilePickerRequests.Pending!;
        Check(!FilePickerRequests.Complete("wrong", []), "Foreign picker completion accepted");
        current = false;
        Check(!FilePickerRequests.IsCurrent(pending.Id), "Retired picker remained current");
        try { await wait; throw new Exception("Retired picker completed"); } catch (OperationCanceledException) { }
        current = true;
        var next = FilePickerRequests.ChooseAsync(Identity("source"), new(Multiple: false), () => current, token.Token);
        pending = FilePickerRequests.Pending!;
        Reject(() => FilePickerRequests.Complete(pending.Id, ["relative.txt"]));
        Check(FilePickerRequests.Complete(pending.Id, []), "Picker cancellation did not complete");
        Check((await next).Length == 0, "Canceled picker created a reference");
    }

    internal static async Task BrokerPolicy()
    {
        var identity = Identity("policy");
        Check(!PlatformCapabilities.IsManifestDeclarable(PlatformCapabilities.FilesReadV1), "File authority became manifest-declarable");
        var store = new ConsentStore(Path.Combine(Path.GetTempPath(), "file-policy-" + Guid.NewGuid().ToString("N")));
        await using var broker = new PlatformCapabilityBroker(identity, [], store, new SimulatedPlatformBrokerBackend(), [PlatformCapabilities.FilesReadV1]);
        broker.SetLifecycle(BrokerLifecycleState.Background);
        var request = new BrokerRequestEnvelope(1, 1, identity, PlatformCapabilities.FilesReadV1, "files.choose", BrokerJson.ToElement(new WidgetFileChoiceRequest()));
        await RejectAsync(async () => { await broker.ExecuteAsync(request); }, "lifecycle_denied");
        await RejectAsync(async () => { await broker.ExecuteAsync(request with { Operation = "files.get", Payload = BrokerJson.ToElement(new WidgetFileIdReference(new string('a', 32))) }); }, "file_unavailable");
        broker.SetLifecycle(BrokerLifecycleState.Interactive);
        await RejectAsync(async () => { await broker.ExecuteAsync(request with { Payload = JsonSerializer.SerializeToElement(new { title = "Choose files", path = "C:/secret.txt" }) }); }, "invalid_payload");
        var choosing = broker.ExecuteAsync(request);
        var pending = FilePickerRequests.Pending ?? throw new Exception("Interactive request did not admit a picker");
        broker.SetLifecycle(BrokerLifecycleState.Background);
        Check(FilePickerRequests.IsCurrent(pending.Id) && !choosing.IsCompleted, "Native-picker focus transfer canceled the admitted request");
        Check(FilePickerRequests.Complete(pending.Id, []), "Admitted picker could not finish after focus transfer");
        Check(BrokerJson.ParsePayload<WidgetFileSelection>(await choosing).Files.Count == 0, "Canceled picker created a file");
        await RejectAsync(async () => { await broker.ExecuteAsync(request with { Operation = "files.accept", Payload = BrokerJson.ToElement(new WidgetFileIdReference(new string('a', 32))) }); }, "file_offer_unavailable");
        var options = new BrokerPipeTransportOptions { RequestTimeout = TimeSpan.FromSeconds(10) };
        Check(BrokerPipeRequestTimeoutPolicy.Resolve(options, request) == TimeSpan.FromSeconds(125), "Picker transport expired before host chooser");
    }

    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
    private static void Reject(Action action) { try { action(); } catch (BrokerException) { return; } throw new Exception("Unauthorized or invalid file operation was accepted"); }
    private static async Task RejectAsync(Func<Task> action, string? code = null) { try { await action(); } catch (BrokerException error) when (code is null || error.Code == code) { return; } throw new Exception("Unauthorized or invalid file operation was accepted"); }
    private sealed class TestClock : TimeProvider { internal DateTimeOffset Now = DateTimeOffset.UtcNow; public override DateTimeOffset GetUtcNow() => Now; }
}
