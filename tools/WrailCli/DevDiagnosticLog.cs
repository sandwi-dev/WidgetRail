using System.Text;

namespace WidgetRail.WrailCli;

// One transcript for this dev invocation, shared by stdout/stderr. It never
// reads the installed overlay's logs or truncates a pre-existing user file.
internal sealed class DevDiagnosticLog : IDisposable
{
    private readonly StreamWriter _writer;
    private readonly TextWriter _warnings;
    private readonly long _maximumBytes;
    private readonly object _gate = new();
    private long _bytes;
    private bool _stopped;

    internal DevDiagnosticLog(string path, TextWriter warnings, long maximumBytes = 2 * 1024 * 1024)
        : this(new FileStream(Path.GetFullPath(path), FileMode.CreateNew, FileAccess.Write, FileShare.Read), warnings, maximumBytes)
    {
    }

    internal DevDiagnosticLog(Stream stream, TextWriter warnings, long maximumBytes = 2 * 1024 * 1024)
    {
        _warnings = warnings;
        _maximumBytes = maximumBytes;
        _writer = new StreamWriter(stream, new UTF8Encoding(false)) { AutoFlush = true };
    }

    internal TextWriter Wrap(TextWriter console, string level) => new TranscriptWriter(this, console, level);

    private void Record(string? value, string level)
    {
        lock (_gate)
        {
            if (_stopped) return;
            var text = $"[{DateTimeOffset.UtcNow:O}] [{level}] {DevSession.SafeMessage(new Exception(value ?? ""))}{Environment.NewLine}";
            if (_bytes + Encoding.UTF8.GetByteCount(text) > _maximumBytes)
            {
                Stop("Development log reached its size limit; console output continues.");
                return;
            }
            try
            {
                _writer.Write(text);
                _bytes += Encoding.UTF8.GetByteCount(text);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ObjectDisposedException)
            {
                Stop("Development log could not be written; console output continues.");
            }
        }
    }

    private void Stop(string message)
    {
        _stopped = true;
        _warnings.WriteLine($"warning: {message}");
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _stopped = true;
            try { _writer.Dispose(); }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ObjectDisposedException)
            { _warnings.WriteLine("warning: Development log could not be flushed."); }
        }
    }

    private sealed class TranscriptWriter(DevDiagnosticLog owner, TextWriter console, string level) : TextWriter
    {
        public override Encoding Encoding => console.Encoding;
        public override void WriteLine(string? value)
        {
            console.WriteLine(value);
            owner.Record(value, level);
        }
        public override async Task WriteLineAsync(string? value)
        {
            await console.WriteLineAsync(value);
            owner.Record(value, level);
        }
    }
}
