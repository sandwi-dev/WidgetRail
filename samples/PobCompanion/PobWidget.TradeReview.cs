using System.Text.Json;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

namespace WidgetRail.Samples.PobCompanion;

public sealed partial class PobWidget
{
    private bool websiteReview;
    private string websiteFocus="pob.website.browser";
    private long websiteFocusSequence;

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
            previewSlot=candidate.Slot;itemDraftSlots=slots;tradeBrowsing=true;SetSection("Items");detailKind="choose-slot";
            tradeCandidates.Clear();tradeCandidates.AddRange(recent);websiteReview=true;
            websiteFocus="pob.website.review.tools";websiteFocusSequence=++focusRequestSequence;
            rankingVisible=false;
            tradePurchaseExpanded=false;tradeWorkspaceFocus="pob.item.compare.controls";
        }
    }

    private bool HandleTradeReviewAction(WidgetActionEvent action)
    {
        var id=action.ActionId;
        if(HandleComparisonViewAction(id))return true;
        if(id=="trade-review-focus")
        {tradeWorkspaceFocus="pob.item.compare.controls";websiteFocus="pob.website.review.tools";websiteFocusSequence=++focusRequestSequence;return true;}
        if(id=="trade-review-expand")
        {websiteVisible=false;tradeWorkspaceFocus="pob.item.compare.controls";focusRequestSequence++;return true;}
        if(id=="trade-review-hide")
        {websiteReview=false;websiteFocus="pob.website.browser";websiteFocusSequence=++focusRequestSequence;return true;}
        if(id=="trade-review-show")
        {rankingVisible=false;websiteReview=true;websiteFocus="pob.website.review.tools";websiteFocusSequence=++focusRequestSequence;return true;}
        if(id=="trade-review-find"&&tradeListing is {} listing)
        {
            Work(async token=>
            {
                await OpenWebsiteAsync(listing.FindUrl(game),token);
                lock(gate){websiteFocus="pob.website.browser";websiteFocusSequence=++focusRequestSequence;}
            },"Finding saved listing…");return true;
        }
        if(id=="trade-review-adopt"&&tradeListing is {} legacy&&pastedItem is {} raw&&previewSlot is {} legacySlot)
        {
            Work(async token=>
            {
                await CompareTradeCandidateAsync(new(raw,legacy,legacySlot),pendingBefore??result!,itemDraftSlots,token);
                await OpenWebsiteAsync(legacy.FindUrl(game),token);
            },"Resuming comparisons…");return true;
        }
        if(!CanReplaceTradeComparison)return false;
        if(id=="trade-review-back")
        {
            Work(async token=>
            {
                await OpenWebsiteAsync(websiteUrl??tradeListing!.Url,token);
                lock(gate)
                {
                    rankingVisible=HasRanking;websiteReview=!rankingVisible;
                    websiteFocus=rankingVisible?(rankedItems.Length>0?"pob.rank.list":"pob.rank.tools"):"pob.website.review.tools";
                    websiteFocusSequence=++focusRequestSequence;
                }
            },"Returning to results…");return true;
        }
        if(id=="trade-review-close")
        {
            var before=pendingBefore??result!;
            Work(_=>
            {
                SaveWork(before,new(before.Xml,undo.ToArray(),redo.ToArray()),refreshXml:false);
                lock(gate){result=before;pendingBefore=null;pastedItem=null;tradeListing=null;tradeBrowsing=false;websiteReview=false;websiteVisible=false;detailKind="";focusRequestSequence++;}
                return Task.CompletedTask;
            },"Finishing comparison…");return true;
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
        var proposed=A(State,"items").FirstOrDefault(i=>S(i,"slot")==previewSlot);
        foreach(var (item,label,key) in new[]{(proposed,"Proposed item","proposed"),(equipped,"Equipped item","equipped")})
        {
            var warnings=A(item,"warnings");
            if(warnings.Length==0)continue;
            rows.Add(Text(label+": PoB does not model these modifiers; the comparison uses its recognized effects.",prefix+".warnings."+key,"pob-small pob-gold"));
            for(var i=0;i<warnings.Length;i++)rows.Add(Text(warnings[i].GetString()??"",prefix+".warnings."+key+"."+i,"pob-small pob-life"));
        }
        AddComparison(rows,before.State,State,prefix+".stats");
        rows.Add(Text("Against "+S(equipped,"name","empty slot")+" · active loadout and configuration. Build unchanged until equipped.",prefix+".help","pob-small"));
        return rows;
    }

    private WidgetElement TradeReviewPanel() => BuildTradeComparison("website.review", expanded: false);

    private WidgetElement EquipmentComparisonWorkspace() => BuildTradeComparison("item.compare", expanded: true);
}
