using System.Text.Json;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

namespace WidgetRail.Samples.PobCompanion;

public sealed partial class PobWidget
{
    private bool websiteReview;
    private string websiteFocus="pob.website.browser";
    private long websiteFocusSequence;
    private long websiteReviewRevealSequence;

    private async Task CompareTradeCandidateAsync(PobTradeCandidate candidate,PobResult before,string[] slots,CancellationToken token)
    {
        if(!slots.Contains(candidate.Slot))throw new IOException("This item no longer fits that slot. Choose another item or slot.");
        var next=await engine.ExecuteAsync(new{op="item",raw=candidate.Raw,slot=candidate.Slot},token,before.Xml,compare:true);
        var recent=tradeCandidates.Where(c=>c.Listing.Id!=candidate.Listing.Id).Prepend(candidate).Take(8).ToArray();
        token.ThrowIfCancellationRequested();
        SaveWork(before,new(before.Xml,undo.ToArray(),redo.ToArray(),new(next.Xml,"item",Slot:candidate.Slot),
            candidate.Raw,candidate.Listing,true,recent),refreshXml:false);
        lock(gate)
        {
            pendingBefore=before;result=next;tradeListing=candidate.Listing;pastedItem=candidate.Raw;
            previewSlot=candidate.Slot;itemDraftSlots=slots;tradeBrowsing=true;section="Items";detailKind="choose-slot";
            tradeCandidates.Clear();tradeCandidates.AddRange(recent);websiteReview=true;
            websiteFocus="pob.website.review";websiteFocusSequence=++focusRequestSequence;
            websiteReviewRevealSequence=focusRequestSequence;
        }
    }

    private bool HandleTradeReviewAction(WidgetActionEvent action)
    {
        var id=action.ActionId;
        if(id=="trade-review-details")
        {websiteFocus="pob.website.review.reading";websiteFocusSequence=++focusRequestSequence;return true;}
        if(id=="trade-review-browse")
        {websiteFocus="pob.website.browser";websiteFocusSequence=++focusRequestSequence;return true;}
        if(id=="trade-review-focus")
        {websiteFocus="pob.website.review";websiteFocusSequence=++focusRequestSequence;return true;}
        if(id=="trade-review-expand")
        {websiteVisible=false;focusRequestSequence++;return true;}
        if(id=="trade-review-hide")
        {websiteReview=false;websiteFocus="pob.website.browser";websiteFocusSequence=++focusRequestSequence;return true;}
        if(id=="trade-review-show")
        {websiteReview=true;websiteFocus="pob.website.review";websiteFocusSequence=++focusRequestSequence;return true;}
        if(id=="trade-review-find"&&tradeListing is {} listing)
        {Work(token=>OpenWebsiteAsync(listing.FindUrl(game),token),"Finding saved listing…");return true;}
        if(id=="trade-review-adopt"&&tradeListing is {} legacy&&pastedItem is {} raw&&previewSlot is {} legacySlot)
        {
            Work(async token=>
            {
                await CompareTradeCandidateAsync(new(raw,legacy,legacySlot),pendingBefore??result!,itemDraftSlots,token);
                await OpenWebsiteAsync(legacy.FindUrl(game),token);
            },"Resuming comparisons…");return true;
        }
        if(!CanReplaceTradeComparison)return false;
        if(id=="trade-review-close")
        {
            var before=pendingBefore??result!;
            Work(_=>
            {
                SaveWork(before,new(before.Xml,undo.ToArray(),redo.ToArray()),refreshXml:false);
                lock(gate){result=before;pendingBefore=null;pastedItem=null;tradeListing=null;tradeBrowsing=false;websiteReview=false;detailKind="";focusRequestSequence++;}
                return Task.CompletedTask;
            },"Closing comparison…");return true;
        }
        if(id.StartsWith("trade-review-slot.",StringComparison.Ordinal)&&int.TryParse(id[18..],out var slot)&&slot>=0&&slot<itemDraftSlots.Length)
        {
            var candidate=new PobTradeCandidate(pastedItem!,tradeListing!,itemDraftSlots[slot]);
            Work(token=>CompareTradeCandidateAsync(candidate,pendingBefore??result!,itemDraftSlots,token),"Comparing equipment…");return true;
        }
        if(id.StartsWith("trade-review-recent.",StringComparison.Ordinal))
        {
            var candidate=tradeCandidates.FirstOrDefault(c=>c.Listing.Id==id[20..]);
            if(candidate is not null)Work(async token=>
            {
                var before=pendingBefore??result!;
                var slots=await ItemSlotsAsync(candidate.Raw,before.Xml,token);
                if(slots.Length==0)throw new IOException("This item has no compatible slot in the current build.");
                await CompareTradeCandidateAsync(candidate with{Slot=slots.Contains(candidate.Slot)?candidate.Slot:slots[0]},before,slots,token);
            },"Comparing saved listing…");
            return true;
        }
        if(id=="trade-review-equip"&&pendingBefore is {} beforeEquip)
        {
            var next=result!;
            Work(async token=>
            {
                token.ThrowIfCancellationRequested();
                SaveWork(next,new(next.Xml,undo.Prepend(beforeEquip.Xml).Take(12).ToArray(),[]));
                lock(gate)
                {
                    PushHistory(undo,beforeEquip.Xml);redo.Clear();pendingBefore=null;pastedItem=null;tradeBrowsing=false;
                    detailKind="item";var item=A(State,"items").FirstOrDefault(i=>S(i,"slot")==previewSlot);
                    detailId=I(item,"id");selections["item"]=detailId;websiteReview=false;
                    ShowFeedback("Equipped in build","Your game character is unchanged. New comparisons use this equipment.",ToastTone.Info);
                    websiteFocus="pob.website.browser";websiteFocusSequence=++focusRequestSequence;
                }
                await RefreshCommittedMapAsync(token);
            },"Equipping in build…");return true;
        }
        return false;
    }

    private List<WidgetElement> TradeReviewControls(string prefix,bool inWebsite)
    {
        var rows=new List<WidgetElement>();
        if(tradeCandidates.Count>1)
            rows.Add(UI.Select("Recent comparisons",tradeCandidates.Select(c=>new SelectOption(c.Listing.Id,
                TradeItemName(c.Listing)+" · "+c.Listing.Price,"trade-review-recent."+c.Listing.Id,c.Listing.Id==tradeListing?.Id)).ToArray(),"pob."+prefix+".recent").Classes("pob-select"));
        if(itemDraftSlots.Length>1)
            rows.Add(UI.Select("Compare with",itemDraftSlots.Select((slot,i)=>new SelectOption("slot"+i,slot,"trade-review-slot."+i,slot==previewSlot)).ToArray(),"pob."+prefix+".slot").Classes("pob-select"));
        rows.Add(UI.Row("pob."+prefix+".actions",
            Button(inWebsite?"Browse listings":websiteSession is null?"Find saved listing":"Return to listings",inWebsite?"trade-review-browse":"website-resume",prefix+".browse"),
            Button("Equip in build","trade-review-equip",prefix+".equip").Disabled(pendingBefore is null)).Classes("pob-toolbar"));
        if(!inWebsite)rows.Add(Button("Close comparison","trade-review-close",prefix+".close"));
        return rows;
    }

    private static string TradeItemName(PobTradeListing listing)=>!string.IsNullOrEmpty(listing.ItemName)&&!string.IsNullOrEmpty(listing.BaseType)&&
        (listing.ItemName==listing.BaseType||listing.ItemName.EndsWith(", "+listing.BaseType,StringComparison.Ordinal))?listing.ItemName:
        string.Join(", ",new[]{listing.ItemName,listing.BaseType}.Where(s=>!string.IsNullOrWhiteSpace(s)));
    private static string TradeMethod(PobTradeListing listing)=>listing.InstantBuyout switch
    {true=>"Instant buyout",false=>"In-person trade",_=>"Check purchase options on the official listing"};

    private void AddTradeContext(List<WidgetElement> rows,string prefix)
    {
        if(tradeListing is not {} listing)return;
        rows.Add(Text(listing.Price+" · "+listing.Seller,prefix+".price","pob-gold"));
        rows.Add(Text(TradeMethod(listing),prefix+".method","pob-small"));
        if(listing.Fee is not null)rows.Add(Text("Gold fee: "+listing.Fee,prefix+".fee","pob-small"));
        if(listing.Availability is not null)rows.Add(Text(listing.Availability,prefix+".availability","pob-small"));
        rows.Add(Text("Captured listing · confirm price and availability on the website.",prefix+".freshness","pob-small"));
    }

    private List<WidgetElement> TradeImpact(string prefix)
    {
        var before=pendingBefore!;
        var equipped=A(before.State,"items").FirstOrDefault(i=>S(i,"slot")==previewSlot);
        var rows=new List<WidgetElement>{Text("Main skill: "+S(before.State,"mainSkill","None"),prefix+".skill","pob-small")};
        AddComparison(rows,before.State,State,prefix+".stats");
        rows.Add(Text("Against "+S(equipped,"name","empty slot")+" · active loadout and configuration. Build unchanged until equipped.",prefix+".help","pob-small"));
        return rows;
    }

    private WidgetElement TradeReviewPanel()
    {
        const string prefix="website.review";
        var item=A(State,"items").FirstOrDefault(i=>S(i,"slot")==previewSlot);
        var details=TradeImpact(prefix);
        var reading=new List<WidgetElement>{Button("Expand comparison","trade-review-expand",prefix+".expand"),
            Text("Proposed item",prefix+".item.heading","pob-heading")};
        reading.AddRange(ItemDetails(item,prefix+".item"));
        reading.Add(Text("Purchase details",prefix+".purchase.heading","pob-heading"));
        AddTradeContext(reading,prefix);
        reading.Add(Button("Find this seller’s item","trade-review-find",prefix+".find"));
        reading.Add(Button("Hide panel","trade-review-hide",prefix+".hide"));
        details.Add(UI.Stack("pob.website.review.reading",reading.ToArray()).RememberChildFocus("pob."+prefix+".expand").Classes("pob-buildtools"));
        var controls=TradeReviewControls(prefix,true);
        controls.Insert(0,Text(S(item,"name",TradeItemName(tradeListing!)),prefix+".title","pob-heading"));
        controls.Insert(1,Text(tradeListing!.Price+" · "+previewSlot,prefix+".summary","pob-gold"));
        var tools=UI.Stack("pob.website.review.tools",controls.ToArray()).RememberChildFocus("pob."+prefix+".browse").Classes("pob-buildtools");
        var scroll=UI.VerticalScroll("pob.website.review.details",details.ToArray()).Classes("pob-scroll")
            .Shortcut(ControllerButton.LeftStick,"trade-review-focus","Comparison controls");
        return UI.Stack("pob.website.review",tools,scroll).RememberChildFocus("pob."+prefix+".browse")
            .Shortcut(ControllerButton.X,"trade-review-details","Item details").Classes("pob-panel","pob-tree-main");
    }

    private WidgetElement EquipmentComparisonWorkspace()
    {
        var item=A(State,"items").FirstOrDefault(i=>S(i,"slot")==previewSlot);
        var left=new List<WidgetElement>{Text("Proposed item","item.compare.proposed","pob-eyebrow")};
        if(tradeListing is {} listing)left.Add(Text(listing.Price,"item.compare.price","pob-gold"));
        if(item.ValueKind==JsonValueKind.Object)left.AddRange(ItemDetails(item));
        if(tradeListing is not null)left.Add(Text("Purchase details","item.compare.purchase.heading","pob-heading"));
        AddTradeContext(left,"item.compare.listing");
        var right=new List<WidgetElement>{Text("Equipment comparison · "+previewSlot,"item.compare.heading","pob-heading"),
            Text("Replacing "+S(A(pendingBefore!.State,"items").FirstOrDefault(i=>S(i,"slot")==previewSlot),"name","empty slot")+" with "+S(item,"name"),"item.compare.names")};
        right.AddRange(TradeImpact("item.compare"));
        if(!CanReplaceTradeComparison)
        {
            AddTradeListing(right);
            if(tradeListing is not null)right.Add(Button("Keep this item and continue comparing","trade-review-adopt"));
        }
        var leftPanel=(ContainerElement)Panel("item.compare.item",left);
        var rightPanel=(ContainerElement)Panel("item.compare.impact",right);
        if(CanReplaceTradeComparison)
        {
            if(NeedsQuickControls(left.Count))leftPanel=leftPanel.Shortcut(ControllerButton.LeftStick,"trade-review-focus","Comparison controls");
            if(NeedsQuickControls(right.Count))rightPanel=rightPanel.Shortcut(ControllerButton.LeftStick,"trade-review-focus","Comparison controls");
        }
        var columns=Split("equipment-comparison",leftPanel,rightPanel,1,1);
        if(!CanReplaceTradeComparison)return columns;
        var selectors=new List<WidgetElement>();
        if(tradeCandidates.Count>1)selectors.Add(UI.Select("Recent comparisons",tradeCandidates.Select(c=>new SelectOption(c.Listing.Id,
            TradeItemName(c.Listing)+" · "+c.Listing.Price,"trade-review-recent."+c.Listing.Id,c.Listing.Id==tradeListing?.Id)).ToArray(),"pob.item.compare.recent").Classes("pob-select"));
        if(itemDraftSlots.Length>1)selectors.Add(UI.Select("Compare with",itemDraftSlots.Select((slot,i)=>new SelectOption("slot"+i,slot,"trade-review-slot."+i,slot==previewSlot)).ToArray(),"pob.item.compare.slot").Classes("pob-select"));
        else selectors.Add(Text("COMPARISON · "+previewSlot+" · not equipped","item.compare.status","pob-eyebrow pob-gold"));
        var controls=UI.Stack("pob.item.compare.controls",
            UI.Row("pob.item.compare.selectors",selectors.ToArray()).Classes("pob-toolbar"),
            UI.Row("pob.item.compare.actions",
                Button(websiteSession is null?"Find saved listing":"Return to listings","website-resume","item.compare.browse"),
                Button("Equip in build","trade-review-equip","item.compare.equip"),
                Button("Find seller’s item","trade-review-find","item.compare.find"),
                Button("Close comparison","trade-review-close","item.compare.close")).Classes("pob-toolbar"))
            .RememberChildFocus("pob.item.compare.browse").Classes("pob-buildtools");
        return UI.Stack("pob.item.compare.page",controls,columns).Classes("pob-workspace","pob-tree-main");
    }
}
