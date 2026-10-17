using System.Globalization;
using System.Text.Json;
using WidgetRail.WidgetSdk;

namespace WidgetRail.Samples.PobCompanion;

public sealed partial class PobWidget
{
    private JsonElement tradeCatalog;
    private string tradeBase="";
    private bool TradeCatalogReady=>tradeCatalog.ValueKind==JsonValueKind.Object;
    private bool TradeSlotAvailable=>A(tradeCatalog,"slots").Any(s=>S(s,"name")==tradeSlot);
    private void ResetTradeCatalog(){tradeCatalog=default;tradeBase="";}
    private bool tradeAdvanced;
    private int tradeBatchLimit=20;
    private readonly Dictionary<int,double> tradeWeights=[];
    private readonly Dictionary<string,bool> tradeFlags=[];
    private readonly Dictionary<string,int> tradeNumbers=[];
    private object[] TradeWeights()=>tradeWeights.Select(w=>(object)new{id=w.Key,weight=w.Value}).ToArray();
    private bool TradeOptionApplies(string key)=>key switch
    {
        "includeRunes" or "sockets" or "links"=>tradeSlot.StartsWith("Weapon",StringComparison.Ordinal)||tradeSlot is "Helmet" or "Body Armour" or "Gloves" or "Boots",
        "includeTalisman"=>tradeSlot=="Amulet",
        "includeEldritch"=>tradeSlot is "Helmet" or "Body Armour" or "Gloves" or "Boots",
        "includeScourge"=>!tradeSlot.StartsWith("Jewel",StringComparison.Ordinal)&&!tradeSlot.StartsWith("Flask",StringComparison.Ordinal),
        _=>true
    };
    private Dictionary<string,object> TradeAdvanced()=>tradeFlags.Where(p=>TradeOptionApplies(p.Key)).ToDictionary(p=>p.Key,p=>(object)p.Value)
        .Concat(tradeNumbers.Where(p=>TradeOptionApplies(p.Key)).Select(p=>new KeyValuePair<string,object>(p.Key,p.Value))).ToDictionary(p=>p.Key,p=>p.Value);
    private void SaveTradePreferences()=>BuildDocument.SaveAtomic(Path.Combine(dataDirectory,"trade-settings.json"),
        JsonSerializer.Serialize(new{league=tradeLeague,budget=tradeBudget,currency=tradeCurrency,priority=tradePriority,mode=tradeMode,
            weights=tradeWeights,flags=tradeFlags,numbers=tradeNumbers,batchLimit=tradeBatchLimit}));
    private async Task ReadTradeCatalogAsync(CancellationToken token)
    {
        var before=pendingBefore??result!;
        var slot=tradeSlot;
        var catalog=(await engine.ExecuteAsync(new{op="trade-catalog",slot},token,before.Xml)).State;
        token.ThrowIfCancellationRequested();
        lock(gate)
        {
            if(tradeSlot!=slot)return;
            tradeCatalog=catalog;
            var bases=A(catalog,"bases");
            if(!bases.Any(b=>S(b,"base")==tradeBase))tradeBase=bases.Length==1?S(bases[0],"base"):"";
        }
    }

    private bool HandleTradeOptions(WidgetActionEvent action)
    {
        var id=action.ActionId;var parts=id.Split('.');
        if(parts is ["trade-stat",var stat]&&int.TryParse(stat,out var statId)&&featurePicker?.Prefix=="trade-stat.")
        {
            if(A(tradeCatalog,"stats").Any(s=>I(s,"id")==statId)&&tradeWeights.Count<20){tradeWeights[statId]=1;tradePriority="custom";}
            featurePicker=null;detailKind="trade";SaveTradePreferences();return true;
        }
        if(detailKind!="trade")return false;
        if(id=="trade-advanced"){tradeAdvanced=!tradeAdvanced;return true;}
        if(id=="trade-add-weight")
        {OpenFeaturePicker("Choose a ranking statistic","trade-stat.",A(tradeCatalog,"stats").Where(s=>!tradeWeights.ContainsKey(I(s,"id"))).ToArray());return true;}
        if(parts is ["trade-slot",var slot]&&int.TryParse(slot,out var index)&&index>=0&&index<A(tradeCatalog,"slots").Length)
        {tradeSlot=S(A(tradeCatalog,"slots")[index],"name");ResetTradeCatalog();Work(ReadTradeCatalogAsync,"Reading equipment categories…");return true;}
        if(parts is ["trade-base",var basis]&&int.TryParse(basis,out var bi)&&bi>=0&&bi<A(tradeCatalog,"bases").Length)
        {tradeBase=S(A(tradeCatalog,"bases")[bi],"base");return true;}
        if(parts is ["trade-batch",var count]&&int.TryParse(count,out var limit)&&limit is 20 or 50 or 100)tradeBatchLimit=limit;
        else if(parts is ["trade-weight",var weight]&&int.TryParse(weight,out var wi)&&tradeWeights.ContainsKey(wi))
        {
            if(!double.TryParse(action.CommittedText,NumberStyles.AllowDecimalPoint,CultureInfo.InvariantCulture,out var number)||!double.IsFinite(number)||number<0||number>100)
            {ShowFeedback("Invalid weight","Enter a number from 0 to 100. Zero ignores the statistic.",ToastTone.Warning);return true;}
            tradeWeights[wi]=number;
        }
        else if(parts is ["trade-remove-weight",var remove]&&int.TryParse(remove,out var ri))tradeWeights.Remove(ri);
        else if(parts is ["trade-flag",var flag]&&flag is "includeMirrored" or "includeCorrupted" or "includeRunes" or "includeTalisman" or "includeScourge" or "includeEldritch")
            tradeFlags[flag]=!tradeFlags.GetValueOrDefault(flag);
        else if(parts is ["trade-number",var key]&&key is "maxLevel" or "sockets" or "links")
        {
            if(string.IsNullOrWhiteSpace(action.CommittedText))tradeNumbers.Remove(key);
            else if(int.TryParse(action.CommittedText,out var number)&&number>=0&&number<=(key=="maxLevel"?100:6))tradeNumbers[key]=number;
            else{ShowFeedback("Invalid limit",key=="maxLevel"?"Enter a level from 0 to 100, or leave blank.":"Enter a count from 0 to 6, or leave blank.",ToastTone.Warning);return true;}
        }
        else return false;
        Work(_=>{SaveTradePreferences();return Task.CompletedTask;},"Saving search preferences…");return true;
    }
    private WidgetElement TradeSlotControl()
    {
        var options=A(tradeCatalog,"slots").Select((s,i)=>new SelectOption("slot"+i,S(s,"title"),"trade-slot."+i,S(s,"name")==tradeSlot)).ToList();
        // Keep the requested target explicit, including inactive weapon swaps.
        // Never substitute the first active slot or render a select with no selection.
        if(!TradeSlotAvailable)options.Insert(0,new SelectOption("requested",ComparisonSlotLabel(tradeSlot),"noop",true));
        return UI.Select("Compare against",options,"pob.trade-slot").Disabled(!TradeCatalogReady).Classes("pob-select");
    }
    private WidgetElement TradeOptionControls()
    {
        var controls=new List<WidgetElement>();
        var bases=A(tradeCatalog,"bases");
        if(bases.Length>1)controls.Add(UI.Select("Empty-slot item category",bases.Select((b,i)=>new SelectOption("base"+i,S(b,"title"),"trade-base."+i,S(b,"base")==tradeBase)).Prepend(new SelectOption("choose","Choose category","noop",tradeBase.Length==0)).ToArray(),"pob.trade-base").Classes("pob-select"));
        controls.Add(UI.Select("Evaluation limit",new[]{20,50,100}.Select(n=>new SelectOption("limit"+n,n+" listings","trade-batch."+n,n==tradeBatchLimit)).ToArray(),"pob.trade-batch").Classes("pob-select"));
        controls.Add(Text("On the website, choose Load and evaluate results. More listings load automatically up to your evaluation limit. Ranking covers the evaluated items, not the whole market.","trade.batch-help","pob-small pob-muted"));
        if(tradePriority=="custom")
        {
            foreach(var (id,value) in tradeWeights)
                controls.Add(UI.Row("pob.trade.weight.row."+id,Field(S(A(tradeCatalog,"stats").FirstOrDefault(s=>I(s,"id")==id),"title","Unavailable statistic"),UI.TextEntry(value.ToString(CultureInfo.InvariantCulture),"0–100","trade-weight."+id,"pob.trade-weight."+id,6).Classes("pob-input"),"trade-weight."+id),Button("Remove","trade-remove-weight."+id)).Classes("pob-toolbar"));
            controls.Add(Button("Add statistic","trade-add-weight").Disabled(tradeWeights.Count>=20));
            controls.Add(Text("Weights express relative importance. PoB handles statistics where lower is better. Set at least one nonzero weight.","trade.weights-help","pob-small pob-muted"));
        }
        controls.Add(Button(tradeAdvanced?"Hide advanced search":"Advanced search…","trade-advanced"));
        if(tradeAdvanced)
        {
            foreach(var (key,title) in new[]{("includeMirrored","Include mirrored items"),("includeCorrupted","Include corruption modifier weights")}
                .Concat(game==2?new[]{("includeRunes","Include rune modifier weights")}:new[]{("includeTalisman","Include talisman modifier weights"),("includeScourge","Include Scourge modifier weights"),("includeEldritch","Include regular Eldritch modifier weights")}))
                if(TradeOptionApplies(key))controls.Add(UI.Switch(title,tradeFlags.GetValueOrDefault(key),"trade-flag."+key,"pob.trade-flag."+key).Classes("pob-toggle"));
            foreach(var (key,title,hint) in new[]{("maxLevel","Maximum required level","Character level"),("sockets","Minimum empty sockets","No minimum")}.Concat(game==1?new[]{("links","Minimum links","No minimum")}:[]))
                if(TradeOptionApplies(key))controls.Add(Field(title,UI.TextEntry(tradeNumbers.TryGetValue(key,out var n)?n.ToString():"",hint,"trade-number."+key,"pob.trade-number."+key,3).Classes("pob-input"),"trade-number."+key));
            controls.Add(Text("Modifier options affect PoB's search weights. Evaluations always use the item as listed, including its existing augments and implicits.","trade.advanced-help","pob-small pob-muted"));
        }
        return UI.Stack("pob.trade.options",controls.ToArray()).Classes("pob-buildtools");
    }
    private string ComparisonSlotLabel(string slot)
    {
        var item=A((pendingBefore??result)!.State,"items").FirstOrDefault(i=>S(i,"slot")==slot);
        return slot+" — "+(item.ValueKind==JsonValueKind.Object?S(item,"name","Empty"):"Empty");
    }
}
