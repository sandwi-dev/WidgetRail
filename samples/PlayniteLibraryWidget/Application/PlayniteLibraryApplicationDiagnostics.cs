using System.Text;
using WidgetRail.Samples.PlayniteLibrary;

internal sealed class PlayniteLibraryApplicationDiagnostics :
    IPlayniteLibraryArtworkDiagnostics
{
    private const long MaximumBytes = 64 * 1024;
    private readonly object _gate = new();
    private readonly string _path;
    private readonly PlayniteArtworkMemoryCounters _memory = new();
    private long _lastMemoryWriteTick;

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
        Append(line);
    }

    public void RecordMemory(PlayniteArtworkMemoryEvent value)
    {
        var snapshot = _memory.Record(value);
        var now = Environment.TickCount64;
        while (true)
        {
            var prior = Volatile.Read(ref _lastMemoryWriteTick);
            if (prior != 0 && now >= prior && now - prior < 1_000) return;
            if (Interlocked.CompareExchange(ref _lastMemoryWriteTick, now, prior) == prior)
                break;
        }
        var line = string.Concat(
            "stage=memory code=", MemoryCode(value),
            " entries=", snapshot.Entries,
            " current-bytes=", snapshot.CurrentBytes,
            " high-water-bytes=", snapshot.HighWaterBytes,
            " hits=", snapshot.Hits,
            " misses=", snapshot.Misses,
            " background-to-cover-fallbacks=", snapshot.BackgroundToCoverFallbacks,
            " evictions=", snapshot.Evictions,
            " cover-events=", snapshot.CoverEvents,
            " background-events=", snapshot.BackgroundEvents,
            " neutral-events=", snapshot.NeutralEvents);
        Append(line);
    }

    private void Append(string line)
    {
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

    private static string MemoryCode(PlayniteArtworkMemoryEvent value) =>
        string.Concat(
            value.Kind.ToString().ToLowerInvariant(), "-",
            value.Role.ToString().ToLowerInvariant());
}
