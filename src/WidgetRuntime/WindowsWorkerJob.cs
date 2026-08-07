using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace GameBarAlternative.WidgetRuntime;

/// <summary>
/// Windows worker containment. Processes are created suspended so no worker
/// code can run before assignment to the job. AppContainer is intentionally a
/// separate boundary: the unpackaged desktop host has no signed package
/// identity or installed capability profile from which to create one safely.
/// </summary>
internal sealed class WindowsWorkerJob : IDisposable
{
    private const uint JobObjectExtendedLimitInformationClass = 9;
    private const uint JobObjectLimitActiveProcess = 0x00000008;
    private const uint JobObjectLimitJobMemory = 0x00000200;
    private const uint JobObjectLimitDieOnUnhandledException = 0x00000400;
    private const uint JobObjectLimitKillOnJobClose = 0x00002000;
    private const uint CreateSuspended = 0x00000004;
    private const uint CreateNoWindow = 0x08000000;
    private readonly SafeJobHandle _handle;
    private bool _disposed;

    private WindowsWorkerJob(SafeJobHandle handle, long memoryLimitBytes)
    {
        _handle = handle;
        MemoryLimitBytes = memoryLimitBytes;
    }

    public long MemoryLimitBytes { get; }
    public uint ActiveProcessLimit => 1;

    public static WindowsWorkerJob Create(long memoryLimitBytes)
    {
        var handle = NativeMethods.CreateJobObjectW(IntPtr.Zero, null);
        if (handle.IsInvalid)
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not create a worker job object.");
        try
        {
            var information = new JobObjectExtendedLimitInformation
            {
                BasicLimitInformation = new JobObjectBasicLimitInformation
                {
                    LimitFlags = JobObjectLimitActiveProcess |
                                 JobObjectLimitJobMemory |
                                 JobObjectLimitDieOnUnhandledException |
                                 JobObjectLimitKillOnJobClose,
                    ActiveProcessLimit = 1,
                },
                JobMemoryLimit = checked((nuint)memoryLimitBytes),
            };
            if (!NativeMethods.SetInformationJobObject(
                    handle,
                    JobObjectExtendedLimitInformationClass,
                    ref information,
                    (uint)Marshal.SizeOf<JobObjectExtendedLimitInformation>()))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not configure worker containment.");
            return new WindowsWorkerJob(handle, memoryLimitBytes);
        }
        catch
        {
            handle.Dispose();
            throw;
        }
    }

    public Process StartProcess(ProcessStartInfo startInfo)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(startInfo);
        var executable = Path.GetFullPath(startInfo.FileName);
        var commandLine = new StringBuilder(BuildCommandLine(executable, startInfo.ArgumentList));
        var startup = new StartupInfo { Size = (uint)Marshal.SizeOf<StartupInfo>() };
        if (!NativeMethods.CreateProcessW(
                executable,
                commandLine,
                IntPtr.Zero,
                IntPtr.Zero,
                false,
                CreateSuspended | CreateNoWindow,
                IntPtr.Zero,
                startInfo.WorkingDirectory,
                ref startup,
                out var created))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Worker process did not start.");

        Process? process = null;
        var resumed = false;
        try
        {
            if (!NativeMethods.AssignProcessToJobObject(_handle, created.Process))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Worker could not be assigned to containment.");
            process = Process.GetProcessById(checked((int)created.ProcessId));
            if (NativeMethods.ResumeThread(created.Thread) == uint.MaxValue)
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Worker main thread could not resume.");
            resumed = true;
            return process;
        }
        catch
        {
            if (!resumed) NativeMethods.TerminateProcess(created.Process, 1);
            process?.Dispose();
            throw;
        }
        finally
        {
            NativeMethods.CloseHandle(created.Thread);
            NativeMethods.CloseHandle(created.Process);
        }
    }

    public void Terminate()
    {
        if (_disposed) return;
        if (!NativeMethods.TerminateJobObject(_handle, 1))
        {
            var error = Marshal.GetLastWin32Error();
            if (error != 0 && error != 6) // ERROR_INVALID_HANDLE after concurrent disposal.
                throw new Win32Exception(error, "Worker job could not be terminated.");
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _handle.Dispose();
    }

    private static string BuildCommandLine(string executable, IReadOnlyCollection<string> arguments)
    {
        var output = new StringBuilder(QuoteArgument(executable));
        foreach (var argument in arguments)
            output.Append(' ').Append(QuoteArgument(argument));
        return output.ToString();
    }

    private static string QuoteArgument(string value)
    {
        if (value.Length != 0 && !value.Any(character => char.IsWhiteSpace(character) || character == '"'))
            return value;
        var output = new StringBuilder("\"");
        var backslashes = 0;
        foreach (var character in value)
        {
            if (character == '\\')
            {
                backslashes++;
                continue;
            }
            if (character == '"')
            {
                output.Append('\\', backslashes * 2 + 1).Append('"');
                backslashes = 0;
                continue;
            }
            output.Append('\\', backslashes).Append(character);
            backslashes = 0;
        }
        output.Append('\\', backslashes * 2).Append('"');
        return output.ToString();
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct StartupInfo
    {
        public uint Size;
        public IntPtr Reserved;
        public IntPtr Desktop;
        public IntPtr Title;
        public uint X;
        public uint Y;
        public uint XSize;
        public uint YSize;
        public uint XCountChars;
        public uint YCountChars;
        public uint FillAttribute;
        public uint Flags;
        public ushort ShowWindow;
        public ushort Reserved2;
        public IntPtr Reserved2Pointer;
        public IntPtr StandardInput;
        public IntPtr StandardOutput;
        public IntPtr StandardError;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessInformation
    {
        public IntPtr Process;
        public IntPtr Thread;
        public uint ProcessId;
        public uint ThreadId;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JobObjectBasicLimitInformation
    {
        public long PerProcessUserTimeLimit;
        public long PerJobUserTimeLimit;
        public uint LimitFlags;
        public nuint MinimumWorkingSetSize;
        public nuint MaximumWorkingSetSize;
        public uint ActiveProcessLimit;
        public nuint Affinity;
        public uint PriorityClass;
        public uint SchedulingClass;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IoCounters
    {
        public ulong ReadOperationCount;
        public ulong WriteOperationCount;
        public ulong OtherOperationCount;
        public ulong ReadTransferCount;
        public ulong WriteTransferCount;
        public ulong OtherTransferCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JobObjectExtendedLimitInformation
    {
        public JobObjectBasicLimitInformation BasicLimitInformation;
        public IoCounters IoInfo;
        public nuint ProcessMemoryLimit;
        public nuint JobMemoryLimit;
        public nuint PeakProcessMemoryUsed;
        public nuint PeakJobMemoryUsed;
    }

    private sealed class SafeJobHandle : SafeHandleZeroOrMinusOneIsInvalid
    {
        public SafeJobHandle() : base(ownsHandle: true) { }
        protected override bool ReleaseHandle() => NativeMethods.CloseHandle(handle);
    }

    private static class NativeMethods
    {
        [DllImport("kernel32.dll", EntryPoint = "CreateJobObjectW", SetLastError = true,
            CharSet = CharSet.Unicode)]
        internal static extern SafeJobHandle CreateJobObjectW(IntPtr attributes, string? name);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool SetInformationJobObject(
            SafeJobHandle job,
            uint informationClass,
            ref JobObjectExtendedLimitInformation information,
            uint informationLength);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool AssignProcessToJobObject(SafeJobHandle job, IntPtr process);

        [DllImport("kernel32.dll", EntryPoint = "CreateProcessW", SetLastError = true,
            CharSet = CharSet.Unicode)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool CreateProcessW(
            string applicationName,
            StringBuilder commandLine,
            IntPtr processAttributes,
            IntPtr threadAttributes,
            [MarshalAs(UnmanagedType.Bool)] bool inheritHandles,
            uint creationFlags,
            IntPtr environment,
            string currentDirectory,
            ref StartupInfo startupInfo,
            out ProcessInformation processInformation);

        [DllImport("kernel32.dll", SetLastError = true)]
        internal static extern uint ResumeThread(IntPtr thread);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool TerminateProcess(IntPtr process, uint exitCode);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool TerminateJobObject(SafeJobHandle job, uint exitCode);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool CloseHandle(IntPtr handle);
    }
}
