using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace WidgetRail.OverlayPlatformClient;

public enum ProcessElectionResult : uint { Owner, Redirected, Failed, AlreadyRunning }
public enum ProcessLeaseStatus : uint { Ok, InvalidArgument, WrongThread }
public readonly record struct ProcessElection(ProcessElectionResult Result, OverlayProcessLease? Lease);

/// <summary>Process lifecycle only; implementations must not initialize controller or Bridge services.</summary>
public interface IOverlayProcessNative
{
    ProcessElectionResult Begin(string profile, bool showExisting, out nint owner, out string error);
    ProcessLeaseStatus BindWindow(nint owner, nuint window, uint message);
    ProcessLeaseStatus End(nint owner);
}

/// <summary>
/// Thread-affine process election. Dispose on the electing thread after all input,
/// windows and owned workers finish cleanup. There is deliberately no finalizer:
/// a mutex cannot be released by the CLR finalizer thread.
/// </summary>
public sealed class OverlayProcessLease : IDisposable
{
    private readonly int thread = Environment.CurrentManagedThreadId;
    private readonly IOverlayProcessNative native;
    private nint owner;
    private OverlayProcessLease(IOverlayProcessNative native, nint owner) { this.native = native; this.owner = owner; }
    public static ProcessElection Elect(string profile, bool showExisting, IOverlayProcessNative? native = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(profile);
        if (profile.Length > 64 || profile.Any(character => !char.IsAsciiLetterOrDigit(character) && character is not ('.' or '_' or '-')))
            throw new ArgumentException("Process profiles contain at most 64 ASCII letters, digits, dots, underscores or hyphens.", nameof(profile));
        native ??= new OverlayProcessNative();
        var result = native.Begin(profile, showExisting, out var owner, out var error);
        if (result == ProcessElectionResult.Owner && owner != 0) return new(result, new(native, owner));
        if (owner != 0) native.End(owner);
        if ((result is ProcessElectionResult.Redirected or ProcessElectionResult.AlreadyRunning) && owner == 0) return new(result, null);
        throw new InvalidOperationException(string.IsNullOrEmpty(error) ? "Process ownership could not be established." : error);
    }
    public void BindWindow(nuint window, uint message)
    {
        CheckThread(); ObjectDisposedException.ThrowIf(owner == 0, this);
        Check(native.BindWindow(owner, window, message));
    }
    public void Dispose()
    {
        CheckThread();
        if (owner == 0) return;
        Check(native.End(owner)); owner = 0;
    }
    private void CheckThread()
    {
        if (thread != Environment.CurrentManagedThreadId) throw new InvalidOperationException("Process ownership must remain on its electing thread.");
    }
    private static void Check(ProcessLeaseStatus status)
    { if (status != ProcessLeaseStatus.Ok) throw new InvalidOperationException($"Process ownership operation failed: {status}."); }
}

internal sealed partial class OverlayProcessNative : IOverlayProcessNative
{
    public unsafe ProcessElectionResult Begin(string profile, bool showExisting, out nint owner, out string error)
    {
        Span<char> buffer = stackalloc char[512]; buffer.Clear();
        fixed (char* target = buffer)
        {
            var result = BeginNative(1, profile, (uint)profile.Length, showExisting ? 1U : 0, 3000, out owner, target, (uint)buffer.Length);
            var length = buffer.IndexOf('\0'); error = new string(buffer[..(length < 0 ? buffer.Length : length)]);
            return result;
        }
    }
    public ProcessLeaseStatus BindWindow(nint owner, nuint window, uint message) => BindNative(owner, window, message);
    public ProcessLeaseStatus End(nint owner) => EndNative(owner);

    [LibraryImport("OverlayPlatformInterop.dll", EntryPoint = "WidgetRailProcessBegin", StringMarshalling = StringMarshalling.Utf16)]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvStdcall)])]
    [DefaultDllImportSearchPaths(DllImportSearchPath.AssemblyDirectory | DllImportSearchPath.System32)]
    private static unsafe partial ProcessElectionResult BeginNative(uint version, string profile, uint profileLength,
        uint showExisting, uint timeoutMilliseconds, out nint owner, char* error, uint errorCapacity);
    [LibraryImport("OverlayPlatformInterop.dll", EntryPoint = "WidgetRailProcessBindWindow")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvStdcall)])]
    [DefaultDllImportSearchPaths(DllImportSearchPath.AssemblyDirectory | DllImportSearchPath.System32)]
    private static partial ProcessLeaseStatus BindNative(nint owner, nuint window, uint message);
    [LibraryImport("OverlayPlatformInterop.dll", EntryPoint = "WidgetRailProcessEnd")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvStdcall)])]
    [DefaultDllImportSearchPaths(DllImportSearchPath.AssemblyDirectory | DllImportSearchPath.System32)]
    private static partial ProcessLeaseStatus EndNative(nint owner);
}
