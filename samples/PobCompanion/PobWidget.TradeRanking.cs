using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

namespace WidgetRail.Samples.PobCompanion;

public sealed partial class PobWidget
{
    private sealed record RankingInput(PobTradeListing Listing,string? Raw,JsonElement Entry);
    private sealed record RankedItem(int Index,PobTradeCandidate Candidate,JsonElement Evaluation);
    private sealed record RankingSave(string Baseline,string Slot,string Weights,RankedItem[] Rows,string[] Skipped,string Sort="strength",CurrencyRateSnapshot? Rates=null);
    private readonly List<RankingInput> rankingInputs=[];
    private RankedItem[] rankedItems=[];
    private string[] rankingSkipped=[];
    private string rankingSlot="",rankingBaseline="",rankingWeights="",rankingBuild="",rankingSummary="";
    private bool rankingVisible, rankingEditing, rankingSkippedExpanded;
    private string rankingSort="strength";
    private CurrencyRateSnapshot? rankingRates;
    private static readonly (string Key,string Label)[] RankingSorts=[("strength","Strongest improvement"),("value","PoB price-adjusted score"),("price","Lowest price")];
    private JsonElement[] rankingSlots=[];
    private bool RankingTargetAvailable=>rankingSlots.Any(s=>S(s,"name")==rankingSlot);
    private string rankingSelected="";
    private sealed record RankEntry(string Key,string Label,string Price,string Summary,string Tone,string Action,bool Selected,int WarningCount);
    private sealed record RankQuery(string Context,RankEntry[] Entries);
    private WidgetIndexedCollection<RankQuery,RankEntry>? rankingCollection;
    private RankQuery rankingQuery=new("",[]);
    private static readonly Regex LuaErrorLocation=new("^(?:[^\\r\\n]*?\\.lua|\\[string \"[^\\r\\n]*?\"\\]):[0-9]+:\\s*",RegexOptions.CultureInvariant|RegexOptions.NonBacktracking);
    internal static string RankingError(string error)
    {
        // Lua can wrap an upstream error in more than one source location.
        // Strip those prefixes, retaining meaningful colons in the actual reason.
        while(LuaErrorLocation.Match(error) is {Success:true} match)error=error[match.Length..];
        return error;
    }
    internal static string SavedRankingError(string text)
    {
        var separator=text.IndexOf(": ",StringComparison.Ordinal);
        return separator<0?RankingError(text):text[..(separator+2)]+RankingError(text[(separator+2)..]);
    }
    private static string XmlHash(string xml)=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(xml)));
    private string RankingWeightKey()=>JsonSerializer.Serialize(new{tradePriority,weights=TradeWeights()});
    private bool RankingStale=>rankingBuild!=documentId||rankingBaseline!=XmlHash((pendingBefore??result)!.Xml)||rankingWeights!=RankingWeightKey();
    private bool HasRanking=>rankingBuild==documentId&&(rankedItems.Length>0||rankingInputs.Count>0);
    private async Task LoadAndEvaluateTradeAsync(CancellationToken token)
    {
        var session=websiteSession??throw new IOException("Open the trade website first.");
        var limit=tradeBatchLimit;
        var reply=await session.RequestAsync<object,JsonElement>("load-results",new{limit},token);
        var context=reply.GetProperty("context");var entries=A(reply,"items");
        token.ThrowIfCancellationRequested();
        lock(gate)ReadTradeBatch(context,entries,false);
        var shortBatch=entries.Length<limit;
        await EvaluateTradeBatchAsync(token);
        if(shortBatch)lock(gate){rankingSummary+=$" · Website supplied {entries.Length} of up to {limit}";}
    }

    private void ReceiveTradeBatch(JsonElement context,JsonElement[] entries)
    {ReadTradeBatch(context,entries,true);}
    private void ReadTradeBatch(JsonElement context,JsonElement[] entries,bool start)
    {
        var preserveSlot=HasRanking;ReadTradePreferences();rankingInputs.Clear();var skipped=new List<string>();var ids=new HashSet<string>();
        foreach(var entry in entries.Take(100))
        {
            try
            {
                var payload=JsonSerializer.SerializeToElement(new{url=S(context,"url"),entry});
                var listing=ReadTradeListing(payload,out var item);
                if(ids.Add(listing.Id))rankingInputs.Add(new(listing,null,item.Clone()));
            }
            catch(IOException error){skipped.Add(Limit(error.Message,250));}
        }
        if(rankingInputs.Count==0)throw new IOException("No valid listings were received. Load trade results and evaluate again.");
        if(!preserveSlot)rankingSlot=tradeSlot.Length>0?tradeSlot:previewSlot??"Weapon 1";
        rankingSkipped=skipped.ToArray();rankedItems=[];rankingBuild=documentId??"";rankingVisible=true;
        if(start)Work(EvaluateTradeBatchAsync,"Preparing listing evaluation…");
    }
    private async Task EvaluateTradeBatchAsync(CancellationToken token)
    {
        var before=pendingBefore??result!;var baseline=XmlHash(before.Xml);var weights=RankingWeightKey();
        var inputs=rankingInputs.Take(tradeBatchLimit).ToArray();var slot=rankingSlot;var evaluated=new List<RankedItem>();var skipped=new List<string>();
        var timer=Stopwatch.StartNew();
        try
        {
            var catalog=(await engine.ExecuteAsync(new{op="trade-catalog",slot},token,before.Xml)).State;
            lock(gate){rankingSlots=A(catalog,"slots");rankingVisible=true;websiteFocus="pob.rank.tools";websiteFocusSequence=++focusRequestSequence;}
            if(!rankingSlots.Any(s=>S(s,"name")==slot))throw new IOException("Choose an active equipment slot for evaluation.");
            await engine.ExecuteAsync(new{op="trade-rank-begin",slot,priority=tradePriority,weights=TradeWeights()},token,before.Xml);
            for(var offset=0;offset<inputs.Length;offset+=10)
            {
                token.ThrowIfCancellationRequested();var batch=inputs.Skip(offset).Take(10).ToArray();
                lock(gate)status=$"Evaluating {offset+1}–{offset+batch.Length} of {inputs.Length} listings…";Invalidate();
                var request=new{op="trade-rank-items",items=batch.Select(input=>input.Raw is null?(object)new{entry=input.Entry}:new{raw=input.Raw}).ToArray()};
                var responses=A((await engine.ExecuteAsync(request,token,before.Xml)).State,"results");
                if(responses.Length!=batch.Length)throw new IOException("PoB returned an incomplete evaluation batch. Evaluate again.");
                for(var j=0;j<batch.Length;j++)
                {
                var input=batch[j];var response=responses[j];
                var error=RankingError(S(response,"error"));
                if(error.Length>0){skipped.Add(TradeItemName(input.Listing)+": "+error);continue;}
                var evaluation=JsonSerializer.SerializeToElement(response.EnumerateObject().Where(p=>p.Name!="raw").ToDictionary(p=>p.Name,p=>p.Value));
                evaluated.Add(new(I(response,"index"),new(S(response,"raw"),input.Listing,slot),evaluation));
                }
            }
            var sorted=await engine.ExecuteAsync(new{op="trade-rank-finish"},token,before.Xml);
            var rows=A(sorted.State,"order").Select(index=>evaluated.Single(r=>r.Index==index.GetInt32())).ToArray();
            token.ThrowIfCancellationRequested();
            (RankedItem[] Rows,CurrencyRateSnapshot? Rates) ordered;
            string? sortingNotice=null;
            try{ordered=await SortRankingAsync(rows,rankingSort,before.Xml,token);}
            catch(Exception error)when(rankingSort!="strength"&&error is IOException or HttpRequestException)
            {ordered=(rows,null);sortingNotice="Price order unavailable: "+RankingError(error.Message)+" Showing strongest improvement.";}
            rows=ordered.Rows;var finalOrder=sortingNotice is null?rankingSort:"strength";
            var saved=new RankingSave(baseline,slot,weights,rows,skipped.ToArray(),finalOrder,ordered.Rates);
            var savedJson=JsonSerializer.Serialize(saved);
            if(Encoding.UTF8.GetByteCount(savedJson)>5*1024*1024)throw new IOException("The evaluation exceeds the saved-results size limit. Evaluate a smaller batch.");
            BuildDocument.SaveAtomic(Path.Combine(dataDirectory,documentId+".ranking.json"),savedJson);
            lock(gate)
            {
                rankingSort=finalOrder;rankingRates=ordered.Rates;rankedItems=rows;rankingSkipped=skipped.ToArray();rankingBaseline=baseline;rankingWeights=weights;rankingBuild=documentId??"";
                if(sortingNotice is not null)ShowFeedback("Price ordering unavailable",sortingNotice,ToastTone.Warning);
                rankingSummary=$"{rows.Length} ranked · {skipped.Count} skipped · {inputs.Length} of {rankingInputs.Count} loaded evaluated · {timer.Elapsed.TotalSeconds:F1}s";
                rankingVisible=true;rankingEditing=false;rankingSkippedExpanded=false;websiteFocus="pob.rank.tools";websiteFocusSequence=++focusRequestSequence;
            }
        }
        catch(OperationCanceledException)
        {
            lock(gate){rankingSummary="Evaluation cancelled. Choose Evaluate again to restart.";rankingVisible=true;}
            throw;
        }
    }
    private void RestoreRanking(string id)
    {
        ReadTradePreferences();
        rankingInputs.Clear();rankedItems=[];rankingSkipped=[];rankingVisible=false;rankingEditing=false;rankingSkippedExpanded=false;rankingBuild=id;rankingBaseline="";rankingSummary="";rankingSelected="";rankingSort="strength";rankingRates=null;
        try
        {
            var file=new FileInfo(Path.Combine(dataDirectory,id+".ranking.json"));if(!file.Exists||file.Length>5*1024*1024)return;
            var saved=JsonSerializer.Deserialize<RankingSave>(File.ReadAllText(file.FullName));
            if(saved is null||saved.Rows is not{Length:<=100}||saved.Slot is not{Length:>0 and <=128}||saved.Weights is not{Length:<=16384}||saved.Baseline is not{Length:64})return;
            if(saved.Rows.Select(r=>r.Index).Distinct().Count()!=saved.Rows.Length||saved.Rows.Any(r=>r.Index is <1 or >100||r.Candidate.Raw is not{Length:>0 and <=32768}||r.Candidate.Slot!=saved.Slot||!r.Candidate.Listing.IsValid(game)||!ValidRankingEvaluation(r.Evaluation)))return;
            if(saved.Rows.Select(r=>r.Candidate.Listing.Id).Distinct().Count()!=saved.Rows.Length)return;
            rankingSort=RankingSorts.Any(s=>s.Key==saved.Sort)?saved.Sort:"strength";rankingRates=saved.Rates;
            rankedItems=saved.Rows;rankingSlot=saved.Slot;rankingBaseline=saved.Baseline;rankingWeights=saved.Weights;rankingSkipped=(saved.Skipped??[]).Take(100).Select(s=>Limit(SavedRankingError(s??""),600)).ToArray();
            rankingInputs.AddRange(rankedItems.Select(r=>new RankingInput(r.Candidate.Listing,r.Candidate.Raw,default)));
            rankingSummary=$"{rankedItems.Length} saved results · check current website availability";
        }
        catch(Exception error)when(error is IOException or JsonException or InvalidOperationException or NullReferenceException or UnauthorizedAccessException){rankedItems=[];rankingInputs.Clear();}
    }
    private static bool ValidRankingEvaluation(JsonElement value)
    {
        static bool Number(JsonElement v)=>v.ValueKind==JsonValueKind.Number&&v.TryGetDouble(out var n)&&double.IsFinite(n);
        if(value.ValueKind!=JsonValueKind.Object||!value.TryGetProperty("score",out var score)||!Number(score))return false;
        foreach(var key in new[]{"scorePercent","baselineScore"})if(value.TryGetProperty(key,out var v)&&!Number(v))return false;
        if(value.TryGetProperty("warnings",out var warnings)&&
            (warnings.ValueKind!=JsonValueKind.Array||warnings.GetArrayLength()>100||warnings.EnumerateArray().Any(w=>w.ValueKind!=JsonValueKind.String||w.GetString() is not {Length:>0 and <=2048})))return false;
        if(!value.TryGetProperty("changes",out var changes)||changes.ValueKind!=JsonValueKind.Array||changes.GetArrayLength()>20)return false;
        return changes.EnumerateArray().All(c=>c.ValueKind==JsonValueKind.Object&&c.TryGetProperty("stat",out var s)&&s.ValueKind==JsonValueKind.String&&s.GetString()is{Length:>0 and <=64}&&
            new[]{"before","after","delta"}.All(k=>c.TryGetProperty(k,out var v)&&Number(v)));
    }
    private bool HandleRankingAction(WidgetActionEvent action)
    {
        var id=action.ActionId;var parts=id.Split('.');
        if(id.StartsWith("trade-rank-",StringComparison.Ordinal)&&((pendingBefore is not null||pastedItem is not null)&&!CanReplaceTradeComparison||result is null))
        {ShowFeedback("Finish current work","Finish the build edit or item draft before evaluating trade items.",ToastTone.Warning);return true;}
        if(parts is ["trade-rank-sort",var order]&&RankingSorts.Any(s=>s.Key==order)&&rankedItems.Length>0)
        {
            if(RankingStale){ShowFeedback("Evaluate again","The build, target or weights changed. Recalculate before sorting.",ToastTone.Warning);return true;}
            Work(async token=>
            {
                var sorted=await SortRankingAsync(rankedItems,order,(pendingBefore??result)!.Xml,token);
                BuildDocument.SaveAtomic(Path.Combine(dataDirectory,documentId+".ranking.json"),JsonSerializer.Serialize(new RankingSave(rankingBaseline,rankingSlot,rankingWeights,sorted.Rows,rankingSkipped,order,sorted.Rates)));
                lock(gate){rankedItems=sorted.Rows;rankingSort=order;rankingRates=sorted.Rates;}
            },"Sorting evaluated listings…");return true;
        }
        if(id=="trade-rank-edit"){rankingEditing=!rankingEditing;websiteFocus="pob.rank.tools";websiteFocusSequence=++focusRequestSequence;return true;}
        if(id=="trade-rank-skipped"){rankingSkippedExpanded=!rankingSkippedExpanded;return true;}
        if(id=="trade-rank-hide"){rankingVisible=false;websiteReview=false;websiteFocus="pob.website.browser";websiteFocusSequence=++focusRequestSequence;return true;}
        if(id=="trade-rank-focus"){websiteFocus="pob.rank.tools";websiteFocusSequence=++focusRequestSequence;return true;}
        if(id=="trade-rank-show"&&HasRanking)
        {
            rankingVisible=true;
            Work(async token=>{await ReadRankingSlotsAsync(token);await OpenWebsiteAsync(websiteUrl??rankingInputs[0].Listing.Url,token);lock(gate){websiteFocus=rankedItems.Length>0?"pob.rank.list":"pob.rank.tools";websiteFocusSequence=++focusRequestSequence;}},"Opening evaluated results…");return true;
        }
        if(id=="trade-rank-recent"&&tradeCandidates.Count>0)
        {
            rankingInputs.Clear();rankingInputs.AddRange(tradeCandidates.Select(c=>new RankingInput(c.Listing,c.Raw,default)));
            rankingSlot=previewSlot??tradeSlot;rankingBuild=documentId??"";rankingVisible=true;
            Work(async token=>{await OpenWebsiteAsync(websiteUrl??rankingInputs[0].Listing.Url,token);await EvaluateTradeBatchAsync(token);},"Evaluating recent comparisons…");return true;
        }
        if(id=="trade-rank-evaluate"&&HasRanking)
        {
            if(!RankingTargetAvailable){rankingEditing=true;ShowFeedback("Choose an active target","The saved equipment slot is unavailable in this loadout. Choose a target in Edit ranking before evaluating.",ToastTone.Info);return true;}
            Work(LoadAndEvaluateTradeAsync,"Loading trade results…");return true;
        }
        if(parts is ["trade-rank-limit",var count]&&int.TryParse(count,out var limit)&&limit is 20 or 50 or 100)
        {tradeBatchLimit=limit;SaveTradePreferences();return true;}
        if(parts is ["trade-rank-priority",var priority]&&TradePriorities.Any(p=>p.Key==priority))
        {tradePriority=priority;SaveTradePreferences();return true;}
        if(parts is ["trade-rank-slot",var slot]&&int.TryParse(slot,out var si)&&si>=0&&si<rankingSlots.Length)
        {rankingSlot=S(rankingSlots[si],"name");rankingBaseline="";return true;}
        if(parts is ["trade-rank-select",var selected]&&int.TryParse(selected,out var ri)&&rankedItems.FirstOrDefault(r=>r.Index==ri)is {} row)
        {
            rankingSelected=row.Candidate.Listing.Id;
            Work(async token=>
            {
                var before=pendingBefore??result!;var slots=await ItemSlotsAsync(row.Candidate.Raw,before.Xml,token);
                if(!slots.Contains(rankingSlot))throw new IOException("This item does not fit the selected target. Choose another slot and evaluate again.");
                await CompareTradeCandidateAsync(row.Candidate with{Slot=rankingSlot},before,slots,token);
                lock(gate)rankingVisible=false;
            },"Comparing selected result…");return true;
        }
        return false;
    }
    private async Task ReadRankingSlotsAsync(CancellationToken token)
    {var catalog=(await engine.ExecuteAsync(new{op="trade-catalog",slot=rankingSlot},token,(pendingBefore??result)!.Xml)).State;lock(gate)rankingSlots=A(catalog,"slots");}
    private string RankingRowSummary(RankedItem row,bool stale)
    {
        if(stale)return "Build, target or weights changed · evaluate again";
        var score=row.Evaluation.TryGetProperty("scorePercent",out var percent)?$"Weighted change {percent.GetDouble():+0.0;-0.0;0}%":$"PoB score {row.Evaluation.GetProperty("score").GetDouble():0.###}"+(row.Evaluation.TryGetProperty("baselineScore",out var baseline)?$" · Equipped {baseline.GetDouble():0.###}":"");
        var damage=A(row.Evaluation,"changes").FirstOrDefault(c=>S(c,"stat")=="CombinedDPS");
        return score+(damage.ValueKind==JsonValueKind.Object?$" · Player DPS {damage.GetProperty("delta").GetDouble():+#,0.##;-#,0.##;0}":"");
    }
    private WidgetElement RankingTargetControl()
    {
        var options=rankingSlots.Select((s,i)=>new SelectOption("slot"+i,S(s,"title"),"trade-rank-slot."+i,S(s,"name")==rankingSlot)).ToList();
        return SelectWithUnavailable("Compare against",options,"pob.rank.slot",
            (rankingSlot.Length>0?rankingSlot:"Saved target")+" — unavailable in this loadout").FocusLeft("pob.website.browser").Classes("pob-select");
    }
    private WidgetElement TradeRankingPanel()
    {
        rankingCollection??=CreateIndexedCollection<RankQuery,RankEntry>("pob.rank.source",rankingQuery,0,new()
        {
            ReadRange=(q,start,count,token)=>ValueTask.FromResult<IReadOnlyList<RankEntry>>(q.Entries.Skip(start).Take(count).ToArray()),ItemKey=e=>new(e.Key),
            RenderItem=(_,e,c)=>UI.ActionSurface("select",c.Id("row"),e.Label+" · "+e.Price+(e.WarningCount>0?$" · PoB does not model {e.WarningCount} modifiers":""),ActionSurfaceOrientation.Vertical,
                UI.Grid(c.Id("identity"),[GridTrack.Auto()],[GridTrack.Star(),GridTrack.Auto()],
                    UI.Text(e.Label,c.Id("title")).Classes("pob-list-title","pob-rank-title"),
                    UI.Text(e.Price,c.Id("price")).Classes("pob-small","pob-gold").InGrid(column:1)).Spacing(0,8),
                UI.RichText(c.Id("summary"),
                    UI.Text(e.Summary.Split(" · Player DPS",2)[0],c.Id("score")).Classes("pob-small","pob-inline",e.Tone),
                    UI.Text(e.Summary.Contains(" · Player DPS",StringComparison.Ordinal)?" · Player DPS"+e.Summary.Split(" · Player DPS",2)[1]:"",c.Id("damage")).Classes("pob-small","pob-inline","pob-muted"),
                    UI.Text(e.WarningCount>0?$" · {e.WarningCount} unmodeled":"",c.Id("warnings")).Classes("pob-small","pob-inline","pob-gold"))
                    .Classes("pob-small","pob-rank-metrics")).Selected(e.Selected).FocusLeft("pob.website.browser").Classes("pob-list-item","pob-rank-item"),
            OnAction=(q,e,a,token)=>{lock(gate){if(!ReferenceEquals(q,rankingQuery)||busy||a.ActionId!="select")return ValueTask.CompletedTask;return OnActionAsync(a with{ActionId=e.Action},token);}}
        });
        var stale=RankingStale;
        var entries=rankedItems.Select((r,i)=>new RankEntry(r.Candidate.Listing.Id,$"{i+1}. {TradeItemName(r.Candidate.Listing)}",r.Candidate.Listing.Price,RankingRowSummary(r,stale),
            !stale&&r.Evaluation.TryGetProperty("scorePercent",out var p)?p.GetDouble()<0?"pob-life":"pob-green":"pob-muted","trade-rank-select."+r.Index,r.Candidate.Listing.Id==rankingSelected,A(r.Evaluation,"warnings").Length)).ToArray();
        var revision=stale?"stale":rankingWeights+rankingBaseline;
        if(rankingQuery.Context!=revision||!rankingQuery.Entries.SequenceEqual(entries))
        {var same=rankingQuery.Entries.Select(e=>e.Key).SequenceEqual(entries.Select(e=>e.Key));rankingQuery=new(revision,entries);if(same)rankingCollection.UpdateContent(rankingQuery);else rankingCollection.PublishQuery(rankingQuery,entries.Length);}
        var controls=new List<WidgetElement>{Text("Ranked listings","rank.heading","pob-heading")};
        if(rankingEditing)
        {
            controls.Add(UI.Stack("pob.rank.setup",
            RankingTargetControl(),
            UI.Grid("pob.rank.options",[GridTrack.Auto()],[GridTrack.Star(2),GridTrack.Star()],
                UI.Select("Priority",TradePriorities.Select(p=>new SelectOption(p.Key,p.Label,"trade-rank-priority."+p.Key,p.Key==tradePriority)).ToArray(),"pob.rank.priority").FocusLeft("pob.website.browser").Classes("pob-select"),
                UI.Select("Limit",new[]{20,50,100}.Select(n=>new SelectOption("limit"+n,n.ToString(),"trade-rank-limit."+n,n==tradeBatchLimit)).ToArray(),"pob.rank.limit").Classes("pob-select").InGrid(column:1)).Spacing(0,8),
            UI.Select("Order",RankingSorts.Select(s=>new SelectOption(s.Key,s.Label,"trade-rank-sort."+s.Key,s.Key==rankingSort)).ToArray(),"pob.rank.order").Disabled(rankedItems.Length==0||stale).Classes("pob-select")).Classes("pob-buildtools"));
            controls.Add(Text(RankingRateLabel(),"rank.rates","pob-small pob-muted"));
        }
        else controls.Add(Text($"{rankingSlot} · {TradePriorities.First(p=>p.Key==tradePriority).Label} · up to {tradeBatchLimit}\n{RankingSorts.First(s=>s.Key==rankingSort).Label}","rank.context","pob-small pob-muted"));
        var actions=new List<WidgetElement>{
            Button(rankingEditing?"Done":"Edit ranking…","trade-rank-edit","rank.edit").FocusLeft("pob.website.browser"),
            Button("Evaluate again","trade-rank-evaluate","rank.evaluate").Disabled(!RankingTargetAvailable)};
        if(rankingSkipped.Length>0)actions.Add(Button((rankingSkippedExpanded?"Hide ":"")+$"{rankingSkipped.Length} skipped","trade-rank-skipped","rank.skipped-toggle"));
        controls.Add(UI.Row("pob.rank.actions",actions.ToArray()).Classes("pob-toolbar"));
        controls.Add(Text(stale?"Scores need recalculation for this build, slot or priority.":rankingSummary,"rank.summary","pob-small pob-muted"));
        if(rankingSkipped.Length>0)
        {
            if(rankingSkippedExpanded)controls.Add(UI.VerticalScroll("pob.rank.skipped",rankingSkipped.Select((reason,i)=>Text(Limit(reason,600),"rank.skipped."+i,"pob-small pob-muted")).ToArray())
                .Shortcut(ControllerButton.LeftStick,"trade-rank-focus","Ranking controls").Classes("pob-scroll","pob-rank-skipped"));
        }
        var tools=UI.Stack("pob.rank.tools",controls.ToArray()).RememberChildFocus("pob.rank.edit").Classes("pob-buildtools","pob-rank-tools");
        var list=UI.Stack("pob.rank.list",UI.CollectionList("pob.rank.results",rankingCollection,76,"Evaluated trade listings").Classes("pob-scroll"))
            .RememberChildFocus("pob.rank.results").Shortcut(ControllerButton.LeftStick,"trade-rank-focus","Ranking controls").Classes("pob-workspace");
        return UI.Stack("pob.rank.panel",tools,list).Classes("pob-panel","pob-tree-main");
    }
}
