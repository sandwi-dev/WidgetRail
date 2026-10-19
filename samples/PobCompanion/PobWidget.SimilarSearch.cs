using System.Globalization;
using System.Text.Json;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

namespace WidgetRail.Samples.PobCompanion;

public sealed partial class PobWidget
{
    private sealed record SimilarReference(PobResult Build, string? Document, string Raw, string ReturnKind, string ReturnGroup, string Label);
    private sealed class SimilarField
    {
        internal required string Key, Title;
        internal bool Supported, Toggle, Enabled;
        internal string? Minimum, Maximum;
    }
    private SimilarReference? similarReference;
    private SimilarField[] similarFields = [];
    private string[] similarModes = [];
    private int similarMode = 1;
    private long similarGeneration;
    private string? similarReturnGroup;
    private bool SimilarCurrent => similarReference is { } reference && ReferenceEquals(reference.Build,result) && reference.Document==documentId;

    private ContainerElement SimilarEntry(string source, string label="Find similar") =>
        UI.Stack("pob.similar.entry."+source,Button(label,"similar-start."+source).Disabled(source=="equipped"&&!A(State,"items").Any(item=>I(item,"id")==detailId)))
            .RememberChildFocus("pob.similar-start."+source);

    private bool HandleSimilarSearchAction(WidgetActionEvent action)
    {
        similarReturnGroup=null;
        var id=action.ActionId;
        if(id.StartsWith("similar-start.",StringComparison.Ordinal))
        {
            if(result is null||pendingBefore is not null)return true;
            var source=id["similar-start.".Length..];
            string raw="",slot="",label="";
            if(source=="equipped"&&detailKind=="item")
            {var item=A(State,"items").FirstOrDefault(item=>I(item,"id")==detailId);raw=S(item,"raw");slot=S(item,"slot");label=S(item,"name");}
            else if(source=="inventory"&&detailKind=="inventory"&&inventoryId>0||source=="draft"&&detailKind=="item-author")
            {raw=S(authoredItem,"raw");label=S(authoredItem,"name");slot=inventorySlot;}
            else if(source is "current" or "other"&&BuildComparisonCurrent&&buildComparisonDetail>=0&&buildComparisonDetail<buildComparison!.Differences.Count)
            {
                var difference=buildComparison.Differences[buildComparisonDetail];
                var entry=source=="other"?difference.Other:difference.Current;
                if(difference.Category==PobBuildComparisonCategory.Equipment&&entry is not null)
                {raw=entry.Value;slot=entry.Key;label=entry.Summary??entry.Label;}
            }
            if(raw.Length==0)return true;
            var reference=new SimilarReference(result,documentId,raw,detailKind,"pob.similar.entry."+source,label);
            var generation=++similarGeneration;
            ReadTradePreferences();
            Work(async token=>
            {
                await LoadTradeLeaguesAsync(token);
                var response=await engine.ExecuteAsync(new{op="similar-catalog",raw=reference.Raw,league=tradeLeague},token,reference.Build.Xml);
                var slots=await ItemSlotsAsync(reference.Raw,reference.Build.Xml,token);
                var catalog=(await engine.ExecuteAsync(new{op="trade-catalog"},token,reference.Build.Xml)).State;
                token.ThrowIfCancellationRequested();
                lock(gate)
                {
                    if(generation!=similarGeneration||!ReferenceEquals(result,reference.Build)||documentId!=reference.Document)return;
                    similarReference=reference;
                    similarFields=A(response.State,"fields").Select(field=>new SimilarField
                    {
                        Key=S(field,"key"),Title=S(field,"title"),Supported=B(field,"supported"),Toggle=B(field,"toggle"),Enabled=B(field,"enabled"),
                        Minimum=field.TryGetProperty("minimum",out var min)&&min.ValueKind==JsonValueKind.String?min.GetString():null,
                        Maximum=field.TryGetProperty("maximum",out var max)&&max.ValueKind==JsonValueKind.String?max.GetString():null,
                    }).ToArray();
                    similarModes=A(response.State,"modes").Select(mode=>mode.GetString()!).ToArray();similarMode=1;
                    tradeCatalog=catalog;
                    var available=A(catalog,"slots").Select(s=>S(s,"name")).Where(slots.Contains).ToArray();
                    tradeSlot=available.Contains(slot)?slot:available.FirstOrDefault()??"";
                    detailKind="similar-search";focusRequestSequence++;
                }
            },"Reading similar-item filters…");
            return true;
        }
        if(detailKind!="similar-search")return false;
        if(id=="back")
        {
            detailKind=SimilarCurrent?similarReference!.ReturnKind:"";
            similarReturnGroup=SimilarCurrent?similarReference!.ReturnGroup:null;
            similarReference=null;similarFields=[];similarModes=[];++similarGeneration;focusRequestSequence++;return true;
        }
        var parts=id.Split('.');
        if(parts.Length<3||parts[0]!="similar"||!long.TryParse(parts[1],out var generationId)||generationId!=similarGeneration||!SimilarCurrent)return false;
        var command=parts[2];
        if(command=="search")
        {
            if(!tradeLeagues.Contains(tradeLeague)||similarReference is not {} reference)return true;
            foreach (var range in similarFields.Where(field => field.Supported))
            {
                if (double.TryParse(range.Minimum, NumberStyles.Float, CultureInfo.InvariantCulture, out var minimum) &&
                    double.TryParse(range.Maximum, NumberStyles.Float, CultureInfo.InvariantCulture, out var maximum) && minimum > maximum)
                {
                    ShowFeedback("Invalid range", "Minimum cannot exceed maximum for " + Limit(range.Title, 120) + ".", ToastTone.Warning);
                    return true;
                }
            }
            var league=tradeLeague;var mode=similarMode;
            var options=similarFields.Where(f=>f.Supported).ToDictionary(f=>f.Key,f=>(object)new{enabled=f.Toggle?(bool?)f.Enabled:null,minimum=f.Minimum,maximum=f.Maximum});
            Work(async token=>
            {
                var query=await engine.ExecuteAsync(new{op="similar-search",raw=reference.Raw,league,mode,options},token,reference.Build.Xml);
                token.ThrowIfCancellationRequested();
                lock(gate)if(!SimilarCurrent||!ReferenceEquals(reference,similarReference)||detailKind!="similar-search")return;
                await OpenTradeSearchAsync(PobTradeSearch.SearchUrl(game,league,query.State.GetProperty("query")),token);
            },"Preparing similar-item search…");return true;
        }
        if(parts.Length!=4)return false;
        var key=parts[3];
        if(command=="league"&&int.TryParse(key,out var leagueIndex)&&leagueIndex>=0&&leagueIndex<tradeLeagues.Length)
        {tradeLeague=tradeLeagues[leagueIndex];SaveTradePreferences();return true;}
        if(command=="mode"&&int.TryParse(key,out var modeIndex)&&modeIndex>=0&&modeIndex<similarModes.Length)
        {similarMode=modeIndex+1;return true;}
        if(command=="slot"&&int.TryParse(key,out var slotIndex)&&slotIndex>=0&&slotIndex<A(tradeCatalog,"slots").Length)
        {tradeSlot=S(A(tradeCatalog,"slots")[slotIndex],"name");return true;}
        var field=similarFields.FirstOrDefault(f=>f.Key==key);
        if(field is null||!field.Supported)return true;
        if(command=="toggle"&&field.Toggle){field.Enabled=!field.Enabled;return true;}
        if(command is "min" or "max")
        {
            if(command=="min"&&field.Minimum is null||command=="max"&&field.Maximum is null)return true;
            var text=(action.CommittedText??"").Trim();
            if(text.Length>16||text.Length>0&&(!double.TryParse(text,NumberStyles.AllowLeadingSign|NumberStyles.AllowDecimalPoint,CultureInfo.InvariantCulture,out var number)||!double.IsFinite(number)||Math.Abs(number)>1_000_000_000))
            {ShowFeedback("Invalid bound","Enter a number, or leave the bound empty.",ToastTone.Warning);return true;}
            if(text.Length>0)text=double.Parse(text,CultureInfo.InvariantCulture).ToString("0.###############",CultureInfo.InvariantCulture);
            if(command=="min")field.Minimum=text;else field.Maximum=text;
            return true;
        }
        return false;
    }

    private WidgetElement SimilarSearchWorkspace()
    {
        var prefix="similar."+similarGeneration+".";
        if(!SimilarCurrent)return UI.Stack("pob.similar.page",ManagementHeader("Find similar"),Text("The build changed. Return and choose the reference item again.","similar.stale","pob-muted"));
        var options=new List<WidgetElement>();
        foreach(var field in similarFields)
        {
            var key=field.Key;
            var row=new List<WidgetElement>{Text(Limit(field.Title,1000),"similar.label."+key,field.Supported?"pob-blue":"pob-muted")};
            var controls=new List<WidgetElement>();
            if(field.Toggle)controls.Add((UI.Switch("Required",field.Enabled,prefix+"toggle."+key,"pob.similar.toggle."+key) with{AccessibilityLabel=field.Title+", required "+(field.Enabled?"On":"Off")}).Disabled(!field.Supported).Classes("pob-toggle"));
            if(field.Minimum is not null)controls.Add(Field("Minimum",(UI.TextEntry(field.Minimum,"No minimum",prefix+"min."+key,"pob.similar.min."+key,16) with{InputKind=TextEntryInputKind.Decimal,AccessibilityLabel="Minimum · "+field.Title}).Disabled(!field.Supported).Classes("pob-input"),"similar.min."+key));
            if(field.Maximum is not null)controls.Add(Field("Maximum",(UI.TextEntry(field.Maximum,"No maximum",prefix+"max."+key,"pob.similar.max."+key,16) with{InputKind=TextEntryInputKind.Decimal,AccessibilityLabel="Maximum · "+field.Title}).Disabled(!field.Supported).Classes("pob-input"),"similar.max."+key));
            if(!field.Supported)row.Add(Text("PoB has no supported trade filter for this modifier.","similar.unsupported."+key,"pob-small pob-muted"));
            row.Add(UI.Grid("pob.similar.fields."+key,[GridTrack.Auto()],controls.Select(_=>GridTrack.Star()).ToArray(),controls.Select((control,index)=>control.InGrid(column:index)).ToArray()).Spacing(0,12));
            options.Add(UI.Stack("pob.similar.option."+key,row.ToArray()).Classes("pob-context-card"));
        }
        var selected=similarFields.Count(f=>f.Toggle&&f.Enabled);
        return UI.Stack("pob.similar.page",
            UI.Stack("pob.similar.tools",ManagementHeader("Find similar · "+Limit(similarReference!.Label,100)),
                UI.Row("pob.similar.actions",Button("Search trade",prefix+"search").Disabled(!tradeLeagues.Contains(tradeLeague)),
                    Text(selected+" required filters","similar.selected","pob-small pob-muted")).Classes("pob-toolbar"))
                .RememberChildFocus("pob."+prefix+"search"),
            UI.VerticalScroll("pob.similar.scroll",
                Text("Choose the guide item's important modifiers, then relax their bounds as needed. Unselected modifiers are not required.","similar.help","pob-muted"),
                UI.Grid("pob.similar.context",[GridTrack.Auto()],[GridTrack.Star(),GridTrack.Star()],
                    SelectWithUnavailable("League",tradeLeagues.Select((league,i)=>new SelectOption("league"+i,league,prefix+"league."+i,league==tradeLeague)).ToArray(),"pob.similar.league","No leagues available").Classes("pob-select"),
                    UI.Select("Listing status",similarModes.Select((mode,i)=>new SelectOption("mode"+i,mode,prefix+"mode."+i,i+1==similarMode)).ToArray(),"pob.similar.mode").Classes("pob-select").InGrid(column:1)).Spacing(0,12),
                SelectWithUnavailable("Compare results against",A(tradeCatalog,"slots").Select((slot,i)=>new SelectOption("slot"+i,S(slot,"title"),prefix+"slot."+i,S(slot,"name")==tradeSlot)).ToArray(),"pob.similar.slot","Choose equipment when comparing a result").Classes("pob-select"),
                UI.Stack("pob.similar.options",options.ToArray()).Classes("pob-buildtools")).Classes("pob-scroll","pob-panel")).Classes("pob-workspace");
    }
}
