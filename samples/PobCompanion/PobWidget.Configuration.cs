using System.Text.Json;
using System.Text.RegularExpressions;
using WidgetRail.WidgetSdk;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.Samples.PobCompanion;

public sealed partial class PobWidget
{
    private string configSection = "";
    private bool configHelp;
    private string[] ConfigurationSections() => A(State,"conditions").Select(c=>S(c,"section","General")).Distinct().ToArray();
    private static string InlineLabel(string value,string fallback="Value")
    {
        var label=Regex.Replace(value,@"\s+"," ").Trim();
        return string.IsNullOrWhiteSpace(label)?fallback:Limit(label,4000);
    }

    private WidgetElement ConfigurationWorkspace()
    {
        var sections=ConfigurationSections();
        if(!sections.Contains(configSection))configSection=sections.FirstOrDefault()??"General";
        var all=A(State,"conditions");
        var searching=!string.IsNullOrWhiteSpace(filter);
        var matching=all.Where(c=>searching
            ? (S(c,"title")+" "+S(c,"section")+" "+S(c,"description")).Contains(filter,StringComparison.OrdinalIgnoreCase)
            : S(c,"section","General")==configSection).ToArray();
        var editable=matching.FirstOrDefault(c=>S(c,"id")!="customMods" && S(c,"value").Length<=2048);
        var firstId=editable.ValueKind==JsonValueKind.Object?"pob.condition."+Array.FindIndex(all,c=>S(c,"id")==S(editable,"id")):null;
        var controlsId="pob.config.controls."+(searching?"search":Array.IndexOf(sections,configSection).ToString());
        var navigation=new List<WidgetElement> {Text("Configuration set","config.set-label","pob-field-label")};
        AddSet(navigation,"configSets","config");
        navigation.Add(Text("Search all sections","config.search-label","pob-field-label"));
        navigation.Add(UI.TextEntry(filter,"Name or description","search","pob.search",256).Classes("pob-input"));
        if(searching)navigation.Add(Button("Clear search","config-clear-search"));
        navigation.Add(Text("Sections","config.sections","pob-heading"));
        for(var i=0;i<sections.Length;i++)
        {
            var category=UI.Button(sections[i],"config-section."+i,"pob.config.category."+i)
                .Selected(!searching&&sections[i]==configSection).Classes("pob-category");
            if(firstId is not null)category=category.FocusRight(controlsId);
            navigation.Add(category);
        }
        navigation.Add(UI.Switch("Show explanations",configHelp,"config-help","pob.config.help").Classes("pob-toggle"));
        var controls=new List<WidgetElement>{Text(searching?"Search results":configSection,"config.title","pob-heading"),
            Text(searching?$"{matching.Length} matching settings across all sections":"Changes update the build immediately. Use Undo to reverse them.","config.summary","pob-muted")};
        string? previousGroup=null;
        foreach(var condition in matching)
        {
            var index=Array.FindIndex(all,c=>S(c,"id")==S(condition,"id"));
            var group=S(condition,"section","General");
            if(searching&&group!=previousGroup)controls.Add(Text(group,"config.group."+index,"pob-heading"));
            previousGroup=group;
            var rows=new List<WidgetElement>();
            if(S(condition,"id")=="customMods" || S(condition,"value").Length>2048)
            {
                rows.Add(Text(S(condition,"id")=="customMods"?"Custom modifiers":InlineLabel(S(condition,"title")),"config.readonly."+index,"pob-field-label"));
                var value=S(condition,"value");
                AddReading(rows,string.IsNullOrWhiteSpace(value)?"None in this configuration.":value,"config.value."+index);
            }
            else rows.Add(Condition(condition));
            if(condition.TryGetProperty("applicable",out var applicable)&&!applicable.GetBoolean())
                rows.Add(Text("Saved setting; not currently applicable to this build.","config.retained."+index,"pob-small"));
            if(configHelp&&!string.IsNullOrWhiteSpace(S(condition,"description")))
                AddFormatted(rows,condition,"description","descriptionRuns","config.description."+index,"pob-help",emphasizeStats:true);
            controls.Add(UI.Stack("pob.config.row."+index,rows.ToArray()).Classes("pob-setting"));
        }
        if(matching.Length==0)controls.Add(Text("No matching settings. Try another search.","config.empty","pob-muted"));
        var right=UI.VerticalScroll(controlsId,controls.ToArray()).Classes("pob-scroll","pob-panel");
        if(firstId is not null)right=right.RememberChildFocus(firstId);
        return Split("configuration",Panel("config.navigation",navigation),right,1,2.6);
    }

    private WidgetElement NotesWorkspace()
    {
        var rows=new List<WidgetElement>{Text("Build notes","notes.heading","pob-heading")};
        foreach(var link in GuideLinks().Select((url,index)=>(url,index)))
            rows.Add(UI.Button("Open guide · "+new Uri(link.url).Host,"guide-link."+link.index,"pob.guide-link."+link.index)
                .OpenIntent(WidgetIntentContracts.Web,JsonSerializer.SerializeToElement(new{url=link.url})).Classes("pob-button"));
        AddFormatted(rows,State,"notes","notesRuns","notes","pob-prose");
        return UI.VerticalScroll("pob.notes.scroll",UI.Stack("pob.notes.document",rows.ToArray()).Classes("pob-reading")).Classes("pob-scroll","pob-panel");
    }

    // Bound individual protocol strings without breaking the document into UI pages.
    internal static void AddReading(List<WidgetElement> rows,string text,string id,string css="pob-prose")
    {
        if(string.IsNullOrWhiteSpace(text)){rows.Add(Text("No notes in this build.",id+".empty","pob-muted"));return;}
        var offset=0;var index=0;
        while(offset<text.Length)
        {
            var length=Math.Min(2800,text.Length-offset);
            if(offset+length<text.Length)
            {
                var split=text.LastIndexOf('\n',offset+length-1,length);
                if(split>=offset+length/2)length=split-offset+1;
                else if(char.IsHighSurrogate(text[offset+length-1]))length--;
            }
            rows.Add(Text(text.Substring(offset,length),id+"."+index++,css));offset+=length;
        }
    }
}
