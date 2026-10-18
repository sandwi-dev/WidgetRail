using System.Text.Json;
using System.Text.RegularExpressions;
using WidgetRail.WidgetSdk;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.Samples.PobCompanion;

public sealed partial class PobWidget
{
    private string configSection = "";
    private bool configHelp;
    private bool configChangedOnly;
    private PobConfigurationCatalog ConfigurationCatalog => result?.Configuration ?? PobConfigurationCatalog.Empty;
    private string[] ConfigurationSections() => ConfigurationCatalog.Options.Select(c=>c.Section).Concat(A(State,"customModGroups").Length>0?["Custom modifiers"]:Array.Empty<string>()).Distinct().ToArray();
    private static string InlineLabel(string value,string fallback="Value")
    {
        var label=Regex.Replace(value,@"\s+"," ").Trim();
        return string.IsNullOrWhiteSpace(label)?fallback:Limit(label,4000);
    }

    private WidgetElement ConfigurationWorkspace()
    {
        var sections=ConfigurationSections();
        if(!sections.Contains(configSection))configSection=sections.FirstOrDefault()??"General";
        var searching=!string.IsNullOrWhiteSpace(filter);
        var catalog=ConfigurationCatalog;
        if(!catalog.SupportsChangedFilter)configChangedOnly=false;
        var matching=catalog.Match(configSection,filter,configChangedOnly).ToArray();
        var inactive=matching.Where(InactiveConfiguration).ToArray();
        var inactiveDisclosure=inactiveConfigurationDisclosure.Select(InactiveConfigurationContext(),defaultExpanded:false);
        var visibleMatching=searching?matching:matching.Where(option=>!InactiveConfiguration(option))
            .Concat(inactiveDisclosure.Expanded?inactive:[]).ToArray();
        var editable=visibleMatching.FirstOrDefault(c=>c.Id!="customMods" && S(c.Raw,"kind")!="customMods" && S(c.Raw,"value").Length<=2048);
        var firstId=editable is not null?"pob.condition."+editable.Index:!searching&&inactive.Length>0?"pob.config.inactive.toggle":null;
        var controlsId="pob.config.controls."+(searching?"search":Array.IndexOf(sections,configSection).ToString());
        var navigation=new List<WidgetElement> {Text("Configuration set","config.set-label","pob-field-label")};
        AddSet(navigation,"configSets","config");
        navigation.Add(UI.Switch(catalog.SupportsChangedFilter?$"Changed settings ({catalog.ChangedCount})":"Changed settings unavailable",configChangedOnly,"config-changed","pob.config.changed")
            .Disabled(!catalog.SupportsChangedFilter).Classes("pob-toggle"));
        navigation.Add(Text("Search all sections","config.search-label","pob-field-label"));
        navigation.Add(SearchField(UI.TextEntry(filter,"Name or description","search","pob.search",256),result!,
            ()=>catalog.Options.Where(c=>!configChangedOnly||c.Changed==true).Select(c=>(c.Title,c.Title+" "+c.Section+" "+c.Description,c.Section))
                .Concat(A(State,"customModGroups").Select(g=>(S(g,"title"),S(g,"title")+" "+S(g,"text"),"Custom modifiers")))).Classes("pob-input"));
        if(searching)navigation.Add(Button("Clear search","config-clear-search"));
        var categories=new List<WidgetElement>{Text("Sections","config.sections","pob-heading")};
        for(var i=0;i<sections.Length;i++)
        {
            var count=catalog.ChangedBySection.GetValueOrDefault(sections[i]);
            var category=UI.Button(sections[i]+(count>0?$" · {count} changed":""),"config-section."+i,"pob.config.category."+i)
                .Selected(!searching&&sections[i]==configSection).Classes("pob-category");
            if(firstId is not null)category=category.FocusRight(controlsId);
            categories.Add(category);
        }
        var quick=NeedsQuickControls(sections.Length);
        ContainerElement categoryList=UI.Stack("pob.config.categories",categories.ToArray());
        if(sections.Length>0)categoryList=QuickControls(categoryList,"pob.config.category.0","pob.config.tools","Search / set",quick);
        var sidebar=UI.VerticalScroll("pob.config.navigation",
            QuickControls(UI.Stack("pob.config.tools",navigation.ToArray()).Classes("pob-buildtools"),"pob.search","pob.config.footer","Explanations",quick),
            categoryList,
            QuickControls(UI.Stack("pob.config.footer",UI.Switch("Show explanations",configHelp,"config-help","pob.config.help").Classes("pob-toggle")),
                "pob.config.help","pob.config.categories","Sections",quick)).Classes("pob-panel","pob-scroll");
        var controls=new List<WidgetElement>{Text(searching?"Search results":configSection,"config.title","pob-heading"),
            Text(searching?$"{matching.Length} matching settings across all sections":"Changes update the build immediately. Use Undo to reverse them.","config.summary","pob-muted")};
        string? previousGroup=null;
        foreach(var option in visibleMatching.Where(option=>searching||!InactiveConfiguration(option)))
        {
            var group=option.Section;
            if(searching&&group!=previousGroup)controls.Add(Text(group,"config.group."+option.Index,"pob-heading"));
            previousGroup=group;
            controls.Add(ConfigurationSetting(option));
        }
        if(!searching&&inactive.Length>0)
        {
            var changed=inactive.Count(option=>option.Changed==true);
            controls.Add(DisclosureButton(inactiveDisclosure,$"saved inactive settings · {inactive.Length} "+(inactive.Length==1?"setting":"settings")+$" · {changed} changed",
                "summary-inactive-config","pob.config.inactive.toggle","pob.config.inactive.disclosure"));
            controls.Add(Text("Saved in this configuration, but not currently applicable. Expanding lets you edit their retained values.","config.inactive.help","pob-small pob-muted"));
            foreach(var option in inactive)
                if(inactiveDisclosure.Expanded)controls.Add(ConfigurationSetting(option));
                else controls.Add(Text(InlineLabel(option.Title)+": "+ConfigurationSummaryValue(option),"config.inactive.summary."+option.Index,"pob-small pob-muted"));
        }
        var customMatches=searching?A(State,"customModGroups").Where(g=>(S(g,"title")+" "+S(g,"text")).Contains(filter,StringComparison.OrdinalIgnoreCase)).ToArray():[];
        foreach(var group in customMatches)controls.Add(Button("Custom modifiers · "+S(group,"title"),"custom-open."+I(group,"id")));
        if(matching.Length==0&&customMatches.Length==0)controls.Add(Text(configChangedOnly?"No changed settings here. Choose another section or turn off Changed settings.":"No matching settings. Try another search.","config.empty","pob-muted"));
        var right=UI.VerticalScroll(controlsId,controls.ToArray()).Classes("pob-scroll","pob-panel");
        if(firstId is not null)right=right.RememberChildFocus(firstId);
        return Split("configuration",sidebar,!searching&&configSection=="Custom modifiers"?CustomModsPanel():right,1,2.6);
    }

    private WidgetElement ConfigurationSetting(PobConfigurationOption option)
    {
        var condition=option.Raw;var index=option.Index;
        var rows=new List<WidgetElement>();
        if(S(condition,"id")=="customMods" || S(condition,"kind")=="customMods" || S(condition,"value").Length>2048)
        {
            rows.Add(Text((option.Id=="customMods"?"Custom modifiers":InlineLabel(option.Title))+(option.Changed==true?" · Changed":""),"config.readonly."+index,"pob-field-label"));
            var value=S(condition,"value");
            AddReading(rows,string.IsNullOrWhiteSpace(value)?"None in this configuration.":value,"config.value."+index);
        }
        else rows.Add(Condition(option));
        if(InactiveConfiguration(option))rows.Add(Text("Saved setting; not currently applicable to this build.","config.retained."+index,"pob-small"));
        if(configHelp&&!string.IsNullOrWhiteSpace(S(condition,"description")))
            AddFormatted(rows,condition,"description","descriptionRuns","config.description."+index,"pob-help",emphasizeStats:true);
        return UI.Stack("pob.config.row."+index,rows.ToArray()).Classes("pob-setting");
    }

    private WidgetElement NotesWorkspace()
    {
        var rows=new List<WidgetElement>{Text("Build notes","notes.heading","pob-heading")};
        foreach(var (link,index) in PobNoteLinks.Read(S(State,"notes")).Select((link,index)=>(link,index)))
            rows.Add(link.Intent.ContractId==WidgetIntentContracts.OpenVideo
                ?(UI.Button(link.Label,"note-link."+index,"pob.note-link."+index) with {Intent=link.Intent}).Classes("pob-button")
                :WebsiteButton(link.Label,"note-link."+index,link.Url));
        AddFormatted(rows,State,"notes","notesRuns","notes","pob-prose");
        var scroll=UI.VerticalScroll("pob.notes.scroll",UI.Stack("pob.notes.document",rows.ToArray()).Classes("pob-reading")).Classes("pob-scroll","pob-panel");
        if(!busy&&pendingBefore is null)
        {
            quickControlTargets.Add("pob.notes.tab-target");
            scroll=scroll.Shortcut(ControllerButton.LeftStick,"quick-controls.pob.notes.tab-target","Notes tab");
        }
        return scroll;
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
