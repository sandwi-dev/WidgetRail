using System.Text.Json;
using WidgetRail.FirstPartyWidgets.Marketplace;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;
using WidgetRail.WidgetStyling;

var tests = new (string Name,Func<Task> Run)[] {
    ("Catalog navigation filters updates and restores the selected card", Navigation),
    ("Downloads retain focus and installation requires the exact open full-trust review", Review),
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
static async Task Navigation()
{
    await using var f=new Fixture();await f.Start();
    Check(f.Commands.All(c=>c.Action is "status" or "inventory"),"Activation mutated packages");
    Check(Walk(f.View().Root).Count(n=>n.Kind==ViewNodeKind.ActionSurface)==2,"Catalog cards missing");
    await f.Act("marketplace.filter");
    Check(Walk(f.View().Root).Count(n=>n.Kind==ViewNodeKind.ActionSurface)==1,"Updates filter included an uninstalled widget");
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
    internal MarketplaceWidget Widget;
    internal Fixture()
    {
        var builder=new WidgetTestHostServicesBuilder();
        foreach(var action in new[]{"status","refresh","inventory","download","install","cancel"})
            builder.WithHandler(WidgetMarketplaceCapabilities.Command(action),(command,_)=>{
                Commands.Enqueue(command);
                if(command.Action=="download")Snapshot=Snapshot with {Revision=2,Operation=new(new string('a',32),"download","running",Id)};
                if(command.Action=="install")Snapshot=Snapshot with {Revision=4,Items=Snapshot.Items.Select(i=>i.Id==Id?i with {InstalledVersion="2.0.0",AvailableVersionSaved=true}:i).ToArray(),
                    Operation=new(new string('a',32),"install","completed",Id,Message:"Installed disabled")};
                return ValueTask.FromResult(Snapshot);
            });
        Widget=WidgetTestHost.Attach(new MarketplaceWidget(),builder.Build());
    }
    internal ViewSnapshot View()=>Widget.RenderSnapshot("marketplace",1);
    internal async Task Start() { await WidgetTestHost.InitializeAsync(Widget);await WidgetTestHost.SetLifecycleStateAsync(Widget,WidgetLifecycleState.Interactive);
        using var deadline=new CancellationTokenSource(TimeSpan.FromSeconds(5));while(Commands.Count<2)await Task.Delay(10,deadline.Token); }
    internal ValueTask Act(string action)=>Widget.OnActionAsync(new(action,action));
    public ValueTask DisposeAsync()=>WidgetTestHost.DestroyAsync(Widget);
}
