using System.IO.Compression;
using System.Text.Json;

namespace WidgetRail.OverlayFrontend.WinUI.Browser.Extensions;

/// <summary>Bounded, off-UI-thread import. No extracted path or manifest URL gains host authority.</summary>
internal static class ExtensionPackage
{
    private const long MaxBytes = 256L * 1024 * 1024;
    private const int MaxFiles = 25000;

    internal static ExtensionManifest Copy(string source, string destination, CancellationToken token)
    {
        Directory.CreateDirectory(destination);
        long bytes = 0; var count = 0;
        if (Directory.Exists(source))
        {
            var sourceRoot = Path.GetFullPath(source).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (Path.GetFullPath(destination).StartsWith(sourceRoot, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Choose the extension folder, not a parent of WidgetRail's managed storage.");
            RejectLink(source);
            CopyDirectory(Path.GetFullPath(source), "");
        }
        else
        {
            if (new FileInfo(source).Length > MaxBytes) throw new InvalidDataException("The extension archive exceeds 256 MB.");
            using var zip = ZipFile.OpenRead(source);
            if (zip.Entries.Count > MaxFiles) throw new InvalidDataException("The extension archive has too many entries.");
            foreach (var entry in zip.Entries)
            {
                token.ThrowIfCancellationRequested();
                // Unix symlinks and Windows reparse entries are not extension resources.
                if (((entry.ExternalAttributes >> 16) & 0xF000) == 0xA000 || (entry.ExternalAttributes & 0x400) != 0)
                    throw new InvalidDataException("Extension archives cannot contain links.");
                var path = ResourcePath(entry.FullName.TrimEnd('/'), destination);
                if (entry.FullName.EndsWith('/')) { Account(0); Directory.CreateDirectory(path); continue; }
                Account(entry.Length);
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                using var input = entry.Open();
                using var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write);
                CopyBounded(input, output, entry.Length, token);
            }
        }
        // Installable packages have manifest.json at their root. Source archives
        // and ambiguous nested packages must not be guessed into executable code.
        return ReadManifest(destination);

        void Account(long length)
        {
            if (++count > MaxFiles || length < 0 || (bytes += length) > MaxBytes)
                throw new InvalidDataException("The extension exceeds the import limit (256 MB or 25,000 files).");
        }
        void CopyDirectory(string directory, string relative)
        {
            foreach (var path in Directory.EnumerateFileSystemEntries(directory))
            {
                token.ThrowIfCancellationRequested(); RejectLink(path);
                var child = Path.Combine(relative, Path.GetFileName(path));
                var target = ResourcePath(child.Replace('\\', '/'), destination);
                if (Directory.Exists(path)) { Account(0); Directory.CreateDirectory(target); CopyDirectory(path, child); }
                else
                {
                    var length = new FileInfo(path).Length; Account(length);
                    using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
                    using var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write);
                    CopyBounded(input, output, length, token);
                }
            }
        }
    }

    private static void CopyBounded(Stream input, Stream output, long expected, CancellationToken token)
    {
        var buffer = new byte[81920]; long copied = 0;
        for (var read = input.Read(buffer); read > 0; read = input.Read(buffer))
        {
            token.ThrowIfCancellationRequested(); copied += read;
            if (copied > expected || copied > MaxBytes) throw new InvalidDataException("Extension resource changed during import.");
            output.Write(buffer, 0, read);
        }
        if (copied != expected) throw new InvalidDataException("Extension resource is incomplete.");
    }

    internal static string ResourcePath(string relative, string root)
    {
        var segments = relative.Split('/');
        if (relative.Length > 1000 || segments.Any(part => part.Length == 0 || part is "." or ".." ||
            part.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || part.EndsWith('.') || part.EndsWith(' ') ||
            IsDeviceName(part))) throw new InvalidDataException("The extension contains an unsafe resource path.");
        return Path.Combine(root, Path.Combine(segments));
    }
    private static bool IsDeviceName(string part)
    {
        var stem = part.Split('.')[0];
        return stem.Equals("CON", StringComparison.OrdinalIgnoreCase) || stem.Equals("PRN", StringComparison.OrdinalIgnoreCase) ||
            stem.Equals("AUX", StringComparison.OrdinalIgnoreCase) || stem.Equals("NUL", StringComparison.OrdinalIgnoreCase) ||
            stem.Length == 4 && (stem.StartsWith("COM", StringComparison.OrdinalIgnoreCase) || stem.StartsWith("LPT", StringComparison.OrdinalIgnoreCase)) && stem[3] is >= '1' and <= '9';
    }
    private static void RejectLink(string path)
    { if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("Extension folders cannot contain links."); }

    internal static ExtensionManifest ReadManifest(string folder)
    {
        var path = Path.Combine(folder, "manifest.json");
        if (!File.Exists(path) || new FileInfo(path).Length > 1024 * 1024)
            throw new InvalidDataException("Choose an unpacked extension folder or installable ZIP with manifest.json at its root.");
        using var json = JsonDocument.Parse(File.ReadAllText(path));
        var root = json.RootElement;
        var name = root.GetProperty("name").GetString(); var version = root.GetProperty("version").GetString();
        var manifestVersion = root.GetProperty("manifest_version").GetInt32();
        if (string.IsNullOrWhiteSpace(name) || name.Length > 256 || string.IsNullOrWhiteSpace(version) ||
            version.Length > 64 || manifestVersion is not (2 or 3)) throw new InvalidDataException("The extension manifest is invalid.");
        if (name.StartsWith("__MSG_", StringComparison.Ordinal) && name.EndsWith("__", StringComparison.Ordinal) && root.TryGetProperty("default_locale", out var locale))
        {
            var messages = ResourcePath("_locales/" + locale.GetString() + "/messages.json", folder);
            if (File.Exists(messages) && new FileInfo(messages).Length <= 1024 * 1024)
            {
                using var localized = JsonDocument.Parse(File.ReadAllText(messages));
                if (localized.RootElement.TryGetProperty(name[6..^2], out var value) && value.TryGetProperty("message", out var message))
                    name = message.GetString() ?? name;
            }
        }
        string? options = null;
        if (root.TryGetProperty("options_ui", out var ui) && ui.TryGetProperty("page", out var page)) options = page.GetString();
        else if (root.TryGetProperty("options_page", out var old)) options = old.GetString();
        if (options is not null) _ = ResourcePath(options.TrimStart('/'), folder);
        var permissions = Strings(root, "permissions");
        var sites = Strings(root, "host_permissions").Concat(permissions.Where(p => p.Contains("://", StringComparison.Ordinal) || p == "<all_urls>"))
            .Concat(root.TryGetProperty("content_scripts", out var scripts) ? scripts.EnumerateArray().SelectMany(s => Strings(s, "matches")) : []).Distinct().ToArray();
        return new(name[..Math.Min(256, name.Length)], version, manifestVersion,
            root.TryGetProperty("key", out var key) ? key.GetString() : null, options?.TrimStart('/'),
            permissions.Where(p => !sites.Contains(p)).ToArray(), sites);
    }
    private static string[] Strings(JsonElement root, string name) => root.TryGetProperty(name, out var values)
        ? values.EnumerateArray().Select(v => v.GetString() ?? "").Where(v => v.Length > 0).ToArray() : [];
}
