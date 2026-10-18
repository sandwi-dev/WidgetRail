using System.Collections.ObjectModel;
using System.Xml;

namespace WidgetRail.Samples.PobCompanion;

internal readonly record struct PobBuildMetadata(string Class, string Ascendancy);

internal sealed record PobLibrarySnapshot(
    (string Id, string Name)[] Entries,
    IReadOnlyDictionary<string, PobBuildMetadata> Metadata,
    long MetadataRevision)
{
    internal static readonly PobLibrarySnapshot Empty = new([], new ReadOnlyDictionary<string, PobBuildMetadata>(new Dictionary<string, PobBuildMetadata>()), 0);
}

/// <summary>Owns disk discovery and metadata caching; the widget publishes one complete snapshot.</summary>
internal sealed class PobBuildLibrary
{
    private readonly Dictionary<string, (DateTime Stamp, long Length, PobBuildMetadata Value)> metadata = new();
    private long revision;

    internal PobLibrarySnapshot Read(string directory)
    {
        if (!Directory.Exists(directory))
        {
            if (metadata.Count > 0) { metadata.Clear(); revision++; }
            return PobLibrarySnapshot.Empty with { MetadataRevision = revision };
        }

        var entries = Directory.EnumerateFiles(directory, "*.xml")
            .Concat(Directory.EnumerateFiles(directory, "*.work.json"))
            .Select(file => (File: new FileInfo(file), Id: Path.GetFileName(file).Split('.')[0]))
            .Where(entry => Guid.TryParseExact(entry.Id, "N", out _))
            .Where(entry => entry.File.Name == entry.Id + ".xml" || entry.File.Name == entry.Id + ".work.json")
            .GroupBy(entry => entry.Id).Select(group => group.OrderByDescending(entry => entry.File.LastWriteTimeUtc).First())
            .OrderByDescending(entry => entry.File.LastWriteTimeUtc)
            .Select(entry =>
            {
                var path = Path.Combine(directory, entry.Id + ".name.txt");
                var label = File.Exists(path) && new FileInfo(path).Length <= 1024 ? File.ReadAllText(path).Trim() : "";
                return (Id: entry.Id, Name: label.Length > 0 ? (label.Length <= 100 ? label : label[..100] + "…") : "Saved build · " + entry.File.LastWriteTime.ToString("g"));
            }).ToArray();

        var current = new Dictionary<string, PobBuildMetadata>();
        foreach (var entry in entries)
        {
            var file = new FileInfo(Path.Combine(directory, entry.Id + ".xml"));
            if (!file.Exists) continue; // Recovery-only entries are still discoverable.
            if (!metadata.TryGetValue(entry.Id, out var cached) || cached.Stamp != file.LastWriteTimeUtc || cached.Length != file.Length)
            {
                var value = new PobBuildMetadata("", "");
                try
                {
                    using var reader = XmlReader.Create(file.FullName, new() { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = BuildDocument.MaximumBytes });
                    reader.MoveToContent();
                    if (reader.ReadToDescendant("Build")) value = new(reader.GetAttribute("className") ?? "", reader.GetAttribute("ascendClassName") ?? "");
                }
                catch (Exception error) when (error is IOException or XmlException or UnauthorizedAccessException) { }
                cached = (file.LastWriteTimeUtc, file.Length, value);
                metadata[entry.Id] = cached;
                revision++;
            }
            current[entry.Id] = cached.Value;
        }
        foreach (var removed in metadata.Keys.Where(id => !current.ContainsKey(id)).ToArray())
        {
            metadata.Remove(removed); revision++;
        }
        return new(entries, new ReadOnlyDictionary<string, PobBuildMetadata>(current), revision);
    }
}
