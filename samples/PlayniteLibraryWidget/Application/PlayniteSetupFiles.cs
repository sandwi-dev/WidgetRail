using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace WidgetRail.Samples.PlayniteLibrary;

internal static class PlayniteSetupFiles
{
    internal const string ExtensionId = "PlayniteBridge_f47ac10b";
    internal const string PluginId = "f47ac10b-58cc-4372-a567-0e02b2c3d479";
    internal static string LocalPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path) || path.StartsWith(@"\\", StringComparison.Ordinal) ||
            path.Any(char.IsControl) || path.Length > 1024 || path.IndexOf(':', 2) >= 0)
            throw new PlayniteSetupException("Choose a folder on a local drive using its full path.");
        return Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
    }
    internal static void NoLinks(string path)
    {
        var current = Path.GetFullPath(path);
        while (!string.IsNullOrEmpty(current))
        {
            if ((File.Exists(current) || Directory.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new PlayniteSetupException("Automatic setup cannot modify linked folders. Choose the actual Playnite folder or use manual setup.");
            current = Path.GetDirectoryName(current);
        }
    }
    internal static bool Within(string root, string child) => Path.GetFullPath(child).StartsWith(
        Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    internal static byte[] Read(string path, int maximumBytes)
    {
        NoLinks(path);
        using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        if (input.Length > maximumBytes) throw new PlayniteSetupException("A Playnite settings file is too large for automatic setup. Use manual setup.");
        using var output = new MemoryStream();
        var buffer = new byte[8192];
        int read;
        while ((read = input.Read(buffer)) != 0)
        {
            if (output.Length + read > maximumBytes) throw new PlayniteSetupException("A Playnite settings file changed during setup. Retry.");
            output.Write(buffer, 0, read);
        }
        return output.ToArray();
    }
    internal static string Digest(string path) => Convert.ToHexString(SHA256.HashData(Read(path, 32 * 1024 * 1024)));
    internal static JsonObject ReadObject(string path, int maximumBytes = 1024 * 1024) =>
        JsonNode.Parse(Read(path, maximumBytes), documentOptions: new() { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip }) as JsonObject
        ?? throw new PlayniteSetupException("A Playnite settings file could not be read. Use manual setup or restore it first.");
    internal static void Write(string path, byte[] bytes)
    {
        NoLinks(path);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { File.WriteAllBytes(temporary, bytes); File.Move(temporary, path, true); }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
    internal static void WriteObject(string path, JsonObject value) => Write(path, Encoding.UTF8.GetBytes(value.ToJsonString(new() { WriteIndented = true })));
    internal static string PluginData(string dataDirectory) => Path.Combine(dataDirectory, "ExtensionsData", PluginId);
    internal static Dictionary<string, string> ReadManifest(string path)
    {
        var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in Encoding.UTF8.GetString(Read(path, 64 * 1024)).Split('\n'))
        {
            var separator = line.IndexOf(':');
            if (separator <= 0 || char.IsWhiteSpace(line[0])) continue;
            fields[line[..separator].Trim()] = line[(separator + 1)..].Trim().Trim('"', '\'');
        }
        return fields;
    }
}
