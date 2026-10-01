using System.Text.Json;

namespace WidgetRail.OverlayFrontend.WinUI.Shell;

/// <summary>Product preferences only; native controls own focus geometry and layout.</summary>
internal sealed record ShellPreferences(IReadOnlyList<string> Order, string? LastWidget, bool ReopenWidget)
{
    internal static ShellPreferences Empty { get; } = new([], null, false);
    internal const int MaximumWidgets = 256;
    internal static bool ValidId(string? id) => id is { Length: > 0 and <= 128 } &&
        id.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_' or '.');

    internal ShellPreferences Reconcile(IEnumerable<string> available, bool complete)
    {
        var admitted = available.Where(ValidId).Distinct(StringComparer.Ordinal).Take(MaximumWidgets).ToArray();
        var allowed = admitted.ToHashSet(StringComparer.Ordinal);
        var order = Order.Where(id => !complete || allowed.Contains(id)).Concat(admitted)
            .Distinct(StringComparer.Ordinal).Take(MaximumWidgets).ToArray();
        var last = complete && LastWidget is { } id && !allowed.Contains(id) ? null : LastWidget;
        return new(order, last, last is not null && ReopenWidget);
    }

    internal ShellPreferences Move(string id, int delta)
    {
        var order = Order.ToArray();
        var index = Array.IndexOf(order, id);
        var next = (long)index + delta;
        if (index < 0 || next < 0 || next >= order.Length) return this;
        (order[index], order[(int)next]) = (order[(int)next], order[index]);
        return this with { Order = order };
    }

    internal IReadOnlyList<string> AvailableOrder(IEnumerable<string> available)
    {
        var ids = available.Distinct(StringComparer.Ordinal).ToArray();
        var admitted = ids.ToHashSet(StringComparer.Ordinal);
        // An incomplete catalog must not hide new widgets just because saved
        // positions for still-undiscovered widgets occupy the persistence bound.
        return Order.Where(admitted.Contains).Concat(ids).Distinct(StringComparer.Ordinal).ToArray();
    }
}

/// <summary>Explicit-profile, bounded state. Atomic writes never modify the native host's legacy file.</summary>
internal sealed class ShellPreferencesStore(string settingsRoot)
{
    private readonly string path = Path.Combine(settingsRoot, "winui-shell-state.json");
    private readonly SemaphoreSlim writes = new(1, 1);
    private long revision;

    internal async Task<ShellPreferences> LoadAsync(CancellationToken cancellationToken = default)
    {
        var source = File.Exists(path) ? path : Path.Combine(settingsRoot, "overlay-state.ini");
        try
        {
            if (!File.Exists(source) || new FileInfo(source).Length > 65536) return ShellPreferences.Empty;
            using var reader = new StreamReader(source);
            var buffer = new char[65537];
            var read = await reader.ReadBlockAsync(buffer.AsMemory(), cancellationToken);
            if (read == buffer.Length) return ShellPreferences.Empty;
            var content = new string(buffer, 0, read);
            var value = source == path ? JsonSerializer.Deserialize(content, ShellJsonContext.Default.ShellPreferences) : Legacy(content);
            return Valid(value) ? value! with { Order = value!.Order.ToArray() } : ShellPreferences.Empty;
        }
        catch (Exception error) when (error is JsonException or IOException or UnauthorizedAccessException or FormatException or OverflowException)
        { return ShellPreferences.Empty; }
    }

    internal async Task SaveAsync(ShellPreferences value, CancellationToken cancellationToken = default)
    {
        if (!Valid(value)) throw new ArgumentException("Invalid shell preferences.", nameof(value));
        value = value with { Order = value.Order.ToArray() };
        var requested = Interlocked.Increment(ref revision);
        await writes.WaitAsync(cancellationToken);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            if (requested != Interlocked.Read(ref revision)) return;
            Directory.CreateDirectory(settingsRoot);
            await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(value, ShellJsonContext.Default.ShellPreferences), cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (requested == Interlocked.Read(ref revision)) File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            try { if (File.Exists(temporary)) File.Delete(temporary); }
            finally { writes.Release(); }
        }
    }

    private static bool Valid(ShellPreferences? value) => value?.Order is { Count: <= ShellPreferences.MaximumWidgets } order &&
        order.All(ShellPreferences.ValidId) && order.Distinct(StringComparer.Ordinal).Count() == order.Count &&
        (value.LastWidget is null ? !value.ReopenWidget : ShellPreferences.ValidId(value.LastWidget));

    private static ShellPreferences? Legacy(string text)
    {
        var fields = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (fields.Length < 4 || fields[0] != "v2" || !int.TryParse(fields[1], out var count) ||
            count < 0 || count > ShellPreferences.MaximumWidgets || fields.Length != count + 4 ||
            fields[^1] is not ("0" or "1")) return null;
        return new(fields.Skip(2).Take(count).ToArray(), fields[^2] == "-" ? null : fields[^2], fields[^1] == "1");
    }
}
