using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

namespace WidgetRail.Samples.PobCompanion;

public sealed partial class PobWidget
{
    private string? unifiedRenderedFocusGroup;
    private WidgetElement WithUnifiedNavigationTarget(WidgetElement root)
    {
        if(unifiedTargetId is not {} target||unifiedTargetSection!=section||unifiedTargetDetail!=detailKind)return root;
        var groupId=WidgetIds.Scope("pob.unified").KeyedId("target",target);
        var found=false;
        WidgetElement Visit(WidgetElement element)
        {
            if(unifiedReadOnlyConfig is {} readOnly&&element.Id=="pob.config.row."+readOnly)
            {
                // This upstream option has no editable control. A reading viewport
                // is the actual destination, preserving its complete value.
                found=true;
                return UI.Stack(groupId,UI.VerticalScroll("pob.config.readonly-scroll",element).Classes("pob-scroll"))
                    .RememberChildFocus("pob.config.readonly-scroll");
            }
            // Keep layout/visibility modifiers attached to the newly authored group;
            // the original control identity, actions and appearance remain unchanged.
            if(element is GridCellElement cell)
            {
                var child=Visit(cell.Child);var p=cell.Placement;
                return ReferenceEquals(child,cell.Child)?cell:child.InGrid(p.Row,p.Column,p.RowSpan,p.ColumnSpan);
            }
            if(element is ResponsiveBranchElement responsive)
            {var child=Visit(responsive.Child);return ReferenceEquals(child,responsive.Child)?responsive:child.VisibleWhen(responsive.Visibility);}
            // Targets are authored control IDs. SDK presentation decorators keep
            // that ID but may wrap the concrete control type (deferred previews).
            if(element.Id==target)
            {
                found=true;
                return UI.Stack(groupId,element).RememberChildFocus(target);
            }
            return element switch
            {
                ContainerElement container=>container with{Children=container.Children.Select(Visit).ToArray()},
                FocusPresentationSurfaceElement surface=>surface with{Content=Visit(surface.Content)},
                BackgroundSurfaceElement background=>background with{Content=Visit(background.Content)},
                _=>element,
            };
        }
        var decorated=Visit(root);
        if(found)unifiedRenderedFocusGroup=groupId;
        return decorated;
    }
    private WidgetView WithUnifiedNavigationFocus(WidgetView view)
    {
        // Search owns only its navigation request. A later explicit focus intent
        // (quick controls, Back, editing, etc.) must supersede that handoff.
        if(unifiedFocusRequest!=lastFocusRequestId||unifiedTargetId is null||unifiedTargetSection!=section||unifiedTargetDetail!=detailKind)return view;
        if(unifiedRenderedFocusGroup is not {} groupId)
        {
            unifiedTargetId=null;unifiedReadOnlyConfig=null;MissingSearchTarget();Invalidate();return view;
        }
        return view with{FocusGroupEntryRequest=busy||unifiedSearchOpen?view.FocusGroupEntryRequest:new(){RequestId=unifiedFocusRequest,GroupId=groupId}};
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
