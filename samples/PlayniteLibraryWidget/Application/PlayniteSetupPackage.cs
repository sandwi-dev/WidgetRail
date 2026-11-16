using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using WidgetRail.Playnite;

namespace WidgetRail.Samples.PlayniteLibrary;

internal sealed class PlayniteSetupPackage(string directory)
{
    internal const string Version = IntegrationIdentity.Version;
    internal static readonly string[] Files = [IntegrationIdentity.ModuleName, "WidgetRail.Playnite.Core.dll", "WidgetRail.Playnite.Contracts.dll", "extension.yaml", "LICENSE", "WidgetRail.Build.json"];
    internal string PackagePath => Path.Combine(directory, "WidgetRail.Playnite.pext");
    internal string DllDigest => Metadata()["dllSha256"]!.GetValue<string>();
    internal string PayloadDigest
    {
        get { var metadata = Metadata(); return Aggregate(Files.Select(name => (name, metadata["files"]![name]!.GetValue<string>()))); }
    }
    private JsonObject Metadata()
    {
        var metadata = PlayniteSetupFiles.ReadObject(Path.Combine(directory, "integration-package.json"), 16 * 1024);
        static bool Hash(JsonNode? node) => node?.GetValue<string>() is { Length: 64 } digest && digest.All(char.IsAsciiHexDigit);
        if (metadata["version"]?.GetValue<string>() != Version || metadata["pluginId"]?.GetValue<string>() != IntegrationIdentity.PluginId ||
            metadata["extensionId"]?.GetValue<string>() != IntegrationIdentity.ExtensionId || !Hash(metadata["sha256"]) || !Hash(metadata["dllSha256"]) ||
            metadata["files"] is not JsonObject files || files.Count != Files.Length || Files.Any(name => !Hash(files[name])))
            throw new PlayniteSetupException("The bundled WidgetRail integration could not be verified. Reinstall the Playnite widget.");
        return metadata;
    }
    internal void Validate()
    {
        var metadata = Metadata();
        if (!string.Equals(PlayniteSetupFiles.Digest(PackagePath), metadata["sha256"]!.GetValue<string>(), StringComparison.OrdinalIgnoreCase))
            throw new PlayniteSetupException("The bundled integration failed its integrity check. Reinstall the Playnite widget.");
    }
    internal static string? InstalledDigest(string target)
    {
        if (!File.Exists(Path.Combine(target, IntegrationIdentity.ModuleName))) return null;
        return Aggregate(Files.Select(name => (name, File.Exists(Path.Combine(target, name)) ? PlayniteSetupFiles.Digest(Path.Combine(target, name)) : "missing")));
    }
    private static string Aggregate(IEnumerable<(string Name, string Digest)> files) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("\n", files.OrderBy(item => item.Name, StringComparer.Ordinal)
            .Select(item => item.Name + "=" + item.Digest.ToUpperInvariant())))));
    internal static bool OwnedBuild(string target)
    {
        var path = Path.Combine(target, "WidgetRail.Build.json");
        if (!File.Exists(path)) return false;
        try
        {
            var record = PlayniteSetupFiles.ReadObject(path, 16384);
            return record["pluginId"]?.GetValue<string>() == IntegrationIdentity.PluginId &&
                record["extensionId"]?.GetValue<string>() == IntegrationIdentity.ExtensionId &&
                Files.Where(name => name.EndsWith(".dll", StringComparison.Ordinal)).All(name => File.Exists(Path.Combine(target, name)) &&
                    string.Equals(record["files"]?[name]?.GetValue<string>(), PlayniteSetupFiles.Digest(Path.Combine(target, name)), StringComparison.OrdinalIgnoreCase));
        }
        catch (Exception error) when (error is IOException or System.Text.Json.JsonException or InvalidOperationException or PlayniteSetupException) { return false; }
    }
    internal void Extract(string target)
    {
        Validate(); var metadata = Metadata();
        PlayniteSetupFiles.NoLinks(target); Directory.CreateDirectory(target);
        var expected = new HashSet<string>(Files, StringComparer.Ordinal);
        using var zip = ZipFile.OpenRead(PackagePath);
        if (zip.Entries.Count != expected.Count) throw new PlayniteSetupException("The bundled integration has unexpected files.");
        foreach (var entry in zip.Entries)
        {
            if (!expected.Remove(entry.FullName) || entry.Length is < 1 or > 4 * 1024 * 1024)
                throw new PlayniteSetupException("The bundled integration contains an invalid file.");
            using (var source = entry.Open())
            using (var destination = new FileStream(Path.Combine(target, entry.FullName), FileMode.Create, FileAccess.Write, FileShare.None))
            {
                var buffer = new byte[8192]; long count = 0; int read;
                while ((read = source.Read(buffer)) != 0)
                { count += read; if (count > entry.Length) throw new PlayniteSetupException("The bundled integration is damaged."); destination.Write(buffer, 0, read); }
                if (count != entry.Length) throw new PlayniteSetupException("The bundled integration is incomplete.");
            }
            if (!string.Equals(PlayniteSetupFiles.Digest(Path.Combine(target, entry.FullName)), metadata["files"]![entry.FullName]!.GetValue<string>(), StringComparison.OrdinalIgnoreCase))
                throw new PlayniteSetupException("An integration file failed its integrity check.");
        }
        var manifest = PlayniteSetupFiles.ReadManifest(Path.Combine(target, "extension.yaml"));
        if (manifest.GetValueOrDefault("Id") != IntegrationIdentity.ExtensionId || manifest.GetValueOrDefault("Version") != Version ||
            manifest.GetValueOrDefault("Module") != IntegrationIdentity.ModuleName)
            throw new PlayniteSetupException("The bundled integration manifest is incompatible.");
    }
}