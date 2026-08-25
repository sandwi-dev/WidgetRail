using System.Text;
using WidgetRail.Samples.SpotifyWidget;

internal sealed class SpotifyApplicationDiagnostics : ISpotifyRuntimeDiagnostics
{
    private const long MaximumBytes = 64 * 1024;
    private readonly object _gate = new();
    private readonly string _path;

    private SpotifyApplicationDiagnostics(string path)
    {
        _path = path;
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
                if (File.Exists(_path) && new FileInfo(_path).Length >= MaximumBytes)
                    File.WriteAllText(_path, string.Empty, new UTF8Encoding(false));
                File.AppendAllText(
                    _path, line + Environment.NewLine, new UTF8Encoding(false));
            }
        }
        catch (Exception exception) when (exception is IOException or
            UnauthorizedAccessException or NotSupportedException)
        {
            // Diagnostics are best-effort and must never become a worker failure.
        }
    }
}
