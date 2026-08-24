using System.Globalization;
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
        if (!IsToken(boundary) || !IsToken(code) || operation < 0 ||
            generation < 0 || elapsedMilliseconds < 0)
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
                var line = string.Concat(
                    DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture),
                    " boundary=", boundary,
                    " code=", code,
                    " operation=", operation.ToString(CultureInfo.InvariantCulture),
                    " generation=", generation.ToString(CultureInfo.InvariantCulture),
                    " elapsed-ms=", elapsedMilliseconds.ToString(CultureInfo.InvariantCulture),
                    Environment.NewLine);
                File.AppendAllText(_path, line, new UTF8Encoding(false));
            }
        }
        catch (Exception exception) when (exception is IOException or
            UnauthorizedAccessException or NotSupportedException)
        {
            // Diagnostics are best-effort and must never become a worker failure.
        }
    }

    private static bool IsToken(string value) =>
        value.Length is > 0 and <= 64 &&
        value.All(character => char.IsAsciiLetterOrDigit(character) ||
            character is '-' or '_');
}
