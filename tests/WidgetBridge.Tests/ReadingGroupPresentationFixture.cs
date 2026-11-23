using System.Text.Json;
using WidgetRail.WidgetBridge;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;
using WidgetRail.WidgetStyling;
using WidgetRail.WidgetPresentationSession;

internal static class ReadingGroupPresentationFixture
{
    internal static async Task ContractAsync()
    {
        var probe=WidgetTestHost.Attach(new Probe(),new WidgetTestHostServicesBuilder().Build());
        await WidgetTestHost.InitializeAsync(probe);
        await WidgetTestHost.SetLifecycleStateAsync(probe,WidgetLifecycleState.Interactive);
        try
        {
            using var host=WidgetTestHost.CreateIndexedCollectionHost(probe,"reading-fixture");
            using var range=await host.AcquireAsync("reading.list",0,2);
            if(range.Range.Items.Count!=2)throw new InvalidOperationException("Reading fixture did not render both rows.");
        }
        finally { await WidgetTestHost.DestroyAsync(probe); }
        var pipe="reading-contract-"+Guid.NewGuid().ToString("N");
        var serving=ServeAsync(pipe);
        using var deadline=new CancellationTokenSource(TimeSpan.FromSeconds(20));
        await using(var session=await WidgetPresentationSession.ConnectAsync(pipe,cancellationToken:deadline.Token))
        {
            await session.ListWidgetsAsync(deadline.Token);
            var frame=await session.EstablishPresentationAsync(session.GetTarget("reading-fixture"),WidgetLifecycleState.Interactive,deadline.Token);
            var source=frame.Snapshot.Root.Children.Single(node=>node.Id=="reading.list").IndexedCollection!;
            await using var lease=await session.AcquireIndexedRangeAsync(frame.Authority,"reading.list",source,0,2,cancellationToken:deadline.Token);
            if(!lease.IsCurrent || lease.Range.Items.Count!=2 || lease.Range.Items.Any(item=>!item.Root.IsReadingGroup))
                throw new InvalidOperationException("Reading rows did not survive real worker admission.");
        }
        await serving.WaitAsync(TimeSpan.FromSeconds(5));
    }

    internal static async Task ServeAsync(string pipe)
    {
        var styles=WrssParser.Parse(".probe-root{width:100%;height:100%;gap:8px;} .probe-list{width:100%;min-height:0px;flex-grow:1;} "+
            ".probe-message{padding:12px;gap:10px;min-height:100px;background:var(--surface-raised);} .probe-actions{gap:12px;} "+
            ".probe-control{padding:8px;min-height:38px;} .probe-copy{font-size:15px;}","reading-fixture.wrss").Document;
        var widget=new ConfiguredWidget{Id="reading-fixture",Name="Reading group fixture",InstanceId="reading-fixture.instance",
            PackageId="dev.reading",PublisherId="dev",WorkerExecutable=Environment.ProcessPath!,
            WorkerFingerprint=new('b',64),CatalogFingerprint=new('b',64),StylePackage=new([styles],[]),
            Intents=new(){Requests=[WidgetIntentContracts.Web]}};
        await using var server=new WidgetBridgeServer(pipe,new([widget]));
        await server.RunAsync(TimeSpan.FromSeconds(30),CancellationToken.None);
    }
    internal sealed class Probe:Widget
    {
        private sealed record State(int Calls=0,string Last="None");
        private readonly WidgetModel<State> state;
        private readonly WidgetIndexedCollection<bool,int> rows;
        private readonly WidgetNavigator<string> navigation;
        internal Probe()
        {
            state=CreateModel(new State());
            navigation=CreateNavigatorWithOptions("reading.navigation","home",new WidgetNavigatorOptions<string>
                {RouteScopeIds=new Dictionary<string,string>{{"home","reading.scope"},{"modal","reading.modal.scope"}}});
            rows=CreateIndexedCollection<bool,int>("reading.source",false,12,new()
            {
                EnableSelection=true,ItemKey=item=>new("message."+item),
                ReadRange=(_,start,count,_)=>ValueTask.FromResult<IReadOnlyList<int>>(Enumerable.Range(start,count).ToArray()),
                RenderItem=(disabled,item,ids)=>item is > 0 and < 6
                    ? UI.ReadingGroup(ids.Id("row"),"Message "+item,ActionSurfaceOrientation.Vertical,
                        UI.Text("Synthetic message "+item,ids.Id("text"))).Classes("probe-message")
                    : UI.ReadingGroup(ids.Id("row"),"Message "+item,ActionSurfaceOrientation.Vertical,
                    UI.Text("Synthetic message "+item,ids.Id("text")).Classes("probe-copy"),
                    UI.Row(ids.Id("actions"),UI.Button("Open attachment","open",ids.Id("open")).Classes("probe-control"),
                        UI.ActionSurface("secondary",ids.Id("secondary"),"Second action",ActionSurfaceOrientation.Horizontal,
                            UI.Icon(WidgetGlyph.Attach,ids.Id("secondary-icon"),"Attachment"),
                            UI.Text("Second action",ids.Id("secondary-text")))
                            .Disabled(disabled).Classes("probe-control"),
                        UI.Button("Unavailable","disabled",ids.Id("disabled")).Disabled().Classes("probe-control")).Classes("probe-actions"),
                    UI.RichText(ids.Id("paragraph"),UI.Text("Read ",ids.Id("lead")),
                        UI.Button("example link","web",ids.Id("link")).OpenIntent(WidgetIntentContracts.Web,JsonSerializer.SerializeToElement(new{url="https://example.invalid/reading"}))))
                    .Classes("probe-message"),
                OnAction=(_,item,action,_)=>
                {
                    if(action.ActionId is not ("open" or "secondary"))throw new InvalidOperationException("Unexpected inline dispatch: "+action.ActionId);
                    state.Update(value=>value with{Calls=value.Calls+1,Last=action.ActionId+":"+item});
                    if(action.ActionId=="open")navigation.PushFromCollectionAction("modal",action,rows!.Descriptor);
                    return ValueTask.CompletedTask;
                }
            });
        }
        public override ValueTask OnActionAsync(WidgetActionEvent action,CancellationToken token=default)
        {
            if(navigation.TryHandleBack(action,action.FocusedElementId))return ValueTask.CompletedTask;
            if(action.ActionId=="refresh")rows.UpdateContent(true);
            if(action.ActionId=="selection"){if(rows.Selection!.IsActive)rows.EndSelection();else rows.BeginSelection();}
            return ValueTask.CompletedTask;
        }
        public override WidgetView Render()
        {
            var visit=navigation.Value;var data=state.Value;
            var result=new WidgetView(UI.Stack("reading.root",
                UI.Row("reading.tools",UI.Button("Refresh rows","refresh","reading.refresh"),UI.Button("Selection","selection","reading.selection")),
                UI.Text(data.Calls+" / "+data.Last,"reading.status"),
                UI.Text("Selected: "+rows.Selection!.Count,"reading.selection.status"),
                UI.CollectionList("reading.list",rows,120,"Messages").Classes("probe-list")).InputScope("reading.scope").Classes("probe-root"),
                "reading.list",ActiveInputScopeId:"reading.scope")
                {FocusGroupEntryRequest=visit.CollectionReturnFocus?.Resolve("reading.list",rows.Descriptor)};
            return visit.Route!="modal"?result:result.WithModal(new("reading.modal","Attachment",
                UI.ReadingSurface("reading.modal.content","Synthetic attachment",ActionSurfaceOrientation.Vertical,UI.Text("Synthetic attachment; no remote media","reading.modal.copy")),
                "reading.modal.content",visit.BackActionId!){HeaderActions=UI.ControllerHint(ControllerButton.B,"Back","reading.modal.back")});
        }
    }
}
