using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using WidgetRail.OverlayFrontend.WinUI.Validation;

namespace WidgetRail.OverlayFrontend.WinUI.Shell;

internal sealed partial class OverlayShellPage
{
    private async Task VerifyAdjustmentOutsideClicksAsync(PinnedSurface target,Action<bool,string> check)
    {
        await SelectAsync(target.WidgetId,enterWidget:true);
        await WebAppUntil(()=>MainFocusEnabled&&WidgetSurface.IsLoaded&&WidgetSurface.ActualWidth>0);
        var original=target.Window.Bounds;
        var originalOpacity=target.Window.OpacityPercent;
        foreach(var region in new FrameworkElement[]{WidgetSurface,Tray})
        {
            var mainWidget=activeWidget;
            var input=0;
            PointerEventHandler observed=(_,_)=>input++;
            var protectedContent=ReferenceEquals(region,Tray)?(UIElement)Tray:surface!;
            protectedContent.AddHandler(UIElement.PointerPressedEvent,observed,true);
            protectedContent.AddHandler(UIElement.PointerReleasedEvent,observed,true);
            try
            {
                await ManagePinAsync(target,()=>true);
                SetPinnedOpacity(originalOpacity==55?60:55);
                StepPinnedPlacement(PinnedPlacementDirection.Left,false);
                await NativeContextProbeInput.ClickWindowElementAsync((nint)hostWindow,region,coordinateRoot:ProductionRoot);
                await WebAppUntil(()=>!PinnedAdjustmentActive);
                check(visible&&foreground&&pinAdjustmentToolbar is null,
                    "Clicking the main "+(ReferenceEquals(region,Tray)?"tray":"widget")+" cancels adjustment without dismissing the overlay");
                check(input==0&&activeWidget==mainWidget&&target.Window.Bounds==original&&target.Window.OpacityPercent==originalOpacity,
                    "The return click is consumed and restores the complete unsaved preview without activating underlying content");
            }
            finally
            {
                protectedContent.RemoveHandler(UIElement.PointerPressedEvent,observed);
                protectedContent.RemoveHandler(UIElement.PointerReleasedEvent,observed);
            }
        }
        await SelectAsync(target.WidgetId,enterWidget:false);
        PrepareRadialBackEntry();RefreshRadialChooser();FocusTray();
        await WebAppUntil(()=>RadialOpen&&radialView is {Visibility:Visibility.Visible,ActualWidth:>0});
        await ManagePinAsync(target,()=>true);
        await NativeContextProbeInput.ClickWindowElementAsync((nint)hostWindow,radialView!,coordinateRoot:ProductionRoot);
        await WebAppUntil(()=>!PinnedAdjustmentActive);
        check(visible&&RadialOpen,"Clicking the retained widget wheel cancels adjustment and leaves the overlay open");
        radialRequested=false;await SelectAsync(target.WidgetId,enterWidget:true);
        await WebAppUntil(()=>MainFocusEnabled);
        await ManagePinAsync(target,()=>true);
        var main=App.Window!.AppWindow;
        await NativeContextProbeInput.ClickWindowElementAsync((nint)hostWindow,ProductionRoot,
            screenPoint:new(main.Position.X+8,main.Position.Y+8));
        await WebAppUntil(()=>!visible);
        check(!PinnedAdjustmentActive&&target.IsCurrent,"A genuine empty-backdrop click still hides the overlay and cancels adjustment");
        NativePickerVisibilityForValidation!(true);
        await WebAppUntil(()=>visible&&MainFocusEnabled);
    }
}
