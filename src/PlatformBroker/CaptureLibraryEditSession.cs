using WidgetRail.WidgetProtocol;

namespace WidgetRail.PlatformBroker;

/// <summary>Verified editor source and one save identity. Host-only: never serialized to a widget.</summary>
internal sealed class CaptureLibraryEditSession : IDisposable
{
    private readonly object gate = new();
    private readonly CaptureLibraryStore store;
    private readonly Action release;
    private readonly CancellationTokenSource lifetime;
    private readonly CancellationToken lifetimeToken;
    private readonly CancellationTokenRegistration storeRetirement;
    private Timer? retirement;
    private bool closed, released, cancelling;
    private int users;
    private CaptureEditRecipe? savingRecipe;
    private Task<CaptureLibraryItem>? saving;
    internal CaptureLibraryEditSession(CaptureLibraryStore store, CaptureLibraryReadLease source, CaptureLibraryItem item,
        CaptureLibraryMutationReference? reference, BrokerWidgetIdentity actor, Func<bool> authorized, CancellationToken storeStop, Action release)
    {
        this.store = store; Source = source; Item = item; Reference = reference; Actor = actor; Authorized = authorized; this.release = release;
        ResultId = Guid.NewGuid().ToString("N"); lifetime = new CancellationTokenSource(); lifetimeToken = lifetime.Token;
        storeRetirement = storeStop.Register(Dispose);
        lock (gate) if (!closed) retirement = new Timer(_ => { if (!IsCurrent) Dispose(); }, null, 100, 100);
    }
    internal string ResultId { get; }
    internal CaptureAttachment Media => Item.Media;
    internal CaptureEditInput Input { get { lock (gate) { DemandCurrent(); return new(Media, Source.Stream.Name, Source.Stream); } } }
    internal CancellationToken LifetimeToken => lifetimeToken;
    internal CaptureLibraryMutationReference? Reference { get; }
    internal BrokerWidgetIdentity Actor { get; }
    internal CaptureLibraryItem Item { get; }
    internal CaptureLibraryReadLease Source { get; }
    internal Func<bool> Authorized { get; }
    internal bool IsCurrent
    {
        get
        {
            lock (gate)
                try { return !closed && !lifetime.IsCancellationRequested && Source.IsCurrent && Authorized(); }
                catch (Exception error) when (error is not (OutOfMemoryException or StackOverflowException or AccessViolationException)) { return false; }
        }
    }
    internal CaptureEditInputLease AcquireInput()
    {
        lock (gate) { DemandCurrent(); if (users != 0) throw new BrokerException("capture_library_edit_busy", "The editor input is already in use."); ++users; return new(new(Media, Source.Stream.Name, Source.Stream), EndUse); }
    }
    internal Task<CaptureLibraryItem> SaveAsync(CaptureEditRecipe recipe, CaptureEditCodec codec, IProgress<double>? progress, CancellationToken token)
    {
        lock (gate)
        {
            if (saving is not null)
            {
                if (savingRecipe != recipe) throw new BrokerException("capture_library_edit_already_saving", "This edit already has a save operation.");
                return saving;
            }
            DemandCurrent(); if (!recipe.IsValid(Media)) throw new BrokerException("capture_library_edit_invalid_recipe", "The edit is outside the source bounds.");
            if (users != 0) throw new BrokerException("capture_library_edit_busy", "The editor input is already in use.");
            savingRecipe = recipe; ++users; return saving = SaveCoreAsync(recipe, codec, progress, token);
        }
    }
    private async Task<CaptureLibraryItem> SaveCoreAsync(CaptureEditRecipe recipe, CaptureEditCodec codec, IProgress<double>? progress, CancellationToken token)
    {
        await Task.Yield();
        try
        {
            try { return await store.SaveSessionAsync(this, recipe, codec, progress, token).ConfigureAwait(false); }
            catch (Exception error) when (error is not (OutOfMemoryException or StackOverflowException or AccessViolationException))
            {
                // A committed copy survives cancellation or a lost final response. Reconcile its exact identity; never encode a replacement.
                using var reconciliation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                if (await store.RecoverEditResultAsync(ResultId, Actor, reconciliation.Token).ConfigureAwait(false) is { } committed) return committed;
                throw;
            }
        }
        finally { EndUse(); }
    }
    private void DemandCurrent()
    { if (!IsCurrent) throw new BrokerException("capture_library_edit_source_retired", "The editor source is no longer current."); }
    private void EndUse()
    { lock (gate) { if (--users == 0 && closed && !cancelling) Release(); } }
    public void Dispose()
    {
        lock (gate)
        {
            if (closed) return; closed = true; cancelling = true; retirement?.Dispose(); retirement = null;
        }
        try { lifetime.Cancel(); }
        finally { lock (gate) { cancelling = false; if (users == 0) Release(); } }
    }
    private void Release()
    {
        if (released) return; released = true; Source.Dispose(); storeRetirement.Unregister(); lifetime.Dispose(); release();
    }
}

internal sealed class CaptureEditInputLease(CaptureEditInput input, Action release) : IDisposable
{
    private Action? releasing = release;
    internal CaptureEditInput Input { get; } = input;
    public void Dispose() => Interlocked.Exchange(ref releasing, null)?.Invoke();
}

/// <summary>Registry-authenticated temporary capture. Taking the source transfers ownership to the editor session.</summary>
internal sealed class HostCaptureEditLease(BrokerWidgetIdentity actor, CaptureLibraryReadLease source, long? capturedAt) : IDisposable
{
    private CaptureLibraryReadLease? retained = source;
    internal BrokerWidgetIdentity Actor { get; } = actor;
    internal long? CapturedAt { get; } = capturedAt;
    internal CaptureLibraryReadLease Take() => Interlocked.Exchange(ref retained, null) ?? throw new ObjectDisposedException(nameof(HostCaptureEditLease));
    public void Dispose() => Interlocked.Exchange(ref retained, null)?.Dispose();
}
