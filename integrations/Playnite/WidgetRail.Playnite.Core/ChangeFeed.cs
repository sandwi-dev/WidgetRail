using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace WidgetRail.Playnite;

public sealed class IntegrationEvent
{
    public long Sequence { get; internal set; }
    public string Kind { get; }
    public string? GameId { get; }
    public string? Provider { get; }
    public string? AchievementId { get; }
    public long ObservedAt { get; } = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    public IntegrationEvent(string kind, string? gameId = null, string? provider = null, string? achievementId = null)
    {
        if (kind.Length == 0 || kind.Length > 64 || gameId != null && !Guid.TryParseExact(gameId, "D", out _) ||
            provider?.Length > 64 || achievementId?.Length > 256) throw new ArgumentException("Invalid integration event.");
        Kind = kind; GameId = gameId; Provider = provider; AchievementId = achievementId;
    }
}

public sealed class EventBatch
{
    public bool ResetRequired { get; }
    public long NextSequence { get; }
    public IReadOnlyList<IntegrationEvent> Events { get; }
    internal EventBatch(bool reset, long next, IReadOnlyList<IntegrationEvent> events)
    { ResetRequired = reset; NextSequence = next; Events = events; }
}

/// <summary>Small change notifications, never a second library or provider database.</summary>
public sealed class ChangeFeed : IDisposable
{
    private readonly object gate = new object();
    private readonly Queue<IntegrationEvent> events = new Queue<IntegrationEvent>();
    private readonly SemaphoreSlim waiters = new SemaphoreSlim(2);
    private readonly CancellationTokenSource lifetime = new CancellationTokenSource();
    private TaskCompletionSource<bool> signal = NewSignal();
    private long sequence;
    private bool disposed;
    private static TaskCompletionSource<bool> NewSignal() => new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
    public long Sequence { get { lock (gate) return sequence; } }
    public void Publish(IntegrationEvent value)
    {
        lock (gate)
        {
            if (disposed) return;
            value.Sequence = ++sequence; events.Enqueue(value);
            while (events.Count > 256) events.Dequeue();
            var previous = signal; signal = NewSignal(); previous.TrySetResult(true);
        }
    }
    private EventBatch Read(long after)
    {
        if (after < 0 || after > sequence || events.Count != 0 && after < events.Peek().Sequence - 1)
            return new EventBatch(true, sequence, Array.Empty<IntegrationEvent>());
        var selected = events.Where(item => item.Sequence > after).Take(128).ToArray();
        return new EventBatch(false, selected.Length == 0 ? sequence : selected[selected.Length - 1].Sequence, selected);
    }
    public async Task<EventBatch> PollAsync(long after, int waitMilliseconds, CancellationToken token)
    {
        if (waitMilliseconds < 0 || waitMilliseconds > 25000) throw new ArgumentOutOfRangeException(nameof(waitMilliseconds));
        Task changed;
        lock (gate)
        {
            if (disposed) throw new ObjectDisposedException(nameof(ChangeFeed));
            var ready = Read(after);
            if (ready.ResetRequired || ready.Events.Count != 0 || waitMilliseconds == 0) return ready;
            if (!waiters.Wait(0)) throw new EventWaitCapacityException();
            changed = signal.Task;
        }
        try
        {
            using (var linked = CancellationTokenSource.CreateLinkedTokenSource(token, lifetime.Token))
            {
                await Task.WhenAny(changed, Task.Delay(waitMilliseconds, linked.Token)).ConfigureAwait(false);
                linked.Token.ThrowIfCancellationRequested();
                // Retire the timeout registration promptly when a change wins.
                linked.Cancel();
            }
            lock (gate) return Read(after);
        }
        finally { waiters.Release(); }
    }
    public void Dispose()
    {
        lock (gate) { if (disposed) return; disposed = true; lifetime.Cancel(); signal.TrySetResult(true); events.Clear(); }
    }
}

public sealed class EventWaitCapacityException : InvalidOperationException { }
