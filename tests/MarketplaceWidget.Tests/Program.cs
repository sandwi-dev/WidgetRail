using System.Text.Json;
using WidgetRail.FirstPartyWidgets.Marketplace;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;
using WidgetRail.WidgetStyling;

if (args is ["--export-native", var output]) { await ExportNative(output); return 0; }
var tests = new (string Name,Func<Task> Run)[] {
    ("Catalog navigation filters updates and restores the selected card", Navigation),
    ("Reopening checks releases while a slow refresh preserves navigation and existing content", RefreshOnOpening),
    ("Downloads retain focus and installation requires the exact open full-trust review", Review),
    ("Retained newer versions remain available through normal update actions", RetainedUpdate),
    ("Individual updates are reviewed and installed without leaving the Updates page", UpdateFromList),
    ("Update all uses one explicit bounded review and preserves per-item results", BatchReview),
    ("Marketplace intent resets the page through the ordinary receiver contract", Intent),
    ("Marketplace package and theme declare only narrow intents and no install permission", Package),
};
var failures=0;
foreach(var test in tests) try { await test.Run(); Console.WriteLine("PASS "+test.Name); } catch(Exception error) { failures++;Console.WriteLine("FAIL "+test.Name+": "+error); }
Console.WriteLine($"{tests.Length-failures}/{tests.Length} passed");return failures==0?0:1;
static void Check(bool value,string message) { if(!value)throw new Exception(message); }
static IEnumerable<ViewNode> Walk(ViewNode node)=>new[]{node}.Concat(node.Children.SelectMany(Walk));
static ViewNode? Node(Fixture f,string id)=>Walk(f.View().Root).FirstOrDefault(n=>n.Id==id);
static async Task Until(Func<bool> ready) { using var deadline=new CancellationTokenSource(TimeSpan.FromSeconds(6));while(!ready())await Task.Delay(10,deadline.Token); }
static async Task ExportNative(string directory)
{
    Directory.CreateDirectory(directory);
    await using var fixture = new Fixture(); await fixture.Start();
    await Write("browse");
    await fixture.Act("marketplace.tab.installed"); await Write("installed");
    fixture.Snapshot = fixture.Snapshot with { Revision = 2, Items = Enumerable.Range(0,16).Select(index =>
        new MarketplaceItem("example.batch." + index, "Synthetic widget " + index, "Controller-friendly synthetic add-on", [], "2.0.0", "1.0.0")).ToArray() };
    await fixture.Act("marketplace.refresh");
    await Until(() => Walk(fixture.View().Root).Count(node => node.Kind == ViewNodeKind.ActionSurface) == 16);
    await fixture.Act("marketplace.tab.updates"); await Write("updates");
    await fixture.Act("marketplace.update-all");
    await Until(() => fixture.Commands.Any(command => command.Action == "download-updates"));
    var permissions = Enumerable.Range(0, 8).Select(index => "example.optional.permission." + index).ToArray();
    fixture.Snapshot = fixture.Snapshot with { Revision = 3,
        Operation = new(new string('a',32), "download-updates", "review", null),
        UpdateBatch = new(fixture.Snapshot.Items.Select((item,index) => new MarketplaceUpdateItem(item.Id,item.Name,"2.0.0","review",
            new(new string('b',64),item.Id,item.Name,"2.0.0","1.0.0",index == 0,true,"Compatible",permissions,permissions,permissions,new string('c',64)))).ToArray(),new string('d',64)) };
    await Until(() => Node(fixture,"marketplace.batch.review.open") is not null);
    await fixture.Act("marketplace.batch.review"); await Write("review");
    await fixture.Act("marketplace.batch.install");
    await Until(() => Node(fixture,"marketplace.batch.review.cancel") is null && fixture.Commands.Any(command => command.Action == "install-updates"));
    await Write("completed");
    async Task Write(string name) => await File.WriteAllBytesAsync(Path.Combine(directory, name + ".snapshot.json"), SnapshotJson.Serialize(fixture.View()));
}

static async Task Navigation()
{
    await using var f=new Fixture();await f.Start();
    Check(Node(f,"marketplace.title")?.Text == "WidgetRail Catalogue", "Catalogue heading missing");
    Check(f.Commands.All(c=>c.Action is "status" or "refresh") && f.Commands.Any(c=>c.Action == "refresh"),"Opening did not check releases or unexpectedly mutated packages");
    Check(Walk(f.View().Root).Count(n=>n.Kind==ViewNodeKind.ActionSurface)==2,"Catalog cards missing");
    await f.Act("marketplace.tab.installed");
    Check(Walk(f.View().Root).Count(n=>n.Kind==ViewNodeKind.ActionSurface)==1,"Installed view included an uninstalled widget");
    await f.Act("marketplace.tab.updates");
    Check(Walk(f.View().Root).Count(n=>n.ActionId?.StartsWith("marketplace.update.", StringComparison.Ordinal)==true)==1,"Updates filter included an uninstalled widget");
    await f.Act("marketplace.tab.installed");
    var card=Walk(f.View().Root).Single(n=>n.Kind==ViewNodeKind.ActionSurface);
    await f.Act(card.ActionId!);Check(Node(f,"marketplace.description") is not null,"Details did not open");
    Check(Node(f,"marketplace.manage")?.Intent?.ContractId==WidgetIntentContracts.OpenWidgetSettings,"Installed management does not route to Settings");
    await f.Act("marketplace.back");Check(f.View().InitialFocusId==card.Id,"Back did not restore selected card");
    Check(ViewSnapshotValidator.Validate(f.View()).Count==0 && WinUiPresentationContract.Validate(f.View()).Count==0,"Catalog snapshot invalid");
}
static async Task Review()
{
    await using var f=new Fixture();await f.Start();
    var card=Walk(f.View().Root).First(n=>n.Kind==ViewNodeKind.ActionSurface);
    await f.Act(card.ActionId!);await f.Act("marketplace.download");
    await Until(()=>Node(f,"marketplace.primary")?.IsBusy==true);
    Check(Node(f,"marketplace.primary")?.IsDisabled!=true,"Busy download removed focus");
    var review=new MarketplaceReview(new string('b',64),Fixture.Id,"First widget","2.0.0","1.0.0",true,true,"Compatible",
        ["system.audio.output.read.v1"],[],["system.audio.output.read.v1"],new string('c',64));
    f.Snapshot=f.Snapshot with { Revision=3, Operation=new(new string('a',32),"download","review",Fixture.Id,Review:review) };
    await Until(()=>Node(f,"marketplace.primary")?.Text=="Review installation");
    await f.Act("marketplace.install");Check(!f.Commands.Any(c=>c.Action=="install"),"Hidden review was approved");
    await f.Act("marketplace.review.open");Check(f.View().InitialFocusId=="marketplace.review.cancel","Review does not start at Cancel");
    Check(ViewSnapshotValidator.Validate(f.View()).Count==0,"Review snapshot invalid");
    await f.Act("marketplace.review.cancel");Check(!f.Commands.Any(c=>c.Action=="install"),"Cancelling review installed");
    await f.Act("marketplace.review.open");await f.Act("marketplace.install");
    await Until(()=>f.Commands.Any(c=>c.Action=="install"));
    var install=f.Commands.Single(c=>c.Action=="install");
    Check(install.ReviewToken==review.Token && install.OperationId==new string('a',32) && install.ApproveFullTrust,"Exact review or full-access acknowledgement lost");
    await Until(()=>Node(f,"marketplace.primary")?.Intent is not null);
    Check(Node(f,"marketplace.primary")!.Intent!.Payload.GetProperty("packageId").GetString()==Fixture.Id,"Completion did not offer exact widget management");
}
static async Task RefreshOnOpening()
{
    await using var f = new Fixture { RefreshGate = new(TaskCreationOptions.RunContinuationsAsynchronously) };
    await f.Start();
    Check(!f.RefreshGate.Task.IsCompleted && Node(f, "marketplace.refresh")?.IsBusy == true, "Fixture did not hold the refresh request");
    await f.Act("marketplace.tab.installed");
    Check(Walk(f.View().Root).Count(n => n.Kind == ViewNodeKind.ActionSurface) == 1, "Refresh hid usable installed content");
    var card = Walk(f.View().Root).Single(n => n.Kind == ViewNodeKind.ActionSurface);
    await f.Act(card.ActionId!);
    Check(Node(f, "marketplace.description") is not null, "Refresh blocked navigation to existing details");
    await f.Act("marketplace.back");
    Check(f.View().InitialFocusId == card.Id, "Refresh lost the remembered card on Back");
    f.RefreshGate.SetResult();
    await Until(() => Node(f, "marketplace.refresh")?.IsBusy != true);
    await WidgetTestHost.SetLifecycleStateAsync(f.Widget, WidgetLifecycleState.Background);
    f.Commands.Clear();
    await WidgetTestHost.SetLifecycleStateAsync(f.Widget, WidgetLifecycleState.Interactive);
    await Until(() => f.Commands.Any(c => c.Action == "refresh"));
    Check(f.Commands.All(c => c.Action is "status" or "refresh"), "Reopening mutated a package without user action");
}
static async Task RetainedUpdate()
{
    await using var f = new Fixture();
    f.Snapshot = f.Snapshot with { Items = f.Snapshot.Items.Select(item => item.Id == Fixture.Id ? item with { AvailableVersionSaved = true } : item).ToArray() };
    await f.Start();
    await f.Act("marketplace.tab.updates");
    var update = Walk(f.View().Root).Single(node => node.ActionId?.StartsWith("marketplace.update.", StringComparison.Ordinal) == true);
    await f.Act(update.ActionId!);
    await Until(() => f.Commands.Any(command => command.Action == "download-updates"));
    Check(ViewSnapshotValidator.Validate(f.View()).Count == 0, "Retained update presentation is invalid");
}

static async Task UpdateFromList()
{
    await using var f = new Fixture(); await f.Start();
    await f.Act("marketplace.tab.updates");
    var update = Walk(f.View().Root).Single(node => node.ActionId?.StartsWith("marketplace.update.", StringComparison.Ordinal) == true);
    await f.Act(update.ActionId!);
    await Until(() => f.Commands.Any(command => command.Action == "download-updates"));
    Check(Node(f, "marketplace.description") is null, "Update opened a details page");
    var review = new MarketplaceReview(new string('b',64), Fixture.Id, "First widget", "2.0.0", "1.0.0", false, true, "Compatible", [], [], [], new string('c',64));
    f.Snapshot = f.Snapshot with { Revision = 3, Operation = new(new string('a',32), "download-updates", "review", null),
        UpdateBatch = new([new(Fixture.Id, "First widget", "2.0.0", "review", review)], new string('d',64)) };
    await Until(() => Node(f, update.Id)?.Text == "Review update");
    await f.Act("marketplace.batch.install");
    Check(!f.Commands.Any(command => command.Action == "install-updates"), "Update bypassed its explicit review");
    await f.Act("marketplace.batch.review");
    Check(f.View().InitialFocusId == "marketplace.batch.review.cancel", "Update review does not start on Cancel");
    await f.Act("marketplace.batch.install");
    await Until(() => f.Commands.Any(command => command.Action == "install-updates"));
    Check(f.Commands.Single(command => command.Action == "install-updates").ReviewToken == new string('d',64), "Update did not use the exact reviewed token");
    Check(Node(f, "marketplace.description") is null && Node(f, "marketplace.tab.updates")?.IsSelected == true,
        "Installing from Updates left the page");
    Check(ViewSnapshotValidator.Validate(f.View()).Count == 0, "Updates result snapshot is invalid");
}
static async Task BatchReview()
{
    await using var f = new Fixture();
    f.Snapshot = f.Snapshot with { Items = Enumerable.Range(0,16).Select(index =>
        new MarketplaceItem("example.batch."+index, "Widget "+index, "Synthetic widget", [], "2.0.0", "1.0.0")).ToArray() };
    await f.Start(); await f.Act("marketplace.tab.updates"); await f.Act("marketplace.update-all");
    await Until(() => f.Commands.Any(command => command.Action == "download-updates"));
    Check(f.Commands.Single(command => command.Action == "download-updates").PackageIds!.Count == 16, "Update all did not freeze the visible updates");
    var permissions = Enumerable.Range(0,64).Select(index => "example.optional.permission."+index).ToArray();
    var batch = new MarketplaceUpdateBatch(f.Snapshot.Items.Select((item,index) => new MarketplaceUpdateItem(item.Id,item.Name,"2.0.0","review",
        new(new string('b',64),item.Id,item.Name,"2.0.0","1.0.0",index==0,true,"Compatible",permissions,permissions,permissions,new string('c',64)))).ToArray(),new string('d',64));
    f.Snapshot = f.Snapshot with { Revision = 3, UpdateBatch = batch, Operation = new(new string('a',32),"download-updates","review",null) };
    await Until(() => Node(f,"marketplace.batch.review.open") is not null);
    await f.Act("marketplace.batch.install");
    Check(!f.Commands.Any(command => command.Action == "install-updates"), "Hidden aggregate review was approved");
    await f.Act("marketplace.batch.review");
    Check(f.View().InitialFocusId == "marketplace.batch.review.cancel", "Batch review did not focus Cancel");
    var errors = ViewSnapshotValidator.Validate(f.View());
    Check(errors.Count == 0, "Maximum-sized review exceeded snapshot limits: " + string.Join("; ",errors));
    Check(Walk(f.View().Root).Any(node => node.Text?.Contains("Full access") == true), "Aggregate review omitted full access");
    await f.Act("marketplace.review.cancel");
    Check(!f.Commands.Any(command => command.Action == "install-updates"), "Cancelling the review approved packages");
    await f.Act("marketplace.batch.review"); await f.Act("marketplace.batch.install");
    await Until(() => f.Commands.Any(command => command.Action == "install-updates"));
    Check(f.Commands.Single(command => command.Action == "install-updates") is { ApproveFullTrust:true, ReviewToken:not null }, "Aggregate full-access decision was not sent");
    await Until(() => Walk(f.View().Root).Any(node => node.Text?.Contains("Updated") == true));
    Check(Node(f,"marketplace.tab.updates")?.IsSelected == true && Node(f,"marketplace.description") is null, "Results left the update list");
}
static async Task Intent()
{
    await using var f=new Fixture();await f.Start();
    await f.Act(Walk(f.View().Root).First(n=>n.Kind==ViewNodeKind.ActionSurface).ActionId!);
    var request=WidgetIntentRequest.Create(WidgetIntentContracts.Marketplace,JsonSerializer.SerializeToElement(new{}));
    Check(await f.Widget.OnIntentAsync(request)==WidgetIntentResult.Accepted && Node(f,"marketplace.description") is null,"Intent retained stale details page");
    Check(await f.Widget.OnIntentAsync(request with { SchemaDigest=new string('0',64) })==WidgetIntentResult.Rejected,"Conflicting contract accepted");
}
static Task Package()
{
    var manifest=ManifestJson.Deserialize(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory,"fixture/manifest.json")));
    Check(WidgetManifestValidator.Validate(manifest).Count==0,"Manifest invalid");
    Check(manifest.Name == "WidgetRail Catalogue" && manifest.Id == "widgetrail.firstparty.marketplace", "Display rename changed the package identity");
    Check(manifest.Permissions.Count==0 && manifest.OptionalPermissions.Count==0,"Widget attempts to self-declare management authority");
    Check(manifest.Intents!.Handles.Single().Id==WidgetIntentContracts.OpenMarketplace,"Marketplace handler missing");
    var style=WrssParser.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"fixture/default.wrss")),"default.wrss");
    Check(style.IsValid,"Marketplace WRSS invalid");
    var compiled=WrssThemeCompiler.Compile([style.Document]);
    Check(compiled.IsValid,string.Join("; ",compiled.Diagnostics.Select(d=>d.Message)));
    return Task.CompletedTask;
}
sealed class Fixture : IAsyncDisposable
{
    internal const string Id="example.first";
    internal MarketplaceSnapshot Snapshot=new(1,[new(Id,"First widget","Controller-first example.",[],"2.0.0","1.0.0",1024),new("example.second","Second widget","Another example.",[],"1.0.0")],CheckedAtUtc:DateTimeOffset.UtcNow);
    internal System.Collections.Concurrent.ConcurrentQueue<MarketplaceCommand> Commands=new();
    internal TaskCompletionSource? RefreshGate;
    internal MarketplaceWidget Widget;
    internal Fixture()
    {
        var builder=new WidgetTestHostServicesBuilder();
        foreach(var action in new[]{"status","refresh","inventory","download","install","cancel","download-updates","install-updates"})
            builder.WithHandler(WidgetMarketplaceCapabilities.Command(action),async (command,token)=>{
                Commands.Enqueue(command);
                if (command.Action == "refresh" && RefreshGate is not null) await RefreshGate.Task.WaitAsync(token);
                if(command.Action=="download")Snapshot=Snapshot with {Revision=2,Operation=new(new string('a',32),"download","running",Id)};
                if (command.Action == "download-updates") Snapshot = Snapshot with { Revision = 2,
                    Operation = new(new string('a',32),"download-updates","running",command.PackageIds![0]),
                    UpdateBatch = new(Snapshot.Items.Where(item => command.PackageIds.Contains(item.Id)).Select(item =>
                        new MarketplaceUpdateItem(item.Id,item.Name,item.AvailableVersion!,"queued")).ToArray()) };
                if (command.Action == "install-updates") Snapshot = Snapshot with { Revision = 4,
                    Items = Snapshot.Items.Select(item => Snapshot.UpdateBatch!.Items.Any(update => update.PackageId == item.Id && update.State == "review")
                        ? item with { InstalledVersion = item.AvailableVersion, AvailableVersionSaved = true } : item).ToArray(),
                    Operation = new(new string('a',32),"install-updates","completed",null),
                    UpdateBatch = Snapshot.UpdateBatch! with { ReviewToken = null, Items = Snapshot.UpdateBatch!.Items.Select(item => item with { State="completed",Review=null }).ToArray() } };
                if(command.Action=="install")Snapshot=Snapshot with {Revision=4,Items=Snapshot.Items.Select(i=>i.Id==Id?i with {InstalledVersion="2.0.0",AvailableVersionSaved=true}:i).ToArray(),
                    Operation=new(new string('a',32),"install","completed",Id,Message:"Widget updated")};
                return Snapshot;
            });
        Widget=WidgetTestHost.Attach(new MarketplaceWidget(),builder.Build());
    }
    internal ViewSnapshot View()
    {
        try { return Widget.RenderSnapshot("marketplace",1); }
        catch (ProtocolValidationException error) { throw new Exception(string.Join("; ", error.Errors), error); }
    }
    internal async Task Start() { await WidgetTestHost.InitializeAsync(Widget);await WidgetTestHost.SetLifecycleStateAsync(Widget,WidgetLifecycleState.Interactive);
        using var deadline=new CancellationTokenSource(TimeSpan.FromSeconds(5));while(Commands.Count<2)await Task.Delay(10,deadline.Token); }
    internal ValueTask Act(string action)=>Widget.OnActionAsync(new(action,action));
    public ValueTask DisposeAsync()=>WidgetTestHost.DestroyAsync(Widget);
}
