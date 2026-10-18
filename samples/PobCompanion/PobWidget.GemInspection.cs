using System.Globalization;
using System.Text.Json;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

namespace WidgetRail.Samples.PobCompanion;

public sealed partial class PobWidget
{
    // Captured catalog data only: no calculation, fetch or build mutation on focus/inspection.
    private sealed record GemInspectionData(string Id,string Name,bool Support,string Tags,string Description,
        double? Comparison,bool? CanSupport);
    private sealed record GemPickerContext(PobResult Build,string? DocumentId,int Group,int Gem,long CatalogRevision,
        string Search,string Type,string Sort,string GroupName,string ReplacedGem,string MainSkill,string Metric,PobInspectionContext InspectionContext);
    private sealed record GemInspection(long Token,GemQuery Query,GemEntry Entry,IndexedCollectionFocusTarget Anchor);
    private GemInspection? gemInspection;
    private long gemInspectionToken;
    private PobResult? gemInspectionContextBuild;
    private int gemInspectionContextGroup;
    private PobInspectionContext? gemInspectionContext;
    private PobInspectionContext GemInspectionContext(JsonElement group)
    {
        if(!ReferenceEquals(gemInspectionContextBuild,result)||gemInspectionContextGroup!=detailId||gemInspectionContext is null)
        {gemInspectionContext=PobInspectionContext.Read(State,result!.Xml,group);gemInspectionContextBuild=result;gemInspectionContextGroup=detailId;}
        return gemInspectionContext;
    }

    private GemPickerContext CaptureGemPickerContext()
    {
        var group=A(State,"skills").First(g=>I(g,"id")==detailId);
        return new(result!,documentId,detailId,pickerGem,gemCatalogRevision,gemSearch,gemType,gemSort,
            GemInspectionText(S(group,"title"),240),
            pickerGem==0?"":GemInspectionText(S(A(group,"gems").FirstOrDefault(g=>I(g,"id")==pickerGem),"name"),240),
            GemInspectionText(S(State,"mainSkill"),240),
            GemInspectionText(S(A(State,"gemSortMetrics").FirstOrDefault(m=>S(m,"id")==gemSort),"title"),160),GemInspectionContext(group));
    }
    private bool GemQueryIsCurrent(GemQuery query) => ReferenceEquals(query,gemQuery)&&query.Context is {} context&&
        result is not null&&ReferenceEquals(context.Build,result)&&context.DocumentId==documentId&&
        detailKind=="gem-picker"&&detailId==context.Group&&pickerGem==context.Gem&&gemCatalogRevision==context.CatalogRevision&&
        gemSearch==context.Search&&gemType==context.Type&&gemSort==context.Sort&&pendingBefore is null;

    private static string GemInspectionText(string value,int maximum)
    {
        var text=Plain(value).Replace("\r","").Trim();
        text=new string(text.Where(c=>!char.IsControl(c)||c is '\n' or '\t').ToArray());
        if(text.Length<=maximum)return text;
        var length=char.IsHighSurrogate(text[maximum-1])?maximum-1:maximum;
        return text[..length]+"…";
    }
    private static GemInspectionData ReadGemInspection(JsonElement gem)
    {
        var description=S(gem,"description");
        foreach(var key in new[]{"id","name","tagString","description"})
            if(gem.TryGetProperty(key,out var field)&&field.ValueKind!=JsonValueKind.String)
                throw new InvalidDataException("PoB returned invalid gem inspection metadata.");
        var id=S(gem,"id");
        if(string.IsNullOrWhiteSpace(id)||id.Length>1024||id.Any(char.IsControl))
            throw new InvalidDataException("PoB returned an invalid gem identity.");
        double? comparison=null;
        if(gem.TryGetProperty("comparison",out var value)&&value.ValueKind==JsonValueKind.Number&&
            value.TryGetDouble(out var number)&&double.IsFinite(number))comparison=number;
        bool? canSupport=gem.TryGetProperty("canSupport",out var supported)&&supported.ValueKind is JsonValueKind.True or JsonValueKind.False
            ?supported.GetBoolean():null;
        return new(id,GemInspectionText(S(gem,"name"),512),B(gem,"support"),
            GemInspectionText(S(gem,"tagString"),2048),GemInspectionText(description,2800),comparison,canSupport);
    }
    private static WidgetElement RenderGemCandidate(GemQuery query,GemEntry entry,WidgetIndexedItemContext context)
    {
        var candidate=UI.ActionSurface("choose",context.Id("gem"),entry.Name,ActionSurfaceOrientation.Horizontal,
            UI.Text(entry.Name,context.Id("name")).Classes("pob-inline")) with
        {ContextActions=[new("inspect","Inspect gem")]};
        var action=candidate.Shortcut(ControllerButton.X,"Inspect",actionId:"inspect").Classes("pob-button");
        if(entry.Inspection is not {} data)return action;
        var content=new List<WidgetElement>{UI.Text(data.Name,context.Id("preview-name")).Classes("pob-heading")};
        if(data.Tags.Length>0)content.Add(UI.Text(GemInspectionText(data.Tags,160),context.Id("preview-tags")).Classes("pob-small","pob-gold"));
        if(data.Description.Length>0)content.Add(UI.Text(GemInspectionText(data.Description,320),context.Id("preview-description")).Classes("pob-small"));
        if(query.Context is {} picker&&GemComparisonText(data,picker) is {Length:>0} total)
            content.Add(UI.Text(total,context.Id("preview-comparison")).Classes("pob-small"));
        content.Add(UI.Row(context.Id("preview-inspect"),UI.ControllerGlyph(ControllerPrompt.X,context.Id("preview-inspect-glyph")),
            UI.Text("Inspect",context.Id("preview-inspect-label")).Classes("pob-small","pob-muted")));
        return action.PresentOnFocus(UI.Stack(context.Id("preview"),content.ToArray()));
    }
    private static string GemComparisonText(GemInspectionData data,GemPickerContext context) =>
        context.Sort!="name"&&data.Comparison is {} total
            ?(context.Metric.Length>0?context.Metric:context.Sort)+" · "+total.ToString("N0",CultureInfo.InvariantCulture)+" total"
            :"";

    private void OpenGemInspection(GemQuery query,GemEntry entry)
    {
        if(!GemQueryIsCurrent(query)||entry.Inspection is null||gemCollection is null)return;
        var index=Array.FindIndex(query.Entries,candidate=>ReferenceEquals(candidate,entry));
        if(index<0)return;
        gemInspection=new(++gemInspectionToken,query,entry,gemCollection.FocusTarget("pob.gems",new(entry.Key),index));
    }
    private bool HandleGemInspectionAction(WidgetActionEvent action)
    {
        var id=action.ActionId;
        if(gemInspection is not {} inspection)return id.StartsWith("gem-inspection-",StringComparison.Ordinal);
        if(!GemQueryIsCurrent(inspection.Query)){gemInspection=null;return true;}
        if(id=="gem-inspection-close."+inspection.Token){gemInspection=null;return true;}
        if(id=="gem-inspection-apply."+inspection.Token)
        {
            gemInspection=null;
            var context=inspection.Query.Context!;
            Edit(new{op="gem-manage",mode=context.Gem==0?"add":"replace",group=context.Group,gem=context.Gem,
                gemId=inspection.Entry.Inspection!.Id},skillDestination:"skill");
        }
        // Inspection owns its scope. Background/superseded callbacks cannot alter its picker or build.
        return true;
    }
    private WidgetView WithGemInspection(WidgetView view)
    {
        if(gemInspection is not {} inspection)return view;
        if(!GemQueryIsCurrent(inspection.Query)){gemInspection=null;return view;}
        var data=inspection.Entry.Inspection!;var context=inspection.Query.Context!;
        var rows=new List<WidgetElement>{Text(data.Support?"Support gem":"Active skill gem","gem-inspection.type","pob-eyebrow pob-gold")};
        if(data.Tags.Length>0)rows.Add(Text(data.Tags,"gem-inspection.tags","pob-gold"));
        if(data.Description.Length>0)rows.Add(Text(data.Description,"gem-inspection.description"));
        rows.Add(Text(context.Gem==0?"Add to "+context.GroupName:"Replace "+context.ReplacedGem+" in "+context.GroupName,
            "gem-inspection.target","pob-muted"));
        if(context.InspectionContext.Summary.Length>0)rows.Add(Text(context.InspectionContext.Summary,"gem-inspection.context","pob-small pob-muted"));
        if(GemComparisonText(data,context) is {Length:>0} total)
        {
            rows.Add(Text(total,"gem-inspection.comparison","pob-heading"));
            if(context.MainSkill.Length>0)rows.Add(Text("Main skill · "+context.MainSkill,"gem-inspection.main","pob-small pob-muted"));
            if(data.Support&&data.CanSupport is {} supports)rows.Add(Text(supports?"PoB marks this gem as supporting the current skill.":
                "PoB does not mark this gem as supporting the current skill.","gem-inspection.support","pob-small"));
            rows.Add(Text("PoB's selected comparison total, not the change from your equipped gem.","gem-inspection.total-help","pob-small pob-muted"));
        }
        var close="gem-inspection-close."+inspection.Token;
        var apply="gem-inspection-apply."+inspection.Token;
        var controls=UI.Row("pob.gem-inspection.actions",Button("Back to gems",close),Button(context.Gem==0?"Add gem":"Replace gem",apply)).Classes("pob-toolbar");
        return view.WithModal(new("pob.gem-inspection",data.Name,InspectionWorkspace("pob.gem-inspection",rows,controls),
            "pob."+close,close){ScrollContent=false,AnchoredPresentation=new(new(IndexedItem:inspection.Anchor),Width:560,Height:440)});
    }
}
