using System.Text.Json;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.OverlayFrontend.WinUI.Notifications;

internal sealed record NotificationCategoryPreference(NotificationPresentation Mode, int? Opacity = null);

internal sealed class NotificationPreferences(string path)
{
    private Dictionary<string, NotificationPresentation> values = new(StringComparer.Ordinal);
    private Dictionary<string, NotificationCategoryPreference> categories = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim writes = new(1, 1);
    internal bool LoadFailed { get; private set; }
    internal NotificationPresentation Get(string widget) => values.GetValueOrDefault(widget,
        LoadFailed ? NotificationPresentation.ListOnly : NotificationPresentation.BannerAndList);
    internal NotificationCategoryPreference Category(string widget, string category) =>
        categories.GetValueOrDefault(Key(widget, category), new(LoadFailed ? NotificationPresentation.Off : NotificationPresentation.BannerAndList));
    internal NotificationPresentation Effective(string widget, string category)
    {
        var master = Get(widget); var local = Category(widget, category).Mode;
        return master == NotificationPresentation.Off || local == NotificationPresentation.Off ? NotificationPresentation.Off :
            master == NotificationPresentation.ListOnly || local == NotificationPresentation.ListOnly ? NotificationPresentation.ListOnly : NotificationPresentation.BannerAndList;
    }
    internal async Task LoadAsync(CancellationToken token)
    {
        try
        {
            if (!File.Exists(path)) return;
            if (new FileInfo(path).Length > 1024 * 1024) throw new InvalidDataException();
            using var stream = File.OpenRead(path);
            using var data = await JsonDocument.ParseAsync(stream, cancellationToken: token);
            var version = data.RootElement.GetProperty("version").GetInt32();
            if (version is not (1 or 2)) throw new InvalidDataException();
            var loaded = new Dictionary<string, NotificationPresentation>(StringComparer.Ordinal);
            var categoryValues = new Dictionary<string, NotificationCategoryPreference>(StringComparer.Ordinal);
            foreach (var property in data.RootElement.GetProperty("widgets").EnumerateObject())
            {
                if (loaded.Count >= 256 || !ValidId(property.Name) || !Enum.TryParse<NotificationPresentation>(property.Value.GetString(), out var mode) || !Enum.IsDefined(mode) || !loaded.TryAdd(property.Name, mode))
                    throw new InvalidDataException();
            }
            if (version == 2)
                foreach (var property in data.RootElement.GetProperty("categories").EnumerateObject())
                {
                    var parts = property.Name.Split('|');
                    if (parts.Length != 2 || !ValidId(parts[0]) || !WidgetNotificationContract.ValidCategoryId(parts[1]) || categoryValues.Count >= 2304 ||
                        !Enum.TryParse<NotificationPresentation>(property.Value.GetProperty("mode").GetString(), out var mode) || !Enum.IsDefined(mode)) throw new InvalidDataException();
                    var opacity = property.Value.TryGetProperty("opacity", out var field) ? field.GetInt32() : (int?)null;
                    if (opacity is < 40 or > 100 || !categoryValues.TryAdd(property.Name, new(mode, opacity))) throw new InvalidDataException();
                }
            values = loaded; categories = categoryValues; LoadFailed = false;
        }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or JsonException or InvalidOperationException or KeyNotFoundException or FormatException)
        { LoadFailed = true; }
    }
    internal Task SetAsync(string widget, NotificationPresentation mode, CancellationToken token)
    {
        if (!ValidId(widget) || !Enum.IsDefined(mode)) throw new ArgumentException("Invalid notification preference.");
        return MutateAsync((next, _) => { next[widget] = mode; return true; }, token);
    }
    internal Task SetCategoryAsync(string widget, string category, NotificationCategoryPreference value, CancellationToken token)
    {
        if (!ValidId(widget) || !WidgetNotificationContract.ValidCategoryId(category) || !Enum.IsDefined(value.Mode) || value.Opacity is < 40 or > 100)
            throw new ArgumentException("Invalid notification category preference.");
        return MutateAsync((_, next) => { next[Key(widget, category)] = value; return true; }, token);
    }
    internal Task EnsureCategoriesAsync(string widget, IReadOnlyList<WidgetNotificationCategory> declarations, CancellationToken token)
    {
        if (!ValidId(widget) || !WidgetNotificationContract.ValidCategories(declarations)) throw new ArgumentException("Invalid category declarations.");
        if (LoadFailed || declarations.All(c => categories.ContainsKey(Key(widget, c.Id)))) return Task.CompletedTask;
        return MutateAsync((_, next) =>
        {
            var changed = false;
            foreach (var category in declarations)
                changed |= next.TryAdd(Key(widget, category.Id), new(category.EnabledByDefault ? NotificationPresentation.BannerAndList : NotificationPresentation.Off, category.DefaultOpacityPercent));
            return changed;
        }, token);
    }
    private async Task MutateAsync(Func<Dictionary<string, NotificationPresentation>, Dictionary<string, NotificationCategoryPreference>, bool> mutate, CancellationToken token)
    {
        await writes.WaitAsync(token);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            if (LoadFailed) throw new IOException("Notification preferences could not be read; the file was not overwritten.");
            var next = new Dictionary<string, NotificationPresentation>(values, StringComparer.Ordinal);
            var nextCategories = new Dictionary<string, NotificationCategoryPreference>(categories, StringComparer.Ordinal);
            if (!mutate(next, nextCategories)) return;
            if (next.Count > 256 || nextCategories.Count > 2304) throw new IOException("Notification preference capacity reached.");
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            await using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                using var json = new Utf8JsonWriter(stream);
                json.WriteStartObject(); json.WriteNumber("version", 2); json.WriteStartObject("widgets");
                foreach (var item in next.OrderBy(item => item.Key, StringComparer.Ordinal)) json.WriteString(item.Key, item.Value.ToString());
                json.WriteEndObject(); json.WriteStartObject("categories");
                foreach (var item in nextCategories.OrderBy(item => item.Key, StringComparer.Ordinal))
                {
                    json.WriteStartObject(item.Key); json.WriteString("mode", item.Value.Mode.ToString());
                    if (item.Value.Opacity is { } opacity) json.WriteNumber("opacity", opacity);
                    json.WriteEndObject();
                }
                json.WriteEndObject(); json.WriteEndObject(); await json.FlushAsync(token); await stream.FlushAsync(token);
                if (stream.Length > 1024 * 1024) throw new IOException("Notification preference capacity reached.");
                token.ThrowIfCancellationRequested(); stream.Flush(flushToDisk: true);
            }
            token.ThrowIfCancellationRequested();
            File.Move(temporary, path, true); values = next; categories = nextCategories;
        }
        finally
        {
            try { if (File.Exists(temporary)) File.Delete(temporary); } catch (IOException) { }
            writes.Release();
        }
    }
    private static string Key(string widget, string category) => widget + "|" + category;
    private static bool ValidId(string value) => value.Length is > 0 and <= 128 && value.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '-' or '_');
}
