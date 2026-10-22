using System.Text.Json;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

namespace WidgetRail.Samples.PobCompanion;

public sealed partial class PobWidget
{
    private sealed record UnifiedSearchCache(PobResult Build,string? Document,CalculationSelection Calculation,string Saved,
        JsonElement Inventory,JsonElement Calculations,PobUnifiedSearchCatalog Index,string[] Limitations);
    private sealed record UnifiedQuery(UnifiedSearchCache? Cache,string Text,string Category,PobSearchResult[] Results,long Epoch=0);
    private UnifiedSearchCache? unifiedCache;
    private UnifiedQuery unifiedQuery=new(null,"","All",[]);
    private WidgetIndexedCollection<UnifiedQuery,PobSearchResult>? unifiedCollection;
    private bool unifiedSearchOpen;
    private long unifiedSearchEpoch;
    private string unifiedSearchText="",unifiedCategory="All",unifiedNotice="";
    private string unifiedSuggestionSource="pob.unified.initial";
    private PobSearchSuggestions? unifiedSuggestions;
    private string? unifiedTargetId;
    private string? unifiedTargetSection,unifiedTargetDetail;
    private string? unifiedIndexedKind,unifiedIndexedKey;
    private long unifiedFocusRequest;
    private int? unifiedMapNode;
    private int? unifiedReadOnlyConfig;
    private string? unifiedSearchDocument,unifiedSelectedResult;
    private long unifiedSearchReturnRequest;


    private string UnifiedSavedSignature()=>string.Join("\n",savedBuilds.Select(b=>b.Id+":"+b.Name));
    private bool UnifiedCacheCurrent(UnifiedSearchCache? cache)=>cache is not null&&ReferenceEquals(cache.Build,result)&&cache.Document==documentId&&
        cache.Calculation==calculationSelection&&cache.Saved==UnifiedSavedSignature();
    private bool UnifiedQueryCurrent(UnifiedQuery query)=>unifiedSearchOpen&&query.Epoch==unifiedSearchEpoch&&ReferenceEquals(query,unifiedQuery)&&UnifiedCacheCurrent(query.Cache)&&
        query.Text==unifiedSearchText&&query.Category==unifiedCategory&&LifecycleState==WidgetLifecycleState.Interactive;

    private void OpenUnifiedSearch(string? category=null)
    {
        if(result is null||LifecycleState!=WidgetLifecycleState.Interactive)return;
        if(featureReading is not null||featureRemoval is not null||timelessDetail is not null||
            pendingImport is not null||deleteBuildId is not null||passiveChoicePopup is not null||skillRemoval is not null||websiteVisible)return;
        if(pendingBefore is not null){ShowFeedback("Finish the comparison first","Apply or discard the current comparison before navigating to another part of the build.",ToastTone.Info);return;}
        var before=result;var document=documentId;var selection=calculationSelection;var epoch=++unifiedSearchEpoch;
        void Open(UnifiedSearchCache cache)
        {
            if(unifiedSearchDocument!=document)
            {unifiedSearchText="";unifiedCategory="All";unifiedSelectedResult=null;unifiedSearchDocument=document;}
            // The Warnings entry point is an explicit filter command; ordinary
            // Search reopens the user's last query instead.
            if(category is not null){unifiedCategory=category;unifiedSearchText="";unifiedSelectedResult=null;}
            if(unifiedCategory!="All"&&!cache.Index.Categories.Contains(unifiedCategory)){unifiedCategory="All";unifiedSelectedResult=null;}
            unifiedCache=cache;unifiedSearchOpen=true;unifiedNotice="";
            unifiedSearchReturnRequest=unifiedSelectedResult is null?0:(lastFocusRequestId = CreateFocusRequest().Id);
            unifiedSuggestionSource="pob.unified."+Guid.NewGuid().ToString("N");
            unifiedSuggestions=new(cache.Index.Entries.Select(e=>(e.Title,e.SearchText,e.Category+" · "+e.Context)),160);
            Invalidate();
        }
        if(UnifiedCacheCurrent(unifiedCache)&&unifiedCache!.Limitations.Length==0){Open(unifiedCache);return;}
        Work(async token=>
        {
            JsonElement inventory=default,calculations=default;var limitations=new List<string>();
            try { inventory=(await engine.ExecuteAsync(new{op="inventory-list"},token,before.Xml)).State;_ = PobSearchInventory.Read(inventory); }
            catch(IOException){inventory=default;limitations.Add("Inventory search is unavailable. Reopen Search to retry.");}
            try { calculations=(await engine.ExecuteAsync(new{op="calculation-catalog",context=selection.Request()},token,before.Xml)).State;_ = PobSearchCalculations.Read(calculations); }
            catch(IOException){calculations=default;limitations.Add("Detailed calculation search is unavailable. Reopen Search to retry.");}
            token.ThrowIfCancellationRequested();
            lock(gate)
            {
                if(epoch!=unifiedSearchEpoch||!ReferenceEquals(result,before)||documentId!=document||LifecycleState!=WidgetLifecycleState.Interactive)return;
                Open(new(before,document,selection,UnifiedSavedSignature(),inventory,calculations,
                    new(before.State,before.Configuration,inventory,calculations,savedBuilds.Select(b=>(b.Id,b.Name))),limitations.ToArray()));
            }
        },"Preparing build search…");
    }
    private bool HandleUnifiedSearchAction(WidgetActionEvent action)
    {
        var id=action.ActionId;
        if(id=="unified-search"){OpenUnifiedSearch();return true;}
        if(id=="unified-warnings"){OpenUnifiedSearch("Warnings");return true;}
        if(!unifiedSearchOpen)return id.StartsWith("unified-search-",StringComparison.Ordinal);
        if(id=="unified-search-close"){unifiedSearchOpen=false;unifiedSearchEpoch++;return true;}
        if(!UnifiedCacheCurrent(unifiedCache)){unifiedSearchOpen=false;MissingSearchTarget();return true;}
        if(id=="unified-search-text")
        {
            var text=action.CommittedText??"";
            if(text!=unifiedSearchText){unifiedSearchText=text;unifiedSelectedResult=null;}
            unifiedNotice="";return true;
        }
        if(id.StartsWith("unified-search-category.",StringComparison.Ordinal))
        {
            var categories=new[]{"All"}.Concat(unifiedCache!.Index.Categories).ToArray();
            if(int.TryParse(id["unified-search-category.".Length..],out var index)&&index>=0&&index<categories.Length)
            {if(unifiedCategory!=categories[index]){unifiedCategory=categories[index];unifiedSelectedResult=null;}unifiedNotice="";}
        }
        return true;
    }
    private WidgetView WithUnifiedSearch(WidgetView view)
    {
        if(!unifiedSearchOpen)return view;
        if(!UnifiedCacheCurrent(unifiedCache)){unifiedSearchOpen=false;return view;}
        unifiedCollection??=CreateIndexedCollection<UnifiedQuery,PobSearchResult>("pob.unified.source",unifiedQuery,0,new()
        {
            ReadRange=(query,start,count,token)=>{token.ThrowIfCancellationRequested();return ValueTask.FromResult<IReadOnlyList<PobSearchResult>>(query.Results.Skip(start).Take(count).ToArray());},
            ItemKey=entry=>new(entry.Key),
            RenderItem=(_,entry,context)=>UI.ActionSurface("open",context.Id("result"),Limit(entry.Title+", "+entry.Category+", "+entry.Context,1800),ActionSurfaceOrientation.Vertical,
                UI.Text(Limit(entry.Title,300),context.Id("title")).Classes("pob-list-title"),
                UI.Text(entry.Category+" · "+Limit(entry.Context,220),context.Id("context")).Classes("pob-small","pob-muted")).Classes("pob-list-item"),
            OnAction=(query,entry,action,token)=>
            {
                lock(gate)
                {
                    if(busy||action.ActionId!="open")return ValueTask.CompletedTask;
                    if(!UnifiedQueryCurrent(query)){MissingSearchTarget();Invalidate();return ValueTask.CompletedTask;}
                    unifiedSelectedResult=entry.Key;
                    NavigateSearchResult(entry,query.Cache!);Invalidate();return ValueTask.CompletedTask;
                }
            },
        });
        if(unifiedQuery.Epoch!=unifiedSearchEpoch||!ReferenceEquals(unifiedQuery.Cache,unifiedCache)||unifiedQuery.Text!=unifiedSearchText||unifiedQuery.Category!=unifiedCategory)
        {
            var computed=unifiedCache!.Index.Match(unifiedSearchText,unifiedCategory);
            // Refresh callback authority on every open without discarding the
            // host's logical list position when membership/order are unchanged.
            var same=unifiedQuery.Cache?.Document==unifiedCache.Document&&unifiedQuery.Text==unifiedSearchText&&unifiedQuery.Category==unifiedCategory&&
                unifiedQuery.Results.Select(e=>e.Key).SequenceEqual(computed.Select(e=>e.Key));
            unifiedQuery=new(unifiedCache,unifiedSearchText,unifiedCategory,computed,unifiedSearchEpoch);
            if(same)unifiedCollection.UpdateContent(unifiedQuery);else unifiedCollection.PublishQuery(unifiedQuery,computed.Length);
            if(unifiedSelectedResult is {} selected&&!computed.Any(e=>e.Key==selected))unifiedSelectedResult=null;
        }
        var matches=unifiedQuery.Results;
        var rows=new List<WidgetElement>{UI.Grid("pob.unified.tools",[GridTrack.Auto()],[GridTrack.Star(),GridTrack.Auto()],
            UI.TextEntry(unifiedSearchText,"Name, modifier or page","unified-search-text","pob.unified.search",160).Suggest(unifiedSuggestionSource).Classes("pob-input"),
            UI.Select("Search in",new[]{new SelectOption("all","All","unified-search-category.0",unifiedCategory=="All")}.Concat(unifiedCache!.Index.Categories.Select((name,i)=>
                new SelectOption("category"+i,name,"unified-search-category."+(i+1),name==unifiedCategory))).ToArray(),"pob.unified.category").Classes("pob-select").InGrid(column:1)).Spacing(0,12),
            Text($"{matches.Length} results","unified.count","pob-small pob-muted")};
        foreach(var (limitation,index) in unifiedCache!.Limitations.Select((value,i)=>(value,i)))rows.Add(Text(limitation,"unified.limitation."+index,"pob-small pob-gold"));
        if(busy)rows.Add(UI.Row("pob.unified.busy",Text(status,"unified.busy-text","pob-small pob-muted"),Button("Cancel","cancel-operation","unified.cancel")).Classes("pob-toolbar"));
        if(unifiedNotice.Length>0)rows.Add(UI.VerticalScroll("pob.unified.notice",Text(unifiedNotice,"unified.notice-text")).Classes("pob-scroll","pob-unified-notice"));
        rows.Add(matches.Length==0?Text("No matching content.","unified.empty","pob-muted"):
            UI.CollectionList("pob.unified.results",unifiedCollection,88,"Build search results").Classes("pob-scroll"));
        var modal=view.WithModal(new("pob.unified","Search build",UI.Stack("pob.unified.content",rows.ToArray()).Classes("pob-workspace"),
            "pob.unified.search",busy?"cancel-operation":"unified-search-close"){ScrollContent=false,AnchoredPresentation=new(new("pob.unified-search"),Width:900,Height:700)});
        if(!busy&&unifiedSearchReturnRequest>0&&unifiedSelectedResult is {} selectedKey&&Array.FindIndex(matches,e=>e.Key==selectedKey) is var selectedIndex&&selectedIndex>=0)
            modal=modal with{FocusGroupEntryRequest=unifiedCollection.Enter("pob.unified.results",unifiedSearchReturnRequest,
                unifiedCollection.FocusTarget("pob.unified.results",new(selectedKey),selectedIndex))};
        return modal;
    }
    private void MissingSearchTarget()=>ShowFeedback("Search result no longer available","The build or source changed. Open Search again to choose a current result.",ToastTone.Warning);

    private void NavigateSearchResult(PobSearchResult entry,UnifiedSearchCache cache)
    {
        if(!UnifiedCacheCurrent(cache)||pendingBefore is not null){MissingSearchTarget();return;}
        var target=entry.Target;
        if(target.Kind==PobSearchTargetKind.Warning){unifiedNotice=entry.Title;return;}
        unifiedTargetId=null;unifiedIndexedKind=null;unifiedIndexedKey=null;unifiedReadOnlyConfig=null;
        switch(target.Kind)
        {
            case PobSearchTargetKind.Passive:
                if(!A(State,"nodes").Any(n=>I(n,"id")==target.Id&&B(n,"interactive"))){MissingSearchTarget();return;}
                SetSection("Tree");detailKind="node";detailId=target.Id;selections["node"]=target.Id;nodeReturnToMap=false;passiveReturnKey="node."+target.Id;
                SetUnifiedFocus("pob.back");unifiedSearchOpen=false;unifiedMapNode=mapSession is null?target.Id:null;SendNodeSelection(target.Id);
                if(mapSession is null)Work(OpenMapAsync,"Opening selected passive…");
                return;
            case PobSearchTargetKind.Skill:
            case PobSearchTargetKind.Gem:
                var group=A(State,"skills").FirstOrDefault(g=>I(g,"id")==target.Id);
                if(group.ValueKind!=JsonValueKind.Object||target.Kind==PobSearchTargetKind.Gem&&!A(group,"gems").Any(g=>I(g,"id")==target.Child)){MissingSearchTarget();return;}
                SetSection("Skills");detailKind="skill";detailId=target.Id;selections["skill"]=target.Id;
                var gem=A(group,"gems").FirstOrDefault(g=>I(g,"id")==target.Child);
                SetUnifiedFocus(target.Kind==PobSearchTargetKind.Skill?"pob.skill."+target.Id:"pob.gem."+target.Child+(B(gem,"levelReadOnly")?"quality":"level"));break;
            case PobSearchTargetKind.EquippedItem:
                if(!A(State,"items").Any(i=>I(i,"id")==target.Id&&I(i,"itemId").ToString()==target.Key)){MissingSearchTarget();return;}
                SetSection("Items");detailKind="item";detailId=target.Id;selections["item"]=target.Id;SetUnifiedFocus("pob.item."+target.Id);break;
            case PobSearchTargetKind.Configuration:
                var option=ConfigurationCatalog.Options.FirstOrDefault(c=>c.Id==target.Key&&c.Index==target.Id);
                if(option is null){MissingSearchTarget();return;}
                SetSection("Configuration");detailKind="";configSection=option.Section;filter="";configChangedOnly=false;
                RevealConfigurationSummary(option);
                var readOnly=option.Id=="customMods"||S(option.Raw,"kind")=="customMods"||S(option.Raw,"value").Length>2048;
                if(readOnly)unifiedReadOnlyConfig=option.Index;
                SetUnifiedFocus(readOnly?"pob.config.readonly-scroll":"pob.condition."+option.Index);break;
            case PobSearchTargetKind.SavedBuild:
                if(!savedBuilds.Any(b=>b.Id==target.Key)){MissingSearchTarget();return;}
                detailKind="library";librarySelection=target.Key;librarySearch="";libraryClass="";libraryOpenFocusRequested=false;
                SetUnifiedIndexedFocus("library",target.Key);break;
            case PobSearchTargetKind.InventoryItem:
                if(!A(cache.Inventory,"inventory").Any(i=>I(i,"id")==target.Id)){MissingSearchTarget();return;}
                NavigateSearchInventory(target.Id,cache);return;
            case PobSearchTargetKind.Calculation:
                NavigateSearchCalculation(target,cache);return;
            case PobSearchTargetKind.Navigation:
                if(target.Key=="inventory"){NavigateSearchInventory(0,cache);return;}
                if(target.Key=="library"){detailKind="library";SetUnifiedFocus("pob.library.search");break;}
                if(target.Key=="sets"){detailKind="sets";SetUnifiedFocus("pob.back");break;}
                SetSection(target.Key switch{"calcs"=>"Calcs","skills"=>"Skills","items"=>"Items","configuration"=>"Configuration","tree"=>"Tree","notes"=>"Notes",_=>section});
                detailKind="";SetUnifiedFocus(target.Key switch{"skills"=>"pob.group-create","items"=>"pob.inventory","configuration"=>"pob.search","calcs"=>"pob.calculations",_=>SectionEntry()});break;
        }
        unifiedSearchOpen=false;
    }
    private void NavigateSearchInventory(int itemId,UnifiedSearchCache cache)
    {
        var epoch=unifiedSearchEpoch;
        Work(async token=>
        {
            var inventory=await ReadInventoryAsync(cache.Build.Xml,itemId,token);
            lock(gate)
            {
                if(epoch!=unifiedSearchEpoch||!UnifiedCacheCurrent(cache)||LifecycleState!=WidgetLifecycleState.Interactive)return;
                if(itemId>0&&inventory.Id!=itemId){MissingSearchTarget();return;}
                PublishInventory(inventory);SetSection("Items");detailKind="inventory";inventorySearch="";unifiedSearchOpen=false;
                if(itemId>0)SetUnifiedIndexedFocus("inventory",itemId.ToString());else SetUnifiedFocus("pob.inventory-search");
            }
        },"Opening inventory item…");
    }
    private void NavigateSearchCalculation(PobSearchTarget target,UnifiedSearchCache cache)
    {
        var chosen=A(cache.Calculations,"sections").FirstOrDefault(s=>S(s,"id")==target.Section);
        if(!A(chosen,"items").Any(i=>S(i,"id")==target.Key)){MissingSearchTarget();return;}
        var epoch=unifiedSearchEpoch;
        Work(async token=>
        {
            var detail=(await engine.ExecuteAsync(new{op="calculation-detail",id=target.Key,context=cache.Calculation.Request()},token,cache.Build.Xml)).State;
            lock(gate)
            {
                if(epoch!=unifiedSearchEpoch||!UnifiedCacheCurrent(cache)||LifecycleState!=WidgetLifecycleState.Interactive)return;
                PublishCalculations(new(cache.Calculations,detail,target.Section,target.Key,cache.Calculation));SetSection("Calcs");detailKind="calculations";calculationSearch="";
                unifiedSearchOpen=false;SetUnifiedIndexedFocus("calculation",target.Key);
            }
        },"Opening calculation sources…");
    }
    private void SetUnifiedFocus(string target)
    {unifiedTargetId=target;unifiedIndexedKind=null;unifiedTargetSection=section;unifiedTargetDetail=detailKind;unifiedFocusRequest=(lastFocusRequestId = CreateFocusRequest().Id);quickControlTarget=null;historyFocusRequested=false;}
    private void SetUnifiedIndexedFocus(string kind,string key)
    {unifiedTargetId=null;unifiedIndexedKind=kind;unifiedIndexedKey=key;unifiedTargetSection=section;unifiedTargetDetail=detailKind;unifiedFocusRequest=(lastFocusRequestId = CreateFocusRequest().Id);quickControlTarget=null;historyFocusRequested=false;}

    private int UnifiedWarningCount()=>A(State,"warnings").Length+A(State,"items").Sum(item=>A(item,"warnings").Length)+(PobMainSkillWarning.Read(State)is null?0:1);
    private WidgetElement UnifiedWarningSummary()=>UI.Stack("pob.warnings",Button("Warnings ("+UnifiedWarningCount()+")","unified-warnings"));
}
