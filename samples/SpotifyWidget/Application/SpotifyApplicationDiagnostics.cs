using System.Text;
using WidgetRail.Samples.SpotifyWidget;

internal sealed class SpotifyApplicationDiagnostics : ISpotifyRuntimeDiagnostics
{
    private const long MaximumBytes = 64 * 1024;
    private static readonly UTF8Encoding Utf8 = new(false);
    private readonly object _gate = new();
    private readonly string _path;
    private readonly string _previousPath;

    private SpotifyApplicationDiagnostics(string path)
    {
        _path = path;
        _previousPath = path + ".previous";
    }

    internal static SpotifyApplicationDiagnostics CreateDefault()
    {
        var root = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "WidgetRail",
            "community-apps",
            "widgetrail.samples.spotify");
        return new(Path.Combine(root, "runtime-diagnostics.log"));
    }

    public void Record(
        string boundary,
        string code,
        long operation = 0,
        long generation = 0,
        long elapsedMilliseconds = 0)
    {
        if (!SpotifyRuntimeDiagnostics.TryEncode(
                DateTimeOffset.UtcNow, boundary, code, operation, generation,
                elapsedMilliseconds, out var line))
            return;
        try
        {
            lock (_gate)
            {
                var directory = Path.GetDirectoryName(_path);
                if (directory is null) return;
                Directory.CreateDirectory(directory);
                var entry = line + Environment.NewLine;
                var entryBytes = Utf8.GetByteCount(entry);
                if (File.Exists(_path) &&
                    new FileInfo(_path).Length + entryBytes > MaximumBytes)
                {
                    if (File.Exists(_previousPath)) File.Delete(_previousPath);
                    File.Move(_path, _previousPath);
                }
                File.AppendAllText(_path, entry, Utf8);
            }
        }
        catch (Exception exception) when (exception is IOException or
            UnauthorizedAccessException or NotSupportedException)
        {
            // Diagnostics are best-effort and must never become a worker failure.
        }
    }
}
