using System.Runtime.InteropServices;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using WidgetRail.OverlayFrontend.WinUI.Browser;

namespace WidgetRail.OverlayFrontend.WinUI.Shell;

internal sealed partial class OverlayShellPage
{
    private bool observingMainPinFocus;
    private static BrowserSurface? NativeKeyboardBrowser(PinnedSurface pin)=>pin.Popup?.Child.ActiveSurface is {HasNativeKeyboardFocus:true} popup
        ?popup:pin.Presenter?.NativeKeyboardBrowser;

    private void AttachPinnedForeground(PinnedSurface current)
    {
        if(!observingMainPinFocus)
        {
            observingMainPinFocus=true;
            // A real main-root click is an explicit return, including clicks on
            // a control that was already this root's remembered focus target.
            AddHandler(UIElement.PointerPressedEvent,new PointerEventHandler((_,_)=>
            {
                if(pinned is {Window.Interactive:true})ExitPinnedInteraction(restoreMain:false);
            }),true);
            GotFocus+=(_,_)=>QueueBorrowedBrowserFocusCheck();
        }
        current.Window.AutomationRoot.GotFocus+=(_,_)=>QueueBorrowedBrowserFocusCheck();
        current.Window.AutomationRoot.LostFocus+=(_,_)=>QueueBorrowedBrowserFocusCheck();
        current.Window.ForegroundChanged+=focused=>
        {
            current.HasForeground=focused;
            if(focused)
            {
                ClearBorrowedBrowserFocus(current);
                current.FocusAcquired?.TrySetResult();
            }
            else if(ReferenceEquals(pinned,current))
            {
                var browser=NativeKeyboardBrowser(current);
                if(browser is null)ExitPinnedInteraction(restoreMain:false);
                else DispatcherQueue.TryEnqueue(()=>SettlePinnedBrowserFocus(current,browser));
            }
            PublishControllerGuide();
        };
    }

    private void SettlePinnedBrowserFocus(PinnedSurface current,BrowserSurface browser)
    {
        if(retired||!visible||!pins.Contains(current)||!ReferenceEquals(pinned,current)||!current.IsCurrent||!current.Window.Interactive)return;
        // A later registered browser may already own a newer exact receipt.
        // This delayed loss cannot revoke that independently validated owner.
        if(current.BorrowedBrowserFocus?.Invoke()==true)return;
        var nativeForeground=PinnedNativeForeground();
        // Ignore a stale loss notification after ordinary native activation recovered.
        if(nativeForeground==current.Window.Handle&&current.HasForeground)return;
        var keyboard=PinnedNativeKeyboardFocus();
        if(foreground&&nativeForeground==(nint)hostWindow&&keyboard!=0&&
            ReferenceEquals(NativeKeyboardBrowser(current),browser))
        {
            // WinUI retains the WebView controller's creation HWND while its
            // visual lives in a pin. Bind this exception to the exact current
            // registered browser AND the actual native keyboard-focus receipt.
            current.BorrowedBrowserFocus=()=>!retired&&visible&&foreground&&pins.Contains(current)&&
                ReferenceEquals(pinned,current)&&current.IsCurrent&&current.Window.Interactive&&
                PinnedNativeForeground()==(nint)hostWindow&&PinnedNativeKeyboardFocus()==keyboard&&
                ReferenceEquals(NativeKeyboardBrowser(current),browser);
            current.Window.SetOwnedBrowserFocus(true);
            PublishControllerGuide();
            return;
        }
        ExitPinnedInteraction(restoreMain:false);
    }

    private void QueueBorrowedBrowserFocusCheck()
    {
        if(pinned is not {BorrowedBrowserFocus: {} ownership} current)return;
        DispatcherQueue.TryEnqueue(()=>
        {
            if(!ReferenceEquals(pinned,current)||!ReferenceEquals(current.BorrowedBrowserFocus,ownership))return;
            if(!ownership())ExitPinnedInteraction(restoreMain:false);
        });
    }
    private static void ClearBorrowedBrowserFocus(PinnedSurface current)
    {current.BorrowedBrowserFocus=null;current.Window.SetOwnedBrowserFocus(false);}

    private void ActivatePinForHostDialog(PinnedSurface current)
    {
        if(current.BorrowedBrowserFocus is null)return;
        NativeKeyboardBrowser(current)?.FocusInteraction?.Invoke();
        ClearBorrowedBrowserFocus(current);
        current.Window.Activate();
        if(PinnedNativeForeground()!=current.Window.Handle)ExitPinnedInteraction(restoreMain:false);
    }
    [LibraryImport("user32.dll",EntryPoint="GetForegroundWindow")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial nint PinnedNativeForeground();
    [LibraryImport("user32.dll",EntryPoint="GetFocus")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial nint PinnedNativeKeyboardFocus();
}
