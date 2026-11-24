using WidgetRail.WidgetProtocol;

namespace WidgetRail.PlatformBroker;

public sealed partial class CaptureLibraryStore
{
    private long changeGeneration;
    private int changeQueued, disposed;
    private readonly object changeGate = new();
    private Timer? changeTimer;
    internal event Action<CaptureLibraryChanged>? Changed;

    private void NotifyChanged()
    {
        if (Volatile.Read(ref disposed) != 0 || Interlocked.Exchange(ref changeQueued, 1) != 0) return;
        lock (changeGate)
        {
            if (disposed != 0) return;
            changeTimer ??= new Timer(_ =>
            {
                Interlocked.Exchange(ref changeQueued, 0);
                if (Volatile.Read(ref disposed) == 0)
                    Changed?.Invoke(new(Interlocked.Increment(ref changeGeneration)));
            });
            changeTimer.Change(100, Timeout.Infinite);
        }
    }

    public async Task<CaptureLibraryQueryPage> QueryAsync(CaptureLibraryQueryRequest request, CancellationToken token)
    {
        DemandQuery(request.Query, request.Revision);
        if (request.Offset < 0 || request.Count is < 1 or > CaptureLibraryLimits.MaximumQueryItems) throw Error("invalid_payload");
        await gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            using var ownership = Own();
            return QueryPage(request, token);
        }
        finally { gate.Release(); }
    }

    public async Task<CaptureLibraryContinuityPage> QueryContinuityAsync(CaptureLibraryQueryRequest request, CancellationToken token)
    {
        DemandQuery(request.Query, request.Revision);
        if (request.Offset < 0 || request.Count is < 1 or > CaptureLibraryLimits.MaximumQueryItems) throw Error("invalid_payload");
        await gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            using var ownership = Own();
            var page = QueryPage(request, token);
            if (request.Query.FavoritesOnly) return new(page, page.Revision);
            // Origin/reference filters and text searches can gain or lose rows when a link changes.
            // Other gallery filters and all current sort orders depend only on the media rows.
            var includesLinks = request.Query.OriginPackageId is not null || !string.IsNullOrWhiteSpace(request.Query.Search);
            using var command = Command(includesLinks
                ? "SELECT epoch || '-' || printf('%x',o.sequence) FROM library_revision JOIN library_ordering o USING(id) WHERE id=1"
                : "SELECT epoch || '-c-' || printf('%x',o.sequence-a.sequence) FROM library_revision JOIN library_ordering o USING(id) JOIN association_ordering a USING(id) WHERE id=1");
            return new(page, (string)command.ExecuteScalar()!);
        }
        finally { gate.Release(); }
    }

    private CaptureLibraryQueryPage QueryPage(CaptureLibraryQueryRequest request, CancellationToken token)
    {
            var revision = DemandRevision(request.Revision);
            var (where, parameters) = QueryWhere(request.Query);
            using var totals = Command("SELECT count(*),coalesce(sum(bytes),0) FROM captures " + where, parameters);
            using var reader = totals.ExecuteReader(); reader.Read();
            var count = reader.GetInt32(0); var bytes = reader.GetInt64(1); reader.Close();
            var items = IndexRows(where + " ORDER BY " + QueryOrder(request.Query.Sort) + " LIMIT $limit OFFSET $offset",
                [..parameters, ("$limit", request.Count), ("$offset", request.Offset)])
                .Select(ItemView).ToArray();
            token.ThrowIfCancellationRequested();
            return new(items, request.Offset, count, bytes, revision);
    }

    public async Task<CaptureLibraryApplications> ApplicationsAsync(CaptureLibraryApplicationsRequest request, CancellationToken token)
    {
        DemandQuery(request.Query, null);
        if (request.Limit is < 1 or > 128 || !CaptureLibraryLimits.ValidText(request.Search, 120)) throw Error("invalid_payload");
        await gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            using var ownership = Own();
            // Exclude the selected application from its own facet so users can
            // switch directly to another match without first clearing the filter.
            var (where, parameters) = QueryWhere(request.Query with { Application = null });
            if (!string.IsNullOrWhiteSpace(request.Search))
            {
                where += (where.Length == 0 ? "WHERE " : " AND ") +
                    "instr(lower(coalesce(json_extract(data,'$.Item.Media.SourceApplication.ApplicationName'),'')),lower($applicationSearch))>0";
                parameters = [..parameters, ("$applicationSearch", request.Search.Trim())];
            }
            using var command = Command("SELECT coalesce(json_extract(data,'$.Item.Media.SourceApplication.ApplicationName'),'') AS application,count(*) FROM captures " + where +
                " GROUP BY application COLLATE NOCASE ORDER BY application='' ASC,application COLLATE NOCASE LIMIT $limit", [..parameters, ("$limit", request.Limit + 1)]);
            using var reader = command.ExecuteReader(); var items = new List<CaptureLibraryApplication>();
            while (reader.Read()) { token.ThrowIfCancellationRequested(); items.Add(new(reader.GetString(0), reader.GetInt32(1))); }
            return new(items.Take(request.Limit).ToArray(), items.Count > request.Limit);
        }
        finally { gate.Release(); }
    }

    public async Task<CaptureLibraryGroups> GroupsAsync(CaptureLibraryGroupsRequest request, CancellationToken token)
    {
        DemandQuery(request.Query, request.Revision);
        if (request.Query.Sort is not (CaptureLibrarySort.Newest or CaptureLibrarySort.Oldest)) throw Error("invalid_payload");
        await gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            using var ownership = Own(); var revision = DemandRevision(request.Revision);
            var (where, parameters) = QueryWhere(request.Query);
            var order = request.Query.Sort == CaptureLibrarySort.Newest ? "DESC" : "ASC";
            using var count = Command("SELECT count(*) FROM captures " + where, parameters);
            var total = checked((int)(long)count.ExecuteScalar()!);
            using var command = Command("SELECT coalesce(strftime('%Y-%m',saved_at/1000,'unixepoch','localtime'),'unknown') AS month,count(*) FROM captures " + where +
                " GROUP BY month ORDER BY min(saved_at) " + order + " LIMIT 256", parameters);
            using var reader = command.ExecuteReader(); var groups = new List<CaptureLibraryGroup>();
            while (reader.Read()) { token.ThrowIfCancellationRequested(); groups.Add(new(reader.GetString(0), reader.GetInt32(1))); }
            if (groups.Count == 256)
            {
                var remaining = total - groups.Take(255).Sum(group => group.Count);
                groups[255] = new(request.Query.Sort == CaptureLibrarySort.Newest ? "older" : "newer", remaining);
            }
            return new(groups.ToArray(), total, revision);
        }
        finally { gate.Release(); }
    }

    public async Task<CaptureLibraryPosition> FindAsync(CaptureLibraryFindRequest request, CancellationToken token)
    {
        DemandQuery(request.Query, request.Revision); DemandId(request.Id);
        await gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            using var ownership = Own();
            var revision = DemandRevision(request.Revision);
            var (where, parameters) = QueryWhere(request.Query);
            using var command = Command("WITH ordered AS (SELECT id,row_number() OVER (ORDER BY " + QueryOrder(request.Query.Sort) +
                ")-1 AS position FROM captures " + where + ") SELECT (SELECT position FROM ordered WHERE id=$id),(SELECT count(*) FROM ordered)",
                [..parameters, ("$id", request.Id)]);
            using var reader = command.ExecuteReader(); reader.Read();
            token.ThrowIfCancellationRequested();
            return new(reader.IsDBNull(0) ? null : reader.GetInt32(0), reader.GetInt32(1), revision);
        }
        finally { gate.Release(); }
    }

    public async Task<CaptureLibraryItem> GetMetadataAsync(string id, CancellationToken token)
    {
        DemandId(id); await gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            using var ownership = Own(); RefreshEntry(id, forceMetadata: true);
            var saved = Read(id); token.ThrowIfCancellationRequested();
            return ItemView(saved);
        }
        finally { gate.Release(); }
    }

    public async Task<CaptureLibraryItem> UpdateMetadataAsync(CaptureLibraryMetadataRequest request, CancellationToken token)
    {
        DemandId(request.Id);
        if (request.ExpectedRevision < 0 || !CaptureLibraryLimits.ValidText(request.Title, 160)) throw Error("invalid_payload");
        await gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            using var ownership = Own();
            RefreshEntry(request.Id, forceMetadata: true);
            var saved = Read(request.Id);
            if (saved.Item.MetadataRevision != request.ExpectedRevision) throw Error("capture_library_metadata_conflict");
            var title = string.IsNullOrWhiteSpace(request.Title) ? null : request.Title.Trim();
            if (saved.Item.Title == title && saved.Item.IsFavorite == request.IsFavorite) return ItemView(saved);
            token.ThrowIfCancellationRequested();
            var updated = saved with { Item = saved.Item with
                { Title = title, IsFavorite = request.IsFavorite, MetadataRevision = checked(saved.Item.MetadataRevision + 1) } };
            // Publish metadata atomically; cancellation after this point must not
            // pretend that an already-committed edit did not happen.
            WriteMetadata(updated); IndexPut(updated, updated.Item.Available ? updated.Item.Media.ByteLength : 0);
            return ItemView(updated);
        }
        finally { gate.Release(); }
    }

    private static void DemandQuery(CaptureLibraryQuery? query, string? revision)
    {
        if (query?.IsWellFormed() != true || revision is not null && !CaptureLibraryLimits.ValidRevision(revision)) throw Error("invalid_payload");
    }
    private string DemandRevision(string? expected)
    {
        using var command = Command("SELECT epoch || '-' || printf('%x',sequence) FROM library_revision WHERE id=1");
        var current = (string)command.ExecuteScalar()!;
        if (expected is not null && current != expected) throw Error("capture_library_query_changed");
        return current;
    }
    private (string Where, (string Name, object Value)[] Parameters) QueryWhere(CaptureLibraryQuery query)
    {
        var conditions = new List<string>(); var parameters = new List<(string, object)>();
        if (query.AvailableOnly) conditions.Add("available=1");
        if (query.UnavailableOnly) conditions.Add("available=0");
        if (query.HiddenOnly) DemandHiddenState();
        LoadSources();
        if (!hiddenStateAvailable) conditions.Add("json_extract(data,'$.Item.SourceId') IS NULL");
        else
        {
            conditions.Add((query.HiddenOnly ? "" : "NOT ") + "EXISTS (SELECT 1 FROM userstate.hidden h WHERE h.id=captures.id)");
            var sources = sourceGrants.Values.Where(SourceEnabled).Select(s => s.Id).ToArray();
            var names = sources.Select((id, i) => { var name = "$visibleSource" + i; parameters.Add((name, id)); return name; }).ToArray();
            conditions.Add("(json_extract(data,'$.Item.SourceId') IS NULL" + (names.Length == 0 ? "" :
                " OR json_extract(data,'$.Item.SourceId') IN (" + string.Join(",", names) + ")") + ")");
        }
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            conditions.Add("(instr(lower(coalesce(json_extract(data,'$.Item.Title'),'')),lower($search))>0 OR instr(lower(coalesce(name,'')),lower($search))>0 OR instr(lower(coalesce(json_extract(data,'$.Item.Media.SourceApplication.ApplicationName'),'')),lower($search))>0 OR EXISTS (SELECT 1 FROM active_capture_associations association JOIN capture_association_refs reference ON reference.key_hash=association.key_hash WHERE association.capture_id=captures.id AND instr(lower(coalesce(reference.label,'')),lower($search))>0))");
            parameters.Add(("$search", query.Search.Trim()));
        }
        if (query.Application is { } application)
        { conditions.Add("coalesce(json_extract(data,'$.Item.Media.SourceApplication.ApplicationName'),'')=$application COLLATE NOCASE"); parameters.Add(("$application", application)); }
        if (query.SourceId is { } source)
        { conditions.Add("coalesce(json_extract(data,'$.Item.SourceId'),'')=$source"); parameters.Add(("$source", source)); }
        if (query.MediaKind != CaptureLibraryMediaKind.All)
        { conditions.Add("json_extract(data,'$.Item.Media.ContentType')=$kind"); parameters.Add(("$kind", query.MediaKind == CaptureLibraryMediaKind.Screenshot ? "image/png" : "video/mp4")); }
        if (query.SourceIntervals is { } intervals)
        {
            var matches = new List<string>(intervals.Count);
            foreach (var interval in intervals)
            {
                var i = matches.Count;
                matches.Add($"($instance{i},$from{i},$through{i})");
                parameters.Add(($"$instance{i}", interval.Instance));
                parameters.Add(($"$from{i}", interval.From));
                parameters.Add(($"$through{i}", interval.Through));
            }
            // Drive the lookup from the bounded interval list, including when the ordinary gallery also filters by source.
            conditions.Add("id IN (WITH windows(instance,start,finish) AS (VALUES " + string.Join(",", matches) +
                ") SELECT matched.id FROM windows CROSS JOIN captures matched WHERE " +
                "json_extract(matched.data,'$.Item.Media.SourceProcess.Instance')=windows.instance AND " +
                "json_extract(matched.data,'$.Item.Media.SourceProcess.From')>=windows.start AND " +
                "json_extract(matched.data,'$.Item.Media.SourceProcess.Through')<=windows.finish)");
        }
        if (query.OriginPackageId is { } origin)
        {
            parameters.Add(("$origin", origin));
            if (query.Reference is { } reference)
            {
                parameters.Add(("$referenceKind", reference.Kind)); parameters.Add(("$referenceId", reference.Id));
                var role = reference.Role is { } requestedRole ? " AND reference.role=$referenceRole" : "";
                if (reference.Role is { } value) parameters.Add(("$referenceRole", (int)value));
                var associations = query.ExcludeDetachedReference ? "capture_associations" : "active_capture_associations";
                conditions.Add((query.ExcludeReference ? "NOT " : "") + $"EXISTS (SELECT 1 FROM {associations} association JOIN capture_association_refs reference ON reference.key_hash=association.key_hash " +
                    "WHERE association.capture_id=captures.id AND association.origin_package=$origin AND reference.kind=$referenceKind AND reference.reference_id=$referenceId" + role + ")");
                if (query.ExcludeDetachedReference && hasDestinationMasks && hiddenStateAvailable)
                    conditions.Add("NOT EXISTS (SELECT 1 FROM userstate.destination_masks m WHERE m.capture_id=captures.id AND m.package=$origin AND m.kind=$referenceKind AND m.destination=$referenceId)");
            }
            else conditions.Add("(json_extract(data,'$.Item.Origin.PackageId')=$origin OR EXISTS (SELECT 1 FROM active_capture_associations association WHERE association.capture_id=captures.id AND association.origin_package=$origin))");
        }
        if (query.FavoritesOnly) conditions.Add("json_extract(data,'$.Item.IsFavorite')=1");
        if (query.FromUnixMilliseconds is { } from) { conditions.Add("saved_at >= $from"); parameters.Add(("$from", from)); }
        if (query.ToUnixMilliseconds is { } to) { conditions.Add("saved_at <= $to"); parameters.Add(("$to", to)); }
        return (conditions.Count == 0 ? "" : "WHERE " + string.Join(" AND ", conditions), parameters.ToArray());
    }
    private static string QueryOrder(CaptureLibrarySort sort) => sort switch
    {
        CaptureLibrarySort.Oldest => "saved_at ASC,id ASC",
        CaptureLibrarySort.Title => "coalesce(nullif(json_extract(data,'$.Item.Title'),''),json_extract(data,'$.Item.Media.SourceApplication.ApplicationName'),name,'') COLLATE NOCASE ASC,saved_at DESC,id ASC",
        CaptureLibrarySort.Application => "coalesce(json_extract(data,'$.Item.Media.SourceApplication.ApplicationName'),'') COLLATE NOCASE ASC,saved_at DESC,id ASC",
        CaptureLibrarySort.Largest => "bytes DESC,saved_at DESC,id ASC",
        CaptureLibrarySort.Longest => "coalesce(json_extract(data,'$.Item.Media.DurationSeconds'),0) DESC,saved_at DESC,id ASC",
        _ => "saved_at DESC,id ASC",
    };
}
