using System.Runtime.InteropServices;
using System.Runtime.ExceptionServices;
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
    private const uint GwOwner = 4;
    private const int DwmwaCloaked = 14;
    private const int ErrorInsufficientBuffer = 122;
    internal const int MaximumWindowClassCharacters = 256;
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
        if (!HasEligibleTopLevelShape(
                window,
                GetShellWindow(),
                IsWindowVisible(window),
                GetWindow(window, GwOwner),
                IsCloaked(window),
                WindowClassName(window),
                GetWindowThreadProcessId(window, out var processId),
                processId,
                (uint)Environment.ProcessId))
            return null;
        using var process = OpenProcess(ProcessQueryLimitedInformation, false, processId);
        if (process.IsInvalid || !IsSameUserAndSession(process, processId))
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
                : executable.DisplayName,
            executable?.Authority);
    }

    private static string? PackagedIdentity(SafeProcessHandle process)
    {
        uint length = 0;
        var status = GetApplicationUserModelId(process, ref length, null);
        if (status != ErrorInsufficientBuffer || length is 0 or > 130) return null;
        var value = new char[length];
        status = GetApplicationUserModelId(process, ref length, value);
        if (status != 0) return null;
        var aumid = NormalizePackagedIdentityBuffer(value, length);
        return aumid is null ? null : WindowsAppsFolderApplicationSource.IdentityFor(aumid);
    }

    internal static string? NormalizePackagedIdentityBuffer(char[] value, uint length)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (length is 0 or > 130 || length > value.Length ||
            value[length - 1] != '\0' ||
            Array.IndexOf(value, '\0', 0, checked((int)length - 1)) >= 0)
            return null;
        return WindowsAppsFolderApplicationSource.NormalizeAumid(
            new string(value, 0, checked((int)length - 1)));
    }

    internal static bool HasEligibleTopLevelShape(
        IntPtr window,
        IntPtr shellWindow,
        bool visible,
        IntPtr owner,
        bool cloaked,
        string? windowClass,
        uint threadId,
        uint processId,
        uint currentProcessId) =>
        window != IntPtr.Zero && window != shellWindow && visible && windowClass is not null &&
        owner == IntPtr.Zero && !cloaked && !IsTaskbarWindowClass(windowClass) &&
        threadId != 0 && processId != 0 &&
        processId != currentProcessId;

    internal static bool IsTaskbarWindowClass(string? windowClass) =>
        string.Equals(windowClass, "Shell_TrayWnd", StringComparison.Ordinal) ||
        string.Equals(windowClass, "Shell_SecondaryTrayWnd", StringComparison.Ordinal);

    private static string? WindowClassName(IntPtr window)
    {
        var buffer = new char[MaximumWindowClassCharacters];
        var length = GetClassNameW(window, buffer, buffer.Length);
        return length is > 0 and < MaximumWindowClassCharacters
            ? new string(buffer, 0, length)
            : null;
    }

    private static ExecutableObservation? ExecutableIdentity(SafeProcessHandle process)
    {
        var length = 32_768u;
        var path = new StringBuilder((int)length);
        if (!QueryFullProcessImageName(process, 0, path, ref length) || length == 0)
            return null;
        var fullPath = Path.GetFullPath(path.ToString());
        if (ExcludedProcesses.Contains(Path.GetFileName(fullPath))) return null;
        using var lease = ExecutableAuthority.AcquireExact(fullPath);
        var authority = lease?.Authority;
        return new ExecutableObservation(
            WindowsStartMenuApplicationSource.IdentityForExecutable(
                fullPath),
            Path.GetFileNameWithoutExtension(fullPath),
            authority);
    }

    private static bool IsSameUserAndSession(
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
        string DisplayName,
        WindowsExecutableAuthority? Authority);

    private static bool IsCloaked(IntPtr window) =>
        DwmGetWindowAttribute(window, DwmwaCloaked, out var cloaked, sizeof(int)) == 0 &&
        cloaked != 0;

    private delegate bool EnumWindowsCallback(IntPtr window, IntPtr parameter);

    private sealed class NativeWindowReader : IWindowsRunningWindowReader
    {
        public void Enumerate(Func<IntPtr, bool> visitor)
        {
            ArgumentNullException.ThrowIfNull(visitor);
            ExceptionDispatchInfo? failure = null;
            _ = EnumWindows((window, _) =>
            {
                var shouldContinue = TryVisitWindow(visitor, window, out var visitFailure);
                failure = visitFailure;
                return shouldContinue;
            }, IntPtr.Zero);
            failure?.Throw();
        }

        public WindowsRunningAppObservation? Inspect(IntPtr window) =>
            InspectWindow(window);
    }

    internal static bool TryVisitWindow(
        Func<IntPtr, bool> visitor,
        IntPtr window,
        out ExceptionDispatchInfo? failure)
    {
        ArgumentNullException.ThrowIfNull(visitor);
        try
        {
            failure = null;
            return visitor(window);
        }
        catch (Exception exception)
        {
            failure = ExceptionDispatchInfo.Capture(exception);
            return false;
        }
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumWindows(EnumWindowsCallback callback, IntPtr parameter);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(IntPtr window);
    [DllImport("user32.dll")]
    private static extern IntPtr GetWindow(IntPtr window, uint command);
    [DllImport("user32.dll")]
    private static extern IntPtr GetShellWindow();
    [DllImport("user32.dll", EntryPoint = "GetClassNameW",
        ExactSpelling = true, CharSet = CharSet.Unicode)]
    private static extern int GetClassNameW(
        IntPtr window, [Out] char[] className, int maximumCount);
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
    [DllImport("kernel32.dll", EntryPoint = "GetApplicationUserModelId",
        ExactSpelling = true, CharSet = CharSet.Unicode)]
    private static extern int GetApplicationUserModelId(
        SafeProcessHandle process, ref uint length,
        [Out] char[]? applicationUserModelId);
    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool OpenProcessToken(
        SafeProcessHandle process, uint desiredAccess, out SafeAccessTokenHandle token);
}
