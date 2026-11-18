using System.Diagnostics;
using WidgetRail.PlatformBroker;
using WidgetRail.WidgetProtocol;

internal static class PlaybackTests
{
    private static readonly BrokerWidgetIdentity Identity = new("example.playback", "example.publisher", "one");
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
    private static void Reject(Action action, string message)
    { try { action(); } catch (Exception error) when (error is IOException or UnauthorizedAccessException or BrokerException) { return; } throw new Exception(message); }
    internal static Task RetainedOriginalAuthority() => RetainedOriginalAuthority("video/mp4");
    internal static Task RetainedOriginalPngAuthority() => RetainedOriginalAuthority("image/png");
    private static Task RetainedOriginalAuthority(string contentType)
    {
        var root = Path.Combine(Path.GetTempPath(), "widgetrail-playback-fixture", Guid.NewGuid().ToString("N"));
        var folder = Path.Combine(root, "originals"); Directory.CreateDirectory(folder);
        var video = contentType == "video/mp4";
        var path = Path.Combine(folder, video ? "video.mp4" : "image.png");
        File.WriteAllBytes(path, video ? [0,0,0,16,102,116,121,112,0,0,0,0,0,0,0,0]
            : Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+aZxkAAAAASUVORK5CYII="));
        var bytes = File.ReadAllBytes(path); var owner = new object(); var clock = new Clock();
        try
        {
            using var registry = new WindowCaptureRegistry(clock, maximumBytes: 1);
            CaptureLibraryReadLease Open()
            {
                var pinned = CapturePinnedFile.Open(path);
                return new(new(Guid.NewGuid().ToString("N"), contentType, video ? 1280 : 1, video ? 720 : 1, pinned.Stream.Length, video ? 7200 : 0, long.MaxValue)
                    { Origin = CaptureAttachmentOrigin.SavedLibrary }, pinned.Stream, authority: pinned);
            }
            var attachment = registry.AdoptLibraryMedia(owner, Identity, Open(), default);
            var resolved = registry.Resolve(Identity, attachment.Id);
            Check(resolved.Path == path && resolved.RequiredCapability == PlatformCapabilities.CaptureLibraryReadV1, "Playback copied the file or lost read permission ownership");
            Reject(() => File.WriteAllBytes(path, [9]), "Leased original was writable");
            Reject(() => File.Delete(path), "Leased original was deletable");
            Reject(() => Directory.Move(folder, folder + "-swapped"), "A pinned ancestor could be swapped before host path reopen");
            Reject(() => registry.Read(new object(), new(attachment.Id, 0), PlatformCapabilities.CaptureLibraryReadV1), "Foreign widget read another original");
            Reject(() => registry.Read(owner, new(attachment.Id, 0)), "Capture authority substituted for library read authority");
            Check(registry.Read(owner, new(attachment.Id, 0), PlatformCapabilities.CaptureLibraryReadV1).Bytes.SequenceEqual(bytes), "Chunk export changed original bytes");
            for (var i = 0; i < 100; i++)
            {
                clock.Now = clock.Now.AddSeconds(90);
                var renewed = registry.Resolve(Identity, attachment.Id);
                Check(renewed.Attachment == attachment && renewed.LeaseExpiresAtUnixMilliseconds > clock.Now.ToUnixTimeMilliseconds(), "Active playback lease failed to renew or changed its opaque attachment");
            }
            registry.Discard(owner, attachment.Id, PlatformCapabilities.CaptureLibraryReadV1);
            Check(File.ReadAllBytes(path).SequenceEqual(bytes), "Release deleted or modified an original");
            Directory.Move(folder, folder + "-swapped"); Directory.Move(folder + "-swapped", folder);
            attachment = registry.AdoptLibraryMedia(owner, Identity, Open(), default);
            registry.Retire(owner, PlatformCapabilities.CaptureLibraryReadV1);
            Check(File.Exists(path), "Read-permission revocation deleted original");
            attachment = registry.AdoptLibraryMedia(owner, Identity, Open(), default);
            clock.Now = clock.Now.AddMinutes(3);
            Reject(() => registry.Resolve(Identity, attachment.Id), "Idle expired playback remained usable");
            Check(File.Exists(path), "Expiry deleted original");
            attachment = registry.AdoptLibraryMedia(owner, Identity, Open(), default);
            registry.Dispose(); registry.Dispose();
            Check(File.ReadAllBytes(path).SequenceEqual(bytes), "Registry disposal deleted original");
            var before = CapturePinnedFile.Open(path); var identityBefore = before.Identity; before.Dispose();
            var stamp = File.GetLastWriteTimeUtc(path);
            var changedBytes = bytes.ToArray(); changedBytes[0] ^= 1;
            File.WriteAllBytes(path, changedBytes); File.SetLastWriteTimeUtc(path, stamp);
            using (var changed = CapturePinnedFile.Open(path)) Check(changed.Identity != identityBefore && changed.Identity.FileIdLow == identityBefore.FileIdLow,
                "In-place edit preserving length and mtime did not change native identity");
            File.Delete(path); File.WriteAllBytes(path, bytes); File.SetLastWriteTimeUtc(path, stamp);
            using (var replaced = CapturePinnedFile.Open(path)) Check(replaced.Identity != identityBefore, "Replacement preserving mtime reused native identity");
            return Task.CompletedTask;
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
    internal static async Task LargeLinkedVideo()
    {
        var root = Path.Combine(Path.GetTempPath(), "widgetrail-playback-large", Guid.NewGuid().ToString("N"));
        var folder = Path.Combine(root, "provider"); Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, "long.mp4");
        var length = (long)CaptureLimits.MaximumAttachmentBytes + 4096;
        using (var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        { output.Write([0,0,0,16,102,116,121,112,0,0,0,0,0,0,0,0]); output.SetLength(length); }
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddSeconds(-10));
        try
        {
            using var store = new CaptureLibraryStore(Path.Combine(root, "metadata"), Path.Combine(root, "owned"), null, null,
                (file, previous, token) => Task.FromResult(previous with { Width = 1920, Height = 1080, ByteLength = new FileInfo(file).Length, DurationSeconds = 7200 }));
            var source = await store.AddSourceAsync(folder, "Long recordings", Identity, default);
            var watch = Stopwatch.StartNew();
            var linked = (await store.LinkAsync(new(source.Id, "long.mp4"), Identity, default)).Item;
            var lease = await store.OpenAsync(linked.Id, CaptureImageResolution.Original, default);
            using var registry = new WindowCaptureRegistry(maximumBytes: 1);
            var attachment = registry.AdoptLibraryMedia(new object(), Identity, lease, default);
            watch.Stop();
            Check(attachment.ByteLength == length && attachment.DurationSeconds == 7200 && registry.Resolve(Identity, attachment.Id).Path == path,
                "Long linked recording used temporary capture limits or a copied path");
            Check(!Directory.EnumerateFiles(Path.Combine(root, "owned")).Any(), "Linked playback copied a video into owned storage");
            Check(!(attachment with { Origin = CaptureAttachmentOrigin.WindowCapture }).IsWellFormed(), "Saved playback limits widened temporary capture validation");
            await store.RemoveSourceAsync(source.Id, Identity, default);
            Reject(() => registry.Resolve(Identity, attachment.Id), "Disconnected source retained playback authority");
            Check(File.Exists(path), "Source disconnect deleted original video");
            Console.WriteLine($"Large linked playback: {length:N0} bytes / 2h, index+open+adopt {watch.ElapsedMilliseconds} ms, no original copy.");
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
    internal static async Task OriginalPngUsesPinnedLibraryAuthority()
    {
        using var fixture = new Fixture();
        var saved = await fixture.Store.SaveAsync(fixture.Source, default);
        using (var registry = new WindowCaptureRegistry(maximumBytes: 1))
        {
            var lease = await fixture.Store.OpenAsync(saved.Id, CaptureImageResolution.Original, saved.Revision, default);
            var attachment = registry.AdoptLibraryMedia(new object(), Identity, lease, default);
            Check(registry.Resolve(Identity, attachment.Id).Path == fixture.MediaPath(saved.Id), "Original PNG was copied before native playback");
            Reject(() => Directory.Move(fixture.Exports, fixture.Exports + "-swapped"), "PNG playback did not pin its original ancestor");
        }
        Check(File.ReadAllBytes(fixture.MediaPath(saved.Id)).SequenceEqual(fixture.Bytes), "PNG playback cleanup changed the original");
        var provider = Path.Combine(fixture.Root, "provider"); Directory.CreateDirectory(provider);
        var path = Path.Combine(provider, "linked.png"); File.WriteAllBytes(path, fixture.Bytes);
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddSeconds(-10));
        using var store = new CaptureLibraryStore(Path.Combine(fixture.Root, "linked-index"), Path.Combine(fixture.Root, "linked-owned"), null, null,
            (file, previous, _) => Task.FromResult(previous with { Width = 100, Height = 100, ByteLength = new FileInfo(file).Length }));
        var source = await store.AddSourceAsync(provider, "Linked screenshots", Identity, default);
        var linked = (await store.LinkAsync(new(source.Id, "linked.png"), Identity, default)).Item;
        using (var registry = new WindowCaptureRegistry(maximumBytes: 1))
        {
            var lease = await store.OpenAsync(linked.Id, CaptureImageResolution.Original, linked.Revision, default);
            var attachment = registry.AdoptLibraryMedia(new object(), Identity, lease, default);
            Check(registry.Resolve(Identity, attachment.Id).Path == path, "Linked PNG playback copied an original");
            await store.RemoveSourceAsync(source.Id, Identity, default);
            Reject(() => registry.Resolve(Identity, attachment.Id), "Disconnected PNG source retained native playback authority");
        }
        Check(File.ReadAllBytes(path).SequenceEqual(fixture.Bytes), "Source disconnection or PNG retirement deleted provider content");
    }
    private sealed class Clock : TimeProvider
    { internal DateTimeOffset Now = DateTimeOffset.UtcNow; public override DateTimeOffset GetUtcNow() => Now; }
}
