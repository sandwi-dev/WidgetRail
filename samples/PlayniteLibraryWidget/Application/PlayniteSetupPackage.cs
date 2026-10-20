using System.IO.Compression;
using System.Security.Cryptography;

namespace WidgetRail.Samples.PlayniteLibrary;

internal sealed class PlayniteSetupPackage(string directory)
{
    internal const string Version = "1.5.3.1";
    internal string PackagePath => Path.Combine(directory, "PlayniteBridge.WidgetRail.pext");
    internal string DllDigest => Metadata()["dllSha256"]!.GetValue<string>();
    internal string OriginalDigest => Metadata()["knownOriginalDllSha256"]!.GetValue<string>();
    private System.Text.Json.Nodes.JsonObject Metadata()
    {
        var metadata = PlayniteSetupFiles.ReadObject(Path.Combine(directory, "bridge-package.json"), 16 * 1024);
        if (metadata["version"]?.GetValue<string>() != Version ||
            new[] { "sha256", "dllSha256", "knownOriginalDllSha256" }.Any(key => metadata[key]?.GetValue<string>() is not { Length: 64 } digest || !digest.All(char.IsAsciiHexDigit)))
            throw new PlayniteSetupException("The bundled integration could not be verified. Reinstall the Playnite widget from the catalogue.");
        return metadata;
    }
    internal void Validate()
    {
        var metadata = Metadata();
        if (!string.Equals(PlayniteSetupFiles.Digest(PackagePath), metadata["sha256"]!.GetValue<string>(), StringComparison.OrdinalIgnoreCase))
            throw new PlayniteSetupException("The bundled integration failed its integrity check. Reinstall the Playnite widget from the catalogue.");
    }
    internal void Extract(string target)
    {
        Validate();
        PlayniteSetupFiles.NoLinks(target);
        Directory.CreateDirectory(target);
        var expected = new HashSet<string>(["PlayniteBridge.dll", "extension.yaml", "LICENSE", "WidgetRail.Build.json"], StringComparer.Ordinal);
        using var zip = ZipFile.OpenRead(PackagePath);
        if (zip.Entries.Count != expected.Count) throw new PlayniteSetupException("The bundled integration has unexpected files.");
        foreach (var entry in zip.Entries)
        {
            if (!expected.Remove(entry.FullName) || entry.Length > 4 * 1024 * 1024 || entry.Length < 1)
                throw new PlayniteSetupException("The bundled integration contains an invalid file.");
            using var source = entry.Open();
            using var destination = new FileStream(Path.Combine(target, entry.FullName), FileMode.Create, FileAccess.Write, FileShare.None);
            var buffer = new byte[8192];
            long count = 0;
            int read;
            while ((read = source.Read(buffer)) != 0)
            {
                count += read;
                if (count > entry.Length) throw new PlayniteSetupException("The bundled integration is damaged.");
                destination.Write(buffer, 0, read);
            }
            if (count != entry.Length) throw new PlayniteSetupException("The bundled integration is incomplete.");
        }
        if (!string.Equals(PlayniteSetupFiles.Digest(Path.Combine(target, "PlayniteBridge.dll")), DllDigest, StringComparison.OrdinalIgnoreCase))
            throw new PlayniteSetupException("The integration DLL failed its integrity check.");
        var manifest = PlayniteSetupFiles.ReadManifest(Path.Combine(target, "extension.yaml"));
        if (manifest.GetValueOrDefault("Id") != PlayniteSetupFiles.ExtensionId || manifest.GetValueOrDefault("Version") != Version ||
            manifest.GetValueOrDefault("Module") != "PlayniteBridge.dll")
            throw new PlayniteSetupException("The bundled integration manifest is incompatible.");
    }
}
