using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace WidgetRail.WindowsAppLibraryProvider;

internal sealed record WindowsRunningAppObservation(
    string RegistrationIdentity,
    string InstanceEvidence);

internal interface IWindowsRunningAppObserver
{
    IReadOnlyList<WindowsRunningAppObservation> Observe(CancellationToken cancellationToken);
}

internal interface IWindowsRunningWindowReader
{
    void Enumerate(Func<IntPtr, bool> visitor);
    WindowsRunningAppObservation? Inspect(IntPtr window);
}

internal sealed class WindowsRunningAppObserver : IWindowsRunningAppObserver
{
    internal const int MaximumTopLevelWindowVisits = 256;
    private const uint ProcessQueryLimitedInformation = 0x1000;
    private const uint TokenQuery = 0x0008;
    private const int TokenElevation = 20;
    private const uint GwOwner = 4;
    private const int DwmwaCloaked = 14;
    private const int ErrorInsufficientBuffer = 122;
    private static readonly HashSet<string> ExcludedProcesses = new(
        ["OverlayHost.exe", "WidgetWorkerHost.exe", "WidgetBridge.exe", "wrail.exe"],
        StringComparer.OrdinalIgnoreCase);
    private readonly IWindowsRunningWindowReader _windows;

    internal WindowsRunningAppObserver() : this(new NativeWindowReader()) { }

    internal WindowsRunningAppObserver(IWindowsRunningWindowReader windows) =>
        _windows = windows ?? throw new ArgumentNullException(nameof(windows));

    public IReadOnlyList<WindowsRunningAppObservation> Observe(
        CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows()) return [];
        var observations = new List<WindowsRunningAppObservation>();
        var canceled = false;
        var visits = 0;
        _windows.Enumerate(window =>
        {
            visits++;
            if (cancellationToken.IsCancellationRequested)
            {
                canceled = true;
                return false;
            }
            if (_windows.Inspect(window) is { } observation)
                observations.Add(observation);
            return visits < MaximumTopLevelWindowVisits;
        });
        if (canceled) cancellationToken.ThrowIfCancellationRequested();
        return observations;
    }

    private static WindowsRunningAppObservation? InspectWindow(IntPtr window)
    {
        if (!IsWindowVisible(window) || GetWindow(window, GwOwner) != IntPtr.Zero ||
            IsCloaked(window) || GetWindowThreadProcessId(window, out var processId) == 0 ||
            processId == 0 || processId == Environment.ProcessId)
            return null;
        using var process = OpenProcess(ProcessQueryLimitedInformation, false, processId);
        if (process.IsInvalid || IsElevated(process)) return null;
        var identity = PackagedIdentity(process) ?? ExecutableIdentity(process);
        if (identity is null ||
            !GetProcessTimes(process, out var created, out _, out _, out _)) return null;
        var instance = Convert.ToHexString(SHA256.HashData(Encoding.ASCII.GetBytes(
            $"{processId:X8}:{created:X16}:{identity}")));
        return new(identity, instance);
    }

    private static string? PackagedIdentity(SafeProcessHandle process)
    {
        uint length = 0;
        var status = GetApplicationUserModelId(process, ref length, null);
        if (status != ErrorInsufficientBuffer || length is 0 or > 130) return null;
        var value = new StringBuilder((int)length);
        status = GetApplicationUserModelId(process, ref length, value);
        var aumid = status == 0 ? WindowsAppsFolderApplicationSource.NormalizeAumid(
            value.ToString()) : null;
        return aumid is null ? null : WindowsAppsFolderApplicationSource.IdentityFor(aumid);
    }

    private static string? ExecutableIdentity(SafeProcessHandle process)
    {
        var length = 32_768u;
        var path = new StringBuilder((int)length);
        if (!QueryFullProcessImageName(process, 0, path, ref length) || length == 0)
            return null;
        var fullPath = Path.GetFullPath(path.ToString());
        if (ExcludedProcesses.Contains(Path.GetFileName(fullPath))) return null;
        return WindowsStartMenuApplicationSource.IdentityForExecutable(fullPath);
    }

    private static bool IsElevated(SafeProcessHandle process)
    {
        if (!OpenProcessToken(process, TokenQuery, out var token)) return true;
        using (token)
        {
            return !GetTokenInformation(token, TokenElevation, out var elevation,
                       Marshal.SizeOf<TokenElevationInfo>(), out _) ||
                elevation.TokenIsElevated != 0;
        }
    }

    private static bool IsCloaked(IntPtr window) =>
        DwmGetWindowAttribute(window, DwmwaCloaked, out var cloaked, sizeof(int)) == 0 &&
        cloaked != 0;

    private delegate bool EnumWindowsCallback(IntPtr window, IntPtr parameter);

    private sealed class NativeWindowReader : IWindowsRunningWindowReader
    {
        public void Enumerate(Func<IntPtr, bool> visitor)
        {
            ArgumentNullException.ThrowIfNull(visitor);
            _ = EnumWindows((window, _) => visitor(window), IntPtr.Zero);
        }

        public WindowsRunningAppObservation? Inspect(IntPtr window) =>
            InspectWindow(window);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct TokenElevationInfo { internal int TokenIsElevated; }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumWindows(EnumWindowsCallback callback, IntPtr parameter);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(IntPtr window);
    [DllImport("user32.dll")]
    private static extern IntPtr GetWindow(IntPtr window, uint command);
    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(
        IntPtr window, int attribute, out int value, int size);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern SafeProcessHandle OpenProcess(
        uint desiredAccess, [MarshalAs(UnmanagedType.Bool)] bool inheritHandle, uint processId);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool QueryFullProcessImageName(
        SafeProcessHandle process, uint flags, StringBuilder path, ref uint size);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetProcessTimes(
        SafeProcessHandle process, out long creation, out long exit,
        out long kernel, out long user);
    [DllImport("kernel32.dll")]
    private static extern int GetApplicationUserModelId(
        SafeProcessHandle process, ref uint length, StringBuilder? applicationUserModelId);
    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool OpenProcessToken(
        SafeProcessHandle process, uint desiredAccess, out SafeAccessTokenHandle token);
    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetTokenInformation(
        SafeAccessTokenHandle token, int informationClass,
        out TokenElevationInfo information,
        int informationLength, out int returnLength);
}
