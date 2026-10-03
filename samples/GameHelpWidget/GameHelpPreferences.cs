using System.Text.Json;

namespace WidgetRail.Samples.GameHelp;

internal sealed record GameHelpPreferences(bool Grounding = false, string Spoilers = "Hints first");

internal sealed class GameHelpPreferencesStore(string? path = null)
{
    private readonly string path = path ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "WidgetRail", "applications", "game-help", "settings.json");

    internal GameHelpPreferences Load()
    {
        try
        {
            if (!File.Exists(path) || new FileInfo(path).Length > 4096) return new();
            using var document = JsonDocument.Parse(File.ReadAllBytes(path));
            var root = document.RootElement;
            var grounding = root.TryGetProperty("grounding", out var enabled) && enabled.ValueKind == JsonValueKind.True;
            var spoilers = root.TryGetProperty("spoilers", out var mode) && mode.ValueKind == JsonValueKind.String ? mode.GetString() : null;
            return new(grounding, spoilers is "Hints first" or "Balanced" or "Full solution" ? spoilers : "Hints first");
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException) { return new(); }
    }

    internal void Save(GameHelpPreferences preferences)
    {
        if (preferences.Spoilers is not ("Hints first" or "Balanced" or "Full solution")) throw new ArgumentException("Invalid spoiler preference.");
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            using (var json = new Utf8JsonWriter(stream))
            {
                json.WriteStartObject();
                json.WriteNumber("schemaVersion", 1);
                json.WriteBoolean("grounding", preferences.Grounding);
                json.WriteString("spoilers", preferences.Spoilers);
                json.WriteEndObject();
            }
            File.Move(temporary, path, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
