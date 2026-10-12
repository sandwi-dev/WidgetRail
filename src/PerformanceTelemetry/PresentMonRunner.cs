using System.Diagnostics;
using System.Security.Cryptography;

namespace WidgetRail.PerformanceTelemetry;

/// <summary>Owns only uniquely named short-lived ETW sessions. No CSV files or input tracking.</summary>
public sealed class PresentMonRunner(string executable, Action<string>? diagnostic = null)
{
    public const string ExpectedSha256 = "B2A706BC6AD475749E3B7E3409263AA1E6906D45BDCF993F6DBC0F660188F1AF";
    private readonly PresentFrameAccumulator _frames = new();
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private string _state = "idle";
    private long rows, acceptedRows;
    public CollectorReading Read() => new(Volatile.Read(ref _state), _frames.Snapshot(_clock.Elapsed.TotalSeconds));

    public async Task RunAsync(Func<bool> wantsCollection, CancellationToken token)
    {
        var failedUntilRelease = false;
        while (!token.IsCancellationRequested)
        {
            if (!wantsCollection())
            {
                failedUntilRelease = false;
                _frames.Clear(); Volatile.Write(ref _state, "idle");
                await Task.Delay(200, token).ConfigureAwait(false);
                continue;
            }
            if (failedUntilRelease) { await Task.Delay(200, token).ConfigureAwait(false); continue; }
            // Keep the verified file open without write/delete sharing until
            // both the collector and its exact-session stop command have exited.
            await using var verified = await OpenVerifiedExecutableAsync(executable, token).ConfigureAwait(false);
            if (verified is null)
            {
                Volatile.Write(ref _state, "missing"); failedUntilRelease = true; continue;
            }
            var session = "WidgetRail-FPS-" + Guid.NewGuid().ToString("N");
            Interlocked.Exchange(ref rows, 0); Interlocked.Exchange(ref acceptedRows, 0);
            using var process = new Process { StartInfo = StartInfo(executable,
                "--session_name", session, "--output_stdout", "--no_console_stats", "--v1_metrics",
                "--no_track_input", "--no_track_gpu", "--no_track_display", "--timed", "120", "--terminate_after_timed") };
            _frames.Clear();
            try
            {
                if (!process.Start()) throw new IOException("Collector did not start.");
                diagnostic?.Invoke($"session-start session={session} child={process.Id} limit-seconds=120");
                Volatile.Write(ref _state, "collecting");
                var stdout = ReadFramesAsync(process.StandardOutput, token);
                var stderr = ReadErrorsAsync(process.StandardError, token);
                var reportAt = _clock.Elapsed.TotalSeconds + 10;
                try
                {
                    while (!process.HasExited && wantsCollection())
                    {
                        if (_clock.Elapsed.TotalSeconds >= reportAt)
                        {
                            reportAt = _clock.Elapsed.TotalSeconds + 10;
                            diagnostic?.Invoke($"session-health rows={Interlocked.Read(ref rows)} accepted={Interlocked.Read(ref acceptedRows)} reader={stdout.Status} errors-reader={stderr.Status}");
                        }
                        await Task.Delay(100, token).ConfigureAwait(false);
                    }
                }
                finally
                {
                    // A crashed collector can leave its real-time ETW session
                    // behind. Retire our exact session even after process exit.
                    await StopSessionAsync(session, process).ConfigureAwait(false);
                    diagnostic?.Invoke($"session-stop exit={process.ExitCode} demand={wantsCollection()} rows={Interlocked.Read(ref rows)} accepted={Interlocked.Read(ref acceptedRows)}");
                    try { await Task.WhenAll(stdout, stderr).WaitAsync(TimeSpan.FromSeconds(3)).ConfigureAwait(false); }
                    catch (Exception error) when (error is OperationCanceledException or IOException or TimeoutException)
                    { diagnostic?.Invoke($"reader-end type={error.GetType().Name} hresult={error.HResult:X8}"); }
                }
                if (wantsCollection() && !token.IsCancellationRequested && process.ExitCode != 0)
                {
                    Volatile.Write(ref _state, process.ExitCode == 6 ? "permission-required" : "failed");
                    failedUntilRelease = true;
                }
            }
            catch (Exception error) when (error is IOException or System.ComponentModel.Win32Exception or InvalidOperationException)
            {
                diagnostic?.Invoke($"session-failed type={error.GetType().Name} hresult={error.HResult:X8}");
                Volatile.Write(ref _state, "failed"); failedUntilRelease = true;
            }
        }
    }

    private async Task ReadFramesAsync(StreamReader reader, CancellationToken token)
    {
        while (await reader.ReadLineAsync(token).ConfigureAwait(false) is { } line)
        {
            Interlocked.Increment(ref rows);
            if (_frames.Observe(line, _clock.Elapsed.TotalSeconds)) Interlocked.Increment(ref acceptedRows);
        }
    }

    private async Task ReadErrorsAsync(StreamReader reader, CancellationToken token)
    {
        // Fixed-size chunks bound error retention even if a provider omits newlines.
        var buffer = new char[512];
        var remaining = 8;
        int count;
        while ((count = await reader.ReadAsync(buffer, token).ConfigureAwait(false)) != 0)
            if (remaining-- > 0) diagnostic?.Invoke("presentmon-stderr " + new string(buffer, 0, count));
    }

    private static async Task DrainAsync(StreamReader reader, CancellationToken token)
    {
        var buffer = new char[1024];
        while (await reader.ReadAsync(buffer, token).ConfigureAwait(false) != 0) { }
    }

    private async Task StopSessionAsync(string session, Process process)
    {
        // Never use the global/default PresentMon name or stop another collector.
        try
        {
            using var stop = Process.Start(StartInfo(executable, "--session_name", session, "--terminate_existing_session"));
            if (stop is not null)
            {
                var drain = Task.WhenAll(DrainAsync(stop.StandardOutput, default), DrainAsync(stop.StandardError, default));
                try { await stop.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(3)).ConfigureAwait(false); }
                catch (TimeoutException) { stop.Kill(); }
                await drain.WaitAsync(TimeSpan.FromSeconds(3)).ConfigureAwait(false);
            }
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(3)).ConfigureAwait(false);
        }
        catch (Exception error) when (error is IOException or System.ComponentModel.Win32Exception or InvalidOperationException or TimeoutException) { }
        finally
        {
            if (!process.HasExited)
            {
                process.Kill();
                await process.WaitForExitAsync().ConfigureAwait(false);
            }
        }
    }

    public static async Task<bool> VerifyExecutableAsync(string path, CancellationToken token)
    {
        await using var file = await OpenVerifiedExecutableAsync(path, token).ConfigureAwait(false);
        return file is not null;
    }

    private static async Task<FileStream?> OpenVerifiedExecutableAsync(string path, CancellationToken token)
    {
        FileStream? file = null;
        try
        {
            file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (Convert.ToHexString(await SHA256.HashDataAsync(file, token).ConfigureAwait(false)) == ExpectedSha256)
            {
                var retained = file; file = null; return retained;
            }
            return null;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { return null; }
        finally { if (file is not null) await file.DisposeAsync().ConfigureAwait(false); }
    }

    private static ProcessStartInfo StartInfo(string path, params string[] arguments)
    {
        var info = new ProcessStartInfo(path) { UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var argument in arguments) info.ArgumentList.Add(argument);
        return info;
    }
}
