using System.Text;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

internal static class IndexedQueryRefreshTests
{
    internal static async Task Run()
    {
        var widget=new Fixture();await WidgetTestHost.InitializeAsync(widget);
        try
        {
            ViewSnapshot Snapshot()=>widget.Render().CreateSnapshot("refresh",1);
            var initial=widget.Source.Descriptor;
            Check(!Encoding.UTF8.GetString(SnapshotJson.Serialize(Snapshot())).Contains("refreshFromQuery"),"Ordinary query gained a new wire member");
            widget.Source.PublishRefresh("same-filter-2",999);
            var first=widget.Source.Descriptor;
            Check(first.RefreshFromQuery==initial.QueryGeneration&&first.QueryGeneration>initial.QueryGeneration&&first.Count==999,
                "Refresh did not capture its actual prior query generation");
            Check(ViewSnapshotValidator.Validate(Snapshot() with{ProtocolVersion=126}).Any(error=>error.Code=="feature_requires_version"),"Old host accepted refresh fallback semantics");
            Check(ViewSnapshotValidator.Validate(Snapshot() with{ProtocolVersion=127}).Count==0,"Valid refresh declaration rejected");
            widget.Source.PublishRefresh("same-filter-3",998);
            Check(widget.Source.Descriptor.RefreshFromQuery==initial.QueryGeneration,"Coalesced refresh chain lost its earliest admitted query");
            widget.Source.UpdateContent("same-filter-metadata");
            Check(widget.Source.Descriptor.RefreshFromQuery==initial.QueryGeneration,"Content refresh reset continuity provenance");
            widget.Source.PublishQuery("new-search",10);
            var reset=widget.Source.Descriptor;
            Check(reset.RefreshFromQuery is null,"New filter/search inherited removed-item fallback");
            widget.Source.PublishRefresh("new-search-refresh",9);
            Check(widget.Source.Descriptor.RefreshFromQuery==reset.QueryGeneration&&reset.QueryGeneration>first.QueryGeneration,
                "Refresh after a query reset reached across the old domain");
            Check(widget.Reads==0&&widget.Lookups==0,"Publishing refresh enumerated or looked up provider items");
            foreach(var invalid in new[]{widget.Source.Descriptor with{RefreshFromQuery=-1},widget.Source.Descriptor with{RefreshFromQuery=widget.Source.Descriptor.QueryGeneration}})
                try{IndexedCollectionContract.ValidateDescriptor(invalid);throw new Exception("Invalid refresh provenance accepted");}catch(ArgumentException){}
        }
        finally{await WidgetTestHost.DestroyAsync(widget);}
    }
    private sealed class Fixture:Widget
    {
        internal int Reads,Lookups;
        internal WidgetIndexedCollection<string,int> Source;
        internal Fixture()=>Source=CreateIndexedCollection<string,int>("source","same-filter-1",1000,new()
        {
            ItemKey=item=>new("item-"+item),RenderItem=(_,item,ids)=>UI.Button(item.ToString(),"select",ids.Id("row")),
            ReadRange=(_,start,count,_)=>{Reads++;return ValueTask.FromResult<IReadOnlyList<int>>(Enumerable.Range(start,count).ToArray());},
            FindIndex=(_,_,_)=>{Lookups++;return ValueTask.FromResult<int?>(null);},OnAction=(_,_,_,_)=>ValueTask.CompletedTask
        });
        public override WidgetView Render()=>new(UI.CollectionGrid("items",Source,160,100,"Items"),"items");
    }
    private static void Check(bool value,string message){if(!value)throw new Exception(message);}
}
