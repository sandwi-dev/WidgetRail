using System.Text.Json;
using System.Text.RegularExpressions;
using WidgetRail.WidgetSdk;

namespace WidgetRail.Samples.PobCompanion;

public sealed partial class PobWidget
{
    internal sealed record FormattedRun(string Text,string Tone);
    private static readonly Regex StatEmphasis=new(@"(?<![\p{L}\p{N}_])(?:Energy Shield|Physical damage|Fire damage|Cold damage|Lightning damage|Chaos damage|Critical Hit Chance|Critical Chance|Attack Speed|Cast Speed|Life|Mana|Spirit|Evasion|Armour|Strength|Dexterity|Intelligence|[-+]?\d+(?:[.,]\d+)*%?)(?![\p{L}\p{N}_])",
        RegexOptions.IgnoreCase|RegexOptions.CultureInvariant,TimeSpan.FromMilliseconds(100));

    internal static IReadOnlyList<FormattedRun> ReadFormattedRuns(JsonElement source,string textField,string runsField,bool emphasizeStats=false)
    {
        var input=A(source,runsField);
        var runs=input.Length>0?input.Select(r=>new FormattedRun(S(r,"text"),S(r,"tone","text"))).ToArray():[new FormattedRun(S(source,textField),"text")];
        var result=new List<FormattedRun>();
        foreach(var run in runs)
        {
            // Upstream formatting wins, especially unsupported-effect warnings.
            if(!emphasizeStats||run.Tone!="text"){result.Add(run);continue;}
            var offset=0;
            foreach(Match match in StatEmphasis.Matches(run.Text))
            {
                if(match.Index>offset)result.Add(new(run.Text[offset..match.Index],"text"));
                var token=match.Value.ToLowerInvariant();
                var tone=token switch
                {
                    "life" or "strength"=>"warning",
                    "mana" or "energy shield" or "intelligence" or "cold damage"=>"blue",
                    "evasion" or "armour" or "dexterity"=>"green",
                    "fire damage"=>"orange", "chaos damage"=>"violet", "physical damage"=>"emphasis",
                    _=>"gold",
                };
                result.Add(new(match.Value,tone));offset=match.Index+match.Length;
            }
            if(offset<run.Text.Length)result.Add(new(run.Text[offset..],"text"));
        }
        return result;
    }

    internal static IReadOnlyList<IReadOnlyList<FormattedRun>> DescriptionParagraphs(JsonElement node)
    {
        var lines=new List<List<FormattedRun>>{new()};
        foreach(var run in ReadFormattedRuns(node,"description","descriptionRuns",true))
        {
            var pieces=run.Text.Split('\n');
            for(var i=0;i<pieces.Length;i++)
            {
                if(i>0)lines.Add(new());
                if(pieces[i].Length>0)lines[^1].Add(new(pieces[i],run.Tone));
            }
        }
        var paragraphs=new List<IReadOnlyList<FormattedRun>>();var current=new List<FormattedRun>();
        foreach(var line in lines)
        {
            if(current.Count>0)current.Add(new("\n","text"));
            current.AddRange(line);
            if(string.Concat(line.Select(r=>r.Text)).TrimEnd().EndsWith(':'))continue;
            paragraphs.Add(current);current=new();
        }
        if(current.Count>0)paragraphs.Add(current);
        return paragraphs;
    }

    private static void AddNodeDescription(List<WidgetElement> rows,JsonElement node,string id)
    {
        var paragraphs=DescriptionParagraphs(node);
        for(var i=0;i<paragraphs.Count;i++)AddRuns(rows,paragraphs[i],id+".paragraph."+i,"pob-text");
    }
    private List<WidgetElement> ItemComparisonContent()
    {
        var before=A(pendingBefore!.State,"items").FirstOrDefault(i=>S(i,"slot")==previewSlot);
        var after=A(State,"items").FirstOrDefault(i=>S(i,"slot")==previewSlot);
        var rows=new List<WidgetElement>{Text("Equipment comparison · "+previewSlot,"item.compare.heading","pob-heading"),
            Text("Replacing "+S(before,"name","empty slot")+" with "+S(after,"name"),"item.compare.names")};
        AddComparison(rows,pendingBefore.State,State,"item.compare.stats");
        rows.Add(Text("Proposed item","item.compare.proposed","pob-eyebrow"));
        if(after.ValueKind==JsonValueKind.Object)rows.AddRange(ItemDetails(after));
        return rows;
    }

    private static void AddComparison(List<WidgetElement> rows,JsonElement before,JsonElement after,string id)
    {
        var old=A(before,"stats").ToDictionary(s=>S(s,"name"),s=>s.GetProperty("value").GetDouble());
        var changed=0;
        foreach(var stat in A(after,"stats"))
        {
            var key=S(stat,"name");
            if(key is not ("CombinedDPS" or "FullDPS" or "Life" or "EnergyShield" or "Mana" or "Armour" or "Evasion"))continue;
            if(key.Contains("DPS",StringComparison.Ordinal)&&(key=="FullDPS"?I(before,"fullDpsIncludedGroups")==0||I(after,"fullDpsIncludedGroups")==0:B(before,"mainSkillDisabled")||B(after,"mainSkillDisabled")))continue;
            if(!old.TryGetValue(key,out var previous))continue;
            var next=stat.GetProperty("value").GetDouble();var delta=next-previous;
            if(Math.Abs(delta)<.01)continue;
            changed++;
            rows.Add(UI.RichText("pob."+id+"."+key,
                Text(StatLabel(key)+": ",id+".label."+key,"pob-muted"),
                Text(StatValue(key,previous)+" → "+StatValue(key,next),id+".value."+key),
                Text($" ({(delta>0?"+":"")}{StatValue(key,delta)})",id+".delta."+key,delta>0?"pob-green":"pob-life")));
        }
        if(B(after,"mainSkillDisabled"))rows.Add(Text(S(after,"mainSkill")+": "+S(after,"mainSkillDisabledReason"),id+".disabled","pob-life"));
        if(changed==0)rows.Add(Text("No change to available headline stats.",id+".unchanged","pob-muted"));
    }

    private static void AddBreakdownRows(List<WidgetElement> content,JsonElement breakdown)
    {
        var columns=A(breakdown,"columns");var rows=A(breakdown,"rows");
        // Key/value rows retain column meaning even in the narrow inspector.
        // Source names remain adjacent to their values, without horizontal pan.
        for(var row=0;row<rows.Length;row++)
        {
            var cells=A(rows[row],"cells");var fields=new List<WidgetElement>();
            for(var column=0;column<Math.Min(columns.Length,cells.Length);column++)
            {
                var id=$"breakdown.row.{row}.cell.{column}";
                var value=new List<WidgetElement>();AddFormatted(value,cells[column],"text","runs",id,emphasizeStats:true);
                fields.Add(UI.Stack("pob."+id+".field",new[]{Text(columns[column].GetString()??"Value",id+".label","pob-small")}.Concat(value).ToArray()));
            }
            content.Add(UI.Stack("pob.breakdown.row."+row,fields.ToArray()).Classes("pob-context-card"));
        }
        if(I(breakdown,"rowCount")>rows.Length)content.Add(Text($"Showing {rows.Length} of {I(breakdown,"rowCount")} sources. Full breakdown available in desktop PoB.","breakdown.remaining","pob-muted"));
    }

    internal static void AddFormatted(List<WidgetElement> rows,JsonElement source,string textField,string runsField,string id,string css="pob-text",bool emphasizeStats=false)
    {
        AddRuns(rows,ReadFormattedRuns(source,textField,runsField,emphasizeStats),id,css);
    }

    private static void AddRuns(List<WidgetElement> rows,IReadOnlyList<FormattedRun> runs,string id,string css)
    {
        var spans=new List<WidgetElement>();var paragraph=0;var length=0;var serial=0;
        void Flush()
        {
            if(spans.Count==0)return;
            rows.Add(UI.RichText("pob."+id+"."+paragraph++,spans.ToArray()).Classes(css));
            spans.Clear();length=0;
        }
        foreach(var run in runs)
        {
            var text=run.Text;var tone=run.Tone;
            var accent=tone switch{"warning"=>"pob-life","blue"=>"pob-blue","gold"=>"pob-gold","orange"=>"pob-orange","green"=>"pob-green","violet"=>"pob-violet","muted"=>"pob-muted","emphasis"=>"pob-emphasis",_=>"pob-text"};
            for(var offset=0;offset<text.Length;)
            {
                if(length>=2400||spans.Count>=128)Flush();
                var count=Math.Min(2400-length,text.Length-offset);
                if(offset+count<text.Length&&char.IsHighSurrogate(text[offset+count-1]))count--;
                if(count==0){Flush();continue;}
                spans.Add(UI.Text(text.Substring(offset,count),"pob."+id+".run."+serial++).Classes(css,"pob-inline",accent));
                offset+=count;length+=count;
            }
        }
        Flush();
    }
}
