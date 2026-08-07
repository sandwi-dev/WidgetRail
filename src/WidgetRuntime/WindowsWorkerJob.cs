using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace GameBarAlternative.WidgetRuntime;

/// <summary>
/// Windows worker containment. Processes are created suspended so no worker
/// code can run before assignment to the job. Community workers additionally
/// receive a capability-free AppContainer token before their main thread runs.
/// </summary>
internal sealed class WindowsWorkerJob : IDisposable
{
    private const uint JobObjectExtendedLimitInformationClass = 9;
    private const uint JobObjectBasicUiRestrictionsClass = 4;
    private const uint JobObjectLimitActiveProcess = 0x00000008;
    private const uint JobObjectLimitJobMemory = 0x00000200;
    private const uint JobObjectLimitDieOnUnhandledException = 0x00000400;
    private const uint JobObjectLimitKillOnJobClose = 0x00002000;
    private const uint CreateSuspended = 0x00000004;
    private const uint CreateUnicodeEnvironment = 0x00000400;
    private const uint ExtendedStartupInfoPresent = 0x00080000;
    private const uint CreateNoWindow = 0x08000000;
    private static readonly nuint ProcThreadAttributeSecurityCapabilities = 0x00020009;
    private const uint JobObjectUiLimitAll = 0x000000FF;
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
            var uiRestrictions = new JobObjectBasicUiRestrictions
            {
                UiRestrictionsClass = JobObjectUiLimitAll,
            };
            if (!NativeMethods.SetInformationJobObjectUiRestrictions(
                    handle,
                    JobObjectBasicUiRestrictionsClass,
                    ref uiRestrictions,
                    (uint)Marshal.SizeOf<JobObjectBasicUiRestrictions>()))
                throw new Win32Exception(
                    Marshal.GetLastWin32Error(), "Could not configure worker UI restrictions.");
            return new WindowsWorkerJob(handle, memoryLimitBytes);
        }
        catch
        {
            handle.Dispose();
            throw;
        }
    }

    public Process StartProcess(
        ProcessStartInfo startInfo,
        WindowsAppContainer? appContainer = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(startInfo);
        var executable = Path.GetFullPath(startInfo.FileName);
        var commandLine = new StringBuilder(BuildCommandLine(executable, startInfo.ArgumentList));
        ProcessInformation created;
        if (appContainer is null)
        {
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
                    out created))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Worker process did not start.");
        }
        else
        {
            using var attributes = ProcThreadAttributeList.Create(appContainer);
            using var environment = appContainer.CreateEnvironmentBlock();
            var startup = new StartupInfoEx
            {
                StartupInfo = new StartupInfo { Size = (uint)Marshal.SizeOf<StartupInfoEx>() },
                AttributeList = attributes.Pointer,
            };
            if (!NativeMethods.CreateProcessAppContainerW(
                    executable,
                    commandLine,
                    IntPtr.Zero,
                    IntPtr.Zero,
                    false,
                    CreateSuspended | CreateNoWindow | CreateUnicodeEnvironment |
                    ExtendedStartupInfoPresent,
                    environment.Pointer,
                    startInfo.WorkingDirectory,
                    ref startup,
                    out created))
                throw new Win32Exception(
                    Marshal.GetLastWin32Error(), "AppContainer worker process did not start.");
            GC.KeepAlive(appContainer);
        }

        Process? process = null;
        var resumed = false;
        try
        {
            if (appContainer is not null)
                VerifyAppContainerToken(created.Process, appContainer.SidPointer);
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

    private static void VerifyAppContainerToken(IntPtr process, IntPtr expectedSid)
    {
        const uint tokenQuery = 0x0008;
        const int tokenIntegrityLevel = 25;
        const int tokenIsAppContainer = 29;
        const int tokenCapabilities = 30;
        const int tokenAppContainerSid = 31;
        const uint securityMandatoryLowRid = 0x00001000;

        if (!NativeMethods.OpenProcessToken(process, tokenQuery, out var token))
            throw new Win32Exception(
                Marshal.GetLastWin32Error(), "Could not inspect the worker isolation token.");
        using (token)
        {
            using var isAppContainer = ReadTokenInformation(token, tokenIsAppContainer);
            if (Marshal.ReadInt32(isAppContainer.Pointer) != 1)
                throw new WidgetProcessException("Worker token is not an AppContainer token.");

            using var appContainer = ReadTokenInformation(token, tokenAppContainerSid);
            var actualSid = Marshal.ReadIntPtr(appContainer.Pointer);
            if (actualSid == IntPtr.Zero || !NativeMethods.EqualSid(expectedSid, actualSid))
                throw new WidgetProcessException("Worker token has the wrong AppContainer SID.");

            using var integrity = ReadTokenInformation(token, tokenIntegrityLevel);
            var integritySid = Marshal.ReadIntPtr(integrity.Pointer);
            var countPointer = NativeMethods.GetSidSubAuthorityCount(integritySid);
            if (countPointer == IntPtr.Zero)
                throw new WidgetProcessException("Worker token integrity SID is invalid.");
            var count = Marshal.ReadByte(countPointer);
            var ridPointer = count == 0
                ? IntPtr.Zero
                : NativeMethods.GetSidSubAuthority(integritySid, checked((uint)(count - 1)));
            if (ridPointer == IntPtr.Zero ||
                unchecked((uint)Marshal.ReadInt32(ridPointer)) != securityMandatoryLowRid)
                throw new WidgetProcessException("Worker token is not Low integrity.");

            using var capabilities = ReadTokenInformation(token, tokenCapabilities);
            if (unchecked((uint)Marshal.ReadInt32(capabilities.Pointer)) != 0)
                throw new WidgetProcessException("Worker token unexpectedly contains capability SIDs.");
        }
    }

    private static TokenInformationBuffer ReadTokenInformation(SafeTokenHandle token, int informationClass)
    {
        _ = NativeMethods.GetTokenInformation(
            token, informationClass, IntPtr.Zero, 0, out var required);
        if (required == 0)
            throw new Win32Exception(
                Marshal.GetLastWin32Error(), "Could not size worker token information.");
        var buffer = Marshal.AllocHGlobal(checked((int)required));
        if (!NativeMethods.GetTokenInformation(
                token, informationClass, buffer, required, out _))
        {
            var error = Marshal.GetLastWin32Error();
            Marshal.FreeHGlobal(buffer);
            throw new Win32Exception(error, "Could not read worker token information.");
        }
        return new TokenInformationBuffer(buffer);
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
    private struct StartupInfoEx
    {
        public StartupInfo StartupInfo;
        public IntPtr AttributeList;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SecurityCapabilities
    {
        public IntPtr AppContainerSid;
        public IntPtr Capabilities;
        public uint CapabilityCount;
        public uint Reserved;
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

    [StructLayout(LayoutKind.Sequential)]
    private struct JobObjectBasicUiRestrictions
    {
        public uint UiRestrictionsClass;
    }

    private sealed class SafeJobHandle : SafeHandleZeroOrMinusOneIsInvalid
    {
        public SafeJobHandle() : base(ownsHandle: true) { }
        protected override bool ReleaseHandle() => NativeMethods.CloseHandle(handle);
    }

    private sealed class SafeTokenHandle : SafeHandleZeroOrMinusOneIsInvalid
    {
        public SafeTokenHandle() : base(ownsHandle: true) { }
        protected override bool ReleaseHandle() => NativeMethods.CloseHandle(handle);
    }

    private sealed class TokenInformationBuffer : IDisposable
    {
        internal TokenInformationBuffer(IntPtr pointer) => Pointer = pointer;
        public IntPtr Pointer { get; private set; }

        public void Dispose()
        {
            if (Pointer == IntPtr.Zero) return;
            Marshal.FreeHGlobal(Pointer);
            Pointer = IntPtr.Zero;
        }
    }

    private sealed class ProcThreadAttributeList : IDisposable
    {
        private IntPtr _securityCapabilities;
        private bool _disposed;

        private ProcThreadAttributeList(IntPtr pointer, IntPtr securityCapabilities)
        {
            Pointer = pointer;
            _securityCapabilities = securityCapabilities;
        }

        public IntPtr Pointer { get; private set; }

        public static ProcThreadAttributeList Create(WindowsAppContainer appContainer)
        {
            nuint size = 0;
            _ = NativeMethods.InitializeProcThreadAttributeList(IntPtr.Zero, 1, 0, ref size);
            if (size == 0)
                throw new Win32Exception(
                    Marshal.GetLastWin32Error(), "Could not size AppContainer launch attributes.");

            var list = Marshal.AllocHGlobal(checked((nint)size));
            var capabilities = IntPtr.Zero;
            var initialized = false;
            try
            {
                if (!NativeMethods.InitializeProcThreadAttributeList(list, 1, 0, ref size))
                    throw new Win32Exception(
                        Marshal.GetLastWin32Error(), "Could not initialize AppContainer launch attributes.");
                initialized = true;
                capabilities = Marshal.AllocHGlobal(Marshal.SizeOf<SecurityCapabilities>());
                Marshal.StructureToPtr(new SecurityCapabilities
                {
                    AppContainerSid = appContainer.SidPointer,
                    Capabilities = IntPtr.Zero,
                    CapabilityCount = 0,
                    Reserved = 0,
                }, capabilities, fDeleteOld: false);
                if (!NativeMethods.UpdateProcThreadAttribute(
                        list,
                        0,
                        ProcThreadAttributeSecurityCapabilities,
                        capabilities,
                        checked((nuint)Marshal.SizeOf<SecurityCapabilities>()),
                        IntPtr.Zero,
                        IntPtr.Zero))
                    throw new Win32Exception(
                        Marshal.GetLastWin32Error(), "Could not set AppContainer launch attributes.");
                return new ProcThreadAttributeList(list, capabilities);
            }
            catch
            {
                if (initialized) NativeMethods.DeleteProcThreadAttributeList(list);
                if (capabilities != IntPtr.Zero) Marshal.FreeHGlobal(capabilities);
                Marshal.FreeHGlobal(list);
                throw;
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            if (Pointer != IntPtr.Zero)
            {
                NativeMethods.DeleteProcThreadAttributeList(Pointer);
                Marshal.FreeHGlobal(Pointer);
                Pointer = IntPtr.Zero;
            }
            if (_securityCapabilities != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(_securityCapabilities);
                _securityCapabilities = IntPtr.Zero;
            }
        }
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

        [DllImport("kernel32.dll", EntryPoint = "SetInformationJobObject", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool SetInformationJobObjectUiRestrictions(
            SafeJobHandle job,
            uint informationClass,
            ref JobObjectBasicUiRestrictions information,
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

        [DllImport("kernel32.dll", EntryPoint = "CreateProcessW", SetLastError = true,
            CharSet = CharSet.Unicode)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool CreateProcessAppContainerW(
            string applicationName,
            StringBuilder commandLine,
            IntPtr processAttributes,
            IntPtr threadAttributes,
            [MarshalAs(UnmanagedType.Bool)] bool inheritHandles,
            uint creationFlags,
            IntPtr environment,
            string currentDirectory,
            ref StartupInfoEx startupInfo,
            out ProcessInformation processInformation);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool InitializeProcThreadAttributeList(
            IntPtr attributeList,
            int attributeCount,
            uint flags,
            ref nuint size);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool UpdateProcThreadAttribute(
            IntPtr attributeList,
            uint flags,
            nuint attribute,
            IntPtr value,
            nuint size,
            IntPtr previousValue,
            IntPtr returnSize);

        [DllImport("kernel32.dll")]
        internal static extern void DeleteProcThreadAttributeList(IntPtr attributeList);

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

        [DllImport("advapi32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool OpenProcessToken(
            IntPtr process,
            uint desiredAccess,
            out SafeTokenHandle token);

        [DllImport("advapi32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool GetTokenInformation(
            SafeTokenHandle token,
            int informationClass,
            IntPtr tokenInformation,
            uint tokenInformationLength,
            out uint returnLength);

        [DllImport("advapi32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool EqualSid(IntPtr firstSid, IntPtr secondSid);

        [DllImport("advapi32.dll")]
        internal static extern IntPtr GetSidSubAuthorityCount(IntPtr sid);

        [DllImport("advapi32.dll")]
        internal static extern IntPtr GetSidSubAuthority(IntPtr sid, uint subAuthority);
    }
}
