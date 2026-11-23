using System.ComponentModel;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32.SafeHandles;

namespace WidgetRail.DiscordDesktop.Connection;

/// <summary>Owner-supplied identity from trusted discovery, never from peer JSON.</summary>
public sealed record DiscordProcessIdentity
{
    public int ProcessId { get; }
    public long CreationFileTime { get; }
    public string ExecutablePath { get; }
    public DiscordProcessIdentity(int processId, long creationFileTime, string executablePath)
    {
        if (processId <= 0 || creationFileTime <= 0) throw new ArgumentOutOfRangeException(nameof(processId));
        if (string.IsNullOrWhiteSpace(executablePath) || !Path.IsPathFullyQualified(executablePath))
            throw new ArgumentException("Use the full trusted executable path.", nameof(executablePath));
        ProcessId = processId; CreationFileTime = creationFileTime; ExecutablePath = Path.GetFullPath(executablePath);
    }
    [SupportedOSPlatform("windows")]
    public static DiscordProcessIdentity Capture(int trustedProcessId)
    {
        using var process = WindowsDiscordIdentity.Open(trustedProcessId);
        return WindowsDiscordIdentity.Read(process, trustedProcessId);
    }
}

[SupportedOSPlatform("windows")]
internal static unsafe partial class WindowsDiscordIdentity
{
    internal static SafeProcessHandle Open(int processId)
    {
        var process = OpenProcess(0x00100000 | 0x00001000, 0, checked((uint)processId)); // SYNCHRONIZE | QUERY_LIMITED_INFORMATION
        if (!process.IsInvalid) return process;
        var error = Marshal.GetLastPInvokeError(); process.Dispose();
        throw new Win32Exception(error, "Could not verify the Discord peer process.");
    }
    internal static DiscordProcessIdentity Read(SafeProcessHandle process, int processId)
    {
        EnsureAlive(process);
        if (GetProcessTimes(process, out var creation, out _, out _, out _) == 0)
            throw new Win32Exception(Marshal.GetLastPInvokeError(), "Could not verify process creation identity.");
        Span<char> path = stackalloc char[32768];
        uint count = (uint)path.Length;
        fixed (char* buffer = path)
            if (QueryFullProcessImageNameW(process, 0, buffer, ref count) == 0)
                throw new Win32Exception(Marshal.GetLastPInvokeError(), "Could not verify process executable.");
        return new(processId, creation.Value, new string(path[..checked((int)count)]));
    }
    internal static SafeProcessHandle Verify(NamedPipeServerStream pipe, DiscordProcessIdentity expected)
    {
        // A PID is only meaningful in the local machine's process namespace.
        // CurrentUserOnly authenticates the Windows user, not the computer.
        Span<char> computer = stackalloc char[256];
        fixed (char* buffer = computer)
        {
            // Windows reports ERROR_PIPE_LOCAL (229) for a local pipe. A
            // successfully returned remote computer name is not local proof,
            // even if it happens to match this machine's name.
            var remoteNameReturned = GetNamedPipeClientComputerNameW(pipe.SafePipeHandle, buffer, (uint)computer.Length) != 0;
            if (remoteNameReturned || Marshal.GetLastPInvokeError() != 229)
                throw new UnauthorizedAccessException("Discord pipe peer must be local and verifiable.");
        }
        if (GetNamedPipeClientProcessId(pipe.SafePipeHandle, out var actual) == 0 || actual != expected.ProcessId)
            throw new UnauthorizedAccessException("Discord pipe process identity did not match.");
        var process = Open(expected.ProcessId);
        try
        {
            var identity = Read(process, expected.ProcessId);
            if (identity.CreationFileTime != expected.CreationFileTime ||
                !string.Equals(identity.ExecutablePath, expected.ExecutablePath, StringComparison.OrdinalIgnoreCase))
                throw new UnauthorizedAccessException("Discord pipe creation/executable identity did not match.");
            return process;
        }
        catch { process.Dispose(); throw; }
    }
    internal static void EnsureAlive(SafeProcessHandle process)
    {
        if (WaitForSingleObject(process, 0) != 0x00000102) throw new UnauthorizedAccessException("Discord peer process has ended or cannot be verified.");
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct FileTime { public uint Low, High; public readonly long Value => checked((long)(((ulong)High << 32) | Low)); }
    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial SafeProcessHandle OpenProcess(uint access, int inherit, uint processId);
    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial int GetNamedPipeClientProcessId(SafePipeHandle pipe, out uint processId);
    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial int GetNamedPipeClientComputerNameW(SafePipeHandle pipe, char* computerName, uint length);
    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial int GetProcessTimes(SafeProcessHandle process, out FileTime creation, out FileTime exit, out FileTime kernel, out FileTime user);
    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial int QueryFullProcessImageNameW(SafeProcessHandle process, uint flags, char* path, ref uint size);
    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial uint WaitForSingleObject(SafeProcessHandle process, uint milliseconds);
}
