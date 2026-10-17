using System.Text.Json;
using WidgetRail.WidgetSdk;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.Samples.PobCompanion;

public sealed partial class PobWidget
{
    private JsonElement calculationCatalog,calculationDetail;
    private string calculationSection="",calculationCell="",calculationSearch="";
    private sealed record CalculationSelection(int Group=0,int Skill=0,string Mode="EFFECTIVE",bool Minion=false)
    {
        internal object Request()=>new{group=Group,skill=Skill,mode=Mode,minion=Minion};
    }
    private CalculationSelection calculationSelection=new();
    private static readonly string[] CalculationModes=["EFFECTIVE","COMBAT","BUFFED","UNBUFFED"];
    private sealed record CalculationView(JsonElement Catalog,JsonElement Detail,string Section,string Cell,CalculationSelection Selection);
    private sealed record CalculationCell(string Id,string Title,string Value,bool Selected);
    private sealed record CalculationQuery(CalculationCell[] Items);
    private CalculationQuery calculationQuery=new([]);
    private WidgetIndexedCollection<CalculationQuery,CalculationCell>? calculationCollection;
    private sealed record CalculationSource(string Key,string Title,string Summary,string[] Labels,JsonElement[] Cells);
    private sealed record CalculationSources(CalculationSource[] Items);
    private CalculationSources calculationSources=new([]);
    private WidgetIndexedCollection<CalculationSources,CalculationSource>? calculationSourceCollection;

    private async Task<CalculationView> ReadCalculationsAsync(string xml,CancellationToken token,string? stat=null,CalculationSelection? selection=null)
    {
        selection??=calculationSelection;
        var context=selection.Request();
        var catalog=(await engine.ExecuteAsync(new{op="calculation-catalog",context},token,xml)).State;
        var sections=A(catalog,"sections");
        var matching=stat is null?default:sections.FirstOrDefault(s=>A(s,"items").Any(i=>A(i,"outputs").Any(o=>o.GetString()==stat)));
        var selected=matching.ValueKind==JsonValueKind.Object?matching:sections.FirstOrDefault(s=>S(s,"id")==calculationSection);
        if(selected.ValueKind!=JsonValueKind.Object)selected=sections.FirstOrDefault();
        var cells=A(selected,"items");
        var cell=stat is null?cells.FirstOrDefault(c=>S(c,"id")==calculationCell):cells.FirstOrDefault(c=>A(c,"outputs").Any(o=>o.GetString()==stat));
        if(cell.ValueKind!=JsonValueKind.Object)cell=cells.FirstOrDefault();
        var details=cell.ValueKind==JsonValueKind.Object?(await engine.ExecuteAsync(new{op="calculation-detail",id=S(cell,"id"),context},token,xml)).State:default;
        return new(catalog,details,S(selected,"id"),S(cell,"id"),selection);
    }

    private void PublishCalculations(CalculationView view)
    {
        calculationSelection=view.Selection;calculationCatalog=view.Catalog;calculationDetail=view.Detail;calculationSection=view.Section;calculationCell=view.Cell;
        var rows=new List<CalculationSource>();
        foreach(var section in A(calculationDetail,"sections"))
        {
            var heading=S(section,"title");
            var columns=A(section,"columns").Select(c=>c.GetString()??"Value").ToArray();
            foreach(var row in A(section,"rows"))
            {
                var cells=A(row,"cells");
                var summary=string.Join(" · ",cells.Select((cell,i)=>(i<columns.Length?columns[i]+": ":"")+S(cell,"text")).Where(s=>!s.EndsWith(": ",StringComparison.Ordinal)));
                rows.Add(new(rows.Count.ToString(),heading.Length>0?heading:"Details",summary,columns,cells));
            }
        }
        calculationSources=new(rows.ToArray());calculationSourceCollection?.PublishQuery(calculationSources,rows.Count);
    }

    private void OpenCalculations(string? stat=null,CalculationSelection? selection=null)=>Work(async token=>
    {
        var view=await ReadCalculationsAsync(result!.Xml,token,stat,selection);
        lock(gate){PublishCalculations(view);detailKind="calculations";calculationSearch="";focusRequestSequence++;}
    },"Reading detailed calculations…");

    private bool HandleCalculationAction(WidgetActionEvent action)
    {
        var id=action.ActionId;
        if(id=="calculations"){OpenCalculations(detailKind=="stat"?statDetail:null,new());return true;}
        if(detailKind!="calculations")return false;
        if(id.StartsWith("calculation-group.",StringComparison.Ordinal)&&int.TryParse(id[18..],out var group)&&
            (group==0||A(State,"skills").Any(g=>I(g,"id")==group)))
        {OpenCalculations(CurrentCalculationStat(),calculationSelection with{Group=group,Skill=0,Minion=false});return true;}
        if(id.StartsWith("calculation-skill.",StringComparison.Ordinal)&&int.TryParse(id[18..],out var skill)&&calculationSelection.Group!=0&&
            A(calculationCatalog.GetProperty("context"),"skills").Any(g=>I(g,"id")==skill))
        {OpenCalculations(CurrentCalculationStat(),calculationSelection with{Skill=skill});return true;}
        if(id.StartsWith("calculation-mode.",StringComparison.Ordinal)&&CalculationModes.Contains(id[17..]))
        {OpenCalculations(CurrentCalculationStat(),calculationSelection with{Mode=id[17..]});return true;}
        if(id is "calculation-actor.player" or "calculation-actor.minion")
        {
            var minion=id.EndsWith("minion",StringComparison.Ordinal);
            if(!minion||B(calculationCatalog.GetProperty("context"),"hasMinion"))OpenCalculations(CurrentCalculationStat(),calculationSelection with{Minion=minion});
            return true;
        }
        if(id=="back"){detailKind="";focusRequestSequence++;return true;}
        if(id=="calculation-search"){calculationSearch=action.CommittedText??"";return true;}
        if(id.StartsWith("calculation-section.",StringComparison.Ordinal))
        {
            var section=id["calculation-section.".Length..];
            if(A(calculationCatalog,"sections").Any(s=>S(s,"id")==section))
            {calculationSection=section;calculationCell="";calculationSearch="";OpenCalculations();}
            return true;
        }
        if(id.StartsWith("calculation-cell.",StringComparison.Ordinal))
        {
            var cell=id["calculation-cell.".Length..];
            if(A(calculationCatalog,"sections").SelectMany(s=>A(s,"items")).Any(c=>S(c,"id")==cell))
                Work(async token=>
                {
                    var response=await engine.ExecuteAsync(new{op="calculation-detail",id=cell,context=calculationSelection.Request()},token,result!.Xml);
                    lock(gate)PublishCalculations(new(calculationCatalog,response.State,calculationSection,cell,calculationSelection));
                },"Reading calculation sources…");
            return true;
        }
        if(id.StartsWith("calculation-source.",StringComparison.Ordinal)&&int.TryParse(id["calculation-source.".Length..],out var index)&&index>=0&&index<calculationSources.Items.Length)
        {
            var source=calculationSources.Items[index];var rows=new List<WidgetElement>();
            for(var i=0;i<source.Cells.Length;i++)
            {
                if(S(source.Cells[i],"text").Length==0)continue;
                rows.Add(Text(i<source.Labels.Length?source.Labels[i]:"Value","calc.full.label."+i,"pob-eyebrow pob-muted"));
                AddFormatted(rows,source.Cells[i],"text","runs","calc.full.value."+i,emphasizeStats:true);
            }
            featureReading=(source.Title.Length>0?source.Title:"Calculation",rows.ToArray());return true;
        }
        return false;
    }

    private static string CalculationValue(string value)=>value switch{"inf" or "+inf"=>"\u221e","-inf"=>"-\u221e",_=>value};
    private static string CalculationMode(string mode)=>mode switch{"EFFECTIVE"=>"Effective DPS","COMBAT"=>"In Combat","BUFFED"=>"Buffed","UNBUFFED"=>"Unbuffed",_=>mode};

    private WidgetElement CalculationsWorkspace()
    {
        var sections=A(calculationCatalog,"sections");var selected=sections.FirstOrDefault(s=>S(s,"id")==calculationSection);
        var entries=A(selected,"items").Where(c=>(S(c,"title")+" "+S(c,"value")).Contains(calculationSearch,StringComparison.OrdinalIgnoreCase))
            .Select(c=>new CalculationCell(S(c,"id"),S(c,"title"),CalculationValue(S(c,"value")),S(c,"id")==calculationCell)).ToArray();
        calculationCollection??=CreateIndexedCollection<CalculationQuery,CalculationCell>("pob.calculation.source",calculationQuery,0,new()
        {
            ReadRange=(query,start,count,token)=>{token.ThrowIfCancellationRequested();return ValueTask.FromResult<IReadOnlyList<CalculationCell>>(query.Items.Skip(start).Take(count).ToArray());},
            ItemKey=item=>new(item.Id),
            RenderItem=(_,item,context)=>UI.ActionSurface("choose",context.Id("cell"),item.Title+": "+item.Value,ActionSurfaceOrientation.Vertical,
                UI.Text(item.Title,context.Id("title")).Classes("pob-small","pob-muted"),UI.Text(item.Value,context.Id("value")).Classes("pob-list-title","pob-gold"))
                .Selected(item.Selected).FocusRight("pob.calculation.right").Classes("pob-list-item"),
            OnAction=(query,item,action,token)=>{lock(gate){if(!ReferenceEquals(query,calculationQuery)||busy||detailKind!="calculations"||action.ActionId!="choose")return ValueTask.CompletedTask;return OnActionAsync(action with{ActionId="calculation-cell."+item.Id},token);}}
        });
        if(!calculationQuery.Items.SequenceEqual(entries))
        {
            var same=calculationQuery.Items.Select(i=>i.Id).SequenceEqual(entries.Select(i=>i.Id));calculationQuery=new(entries);
            if(same)calculationCollection.UpdateContent(calculationQuery);else calculationCollection.PublishQuery(calculationQuery,entries.Length);
        }
        calculationSourceCollection??=CreateIndexedCollection<CalculationSources,CalculationSource>("pob.calculation.detail-source",calculationSources,calculationSources.Items.Length,new()
        {
            ReadRange=(query,start,count,token)=>{token.ThrowIfCancellationRequested();return ValueTask.FromResult<IReadOnlyList<CalculationSource>>(query.Items.Skip(start).Take(count).ToArray());},
            ItemKey=item=>new(item.Key),
            RenderItem=(_,item,context)=>
            {
                var values=new List<WidgetElement>{UI.Text(item.Title,context.Id("title")).Classes("pob-small","pob-muted")};
                var runs=new List<FormattedRun>();
                for(var i=0;i<item.Cells.Length;i++)
                {
                    if(S(item.Cells[i],"text").Length==0)continue;
                    if(runs.Count>0)runs.Add(new(" · ","muted"));
                    if(i<item.Labels.Length)runs.Add(new(item.Labels[i]+": ","muted"));
                    runs.AddRange(ReadFormattedRuns(item.Cells[i],"text","runs",true));
                }
                AddRuns(values,runs,context.Id("summary"),"pob-choice-description");
                return UI.ActionSurface("read",context.Id("source"),item.Title+": "+Limit(item.Summary,400),ActionSurfaceOrientation.Vertical,values.ToArray())
                    .FocusLeft("pob.calculation.cells").Classes("pob-list-item");
            },
            OnAction=(query,item,action,token)=>{lock(gate){if(!ReferenceEquals(query,calculationSources)||busy||detailKind!="calculations"||action.ActionId!="read")return ValueTask.CompletedTask;return OnActionAsync(action with{ActionId="calculation-source."+item.Key},token);}}
        });
        var tools=QuickControls(UI.Stack("pob.calculation.tools",Button("Back to overview","back"),
            CalculationSkillControls(),
            UI.Select("Calculation section",sections.Select(s=>new SelectOption(S(s,"id"),S(s,"title"),"calculation-section."+S(s,"id"),S(s,"id")==calculationSection)).ToArray(),"pob.calculation.section").Classes("pob-select"),
            UI.TextEntry(calculationSearch,"Search this section","calculation-search","pob.calculation.search",100).Classes("pob-input"),Text($"{entries.Length} calculations","calculation.count","pob-small pob-muted")),"pob.calculation.section","pob.calculation.cells","Calculations",NeedsQuickControls(entries.Length));
        var list=QuickControls(UI.Stack("pob.calculation.list",UI.CollectionList("pob.calculation.cells",calculationCollection,80,"Calculations").Classes("pob-scroll")).Classes("pob-workspace"),"pob.calculation.cells","pob.calculation.tools","Section and search",NeedsQuickControls(entries.Length));
        var chosen=A(selected,"items").FirstOrDefault(c=>S(c,"id")==calculationCell);
        var context=calculationCatalog.GetProperty("context");
        var heading=UI.Stack("pob.calculation.detail-heading",Text(S(chosen,"title"),"calculation.title","pob-heading"),Text(CalculationValue(S(chosen,"value")),"calculation.value","pob-heading pob-gold"),
            Text($"{(calculationSelection.Group==0?"Following main skill":"Inspecting")}: {S(context,"skill")} · {(B(context,"minion")?"Minion":"Player")}","calculation.context","pob-small pob-muted"),CalculationModeControls(context));
        var right=new List<WidgetElement>{heading};
        var explanation=new List<WidgetElement>();
        foreach(var (entry,index) in A(calculationDetail,"sections").Select((entry,index)=>(entry,index)))
        {
            var lines=A(entry,"lines");
            if(lines.Length>0&&S(entry,"title").Length>0)explanation.Add(Text(S(entry,"title"),"calculation.explanation.title."+index,"pob-heading"));
            foreach(var (line,lineIndex) in lines.Select((line,lineIndex)=>(line,lineIndex)))
                AddFormatted(explanation,line,"text","runs",$"calculation.explanation.{index}.{lineIndex}",emphasizeStats:true);
            if(S(entry,"footer").Length>0)explanation.Add(Text(S(entry,"footer"),"calculation.explanation.footer."+index,"pob-muted"));
        }
        if(explanation.Count>0)right.Add(UI.VerticalScroll("pob.calculation.explanation",explanation.ToArray()).Classes("pob-scroll","pob-calculation-explanation"));
        if(calculationSources.Items.Length>0)
        {
            right.Add(Text("Sources and breakdown · Select a row for all fields","calculation.sources-count","pob-small pob-muted"));
            right.Add(QuickControls(UI.Stack("pob.calculation.sources",UI.CollectionList("pob.calculation.source-list",calculationSourceCollection,112,"Calculation sources").Classes("pob-scroll")).Classes("pob-workspace"),"pob.calculation.source-list","pob.calculation.tools","Section and search",NeedsQuickControls(calculationSources.Items.Length)));
        }
        else if(explanation.Count==0)right.Add(Text("PoB provides no further breakdown for this value.","calculation.empty","pob-muted"));
        var rightPanel=UI.Stack("pob.calculation.right",right.ToArray()).RememberChildFocus("pob.calculation.mode").Classes("pob-panel","pob-workspace");
        return Split("calculations",UI.Stack("pob.calculation.left",tools,list).Classes("pob-panel","pob-workspace"),rightPanel,1,1.6);
    }

    private string? CurrentCalculationStat()=>A(calculationCatalog,"sections").SelectMany(s=>A(s,"items"))
        .Where(c=>S(c,"id")==calculationCell).SelectMany(c=>A(c,"outputs")).Select(o=>o.GetString()).FirstOrDefault();

    private WidgetElement CalculationSkillControls()
    {
        var options=new List<SelectOption>{new("follow","Follow main skill: "+S(State,"mainSkill"),"calculation-group.0",calculationSelection.Group==0)};
        options.AddRange(A(State,"skills").Select(g=>new SelectOption("group"+I(g,"id"),InlineLabel(I(g,"id")+". "+S(g,"title"),"Group "+I(g,"id")),"calculation-group."+I(g,"id"),I(g,"id")==calculationSelection.Group)));
        var rows=new List<WidgetElement>{UI.Select("Inspect skill group",options,"pob.calculation.group").Classes("pob-select")};
        var skills=A(calculationCatalog.GetProperty("context"),"skills");
        if(calculationSelection.Group!=0&&skills.Length>1)rows.Add(UI.Select("Skill in group",skills.Select(s=>new SelectOption("skill"+I(s,"id"),S(s,"title"),"calculation-skill."+I(s,"id"),B(s,"active"))).ToArray(),"pob.calculation.skill").Classes("pob-select"));
        return UI.Stack("pob.calculation.skill-controls",rows.ToArray()).Classes("pob-buildtools");
    }

    private WidgetElement CalculationModeControls(JsonElement context)
    {
        var controls=new List<WidgetElement>{UI.Select("Buff mode",CalculationModes.Select(m=>new SelectOption(m,CalculationMode(m),"calculation-mode."+m,m==calculationSelection.Mode)).ToArray(),"pob.calculation.mode").Classes("pob-select")};
        if(B(context,"hasMinion"))controls.Add(UI.Select("Actor",new[]{new SelectOption("player","Player","calculation-actor.player",!calculationSelection.Minion),new SelectOption("minion","Minion","calculation-actor.minion",calculationSelection.Minion)},"pob.calculation.actor").Classes("pob-select"));
        return UI.Row("pob.calculation.modes",controls.ToArray()).Classes("pob-toolbar");
    }
}
