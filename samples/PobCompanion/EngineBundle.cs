using System.IO.Compression;
using System.Security.Cryptography;

namespace WidgetRail.Samples.PobCompanion;

internal static class EngineBundle
{
    public static async Task<string> ResolveAsync(string location, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (Directory.Exists(location)) return location; // Explicit developer checkout/probe.
        await using var archiveStream = EngineArchiveStream.Open(location);
        var digest = Convert.ToHexString(await SHA256.HashDataAsync(archiveStream, token));
        archiveStream.Position = 0;
        var cache = Path.GetFullPath(Environment.GetEnvironmentVariable("WIDGETRAIL_POB_CACHE") ??
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WidgetRail", "pob-engines"));
        RejectLinks(cache);
        var destination = Path.Combine(cache, digest);
        if (Complete(destination, digest)) return destination;
        Directory.CreateDirectory(cache);
        var staging = Path.Combine(cache, digest + "." + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(staging);
        try
        {
            using var zip = new ZipArchive(archiveStream, ZipArchiveMode.Read, leaveOpen: true);
            if (zip.Entries.Count > 10000 || zip.Entries.Sum(e => e.Length) > 768L * 1024 * 1024)
                throw new IOException("The engine bundle exceeds its extraction bounds.");
            var prefix = staging + Path.DirectorySeparatorChar;
            foreach (var entry in zip.Entries)
            {
                token.ThrowIfCancellationRequested();
                var target = Path.GetFullPath(Path.Combine(staging, entry.FullName));
                if (!target.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) || entry.FullName.Contains(':') ||
                    (entry.ExternalAttributes >> 16 & 0xF000) == 0xA000)
                    throw new IOException("The engine bundle contains an invalid path.");
                RejectLinks(target);
                if (entry.FullName.EndsWith('/')) { Directory.CreateDirectory(target); continue; }
                if (entry.Length > 128L * 1024 * 1024) throw new IOException("An engine file exceeds its extraction bound.");
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                await using var input = entry.Open();
                await using var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true);
                await input.CopyToAsync(output, token);
            }
            if (!File.Exists(Path.Combine(staging, "src", "HeadlessWrapper.lua"))) throw new IOException("The engine bundle is incomplete.");
            await File.WriteAllTextAsync(Path.Combine(staging, ".complete"), digest, token);
            token.ThrowIfCancellationRequested();
            try { Directory.Move(staging, destination); }
            catch (IOException) when (Complete(destination, digest)) { /* Another instance published the same immutable bundle. */ }
            return destination;
        }
        finally
        {
            // Only our newly created, canonical staging directory is eligible.
            if (Directory.Exists(staging) && Path.GetDirectoryName(staging) == cache)
            {
                RejectLinks(staging);
                Directory.Delete(staging, recursive: true);
            }
        }
    }
    private static bool Complete(string path, string digest)
    {
        RejectLinks(path);
        var marker = Path.Combine(path, ".complete");
        if (!File.Exists(marker)) return false;
        RejectLinks(marker);
        return new FileInfo(marker).Length == digest.Length && File.ReadAllText(marker) == digest && File.Exists(Path.Combine(path, "src", "HeadlessWrapper.lua"));
    }
    private static void RejectLinks(string path)
    {
        for (var current = Path.GetFullPath(path); current is not null; current = Path.GetDirectoryName(current))
            if ((File.Exists(current) || Directory.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("The engine cache cannot use redirected folders.");
    }
}
