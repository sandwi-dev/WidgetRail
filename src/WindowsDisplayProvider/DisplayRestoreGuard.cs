using System.Diagnostics;
using System.IO.Pipes;
using System.Text;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Principal;
using System.Text.Json;
using WidgetRail.PlatformBroker;

namespace WidgetRail.WindowsDisplayProvider;

internal sealed record GuardRequest(string Id, DisplayConfiguration Configuration);
internal sealed record GuardReply(string State, DateTimeOffset? Deadline = null, uint? JobFlags = null);

/// <summary>Private bridge entry point. The helper owns apply and rollback, not the widget.</summary>
[SupportedOSPlatform("windows")]
public static partial class DisplayRestoreGuard
{
    public static Task<int> RunAsync(string? pipeName = null) => Task.Run(() =>
    {
        using var pipe = pipeName is null ? null : new NamedPipeClientStream(".", pipeName,
            PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        if (pipe is not null) pipe.Connect(5000);
        using var reader = pipe is null ? null : new StreamReader(pipe, Encoding.UTF8, false, 4096, leaveOpen: true);
        using var writer = pipe is null ? null : new StreamWriter(pipe, new UTF8Encoding(false), 4096, leaveOpen: true) { AutoFlush = true };
        TextReader input = reader is null ? Console.In : reader;
        TextWriter output = writer is null ? Console.Out : writer;
        // The launcher explicitly requests breakaway. Windows may retain an
        // outer job owned by an external launcher; membership alone does not
        // mean WidgetRail owns that job or can close it when the bridge exits.
        // This helper is never assigned to WidgetRail's worker containment job.
        uint? inheritedJobFlags = IsProcessInJob(GetCurrentProcess(), 0, out var inJob) != 0 &&
            inJob != 0 && TryReadJobFlags(out var flags) ? flags : null;
        using var identity = WindowsIdentity.GetCurrent();
        using var mutex = new Mutex(false, @"Local\WidgetRail.DisplayProfileRestore." + identity.User!.Value);
        bool acquired;
        try { acquired = mutex.WaitOne(0); }
        catch (AbandonedMutexException) { acquired = true; }
        if (!acquired)
        {
            output.WriteLine(JsonSerializer.Serialize(new GuardReply("guard-busy")));
            return 1;
        }
        // Keep mutex ownership on this helper thread across asynchronous pipe
        // waits. A bridge restart cannot overlap an earlier helper's rollback.
        try
        {
            output.WriteLine(JsonSerializer.Serialize(new GuardReply("ready", JobFlags: inheritedJobFlags)));
            return RunCoreAsync(input, output, new WindowsDisplayNative(),
            TimeProvider.System, TimeSpan.FromSeconds(15)).GetAwaiter().GetResult(); }
        finally { mutex.ReleaseMutex(); }
    });

    internal static async Task<int> RunCoreAsync(TextReader input, TextWriter output,
        IDisplayNative native, TimeProvider time, TimeSpan confirmationTimeout)
    {
        DisplayConfiguration? baseline = null;
        var needsRollback = false;
        try
        {
            var line = await input.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            if (line is null || line.Length > 128 * 1024) return 2;
            var request = JsonSerializer.Deserialize<GuardRequest>(line)
                ?? throw new InvalidDataException();
            if (!Guid.TryParseExact(request.Id, "N", out _)) return 2;
            request.Configuration.Validate();
            baseline = native.Capture();
            var target = DisplayProfileMatching.Remap(request.Configuration, native.ConnectedPaths());
            native.Validate(target);
            needsRollback = true; // Apply can partially change topology before reporting failure.
            native.Apply(target, persist: false);
            var started = time.GetTimestamp();
            using var timeoutCancellation = new CancellationTokenSource();
            var timeout = Task.Delay(confirmationTimeout, time, timeoutCancellation.Token);
            await output.WriteLineAsync(JsonSerializer.Serialize(
                new GuardReply("applied", time.GetUtcNow() + confirmationTimeout))).ConfigureAwait(false);
            await output.FlushAsync().ConfigureAwait(false);
            var decision = input.ReadLineAsync(); // EOF means the parent exited: revert immediately.
            var completed = await Task.WhenAny(decision, timeout).ConfigureAwait(false);
            var keep = completed == decision && await decision.ConfigureAwait(false) == "keep:" + request.Id &&
                time.GetElapsedTime(started) < confirmationTimeout;
            if (keep)
            {
                native.Apply(target, persist: true);
                needsRollback = false;
            }
            else
            {
                native.Restore(baseline);
                needsRollback = false;
            }
            timeoutCancellation.Cancel();
            await output.WriteLineAsync(JsonSerializer.Serialize(new GuardReply(keep ? "kept" : "reverted"))).ConfigureAwait(false);
            await output.FlushAsync().ConfigureAwait(false);
            return 0;
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            var state = "restore-failed";
            if (needsRollback && baseline is not null)
            {
                try { native.Restore(baseline); }
                catch (Exception rollback) when (rollback is not OutOfMemoryException) { state = "rollback-failed"; }
            }
            try { await output.WriteLineAsync(JsonSerializer.Serialize(new GuardReply(state))).ConfigureAwait(false); }
            catch (IOException) { }
            return 1;
        }
    }

    [LibraryImport("kernel32.dll")] private static partial nint GetCurrentProcess();
    [LibraryImport("kernel32.dll")] private static partial int IsProcessInJob(nint process, nint job, out int result);
    private static bool TryReadJobFlags(out uint flags)
    {
        var read = QueryInformationJobObject(0, 9, out var limits, (uint)Marshal.SizeOf<JobLimits>(), 0) != 0;
        flags = limits.Basic.Flags;
        return read;
    }
    [StructLayout(LayoutKind.Sequential)] private struct BasicJobLimits
    {
        public long ProcessTime, JobTime; public uint Flags; public nuint MinimumWorkingSet, MaximumWorkingSet;
        public uint ActiveProcesses; public nuint Affinity; public uint Priority, Scheduling;
    }
    [StructLayout(LayoutKind.Sequential)] private struct JobIo
    { public ulong ReadCount, WriteCount, OtherCount, ReadBytes, WriteBytes, OtherBytes; }
    [StructLayout(LayoutKind.Sequential)] private struct JobLimits
    { public BasicJobLimits Basic; public JobIo Io; public nuint ProcessMemory, JobMemory, PeakProcess, PeakJob; }
    [LibraryImport("kernel32.dll")] private static partial int QueryInformationJobObject(nint job, uint kind,
        out JobLimits limits, uint size, nint returned);
}

internal interface IDisplayRestoreSession : IAsyncDisposable
{
    DisplayProfilePending Pending { get; }
    Task<string> Completion { get; }
    Task DecideAsync(bool keep, CancellationToken token);
}
internal sealed record DisplayProfilePending(string Id, DateTimeOffset Deadline);
internal interface IDisplayRestoreLauncher
{
    Task<IDisplayRestoreSession> StartAsync(DisplayConfiguration configuration, CancellationToken token);
}

internal sealed class DisplayRestoreLauncher(string executable) : IDisplayRestoreLauncher
{
    public async Task<IDisplayRestoreSession> StartAsync(DisplayConfiguration configuration, CancellationToken token)
    {
        var id = Guid.NewGuid().ToString("N");
        var connection = await DisplayGuardConnection.StartAsync(executable, token).ConfigureAwait(false);
        try
        {
            await connection.Writer.WriteLineAsync(JsonSerializer.Serialize(new GuardRequest(id, configuration))).ConfigureAwait(false);
            await connection.Writer.FlushAsync(token).ConfigureAwait(false);
            var line = await connection.Reader.ReadLineAsync(token).AsTask()
                .WaitAsync(TimeSpan.FromSeconds(20), token).ConfigureAwait(false);
            var reply = line is { Length: < 1024 } ? JsonSerializer.Deserialize<GuardReply>(line) : null;
            if (reply?.State == "guard-busy")
                throw new BrokerException("display_busy", "Another display change is still finishing. Try again shortly.");
            if (reply?.State == "rollback-failed")
                throw new BrokerException("display_rollback_failed", "Windows couldn't restore the previous display setup.");
            if (reply?.State != "applied" || reply.Deadline is null)
                throw new BrokerException(reply?.State == "guard-unavailable" ? "display_guard_unavailable" : "display_restore_failed",
                    reply?.State == "guard-unavailable"
                        ? "Display restore cannot run safely in this host session. Start WidgetRail normally and try again."
                        : "Windows couldn't apply this display profile. Check the connected displays and try again.");
            return new Session(connection, new(id, reply.Deadline.Value));
        }
        catch
        {
            await connection.DisposeAsync().ConfigureAwait(false); // Pipe closure requests rollback.
            throw;
        }
    }
    private sealed class Session : IDisplayRestoreSession
    {
        private readonly DisplayGuardConnection _connection;
        public DisplayProfilePending Pending { get; }
        public Task<string> Completion { get; }
        internal Session(DisplayGuardConnection connection, DisplayProfilePending pending)
        {
            _connection = connection; Pending = pending; Completion = ReadCompletionAsync();
        }
        private async Task<string> ReadCompletionAsync()
        {
            try
            {
                var line = await _connection.Reader.ReadLineAsync().ConfigureAwait(false);
                return line is { Length: < 1024 } ? JsonSerializer.Deserialize<GuardReply>(line)?.State ?? "restore-failed" : "restore-failed";
            }
            catch (Exception error) when (error is IOException or InvalidOperationException or ObjectDisposedException or JsonException)
            { return "restore-failed"; }
        }
        public async Task DecideAsync(bool keep, CancellationToken token)
        {
            if (!Completion.IsCompleted)
            {
                try
                {
                    await _connection.Writer.WriteLineAsync((keep ? "keep:" : "revert:") + Pending.Id).ConfigureAwait(false);
                    await _connection.Writer.FlushAsync(token).ConfigureAwait(false);
                }
                catch (IOException) { } // An expired helper's final outcome is authoritative.
            }
            await Completion.WaitAsync(TimeSpan.FromSeconds(20), token).ConfigureAwait(false);
        }
        public ValueTask DisposeAsync() => _connection.DisposeAsync();
    }
}
