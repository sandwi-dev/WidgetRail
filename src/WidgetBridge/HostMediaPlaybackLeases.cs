using WidgetRail.PlatformBroker;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.WidgetBridge;

/// <summary>Frontend playback custody; cached declarations never hold an open original.
/// Reuses the capture registry's retained-original lease and idle expiry, without another timer.</summary>
internal sealed class HostMediaPlaybackLeases : IDisposable, IAsyncDisposable
{
    private sealed class Entry
    {
        internal Entry(BridgeMediaPlayerRequest request) { Request = request; Token = Lifetime.Token; }
        internal readonly BridgeMediaPlayerRequest Request;
        internal readonly SemaphoreSlim Serial = new(1, 1);
        internal readonly CancellationTokenSource Lifetime = new();
        internal readonly CancellationToken Token;
        internal CancellationTokenRegistration Authority;
        internal CaptureAttachment? Attachment;
        internal bool Closed;
        internal bool CleanupComplete;
        internal int Active;
        internal readonly TaskCompletionSource Drained = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal DateTimeOffset Expires = DateTimeOffset.UtcNow.AddMinutes(2);
    }
    private readonly object gate = new();
    private readonly Dictionary<string, Entry> entries = new(StringComparer.Ordinal);
    private readonly Dictionary<string, (DateTimeOffset Expires, Entry? Entry)> closed = new(StringComparer.Ordinal);
    private bool disposed;
    internal int PendingOrOpenCount { get { lock (gate) return entries.Count; } }

    internal async Task<HostMediaPlayerSource> ResolveAsync(BridgeMediaPlayerRequest request,
        BridgeClientRegistry.MediaPlayerAdmission admission, CaptureLibraryStore library, CancellationToken token)
    {
        if (!CaptureLimits.ValidToken(request.PlaybackLeaseId) || request.Media.Source.SavedCapture is not { } saved)
            throw new BridgeProtocolException("Saved media requires a current playback demand.");
        var id = request.PlaybackLeaseId!;
        Entry entry;
        Sweep();
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (closed.ContainsKey(id)) throw new BridgeProtocolException("Playback demand retired.");
            if (!entries.TryGetValue(id, out entry!))
            {
                if (entries.Count >= 16 || closed.Count >= 512 || entries.Values.Count(value => value.Request.WidgetId == request.WidgetId) >= 4)
                    throw new BridgeProtocolException("Native playback admission is full.");
                entries.Add(id, entry = new(request));
            }
            else if (entry.Request.WidgetId != request.WidgetId || entry.Request.WorkerRun != request.WorkerRun ||
                entry.Request.ElementId != request.ElementId || entry.Request.IndexedOrigin != request.IndexedOrigin ||
                entry.Request.Media.Source != request.Media.Source || entry.Request.Media.Revision != request.Media.Revision)
                throw new BridgeProtocolException("Playback demand changed owner or content.");
            ++entry.Active;
        }
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(token, admission.Lifetime, entry.Token);
        var ct = cancellation.Token;
        var entered = false;
        try
        {
            await entry.Serial.WaitAsync(ct).ConfigureAwait(false); entered = true;
            if (!admission.IsCurrent) throw new BridgeProtocolException("Playback declaration retired.");
            if (entry.Attachment is null)
            {
                var source = await library.OpenAsync(saved.Id, CaptureImageResolution.Original, saved.Revision, ct).ConfigureAwait(false);
                try
                {
                    ct.ThrowIfCancellationRequested();
                    if (!admission.IsCurrent) throw new BridgeProtocolException("Playback declaration retired while opening.");
                    var widget = admission.Widget;
                    var identity = new BrokerWidgetIdentity(widget.PackageId, widget.PublisherId, widget.InstanceId);
                    lock (gate)
                    {
                        ct.ThrowIfCancellationRequested();
                        if (entry.Closed) throw new OperationCanceledException(ct);
                        entry.Attachment = WindowCaptureRegistry.Shared.AdoptLibraryMedia(entry, identity, source, ct);
                        source = null!; // Ownership moved to the existing retained-original registry.
                    }
                }
                finally { if (source is not null) await source.DisposeAsync().ConfigureAwait(false); }
                // Indexed withdrawal/worker retirement cancels the existing authority.
                var registration = admission.Lifetime.Register(() => Release(id));
                var keep = false;
                lock (gate) { if (!entry.Closed) { entry.Authority = registration; keep = true; } }
                if (!keep) registration.Dispose();
            }
            ct.ThrowIfCancellationRequested();
            if (!admission.IsCurrent) throw new BridgeProtocolException("Playback declaration retired before publication.");
            var owner = admission.Widget;
            var resolved = WindowCaptureRegistry.Shared.Resolve(new(owner.PackageId, owner.PublisherId, owner.InstanceId), entry.Attachment!.Id);
            lock (gate)
            {
                if (entry.Closed) throw new OperationCanceledException(ct);
                entry.Expires = DateTimeOffset.UtcNow.AddMinutes(2);
            }
            return new(request.Media, new Uri(resolved.Path).AbsoluteUri, entry.Attachment.ContentType == "image/png",
                entry.Attachment.Width, entry.Attachment.Height, entry.Attachment.DurationSeconds,
                resolved.LeaseExpiresAtUnixMilliseconds ?? entry.Attachment.ExpiresAtUnixMilliseconds, RequiresRevalidation: true) { PlaybackLeaseId = id };
        }
        catch { Release(id); throw; }
        finally
        {
            if (entered) entry.Serial.Release();
            lock (gate) { if (--entry.Active == 0 && entry.Closed && entry.CleanupComplete) entry.Drained.TrySetResult(); }
        }
    }
    internal void Release(string id) => Retire(id);
    internal Task ReleaseAsync(string id) => Retire(id)?.Drained.Task ?? Task.CompletedTask;
    private Entry? Retire(string id)
    {
        if (!CaptureLimits.ValidToken(id)) throw new BridgeProtocolException("Invalid playback demand.");
        Entry? retired;
        lock (gate)
        {
            if (!entries.Remove(id, out retired) && closed.TryGetValue(id, out var previous)) return previous.Entry;
            // An explicit cancellation can arrive before the matching acquire is admitted.
            // Retain its bounded tombstone so a late request cannot create an orphan.
            if (closed.Count < 528) closed[id] = (DateTimeOffset.UtcNow.AddMinutes(2), retired);
            if (retired is not null) retired.Closed = true;
        }
        if (retired is null) return null;
        retired.Lifetime.Cancel();
        WindowCaptureRegistry.Shared.Retire(retired);
        retired.Authority.Dispose();
        retired.Lifetime.Dispose();
        lock (gate) { retired.CleanupComplete = true; if (retired.Active == 0) retired.Drained.TrySetResult(); }
        return retired;
    }
    private void Sweep()
    {
        var now = DateTimeOffset.UtcNow;
        string[] expired;
        lock (gate)
        {
            foreach (var id in closed.Where(pair => pair.Value.Expires <= now && pair.Value.Entry?.Drained.Task.IsCompleted != false).Select(pair => pair.Key).ToArray()) closed.Remove(id);
            expired = entries.Where(pair => pair.Value.Expires <= now).Select(pair => pair.Key).ToArray();
        }
        foreach (var id in expired) Release(id);
    }
    public void Dispose()
    {
        string[] ids;
        lock (gate) { if (disposed) return; disposed = true; ids = entries.Keys.ToArray(); }
        foreach (var id in ids) Release(id);
    }
    public async ValueTask DisposeAsync()
    {
        Dispose();
        Task[] pending;
        lock (gate) pending = closed.Values.Where(value => value.Entry is not null).Select(value => value.Entry!.Drained.Task).ToArray();
        await Task.WhenAll(pending).ConfigureAwait(false);
    }
}
