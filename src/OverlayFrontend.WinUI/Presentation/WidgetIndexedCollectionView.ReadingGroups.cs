using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using WidgetRail.OverlayFrontend.WinUI.Collections;
using WidgetRail.WidgetPresentationSession;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

namespace WidgetRail.OverlayFrontend.WinUI.Presentation;

internal sealed partial class WidgetIndexedCollectionView
{
    private bool readingBackPressed;
    private sealed record RailAnchor(string Source, long Generation, long Revision, string Key, double Height, double ThumbnailY);
    private RailAnchor? railAnchor;

    private void CaptureRailAnchor(WidgetIndexedRow row)
    {
        railAnchor = null;
        if (view is null || ReadingView(row) is not { IsActionRail: true } body ||
            FindNativeScroll(view) is not { } scroll) return;
        var thumb = body.SelectedReadingControl;
        if (thumb is null || !WidgetReadingScrollFocus.IsVisible(thumb, scroll)) thumb = body.FocusedReadingControl;
        if (thumb is null || !WidgetReadingScrollFocus.IsVisible(thumb, scroll)) return;
        var descriptor = row.Lease.Range.Source;
        railAnchor = new(descriptor.SourceId, descriptor.QueryGeneration, descriptor.ContentRevision,
            row.Item.Key, body.ActualHeight, LayoutBoundsInScroll(thumb, scroll).Y);
    }

    private void RestoreRailAnchor(WidgetIndexedRow row, FrameworkElement container, FrameworkElement focused)
    {
        var anchor = railAnchor;
        railAnchor = null;
        var current = row.Lease.Range.Source;
        if (anchor is null || anchor.Key != row.Item.Key || anchor.Source != current.SourceId ||
            anchor.Generation != current.QueryGeneration || current.ContentRevision != anchor.Revision + 1 ||
            Math.Abs(container.ActualHeight - anchor.Height) < 2 || view is null || FindNativeScroll(view) is not { } scroll) return;
        var bounds = LayoutBoundsInScroll(container, scroll);
        var visible = WidgetScrollViewport.ContentBounds(scroll);
        var thumb = LayoutBoundsInScroll(focused, scroll);
        // Keep the thumb under the reader. Move it only as far as necessary to
        // fit the expanded entry; very short windows prioritize the selected thumb.
        var fit = bounds.Height <= visible.Height ? bounds : thumb;
        var delta = Math.Clamp(thumb.Y - anchor.ThumbnailY, Math.Min(fit.Bottom - visible.Bottom, fit.Top - visible.Top), fit.Top - visible.Top);
        var offset = Math.Clamp(scroll.VerticalOffset + delta, 0, scroll.ScrollableHeight);
        if (Math.Abs(offset - scroll.VerticalOffset) > .75)
            scroll.ChangeView(null, offset, null, disableAnimation: true);
    }
#if ENABLE_WIDGET_VALIDATION
    internal Action<string>? ReadingActionDiagnosticForValidation { get; set; }
#endif
    internal bool ReadingActionsEnabled=>CanReceiveInput && !SelectionActive;
    private WidgetIndexedRowView? ReadingView(WidgetIndexedRow row)=>row.Lease.IsCurrent && row.Item.Root.IsReadingGroup &&
        row.Item.Root is {IsDisabled:not true,IsBusy:not true}
        ? containers.Values.FirstOrDefault(item=>ReferenceEquals(item.Slot.Value,row)).LayoutOwner:null;
    private WidgetIndexedRowView? FocusedReadingView=>ReadingActionsEnabled && FocusedRow() is {} row?ReadingView(row):null;
    internal ViewNode? FocusedReadingAction=>FocusedReadingView?.FocusedReadingAction;
    internal bool CanEnterReadingActions=>FocusedReadingView is {FocusedReadingAction:null,HasReadingActions:true};
    internal bool HandleReadingMedia(ControllerButton button, ControllerEventPhase phase) => FocusedReadingView?.HandleReadingMedia(button,phase)==true;
    internal IReadOnlyList<ControllerGuideHint>? ReadingMediaGuide => FocusedReadingView?.ReadingMediaGuide;
    internal (object Identity, bool Available)? ReadingMediaHeldAction(ControllerButton button) => FocusedReadingView?.ReadingMediaHeldAction(button);

    internal bool HandleReadingKey(KeyRoutedEventArgs args)
    {
        if (FocusedReadingView is not { } row) return false;
        if (args.Key == Windows.System.VirtualKey.Escape && row.FocusedReadingAction is not null)
        {
            if (row.HandleReadingMedia(ControllerButton.B,ControllerEventPhase.Pressed))
            { row.HandleReadingMedia(ControllerButton.B,ControllerEventPhase.Released); args.Handled=true; return true; }
            if (row.IsActionRail && FocusedRow() is { } focused && focused.Item.Root.Shortcuts.Any(
                shortcut => shortcut.Button == ControllerButton.B && shortcut.Phase == ControllerEventPhase.Pressed))
            {
                args.Handled = true;
                if (!args.KeyStatus.WasKeyDown) _ = InvokeAsync(focused, ControllerButton.B);
                return true;
            }
            args.Handled = !row.IsActionRail && row.ReturnToReadingRow();
            return args.Handled;
        }
        if (args.Key is not (Windows.System.VirtualKey.Enter or Windows.System.VirtualKey.Space) ||
            row.FocusedReadingAction is not null || !row.HasReadingActions) return false;
        args.Handled = true;
        if (!args.KeyStatus.WasKeyDown) row.EnterReadingActions();
        return true;
    }

    private bool MoveReadingGroup(FocusNavigationDirection direction)
    {
        if(FocusedReadingView is not {} row)return false;
        if(row.FocusedReadingAction is null)return direction==FocusNavigationDirection.Right && row.EnterReadingActions();
        if(row.MoveReadingFocus(direction))return true;
        // At the left edge, return to the reading row before leaving its panel.
        // Vertical boundaries continue through ordinary indexed navigation.
        return direction==FocusNavigationDirection.Left && !row.IsActionRail && row.ReturnToReadingRow();
    }
    private bool ReturnReadingGroup(ControllerButton button,ControllerEventPhase phase)
    {
        if(button==ControllerButton.B && phase==ControllerEventPhase.Released && readingBackPressed)
        {readingBackPressed=false;return true;}
        if(button!=ControllerButton.B || FocusedReadingView is not {FocusedReadingAction:not null, IsActionRail:false} row)return false;
        if(phase==ControllerEventPhase.Pressed){readingBackPressed=true;row.ReturnToReadingRow();}
        return true;
    }
    private bool ActivateReadingGroup(WidgetIndexedRow row,ControllerButton button,ControllerEventPhase phase)
    {
        if(button!=ControllerButton.A || !row.Item.Root.IsReadingGroup)return false;
        if(phase!=ControllerEventPhase.Pressed)return true;
        if(ReadingView(row) is {} body)
        {
            if(body.FocusedReadingAction is not null)body.ActivateReadingAction();
            else body.EnterReadingActions();
        }
        return true;
    }

    internal async Task DispatchReadingActionAsync(WidgetIndexedRow row,WidgetPresentationBinding rowPresentation,WidgetActionEvent action)
    {
        var captured=source;
        if(captured is null || !rowPresentation.SameInput(captured.Presentation))return;
        // The leased row body can outlive many unchanged parent publications.
        // Its cached fragment frame is not the currently displayed collection
        // ancestry and can expire from the bridge's bounded input history.
        // Match ordinary indexed input: capture the collection's displayed
        // parent now, while retaining the exact current row lease and action.
        var displayed=captured.Presentation;
        CaptureRailAnchor(row);
        bool Current()=>!disposed && ReadingActionsEnabled && captured is not null && ReferenceEquals(source,captured) &&
            ReferenceEquals(row.Owner,captured) && row.Lease.IsCurrent && displayed.SameInput(captured.Presentation) &&
            row.Item.Root is {IsReadingGroup:true,IsDisabled:not true,IsBusy:not true};
        if(!Current())return;
        try
        {
            if(EnsureInteractionAsync is {} admit && !await admit(displayed.Frame.Authority,CancellationToken.None))return;
            if(!Current())return;
            if(session.ResolveIndexedIntentAction(row.Lease,displayed.Frame,row.Item.Key,action) is {} intent)
            {
                if(DispatchIntentAsync is {} dispatch)await dispatch(new(displayed.Frame,intent)
                    {IndexedLease=row.Lease,IndexedItemKey=row.Item.Key,PinnedSelection=displayed.Selection,PinnedProjection=displayed.Projection});
            }
            else
            {
                var admission=await row.Lease.AdmitInlineActionAsync(displayed.Frame,row.Item.Key,new(action.SourceElementId,action.ActionId),
                    ++sequence,Environment.TickCount64*1000);
#if ENABLE_WIDGET_VALIDATION
                ReadingActionDiagnosticForValidation?.Invoke($"admission={admission}; row={rowPresentation.Frame.Authority.SnapshotSequence}; displayed={displayed.Frame.Authority.SnapshotSequence}; current={captured.Presentation.Frame.Authority.SnapshotSequence}");
#endif
            }
        }
        catch(WidgetPresentationSessionException error)when(WidgetInputFailure.IsStale(error))
        {
#if ENABLE_WIDGET_VALIDATION
            ReadingActionDiagnosticForValidation?.Invoke($"stale={error.Code}; displayed={displayed.Frame.Authority.SnapshotSequence}; current={captured?.Presentation.Frame.Authority.SnapshotSequence}");
#endif
            session.RequestInputRefresh(displayed.Frame);
        }
        catch(OperationCanceledException)when(!Current()){}
        catch(Exception error){if(Current())failed(error);else Diagnostics.FrontendFailureLog.Current.Write("retired-reading-group-input",error);}
    }
}
