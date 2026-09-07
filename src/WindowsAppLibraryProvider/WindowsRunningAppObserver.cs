using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace WidgetRail.WindowsAppLibraryProvider;

internal sealed record WindowsRunningAppObservation(
    string RegistrationIdentity,
    string InstanceEvidence,
    string DisplayName = "",
    WindowsExecutableAuthority? PortableAuthority = null);

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
    private static readonly IWindowsExecutableAuthorityReader ExecutableAuthority =
        new WindowsExecutableAuthorityReader();

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
        if (process.IsInvalid || !IsSameUserSessionNonElevated(process, processId))
            return null;
        var packagedIdentity = PackagedIdentity(process);
        var executable = packagedIdentity is null ? ExecutableIdentity(process) : null;
        var identity = packagedIdentity ?? executable?.Identity;
        if (identity is null ||
            !GetProcessTimes(process, out var created, out _, out _, out _)) return null;
        var instance = Convert.ToHexString(SHA256.HashData(Encoding.ASCII.GetBytes(
            $"{processId:X8}:{created:X16}:{identity}")));
        return new(
            identity,
            instance,
            executable is null
                ? string.Empty
                : Path.GetFileNameWithoutExtension(executable.Authority.CanonicalPath),
            executable?.Authority);
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

    private static ExecutableObservation? ExecutableIdentity(SafeProcessHandle process)
    {
        var length = 32_768u;
        var path = new StringBuilder((int)length);
        if (!QueryFullProcessImageName(process, 0, path, ref length) || length == 0)
            return null;
        var fullPath = Path.GetFullPath(path.ToString());
        if (ExcludedProcesses.Contains(Path.GetFileName(fullPath))) return null;
        var authority = ExecutableAuthority.ReadExact(fullPath);
        return authority is null ? null : new ExecutableObservation(
            WindowsStartMenuApplicationSource.IdentityForExecutable(
                authority.CanonicalPath), authority);
    }

    private static bool IsSameUserSessionNonElevated(
        SafeProcessHandle process,
        uint processId)
    {
        if (!ProcessIdToSessionId(processId, out var processSession) ||
            !ProcessIdToSessionId((uint)Environment.ProcessId, out var currentSession) ||
            processSession != currentSession)
            return false;
        if (!OpenProcessToken(process, TokenQuery, out var token)) return false;
        using (token)
        {
            if (!GetTokenInformation(token, TokenElevation, out var elevation,
                    Marshal.SizeOf<TokenElevationInfo>(), out _) ||
                elevation.TokenIsElevated != 0)
                return false;
            try
            {
                using var processIdentity = new WindowsIdentity(token.DangerousGetHandle());
                using var currentIdentity = WindowsIdentity.GetCurrent();
                var currentUser = currentIdentity.User;
                return processIdentity.User is not null && currentUser is not null &&
                    processIdentity.User.Equals(currentUser);
            }
            catch (Exception exception) when (exception is ArgumentException or
                System.Security.SecurityException or UnauthorizedAccessException)
            {
                return false;
            }
        }
    }

    private sealed record ExecutableObservation(
        string Identity,
        WindowsExecutableAuthority Authority);

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
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ProcessIdToSessionId(uint processId, out uint sessionId);
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
