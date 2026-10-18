using System.Text.Json;
using WidgetRail.WidgetSdk;

namespace WidgetRail.Samples.PobCompanion;

public sealed partial class PobWidget
{
    private WidgetElement TreeAllocationControl()=>UI.Stack("pob.tree.allocation",
        UI.Select("Allocate passives to",new[]{"Shared tree","Weapon set 1","Weapon set 2"}
        .Select((name,i)=>new SelectOption("mode"+i,name,"tree-mode."+i,I(State,"allocationMode")==i)).ToArray(),"pob.tree.allocation-mode")
        .Disabled(pendingBefore is not null).Classes("pob-select"),
        Text(string.Join(" · ",A(State,"passiveBudgets").Select((budget,i)=>new[]{"Passives","Set 1","Set 2","Ascendancy"}[i]+": "+I(budget,"used")+" / "+I(budget,"maximum"))),"tree.budgets","pob-small pob-muted"));
    private void AddTreeEditingControls(List<WidgetElement> rows,JsonElement node)
    {
        if(Capabilities.WeaponTreeModes)rows.Add(Text(B(node,"allocated")?"Allocated to: "+(I(node,"allocationMode")==0?"Shared tree":"Weapon set "+I(node,"allocationMode")):
            "Allocation target: "+(I(State,"allocationMode")==0?"Shared tree":"Weapon set "+I(State,"allocationMode")),"node.allocation-mode","pob-small pob-muted"));
        if(B(node,"canTattoo"))rows.Add(Button(B(node,"isTattoo")?"Change tattoo…":"Add tattoo…","tree-tattoo"));
        if(B(node,"isTattoo"))rows.Add(Button("Preview tattoo removal","tree-tattoo-remove"));
    }
    private bool HandleTreeEditing(WidgetActionEvent action)
    {
        var id=action.ActionId;
        if(HandleTimeless(action))return true;
        if(id.StartsWith("tree-mode.",StringComparison.Ordinal)&&int.TryParse(id[10..],out var mode)&&mode is >=0 and <=2&&Capabilities.WeaponTreeModes&&pendingBefore is null)
        {
            Work(async token=>
            {
                var next=await engine.ExecuteAsync(new{op="tree-mode",value=mode},token,result!.Xml);
                lock(gate)result=next;
                await RefreshCommittedMapAsync(token);
            },"Changing passive allocation target…");return true;
        }
        if(id=="tree-tattoo"&&detailKind=="node"&&pendingBefore is null)
        {
            var node=detailId;
            Work(async token=>
            {
                var choices=await engine.ExecuteAsync(new{op="tattoo-choices",id=node},token,result!.Xml);
                lock(gate)OpenFeaturePicker("Choose a tattoo","tree-tattoo-choice.",A(choices.State,"choices"));
            },"Reading eligible tattoos…");return true;
        }
        if(id=="tree-tattoo-remove"&&detailKind=="node"&&pendingBefore is null)
        {Preview(new{op="tattoo-edit",id=detailId,remove=true});return true;}
        if(id.StartsWith("tree-tattoo-choice.",StringComparison.Ordinal)&&featurePicker is {Prefix:"tree-tattoo-choice."} picker&&
            int.TryParse(id[19..],out var choice)&&picker.Choices.Any(c=>I(c,"id")==choice))
        {featurePicker=null;detailKind="node";Preview(new{op="tattoo-edit",id=detailId,choice,key=S(picker.Choices.Single(c=>I(c,"id")==choice),"key")});return true;}
        return false;
    }
}
