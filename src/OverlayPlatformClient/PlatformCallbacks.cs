using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace WidgetRail.OverlayPlatformClient;

internal sealed unsafe class PlatformCallbacks : IDisposable
{
    private readonly IPlatformDispatcher dispatcher;
    private readonly Action eventAvailable;
    private readonly Action<string>? diagnostic;
    private readonly ConcurrentQueue<string> diagnostics = new();
    private GCHandle context;
    private int closing;
    private int scheduled;
    private int signaled;
    private int diagnosticCount;
    private long failures;

    public PlatformCallbacks(IPlatformDispatcher dispatcher, Action eventAvailable, Action<string>? diagnostic)
    {
        this.dispatcher = dispatcher;
        this.eventAvailable = eventAvailable;
        this.diagnostic = diagnostic;
        context = GCHandle.Alloc(this);
    }

    public long Failures => Interlocked.Read(ref failures);

    public PlatformCreateOptions CreateOptions()
    {
        var options = PlatformCreateOptions.Create();
        options.CallbackContext = GCHandle.ToIntPtr(context);
        options.EventAvailable = (nint)(delegate* unmanaged[Stdcall]<nint, void>)&OnEventAvailable;
        options.Diagnostic = (nint)(delegate* unmanaged[Stdcall]<nint, char*, void>)&OnDiagnostic;
        return options;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static void OnEventAvailable(nint context)
    {
        try
        {
            var self = (PlatformCallbacks?)GCHandle.FromIntPtr(context).Target;
            if (self is null || Volatile.Read(ref self.closing) != 0) return;
            Interlocked.Exchange(ref self.signaled, 1);
            self.Schedule();
        }
        catch { /* Reverse callbacks must never unwind into native code. */ }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static void OnDiagnostic(nint context, char* message)
    {
        try
        {
            var self = (PlatformCallbacks?)GCHandle.FromIntPtr(context).Target;
            if (self is null || message is null || self.diagnostic is null || Volatile.Read(ref self.closing) != 0) return;
            // The pointer belongs to the callback's native stack lifetime.
            // Limit pending diagnostics without dropping controller edges.
            if (Interlocked.Increment(ref self.diagnosticCount) > 128)
            {
                Interlocked.Decrement(ref self.diagnosticCount);
                return;
            }
            var length = 0;
            while (length < 4096 && message[length] != '\0') length++;
            self.diagnostics.Enqueue(new string(message, 0, length));
            self.Schedule();
        }
        catch { /* Reverse callbacks must never unwind into native code. */ }
    }

    // A rejected dispatch leaves the notification pending. The owner's ordinary
    // polling cycle can retry it; no hidden synchronous fallback is permitted.
    public void RetryDispatch() => Schedule();

    private void Schedule()
    {
        if (Volatile.Read(ref closing) != 0 ||
            (Volatile.Read(ref signaled) == 0 && diagnostics.IsEmpty) ||
            Interlocked.CompareExchange(ref scheduled, 1, 0) != 0) return;
        try
        {
            if (dispatcher.TryEnqueue(Dispatch)) return;
        }
        catch
        {
            Interlocked.Increment(ref failures);
        }
        Interlocked.Exchange(ref scheduled, 0);
    }

    private void Dispatch()
    {
        // Clear before consuming so signals arriving during dispatch cannot be
        // stranded behind an already-scheduled bit.
        Interlocked.Exchange(ref scheduled, 0);
        if (Volatile.Read(ref closing) != 0) return;
        if (Interlocked.Exchange(ref signaled, 0) != 0)
        {
            try { eventAvailable(); }
            catch { Interlocked.Increment(ref failures); }
        }
        for (var i = 0; i < 128 && Volatile.Read(ref closing) == 0 && diagnostics.TryDequeue(out var message); i++)
        {
            Interlocked.Decrement(ref diagnosticCount);
            try { diagnostic?.Invoke(message); }
            catch { Interlocked.Increment(ref failures); }
        }
        Schedule();
    }

    public void CloseAdmission() => Interlocked.Exchange(ref closing, 1);

    // Must only run after native Destroy has drained callbacks.
    public void Dispose()
    {
        CloseAdmission();
        if (context.IsAllocated) context.Free();
        diagnostics.Clear();
    }
}
