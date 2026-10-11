using WidgetRail.WidgetProtocol;

namespace WidgetRail.PlatformBroker;

public sealed record HostPermissionRequest(string RequestId, BrokerWidgetIdentity Widget, IReadOnlyList<string> CapabilityIds);

/// <summary>Bounded, worker-owned consent requests. Only the authenticated frontend may complete a prompt.</summary>
internal sealed class PermissionRequestRegistry(TimeProvider? clock = null)
{
    internal static PermissionRequestRegistry Shared { get; } = new();
    private readonly object gate = new();
    private readonly TimeProvider time = clock ?? TimeProvider.System;
    private readonly Dictionary<string, Entry> entries = new(StringComparer.Ordinal);
    private sealed class Entry(object owner, HostPermissionRequest request, DateTimeOffset expires,
        Func<WidgetPermissionOutcome, CancellationToken, Task> commit)
    {
        internal readonly object Owner = owner;
        internal readonly HostPermissionRequest Request = request;
        internal DateTimeOffset Expires = expires;
        internal readonly Func<WidgetPermissionOutcome, CancellationToken, Task> Commit = commit;
        internal readonly CancellationTokenSource Cancellation = new();
        internal WidgetPermissionOutcome Outcome = WidgetPermissionOutcome.Pending;
        internal bool Completing;
    }
    internal WidgetPermissionRequestState Begin(object owner, BrokerWidgetIdentity identity, IReadOnlyList<string> ids,
        Func<WidgetPermissionOutcome, CancellationToken, Task> commit)
    {
        lock (gate)
        {
            Sweep();
            // One modal at a time, no background prompt queue or repeated prompt flood.
            if (entries.Count >= 16 || entries.Values.Any(e => e.Outcome == WidgetPermissionOutcome.Pending)) return new(WidgetPermissionOutcome.Unavailable);
            var id = Guid.NewGuid().ToString("N");
            entries.Add(id, new(owner, new(id, identity, ids.ToArray()), time.GetUtcNow().AddMinutes(2), commit));
            return new(WidgetPermissionOutcome.Pending, id);
        }
    }
    internal HostPermissionRequest? Take()
    { lock (gate) { Sweep(); return entries.Values.FirstOrDefault(e => e.Outcome == WidgetPermissionOutcome.Pending && !e.Completing)?.Request; } }
    internal bool IsCurrent(string id)
    { lock (gate) { Sweep(); return entries.TryGetValue(id, out var e) && e.Outcome == WidgetPermissionOutcome.Pending; } }
    internal WidgetPermissionRequestState Status(object owner, string id)
    {
        lock (gate)
        {
            Sweep();
            if (!entries.TryGetValue(id, out var entry) || !ReferenceEquals(entry.Owner, owner)) return new(WidgetPermissionOutcome.Unavailable, id);
            return new(entry.Outcome, id);
        }
    }
    internal void Cancel(object owner, string id)
    { lock (gate) { if (entries.TryGetValue(id, out var entry) && ReferenceEquals(owner, entry.Owner)) CancelEntry(entry); } }
    internal void CancelOwner(object owner, bool remove = false)
    {
        lock (gate)
            foreach (var (id, entry) in entries.Where(p => ReferenceEquals(p.Value.Owner, owner)).ToArray())
            {
                CancelEntry(entry);
                if (remove) { entries.Remove(id); if (!entry.Completing) entry.Cancellation.Dispose(); }
            }
    }
    internal async Task<bool> CompleteAsync(string id, WidgetPermissionOutcome outcome, CancellationToken token)
    {
        if (outcome is not (WidgetPermissionOutcome.Granted or WidgetPermissionOutcome.Denied or WidgetPermissionOutcome.Cancelled or WidgetPermissionOutcome.Unavailable)) return false;
        Entry entry; CancellationToken ownedToken;
        lock (gate)
        {
            Sweep();
            if (!entries.TryGetValue(id, out entry!) || entry.Outcome != WidgetPermissionOutcome.Pending || entry.Completing) return false;
            entry.Completing = true; ownedToken = entry.Cancellation.Token;
        }
        try
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, ownedToken);
            linked.Token.ThrowIfCancellationRequested();
            if (outcome is WidgetPermissionOutcome.Granted or WidgetPermissionOutcome.Denied)
                await entry.Commit(outcome, linked.Token).ConfigureAwait(false);
            lock (gate)
            {
                if (entry.Outcome != WidgetPermissionOutcome.Pending) return false;
                entry.Outcome = outcome; entry.Expires = time.GetUtcNow().AddMinutes(1); return true;
            }
        }
        catch (Exception error) when (error is BrokerException or IOException or UnauthorizedAccessException or OperationCanceledException)
        { lock (gate) if (entry.Outcome == WidgetPermissionOutcome.Pending) entry.Outcome = WidgetPermissionOutcome.Unavailable; return false; }
        finally
        {
            lock (gate)
            {
                entry.Completing = false;
                if (!entries.ContainsKey(id)) entry.Cancellation.Dispose();
            }
        }
    }
    private void CancelEntry(Entry entry)
    {
        if (entry.Outcome != WidgetPermissionOutcome.Pending) return;
        entry.Outcome = WidgetPermissionOutcome.Cancelled;
        entry.Expires = time.GetUtcNow().AddMinutes(1);
        entry.Cancellation.Cancel();
    }
    private void Sweep()
    {
        foreach (var (id, entry) in entries.Where(p => p.Value.Expires <= time.GetUtcNow()).ToArray())
        {
            if (entry.Outcome == WidgetPermissionOutcome.Pending) { CancelEntry(entry); entry.Outcome = WidgetPermissionOutcome.Unavailable; }
            else { entries.Remove(id); if (!entry.Completing) entry.Cancellation.Dispose(); }
        }
    }
}
