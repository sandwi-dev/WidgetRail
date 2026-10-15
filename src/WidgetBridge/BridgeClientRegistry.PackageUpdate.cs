namespace WidgetRail.WidgetBridge;

internal sealed partial class BridgeClientRegistry
{
    private readonly Dictionary<string, TaskCompletionSource> packageUpdates = new(StringComparer.Ordinal);

    // Blocks new workers, retires the exact old generation, and stays held until
    // settings are copied, publication finishes, and the catalog is reloaded.
    internal async Task<IDisposable> AcquirePackageUpdateAsync(string widgetId, CancellationToken token)
    {
        ClientRegistration? registration;
        bool start = false;
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_gate)
        {
            DemandNotDisposed();
            if (packageUpdates.ContainsKey(widgetId) || widgetRestarts.ContainsKey(widgetId))
                throw new BridgeProtocolException("This widget is restarting or updating. Try again shortly.");
            packageUpdates.Add(widgetId, completion);
            _clients.TryGetValue(widgetId, out registration);
            if (registration is not null) start = ReserveRetirementLocked(registration, restartReserved: false);
        }
        var lease = new PackageUpdateLease(this, widgetId, completion);
        try
        {
            if (start) StartRetirement(registration!);
            if (registration is not null)
            {
                await registration.ResourceRetired.WaitAsync(RestartDeadline, token).ConfigureAwait(false);
                if (registration.RetirementFailure is { } failure)
                    throw new AggregateException("The widget could not stop for its update.", failure);
            }
            return lease;
        }
        catch { lease.Dispose(); throw; }
    }

    private sealed class PackageUpdateLease(BridgeClientRegistry owner, string id, TaskCompletionSource completion) : IDisposable
    {
        private BridgeClientRegistry? registry = owner;
        public void Dispose()
        {
            var current = Interlocked.Exchange(ref registry, null);
            if (current is null) return;
            lock (current._gate)
            {
                current.packageUpdates.Remove(id);
                completion.TrySetResult();
            }
        }
    }
}
