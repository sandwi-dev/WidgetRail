using System.Text.Json;
using System.Globalization;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

namespace WidgetRail.Samples.PobCompanion;

public sealed partial class PobWidget
{
    private JsonElement timelessState;
    private (TimelessRow Row,WidgetElement[] Lines)? timelessDetail;
    private sealed record TimelessRow(int Id,int Seed,int Socket,string Title,string Description);
    private sealed record TimelessQuery(TimelessRow[] Rows);
    private TimelessQuery timelessQuery=new([]);
    private TimelessRow[] timelessRows=[];
    private WidgetIndexedCollection<TimelessQuery,TimelessRow>? timelessCollection;
    private bool HandleTimeless(WidgetActionEvent action)
    {
        var id=action.ActionId;var parts=id.Split('.');
        if(id=="timeless-open"&&Capabilities.TimelessFinder&&pendingBefore is null)
        {
            Work(async token=>
            {
                var reply=await engine.ExecuteAsync(new{op="timeless-catalog"},token,result!.Xml);
                var rows=await ReadTimelessRowsAsync(reply.State,reply.Xml,token);
                lock(gate){timelessRows=rows;timelessState=reply.State;detailKind="timeless";focusRequestSequence++;}
            },"Reading Timeless Jewel finder…");return true;
        }
        if(parts is ["timeless-fallback-choice",var fallbackValue]&&int.TryParse(fallbackValue,out var fallbackOption)&&A(timelessState,"settings").Any(s=>S(s,"key")=="nodeSelect"&&A(s,"options").Any(o=>I(o,"id")==fallbackOption)))
        {featurePicker=null;detailKind="timeless";Edit(new{op="timeless-edit",control="nodeSelect",value=fallbackOption,fallback=true});return true;}
        if(parts is ["timeless-choice",var key,var value]&&int.TryParse(value,out var option)&&
            A(timelessState,"settings").Any(s=>S(s,"key")==key&&A(s,"options").Any(o=>I(o,"id")==option)))
        {featurePicker=null;detailKind="timeless";Edit(new{op="timeless-edit",control=key,value=option});return true;}
        if(detailKind!="timeless")return false;
        if(id=="back"){detailKind="";focusRequestSequence++;return true;}
        if(id=="timeless-fallback-picker")
        {var setting=A(timelessState,"settings").First(s=>S(s,"key")=="nodeSelect");OpenFeaturePicker("Add fallback modifier","timeless-fallback-choice.",A(setting,"options"));return true;}
        if(parts is ["timeless-picker",var control])
        {
            var setting=A(timelessState,"settings").FirstOrDefault(s=>S(s,"key")==control);
            if(setting.ValueKind==JsonValueKind.Object)OpenFeaturePicker(S(setting,"title"),"timeless-choice."+control+".",A(setting,"options"));
            return true;
        }
        if(parts is ["timeless-weight",var weight]&&weight is "primary" or "secondary" or "minimum")
        {
            if(!double.TryParse(action.CommittedText,NumberStyles.Float,CultureInfo.InvariantCulture,out var amount)||!double.IsFinite(amount)||amount<0||amount>(weight=="minimum"?500:10))
            {ShowFeedback("Invalid weight",weight=="minimum"?"Use 0–499, or 500 to require this modifier.":"Use a weight from 0 to 10.",ToastTone.Warning);return true;}
            Edit(new{op="timeless-edit",weight,modifier=I(timelessState,"modifier"),value=amount,fallback=B(timelessState,"editingFallback")});return true;
        }
        if(parts is ["timeless-clear",var cleared]&&cleared is "searchList" or "searchListFallback")
        {Edit(new{op="timeless-edit",field=cleared,text=""});return true;}
        if(id=="timeless-search"){Edit(new{op="timeless-edit",search=true});return true;}
        if(id=="timeless-filter"){Edit(new{op="timeless-edit",filter=!B(timelessState,"filter")});return true;}
        if(parts is ["timeless-text",var field]&&field is "searchList" or "searchListFallback")
        {Edit(new{op="timeless-edit",field,text=action.CommittedText??""});return true;}
        if(parts is ["timeless-inspect",var rowIndex]&&int.TryParse(rowIndex,out var rowId)&&timelessQuery.Rows.FirstOrDefault(r=>r.Id==rowId)is {} inspected)
        {
            Work(async token=>
            {
                var reply=await engine.ExecuteAsync(new{op="timeless-detail",id=rowId,version=I(timelessState,"resultVersion")},token,result!.Xml);
                var lines=new List<WidgetElement>();foreach(var (line,i) in A(reply.State,"lines").Select((l,i)=>(l,i)))AddFormatted(lines,line,"text","runs","timeless.detail."+i,emphasizeStats:true);
                lock(gate)timelessDetail=(inspected,lines.ToArray());
            },"Reading jewel effects…");return true;
        }
        if(parts is ["timeless-add",var index]&&int.TryParse(index,out var selected)&&timelessQuery.Rows.FirstOrDefault(r=>r.Id==selected)is {} row)
        {Edit(new{op="timeless-edit",addSeed=row.Seed,socket=row.Socket});return true;}
        return false;
    }
    private async Task<TimelessRow[]> ReadTimelessRowsAsync(JsonElement state,string xml,CancellationToken token)
    {
        var count=I(state,"resultCount");var rows=new List<TimelessRow>();
        for(var start=0;start<count;start+=1000)
        {
            var response=await engine.ExecuteAsync(new{op="timeless-range",version=I(state,"resultVersion"),start,count=Math.Min(1000,count-start)},token,xml);
            var page=A(response.State,"results");
            if(page.Length!=Math.Min(1000,count-start))throw new IOException("Incomplete jewel results. Search again.");
            rows.AddRange(page.Select(r=>new TimelessRow(I(r,"id"),I(r,"seed"),I(r,"socket"),"Seed "+I(r,"seed")+" · Score "+r.GetProperty("score").GetDouble().ToString("0.###",CultureInfo.CurrentCulture),S(r,"socketLabel"))));
        }
        return rows.ToArray();
    }
    private void AddTimelessText(List<WidgetElement> rows,string title,string property,string field,string id)
    {
        var text=S(timelessState,property);
        if(text.Length<=2048)rows.Add(Field(title,UI.TextEntry(text,"Choose modifiers above; optionally edit the list","timeless-text."+field,"pob.timeless."+id,2048).Classes("pob-input"),"timeless."+id));
        else{rows.Add(Text(title,"timeless."+id+".title","pob-eyebrow"));AddReading(rows,text,"timeless."+id+".text");}
        if(text.Length>0)rows.Add(Button("Clear "+title.ToLowerInvariant(),"timeless-clear."+field));
    }
    private WidgetElement TimelessWorkspace()
    {
        var controls=new List<WidgetElement>{Text("Timeless Jewel finder","timeless.title","pob-heading"),
            Text("Choose a socket and desired modifiers. PoB searches seeds and scores their effects. Adding a result puts a planning item in your inventory; it does not equip or buy it.","timeless.help","pob-small pob-muted")};
        foreach(var setting in A(timelessState,"settings"))
        {
            var key=S(setting,"key");var options=A(setting,"options");
            if(options.Length<=12&&key!="nodeSelect")controls.Add(UI.Select(S(setting,"title"),options.Select(o=>new SelectOption("option"+I(o,"id"),S(o,"title"),"timeless-choice."+key+"."+I(o,"id"),B(o,"active"))).ToArray(),"pob.timeless."+key).Classes("pob-select"));
            else controls.Add(Button(S(setting,"title")+": "+S(options.FirstOrDefault(o=>B(o,"active")),"title","Choose…"),"timeless-picker."+key));
        }
        controls.Add(Button("Add fallback modifier…","timeless-fallback-picker"));
        controls.Add(UI.Switch("Only score allocated nodes",B(timelessState,"filter"),"timeless-filter","pob.timeless.filter").Classes("pob-toggle"));
        if(I(timelessState,"modifier")>1)
        {
            controls.Add(Text(B(timelessState,"editingFallback")?"Fallback modifier weights":"Desired modifier weights","timeless.weights.title","pob-eyebrow"));
            foreach(var key in new[]{"primary","secondary","minimum"})
            {
                if(key=="secondary"&&!B(timelessState,"secondaryEnabled"))continue;
                controls.Add(Field(key=="minimum"?"Minimum weight (500 = required)":key=="primary"?"Primary stat weight":"Secondary stat weight",
                    UI.TextEntry(S(timelessState,key),"Weight","timeless-weight."+key,"pob.timeless.weight."+key,16).Classes("pob-input"),"timeless.weight."+key));
            }
        }
        AddTimelessText(controls,"Desired modifiers and weights","searchList","searchList","desired");
        AddTimelessText(controls,"Fallback modifiers","fallback","searchListFallback","fallback");
        var rows=timelessRows;
        timelessCollection??=CreateIndexedCollection<TimelessQuery,TimelessRow>("pob.timeless.source",timelessQuery,0,new()
        {
            ReadRange=(q,start,count,token)=>ValueTask.FromResult<IReadOnlyList<TimelessRow>>(q.Rows.Skip(start).Take(count).ToArray()),ItemKey=r=>new(r.Id.ToString()),
            RenderItem=(_,r,c)=>UI.ActionSurface("inspect",c.Id("row"),"Inspect "+r.Title,ActionSurfaceOrientation.Vertical,
                UI.Text(r.Title,c.Id("title")).Classes("pob-list-title"),UI.Text(r.Description+" · Inspect effects",c.Id("description")).Classes("pob-small")).FocusLeft("pob.timeless.controls").Classes("pob-list-item"),
            OnAction=(q,r,a,token)=>{lock(gate){if(!ReferenceEquals(q,timelessQuery)||busy||a.ActionId!="inspect")return ValueTask.CompletedTask;return OnActionAsync(a with{ActionId="timeless-inspect."+r.Id},token);}}
        });
        if(!timelessQuery.Rows.SequenceEqual(rows)){timelessQuery=new(rows);timelessCollection.PublishQuery(timelessQuery,rows.Length);}
        return Split("timeless",QuickControls(UI.Stack("pob.timeless.controls",UI.Row("pob.timeless.actions",Button("Back to tree","back"),Button("Search jewel seeds","timeless-search")).Classes("pob-toolbar"),UI.VerticalScroll("pob.timeless.settings",controls.ToArray()).RememberChildFocus("pob.timeless.jewelSelect").Classes("pob-scroll")).Classes("pob-panel","pob-workspace"),"pob.timeless-search","pob.timeless.list-host","Results",NeedsQuickControls(rows.Length)),
            UI.Stack("pob.timeless.results",Text(rows.Length+" matching seeds","timeless.count","pob-heading"),
                Text("Results use PoB’s selected modifier weights, not total build DPS.","timeless.result-help","pob-small pob-muted"),
                QuickControls(UI.Stack("pob.timeless.list-host",UI.CollectionList("pob.timeless.list",timelessCollection,88,"Timeless Jewel results").Classes("pob-scroll")).Classes("pob-workspace"),"pob.timeless.list","pob.timeless.controls","Finder controls",NeedsQuickControls(rows.Length))).Classes("pob-panel","pob-workspace"),1,1);
    }
}
