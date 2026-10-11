using System.Diagnostics;
using System.IO.Pipes;
using System.Security.Cryptography;
using System.Text;
using WidgetRail.PerformanceTelemetry;

namespace WidgetRail.WindowsPerformanceProvider;

/// <summary>
/// One host-owned collector. Enabling is explicit; reads only renew a short
/// collection lease. The helper can stay idle after consent without tracing.
/// </summary>
public sealed class FpsCollectorClient(string executable) : IAsyncDisposable
{
    private readonly object _gate = new();
    private readonly CancellationTokenSource _lifetime = new();
    private Task _session = Task.CompletedTask;
    private CollectorReading _reading = new("not-enabled", []);
    private long _demandAt = long.MinValue / 2;
    private long _receivedAt;
    private bool _disposed;
    private bool _enabled, _retry;

    public CollectorReading Read(bool demand = true)
    {
        lock (_gate)
        {
            if (_disposed) return new("unavailable", []);
            if (demand) _demandAt = Environment.TickCount64;
            if (!_enabled) return new("not-enabled", []);
            return _reading.State == "collecting" && Environment.TickCount64 - _receivedAt > 3000
                ? new("warming", []) : _reading;
        }
    }

    public string Enable()
    {
        lock (_gate)
        {
            if (_disposed || !OperatingSystem.IsWindows()) return "unavailable";
            _enabled = true;
            _demandAt = Environment.TickCount64;
            if (!_session.IsCompleted)
            {
                if (_reading.State is "failed" or "permission-required" or "missing") _retry = true;
                return _reading.State;
            }
            if (!File.Exists(executable)) { _reading = new("missing", []); return _reading.State; }
            _reading = new("enabling", []);
            // ShellExecute waits for UAC. It must not block a widget action or
            // the Bridge request loop; cancellation is checked again on return.
            _session = Task.Run(() => RunAsync(_lifetime.Token));
            return "enabling";
        }
    }

    public string Disable()
    {
        lock (_gate) { _enabled = false; return "not-enabled"; }
    }

    private async Task RunAsync(CancellationToken token)
    {
        var name = "wrail-fps-" + Environment.ProcessId + "-" + Guid.NewGuid().ToString("N");
        var nonce = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        try
        {
            if (!await PresentMonRunner.VerifyExecutableAsync(Path.Combine(Path.GetDirectoryName(executable)!, "PresentMon.exe"), token).ConfigureAwait(false))
            {
                lock (_gate) _reading = new("missing", []);
                return;
            }
            await using var pipe = new NamedPipeServerStream(name, PipeDirection.InOut, 1, PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly | PipeOptions.FirstPipeInstance);
            // Shell elevation does not reliably inherit DOTNET_ROOT. Use the
            // distribution's existing .NET host explicitly, avoiding a second
            // runtime copy or a dependency on a machine-wide .NET installation.
            var bundledDotnet = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(executable)!, "..", "..", "dotnet", "dotnet.exe"));
            var info = new ProcessStartInfo(File.Exists(bundledDotnet) ? bundledDotnet : executable)
                { UseShellExecute = true, Verb = "runas", WindowStyle = ProcessWindowStyle.Hidden };
            if (File.Exists(bundledDotnet)) info.ArgumentList.Add(Path.ChangeExtension(executable, ".dll"));
            foreach (var value in new[] { "--pipe", name, "--parent", Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture), "--nonce", nonce })
                info.ArgumentList.Add(value);
            using var helper = Process.Start(info) ?? throw new IOException("Collector launch failed.");
            token.ThrowIfCancellationRequested();
            using var connect = CancellationTokenSource.CreateLinkedTokenSource(token);
            connect.CancelAfter(TimeSpan.FromSeconds(15));
            await pipe.WaitForConnectionAsync(connect.Token).ConfigureAwait(false);
            if (!CollectorProtocol.IsClient(pipe, helper.Id)) throw new IOException("Unexpected collector client.");
            var hello = await CollectorProtocol.ReadAsync<CollectorHello>(pipe, connect.Token).ConfigureAwait(false);
            if (hello.Version != CollectorProtocol.Version || hello.Nonce is null || hello.Nonce.Length != nonce.Length ||
                !CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(hello.Nonce), Encoding.ASCII.GetBytes(nonce)))
                throw new IOException("Collector authentication failed.");
            while (!token.IsCancellationRequested)
            {
                bool collect;
                lock (_gate) { collect = _enabled && !_retry && Environment.TickCount64 - _demandAt < 5000; _retry = false; }
                using var heartbeat = CancellationTokenSource.CreateLinkedTokenSource(token);
                heartbeat.CancelAfter(TimeSpan.FromSeconds(5));
                await CollectorProtocol.WriteAsync(pipe, new CollectorCommand(collect), heartbeat.Token).ConfigureAwait(false);
                var reading = await CollectorProtocol.ReadAsync<CollectorReading>(pipe, heartbeat.Token).ConfigureAwait(false);
                CollectorProtocol.Validate(reading);
                lock (_gate) { _reading = reading; _receivedAt = Environment.TickCount64; }
                await Task.Delay(1000, token).ConfigureAwait(false);
            }
        }
        catch (System.ComponentModel.Win32Exception error) when (error.NativeErrorCode == 1223)
        {
            lock (_gate) _reading = new("declined", []);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception or
            OperationCanceledException or System.Text.Json.JsonException or InvalidOperationException)
        {
            lock (_gate) _reading = new(token.IsCancellationRequested ? "idle" : "failed", []);
        }
    }

    public async ValueTask DisposeAsync()
    {
        Task session;
        lock (_gate) { if (_disposed) return; _disposed = true; session = _session; }
        _lifetime.Cancel();
        // A user can leave UAC unanswered. Do not block Bridge shutdown on it.
        // If approval arrives later, the helper has no authenticated connection
        // and exits after its own ten-second connect bound without starting ETW.
        try { await session.WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false); }
        catch (TimeoutException) { }
        if (session.IsCompleted) _lifetime.Dispose();
        else _ = session.ContinueWith(_ => _lifetime.Dispose(), CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
    }
}
