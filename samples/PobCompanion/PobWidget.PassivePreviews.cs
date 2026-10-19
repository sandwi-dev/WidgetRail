using System.Text.Json;
using WidgetRail.WidgetSdk;

namespace WidgetRail.Samples.PobCompanion;

public sealed partial class PobWidget
{
    private sealed record PassivePreviewContext(PassiveQuery Query,PobResult Build,string? Document);
    private PassivePreviewContext? passivePreviewContext;
    private WidgetFocusPreviewSource<PassivePreviewContext>? passivePreviews;

    private WidgetElement WithPassivePreviews(WidgetElement list)
    {
        if(passivePreviewContext is null||!ReferenceEquals(passivePreviewContext.Query,passiveQuery)||
            !ReferenceEquals(passivePreviewContext.Build,result)||passivePreviewContext.Document!=documentId)
        {
            passivePreviewContext=new(passiveQuery,result!,documentId);
            if(passivePreviews is null)passivePreviews=CreateFocusPreviewSource("pob.passive-preview-source",passivePreviewContext,new WidgetFocusPreviewOptions<PassivePreviewContext>
            {
                Resolve=(context,key,token)=>
                {
                    token.ThrowIfCancellationRequested();
                    var entry=context.Query.Entries.SingleOrDefault(entry=>entry.Key==key)??throw new ArgumentException("Unknown passive preview key.");
                    return ValueTask.FromResult(entry.IsGroup?PassiveGroupPreview(entry):PassivePreview(entry.Nodes[0]));
                },
            });
            else passivePreviews.Publish(passivePreviewContext);
        }
        return UI.FocusPresentationSurface(list,UI.Text("","pob.passives.preview.empty"),"pob.passives.preview").Classes("pob-workspace") with
        {Preview=new(Width:440,Height:480){SizeToContentHeight=true},PreviewSource=passivePreviews!.Descriptor};
    }

    private static WidgetElement PassivePreview(JsonElement node)
    {
        var prefix="passive-preview."+I(node,"id");
        var rows=new List<WidgetElement>{Text(S(node,"name"),prefix+".title","pob-preview-title pob-gold")};
        rows.Add(Text(PassiveState(node),prefix+".state","pob-preview-meta"));
        AddPassivePreviewDescription(rows,node,prefix);
        if(S(node,"socketedItem").Length>0)rows.Add(Text("Socketed: "+S(node,"socketedItem"),prefix+".socket","pob-preview-meta"));
        if(B(node,"canChoose"))rows.Add(Text("Effect choice available",prefix+".choice","pob-preview-meta"));
        if(!B(node,"allocated")&&!B(node,"granted")&&B(node,"canAllocate")&&I(node,"pathCount")>0)
            rows.Add(Text("Allocation path: "+I(node,"pathCount")+" node(s)",prefix+".path","pob-preview-meta"));
        return UI.Stack("pob."+prefix,rows.ToArray()).Classes("pob-preview-content");
    }

    private static void AddPassivePreviewDescription(List<WidgetElement> rows,JsonElement node,string prefix)
    {
        var paragraphs=DescriptionParagraphs(node);
        foreach(var (paragraph,index) in paragraphs.Take(6).Select((paragraph,index)=>(paragraph,index)))
            AddRuns(rows,SummaryRuns(paragraph,600),prefix+".description."+index,"pob-text");
        if(paragraphs.Count==0)rows.Add(Text("No passive effect description supplied.",prefix+".empty","pob-preview-meta"));
        if(paragraphs.Count>6||paragraphs.Any(paragraph=>paragraph.Sum(run=>run.Text.Length)>600))
            rows.Add(Text("Open passive for the full description",prefix+".more","pob-preview-meta"));
    }

    internal static WidgetElement PassiveGroupPreview(PassiveEntry group)
    {
        var prefix="passive-preview."+group.Key;
        var states=group.Nodes.GroupBy(PassiveState).Select(state=>state.Count()+" "+state.Key.ToLowerInvariant());
        var rows=new List<WidgetElement>{Text(group.Name,prefix+".title","pob-preview-title pob-gold"),
            Text(group.Nodes.Length+" matching nodes · "+string.Join(" · ",states),prefix+".state","pob-preview-meta")};
        var first=ReadFormattedRuns(group.Nodes[0],"description","descriptionRuns");
        if(group.Nodes.All(node=>ReadFormattedRuns(node,"description","descriptionRuns").SequenceEqual(first)))
        {
            rows.Add(Text("Shared effect per node",prefix+".shared","pob-eyebrow pob-muted"));
            AddPassivePreviewDescription(rows,group.Nodes[0],prefix);
        }
        else rows.Add(Text("Effects differ between these nodes. Expand the group to inspect each passive.",prefix+".different","pob-preview-meta"));
        return UI.Stack("pob."+prefix,rows.ToArray()).Classes("pob-preview-content");
    }
}
