using System.Text;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

internal static class IndexedDirectionalEntryTests
{
    internal static async Task Run()
    {
        var widget=new Fixture();await WidgetTestHost.InitializeAsync(widget);
        try
        {
            var list=UI.CollectionList("items",widget.Source,44,"Items");
            ViewSnapshot Snapshot(IndexedCollectionElement element,long sequence)=>new WidgetView(
                UI.Stack("root",element,UI.Text(new string('x',2000),"stable")),"items").CreateSnapshot("entry-policy",sequence);
            var before=Snapshot(list,1);
            Check(!Encoding.UTF8.GetString(SnapshotJson.Serialize(before)).Contains("indexedDirectionalEntry"),"Legacy snapshots acquired a new wire member");
            Check(ViewSnapshotValidator.Validate(before with{ProtocolVersion=124}).Count==0,"Default collection lost old protocol compatibility");
            var after=Snapshot(list.DirectionalEntry(IndexedDirectionalEntryMode.VisibleEdge),2);
            Check(after.Root.Children[0].IndexedDirectionalEntry==IndexedDirectionalEntryMode.VisibleEdge,"SDK policy was not serialized");
            Check(ViewSnapshotValidator.Validate(after with{ProtocolVersion=124}).Any(error=>error.Code=="feature_requires_version"),"Old hosts were allowed to misread directional policy");
            Check(ViewSnapshotValidator.Validate(after with{ProtocolVersion=125}).Count==0,"Declared125 policy is invalid");
            Check(SnapshotJson.Deserialize(SnapshotJson.Serialize(after)).Root.Children[0].IndexedDirectionalEntry==IndexedDirectionalEntryMode.VisibleEdge,"Policy round-trip lost value");
            Check(ViewSnapshotValidator.Validate(after with{Root=after.Root with{IndexedDirectionalEntry=IndexedDirectionalEntryMode.VisibleEdge}})
                .Any(error=>error.Code=="invalid_indexed_directional_entry"),"Non-collection policy was silently ignored");
            foreach(var invalid in new[]{(IndexedDirectionalEntryMode)99})
                try{list.DirectionalEntry(invalid);throw new Exception("Unknown policy admitted");}catch(ArgumentException){}
            try{UI.CollectionGrid("grid",widget.Source,120,44,"Grid").DirectionalEntry(IndexedDirectionalEntryMode.VisibleEdge);throw new Exception("Grid policy admitted without defined semantics");}catch(ArgumentException){}
            var reset=Snapshot(list.DirectionalEntry(IndexedDirectionalEntryMode.VisibleEdge).DirectionalEntry(IndexedDirectionalEntryMode.Remembered),3);
            Check(!Encoding.UTF8.GetString(SnapshotJson.Serialize(reset)).Contains("indexedDirectionalEntry"),"Reset-to-default policy changed old wire shape");
            const string generation="aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
            var delta=WidgetPresentationDiff.Create(before,after,generation,1,PresentationUpdateCapabilities.Current,WidgetPresentationTransactionKind.IncrementalUpdate).Update;
            Check(delta is not null,"Small policy change dropped incremental update");
            var received=PresentationUpdateMaterializer.Apply(before,delta!,generation);
            Check(received.Root.Children[0].IndexedDirectionalEntry==IndexedDirectionalEntryMode.VisibleEdge,"Incremental publication lost opt-in policy");
            Check(widget.Reads==0,"Declaring directional policy realized remote rows");
        }
        finally{await WidgetTestHost.DestroyAsync(widget);}
    }
    private sealed class Fixture:Widget
    {
        internal int Reads;
        internal WidgetIndexedCollection<string,int> Source;
        internal Fixture()=>Source=CreateIndexedCollection<string,int>("source","query",20,new()
        {
            ItemKey=item=>new("item-"+item),RenderItem=(_,item,ids)=>UI.Button(item.ToString(),"select",ids.Id("row")),
            OnAction=(_,_,_,_)=>ValueTask.CompletedTask,
            ReadRange=(_,start,count,_)=>{Reads++;return ValueTask.FromResult<IReadOnlyList<int>>(Enumerable.Range(start,count).ToArray());}
        });
        public override WidgetView Render()=>new(UI.CollectionList("items",Source,44,"Items"),"items");
    }
    private static void Check(bool value,string reason){if(!value)throw new Exception(reason);}
}
