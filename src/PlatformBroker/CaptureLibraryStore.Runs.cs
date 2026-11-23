using System.Text.Json;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.PlatformBroker;

public sealed partial class CaptureLibraryStore
{
    public async Task<CaptureLibraryRunsPage> RunsAsync(CaptureLibraryRunsRequest request, CancellationToken token)
    {
        if (!request.IsWellFormed()) throw Error("invalid_payload");
        await gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            using var ownership = Own();
            var revision = DemandRevision(request.Revision);
            var (where, filters) = QueryWhere(request.Query);
            var parameters = filters.ToList();
            var matches = new List<string>();
            foreach (var term in request.SourceTerms ?? [])
            {
                var parameter = "$term" + matches.Count;
                matches.Add($"(instr(source,lower({parameter}))>0)");
                parameters.Add((parameter, term));
            }
            var score = matches.Count == 0 ? "0" : string.Join(" + ", matches);
            var preferred = request.PreferredProcessName is null ? "0" : "executable=$preferred COLLATE NOCASE";
            if (request.PreferredProcessName is not null) parameters.Add(("$preferred", request.PreferredProcessName));
            parameters.Add(("$limit", request.Count + 1));
            parameters.Add(("$offset", request.Offset));
            // Group before paging. The fixed-width timestamp prefix selects a deterministic
            // representative even when several files share the exact capture time.
            using var command = Command($"""
                WITH eligible AS (
                    SELECT id, json_extract(data,'$.Item.Media.SourceProcess.Instance') AS instance,
                        coalesce(json_extract(data,'$.Item.Media.SourceProcess.From'),json_extract(data,'$.Item.CapturedAtUnixMilliseconds'),saved_at) AS started,
                        coalesce(json_extract(data,'$.Item.Media.SourceProcess.Through'),json_extract(data,'$.Item.CapturedAtUnixMilliseconds'),saved_at) AS ended,
                        json_extract(data,'$.Item.Media.ContentType')='video/mp4' AS video,
                        json_extract(data,'$.Item.Media.SourceApplication.ProcessName') AS executable,
                        lower(coalesce(json_extract(data,'$.Item.Media.SourceApplication.ApplicationName'),'') || char(10) ||
                            coalesce(json_extract(data,'$.Item.Media.SourceApplication.ProcessName'),'') || char(10) ||
                            coalesce(json_extract(data,'$.Item.Media.SourceApplication.WindowTitle'),'')) AS source
                    FROM captures {where}
                ), runs AS (
                    SELECT instance,min(started) AS first,max(ended) AS last,count(*)-sum(video) AS photos,sum(video) AS videos,
                        max({score}) AS relevance,coalesce(max({preferred}),0) AS learned,
                        max(printf('%016x',ended)||id) AS representative
                    FROM eligible GROUP BY coalesce(instance,id)
                    ORDER BY learned DESC,relevance DESC,last DESC,representative DESC LIMIT $limit OFFSET $offset
                )
                SELECT c.data,r.instance,r.first,r.last,r.photos,r.videos,r.relevance,r.learned
                FROM runs r JOIN captures c ON c.id=substr(r.representative,17)
                ORDER BY r.learned DESC,r.relevance DESC,r.last DESC,r.representative DESC
                """, parameters.ToArray());
            using var reader = command.ExecuteReader();
            var runs = new List<CaptureLibraryRun>(request.Count);
            while (runs.Count < request.Count && reader.Read())
            {
                token.ThrowIfCancellationRequested();
                var saved = JsonSerializer.Deserialize<Saved>(reader.GetString(0))!;
                runs.Add(new(ItemView(saved), reader.IsDBNull(1) ? null : reader.GetString(1),
                    reader.GetInt64(2), reader.GetInt64(3), reader.GetInt32(4), reader.GetInt32(5),
                    reader.GetInt32(6) > 0, reader.GetInt32(7) != 0));
            }
            return new(runs, request.Offset, runs.Count == request.Count && reader.Read(), revision);
        }
        finally { gate.Release(); }
    }
}
