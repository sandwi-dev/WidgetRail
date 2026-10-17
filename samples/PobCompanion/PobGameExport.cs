using System.Text;
using System.Text.Json;

namespace WidgetRail.Samples.PobCompanion;

internal static class PobGameExport
{
    internal static string DefaultDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "My Games", "Path of Exile 2", "BuildPlanner");

    internal static string[] Save(string directory, JsonElement files, CancellationToken token)
    {
        if (!Path.IsPathFullyQualified(directory) || directory.StartsWith("\\\\", StringComparison.Ordinal) || directory.StartsWith("//", StringComparison.Ordinal))
            throw new IOException("Choose a folder on a local drive.");
        if (files.ValueKind != JsonValueKind.Array || files.GetArrayLength() is < 1 or > 128)
            throw new IOException("PoB returned no valid export files.");
        Directory.CreateDirectory(directory);
        var written = new List<string>();
        try
        {
            foreach (var file in files.EnumerateArray())
            {
                token.ThrowIfCancellationRequested();
                var name = file.GetProperty("name").GetString() ?? "Build";
                var text = file.GetProperty("text").GetString() ?? "";
                if (Encoding.UTF8.GetByteCount(text) > BuildDocument.MaximumBytes) throw new IOException("Export is too large.");
                using var json = JsonDocument.Parse(text);
                if (json.RootElement.ValueKind != JsonValueKind.Object || !json.RootElement.TryGetProperty("name", out _)) throw new IOException("Invalid game build export.");
                var stem = "PoB - " + new string(name.Select(c => Path.GetInvalidFileNameChars().Contains(c) || char.IsControl(c) ? '-' : c).ToArray());
                stem = stem[..Math.Min(stem.Length, 120)].TrimEnd(' ', '.');
                var temporary = Path.Combine(directory, Guid.NewGuid().ToString("N") + ".tmp");
                try
                {
                    File.WriteAllText(temporary, text, new UTF8Encoding(false));
                    for (var i = 0; ; i++)
                    {
                        if (i > 999) throw new IOException("Too many exports have the same name. Choose another name or folder.");
                        var destination = Path.Combine(directory, stem + (i == 0 ? "" : $" ({i + 1})") + ".build");
                        try { File.Move(temporary, destination, overwrite: false); written.Add(destination); break; }
                        catch (IOException) when (File.Exists(destination)) { }
                    }
                }
                finally { if (File.Exists(temporary)) File.Delete(temporary); }
            }
            return written.ToArray();
        }
        catch
        {
            // Only files created by this export are rolled back; existing guides are never replaced.
            foreach (var path in written) { try { File.Delete(path); } catch (IOException) { } catch (UnauthorizedAccessException) { } }
            throw;
        }
    }
}
