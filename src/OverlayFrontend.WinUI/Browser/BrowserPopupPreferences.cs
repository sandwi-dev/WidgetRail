using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace WidgetRail.OverlayFrontend.WinUI.Browser;

internal enum BrowserPopupPlacement { Ask, InWidget, Pinned }

/// <summary>Host-owned placement choices, isolated by widget, account profile and requesting origin.</summary>
internal sealed class BrowserPopupPreferences(string path)
{
    private Dictionary<string, BrowserPopupPlacement> values = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim writes = new(1, 1);
    private Task? loading;
    internal Task LoadAsync(CancellationToken token) => loading ??= ReadAsync(token);
    internal BrowserPopupPlacement Get(string widget, string profile, string origin) =>
        values.GetValueOrDefault(Key(widget, profile, origin), BrowserPopupPlacement.Ask);

    private static string Key(string widget, string profile, string origin)
    {
        if (string.IsNullOrEmpty(widget) || widget.Length > 128 || string.IsNullOrEmpty(profile) || profile.Length > 128 ||
            origin.Length > 2048 || BrowserSiteIdentity.Origin(origin, requireHttps: false) != origin)
            throw new ArgumentException("Invalid popup preference scope.");
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{widget.Length}:{widget}{profile.Length}:{profile}{origin}")));
    }
    private async Task ReadAsync(CancellationToken token)
    {
        try
        {
            if (!File.Exists(path)) return;
            if (new FileInfo(path).Length > 128 * 1024) throw new InvalidDataException();
            using var stream = File.OpenRead(path);
            using var data = await JsonDocument.ParseAsync(stream, cancellationToken: token);
            if (data.RootElement.GetProperty("version").GetInt32() != 1) throw new InvalidDataException();
            var loaded = new Dictionary<string, BrowserPopupPlacement>(StringComparer.Ordinal);
            foreach (var property in data.RootElement.GetProperty("sites").EnumerateObject())
            {
                if (loaded.Count >= 512 || property.Name.Length != 64 || !property.Name.All(char.IsAsciiHexDigit) ||
                    !Enum.TryParse<BrowserPopupPlacement>(property.Value.GetString(), out var mode) || !Enum.IsDefined(mode))
                    throw new InvalidDataException();
                loaded.Add(property.Name, mode);
            }
            values = loaded;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException or KeyNotFoundException or FormatException or ArgumentException)
        { /* Unreadable preferences return to Ask; never silently replace a pin. */ }
    }
    internal async Task SetAsync(string widget, string profile, string origin, BrowserPopupPlacement mode, CancellationToken token)
    {
        var key = Key(widget, profile, origin);
        if (!Enum.IsDefined(mode)) throw new ArgumentException("Invalid popup placement.");
        await LoadAsync(token);
        await writes.WaitAsync(token);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            var next = new Dictionary<string, BrowserPopupPlacement>(values, StringComparer.Ordinal);
            if (mode == BrowserPopupPlacement.Ask) next.Remove(key); else next[key] = mode;
            if (next.Count > 512) throw new InvalidOperationException("Popup preference capacity reached.");
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            await using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.Asynchronous))
            {
                using var json = new Utf8JsonWriter(stream);
                json.WriteStartObject(); json.WriteNumber("version", 1); json.WriteStartObject("sites");
                foreach (var item in next.OrderBy(item => item.Key, StringComparer.Ordinal)) json.WriteString(item.Key, item.Value.ToString());
                json.WriteEndObject(); json.WriteEndObject(); await json.FlushAsync(token); await stream.FlushAsync(token);
            }
            token.ThrowIfCancellationRequested();
            File.Move(temporary, path, true); values = next;
        }
        finally
        {
            try { if (File.Exists(temporary)) File.Delete(temporary); } catch (IOException) { }
            writes.Release();
        }
    }
}
