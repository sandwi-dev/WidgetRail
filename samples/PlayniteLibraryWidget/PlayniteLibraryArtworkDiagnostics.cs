namespace WidgetRail.Samples.PlayniteLibrary;

internal interface IPlayniteLibraryArtworkDiagnostics
{
    void Record(string stage, string code, int count, string sizeClass);
    void RecordMemory(PlayniteArtworkMemoryEvent value) { }
}

internal enum PlayniteArtworkMemoryEventKind
{
    Hit,
    Miss,
    Store,
    Eviction,
    FallbackAlias,
}

internal enum PlayniteArtworkRole
{
    Cover,
    Background,
    Neutral,
}

internal readonly record struct PlayniteArtworkMemoryEvent(
    PlayniteArtworkMemoryEventKind Kind,
    PlayniteArtworkRole Role,
    int Bytes = 0,
    int PreviousBytes = 0);

internal readonly record struct PlayniteArtworkMemorySnapshot(
    long Entries,
    long CurrentBytes,
    long HighWaterBytes,
    long Hits,
    long Misses,
    long FallbackAliases,
    long Evictions,
    long CoverEvents,
    long BackgroundEvents,
    long NeutralEvents);

internal sealed class PlayniteArtworkMemoryCounters
{
    private readonly object _gate = new();
    private PlayniteArtworkMemorySnapshot _snapshot;

    internal PlayniteArtworkMemorySnapshot Record(PlayniteArtworkMemoryEvent value)
    {
        if (value.Bytes < 0 || value.PreviousBytes < 0)
            throw new ArgumentOutOfRangeException(nameof(value));
        lock (_gate)
        {
            var entries = _snapshot.Entries;
            var currentBytes = _snapshot.CurrentBytes;
            if (value.Kind == PlayniteArtworkMemoryEventKind.Store)
            {
                if (value.PreviousBytes == 0) entries = AddSaturated(entries, 1);
                currentBytes = AddSignedSaturated(
                    currentBytes, (long)value.Bytes - value.PreviousBytes);
            }
            else if (value.Kind == PlayniteArtworkMemoryEventKind.Eviction)
            {
                entries = Math.Max(0, entries - 1);
                currentBytes = AddSignedSaturated(currentBytes, -value.Bytes);
            }

            _snapshot = new PlayniteArtworkMemorySnapshot(
                entries,
                currentBytes,
                Math.Max(_snapshot.HighWaterBytes, currentBytes),
                AddIf(_snapshot.Hits, value.Kind == PlayniteArtworkMemoryEventKind.Hit),
                AddIf(_snapshot.Misses, value.Kind == PlayniteArtworkMemoryEventKind.Miss),
                AddIf(_snapshot.FallbackAliases,
                    value.Kind == PlayniteArtworkMemoryEventKind.FallbackAlias),
                AddIf(_snapshot.Evictions,
                    value.Kind == PlayniteArtworkMemoryEventKind.Eviction),
                AddIf(_snapshot.CoverEvents, value.Role == PlayniteArtworkRole.Cover),
                AddIf(_snapshot.BackgroundEvents,
                    value.Role == PlayniteArtworkRole.Background),
                AddIf(_snapshot.NeutralEvents, value.Role == PlayniteArtworkRole.Neutral));
            return _snapshot;
        }
    }

    internal PlayniteArtworkMemorySnapshot Capture()
    {
        lock (_gate) return _snapshot;
    }

    internal void Reset()
    {
        lock (_gate) _snapshot = default;
    }

    internal static long AddSaturated(long value, long increment) =>
        increment <= 0 ? value : value >= long.MaxValue - increment
            ? long.MaxValue
            : value + increment;

    private static long AddIf(long value, bool add) =>
        add ? AddSaturated(value, 1) : value;

    private static long AddSignedSaturated(long value, long delta)
    {
        if (delta >= 0) return AddSaturated(value, delta);
        return delta == long.MinValue || -delta >= value ? 0 : value + delta;
    }
}

internal static class PlayniteLibraryArtworkDiagnostics
{
    internal static IPlayniteLibraryArtworkDiagnostics None { get; } =
        new NullDiagnostics();

    internal static bool TryEncode(
        string stage,
        string code,
        int count,
        string sizeClass,
        out string line)
    {
        if (!IsToken(stage) || !IsToken(code) || !IsToken(sizeClass) || count is < 1 or > 128)
        {
            line = string.Empty;
            return false;
        }

        line = string.Concat(
            "stage=", stage,
            " code=", code,
            " count=", count.ToString(System.Globalization.CultureInfo.InvariantCulture),
            " size-class=", sizeClass);
        return true;
    }

    private static bool IsToken(string value) =>
        value.Length is > 0 and <= 48 &&
        value.All(character => char.IsAsciiLetterOrDigit(character) ||
            character is '-' or '_');

    private sealed class NullDiagnostics : IPlayniteLibraryArtworkDiagnostics
    {
        public void Record(string stage, string code, int count, string sizeClass)
        {
        }
    }
}
