namespace WidgetRail.Samples.PobCompanion;

public sealed partial class PobWidget
{
    private readonly Dictionary<string,(DateTime Stamp,string Class,string Ascendancy)> libraryMetadata=new();
    private string libraryClass="";
    private long libraryMetadataRevision;
    private string[] LibraryClasses()=>savedBuilds.Select(b=>libraryMetadata.GetValueOrDefault(b.Id).Class??"").Where(c=>c.Length>0).Distinct().Order(StringComparer.Ordinal).ToArray();
    private void RefreshBuildMetadata(string id)
    {
        var file=new FileInfo(Path.Combine(dataDirectory,id+".xml"));
        if(!file.Exists||libraryMetadata.TryGetValue(id,out var cached)&&cached.Stamp==file.LastWriteTimeUtc)return;
        libraryMetadataRevision++;
        try
        {
            using var reader=System.Xml.XmlReader.Create(file.FullName,new(){DtdProcessing=System.Xml.DtdProcessing.Prohibit,XmlResolver=null,MaxCharactersInDocument=BuildDocument.MaximumBytes});
            reader.MoveToContent();
            if(reader.ReadToDescendant("Build"))libraryMetadata[id]=(file.LastWriteTimeUtc,reader.GetAttribute("className")??"",reader.GetAttribute("ascendClassName")??"");
        }
        catch(Exception error)when(error is IOException or System.Xml.XmlException or UnauthorizedAccessException)
        {libraryMetadata[id]=(file.LastWriteTimeUtc,"","");}
    }
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
            .Concat(Directory.EnumerateFiles(dataDirectory,"*.work.json"))
            .Select(file=>(File:new FileInfo(file),Id:Path.GetFileName(file).Split('.')[0]))
            .Where(entry=>Guid.TryParseExact(entry.Id,"N",out _))
            .Where(entry=>entry.File.Name==entry.Id+".xml"||entry.File.Name==entry.Id+".work.json")
            .GroupBy(entry=>entry.Id).Select(group=>group.OrderByDescending(entry=>entry.File.LastWriteTimeUtc).First())
            .OrderByDescending(entry=>entry.File.LastWriteTimeUtc)
            .Select(entry=>
            {
                var file=entry.File;var id=entry.Id;
                var path=Path.Combine(dataDirectory,id+".name.txt");
                var label=File.Exists(path)&&new FileInfo(path).Length<=1024?File.ReadAllText(path).Trim():"";
                return (Id:id,Name:label.Length>0?Limit(label,100):"Saved build · "+file.LastWriteTime.ToString("g"));
            }).ToArray();
        lock(gate){foreach(var entry in entries)RefreshBuildMetadata(entry.Id);savedBuilds=entries;}
    }
}
