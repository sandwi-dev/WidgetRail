using System.Text;
using WidgetRail.Samples.PlayniteLibrary;

internal sealed class PlayniteLibraryApplicationDiagnostics :
    IPlayniteLibraryArtworkDiagnostics
{
    private const long MaximumBytes = 64 * 1024;
    private readonly object _gate = new();
    private readonly string _path;

    private PlayniteLibraryApplicationDiagnostics(string path) => _path = path;

    internal static PlayniteLibraryApplicationDiagnostics CreateDefault()
    {
        var root = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "WidgetRail",
            "community-apps",
            "widgetrail.samples.playnite-library");
        return new(Path.Combine(root, "artwork-diagnostics.log"));
    }

    public void Record(string stage, string code, int count, string sizeClass)
    {
        if (!PlayniteLibraryArtworkDiagnostics.TryEncode(
                stage, code, count, sizeClass, out var line))
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
            // Artwork fallback diagnostics are best-effort and never own the library.
        }
    }
}
