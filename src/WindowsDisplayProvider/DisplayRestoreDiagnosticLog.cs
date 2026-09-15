using System.ComponentModel;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;

namespace WidgetRail.WindowsDisplayProvider;

/// <summary>Best-effort observations only. No disk I/O or extra native reads on the restore path.</summary>
internal sealed class DisplayRestoreDiagnosticLog : IAsyncDisposable
{
    internal const int MaximumFileBytes = 1024 * 1024;
    private readonly Channel<Entry> _entries = Channel.CreateBounded<Entry>(new BoundedChannelOptions(64)
    { SingleReader = true, FullMode = BoundedChannelFullMode.Wait, AllowSynchronousContinuations = false });
    private readonly long _started = Stopwatch.GetTimestamp();
    private readonly Task _worker;
    internal Task Completion => _worker;
    private int _dropped;
    private sealed record Entry(DateTimeOffset Utc, long ElapsedMs, string Stage,
        DisplayConfiguration? Configuration, string? ErrorType, int? NativeError, bool Readback, DateTimeOffset? Deadline);

    internal DisplayRestoreDiagnosticLog(string directory, string transaction, Func<DisplayConfiguration> capture)
    {
        // The directory comes from the trusted backend, never the widget payload.
        _worker = Task.Run(() => WriteAsync(directory, transaction, capture));
    }

    internal void Record(string stage, DisplayConfiguration? configuration = null, Exception? error = null,
        bool readback = false, DateTimeOffset? deadline = null)
    {
        if (!_entries.Writer.TryWrite(new(DateTimeOffset.UtcNow,
                (long)Stopwatch.GetElapsedTime(_started).TotalMilliseconds, stage, configuration,
                error?.GetType().Name, (error as Win32Exception)?.NativeErrorCode, readback, deadline)))
            Interlocked.Increment(ref _dropped);
    }

    private async Task WriteAsync(string directory, string transaction, Func<DisplayConfiguration> capture)
    {
        try
        {
            // Refuse redirected locations; diagnostic failure must never affect rollback.
            for (var parent = new DirectoryInfo(directory); parent is not null; parent = parent.Parent)
                if (parent.Exists && (parent.Attributes & FileAttributes.ReparsePoint) != 0) return;
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, "restore.log");
            DisplayConfiguration? baseline = null, target = null;
            string? lastObservation = null;
            using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(500));
            var tick = timer.WaitForNextTickAsync().AsTask();
            var available = _entries.Reader.WaitToReadAsync().AsTask();
            while (true)
            {
                await Task.WhenAny(available, tick).ConfigureAwait(false);
                while (_entries.Reader.TryRead(out var entry))
                {
                    if (entry.Stage == "baseline") baseline = entry.Configuration;
                    if (entry.Stage == "target") target = entry.Configuration;
                    Append(new { entry.Utc, entry.ElapsedMs, Transaction = transaction, Pid = Environment.ProcessId,
                        entry.Stage, Setup = entry.Configuration is null ? null : Describe(entry.Configuration),
                        entry.ErrorType, entry.NativeError, entry.Deadline, Dropped = Volatile.Read(ref _dropped) });
                    if (entry.Readback) Observe(entry.Stage);
                }
                if (available.IsCompleted && !await available.ConfigureAwait(false)) break;
                if (tick.IsCompleted)
                {
                    if (!await tick.ConfigureAwait(false)) break;
                    if (target is not null) Observe("sample");
                    tick = timer.WaitForNextTickAsync().AsTask();
                }
                if (available.IsCompleted) available = _entries.Reader.WaitToReadAsync().AsTask();
            }

            void Observe(string trigger)
            {
                var readStarted = DateTimeOffset.UtcNow;
                var watch = Stopwatch.StartNew();
                try
                {
                    var current = capture();
                    var setup = Describe(current);
                    var signature = JsonSerializer.Serialize(setup);
                    if (trigger != "sample" || signature != lastObservation)
                        Append(new { Utc = DateTimeOffset.UtcNow, Transaction = transaction, Pid = Environment.ProcessId,
                            Stage = "active-readback", Trigger = trigger, ReadStartedUtc = readStarted,
                            ReadMs = watch.ElapsedMilliseconds, Setup = setup,
                            MatchesBaseline = baseline?.Matches(current), MatchesTarget = target?.Matches(current) });
                    lastObservation = signature;
                }
                catch (Exception error) when (error is not OutOfMemoryException)
                {
                    Append(new { Utc = DateTimeOffset.UtcNow, Transaction = transaction, Pid = Environment.ProcessId,
                        Stage = "readback-failed", Trigger = trigger, ReadStartedUtc = readStarted,
                        ReadMs = watch.ElapsedMilliseconds, ErrorType = error.GetType().Name,
                        NativeError = (error as Win32Exception)?.NativeErrorCode });
                }
            }

            void Append(object value)
            {
                var line = JsonSerializer.Serialize(value) + Environment.NewLine;
                foreach (var file in new[] { path, path + ".1" })
                    if (File.Exists(file) && (File.GetAttributes(file) & FileAttributes.ReparsePoint) != 0)
                        throw new IOException();
                if (File.Exists(path) && new FileInfo(path).Length + Encoding.UTF8.GetByteCount(line) > MaximumFileBytes)
                    File.Move(path, path + ".1", overwrite: true);
                File.AppendAllText(path, line, Encoding.UTF8);
            }
        }
        catch (Exception error) when (error is not OutOfMemoryException) { }
    }

    internal static object Describe(DisplayConfiguration configuration)
    {
        configuration.Validate();
        var paths = configuration.ReadPaths(); var modes = configuration.ReadModes();
        return new { configuration.Mode, Displays = paths.Select((path, index) =>
        {
            var source = modes[path.Source.ModeIndex].Source;
            var signal = modes[path.Target.ModeIndex];
            var identity = configuration.Targets[index];
            return new {
                // No monitor names, raw device paths, EDID serials or profile names.
                Connection = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity.DevicePath.ToUpperInvariant()))),
                Physical = identity.HardwareKey,
                SourceAdapter = path.Source.Adapter, SourceId = path.Source.Id,
                TargetAdapter = path.Target.Adapter, TargetId = path.Target.Id,
                source.Width, source.Height, source.X, source.Y, path.Target.Rotation,
                RefreshNumerator = path.Target.Refresh.Numerator, RefreshDenominator = path.Target.Refresh.Denominator,
                path.Target.Scaling, path.Target.Technology, path.Target.Available, path.Flags,
                signal.Signal0, signal.Signal1, signal.Signal2, signal.Signal3, signal.Signal4, signal.Signal5
            };
        }).ToArray() };
    }

    public async ValueTask DisposeAsync()
    {
        _entries.Writer.TryComplete();
        // Only flush after Keep/rollback has completed. A slow disk/native read cannot hold the guard alive.
        try { await _worker.WaitAsync(TimeSpan.FromMilliseconds(250)).ConfigureAwait(false); }
        catch (TimeoutException) { }
    }
}
