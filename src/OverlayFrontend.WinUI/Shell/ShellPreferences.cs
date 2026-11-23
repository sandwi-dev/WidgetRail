using System.Text.Json;

namespace WidgetRail.OverlayFrontend.WinUI.Shell;

internal enum TrayDirection { Up, Right, Down, Left }

internal sealed record TrayActionBinding(string Provider, string Action, string? Publisher = null, string? Fallback = null,
    string? Package = null, string? Label = null)
{
    internal bool SameAction(TrayActionBinding? other) => other is not null && Provider == other.Provider &&
        Action == other.Action && Publisher == other.Publisher && Package == other.Package;
    internal bool IsValid => ShellPreferences.ValidId(Provider) && ShellPreferences.ValidId(Action) &&
        (Fallback is null || ShellPreferences.ValidId(Fallback) && Fallback != Action) &&
        (Label is null || Label is { Length: > 0 and <= 80 } && !Label.Any(char.IsControl)) &&
        ShellPreferences.ValidId(Publisher) && ShellPreferences.ValidId(Package);
}

internal sealed record TrayActionSlots(TrayActionBinding? Up = null, TrayActionBinding? Right = null,
    TrayActionBinding? Down = null, TrayActionBinding? Left = null)
{
    internal bool IsValid => Up?.IsValid != false && Right?.IsValid != false && Down?.IsValid != false && Left?.IsValid != false;
    internal TrayActionSlots Repair() => new(Up?.IsValid == true ? Up : null, Right?.IsValid == true ? Right : null,
        Down?.IsValid == true ? Down : null, Left?.IsValid == true ? Left : null);
    internal TrayActionBinding? Get(TrayDirection direction) => direction switch
    {
        TrayDirection.Up => Up, TrayDirection.Right => Right, TrayDirection.Down => Down, TrayDirection.Left => Left,
        _ => throw new ArgumentOutOfRangeException(nameof(direction))
    };
    internal TrayActionSlots Assign(TrayDirection direction, TrayActionBinding? value)
    {
        if (value?.IsValid == false) throw new ArgumentException("Invalid tray quick action.", nameof(value));
        return direction switch
        {
            TrayDirection.Up => this with { Up = value }, TrayDirection.Right => this with { Right = value },
            TrayDirection.Down => this with { Down = value }, TrayDirection.Left => this with { Left = value },
            _ => throw new ArgumentOutOfRangeException(nameof(direction))
        };
    }
}

internal sealed record MediaAudioPreference(bool Muted = true, double Volume = 1)
{
    internal bool IsValid => double.IsFinite(Volume) && Volume is >= 0 and <= 1;
}

/// <summary>Product preferences only; native controls own focus geometry and layout.</summary>
internal sealed record ShellPreferences(IReadOnlyList<string> Order, string? LastWidget, bool ReopenWidget)
{
    internal static ShellPreferences Empty { get; } = new([], null, false);
    public TrayActionSlots TrayActions { get; init; } = new();
    public IReadOnlyDictionary<string, MediaAudioPreference> MediaAudio { get; init; } =
        new Dictionary<string, MediaAudioPreference>(StringComparer.Ordinal);
    internal const int MaximumWidgets = 256;
    internal const int MaximumAudioWidgets = 128;
    internal static bool ValidId(string? id) => id is { Length: > 0 and <= 128 } &&
        id.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_' or '.');

    internal ShellPreferences Reconcile(IEnumerable<string> available, bool complete)
    {
        var admitted = available.Where(ValidId).Distinct(StringComparer.Ordinal).Take(MaximumWidgets).ToArray();
        var allowed = admitted.ToHashSet(StringComparer.Ordinal);
        var order = Order.Where(id => !complete || allowed.Contains(id)).Concat(admitted)
            .Distinct(StringComparer.Ordinal).Take(MaximumWidgets).ToArray();
        var last = complete && LastWidget is { } id && !allowed.Contains(id) ? null : LastWidget;
        return this with { Order = order, LastWidget = last, ReopenWidget = last is not null && ReopenWidget };
    }

    internal ShellPreferences RememberAudio(string widgetId, MediaAudioPreference value)
    {
        if (!ValidId(widgetId) || !value.IsValid) throw new ArgumentException("Invalid media audio preference.");
        if (!MediaAudio.ContainsKey(widgetId) && MediaAudio.Count >= MaximumAudioWidgets) return this;
        var copy = new Dictionary<string, MediaAudioPreference>(MediaAudio, StringComparer.Ordinal) { [widgetId] = value };
        return this with { MediaAudio = copy };
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
            if (!File.Exists(source)) return ShellPreferences.Empty;
            if (new FileInfo(source).Length > 65536) return ShellPreferences.Empty;
            using var reader = new StreamReader(source);
            var buffer = new char[65537];
            var read = await reader.ReadBlockAsync(buffer.AsMemory(), cancellationToken);
            if (read == buffer.Length) return ShellPreferences.Empty;
            var content = new string(buffer, 0, read);
            var value = source == path ? JsonSerializer.Deserialize(content, ShellJsonContext.Default.ShellPreferences) : Legacy(content);
            // Generated deserialization assigns null to absent init-only properties in older profiles.
            if (value is { MediaAudio: null }) value = value with { MediaAudio = ShellPreferences.Empty.MediaAudio };
            if (value is not null && value.TrayActions?.IsValid != true)
                value = value with { TrayActions = value.TrayActions?.Repair() ?? new() };
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
        (value.LastWidget is null ? !value.ReopenWidget : ShellPreferences.ValidId(value.LastWidget)) &&
        value.TrayActions?.IsValid == true && value.MediaAudio is { Count: <= ShellPreferences.MaximumAudioWidgets } audio &&
        audio.All(pair => ShellPreferences.ValidId(pair.Key) && pair.Value?.IsValid == true);

    private static ShellPreferences? Legacy(string text)
    {
        var fields = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (fields.Length < 4 || fields[0] != "v2" || !int.TryParse(fields[1], out var count) ||
            count < 0 || count > ShellPreferences.MaximumWidgets || fields.Length != count + 4 ||
            fields[^1] is not ("0" or "1")) return null;
        return new(fields.Skip(2).Take(count).ToArray(), fields[^2] == "-" ? null : fields[^2], fields[^1] == "1");
    }
}
