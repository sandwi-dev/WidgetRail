using System.ComponentModel;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Microsoft.Win32.SafeHandles;
using WidgetRail.PlatformBroker;

namespace WidgetRail.WindowsDisplayProvider;

internal sealed class DisplayGuardConnection : IAsyncDisposable
{
    private readonly NamedPipeServerStream _pipe;
    private readonly SafeProcessHandle _process;
    public int ProcessId { get; }
    public StreamReader Reader { get; }
    public StreamWriter Writer { get; }
    private DisplayGuardConnection(NamedPipeServerStream pipe, SafeProcessHandle process, int processId)
    {
        _pipe = pipe; _process = process; ProcessId = processId;
        Reader = new(pipe, Encoding.UTF8, false, 4096, leaveOpen: true);
        Writer = new(pipe, new UTF8Encoding(false), 4096, leaveOpen: true) { AutoFlush = true };
    }
    internal static async Task<DisplayGuardConnection> StartAsync(string executable, CancellationToken token)
    {
        var name = "WidgetRail.DisplayGuard." + Guid.NewGuid().ToString("N");
        var pipe = new NamedPipeServerStream(name, PipeDirection.InOut, 1, PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        SafeProcessHandle? process = null;
        DisplayGuardConnection? connection = null;
        try
        {
            (process, var id) = DisplayGuardProcess.Start(executable, name);
            await pipe.WaitForConnectionAsync(token).WaitAsync(TimeSpan.FromSeconds(10), token).ConfigureAwait(false);
            if (!DisplayGuardProcess.IsClient(pipe.SafePipeHandle, id))
                throw new BrokerException("display_guard_unavailable", "The display restore helper could not be verified.");
            connection = new DisplayGuardConnection(pipe, process, checked((int)id));
            var readyLine = await connection.Reader.ReadLineAsync(token).AsTask()
                .WaitAsync(TimeSpan.FromSeconds(10), token).ConfigureAwait(false);
            var ready = readyLine is { Length: < 1024 } ? JsonSerializer.Deserialize<GuardReply>(readyLine) : null;
            if (ready?.State != "ready")
                throw new BrokerException(ready?.State == "guard-busy" ? "display_busy" : "display_guard_unavailable",
                    ready?.State == "guard-busy" ? "Another display change is still finishing. Try again shortly."
                        : "Windows couldn't start an independent display restore helper.",
                    new InvalidOperationException(readyLine ?? "No guard readiness reply."));
            return connection;
        }
        catch
        {
            if (connection is not null) await connection.DisposeAsync().ConfigureAwait(false);
            else { pipe.Dispose(); process?.Dispose(); }
            throw;
        }
    }
    public ValueTask DisposeAsync()
    {
        // Disconnect first: rollback is triggered even if an async read is pending.
        _pipe.Dispose();
        try { Reader.Dispose(); Writer.Dispose(); }
        catch (Exception error) when (error is IOException or ObjectDisposedException or InvalidOperationException) { }
        finally { _process.Dispose(); } // Never terminate the helper that owns rollback.
        return ValueTask.CompletedTask;
    }
}

internal static unsafe partial class DisplayGuardProcess
{
    internal static (SafeProcessHandle, uint) Start(string executable, string pipeName)
    {
        var startup = new StartupInfo { Size = (uint)Marshal.SizeOf<StartupInfo>() };
        var command = ("\"" + executable + "\" --display-profile-guard " + pipeName + "\0").ToCharArray();
        ProcessInformation created;
        fixed (char* arguments = command)
        {
            // Explicit breakaway is honored only when the enclosing job permits
            // it. Failure is safe: no child can apply a display change.
            if (CreateProcessW(executable, arguments, 0, 0, 0,
                0x08000000 | 0x01000000, 0, Path.GetDirectoryName(executable), ref startup, out created) == 0)
                throw new BrokerException("display_guard_unavailable",
                    "Windows couldn't start an independent display restore helper.",
                    new Win32Exception(Marshal.GetLastPInvokeError()));
        }
        CloseHandle(created.Thread);
        return (new SafeProcessHandle(created.Process, ownsHandle: true), created.ProcessId);
    }
    internal static bool IsClient(SafePipeHandle pipe, uint expected) =>
        GetNamedPipeClientProcessId(pipe, out var actual) != 0 && actual == expected;

    [StructLayout(LayoutKind.Sequential)]
    private struct StartupInfo
    {
        public uint Size;
        public nint Reserved, Desktop, Title;
        public uint X, Y, XSize, YSize, XCountChars, YCountChars, FillAttribute, Flags;
        public ushort ShowWindow, ReservedCount;
        public nint ReservedBytes, StdInput, StdOutput, StdError;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessInformation { public nint Process, Thread; public uint ProcessId, ThreadId; }
    [LibraryImport("kernel32.dll", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    private static partial int CreateProcessW(string application, char* command, nint processAttributes,
        nint threadAttributes, int inherit, uint flags, nint environment, string? directory,
        ref StartupInfo startup, out ProcessInformation process);
    [LibraryImport("kernel32.dll")] private static partial int CloseHandle(nint handle);
    [LibraryImport("kernel32.dll")] private static partial int GetNamedPipeClientProcessId(SafePipeHandle pipe, out uint processId);
}
