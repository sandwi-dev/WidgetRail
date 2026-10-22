using System.Text.Json;

namespace WidgetRail.OverlayFrontend.WinUI.Shell;

internal sealed record PinnedPreferences(int Version, IReadOnlyDictionary<string, PinnedPlacement> Placements)
{
    internal static PinnedPreferences Empty => new(2, new Dictionary<string, PinnedPlacement>());
    internal bool IsValid => Version is 1 or 2 && Placements is { Count: <= ShellPreferences.MaximumWidgets } &&
        Placements.All(pair => pair.Value?.IsValid == true && (Version == 1
            ? ShellPreferences.ValidId(pair.Key) : ValidKey(pair.Key, pair.Value.LayoutId)));

    internal PinnedPlacement? Find(string widgetId, string layoutId) => Version == 1
        ? Placements.GetValueOrDefault(widgetId) is { } legacy && legacy.LayoutId == layoutId ? legacy : null
        : Placements.GetValueOrDefault(Key(widgetId, layoutId));

    internal PinnedPreferences Upgrade() => Version == 1
        ? new(2, Placements.ToDictionary(pair => Key(pair.Key, pair.Value.LayoutId), pair => pair.Value, StringComparer.Ordinal)) : this;

    internal PinnedPreferences WithPlacement(string widgetId, PinnedPlacement placement)
    {
        if (!ShellPreferences.ValidId(widgetId) || !placement.IsValid) throw new ArgumentException("Invalid pinned placement identity.");
        var values = new Dictionary<string, PinnedPlacement>(Upgrade().Placements, StringComparer.Ordinal);
        var key = Key(widgetId, placement.LayoutId);
        values.Remove(key);
        if (values.Count >= ShellPreferences.MaximumWidgets) values.Remove(values.Keys.First());
        values.Add(key, placement);
        return new(2, values);
    }

    // IDs cannot contain ':', so the composite key is unambiguous. The stored
    // layout must agree with the key; one layout never borrows another's size.
    private static string Key(string widgetId, string layoutId) => widgetId + ":" + layoutId;
    private static bool ValidKey(string key, string layoutId)
    {
        var separator = key.IndexOf(':');
        return separator > 0 && ShellPreferences.ValidId(key[..separator]) && key[(separator + 1)..] == layoutId;
    }
}

/// <summary>Geometry preferences only. Pin selection and interaction ownership are session-only.</summary>
internal sealed class PinnedPreferencesStore(string settingsRoot)
{
    private const int MaximumBytes = 1024 * 1024;
    private readonly string path = Path.Combine(settingsRoot, "winui-pinned-state.json");
    private readonly SemaphoreSlim writes = new(1, 1);
    private long revision;

    internal async Task<PinnedPreferences> LoadAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            if (!File.Exists(path) || new FileInfo(path).Length > MaximumBytes) return PinnedPreferences.Empty;
            await using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            var bytes = new byte[MaximumBytes + 1];
            var count = await input.ReadAtLeastAsync(bytes, bytes.Length, throwOnEndOfStream: false, cancellationToken);
            if (count > MaximumBytes) return PinnedPreferences.Empty;
            var value = JsonSerializer.Deserialize(bytes.AsSpan(0, count), ShellJsonContext.Default.PinnedPreferences);
            return value?.IsValid == true ? Freeze(value.Upgrade()) : PinnedPreferences.Empty;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException)
        { return PinnedPreferences.Empty; }
    }

    internal async Task SaveAsync(PinnedPreferences value, CancellationToken cancellationToken = default)
    {
        if (!value.IsValid) throw new ArgumentException("Invalid pinned preferences.", nameof(value));
        value = Freeze(value.Upgrade());
        var bytes = JsonSerializer.SerializeToUtf8Bytes(value, ShellJsonContext.Default.PinnedPreferences);
        if (bytes.Length > MaximumBytes) throw new ArgumentException("Pinned preferences exceed their storage bound.", nameof(value));
        var request = Interlocked.Increment(ref revision);
        await writes.WaitAsync(cancellationToken);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            if (request != Interlocked.Read(ref revision)) return;
            Directory.CreateDirectory(settingsRoot);
            await File.WriteAllBytesAsync(temporary, bytes, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (request == Interlocked.Read(ref revision)) File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            try { if (File.Exists(temporary)) File.Delete(temporary); }
            finally { writes.Release(); }
        }
    }
    private static PinnedPreferences Freeze(PinnedPreferences value) =>
        value with { Placements = new Dictionary<string, PinnedPlacement>(value.Placements, StringComparer.Ordinal) };
}
