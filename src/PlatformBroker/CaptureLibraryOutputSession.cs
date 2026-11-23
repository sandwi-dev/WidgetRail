using WidgetRail.WidgetProtocol;

namespace WidgetRail.PlatformBroker;

/// <summary>Trusted encoder output reservation. It shares the library's crash-safe edit/copy publication lane.</summary>
public sealed class CaptureLibraryOutputSession : IDisposable
{
    private readonly CaptureLibraryStore store;
    private readonly Action release;
    private readonly FileStream jobLock;
    private readonly object gate = new();
    private readonly CancellationTokenSource lifetime = new();
    private readonly CancellationTokenRegistration storeRetirement;
    private bool closed, committing, released, cancelling;
    internal CaptureLibraryOutputSession(CaptureLibraryStore store, string id, string path, BrokerWidgetIdentity actor,
        long maximumBytes, double maximumDurationSeconds, FileStream jobLock, CancellationToken lifetime, Action release)
    {
        this.store = store; Id = id; Path = path; Actor = actor; MaximumBytes = maximumBytes; this.jobLock = jobLock;
        MaximumDurationSeconds = maximumDurationSeconds;
        Lifetime = this.lifetime.Token; this.release = release;
        storeRetirement = lifetime.Register(Dispose);
    }
    public string Id { get; }
    public string Path { get; }
    public long MaximumBytes { get; }
    public double MaximumDurationSeconds { get; }
    internal BrokerWidgetIdentity Actor { get; }
    internal CancellationToken Lifetime { get; }
    internal bool IsCurrent { get { lock (gate) return !closed && !Lifetime.IsCancellationRequested; } }
    internal bool Published { get; set; }
    internal bool RecoveryRequired { get; set; }
    public Task<CaptureLibraryItem> CommitAsync(CaptureAttachment media, long capturedAtUnixMilliseconds,
        Func<bool> isAuthorized, CancellationToken cancellationToken = default)
        =>CommitCoreAsync(media,capturedAtUnixMilliseconds,isAuthorized,cancellationToken,false);
    internal Task<CaptureLibraryItem> CommitDraftAsync(CaptureAttachment media,long capturedAtUnixMilliseconds,
        Func<bool> isAuthorized,CancellationToken cancellationToken, Func<CancellationToken, Task>? reauthorize = null)
        =>CommitCoreAsync(media,capturedAtUnixMilliseconds,isAuthorized,cancellationToken,true,reauthorize);
    private async Task<CaptureLibraryItem> CommitCoreAsync(CaptureAttachment media,long capturedAtUnixMilliseconds,
        Func<bool> isAuthorized,CancellationToken cancellationToken,bool preserveUnpublished, Func<CancellationToken, Task>? reauthorize = null)
    {
        lock (gate)
        {
            if (!IsCurrent || committing) throw new BrokerException("capture_library_output_retired", "The output reservation is no longer available.");
            committing = true;
        }
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(Lifetime, cancellationToken);
        try { return await store.CommitOutputAsync(this, media, capturedAtUnixMilliseconds, isAuthorized, lifetime.Token, reauthorize).ConfigureAwait(false); }
        finally
        {
            lock (gate) { committing = false; if (closed && !cancelling) Release(); }
            if(!preserveUnpublished || Published || RecoveryRequired)Dispose();
        }
    }
    public void Dispose()
    {
        lock (gate) { if (closed) return; closed = true; cancelling = true; }
        try { lifetime.Cancel(); }
        finally { lock (gate) { cancelling = false; if (!committing) Release(); } }
    }
    private void Release()
    {
        if (released) return; released = true;
        try { store.RetireOutput(this); }
        finally { jobLock.Dispose(); store.ReleaseOutputLock(Id); storeRetirement.Unregister(); lifetime.Dispose(); release(); }
    }
}
