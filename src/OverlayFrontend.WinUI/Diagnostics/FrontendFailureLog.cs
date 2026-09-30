using System.Text;

namespace WidgetRail.OverlayFrontend.WinUI.Diagnostics;

/// <summary>Durable, bounded last-chance diagnostics. Never changes exception handling.</summary>
internal sealed class FrontendFailureLog(string path)
{
    internal static FrontendFailureLog Current { get; } = new(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "WidgetRail", "WinUI", "diagnostics", "frontend-errors.log"));
    private readonly object gate = new();
    private string context = "startup";
    internal void SetContext(string value) => Volatile.Write(ref context, value);

    internal void Write(string source, Exception? error, string? message = null)
    {
        try
        {
            var detail = $"{DateTimeOffset.UtcNow:O} pid={Environment.ProcessId} source={source} " +
                $"hresult=0x{error?.HResult ?? 0:X8} {Volatile.Read(ref context)}\n{message}\n{error}\n";
            var limit = source == "xaml-layout" ? 128 * 1024 : 32 * 1024;
            if (detail.Length > limit) detail = detail[..limit] + "\n[truncated]\n";
            var bytes = Encoding.UTF8.GetBytes(detail);
            lock (gate)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
                if (File.Exists(path) && new FileInfo(path).Length + bytes.Length > 512 * 1024)
                    File.Move(path, path + ".previous", overwrite: true);
                // A queued async log can be lost when XAML terminates immediately
                // after this callback. Only failures take this synchronous path.
                using var output = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read);
                output.Write(bytes); output.Flush(flushToDisk: true);
            }
        }
        catch { /* Logging failure must never replace the original exception. */ }
    }
}
