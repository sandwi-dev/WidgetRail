namespace WidgetRail.OverlayFrontend.WinUI.Browser;

/// <summary>Reserves host browser capacity before asynchronous startup; late registration cannot recreate a retired session.</summary>
internal sealed class BrowserSessionStartups(int maximum = 4)
{
    private readonly object gate = new();
    private readonly Dictionary<(string Widget, string Document), Lease> pending = [];

    internal int CountResidents(IEnumerable<(string Widget, string Document)> residents)
    { lock (gate) return residents.Concat(pending.Keys).Distinct().Count(); }

    internal Lease? TryReserve((string Widget, string Document) key,
        IEnumerable<(string Widget, string Document)> residents, CancellationToken lifetime)
    {
        lock (gate)
        {
            var occupied = residents.Concat(pending.Keys).ToHashSet();
            if (lifetime.IsCancellationRequested || occupied.Contains(key) || occupied.Count >= maximum) return null;
            var lease = new Lease(this, key, lifetime);
            pending.Add(key, lease);
            return lease;
        }
    }

    internal void Retire((string Widget, string Document) key)
    {
        Lease? lease;
        lock (gate) pending.Remove(key, out lease);
        lease?.Cancel();
    }

    internal void RetireMissing(IReadOnlySet<string> liveSessions)
    {
        (string Widget, string Document)[] missing;
        lock (gate) missing = pending.Keys.Where(key => !liveSessions.Contains(key.Document)).ToArray();
        foreach (var key in missing) Retire(key);
    }

    internal void RetireWidget(string widgetId)
    {
        (string Widget, string Document)[] missing;
        lock (gate) missing = pending.Keys.Where(key => key.Widget == widgetId).ToArray();
        foreach (var key in missing) Retire(key);
    }

    internal void RetireAll()
    {
        (string Widget, string Document)[] keys;
        lock (gate) keys = pending.Keys.ToArray();
        foreach (var key in keys) Retire(key);
    }

    internal sealed class Lease : IDisposable
    {
        private readonly BrowserSessionStartups owner;
        private readonly (string Widget, string Document) key;
        private readonly CancellationTokenSource cancellation;
        private bool disposed;
        internal Lease(BrowserSessionStartups owner, (string Widget, string Document) key, CancellationToken lifetime)
        { this.owner = owner; this.key = key; cancellation = CancellationTokenSource.CreateLinkedTokenSource(lifetime); Token = cancellation.Token; }
        internal CancellationToken Token { get; }
        internal bool IsCurrent
        { get { lock (owner.gate) return !disposed && !Token.IsCancellationRequested && ReferenceEquals(owner.pending.GetValueOrDefault(key), this); } }

        /// <summary>The commit must not await. The lease remains reserved through subsequent native initialization.</summary>
        internal bool TryCommit(Action commit)
        {
            lock (owner.gate)
            {
                if (!IsCurrent) return false;
                commit();
                return true;
            }
        }
        internal void Cancel()
        { lock (owner.gate) { if (!disposed) cancellation.Cancel(); } }
        public void Dispose()
        {
            lock (owner.gate)
            {
                if (disposed) return;
                disposed = true;
                if (ReferenceEquals(owner.pending.GetValueOrDefault(key), this)) owner.pending.Remove(key);
                cancellation.Dispose();
            }
        }
    }
}
