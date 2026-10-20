using System.Text.Json;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

namespace WidgetRail.Samples.PobCompanion;

public sealed partial class PobWidget
{
    private sealed record RowPreviewContext(PobResult Build,string? Document,bool Skills);
    private RowPreviewContext? rowPreviewContext;
    private WidgetFocusPreviewSource<RowPreviewContext>? rowPreviews;

    private WidgetElement WithRowPreviews(WidgetElement rows,bool skills)
    {
        if(rowPreviewContext is null||!ReferenceEquals(rowPreviewContext.Build,result)||rowPreviewContext.Document!=documentId||rowPreviewContext.Skills!=skills)
        {
            rowPreviewContext=new(result!,documentId,skills);
            if(rowPreviews is null)rowPreviews=CreateFocusPreviewSource("pob.row-preview-source",rowPreviewContext,new WidgetFocusPreviewOptions<RowPreviewContext>
            {
                Resolve=(context,key,token)=>
                {
                    token.ThrowIfCancellationRequested();
                    var prefix=context.Skills?"skill.":"item.";
                    if(!key.StartsWith(prefix,StringComparison.Ordinal)||!int.TryParse(key[prefix.Length..],out var id))throw new ArgumentException("Unknown preview key.");
                    var entry=A(context.Build.State,context.Skills?"skills":"items").Single(entry=>I(entry,"id")==id);
                    return ValueTask.FromResult(context.Skills?SkillGroupPreview(entry):EquipmentSummaryPreview(entry));
                },
            });
            else rowPreviews.Publish(rowPreviewContext);
        }
        return UI.FocusPresentationSurface(rows,UI.Text("","pob.rows.preview.empty"),"pob.rows.preview") with
        {Preview=new(Width:440,Height:480){SizeToContentHeight=true},PreviewSource=rowPreviews!.Descriptor};
    }

    private static WidgetElement SkillGroupPreview(JsonElement group)
    {
        var id="group-preview."+I(group,"id");var name=S(group,"title");
        var rows=new List<WidgetElement>{Text(string.IsNullOrWhiteSpace(name)?"Unnamed group":name,id+".title","pob-preview-title pob-blue")};
        var flags=new List<string>{B(group,"enabled")?"Enabled":"Disabled"};
        if(B(group,"main"))flags.Add("Main skill");if(B(group,"fullDps"))flags.Add("Included in Full DPS");
        rows.Add(Text(string.Join(" · ",flags),id+".state","pob-preview-meta"));
        var weaponSets=A(group,"weaponSets").Where(set=>B(set,"enabled")).Select(set=>"Weapon set "+I(set,"id")).ToArray();
        var location=weaponSets.Length>0?string.Join(" · ",weaponSets):S(group,"slot");
        if(location.Length>0)rows.Add(Text(location,id+".location","pob-preview-meta"));
        if(B(group,"generated"))rows.Add(Text("Granted by "+S(group,"sourceName","build effects"),id+".source","pob-preview-meta"));
        if(I(group,"groupCount")>1)rows.Add(Text("Group count · "+I(group,"groupCount"),id+".count","pob-preview-meta"));
        var gems=A(group,"gems");
        foreach(var (gem,index) in gems.Take(6).Select((gem,index)=>(gem,index)))
        {
            var description="Level "+S(gem,"level")+" · Quality "+S(gem,"quality")+"%";
            if(I(gem,"count")>1)description+=" · Count "+I(gem,"count");
            if(!B(gem,"enabled"))description+=" · Disabled";
            rows.Add(UI.Stack("pob."+id+".gem."+index,
                Text(CandidateText(S(gem,"name"),100),id+".gem-name."+index,"pob-preview-gem-name pob-blue"),
                Text(description,id+".gem-meta."+index,"pob-preview-meta")).Classes("pob-preview-gem"));
        }
        if(gems.Length==0)rows.Add(Text("No gems in this group",id+".empty","pob-preview-meta"));
        if(gems.Length>6)rows.Add(Text("+ "+(gems.Length-6)+" more gems",id+".more","pob-preview-meta"));
        return UI.Stack("pob."+id,rows.ToArray()).Classes("pob-preview-content");
    }

    private static WidgetElement EquipmentSummaryPreview(JsonElement item)
    {
        var id="equipment-preview."+I(item,"id");var name=S(item,"name");var basis=S(item,"base");
        var rows=new List<WidgetElement>{Text(name,id+".title","pob-preview-title "+RarityClass(S(item,"rarity"))),
            Text(string.Join(" · ",new[]{S(item,"slot"),S(item,"rarity"),basis}.Where(value=>value.Length>0)),id+".identity","pob-preview-meta")};
        var lines=A(item,"details").SelectMany(block=>A(block,"lines")).Where(line=>
            S(line,"text").Length>0&&S(line,"text")!=name&&S(line,"text")!=basis).ToArray();
        bool IsModifier(JsonElement line)=>S(line,"tone") is "blue" or "warning" || ReadFormattedRuns(line,"text","runs").FirstOrDefault()?.Tone is "blue" or "green" or "warning";
        var properties=lines.Where(line=>!IsModifier(line)&&S(line,"tone","text")=="text"&&S(line,"text")!=S(item,"slot")).Take(2);
        var modifiers=lines.Where(IsModifier).OrderByDescending(line=>S(line,"tone")=="warning").Take(4);
        var shown=properties.Concat(modifiers).ToArray();
        foreach(var (line,index) in shown.Select((line,index)=>(line,index)))
            AddRuns(rows,SummaryRuns(ReadFormattedRuns(line,"text","runs"),220),id+".line."+index,"pob-preview-modifiers");
        if(lines.Length>shown.Length)rows.Add(Text("+ "+(lines.Length-shown.Length)+" more detail lines",id+".more","pob-preview-meta"));
        if(shown.Length==0)rows.Add(Text("No summary properties available",id+".empty","pob-preview-meta"));
        return UI.Stack("pob."+id,rows.ToArray()).Classes("pob-preview-content");
    }

    private static IReadOnlyList<FormattedRun> SummaryRuns(IReadOnlyList<FormattedRun> source,int maximum,int maximumRuns=int.MaxValue)
    {
        var result=new List<FormattedRun>();var remaining=maximum;
        foreach(var run in source)
        {
            if(result.Count>=maximumRuns-1){result.Add(new("…","muted"));break;}
            if(run.Text.Length<=remaining){result.Add(run);remaining-=run.Text.Length;continue;}
            var length=remaining>0&&char.IsHighSurrogate(run.Text[remaining-1])?remaining-1:remaining;
            if(length>0)result.Add(run with{Text=run.Text[..length]});
            result.Add(new("…","muted"));break;
        }
        return result;
    }
}
