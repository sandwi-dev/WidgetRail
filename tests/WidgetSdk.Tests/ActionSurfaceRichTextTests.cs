using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

internal static class ActionSurfaceRichTextTests
{
    private static WidgetElement Rich(string id,int count)=>UI.RichText(id,
        Enumerable.Range(0,count).Select(i=>(WidgetElement)UI.Text(i.ToString(),id+".run"+i).Classes(i%2==0?"accent":"muted")).ToArray());
    private static ActionSurfaceElement Surface(params WidgetElement[] children)=>
        UI.ActionSurface("read","surface","Read explanation",ActionSurfaceOrientation.Vertical,children);
    private static ViewSnapshot Snapshot(WidgetElement root)=>new WidgetView(root).CreateSnapshot("rich-action",1);
    private static void Check(bool value,string message){if(!value)throw new InvalidOperationException(message);}
    private static void Reject(Action action){try{action();}catch(ArgumentException){return;}throw new InvalidOperationException("Expected rejected surface");}

    internal static async Task Run()
    {
        var small=Snapshot(Surface(Rich("small",4)));
        Check(small.ProtocolVersion==ProtocolConstants.RichTextVersion,"Existing small cards retain their protocol requirement");
        var dense=Snapshot(Surface(Rich("dense",128)));
        Check(dense.ProtocolVersion==ProtocolConstants.ActionSurfaceRichTextBudgetVersion,"Dense cards declare the expanded protocol");
        Check(ViewSnapshotValidator.Validate(dense).Count==0,"Dense rich text is accepted on the wire");
        Check(SnapshotJson.Deserialize(SnapshotJson.Serialize(dense)).Root.Children[0].Children.Count==128,"Wire roundtrip preserves every span");
        Check(ViewSnapshotValidator.Validate(dense with{ProtocolVersion=86}).Any(e=>e.Code=="feature_requires_version"),"Old protocol cannot silently admit expanded rich text");
        var maximum=Snapshot(Surface(Rich("maximum",ProtocolConstants.MaximumActionSurfaceRichTextSpans)));
        Check(ViewSnapshotValidator.Validate(maximum).Count==0,"The shared span boundary is inclusive");
        Reject(()=>Surface(Rich("first",256),Rich("second",257)));
        var overflow=dense with{Root=dense.Root with{Children=[maximum.Root.Children[0],small.Root.Children[0]]}};
        Check(ViewSnapshotValidator.Validate(overflow).Any(e=>e.Code=="action_surface_too_many_rich_text_spans"),"Span budget is aggregate across paragraphs");
        var nested=Rich("deep-copy",5);
        for(var depth=0;depth<3;depth++)nested=UI.Stack("layout"+depth,nested);
        var boundary=Snapshot(Surface(nested));
        Check(boundary.ProtocolVersion==87&&ViewSnapshotValidator.Validate(boundary).Count==0,"Inline spans do not add layout depth");
        Reject(()=>Surface(UI.Stack("too-deep",nested)));
        Reject(()=>Surface(UI.Stack("too-wide",Enumerable.Range(0,32).Select(i=>UI.Text("x","layout-leaf"+i)).ToArray())));
        var text=dense.Root.Children[0].Children[0] with{ActionId="nested-action"};
        var interactive=dense with{Root=dense.Root with{Children=[dense.Root.Children[0] with{Children=[text]}]}};
        Check(ViewSnapshotValidator.Validate(interactive).Any(e=>e.Code=="interactive_action_surface_descendant"),"Inline accounting never admits nested interaction");
        var invalid=dense with{Root=dense.Root with{Children=[dense.Root.Children[0] with{Children=[text with{ActionId=null,Children=[small.Root.Children[0]]}]}]}};
        Check(ViewSnapshotValidator.Validate(invalid).Any(e=>e.Code=="invalid_rich_text"),"Nested layout cannot masquerade as an inline span");

        var widget=new IndexedFixture();
        await WidgetTestHost.InitializeAsync(widget);
        await WidgetTestHost.SetLifecycleStateAsync(widget,WidgetLifecycleState.Visible);
        try
        {
            using var host=WidgetTestHost.CreateIndexedCollectionHost(widget,"rich-indexed");
            Check(host.CurrentSnapshot.ProtocolVersion==ProtocolConstants.CurrentVersion,"Deferred templates advertise current protocol");
            using var lease=await host.AcquireAsync("items",0,1);
            Check(lease.Range.Items.Single().Root.Children.Single().Children.Count==128,"Virtualized rows retain all rich-text spans");
        }
        finally{await WidgetTestHost.DestroyAsync(widget);}
    }

    private sealed class IndexedFixture:Widget
    {
        private readonly WidgetIndexedCollection<string,string> source;
        public IndexedFixture()=>source=CreateIndexedCollection<string,string>("source","query",1,new()
        {
            ReadRange=(_,_,_,_)=>ValueTask.FromResult<IReadOnlyList<string>>(["formula"]),
            ItemKey=value=>new(value),
            RenderItem=(_,_,context)=>UI.ActionSurface("read",context.Id("row"),"Read formula",ActionSurfaceOrientation.Vertical,
                UI.RichText(context.Id("copy"),Enumerable.Range(0,128).Select(i=>(WidgetElement)UI.Text(i.ToString(),context.Id("run"+i))).ToArray())),
            OnAction=(_,_,_,_)=>ValueTask.CompletedTask
        });
        public override WidgetView Render()=>new(UI.Stack("root",UI.CollectionList("items",source,100,"Items")),"items");
    }
}
