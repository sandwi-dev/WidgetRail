using System.Text.Json;

namespace WidgetRail.OverlayFrontend.WinUI.Browser;

internal sealed class WebNotificationPreferences(string path)
{
    private Dictionary<string, WebNotificationPresentation> values = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim writes = new(1, 1);
    internal bool LoadFailed { get; private set; }
    internal WebNotificationPresentation Get(string widget) => values.GetValueOrDefault(widget,
        LoadFailed ? WebNotificationPresentation.ListOnly : WebNotificationPresentation.BannerAndList);
    internal async Task LoadAsync(CancellationToken token)
    {
        try
        {
            if (!File.Exists(path)) return;
            if (new FileInfo(path).Length > 64 * 1024) throw new InvalidDataException();
            using var stream = File.OpenRead(path);
            using var data = await JsonDocument.ParseAsync(stream, cancellationToken: token);
            if (data.RootElement.GetProperty("version").GetInt32() != 1) throw new InvalidDataException();
            var loaded = new Dictionary<string, WebNotificationPresentation>(StringComparer.Ordinal);
            foreach (var property in data.RootElement.GetProperty("widgets").EnumerateObject())
            {
                if (loaded.Count >= 256 || !ValidId(property.Name) || !Enum.TryParse<WebNotificationPresentation>(property.Value.GetString(), out var mode) || !Enum.IsDefined(mode))
                    throw new InvalidDataException();
                loaded[property.Name] = mode;
            }
            values = loaded; LoadFailed = false;
        }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or JsonException or InvalidOperationException or KeyNotFoundException or FormatException)
        { LoadFailed = true; }
    }
    internal async Task SetAsync(string widget, WebNotificationPresentation mode, CancellationToken token)
    {
        if (!ValidId(widget) || !Enum.IsDefined(mode)) throw new ArgumentException("Invalid notification preference.");
        await writes.WaitAsync(token);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            var next = new Dictionary<string, WebNotificationPresentation>(values, StringComparer.Ordinal) { [widget] = mode };
            if (next.Count > 256) throw new InvalidOperationException("Notification preference capacity reached.");
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            await using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.Asynchronous))
            {
                using var json = new Utf8JsonWriter(stream);
                json.WriteStartObject(); json.WriteNumber("version", 1); json.WriteStartObject("widgets");
                foreach (var item in next.OrderBy(item => item.Key, StringComparer.Ordinal)) json.WriteString(item.Key, item.Value.ToString());
                json.WriteEndObject(); json.WriteEndObject(); await json.FlushAsync(token); await stream.FlushAsync(token);
            }
            File.Move(temporary, path, true); values = next; LoadFailed = false;
        }
        finally
        {
            try { if (File.Exists(temporary)) File.Delete(temporary); } catch (IOException) { }
            writes.Release();
        }
    }
    private static bool ValidId(string value) => value.Length is > 0 and <= 128 && value.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '-' or '_');
}
