using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;

namespace WidgetRail.OverlayFrontend.WinUI.Shell;

internal sealed partial class OverlayShellPage
{
    private PinnedSurface? managedPin => pinnedAdjustment?.Surface;
    private bool openingPinManagement;
    private bool refreshingPinManagement;
    private bool pinManagementAdjustmentRequested;
    private bool pinManagementInteractionRequested;
    private double reservedPinCommandWidth;
    private bool DesktopPinHelpVisible => PinnedInputActive && pinManagementInteractionRequested;

    private void RefreshPinManagementCommands()
    {
        if(refreshingPinManagement)return;
        refreshingPinManagement=true;
        try
        {
            if(!PinnedInputActive&&!PinnedInteractionRequested)pinManagementInteractionRequested=false;
            ManagePinsButton.Visibility=PinnedAdjustmentActive?Visibility.Collapsed:Visibility.Visible;
            FavoriteMenuButton.Visibility = PinnedAdjustmentActive || PinnedInputActive || RadialOpen ? Visibility.Collapsed : Visibility.Visible;
            FavoriteMenuButton.IsEnabled = visible && !retired && !HostChoiceActive && !LocalInstallActive && !SystemFilePickerOpen && !IsMediaFullscreen;
            var canReturn=!retired&&visible&&(PinnedInteractionRequested||returnPointerPending);
            ReturnFromPinButton.Visibility=canReturn&&!PinnedAdjustmentActive?Visibility.Visible:Visibility.Collapsed;
            ReturnFromPinButton.IsEnabled=canReturn;
            // Widget entry is transient: keep rail chrome steady while the command
            // handlers reject requests until switching finishes.
            ManagePinsButton.IsEnabled=!retired&&visible&&!HostChoiceActive&&!LocalInstallActive&&
                !SystemFilePickerOpen&&!IsMediaFullscreen&&pins.Any(pin=>pin.IsCurrent&&pin.Window.IsVisible);
            PinAdjustmentHelp.Visibility=DesktopPinHelpVisible?Visibility.Visible:Visibility.Collapsed;
            TrayHelp.Visibility=DesktopPinHelpVisible?Visibility.Collapsed:Visibility.Visible;
            PinAdjustmentHelp.Text="Click the main overlay to return.";
            if(shellViewport.Width>0)
            {
                PinManagementCommands.Measure(new(double.PositiveInfinity,ProductionShellGeometry.RailHeight));
                var desired=PinManagementCommands.DesiredSize.Width;
                if(Math.Abs((desired>0?desired+8:0)-reservedPinCommandWidth)>.5)
                    ConfigureRail(Math.Max(1,shellViewport.Width-48/Appearance.InterfaceScale));
            }
        }
        finally{refreshingPinManagement=false;}
    }

    private bool RestorePinManagementAdjustmentFocus(bool allowFocus=true)
    {
        var restore=pinManagementAdjustmentRequested;
        pinManagementAdjustmentRequested=false;
        RefreshPinManagementCommands();
        if(!allowFocus||!restore||retired||!visible||!foreground||!ManagePinsButton.IsEnabled)return false;
        ManagePinsButton.Focus(FocusState.Keyboard);return true;
    }

    private async void ManagePinsClicked(object sender,RoutedEventArgs args)=>await ManageOpenPinsAsync();
    private async void ManagePinsAcceleratorInvoked(KeyboardAccelerator sender,KeyboardAcceleratorInvokedEventArgs args)
    {
        if(switching||!ManagePinsButton.IsEnabled||PinnedAdjustmentActive||surface?.NativeKeyboardBrowser is not null||PinnedInteractionRequested)return;
        args.Handled=true;await ManageOpenPinsAsync();
    }
    private async void InteractPinAcceleratorInvoked(KeyboardAccelerator sender,KeyboardAcceleratorInvokedEventArgs args)
    {
        if(switching||!ManagePinsButton.IsEnabled||PinnedAdjustmentActive||surface?.NativeKeyboardBrowser is not null||PinnedInteractionRequested||!HasInteractivePins)return;
        args.Handled=true;
        await ChoosePinAsync("Interact with pin",_=>EnterPinnedAsync(),interactionOnly:true);
    }
    private void ReturnFromPinClicked(object sender,RoutedEventArgs args)
    {
        ExitPinnedInteraction(restoreMain:true);
        CompleteReturnPointer();
        RefreshPinManagementCommands();
        if(ManagePinsButton.IsEnabled)ManagePinsButton.Focus(FocusState.Keyboard);
    }

    private async Task ManageOpenPinsAsync()
    {
        if(openingPinManagement||retired||!visible||!foreground||switching||HostChoiceActive||
            LocalInstallActive||SystemFilePickerOpen||IsMediaFullscreen||PinnedAdjustmentActive)return;
        var requestOwner=owner;var session=visibleSince;
        bool Current()=>!retired&&visible&&foreground&&visibleSince==session&&ReferenceEquals(owner,requestOwner);
        openingPinManagement=true;
        try
        {
            CloseTrayMenu(false);
            await ChoosePinAsync("Adjust pin",target=>ManagePinAsync(target,Current),selectTarget:false);
        }
        catch(OperationCanceledException)when(retired){}
        catch(Exception error){ReportOperationFailure(error,Current);}
        finally
        {
            openingPinManagement=false;RefreshPinManagementCommands();
            if(Current()&&!PinnedAdjustmentActive&&!PinnedInputActive&&ManagePinsButton.IsEnabled)ManagePinsButton.Focus(FocusState.Keyboard);
        }
    }

    private async Task ManagePinAsync(PinnedSurface target,Func<bool> requestCurrent)
    {
        if(!requestCurrent()||!pins.Contains(target)||!target.IsCurrent||!target.Window.IsVisible)return;
        if(!ReferenceEquals(pinned,target))ExitPinnedInteraction(restoreMain:false);
        pinned=target;pinManagementAdjustmentRequested=openingPinManagement;
        await BeginPinnedAdjustmentAsync();
    }
}
