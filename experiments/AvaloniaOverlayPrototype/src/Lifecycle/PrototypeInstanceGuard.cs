namespace GameBarAlternative.AvaloniaPrototype.Lifecycle;

internal sealed class PrototypeInstanceGuard : IDisposable
{
    internal const string DefaultName = @"Local\GameBarAlternative.AvaloniaOverlayPrototype";
    private readonly Mutex mutex;
    private bool disposed;

    private PrototypeInstanceGuard(Mutex mutex) => this.mutex = mutex;

    public static bool TryAcquire(out PrototypeInstanceGuard? guard, string? name = null)
    {
        var mutex = new Mutex(initiallyOwned: true, name ?? DefaultName, out var createdNew);
        if (!createdNew)
        {
            mutex.Dispose();
            guard = null;
            return false;
        }
        guard = new PrototypeInstanceGuard(mutex);
        return true;
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        mutex.ReleaseMutex();
        mutex.Dispose();
    }
}
