using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace WidgetRail.OverlayFrontend.WinUI;

/// <summary>CLI owns the lasting job handle; the frontend must not keep it alive.</summary>
internal static partial class DevelopmentJobEnrollment
{
    internal static void Join(string name)
    {
        // No inherited or retained handle: killing the CLI closes the last handle
        // and reclaims this frontend and all children it subsequently creates.
        using var job = OpenJobObjectW(0x0001 /* JOB_OBJECT_ASSIGN_PROCESS */, false, name);
        if (job.IsInvalid) throw new Win32Exception(Marshal.GetLastPInvokeError(), "The development process owner is unavailable.");
        if (!AssignProcessToJobObject(job, new nint(-1) /* GetCurrentProcess pseudo-handle */))
            throw new Win32Exception(Marshal.GetLastPInvokeError(), "The frontend could not join its development process owner.");
    }

    [LibraryImport("kernel32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial SafeFileHandle OpenJobObjectW(uint access,
        [MarshalAs(UnmanagedType.Bool)] bool inherit, string name);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool AssignProcessToJobObject(SafeFileHandle job, nint process);
}
