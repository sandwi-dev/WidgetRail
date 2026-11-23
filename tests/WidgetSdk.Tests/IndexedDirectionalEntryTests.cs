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
            Check(!Encoding.UTF8.GetString(SnapshotJson.Serialize(before)).Contains("directionalAnchor"), "Default anchor changed legacy wire shape");
            var anchored = Snapshot(list.WithDirectionalAnchor(FocusAnchorPoint.Center, 45), 2);
            var anchor = new DirectionalScrollAnchor(FocusAnchorPoint.Center, 45);
            Check(ViewSnapshotValidator.Validate(anchored).Count == 0 &&
                SnapshotJson.Deserialize(SnapshotJson.Serialize(anchored)).Root.Children[0].DirectionalAnchor == anchor, "Scroll anchor roundtrip failed");
            Check(ViewSnapshotValidator.Validate(anchored with { ProtocolVersion = 142 }).Any(error => error.Code == "feature_requires_version"),
                "Anchor was admitted by an older protocol");
            var scroll = UI.HorizontalScroll("rail", UI.Button("One", "open", "card"))
                .WithDirectionalAnchor(FocusAnchorPoint.Leading, 10);
            var shelf = new WidgetView(scroll, "card").CreateSnapshot("shelf", 1);
            Check(shelf.Root.DirectionalAnchor == new DirectionalScrollAnchor(FocusAnchorPoint.Leading, 10) &&
                ViewSnapshotValidator.Validate(shelf).Count == 0, "Ordinary scroll lost its anchor");
            Check(ViewSnapshotValidator.Validate(shelf with { ProtocolVersion = 144 }).Any(error => error.Code == "feature_requires_version"),
                "Ordinary scroll anchor was admitted before its host implementation");
            Check(ViewSnapshotValidator.Validate(before with { Root = before.Root with { DirectionalAnchor = anchor } })
                .Any(error => error.Code == "invalid_directional_anchor"), "Anchor on a stack was admitted");
            foreach (var invalid in new[] { anchor with { ViewportPercent = -1 }, anchor with { ViewportPercent = 101 }, anchor with { ItemPoint = (FocusAnchorPoint)99 } })
            {
                try { list.WithDirectionalAnchor(invalid.ItemPoint, invalid.ViewportPercent); throw new Exception("Invalid anchor admitted"); }
                catch (ArgumentException) { }
            }
            var exits=new WidgetView(UI.Stack("exit-root",list.FocusLeft("exit"),UI.Button("Exit","exit","exit"))).CreateSnapshot("entry-policy",1);
            Check(ViewSnapshotValidator.Validate(exits).Count==0 && SnapshotJson.Deserialize(SnapshotJson.Serialize(exits)).Root.Children[0].Focus?.Left=="exit",
                "Collection exit is validated and transported without realizing or rewriting rows");
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
            var anchorDelta = WidgetPresentationDiff.Create(before, anchored, generation, 1, PresentationUpdateCapabilities.Current,
                WidgetPresentationTransactionKind.IncrementalUpdate).Update;
            Check(anchorDelta is not null && PresentationUpdateMaterializer.Apply(before, anchorDelta, generation).Root.Children[0].DirectionalAnchor == anchor,
                "Incremental update lost anchor");
            var delta=WidgetPresentationDiff.Create(before,after,generation,1,PresentationUpdateCapabilities.Current,WidgetPresentationTransactionKind.IncrementalUpdate).Update;
            Check(delta is not null,"Small policy change dropped incremental update");
            var received=PresentationUpdateMaterializer.Apply(before,delta!,generation);
            Check(received.Root.Children[0].IndexedDirectionalEntry==IndexedDirectionalEntryMode.VisibleEdge,"Incremental publication lost opt-in policy");
            Check(widget.Reads==0,"Declaring directional policy realized remote rows");
            var preferred=Snapshot(list.PreferredEntry(new("item-15")),4);
            Check(preferred.Root.Children[0].IndexedPreferredEntryKey=="item-15" && widget.Reads==0,"Preferred entry must remain declarative and lazy");
            Check(ViewSnapshotValidator.Validate(preferred with{ProtocolVersion=129}).Any(error=>error.Code=="feature_requires_version"),"Preferred entry must reject old hosts");
            Check(ViewSnapshotValidator.Validate(preferred).Count==0 && SnapshotJson.Deserialize(SnapshotJson.Serialize(preferred)).Root.Children[0].IndexedPreferredEntryKey=="item-15","Preferred entry round-trip failed");
            var preferredDelta=WidgetPresentationDiff.Create(before,preferred,generation,1,PresentationUpdateCapabilities.Current,WidgetPresentationTransactionKind.IncrementalUpdate).Update;
            Check(preferredDelta is not null && PresentationUpdateMaterializer.Apply(before,preferredDelta,generation).Root.Children[0].IndexedPreferredEntryKey=="item-15","Preferred entry delta lost its target");
            Check(Snapshot(list.PreferredEntry(new("item-15")).PreferredEntry(null),5).Root.Children[0].IndexedPreferredEntryKey is null,"Preferred entry cannot be withdrawn");
            Check(Snapshot(list.PreferredEntry(new("item-15")).DirectionalEntry(IndexedDirectionalEntryMode.VisibleEdge),6).Root.Children[0].IndexedPreferredEntryKey is null,"Entry modes must not conflict");
            foreach(var invalidNode in new[]{
                preferred.Root.Children[0] with{IndexedPreferredEntryKey="bad key"},
                preferred.Root.Children[0] with{IndexedDirectionalEntry=IndexedDirectionalEntryMode.VisibleEdge},
                preferred.Root.Children[0] with{IndexedCollection=preferred.Root.Children[0].IndexedCollection! with{CanLocateItems=false}}})
                Check(ViewSnapshotValidator.Validate(preferred with{Root=preferred.Root with{Children=[invalidNode]}}).Any(error=>error.Code=="invalid_indexed_preferred_entry"),"Invalid preferred target admitted");
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
            ReadRange=(_,start,count,_)=>{Reads++;return ValueTask.FromResult<IReadOnlyList<int>>(Enumerable.Range(start,count).ToArray());},
            FindIndex=(_,key,_)=>ValueTask.FromResult<int?>(int.TryParse(key.Value.AsSpan(5),out var index)&&index is >=0 and <20?index:null)
        });
        public override WidgetView Render()=>new(UI.CollectionList("items",Source,44,"Items"),"items");
    }
    private static void Check(bool value,string reason){if(!value)throw new Exception(reason);}
}
