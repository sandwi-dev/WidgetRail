using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace WidgetRail.WrailCli;

internal interface IDevHostActivation
{
    Process Activate(DevHostTarget target, IReadOnlyList<string> arguments);
    void VerifyIdentity(DevHostTarget target, Process process);
}

internal sealed class WindowsDevHostActivation : IDevHostActivation
{
    public Process Activate(DevHostTarget target, IReadOnlyList<string> arguments)
    {
        if (!OperatingSystem.IsWindows()) throw new CliUsageException("WinUI launch requires Windows.");
        var start = new ProcessStartInfo(target.ExecutablePath)
        {
            UseShellExecute = false,
            WorkingDirectory = target.FrontendRoot,
        };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        return Process.Start(start) ?? throw new CliOperationException("WinUI frontend did not start.");
    }

    public void VerifyIdentity(DevHostTarget target, Process process)
    {
        var path = new StringBuilder(32768);
        var length = (uint)path.Capacity;
        if (!QueryFullProcessImageNameW(process.SafeHandle, 0, path, ref length))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not authenticate the launched WinUI process image.");
        if (!string.Equals(Path.GetFullPath(path.ToString()), Path.GetFullPath(target.ExecutablePath), StringComparison.OrdinalIgnoreCase))
            throw new CliOperationException("Launched process does not have the exact requested WinUI executable path.");
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool QueryFullProcessImageNameW(SafeProcessHandle process, uint flags, StringBuilder path, ref uint length);
}
