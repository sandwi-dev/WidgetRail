using System.Threading.Channels;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

internal static class InteractivePreviewTests
{
    internal static async Task IndexedAuthority()
    {
        var widget=new Fixture();
        await WidgetTestHost.InitializeAsync(widget);
        await WidgetTestHost.SetLifecycleStateAsync(widget,WidgetLifecycleState.Interactive);
        try
        {
            using var host=WidgetTestHost.CreateIndexedCollectionHost(widget,"preview-authority");
            using var lease=await host.AcquireAsync("items",0,2);
            var row=lease.Range.Items[0];var other=lease.Range.Items[1];
            var apply=row.Root.FocusPresentation!.Children[0];var select=row.Root.FocusPresentation.Children[1];
            Check(lease.RoutePreviewAction(row.Key,apply.Id,"apply")==WidgetOperationAdmission.Enqueued,"exact captured action");
            Check(await widget.Next()=="first:0:apply","correct captured query and item");
            Check(lease.RoutePreviewAction(row.Key,other.Root.FocusPresentation!.Children[0].Id,"apply") is null,"cross-row action rejected");
            Check(lease.RoutePreviewAction(row.Key,apply.Id,"forged") is null,"undeclared action rejected");
            Check(lease.RoutePreviewAction(row.Key,select.Id,"disabled") is null,"disabled option rejected");
            Check(lease.RoutePreviewAction(row.Key,select.Id,"busy") is null,"busy option rejected");
            Check(lease.RoutePreviewAction(row.Key,select.Id,"select")==WidgetOperationAdmission.Enqueued,"declared option admitted");
            Check(await widget.Next()=="first:0:select","selector retains row identity");
            widget.Enabled=false;host.PublishSnapshot();
            Check(lease.RoutePreviewAction(row.Key,apply.Id,"apply") is null,"removing interaction opt-in revokes admission");
            widget.Enabled=true;widget.Source.PublishQuery("second",2);host.PublishSnapshot();
            Check(lease.RoutePreviewAction(row.Key,apply.Id,"apply") is null,"query retirement revokes old fragment");
            using var fresh=await host.AcquireAsync("items",0,2);var current=fresh.Range.Items[0];var currentApply=current.Root.FocusPresentation!.Children[0];
            fresh.Dispose();
            Check(fresh.RoutePreviewAction(current.Key,currentApply.Id,"apply") is null,"released lease cannot act");
        }
        finally {await WidgetTestHost.DestroyAsync(widget);}
    }
    private static void Check(bool condition,string description){if(!condition)throw new Exception(description);}
    private sealed class Fixture:Widget
    {
        internal readonly WidgetIndexedCollection<string,int> Source;
        internal bool Enabled=true;
        private readonly Channel<string> actions=Channel.CreateUnbounded<string>();
        internal Fixture()
        {
            Source=CreateIndexedCollection<string,int>("preview-source","first",2,new()
            {
                ReadRange=(_,start,count,_)=>ValueTask.FromResult<IReadOnlyList<int>>(Enumerable.Range(start,count).ToArray()),
                ItemKey=item=>new("item"+item),
                RenderItem=(_,item,context)=>UI.Button("Item "+item,"choose",context.Id("source")).PresentOnFocus(
                    UI.Stack(context.Id("preview"),UI.Button("Apply","apply",context.Id("apply")),
                        UI.Select("Choice",new SelectOption[]{new("selected","Selected","select",true),
                            new("disabled","Disabled","disabled",IsDisabled:true),new("busy","Busy","busy",IsBusy:true)},context.Id("choice")))),
                OnAction=(query,item,action,token)=>actions.Writer.WriteAsync(query+":"+item+":"+action.ActionId,token),
            });
        }
        internal Task<string> Next()=>actions.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        public override WidgetView Render()=>new(UI.FocusPresentationSurface(UI.CollectionList("items",Source,64,"Items"),UI.Text("","empty"),"surface")
            with{Preview=new(){EnterButton=Enabled?ControllerButton.X:null}},"items");
    }
}
