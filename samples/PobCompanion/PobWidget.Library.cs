namespace WidgetRail.Samples.PobCompanion;

public sealed partial class PobWidget
{
    private string BuildName() => savedBuilds.FirstOrDefault(item=>item.Id==documentId).Name ?? S(State,"mainSkill","New build");
    private void EnsureBuildName(PobResult build,string? targetId=null)
    {
        var path=Path.Combine(dataDirectory,(targetId??documentId)+".name.txt");
        if(File.Exists(path))return;
        var skill=S(build.State,"mainSkill","New build");
        if(string.IsNullOrWhiteSpace(skill))skill="New build";
        var identity=S(build.State,"ascendancy");
        if(string.IsNullOrWhiteSpace(identity)||identity=="None")identity=S(build.State,"class");
        var name=skill+(identity.Length>0?" · "+identity:"");
        BuildDocument.SaveAtomic(path,name.Length>100?name[..99]+"…":name);
    }
    private void RefreshLibrary()
    {
        if(!Directory.Exists(dataDirectory))return;
        var entries=Directory.EnumerateFiles(dataDirectory,"*.xml")
            .Where(file=>Guid.TryParseExact(Path.GetFileNameWithoutExtension(file),"N",out _))
            .Select(file=>new FileInfo(file)).OrderByDescending(file=>file.LastWriteTimeUtc).Take(256)
            .Select(file=>
            {
                var id=Path.GetFileNameWithoutExtension(file.Name);
                var path=Path.Combine(dataDirectory,id+".name.txt");
                var label=File.Exists(path)&&new FileInfo(path).Length<=1024?File.ReadAllText(path).Trim():"";
                return (Id:id,Name:label.Length>0?Limit(label,100):"Saved build · "+file.LastWriteTime.ToString("g"));
            }).ToArray();
        lock(gate)savedBuilds=entries;
    }
}
