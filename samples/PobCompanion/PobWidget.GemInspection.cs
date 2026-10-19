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
            UI.Text(entry.Name,context.Id("name")).Classes("pob-inline")).Classes("pob-button");
        return candidate.PresentOnFocus(entry.Key);
    }
    private static WidgetElement RenderGemCandidatePreview(GemQuery query,GemEntry entry,WidgetIdScope context)
    {
        if(entry.Inspection is not {} data||query.Context is not {} picker)return UI.Text("Gem details unavailable",context.Id("empty"));
        var rows=new List<WidgetElement>{UI.Text(data.Support?"Support gem":"Active skill gem",context.Id("type")).Classes("pob-eyebrow","pob-gold")};
        if(data.Tags.Length>0)rows.Add(UI.Text(data.Tags,context.Id("tags")).Classes("pob-gold"));
        if(data.Description.Length>0)rows.Add(UI.Text(data.Description,context.Id("description")).Classes("pob-text"));
        rows.Add(UI.Text(picker.Gem==0?"Add to "+picker.GroupName:"Replace "+picker.ReplacedGem+" in "+picker.GroupName,context.Id("target")).Classes("pob-muted"));
        if(picker.InspectionContext.Summary.Length>0)rows.Add(UI.Text(picker.InspectionContext.Summary,context.Id("context")).Classes("pob-small","pob-muted"));
        if(GemComparisonText(data,picker) is {Length:>0} total)
        {
            rows.Add(UI.Text(total,context.Id("comparison")).Classes("pob-heading"));
            if(picker.MainSkill.Length>0)rows.Add(UI.Text("Main skill · "+picker.MainSkill,context.Id("main")).Classes("pob-small","pob-muted"));
            if(data.Support&&data.CanSupport is {} supports)rows.Add(UI.Text(supports?"Supports the current skill":"Does not support the current skill",context.Id("support")).Classes("pob-small"));
            rows.Add(UI.Text("PoB's selected comparison total, not the change from your equipped gem.",context.Id("total-help")).Classes("pob-small","pob-muted"));
        }
        return InspectionWorkspace(context.Id("preview"),rows,
            UI.Stack(context.Id("controls"),UI.Text(data.Name,context.Id("title")).Classes("pob-preview-title","pob-blue"),
                UI.Button(picker.Gem==0?"Add gem":"Replace gem","choose",context.Id("apply")).Classes("pob-button")));
    }
    private static string GemComparisonText(GemInspectionData data,GemPickerContext context) =>
        context.Sort!="name"&&data.Comparison is {} total
            ?(context.Metric.Length>0?context.Metric:context.Sort)+" · "+total.ToString("N0",CultureInfo.InvariantCulture)+" total"
            :"";

}
