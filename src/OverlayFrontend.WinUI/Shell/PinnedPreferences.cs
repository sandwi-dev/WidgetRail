using System.Text.Json;

namespace WidgetRail.OverlayFrontend.WinUI.Shell;

internal sealed record PinnedPreferences(int Version, string? WidgetId, IReadOnlyDictionary<string, PinnedPlacement> Placements)
{
    internal static PinnedPreferences Empty => new(1, null, new Dictionary<string, PinnedPlacement>());
    internal bool IsValid => Version == 1 && Placements is { Count: <= ShellPreferences.MaximumWidgets } &&
        Placements.All(pair => ShellPreferences.ValidId(pair.Key) && pair.Value?.IsValid == true) &&
        (WidgetId is null || ShellPreferences.ValidId(WidgetId) && Placements.ContainsKey(WidgetId));
}

/// <summary>Explicit-profile pinned preferences; interaction ownership is never persisted.</summary>
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
            return value?.IsValid == true ? Freeze(value) : PinnedPreferences.Empty;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException)
        { return PinnedPreferences.Empty; }
    }

    internal async Task SaveAsync(PinnedPreferences value, CancellationToken cancellationToken = default)
    {
        if (!value.IsValid) throw new ArgumentException("Invalid pinned preferences.", nameof(value));
        value = Freeze(value);
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
