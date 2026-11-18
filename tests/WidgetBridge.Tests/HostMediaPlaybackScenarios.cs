using WidgetRail.PlatformBroker;
using WidgetRail.WidgetBridge;
using WidgetRail.WidgetProtocol;

internal static class HostMediaPlaybackScenarios
{
    internal static async Task RetainedOriginalAndRetirement()
    {
        await using var fixture = new Fixture();
        var saved = await fixture.Save();
        using var authority = new CancellationTokenSource();
        using var admission = fixture.Admission(authority.Token);
        var request = fixture.Request(saved);
        var media = await fixture.Leases.ResolveAsync(request, admission, fixture.Store, default);
        var path = new Uri(media.Uri).LocalPath;
        Check(path.StartsWith(fixture.Owned, StringComparison.OrdinalIgnoreCase) && File.ReadAllBytes(path).SequenceEqual(fixture.Bytes),
            "Preview copied or changed the stored original");
        Check(media.Media.Source.SavedCapture == new SavedCaptureMediaReference(saved.Id, saved.Revision) && media.Media.Source.Attachment is null &&
            media.PlaybackLeaseId == request.PlaybackLeaseId && fixture.Leases.PendingOrOpenCount == 1, "Cache declaration acquired an attachment or duplicate resource");
        Check((await fixture.Leases.ResolveAsync(request, admission, fixture.Store, default)).Uri == media.Uri && fixture.Leases.PendingOrOpenCount == 1,
            "Authority renewal opened a second original");
        ExpectLocked(path);
        authority.Cancel();
        await fixture.Leases.ReleaseAsync(request.PlaybackLeaseId!);
        Check(fixture.Leases.PendingOrOpenCount == 0, "Retired authority retained a playback owner");
        using (File.Open(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) { }
        await Reject(() => fixture.Leases.ResolveAsync(request, fixture.Admission(), fixture.Store, default));
        Check(fixture.Leases.PendingOrOpenCount == 0, "Replayed retired demand opened another file");
        var stale = fixture.Request(saved with { Revision = saved.Revision + 1 });
        await Reject(() => fixture.Leases.ResolveAsync(stale, fixture.Admission(), fixture.Store, default));
        Check(fixture.Leases.PendingOrOpenCount == 0, "Wrong content revision retained a file");
        var newerMetadata = await fixture.Store.UpdateMetadataAsync(new(saved.Id, saved.MetadataRevision, "A title", true), default);
        var renamed = fixture.Request(newerMetadata);
        _ = await fixture.Leases.ResolveAsync(renamed, fixture.Admission(), fixture.Store, default);
        Check(newerMetadata.Revision == saved.Revision, "Title/favorite metadata changed content identity");
        await fixture.Leases.DisposeAsync();
        using (File.Open(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) { }
        Check(fixture.Leases.PendingOrOpenCount == 0, "Host disposal retained native file custody");
    }

    internal static async Task PendingOpenDrainsBeforeReplacement()
    {
        await using var fixture = new Fixture();
        var first = await fixture.Save(); var second = await fixture.Save();
        Check(first.Id != second.Id, "Replacement fixture must use distinct media bytes");
        var initial = fixture.Request(first);
        var initialMedia = await fixture.Leases.ResolveAsync(initial, fixture.Admission(), fixture.Store, default);
        var firstPath = new Uri(initialMedia.Uri).LocalPath;
        await fixture.Leases.ReleaseAsync(initial.PlaybackLeaseId!);
        fixture.DelayPath = firstPath;
        File.SetLastWriteTimeUtc(firstPath, DateTime.UtcNow.AddSeconds(5));
        var slow = fixture.Request(first);
        var opening = fixture.Leases.ResolveAsync(slow, fixture.Admission(), fixture.Store, default);
        await fixture.InspectEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var retiring = fixture.Leases.ReleaseAsync(slow.PlaybackLeaseId!);
        Check(!retiring.IsCompleted, "Cancellation was mistaken for completion of a noncooperative open");
        var next = fixture.Request(second);
        var replacement = fixture.Leases.ResolveAsync(next, fixture.Admission(), fixture.Store, default);
        fixture.InspectRelease.TrySetResult();
        await Reject(() => opening);
        await retiring.WaitAsync(TimeSpan.FromSeconds(5));
        var resolved = await replacement;
        Check(resolved.Media.Source.SavedCapture?.Id == second.Id && fixture.Leases.PendingOrOpenCount == 1,
            "Late old open displaced or retired the new preview");
        using (File.Open(firstPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) { }
        await fixture.Leases.ReleaseAsync(next.PlaybackLeaseId!);
        Check(fixture.Leases.PendingOrOpenCount == 0, "Replacement withdrawal left a file owner");
    }
    private static async Task Reject(Func<Task> action)
    {
        try { await action(); }
        catch (Exception error) when (error is BrokerException or BridgeProtocolException or OperationCanceledException) { return; }
        throw new Exception("Invalid playback demand succeeded");
    }
    private static void ExpectLocked(string path)
    {
        try { using var write = File.Open(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None); }
        catch (IOException) { return; }
        throw new Exception("Original was not retained while playing");
    }
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    private sealed class Fixture : IAsyncDisposable
    {
        private readonly string root = Path.Combine(Path.GetTempPath(), "widgetrail-playback-owner-" + Guid.NewGuid().ToString("N"));
        internal string Owned => Path.Combine(root, "owned");
        internal readonly byte[] Bytes = Enumerable.Range(0, 128).Select(value => (byte)value).ToArray();
        internal readonly CaptureLibraryStore Store;
        internal readonly HostMediaPlaybackLeases Leases = new();
        internal string? DelayPath;
        internal readonly TaskCompletionSource InspectEntered = new(TaskCreationOptions.RunContinuationsAsynchronously), InspectRelease = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int saves;
        private readonly ConfiguredWidget widget = new() { Id = "media", PackageId = "example.media", PublisherId = "example", InstanceId = "media.instance", Name = "Media", WorkerExecutable = Environment.ProcessPath! };
        internal Fixture()
        {
            Directory.CreateDirectory(root);
            Store = new(Path.Combine(root, "index"), Owned, null, null, async (path, media, _) =>
            {
                if (path == DelayPath) { InspectEntered.TrySetResult(); await InspectRelease.Task; }
                return media;
            });
        }
        internal async Task<CaptureLibraryItem> Save()
        {
            var path = Path.Combine(root, Guid.NewGuid().ToString("N") + ".mp4");
            var bytes = Bytes.ToArray(); if (++saves > 1) bytes[0] = (byte)saves;
            await File.WriteAllBytesAsync(path, bytes);
            return await Store.SaveAsync(new(new(Guid.NewGuid().ToString("N"), "video/mp4", 64, 64, Bytes.Length, 5,
                DateTimeOffset.UtcNow.AddMinutes(1).ToUnixTimeMilliseconds()), path), default);
        }
        internal BridgeClientRegistry.MediaPlayerAdmission Admission(CancellationToken token = default) => new(widget, () => !token.IsCancellationRequested, token, () => { });
        internal BridgeMediaPlayerRequest Request(CaptureLibraryItem item) => new("media", new(1, 1), 1, "preview.player",
            new(MediaPlayerSource.FromSavedCapture(item.Id, item.Revision), new(AutoPlay: true, Muted: true))) { PlaybackLeaseId = Guid.NewGuid().ToString("N") };
        public async ValueTask DisposeAsync()
        {
            InspectRelease.TrySetResult(); await Leases.DisposeAsync(); Store.Dispose();
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }
}
