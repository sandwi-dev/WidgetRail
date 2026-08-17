using System.Collections.Concurrent;

namespace WidgetRail.WindowsAudioProvider;

/// <summary>
/// Separates callback-owned values by endpoint generation so a late callback from an
/// unregistered manager can never be adopted by the replacement endpoint.
/// </summary>
internal sealed class EndpointGenerationQueue<T>
{
    private readonly ConcurrentQueue<Entry> _entries = new();

    public void Enqueue(long generation, T value) => _entries.Enqueue(new Entry(generation, value));

    public IEnumerable<T> DrainCurrent(long generation, Action<T> releaseStale)
    {
        while (_entries.TryDequeue(out var entry))
        {
            if (entry.Generation == generation) yield return entry.Value;
            else releaseStale(entry.Value);
        }
    }

    public void DrainAll(Action<T> release)
    {
        while (_entries.TryDequeue(out var entry)) release(entry.Value);
    }

    private sealed record Entry(long Generation, T Value);
}
