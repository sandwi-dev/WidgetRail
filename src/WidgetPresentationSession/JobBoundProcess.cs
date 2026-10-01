using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace WidgetRail.WidgetPresentationSession;

/// <summary>
/// Starts a redirected child atomically in an existing named development job.
/// No sandbox restrictions or ownership of the external job are introduced here.
/// </summary>
internal static partial class JobBoundProcess
{
    internal sealed record Child(Process Process, StreamReader Output, StreamReader Error);

    internal static Child Start(ProcessStartInfo start, string jobName)
    {
        ArgumentNullException.ThrowIfNull(start);
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        if (string.IsNullOrWhiteSpace(jobName) || jobName.Length > 256 || jobName.Any(char.IsControl))
            throw new ArgumentException("A bounded named process owner is required.", nameof(jobName));
        if (start.UseShellExecute || !start.RedirectStandardOutput || !start.RedirectStandardError || start.RedirectStandardInput ||
            !string.IsNullOrEmpty(start.Arguments) || !string.IsNullOrEmpty(start.UserName))
            throw new ArgumentException("Job-bound startup requires explicit arguments and redirected output/error.", nameof(start));
        var commandText = string.Join(' ', new[] { Quote(Path.GetFullPath(start.FileName)) }.Concat(start.ArgumentList.Select(Quote)));
        var environmentText = EnvironmentBlock(start);
        using var job = OpenJobObjectW(0x0001 | 0x0004 /* assign/query */, false, jobName);
        if (job.IsInvalid) throw Error("The child process owner is unavailable.");
        var security = new SecurityAttributes { Length = (uint)Marshal.SizeOf<SecurityAttributes>(), Inherit = 1 };
        CreateOutputPipe(ref security, out var outputRead, out var outputWrite);
        using (outputRead)
        using (outputWrite)
        {
            CreateOutputPipe(ref security, out var errorRead, out var errorWrite);
            using (errorRead)
            using (errorWrite)
            using (var input = CreateFileW("NUL", 0x80000000, 3, ref security, 3, 0x80, IntPtr.Zero))
            {
                if (input.IsInvalid) throw Error("Could not create the child standard input handle.");
                using var attributes = new AttributeList(job, input, outputWrite, errorWrite);
                IntPtr command = IntPtr.Zero, environment = IntPtr.Zero;
                var startup = new StartupInfoEx
                {
                    Info = new StartupInfo { Size = (uint)Marshal.SizeOf<StartupInfoEx>(), Flags = 0x100 /* STARTF_USESTDHANDLES */,
                        Input = input.DangerousGetHandle(), Output = outputWrite.DangerousGetHandle(), Error = errorWrite.DangerousGetHandle() },
                    Attributes = attributes.Pointer,
                };
                ProcessInformation created = default;
                Process? process = null;
                StreamReader? output = null, error = null;
                var transferred = false;
                try
                {
                    command = Marshal.StringToHGlobalUni(commandText);
                    environment = Marshal.StringToHGlobalUni(environmentText);
                    // JOB_LIST is applied during creation. Suspension lets us verify
                    // membership and own readers/process handles before any child code.
                    if (!CreateProcessW(Path.GetFullPath(start.FileName), command, IntPtr.Zero, IntPtr.Zero, true,
                        0x4 | 0x400 | 0x80000 | 0x08000000 /* suspended, unicode env, extended info, no window */,
                        environment, start.WorkingDirectory, ref startup, out created))
                        throw Error("Job-bound child process could not be created.");
                    if (!IsProcessInJob(created.Process, job, out var owned)) throw Error("Could not verify the child's required process owner.");
                    if (!owned) throw new InvalidOperationException("Child process was not admitted to its required process owner.");
                    process = Process.GetProcessById(checked((int)created.ProcessId));
                    _ = process.SafeHandle;
                    output = Reader(outputRead, start.StandardOutputEncoding);
                    error = Reader(errorRead, start.StandardErrorEncoding);
                    if (ResumeThread(created.Thread) == uint.MaxValue) throw Error("Job-bound child thread could not resume.");
                    transferred = true;
                    return new(process, output, error);
                }
                finally
                {
                    if (!transferred)
                    {
                        if (created.Process != IntPtr.Zero) _ = TerminateProcess(created.Process, 1);
                        output?.Dispose(); error?.Dispose(); process?.Dispose();
                    }
                    if (created.Thread != IntPtr.Zero) _ = CloseHandle(created.Thread);
                    if (created.Process != IntPtr.Zero) _ = CloseHandle(created.Process);
                    Marshal.FreeHGlobal(environment); Marshal.FreeHGlobal(command);
                }
            }
        }
    }

    private static StreamReader Reader(SafeFileHandle handle, Encoding? encoding)
    {
        // FileStream takes ownership. Transfer the native handle, never close the
        // same handle once as a pipe local and again when diagnostics are retired.
        var owned = new SafeFileHandle(handle.DangerousGetHandle(), ownsHandle: true);
        handle.SetHandleAsInvalid();
        try { return new(new FileStream(owned, FileAccess.Read, 4096, isAsync: false), encoding ?? Encoding.UTF8, true, 4096); }
        catch { owned.Dispose(); throw; }
    }

    internal static string EnvironmentBlock(ProcessStartInfo start)
    {
        var values = start.Environment.OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase).Select(pair =>
        {
            if (pair.Key.Length == 0 || pair.Key.Contains('=') || pair.Key.Contains('\0') || pair.Value?.Contains('\0') == true)
                throw new ArgumentException("Child environment contains an invalid entry.", nameof(start));
            return pair.Key + "=" + pair.Value;
        });
        return string.Join('\0', values) + "\0\0";
    }

    private static void CreateOutputPipe(ref SecurityAttributes security, out SafeFileHandle read, out SafeFileHandle write)
    {
        if (!CreatePipe(out read, out write, ref security, 0)) throw Error("Could not create child diagnostic pipe.");
        if (!SetHandleInformation(read, 1, 0))
        { var error = Error("Could not make the diagnostic reader private."); read.Dispose(); write.Dispose(); throw error; }
    }

    private static string Quote(string value)
    {
        if (value.Length != 0 && !value.Any(character => char.IsWhiteSpace(character) || character == '"')) return value;
        var result = new StringBuilder("\"");
        var slashes = 0;
        foreach (var character in value)
        {
            if (character == '\\') { slashes++; continue; }
            if (character == '"') { result.Append('\\', slashes * 2 + 1).Append('"'); slashes = 0; continue; }
            result.Append('\\', slashes).Append(character); slashes = 0;
        }
        return result.Append('\\', slashes * 2).Append('"').ToString();
    }

    private sealed class AttributeList : IDisposable
    {
        internal IntPtr Pointer { get; private set; }
        private IntPtr jobValue, handles;
        private bool initialized;
        internal AttributeList(SafeFileHandle job, params SafeFileHandle[] inherited)
        {
            nuint size = 0;
            const uint count = 2;
            _ = InitializeProcThreadAttributeList(IntPtr.Zero, count, 0, ref size);
            if (size == 0 || size > 65536) throw Error("Could not size process attributes.");
            Pointer = Marshal.AllocHGlobal(checked((int)size));
            try
            {
                if (!InitializeProcThreadAttributeList(Pointer, count, 0, ref size)) throw Error("Could not initialize process attributes.");
                initialized = true;
                jobValue = Marshal.AllocHGlobal(IntPtr.Size); Marshal.WriteIntPtr(jobValue, job.DangerousGetHandle());
                if (!UpdateProcThreadAttribute(Pointer, 0, 0x2000D /* JOB_LIST */, jobValue, (nuint)IntPtr.Size, IntPtr.Zero, IntPtr.Zero))
                    throw Error("Could not configure explicit child job inheritance.");
                handles = Marshal.AllocHGlobal(inherited.Length * IntPtr.Size);
                for (var index = 0; index < inherited.Length; index++) Marshal.WriteIntPtr(handles, index * IntPtr.Size, inherited[index].DangerousGetHandle());
                if (!UpdateProcThreadAttribute(Pointer, 0, 0x20002 /* HANDLE_LIST */, handles, (nuint)(inherited.Length * IntPtr.Size), IntPtr.Zero, IntPtr.Zero))
                    throw Error("Could not configure explicit child job/handle inheritance.");

            }
            catch { Dispose(); throw; }
        }
        public void Dispose()
        {
            if (initialized) DeleteProcThreadAttributeList(Pointer);
            initialized = false;
            Marshal.FreeHGlobal(Pointer); Pointer = IntPtr.Zero;
            Marshal.FreeHGlobal(jobValue); jobValue = IntPtr.Zero;
            Marshal.FreeHGlobal(handles); handles = IntPtr.Zero;
        }
    }

    private static Win32Exception Error(string message) => new(Marshal.GetLastPInvokeError(), message);
    [StructLayout(LayoutKind.Sequential)] private struct SecurityAttributes { internal uint Length; internal IntPtr Descriptor; internal int Inherit; }
    [StructLayout(LayoutKind.Sequential)] private struct StartupInfo
    {
        internal uint Size; internal IntPtr Reserved, Desktop, Title;
        internal uint X, Y, XSize, YSize, XChars, YChars, Fill, Flags;
        internal ushort ShowWindow, ReservedSize; internal IntPtr ReservedPointer, Input, Output, Error;
    }
    [StructLayout(LayoutKind.Sequential)] private struct StartupInfoEx { internal StartupInfo Info; internal IntPtr Attributes; }
    [StructLayout(LayoutKind.Sequential)] private struct ProcessInformation { internal IntPtr Process, Thread; internal uint ProcessId, ThreadId; }
    [LibraryImport("kernel32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    private static partial SafeFileHandle OpenJobObjectW(uint access, [MarshalAs(UnmanagedType.Bool)] bool inherit, string name);
    [LibraryImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CreatePipe(out SafeFileHandle read, out SafeFileHandle write, ref SecurityAttributes attributes, uint size);
    [LibraryImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetHandleInformation(SafeFileHandle handle, uint mask, uint flags);
    [LibraryImport("kernel32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    private static partial SafeFileHandle CreateFileW(string name, uint access, uint share, ref SecurityAttributes attributes, uint creation, uint flags, IntPtr template);
    [LibraryImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool InitializeProcThreadAttributeList(IntPtr list, uint count, uint flags, ref nuint size);
    [LibraryImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool UpdateProcThreadAttribute(IntPtr list, uint flags, nuint attribute, IntPtr value, nuint size, IntPtr previous, IntPtr returned);
    [LibraryImport("kernel32.dll")] private static partial void DeleteProcThreadAttributeList(IntPtr list);
    [LibraryImport("kernel32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)] [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CreateProcessW(string application, IntPtr command, IntPtr processAttributes, IntPtr threadAttributes,
        [MarshalAs(UnmanagedType.Bool)] bool inherit, uint flags, IntPtr environment, string directory, ref StartupInfoEx startup, out ProcessInformation process);
    [LibraryImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool IsProcessInJob(IntPtr process, SafeFileHandle job, [MarshalAs(UnmanagedType.Bool)] out bool owned);
    [LibraryImport("kernel32.dll", SetLastError = true)] private static partial uint ResumeThread(IntPtr thread);
    [LibraryImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool TerminateProcess(IntPtr process, uint exitCode);
    [LibraryImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CloseHandle(IntPtr handle);
}
