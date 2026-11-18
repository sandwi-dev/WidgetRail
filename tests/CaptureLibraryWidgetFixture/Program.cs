using System.Globalization;
using System.Threading.Channels;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetRuntime;
using WidgetRail.WidgetSdk;
using WidgetRail.PlatformSettings;
using WidgetRail.FirstPartyWidgets.CaptureLibrary;

// Real shipping widget and renderer; synthetic metadata and bounded artwork only.
// No capture, filesystem deletion, external app, or account service is reachable.
if (args is ["--write-appearance", var profile, var variant])
{
    var appearance = AppearanceSettings.Default with
    {
        ThemeId = variant == "light" ? ThemeIdentity.BuiltInMineralLight : ThemeIdentity.BuiltInMonoInk,
        InterfaceScale = variant == "large" ? 1.25 : 1,
        TextScale = variant == "large" ? 1.5 : 1,
        Contrast = variant == "contrast" ? ContrastPreference.High : ContrastPreference.Standard,
    };
    await new PlatformSettingsStore(new(profile)).ReplaceAsync(PlatformSettingsDocument.Default with { Appearance = appearance });
    return;
}
string Required(string name) { var index=Array.IndexOf(args,name); return index>=0&&index+1<args.Length?args[index+1]:throw new ArgumentException(name); }
var art=File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory,"fixture-art.png"));
var thumbnail=new CaptureLibraryThumbnail(256,144,art);
if(!thumbnail.IsWellFormed())throw new InvalidDataException("Expected bounded synthetic PNG");
var changes=Channel.CreateBounded<CaptureLibraryChanged>(new BoundedChannelOptions(1){FullMode=BoundedChannelFullMode.DropOldest});
var names=new[]{"Hollow Knight Silksong","Forza Horizon","Baldur's Gate","Elden Ring","A very long application name for layout verification"};
var rows=Enumerable.Range(1,1200).Select(index=>new CaptureLibraryItem(index.ToString("x32"),
    new(index.ToString("x32"),index%4==0?"video/mp4":"image/png",1280,720,1024,index%4==0?5:0,long.MaxValue){SourceApplication=new(names[index%names.Length],"Synthetic window")},
    DateTimeOffset.Now.AddHours(-index*2).ToUnixTimeMilliseconds()) { Title=index%5==0?"A memorable moment with a longer capture title "+index:null,IsFavorite=index%7==0,FileName="Fixture-"+index+".png" }).ToList();
var gate=new object();long revision=1;
CaptureLibraryItem[] Query(CaptureLibraryQuery query)
{
    var matches=rows.Where(row=>(query.Search is not {Length:>0} text||(row.Title+" "+row.Media.SourceApplication!.ApplicationName).Contains(text,StringComparison.OrdinalIgnoreCase))&&
        (query.Application is null||query.Application==row.Media.SourceApplication!.ApplicationName)&&(!query.FavoritesOnly||row.IsFavorite)&&
        (query.MediaKind==CaptureLibraryMediaKind.All||query.MediaKind==CaptureLibraryMediaKind.Video==(row.Media.ContentType=="video/mp4"))&&
        (query.FromUnixMilliseconds is null||row.SavedAtUnixMilliseconds>=query.FromUnixMilliseconds));
    return (query.Sort switch{CaptureLibrarySort.Oldest=>matches.OrderBy(row=>row.SavedAtUnixMilliseconds),CaptureLibrarySort.Title=>matches.OrderBy(row=>row.Title),
        CaptureLibrarySort.Application=>matches.OrderBy(row=>row.Media.SourceApplication!.ApplicationName),_=>matches.OrderByDescending(row=>row.SavedAtUnixMilliseconds)}).ThenBy(row=>row.Id).ToArray();
}
var client=new WidgetTestHostServicesBuilder().WithPrivateState(new())
    .WithResponse(WidgetCaptureCapabilities.Capabilities,new CaptureCapabilities(true,[CaptureImageFormat.Png],
        [new("1080p30-hardware",1920,1080,30,3_000_000,12_000_000,CaptureEncoderMode.Hardware)],[CaptureAudioMode.None],true,120))
    .WithResponse(WidgetCaptureLibrarySourceCapabilities.List,new CaptureLibrarySources([
        new(new string('b',32),"Game recordings",true,1200,2,@"C:\Example captures\Saved recordings"),
        new(new string('c',32),"Disconnected drive",false,0,0,@"D:\Another long folder name\Captures")] ))
    .WithEventStream(WidgetCaptureLibraryCapabilities.Changed,token=>changes.Reader.ReadAllAsync(token))
    .WithResponse(WidgetPermissionCapabilities.Request,new WidgetPermissionRequestState(WidgetPermissionOutcome.Granted))
    .WithHandler(WidgetCaptureLibraryCapabilities.Query,(request,_)=>{lock(gate){var data=Query(request.Query);return ValueTask.FromResult(new CaptureLibraryQueryPage(data.Skip(request.Offset).Take(request.Count).ToArray(),request.Offset,data.Length,data.Length*1024,revision.ToString("x")));}})
    .WithHandler(WidgetCaptureLibraryCapabilities.Find,(request,_)=>{lock(gate){var data=Query(request.Query);var index=Array.FindIndex(data,row=>row.Id==request.Id);return ValueTask.FromResult(new CaptureLibraryPosition(index<0?null:index,data.Length,revision.ToString("x")));}})
    .WithHandler(WidgetCaptureLibraryCapabilities.Applications,(request,_)=>{lock(gate){var data=Query(request.Query with{Application=null}).GroupBy(row=>row.Media.SourceApplication!.ApplicationName).Select(group=>new CaptureLibraryApplication(group.Key,group.Count())).ToArray();return ValueTask.FromResult(new CaptureLibraryApplications(data,false));}})
    .WithHandler(WidgetCaptureLibraryCapabilities.Folders,(request,_)=>{lock(gate){return ValueTask.FromResult(new CaptureLibraryFolders([new("","WidgetRail captures",Query(request.Query with{SourceId=null}).Length)],revision.ToString("x")));}})
    .WithHandler(WidgetCaptureLibraryCapabilities.Groups,(request,_)=>{lock(gate){var data=Query(request.Query);var groups=data.GroupBy(row=>DateTimeOffset.FromUnixTimeMilliseconds(row.SavedAtUnixMilliseconds).ToLocalTime().ToString("yyyy-MM",CultureInfo.InvariantCulture)).Select(group=>new CaptureLibraryGroup(group.Key,group.Count())).ToArray();return ValueTask.FromResult(new CaptureLibraryGroups(groups,data.Length,revision.ToString("x")));}})
    .WithHandler(WidgetCaptureLibraryCapabilities.Thumbnail,(request,_)=>ValueTask.FromResult(new CaptureLibraryThumbnailChunk(256,144,art.Length,request.Offset,art.Skip(request.Offset).Take(CaptureLimits.ChunkBytes).ToArray())))
    .WithHandler(WidgetCaptureLibraryCapabilities.UpdateMetadataBatch,(request,_)=>{lock(gate){var results=new List<CaptureLibraryMutationResult>();foreach(var edit in request.Items){var index=rows.FindIndex(row=>row.Id==edit.Id);rows[index]=rows[index] with{Title=edit.Title,IsFavorite=edit.IsFavorite,MetadataRevision=rows[index].MetadataRevision+1};results.Add(new(edit.Id,rows[index]));}changes.Writer.TryWrite(new(++revision));return ValueTask.FromResult(new CaptureLibraryBatchResult(results));}})
    .WithHandler(WidgetCaptureLibraryCapabilities.Recycle,(request,_)=>{lock(gate){foreach(var row in request.Items)rows.RemoveAll(item=>item.Id==row.Id);changes.Writer.TryWrite(new(++revision));return ValueTask.FromResult(new CaptureLibraryBatchResult(request.Items.Select(item=>new CaptureLibraryMutationResult(item.Id)).ToArray()));}}).Build();
try
{
await new WidgetWorkerServer(new CaptureLibraryWidget(),Required("--widget-instance"),Required("--widget-pipe"),
    int.Parse(Required("--max-message-bytes"),CultureInfo.InvariantCulture),client,Required("--widget-session-nonce")).RunAsync();

}
catch(Exception error)
{
    await File.WriteAllTextAsync(Path.Combine(AppContext.BaseDirectory,"fixture-error.txt"),error.ToString());
    throw;
}
