using System.ComponentModel;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.Principal;
using Microsoft.Win32.SafeHandles;

namespace GameBarAlternative.PlatformBroker;

/// <summary>
/// Creates a single-client Windows pipe whose discretionary ACL names only the
/// desktop host and one AppContainer SID. The Low mandatory label is supplied
/// at handle creation, which does not require enabling SeSecurityPrivilege.
/// </summary>
internal static class WindowsIsolatedPipeFactory
{
    private const uint PipeAccessDuplex = 0x00000003;
    private const uint FileFlagFirstPipeInstance = 0x00080000;
    private const uint FileFlagOverlapped = 0x40000000;
    private const uint PipeRejectRemoteClients = 0x00000008;

    internal static NamedPipeServerStream Create(
        string pipeName,
        string appContainerSid,
        int bufferSize = 4096)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("AppContainer pipes require Windows.");
        ArgumentException.ThrowIfNullOrWhiteSpace(pipeName);
        ArgumentException.ThrowIfNullOrWhiteSpace(appContainerSid);
        if (bufferSize is < 256 or > 1024 * 1024)
            throw new ArgumentOutOfRangeException(nameof(bufferSize));

        var currentUser = WindowsIdentity.GetCurrent().User
            ?? throw new InvalidOperationException("The current Windows user SID is unavailable.");
        var sddl =
            $"D:P(A;;GA;;;{currentUser.Value})(A;;GRGW;;;{appContainerSid})S:(ML;;NW;;;LW)";
        if (!NativeMethods.ConvertStringSecurityDescriptorToSecurityDescriptorW(
                sddl,
                1,
                out var descriptor,
                out _))
            throw new Win32Exception(
                Marshal.GetLastWin32Error(), "Could not build isolated pipe security.");

        try
        {
            var attributes = new SecurityAttributes
            {
                Length = Marshal.SizeOf<SecurityAttributes>(),
                SecurityDescriptor = descriptor,
                InheritHandle = false,
            };
            var handle = NativeMethods.CreateNamedPipeW(
                $"\\\\.\\pipe\\{pipeName}",
                PipeAccessDuplex | FileFlagFirstPipeInstance | FileFlagOverlapped,
                PipeRejectRemoteClients,
                1,
                checked((uint)bufferSize),
                checked((uint)bufferSize),
                0,
                ref attributes);
            if (handle.IsInvalid)
            {
                var error = Marshal.GetLastWin32Error();
                handle.Dispose();
                throw new Win32Exception(error, "Could not create the isolated pipe.");
            }
            return new NamedPipeServerStream(
                PipeDirection.InOut,
                isAsync: true,
                isConnected: false,
                handle);
        }
        finally
        {
            _ = NativeMethods.LocalFree(descriptor);
        }
    }

    internal static void VerifyClientProcess(
        NamedPipeServerStream pipe,
        int expectedProcessId)
    {
        ArgumentNullException.ThrowIfNull(pipe);
        if (expectedProcessId <= 0)
            throw new ArgumentOutOfRangeException(nameof(expectedProcessId));
        if (!NativeMethods.GetNamedPipeClientProcessId(
                pipe.SafePipeHandle,
                out var actualProcessId))
            throw new Win32Exception(
                Marshal.GetLastWin32Error(), "Could not authenticate the isolated pipe client.");
        if (actualProcessId != checked((uint)expectedProcessId))
            throw new InvalidOperationException(
                "An unexpected process connected to the isolated pipe.");
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SecurityAttributes
    {
        public int Length;
        public IntPtr SecurityDescriptor;
        [MarshalAs(UnmanagedType.Bool)] public bool InheritHandle;
    }

    private static class NativeMethods
    {
        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool ConvertStringSecurityDescriptorToSecurityDescriptorW(
            string stringSecurityDescriptor,
            uint stringSdRevision,
            out IntPtr securityDescriptor,
            out uint securityDescriptorSize);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        internal static extern SafePipeHandle CreateNamedPipeW(
            string name,
            uint openMode,
            uint pipeMode,
            uint maxInstances,
            uint outBufferSize,
            uint inBufferSize,
            uint defaultTimeout,
            ref SecurityAttributes securityAttributes);

        [DllImport("kernel32.dll")]
        internal static extern IntPtr LocalFree(IntPtr memory);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool GetNamedPipeClientProcessId(
            SafePipeHandle pipe,
            out uint clientProcessId);
    }
}
