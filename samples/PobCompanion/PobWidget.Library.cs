namespace WidgetRail.Samples.PobCompanion;

public sealed partial class PobWidget
{
    private readonly PobBuildLibrary buildLibrary = new();
    private PobLibrarySnapshot librarySnapshot = PobLibrarySnapshot.Empty;
    private IReadOnlyDictionary<string, PobBuildMetadata> libraryMetadata => librarySnapshot.Metadata;
    private long libraryMetadataRevision => librarySnapshot.MetadataRevision;
    private string libraryClass = "";
    private string[] LibraryClasses() => libraryMetadata.Values.Select(value => value.Class).Where(c => c.Length > 0).Distinct().Order(StringComparer.Ordinal).ToArray();
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
        var snapshot = buildLibrary.Read(dataDirectory);
        lock (gate) { librarySnapshot = snapshot; RefreshRecentBuildEntries(); }
    }
}
