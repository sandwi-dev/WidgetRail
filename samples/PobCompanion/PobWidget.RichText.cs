using System.Text.Json;
using WidgetRail.WidgetSdk;

namespace WidgetRail.Samples.PobCompanion;

public sealed partial class PobWidget
{
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
                var value=new List<WidgetElement>();AddFormatted(value,cells[column],"text","runs",id);
                fields.Add(UI.Stack("pob."+id+".field",new[]{Text(columns[column].GetString()??"Value",id+".label","pob-small")}.Concat(value).ToArray()));
            }
            content.Add(UI.Stack("pob.breakdown.row."+row,fields.ToArray()).Classes("pob-context-card"));
        }
        if(I(breakdown,"rowCount")>rows.Length)content.Add(Text($"Showing {rows.Length} of {I(breakdown,"rowCount")} sources. Full breakdown available in desktop PoB.","breakdown.remaining","pob-muted"));
    }

    internal static void AddFormatted(List<WidgetElement> rows,JsonElement source,string textField,string runsField,string id,string css="pob-text")
    {
        var runs=A(source,runsField);
        if(runs.Length==0){var text=S(source,textField);if(!string.IsNullOrWhiteSpace(text))AddReading(rows,text,id,css);return;}
        var spans=new List<WidgetElement>();var paragraph=0;var length=0;var serial=0;
        void Flush()
        {
            if(spans.Count==0)return;
            rows.Add(UI.RichText("pob."+id+"."+paragraph++,spans.ToArray()).Classes(css));
            spans.Clear();length=0;
        }
        foreach(var run in runs)
        {
            var text=S(run,"text");var tone=S(run,"tone");
            var accent=tone switch{"warning"=>"pob-life","blue"=>"pob-blue","gold"=>"pob-gold","orange"=>"pob-orange","green"=>"pob-green","violet"=>"pob-violet","muted"=>"pob-muted",_=>"pob-text"};
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
