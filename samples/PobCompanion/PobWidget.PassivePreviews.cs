using System.Text.Json;
using WidgetRail.WidgetSdk;

namespace WidgetRail.Samples.PobCompanion;

public sealed partial class PobWidget
{
    private sealed record PassivePreviewContext(ListQuery Query,PobResult Build,string? Document);
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
                    if(!key.StartsWith("node.",StringComparison.Ordinal)||!int.TryParse(key[5..],out var id)||!context.Query.Entries.Any(entry=>entry.Key==key))
                        throw new ArgumentException("Unknown passive preview key.");
                    var node=A(context.Build.State,"nodes").Single(node=>I(node,"id")==id);
                    return ValueTask.FromResult(PassivePreview(node));
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
        rows.Add(Text(B(node,"granted")?"Granted":B(node,"allocated")?"Allocated":"Unallocated",prefix+".state","pob-preview-meta"));
        var paragraphs=DescriptionParagraphs(node);
        foreach(var (paragraph,index) in paragraphs.Take(6).Select((paragraph,index)=>(paragraph,index)))
            AddRuns(rows,SummaryRuns(paragraph,600),prefix+".description."+index,"pob-text");
        if(paragraphs.Count==0)rows.Add(Text("No passive effect description supplied.",prefix+".empty","pob-preview-meta"));
        if(paragraphs.Count>6||paragraphs.Any(paragraph=>paragraph.Sum(run=>run.Text.Length)>600))
            rows.Add(Text("Open passive for the full description",prefix+".more","pob-preview-meta"));
        if(S(node,"socketedItem").Length>0)rows.Add(Text("Socketed: "+S(node,"socketedItem"),prefix+".socket","pob-preview-meta"));
        if(B(node,"canChoose"))rows.Add(Text("Effect choice available",prefix+".choice","pob-preview-meta"));
        if(!B(node,"allocated")&&!B(node,"granted")&&B(node,"canAllocate")&&I(node,"pathCount")>0)
            rows.Add(Text("Allocation path: "+I(node,"pathCount")+" node(s)",prefix+".path","pob-preview-meta"));
        return UI.Stack("pob."+prefix,rows.ToArray()).Classes("pob-preview-content");
    }
}
