using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace WidgetRail.WindowsPerformanceProvider;

public sealed record SystemPerformanceReading(DateTimeOffset SampledAtUtc, double? CpuPercent,
    double? GpuPercent, long? MemoryUsedBytes, long? MemoryTotalBytes, long? DedicatedGpuMemoryBytes);

/// <summary>System-wide metrics, sampled only on demand and cached for one second.</summary>
public sealed partial class WindowsSystemSampler : IDisposable
{
    private readonly object _gate = new();
    private SafePdhQueryHandle? _query;
    private nint _gpu, _vram;
    private bool _initialized, _disposed;
    private (ulong Idle, ulong Total)? _cpu;
    private SystemPerformanceReading? _last;
    private long _lastTick;

    public SystemPerformanceReading Read()
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            var tick = Environment.TickCount64;
            if (_last is not null && tick - _lastTick < 1000) return _last;
            var warmed = _last is not null && tick - _lastTick <= 3000;
            if (!warmed) _cpu = null;
            _lastTick = tick;
            if (!OperatingSystem.IsWindows()) return _last = new(DateTimeOffset.UtcNow, null, null, null, null, null);
            EnsureCounters();
            double? cpu = null;
            if (GetSystemTimes(out var idle, out var kernel, out var user) != 0)
            {
                var total = kernel + user;
                if (_cpu is { } prior && total > prior.Total && idle >= prior.Idle)
                    cpu = Math.Clamp(100d * (1 - (double)(idle - prior.Idle) / (total - prior.Total)), 0, 100);
                _cpu = (idle, total);
            }
            long? used = null, memory = null;
            var status = new MemoryStatus { Length = (uint)Marshal.SizeOf<MemoryStatus>() };
            if (GlobalMemoryStatusEx(ref status) != 0 && status.TotalPhysical <= long.MaxValue && status.AvailablePhysical <= status.TotalPhysical)
            {
                memory = (long)status.TotalPhysical;
                used = (long)(status.TotalPhysical - status.AvailablePhysical);
            }
            double? gpu = null;
            long? vram = null;
            if (_query is { IsInvalid: false } && PdhCollectQueryData(_query) == 0)
            {
                var engines = ReadCounter(_gpu);
                if (warmed && engines.Count != 0)
                    gpu = Math.Clamp(engines.GroupBy(e => EngineIdentity(e.Name), StringComparer.Ordinal)
                        .Max(group => group.Sum(e => e.Value)), 0, 100);
                var adapters = ReadCounter(_vram);
                var bytes = adapters.Sum(e => e.Value);
                if (adapters.Count != 0 && double.IsFinite(bytes) && bytes <= long.MaxValue) vram = (long)bytes;
            }
            return _last = new(DateTimeOffset.UtcNow, cpu, gpu, used, memory, vram);
        }
    }

    private static string EngineIdentity(string name)
    {
        var luid = name.IndexOf("luid_", StringComparison.OrdinalIgnoreCase);
        return luid < 0 ? name : name[luid..];
    }

    private void EnsureCounters()
    {
        if (_initialized) return;
        _initialized = true;
        if (PdhOpenQuery(null, 0, out var query) != 0) { query?.Dispose(); return; }
        _query = query;
        if (PdhAddEnglishCounter(query, "\\GPU Engine(*)\\Utilization Percentage", 0, out _gpu) != 0) _gpu = 0;
        if (PdhAddEnglishCounter(query, "\\GPU Adapter Memory(*)\\Dedicated Usage", 0, out _vram) != 0) _vram = 0;
        _ = PdhCollectQueryData(query);
    }

    private static IReadOnlyList<(string Name, double Value)> ReadCounter(nint counter)
    {
        if (counter == 0) return [];
        uint bytes = 0, count = 0;
        const uint moreData = 0x800007D2, formatDouble = 0x200;
        var result = PdhGetFormattedCounterArray(counter, formatDouble, ref bytes, ref count, 0);
        if (result != moreData || bytes is 0 or > 2 * 1024 * 1024) return [];
        // The counter set can change between calls. Retry once with the new bound.
        for (var attempt = 0; attempt < 2; attempt++)
        {
            var allocated = bytes;
            var buffer = Marshal.AllocHGlobal((int)allocated);
            try
            {
                result = PdhGetFormattedCounterArray(counter, formatDouble, ref bytes, ref count, buffer);
                if (result == moreData && bytes <= 2 * 1024 * 1024) continue;
                var size = Marshal.SizeOf<CounterItem>();
                if (result != 0 || count > 32768 || (ulong)count * (uint)size > allocated) return [];
                var values = new List<(string, double)>((int)count);
                for (var i = 0; i < count; i++)
                {
                    var item = Marshal.PtrToStructure<CounterItem>(buffer + checked(i * size));
                    if (item.Value.Status is 0 or 1 && double.IsFinite(item.Value.Value) && item.Value.Value >= 0)
                        values.Add((Marshal.PtrToStringUni(item.Name) ?? "unknown", item.Value.Value));
                }
                return values;
            }
            finally { Marshal.FreeHGlobal(buffer); }
        }
        return [];
    }

    public void Dispose()
    {
        lock (_gate) { _disposed = true; _query?.Dispose(); _query = null; }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct CounterValue { internal uint Status; internal double Value; }
    [StructLayout(LayoutKind.Sequential)]
    private struct CounterItem { internal nint Name; internal CounterValue Value; }
    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatus
    {
        internal uint Length, Load;
        internal ulong TotalPhysical, AvailablePhysical, TotalPageFile, AvailablePageFile, TotalVirtual, AvailableVirtual, AvailableExtendedVirtual;
    }
    private sealed class SafePdhQueryHandle : SafeHandleZeroOrMinusOneIsInvalid
    {
        public SafePdhQueryHandle() : base(true) { }
        protected override bool ReleaseHandle() => PdhCloseQuery(handle) == 0;
    }

    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial int GetSystemTimes(out ulong idle, out ulong kernel, out ulong user);
    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial int GlobalMemoryStatusEx(ref MemoryStatus status);
    [LibraryImport("pdh.dll", EntryPoint = "PdhOpenQueryW", StringMarshalling = StringMarshalling.Utf16)]
    private static partial uint PdhOpenQuery(string? source, nuint userData, out SafePdhQueryHandle query);
    [LibraryImport("pdh.dll", EntryPoint = "PdhAddEnglishCounterW", StringMarshalling = StringMarshalling.Utf16)]
    private static partial uint PdhAddEnglishCounter(SafePdhQueryHandle query, string path, nuint userData, out nint counter);
    [LibraryImport("pdh.dll")]
    private static partial uint PdhCollectQueryData(SafePdhQueryHandle query);
    [LibraryImport("pdh.dll", EntryPoint = "PdhGetFormattedCounterArrayW")]
    private static partial uint PdhGetFormattedCounterArray(nint counter, uint format, ref uint bytes, ref uint count, nint items);
    [LibraryImport("pdh.dll")]
    private static partial uint PdhCloseQuery(nint query);
}
