using System.Text.Json;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

namespace WidgetRail.Samples.PobCompanion;

public sealed partial class PobWidget
{
    private const int RecentBuildLimit=20;
    private string[] recentBuildIds=[];
    private bool recentBuildsLoaded;
    private (string Id,string Name)[] recentBuildEntries=[];

    private void RefreshRecentBuildEntries()
    {
        if(!recentBuildsLoaded)
        {
            recentBuildsLoaded=true;
            var path=Path.Combine(dataDirectory,"recent-builds.json");
            try
            {
                if(File.Exists(path)&&new FileInfo(path).Length<=4096)
                    recentBuildIds=(JsonSerializer.Deserialize<string[]>(File.ReadAllText(path))??[])
                        .Where(id=>Guid.TryParseExact(id,"N",out _)).Distinct(StringComparer.Ordinal).Take(RecentBuildLimit).ToArray();
            }
            catch(Exception error)when(error is IOException or UnauthorizedAccessException or JsonException){ }
        }
        var entries=savedBuilds.ToDictionary(entry=>entry.Id,StringComparer.Ordinal);
        recentBuildEntries=(documentId is {} current?new[]{current}:[]).Concat(recentBuildIds).Concat(savedBuilds.Select(entry=>entry.Id))
            .Distinct(StringComparer.Ordinal).Where(entries.ContainsKey).Take(RecentBuildLimit).Select(id=>entries[id]).ToArray();
    }

    private void RememberRecentBuild(string id)
    {
        lock(gate)
        {
            RefreshRecentBuildEntries();
            recentBuildIds=new[]{id}.Concat(recentBuildEntries.Select(entry=>entry.Id)).Distinct(StringComparer.Ordinal).Take(RecentBuildLimit).ToArray();
            try { BuildDocument.SaveAtomic(Path.Combine(dataDirectory,"recent-builds.json"),JsonSerializer.Serialize(recentBuildIds)); }
            catch(Exception error)when(error is IOException or UnauthorizedAccessException){ /* Optional history must not fail a successful build switch. */ }
            RefreshRecentBuildEntries();
        }
    }

    private WidgetElement RecentBuildSelector()
    {
        var entries=recentBuildEntries;
        var options=entries.Select(entry=>new SelectOption(entry.Id,entry.Id==documentId?BuildName():entry.Name,
            "recent-build."+entry.Id,entry.Id==documentId)).ToList();
        if(documentId is {} current&&!entries.Any(entry=>entry.Id==current))
        {
            if(options.Count>=RecentBuildLimit)options.RemoveAt(options.Count-1);
            options.Insert(0,new(current,BuildName(),"recent-build."+current,true));
        }
        options.Add(new("library","All saved builds…","library"));
        return UI.Select("Build",options.ToArray(),"pob.recent-builds").Disabled(pendingBefore is not null||buildComparisonRunning)
            .Classes("pob-select","pob-recent-builds");
    }
}
