using System.ComponentModel;
using System.Runtime.InteropServices;

namespace WidgetRail.WidgetPresentationSession;

/// <summary>Reentrant exclusive gate which never pumps STA messages while waiting.</summary>
internal sealed partial class PresentationGate
{
    private readonly object sync = new();
    internal Scope Enter()
    {
        if (Thread.CurrentThread.GetApartmentState() != ApartmentState.STA)
        {
            Monitor.Enter(sync);
            return new(sync);
        }
        // A synchronous session read may be nested inside a native XAML layout
        // callback. Monitor's ordinary STA wait dispatches COM/window messages,
        // which can re-enter that layout and terminate the frontend.
        var previous = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(NonPumpingContext.Instance);
        try { Monitor.Enter(sync); }
        finally { SynchronizationContext.SetSynchronizationContext(previous); }
        return new(sync);
    }
    internal readonly struct Scope(object sync) : IDisposable
    {
        public void Dispose() => Monitor.Exit(sync);
    }
    private sealed class NonPumpingContext : SynchronizationContext
    {
        internal static readonly NonPumpingContext Instance = new();
        private NonPumpingContext() => SetWaitNotificationRequired();
        public override unsafe int Wait(IntPtr[] waitHandles, bool waitAll, int millisecondsTimeout)
        {
            // Handles are borrowed from the CLR for this call only. A kernel
            // wait with alertable=false dispatches neither messages nor APCs.
            fixed (IntPtr* handles = waitHandles)
            {
                var result = WaitForMultipleObjectsEx((uint)waitHandles.Length, handles,
                    waitAll ? 1 : 0, unchecked((uint)millisecondsTimeout), 0);
                if (result == uint.MaxValue) throw new Win32Exception(Marshal.GetLastPInvokeError());
                return unchecked((int)result);
            }
        }
    }
    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static unsafe partial uint WaitForMultipleObjectsEx(uint count, IntPtr* handles, int waitAll,
        uint milliseconds, int alertable);
}
