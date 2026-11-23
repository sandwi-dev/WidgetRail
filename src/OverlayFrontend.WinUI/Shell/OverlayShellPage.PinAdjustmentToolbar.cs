using Microsoft.UI.Xaml.Input;

namespace WidgetRail.OverlayFrontend.WinUI.Shell;

internal sealed partial class OverlayShellPage
{
    private void OpenPinnedAdjustmentToolbar()
    {
        var adjustment=pinnedAdjustment??throw new InvalidOperationException("No pin adjustment is active.");
        var toolbar=new PinnedAdjustmentToolbar((nint)hostWindow);
        pinAdjustmentToolbar=toolbar;
        bool Current()=>ReferenceEquals(pinAdjustmentToolbar,toolbar)&&ReferenceEquals(pinnedAdjustment,adjustment)&&
            pins.Contains(adjustment.Surface)&&adjustment.Surface.IsCurrent;
        toolbar.OpacityChanged+=value=>{if(Current())SetPinnedOpacity(value);};
        toolbar.SaveRequested+=async()=>{if(Current())await FinishPinnedAdjustmentAsync(save:true);};
        toolbar.CancelRequested+=()=>{if(Current())CancelPinnedAdjustment();};
        toolbar.CloseRequested+=async()=>{if(Current())await CloseAdjustedPinAsync();};
        toolbar.KeyPressed+=(sender,args)=>{if(Current())PinnedAdjustmentKeyDown(sender,args);};
        toolbar.LayoutInvalidated+=()=>{if(Current())RefreshPinnedAdjustmentToolbar();};
        toolbar.Window.ForegroundChanged+=focused=>
        {
            if(focused||!Current())return;
            DispatcherQueue.TryEnqueue(()=>
            {
                if(!Current())return;
                var owner=PinnedNativeForeground();
                if(owner!=(nint)hostWindow&&owner!=toolbar.Window.Handle)CancelPinnedAdjustment(restoreFocus:false);
            });
        };
        RefreshPinnedAdjustmentToolbar();
        toolbar.Focus();
    }

    private void RefreshPinnedAdjustmentToolbar()
    {
        if(pinAdjustmentToolbar is not {} toolbar||pinnedAdjustment is not {} adjustment)return;
        var appearance=AppearanceForDisplay(adjustment.Surface.DisplayId);
        toolbar.Update(adjustment.Placement.Current,adjustment.Placement.Monitor,adjustment.Placement.OpacityPercent,
            appearance,ShellChromePalette.Resolve(ShellPalette,appearance),ShellPalette);
        OverlayWindowFrame.EnsureAbove(toolbar.Window.Handle,adjustment.Surface.Window.Handle);
        if(notificationBanner?.IsVisible==true)PumpNotificationBanners();
    }

    private void RetirePinnedAdjustmentToolbar(bool restoreFocus=true)
    {
        if(pinAdjustmentToolbar is not {} toolbar)return;
        pinAdjustmentToolbar=null;
        if(restoreFocus&&!retired&&visible&&PinnedNativeForeground()==toolbar.Window.Handle)ReturnFromPinnedRequested?.Invoke();
        toolbar.Dispose();
    }

    private async Task CloseAdjustedPinAsync()
    {
        if(pinnedAdjustment is not {} adjustment||savingPinnedAdjustment)return;
        var target=adjustment.Surface;
        CancelPinnedAdjustment();
        if(!retired&&pins.Contains(target)&&target.IsCurrent)await UnpinAsync(save:false,target);
    }
}
