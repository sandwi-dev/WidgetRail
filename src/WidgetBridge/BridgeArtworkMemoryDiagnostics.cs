using System.Diagnostics;

namespace WidgetRail.WidgetBridge;

internal readonly record struct BridgeArtworkMemorySnapshot(
    long Requests,
    long Completed,
    long Failed,
    long InFlight,
    long MaximumInFlight,
    long RawBytes,
    long Base64Characters,
    long ManagedHeapBytes,
    long LargeObjectHeapBytes,
    long AllocatedBytesPerSecond,
    int Gen2Collections,
    long PrivateBytes,
    long WorkingSetBytes);

/// <summary>
/// Process-local, aggregate artwork transport accounting. No widget, handle,
/// package, request, or content identity is retained by this owner.
/// </summary>
internal sealed class BridgeArtworkMemoryDiagnostics
{
    private readonly object _gate = new();
    private long _requests;
    private long _completed;
    private long _failed;
    private long _inFlight;
    private long _maximumInFlight;
    private long _rawBytes;
    private long _base64Characters;
    private long _lastAllocatedBytes = GC.GetTotalAllocatedBytes(precise: false);
    private long _lastAllocationTimestamp = Stopwatch.GetTimestamp();

    internal IDisposable BeginRequest()
    {
        lock (_gate)
        {
            _requests = AddSaturated(_requests, 1);
            _inFlight = AddSaturated(_inFlight, 1);
            _maximumInFlight = Math.Max(_maximumInFlight, _inFlight);
        }
        return new RequestLease(this);
    }

    internal void RecordPayload(int rawBytes, int base64Characters)
    {
        if (rawBytes < 0) throw new ArgumentOutOfRangeException(nameof(rawBytes));
        if (base64Characters < 0)
            throw new ArgumentOutOfRangeException(nameof(base64Characters));
        lock (_gate)
        {
            _rawBytes = AddSaturated(_rawBytes, rawBytes);
            _base64Characters = AddSaturated(_base64Characters, base64Characters);
        }
    }

    internal BridgeArtworkMemorySnapshot Capture()
    {
        lock (_gate)
        {
            var now = Stopwatch.GetTimestamp();
            var allocated = GC.GetTotalAllocatedBytes(precise: false);
            var elapsedTicks = Math.Max(1, now - _lastAllocationTimestamp);
            var allocatedDelta = Math.Max(0, allocated - _lastAllocatedBytes);
            var allocationRate = SaturatedRate(allocatedDelta, elapsedTicks);
            _lastAllocatedBytes = allocated;
            _lastAllocationTimestamp = now;

            var memory = GC.GetGCMemoryInfo();
            var generations = memory.GenerationInfo;
            var largeObjectHeapBytes = generations.Length > 3
                ? Math.Max(0, generations[3].SizeAfterBytes)
                : 0;
            using var process = Process.GetCurrentProcess();
            return new BridgeArtworkMemorySnapshot(
                _requests,
                _completed,
                _failed,
                _inFlight,
                _maximumInFlight,
                _rawBytes,
                _base64Characters,
                Math.Max(0, GC.GetTotalMemory(forceFullCollection: false)),
                largeObjectHeapBytes,
                allocationRate,
                Math.Max(0, GC.CollectionCount(2)),
                Math.Max(0, process.PrivateMemorySize64),
                Math.Max(0, process.WorkingSet64));
        }
    }

    internal void Reset()
    {
        lock (_gate)
        {
            _requests = 0;
            _completed = 0;
            _failed = 0;
            _inFlight = 0;
            _maximumInFlight = 0;
            _rawBytes = 0;
            _base64Characters = 0;
            _lastAllocatedBytes = GC.GetTotalAllocatedBytes(precise: false);
            _lastAllocationTimestamp = Stopwatch.GetTimestamp();
        }
    }

    internal static long AddSaturated(long value, long increment) =>
        increment <= 0 ? value : value >= long.MaxValue - increment
            ? long.MaxValue
            : value + increment;

    private static long SaturatedRate(long bytes, long elapsedTicks)
    {
        if (bytes <= 0) return 0;
        var rate = (double)bytes * Stopwatch.Frequency / elapsedTicks;
        return rate >= long.MaxValue ? long.MaxValue : Math.Max(0, (long)rate);
    }

    private void Complete(bool failed)
    {
        lock (_gate)
        {
            if (_inFlight > 0) _inFlight--;
            if (failed) _failed = AddSaturated(_failed, 1);
            else _completed = AddSaturated(_completed, 1);
        }
    }

    private sealed class RequestLease(BridgeArtworkMemoryDiagnostics owner) : IDisposable
    {
        private BridgeArtworkMemoryDiagnostics? _owner = owner;
        private bool _failed = true;

        internal void Succeeded() => _failed = false;

        public void Dispose()
        {
            var owner = Interlocked.Exchange(ref _owner, null);
            owner?.Complete(_failed);
        }
    }

    internal static void MarkSucceeded(IDisposable lease)
    {
        if (lease is RequestLease request) request.Succeeded();
    }
}
