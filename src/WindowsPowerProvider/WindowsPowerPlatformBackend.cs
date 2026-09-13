using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Principal;
using Microsoft.Win32.SafeHandles;
using WidgetRail.PlatformBroker;

[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("PowerWidget.Tests")]

namespace WidgetRail.WindowsPowerProvider;

/// <summary>Only the trusted bridge constructs the Windows power provider.</summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsPowerPlatformBackend : IPowerPlatformBrokerBackend
{
    private readonly IPowerNative _native;
    private readonly SemaphoreSlim _commands = new(1, 1);
    public WindowsPowerPlatformBackend() : this(new WindowsPowerNative()) { }
    internal WindowsPowerPlatformBackend(IPowerNative native) => _native = native;

    public Task<PowerAvailability> GetPowerAvailabilityAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            _native.WithShutdownPrivilege(() => { });
            return Task.FromResult(new PowerAvailability(true, true, _native.CanSleep));
        }
        catch (Win32Exception) { return Task.FromResult(new PowerAvailability(false, false, false)); }
        catch (UnauthorizedAccessException) { return Task.FromResult(new PowerAvailability(false, false, false)); }
    }

    public async Task ExecutePowerAsync(PowerCommand command, CancellationToken cancellationToken)
    {
        if (!Enum.IsDefined(command)) throw new BrokerException("invalid_payload", "Unknown power command.");
        if (!await _commands.WaitAsync(0, cancellationToken).ConfigureAwait(false))
            throw new BrokerException("power_busy", "A power request is already in progress.");
        try
        {
            // SetSuspendState can block until resume. Keep the bridge's request thread free.
            await Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (command == PowerCommand.Sleep && !_native.CanSleep)
                    throw new BrokerException("power_unavailable", "Sleep is unavailable on this PC.");
                _native.WithShutdownPrivilege(() =>
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    // No force/force-if-hung flags: applications can block shutdown to save work.
                    if (command == PowerCommand.Sleep) _native.Sleep();
                    else _native.ExitWindows(command == PowerCommand.Restart ? 0x2u : 0x8u, 0x80040000u);
                });
            }, cancellationToken).ConfigureAwait(false);
        }
        catch (Win32Exception error)
        {
            throw new BrokerException(error.NativeErrorCode is 5 or 1300 or 1314 ? "power_denied" : "power_failed",
                "Windows couldn't complete the power request.", error);
        }
        catch (UnauthorizedAccessException error)
        {
            throw new BrokerException("power_denied", "Windows didn't allow the power request.", error);
        }
        finally { _commands.Release(); }
    }
}

internal interface IPowerNative
{
    bool CanSleep { get; }
    void WithShutdownPrivilege(Action action);
    void ExitWindows(uint flags, uint reason);
    void Sleep();
}

[SupportedOSPlatform("windows")]
internal sealed partial class WindowsPowerNative : IPowerNative
{
    public bool CanSleep => IsPwrSuspendAllowed() != 0;
    public void ExitWindows(uint flags, uint reason)
    {
        if (ExitWindowsEx(flags, reason) == 0) throw new Win32Exception(Marshal.GetLastPInvokeError());
    }
    public void Sleep()
    {
        if (SetSuspendState(0, 0, 0) == 0) throw new Win32Exception(Marshal.GetLastPInvokeError());
    }

    public void WithShutdownPrivilege(Action action)
    {
        // Enable only on a temporary token. RunImpersonated restores the thread's prior
        // identity even on failure; the bridge's process token is never modified.
        if (OpenProcessToken(GetCurrentProcess(), 0x0002 | 0x0008, out var processToken) == 0)
            throw new Win32Exception(Marshal.GetLastPInvokeError());
        using (processToken)
        {
            if (DuplicateTokenEx(processToken, 0x0002 | 0x0004 | 0x0008 | 0x0020,
                    0, 2, 2, out var token) == 0)
                throw new Win32Exception(Marshal.GetLastPInvokeError());
            using (token)
            {
                if (LookupPrivilegeValueW(null, "SeShutdownPrivilege", out var luid) == 0)
                    throw new Win32Exception(Marshal.GetLastPInvokeError());
                var privileges = new TokenPrivileges { Count = 1, Luid = luid, Attributes = 2 };
                var adjusted = AdjustTokenPrivileges(token, 0, ref privileges, 0, 0, 0);
                var error = Marshal.GetLastPInvokeError();
                if (adjusted == 0 || error != 0) throw new Win32Exception(error);
                WindowsIdentity.RunImpersonated(token, action);
            }
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Luid { public uint Low; public int High; }
    [StructLayout(LayoutKind.Sequential)]
    private struct TokenPrivileges { public uint Count; public Luid Luid; public uint Attributes; }
    [LibraryImport("kernel32.dll")] private static partial nint GetCurrentProcess();
    [LibraryImport("advapi32.dll", SetLastError = true)]
    private static partial int OpenProcessToken(nint process, uint access, out SafeAccessTokenHandle token);
    [LibraryImport("advapi32.dll", SetLastError = true)]
    private static partial int DuplicateTokenEx(SafeAccessTokenHandle existing, uint access, nint attributes,
        int level, int type, out SafeAccessTokenHandle token);
    [LibraryImport("advapi32.dll", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    private static partial int LookupPrivilegeValueW(string? system, string name, out Luid luid);
    [LibraryImport("advapi32.dll", SetLastError = true)]
    private static partial int AdjustTokenPrivileges(SafeAccessTokenHandle token, int disableAll,
        ref TokenPrivileges state, uint length, nint previous, nint returned);
    [LibraryImport("user32.dll", SetLastError = true)]
    private static partial int ExitWindowsEx(uint flags, uint reason);
    [LibraryImport("powrprof.dll")] private static partial byte IsPwrSuspendAllowed();
    [LibraryImport("powrprof.dll", SetLastError = true)]
    private static partial byte SetSuspendState(byte hibernate, byte force, byte disableWake);
}
