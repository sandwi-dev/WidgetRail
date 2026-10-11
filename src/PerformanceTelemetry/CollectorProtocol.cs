using System.Buffers.Binary;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace WidgetRail.PerformanceTelemetry;

public sealed record CollectorHello(int Version, string Nonce);
public sealed record CollectorCommand(bool Collect);
public sealed record CollectorReading(string State, IReadOnlyList<ProcessFrameRate> Processes);

/// <summary>Private numeric-only channel between the Bridge and its collector.</summary>
public static partial class CollectorProtocol
{
    public const int Version = 1;
    public const int MaximumFrameBytes = 64 * 1024;
    public static bool ValidPipeName(string name) => name.Length is > 20 and < 150 &&
        name.StartsWith("wrail-fps-", StringComparison.Ordinal) && name.All(c => char.IsAsciiLetterOrDigit(c) || c == '-');

    public static async Task WriteAsync<T>(Stream stream, T value, CancellationToken token)
    {
        var payload = JsonSerializer.SerializeToUtf8Bytes(value);
        if (payload.Length > MaximumFrameBytes) throw new IOException("Collector frame exceeds its bound.");
        var header = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(header, payload.Length);
        await stream.WriteAsync(header, token).ConfigureAwait(false);
        await stream.WriteAsync(payload, token).ConfigureAwait(false);
        await stream.FlushAsync(token).ConfigureAwait(false);
    }

    public static async Task<T> ReadAsync<T>(Stream stream, CancellationToken token)
    {
        var header = new byte[4];
        await stream.ReadExactlyAsync(header, token).ConfigureAwait(false);
        var count = BinaryPrimitives.ReadInt32LittleEndian(header);
        if (count <= 0 || count > MaximumFrameBytes) throw new IOException("Invalid collector frame.");
        var buffer = new byte[count];
        await stream.ReadExactlyAsync(buffer, token).ConfigureAwait(false);
        return JsonSerializer.Deserialize<T>(buffer) ?? throw new IOException("Missing collector message.");
    }

    public static void Validate(CollectorReading reading)
    {
        if (reading.State is not ("idle" or "collecting" or "permission-required" or "failed" or "missing") ||
            reading.Processes is null || reading.Processes.Count > PresentFrameAccumulator.MaximumStreams ||
            reading.Processes.Any(p => p is null || p.ProcessId <= 0 || !double.IsFinite(p.FramesPerSecond) ||
                p.FramesPerSecond is < 0 or > 10_000 || p.SampleCount is <= 0 or > PresentFrameAccumulator.MaximumSamplesPerStream) ||
            reading.Processes.Select(p => p.ProcessId).Distinct().Count() != reading.Processes.Count)
            throw new IOException("Invalid collector reading.");
    }

    public static bool IsServer(NamedPipeClientStream pipe, int pid) => pid > 0 &&
        GetNamedPipeServerProcessId(pipe.SafePipeHandle, out var actual) != 0 && actual == (uint)pid;
    public static bool IsClient(NamedPipeServerStream pipe, int pid) => pid > 0 &&
        GetNamedPipeClientProcessId(pipe.SafePipeHandle, out var actual) != 0 && actual == (uint)pid;

    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial int GetNamedPipeServerProcessId(Microsoft.Win32.SafeHandles.SafePipeHandle pipe, out uint pid);
    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial int GetNamedPipeClientProcessId(Microsoft.Win32.SafeHandles.SafePipeHandle pipe, out uint pid);
}
