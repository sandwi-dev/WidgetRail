namespace WidgetRail.PlatformBroker;

/// <summary>Tracks work accepted by the host under one exact action grant.</summary>
internal sealed class HostActionCompletion
{
    private readonly object gate = new();
    private Task? hostWork;
    private bool sealedRegistration;

    // The capability registers before returning its acceptance to the worker.
    internal void Register(Task completion)
    {
        lock (gate)
        {
            if (!sealedRegistration)
                hostWork = hostWork is null ? completion : Task.WhenAll(hostWork, completion);
        }
    }

    internal async Task<T> WaitAsync<T>(Task<T> worker)
    {
        var outcome = await worker.ConfigureAwait(false);
        Task? pending;
        lock (gate)
        {
            sealedRegistration = true;
            pending = hostWork;
        }
        if (pending is not null)
            await pending.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        return outcome;
    }
}
