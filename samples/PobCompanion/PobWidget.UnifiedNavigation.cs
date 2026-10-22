using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

namespace WidgetRail.Samples.PobCompanion;

public sealed partial class PobWidget
{
    private WidgetView WithUnifiedNavigationFocus(WidgetView view)
    {
        if (unifiedFocusRequest != lastFocusRequestId || unifiedTargetId is not { } target ||
            unifiedTargetSection != section || unifiedTargetDetail != detailKind || busy || unifiedSearchOpen) return view;
        var resolved = WidgetFocusTargetLookup.Resolve(view.Root, target, view.ActiveInputScopeId);
        if (resolved.State is not (WidgetFocusTargetState.Valid or WidgetFocusTargetState.Disabled))
        {
            unifiedTargetId = null; unifiedReadOnlyConfig = null; MissingSearchTarget(); Invalidate(); return view;
        }
        return view with { FocusGroupEntryRequest = new()
        {
            RequestId = unifiedFocusRequest, GroupId = view.Root.Id, TargetId = target,
        } };
    }
    private FocusGroupEntryRequest? UnifiedIndexedFocusRequest()
    {
        if(unifiedFocusRequest!=lastFocusRequestId||unifiedSearchOpen||unifiedTargetSection!=section||unifiedTargetDetail!=detailKind||unifiedIndexedKey is not {} key)return null;
        if(unifiedIndexedKind=="inventory"&&inventoryCollection is not null)
        {
            var index=Array.FindIndex(inventoryQuery.Entries,e=>e.Key==key);
            if(index>=0)return inventoryCollection.Enter("pob.stored.items",unifiedFocusRequest,inventoryCollection.FocusTarget("pob.stored.items",new(key),index));
        }
        if(unifiedIndexedKind=="library"&&library is not null)
        {
            var index=Array.FindIndex(libraryQuery.Entries,e=>e.Key==key);
            if(index>=0)return library.Enter("pob.saved-builds",unifiedFocusRequest,library.FocusTarget("pob.saved-builds",new(key),index));
        }
        if(unifiedIndexedKind=="calculation"&&calculationCollection is not null)
        {
            var index=Array.FindIndex(calculationQuery.Items,e=>e.Id==key);
            if(index>=0)return calculationCollection.Enter("pob.calculation.cells",unifiedFocusRequest,calculationCollection.FocusTarget("pob.calculation.cells",new(key),index));
        }
        return null;
    }
    protected override ValueTask OnLifecycleStateChangedAsync(WidgetLifecycleState previous,WidgetLifecycleState current,CancellationToken stateLifetime)
    {
        if(current!=WidgetLifecycleState.Interactive)
        {
            lock(gate){unifiedSearchOpen=false;unifiedSearchEpoch++;skillOptionsState.Suspend();RetireBuildComparisonRequest();}
        }
        return ValueTask.CompletedTask;
    }
}
