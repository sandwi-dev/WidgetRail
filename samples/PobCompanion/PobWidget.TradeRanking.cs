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
    private sealed record RankingSave(string Baseline,string Slot,string Weights,RankedItem[] Rows,string[] Skipped);
    private readonly List<RankingInput> rankingInputs=[];
    private RankedItem[] rankedItems=[];
    private string[] rankingSkipped=[];
    private string rankingSlot="",rankingBaseline="",rankingWeights="",rankingBuild="",rankingSummary="";
    private bool rankingVisible;
    private JsonElement[] rankingSlots=[];
    private string rankingSelected="";
    private sealed record RankEntry(string Key,string Label,string Summary,string Tone,string Action,bool Selected);
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

    private void ReceiveTradeBatch(JsonElement context,JsonElement[] entries)
    {
        ReadTradePreferences();rankingInputs.Clear();var skipped=new List<string>();var ids=new HashSet<string>();
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
        rankingSlot=tradeSlot.Length>0?tradeSlot:previewSlot??"Weapon 1";
        rankingSkipped=skipped.ToArray();rankedItems=[];rankingBuild=documentId??"";rankingVisible=true;
        Work(EvaluateTradeBatchAsync,"Preparing listing evaluation…");
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
            for(var i=0;i<inputs.Length;i++)
            {
                token.ThrowIfCancellationRequested();var input=inputs[i];
                lock(gate)status=$"Evaluating {i+1} of {inputs.Length} listings…";Invalidate();
                var request=input.Raw is null?(object)new{op="trade-rank-item",entry=input.Entry}:new{op="trade-rank-item",raw=input.Raw};
                var response=(await engine.ExecuteAsync(request,token,before.Xml)).State;
                var error=RankingError(S(response,"error"));
                if(error.Length>0){skipped.Add(TradeItemName(input.Listing)+": "+error);continue;}
                var evaluation=JsonSerializer.SerializeToElement(response.EnumerateObject().Where(p=>p.Name!="raw").ToDictionary(p=>p.Name,p=>p.Value));
                evaluated.Add(new(I(response,"index"),new(S(response,"raw"),input.Listing,slot),evaluation));
            }
            var sorted=await engine.ExecuteAsync(new{op="trade-rank-finish"},token,before.Xml);
            var rows=A(sorted.State,"order").Select(index=>evaluated.Single(r=>r.Index==index.GetInt32())).ToArray();
            token.ThrowIfCancellationRequested();
            var saved=new RankingSave(baseline,slot,weights,rows,skipped.ToArray());
            var savedJson=JsonSerializer.Serialize(saved);
            if(Encoding.UTF8.GetByteCount(savedJson)>5*1024*1024)throw new IOException("The evaluation exceeds the saved-results size limit. Evaluate a smaller batch.");
            BuildDocument.SaveAtomic(Path.Combine(dataDirectory,documentId+".ranking.json"),savedJson);
            lock(gate)
            {
                rankedItems=rows;rankingSkipped=skipped.ToArray();rankingBaseline=baseline;rankingWeights=weights;rankingBuild=documentId??"";
                rankingSummary=$"{rows.Length} ranked · {skipped.Count} skipped · {inputs.Length} of {rankingInputs.Count} loaded evaluated · {timer.Elapsed.TotalSeconds:F1}s";
                rankingVisible=true;websiteFocus="pob.rank.tools";websiteFocusSequence=++focusRequestSequence;
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
        rankingInputs.Clear();rankedItems=[];rankingSkipped=[];rankingVisible=false;rankingBuild=id;rankingBaseline="";rankingSummary="";rankingSelected="";
        try
        {
            var file=new FileInfo(Path.Combine(dataDirectory,id+".ranking.json"));if(!file.Exists||file.Length>5*1024*1024)return;
            var saved=JsonSerializer.Deserialize<RankingSave>(File.ReadAllText(file.FullName));
            if(saved is null||saved.Rows is not{Length:<=100}||saved.Slot is not{Length:>0 and <=128}||saved.Weights is not{Length:<=16384}||saved.Baseline is not{Length:64})return;
            if(saved.Rows.Select(r=>r.Index).Distinct().Count()!=saved.Rows.Length||saved.Rows.Any(r=>r.Index is <1 or >100||r.Candidate.Raw is not{Length:>0 and <=32768}||r.Candidate.Slot!=saved.Slot||!r.Candidate.Listing.IsValid(game)||!ValidRankingEvaluation(r.Evaluation)))return;
            if(saved.Rows.Select(r=>r.Candidate.Listing.Id).Distinct().Count()!=saved.Rows.Length)return;
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
        if(!value.TryGetProperty("changes",out var changes)||changes.ValueKind!=JsonValueKind.Array||changes.GetArrayLength()>20)return false;
        return changes.EnumerateArray().All(c=>c.ValueKind==JsonValueKind.Object&&c.TryGetProperty("stat",out var s)&&s.ValueKind==JsonValueKind.String&&s.GetString()is{Length:>0 and <=64}&&
            new[]{"before","after","delta"}.All(k=>c.TryGetProperty(k,out var v)&&Number(v)));
    }
    private bool HandleRankingAction(WidgetActionEvent action)
    {
        var id=action.ActionId;var parts=id.Split('.');
        if(id.StartsWith("trade-rank-",StringComparison.Ordinal)&&((pendingBefore is not null||pastedItem is not null)&&!CanReplaceTradeComparison||result is null))
        {ShowFeedback("Finish current work","Finish the build edit or item draft before evaluating trade items.",ToastTone.Warning);return true;}
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
        if(id=="trade-rank-evaluate"&&HasRanking){Work(EvaluateTradeBatchAsync,"Preparing listing evaluation…");return true;}
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
        var changes=A(row.Evaluation,"changes").Where(c=>S(c,"stat") is "CombinedDPS" or "Life" or "EnergyShield"&&Math.Abs(c.GetProperty("delta").GetDouble())>=.01).Select(c=>$"{(S(c,"stat")=="CombinedDPS"?"Player DPS":StatLabel(S(c,"stat")))} {c.GetProperty("delta").GetDouble():+0.##;-0.##;0}");
        return score+" · "+string.Join(" · ",changes);
    }
    private WidgetElement TradeRankingPanel()
    {
        rankingCollection??=CreateIndexedCollection<RankQuery,RankEntry>("pob.rank.source",rankingQuery,0,new()
        {
            ReadRange=(q,start,count,token)=>ValueTask.FromResult<IReadOnlyList<RankEntry>>(q.Entries.Skip(start).Take(count).ToArray()),ItemKey=e=>new(e.Key),
            RenderItem=(_,e,c)=>UI.ActionSurface("select",c.Id("row"),e.Label,ActionSurfaceOrientation.Vertical,
                UI.Text(e.Label,c.Id("title")).Classes("pob-list-title"),UI.Text(e.Summary.Split(" · ",2)[0],c.Id("score")).Classes("pob-small",e.Tone),
                UI.Text(e.Summary.Contains(" · ",StringComparison.Ordinal)?e.Summary.Split(" · ",2)[1]:"",c.Id("summary")).Classes("pob-small")).Selected(e.Selected).FocusLeft("pob.website.browser").Classes("pob-list-item"),
            OnAction=(q,e,a,token)=>{lock(gate){if(!ReferenceEquals(q,rankingQuery)||busy||a.ActionId!="select")return ValueTask.CompletedTask;return OnActionAsync(a with{ActionId=e.Action},token);}}
        });
        var stale=RankingStale;
        var entries=rankedItems.Select((r,i)=>new RankEntry(r.Candidate.Listing.Id,$"{i+1}. {TradeItemName(r.Candidate.Listing)} · {r.Candidate.Listing.Price}",RankingRowSummary(r,stale),
            !stale&&r.Evaluation.TryGetProperty("scorePercent",out var p)?p.GetDouble()<0?"pob-life":"pob-green":"pob-muted","trade-rank-select."+r.Index,r.Candidate.Listing.Id==rankingSelected)).ToArray();
        var revision=stale?"stale":rankingWeights+rankingBaseline;
        if(rankingQuery.Context!=revision||!rankingQuery.Entries.SequenceEqual(entries))
        {var same=rankingQuery.Entries.Select(e=>e.Key).SequenceEqual(entries.Select(e=>e.Key));rankingQuery=new(revision,entries);if(same)rankingCollection.UpdateContent(rankingQuery);else rankingCollection.PublishQuery(rankingQuery,entries.Length);}
        var tools=UI.Stack("pob.rank.tools",Text("Ranked listings","rank.heading","pob-heading"),
            rankingSlots.Length==0?Text(rankingSlot,"rank.slot","pob-small"):UI.Select("Compare against",rankingSlots.Select((s,i)=>new SelectOption("slot"+i,S(s,"title"),"trade-rank-slot."+i,S(s,"name")==rankingSlot)).ToArray(),"pob.rank.slot").FocusLeft("pob.website.browser").Classes("pob-select"),
            UI.Grid("pob.rank.options",[GridTrack.Auto()],[GridTrack.Star(2),GridTrack.Star()],
                UI.Select("Priority",TradePriorities.Select(p=>new SelectOption(p.Key,p.Label,"trade-rank-priority."+p.Key,p.Key==tradePriority)).ToArray(),"pob.rank.priority").FocusLeft("pob.website.browser").Classes("pob-select"),
                UI.Select("Limit",new[]{20,50,100}.Select(n=>new SelectOption("limit"+n,n.ToString(),"trade-rank-limit."+n,n==tradeBatchLimit)).ToArray(),"pob.rank.limit").Classes("pob-select").InGrid(column:1)).Spacing(0,8),
            UI.Row("pob.rank.actions",Button("Evaluate again","trade-rank-evaluate","rank.evaluate").FocusLeft("pob.website.browser"),Button("Hide results","trade-rank-hide","rank.hide")).Classes("pob-toolbar"),
            Text(rankingSummary,"rank.summary","pob-small pob-muted"),
            Text(stale?"Scores need recalculation for this build, slot or priority.":"Priority: "+TradePriorities.Single(p=>p.Key==tradePriority).Label+". Inspect gains and losses before equipping. Prices retain their listed currencies.","rank.help","pob-small pob-muted"))
            .RememberChildFocus(rankingSlots.Length==0?"pob.rank.evaluate":"pob.rank.slot").Classes("pob-buildtools");
        var list=UI.Stack("pob.rank.list",UI.CollectionList("pob.rank.results",rankingCollection,128,"Evaluated trade listings").Classes("pob-scroll"))
            .RememberChildFocus("pob.rank.results").Shortcut(ControllerButton.LeftStick,"trade-rank-focus","Ranking controls").Classes("pob-workspace");
        var children=new List<WidgetElement>{tools,list};
        if(rankingSkipped.Length>0)children.Add(UI.VerticalScroll("pob.rank.skipped",new WidgetElement[]{Text("Skipped listings","rank.skipped.title","pob-small")}
            .Concat(rankingSkipped.Select((reason,i)=>Text(Limit(reason,600),"rank.skipped."+i,"pob-small pob-muted"))).ToArray())
            .Shortcut(ControllerButton.LeftStick,"trade-rank-focus","Ranking controls").Classes("pob-scroll"));
        return UI.Stack("pob.rank.panel",children.ToArray()).Classes("pob-panel","pob-tree-main");
    }
}
