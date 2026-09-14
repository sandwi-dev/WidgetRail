using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace WidgetRail.WrailCli;

internal sealed record PackageSourceRecord(string Id, string Version, string Publisher, string Source, string Sha256, string ContentDigest);
internal sealed record PackageSourceDocument(int SchemaVersion, PackageSourceRecord[] Entries);

internal static class PackageSources
{
    private static string DirectoryPath(string root) => Path.Combine(Path.GetFullPath(root), ".wrail-sources");
    private static string RecordPath(string root, string kind, string id) => Path.Combine(DirectoryPath(root),
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(kind + ":" + id))).ToLowerInvariant() + ".json");

    internal static PackageSourceRecord? Find(string root, string kind, string id, string version, string digest)
    {
        var entry = Read(RecordPath(root, kind, id)).SingleOrDefault(entry => entry.Version == version && entry.Id == id);
        if (entry is null || entry.ContentDigest != digest) return null;
        _ = GitHubPackageSource.Parse(entry.Source, kind == "widget" ? ".wrwidget" : ".wrtheme");
        _ = PackageIntegrity.ParseExpectedSha256(entry.Sha256);
        return entry;
    }

    internal static async Task SaveAsync(string root, string kind, PackageSourceRecord record, TextWriter output, CancellationToken token)
    {
        try
        {
            var directory = DirectoryPath(root);
            CheckPath(directory);
            Directory.CreateDirectory(directory);
            CheckPath(directory);
            var path = RecordPath(root, kind, record.Id);
            var lockPath = Path.Combine(directory, ".lock");
            CheckPath(lockPath);
            await using var gate = await AcquireAsync(lockPath, token);
            if (!File.Exists(path) && Directory.EnumerateFiles(directory, "*.json").Take(513).Count() >= 512)
                throw new IOException("Package source record limit reached.");
            var entries = Read(path).Where(entry => entry.Version != record.Version).Append(record).ToArray();
            if (entries.Length > 64) throw new IOException("Package source version limit reached.");
            await PublishAsync(path, entries, token);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException or CliOperationException)
        {
            await output.WriteLineAsync("warning: Package installed, but its GitHub source could not be saved. Use --repo when checking for an update.");
        }
    }

    internal static async Task RemoveAsync(string root, string kind, string id, string? version, TextWriter output, CancellationToken token)
    {
        try
        {
            var path = RecordPath(root, kind, id);
            CheckPath(path);
            if (!File.Exists(path)) return;
            var lockPath = Path.Combine(DirectoryPath(root), ".lock");
            CheckPath(lockPath);
            await using var gate = await AcquireAsync(lockPath, token);
            var entries = version is null ? [] : Read(path).Where(entry => entry.Version != version).ToArray();
            if (entries.Length == 0) File.Delete(path);
            else await PublishAsync(path, entries, token);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or CliOperationException)
        { await output.WriteLineAsync("warning: Package removal succeeded, but cached GitHub source metadata could not be removed."); }
    }

    private static async Task PublishAsync(string path, PackageSourceRecord[] entries, CancellationToken token)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(new PackageSourceDocument(1, entries), DiagnosticJson.Options);
        if (bytes.Length > 64 * 1024) throw new CliOperationException("Package source metadata exceeds its size limit.");
        var temporary = Path.Combine(Path.GetDirectoryName(path)!, Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            await using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                await stream.WriteAsync(bytes, token);
            CheckPath(path);
            File.Move(temporary, path, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private static PackageSourceRecord[] Read(string path)
    {
        CheckPath(path);
        if (!File.Exists(path)) return [];
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
        if (stream.Length > 64 * 1024) throw new CliOperationException("Package source metadata is too large. Use --repo to choose a source explicitly.");
        try
        {
            var document = JsonSerializer.Deserialize<PackageSourceDocument>(stream, DiagnosticJson.Options)
                ?? throw new JsonException();
            if (document.SchemaVersion != 1 || document.Entries is null) throw new JsonException();
            var entries = document.Entries;
            if (entries.Length > 64 || entries.Any(entry => entry is null || string.IsNullOrEmpty(entry.Id) ||
                    string.IsNullOrEmpty(entry.Version) || string.IsNullOrEmpty(entry.Publisher) ||
                    string.IsNullOrEmpty(entry.Source) || entry.Source.Length > 512 || entry.Sha256 is not { Length: 64 } ||
                    entry.ContentDigest is not { Length: 64 }) ||
                entries.Select(entry => entry.Version).Distinct(StringComparer.Ordinal).Count() != entries.Length)
                throw new JsonException();
            return entries;
        }
        catch (JsonException) { throw new CliOperationException("Package source metadata is invalid. Use --repo to choose a source explicitly."); }
    }

    private static async Task<FileStream> AcquireAsync(string path, CancellationToken token)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(2);
        while (true)
        {
            token.ThrowIfCancellationRequested();
            try { return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
            catch (IOException) when (DateTime.UtcNow < deadline) { await Task.Delay(40, token); }
        }
    }

    private static void CheckPath(string path)
    {
        for (var current = Path.GetFullPath(path); current is not null; current = Path.GetDirectoryName(current))
            if ((File.Exists(current) || Directory.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new CliOperationException("Package source metadata cannot use reparse points.");
    }

    internal static string ThemeDigest(ThemePackageInspection inspection)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var file in inspection.Files.OrderBy(file => file.RelativePath, StringComparer.Ordinal))
        {
            hash.AppendData(Encoding.UTF8.GetBytes(file.RelativePath + "\0"));
            hash.AppendData(BitConverter.GetBytes(file.Content.Length));
            hash.AppendData(file.Content);
        }
        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }
}
