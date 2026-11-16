using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Web.Script.Serialization;

namespace WidgetRail.Playnite;

/// <summary>Bounded legacy compatibility; no writes and no dependency on optional plugin assemblies.</summary>
internal sealed class OptionalPluginData
{
    private readonly string root;
    internal OptionalPluginData(string root) { this.root = root; }
    private Dictionary<string, object>? Read(string provider, string folder, Guid game)
    {
        var path = Path.Combine(root, provider, folder, game.ToString("D") + ".json");
        if (!File.Exists(path)) return null;
        using (var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
        using (var output = new MemoryStream())
        {
            if (input.Length > 2 * 1024 * 1024) return null;
            var buffer = new byte[8192]; int count;
            while ((count = input.Read(buffer, 0, buffer.Length)) != 0)
            { if (output.Length + count > 2 * 1024 * 1024) return null; output.Write(buffer, 0, count); }
            return new JavaScriptSerializer { MaxJsonLength = 2 * 1024 * 1024, RecursionLimit = 24 }
                .DeserializeObject(new UTF8Encoding(false, true).GetString(output.ToArray())) as Dictionary<string, object>;
        }
    }
    private static object? Get(Dictionary<string, object> data, string name) => data.TryGetValue(name, out var value) ? value : null;
    private static string? Date(object? value) => value is DateTime time ? time.ToString("o", CultureInfo.InvariantCulture) :
        value is string text && text.Length <= 64 && DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal, out var parsed) ? parsed.ToString("o", CultureInfo.InvariantCulture) : null;
    private static double? Percent(object? value)
    {
        if (!(value is double || value is decimal || value is float || value is int || value is long)) return null;
        var number = Convert.ToDouble(value, CultureInfo.InvariantCulture);
        return !double.IsNaN(number) && number >= 0 && number <= 100 ? number : (double?)null;
    }
    private static long Number(object? value)
    {
        try { return Math.Max(0, Convert.ToInt64(value, CultureInfo.InvariantCulture)); }
        catch (Exception error) when (error is FormatException || error is OverflowException || error is InvalidCastException) { return 0; }
    }
    internal object Achievements(Guid game)
    {
        var data = Read("cebe6d32-8c46-4459-b993-5a5189d60788", "SuccessStory", game);
        if (data == null) return new { gameId = game.ToString("D"), installed = false, provider = "success-story" };
        var source = Get(data, "Items") as object[] ?? Array.Empty<object>();
        if (source.Length > 4000) throw new ApiException(413, "achievement_limit");
        var rows = source.OfType<Dictionary<string, object>>().Select(item =>
        {
            var stamp = Date(Get(item, "DateUnlocked"));
            var unlocked = !string.IsNullOrEmpty(stamp) && !stamp!.StartsWith("0001", StringComparison.Ordinal);
            return new { name = WireJson.Text(Get(item, "Name")), description = WireJson.Text(Get(item, "Description"), 2048),
                unlocked, dateUnlocked = unlocked ? stamp : null, percent = Percent(Get(item, "Percent")), gamerScore = Number(Get(item, "GamerScore")),
                isHidden = Get(item, "IsHidden") as bool? ?? false };
        }).ToArray();
        return new { gameId = game.ToString("D"), installed = true, provider = "success-story", total = rows.Length,
            unlocked = rows.Count(row => row.unlocked), achievements = rows };
    }
    internal object Activity(Guid game)
    {
        var data = Read("afbb1a0d-04a1-4d0c-9afa-c6e42ca855b4", "GameActivity", game);
        if (data == null) return new { gameId = game.ToString("D"), installed = false };
        var source = Get(data, "Items") as object[] ?? Array.Empty<object>();
        if (source.Length > 10000) throw new ApiException(413, "activity_limit");
        var rows = source.OfType<Dictionary<string, object>>().Select(item => new { date = Date(Get(item, "DateSession")),
            elapsedSeconds = Number(Get(item, "ElapsedSeconds")), action = WireJson.Text(Get(item, "GameActionName"), 128) }).ToArray();
        return new { gameId = game.ToString("D"), installed = true, totalSeconds = Number(Get(data, "SessionPlaytime")), sessions = rows };
    }
}
