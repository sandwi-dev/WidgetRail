using System.Text.RegularExpressions;

namespace WidgetRail.PerformanceTelemetry;

/// <summary>Small per-process diagnostic log. Never records raw frame rows or authentication data.</summary>
public sealed class FpsDiagnosticLog(string path)
{
    private readonly object gate = new();
    public void Write(string message)
    {
        try
        {
            lock (gate)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                if (File.Exists(path) && new FileInfo(path).Length >= 128 * 1024)
                    File.Move(path, path + ".previous", overwrite: true);
                File.AppendAllText(path, $"{DateTimeOffset.UtcNow:o} pid={Environment.ProcessId} {Sanitize(message)}{Environment.NewLine}");
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.Security.SecurityException or RegexMatchTimeoutException) { }
    }
    public static string Sanitize(string value)
    {
        var bounded = value[..Math.Min(value.Length, 1024)];
        try { bounded = Regex.Replace(bounded, @"(?:[A-Za-z]:\\|\\\\)[^\r\n\""<>|]+", "[path]", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(50)); }
        catch (RegexMatchTimeoutException) { return "[diagnostic redacted]"; }
        return new string(bounded.Select(c => char.IsControl(c) ? ' ' : c).ToArray());
    }
}

/// <summary>The elevated helper retains only a bounded queue; the unelevated Bridge writes files.</summary>
public sealed class FpsDiagnosticBuffer
{
    private readonly Queue<string> messages = new();
    public void Write(string message)
    {
        lock (messages)
        {
            while (messages.Count >= 16) messages.Dequeue();
            messages.Enqueue(FpsDiagnosticLog.Sanitize(message));
        }
    }
    public string[] Drain()
    {
        lock (messages) { var result = messages.ToArray(); messages.Clear(); return result; }
    }
}
