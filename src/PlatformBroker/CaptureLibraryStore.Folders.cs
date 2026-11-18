using WidgetRail.WidgetProtocol;

namespace WidgetRail.PlatformBroker;

public sealed partial class CaptureLibraryStore
{
    public async Task<CaptureLibraryFolders> FoldersAsync(CaptureLibraryFoldersRequest request, CancellationToken token)
    {
        DemandQuery(request.Query, request.Revision);
        await gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            using var ownership = Own();
            var revision = DemandRevision(request.Revision);
            LoadSources();
            var (where, parameters) = QueryWhere(request.Query with { SourceId = null });
            using var command = Command("SELECT coalesce(json_extract(data,'$.Item.SourceId'),'') AS source,count(*) FROM captures " +
                where + " GROUP BY source", parameters);
            using var reader = command.ExecuteReader();
            var counts = new Dictionary<string, int>(StringComparer.Ordinal);
            while (reader.Read()) { token.ThrowIfCancellationRequested(); counts.Add(reader.GetString(0), reader.GetInt32(1)); }
            reader.Close();
            var sources = sourceGrants.Values.Where(SourceEnabled).OrderBy(s => s.Label, StringComparer.OrdinalIgnoreCase).ThenBy(s => s.Id, StringComparer.Ordinal).ToArray();
            var labels = sources.ToDictionary(s => s.Id, s => FolderLabel(s, sources), StringComparer.Ordinal);
            var items = new List<CaptureLibraryFolder> { new("", "WidgetRail captures", counts.GetValueOrDefault("")) };
            foreach (var source in sources)
            {
                token.ThrowIfCancellationRequested();
                var label = labels[source.Id];
                // Even identical leaf/parent names remain distinct without disclosing an absolute provider path.
                if (labels.Values.Count(value => value.Equals(label, StringComparison.OrdinalIgnoreCase)) > 1)
                    label += " · " + source.Id[..8];
                items.Add(new(source.Id, label, counts.GetValueOrDefault(source.Id)));
            }
            return new(items.ToArray(), revision);
        }
        finally { gate.Release(); }
    }

    private static string FolderLabel(SourceGrant source, SourceGrant[] sources)
    {
        var label = source.Label;
        if (sources.Count(s => s.Label.Equals(label, StringComparison.OrdinalIgnoreCase)) < 2) return label;
        // Expose only a short display qualifier; source-management paths stay owner scoped.
        var leaf = Path.GetFileName(source.Directory);
        var parent = Path.GetFileName(Path.GetDirectoryName(source.Directory));
        var qualifier = string.IsNullOrEmpty(parent) ? leaf : parent + "/" + leaf;
        if (qualifier.Length > 96) qualifier = "…" + qualifier[^95..];
        return label + " · " + qualifier;
    }
}
