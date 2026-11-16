using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;

namespace WidgetRail.OverlayFrontend.WinUI.Shell;

internal sealed partial class OverlayShellPage
{
#if ENABLE_WIDGET_VALIDATION
    private List<string>? pinnedKeyboardTraceForValidation;
    private List<string>? pinReturnGapTraceForValidation;
#endif
    private void AttachPinnedKeyboardReturn(PinnedSurface target)
    {
        var root=target.Window.AutomationRoot;
        root.KeyDown+=(_,args)=>
        {
#if ENABLE_WIDGET_VALIDATION
            if(args.Key==Windows.System.VirtualKey.Escape)pinnedKeyboardTraceForValidation?.Add(
                $"escape: source={args.OriginalSource?.GetType().Name}, focus={FocusManager.GetFocusedElement(root.XamlRoot)?.GetType().Name}, owns={target.Presenter?.OwnsKeyboardEscape}, allowed={CanReturn()}, handled={args.Handled}");
#endif
            if(args.Handled||args.Key!=Windows.System.VirtualKey.Escape||args.KeyStatus.WasKeyDown||!CanReturn())return;
            // Native/portal handlers on the same root still get first refusal.
            // WebView page key events are not forwarded here or intercepted.
            root.DispatcherQueue.TryEnqueue(()=>
            {
                if(!args.Handled&&CanReturn())
                {
#if ENABLE_WIDGET_VALIDATION
                    pinnedKeyboardTraceForValidation?.Add("native root returned to overlay");
#endif
                    ExitPinnedInteraction(restoreMain:true);
                }
            });
        };
        bool CanReturn()=>!retired&&visible&&pins.Contains(target)&&ReferenceEquals(pinned,target)&&target.IsCurrent&&
            target.Window.Interactive&&target.HasForeground&&pinnedAdjustment is null&&!savingPinnedAdjustment&&
            !HostChoiceActive&&!LocalInstallActive&&!SystemFilePickerOpen&&!nativeBrowserPickerActive&&ActiveControllerKeyboard is null&&
            target.Presenter?.OwnsKeyboardEscape!=true&&target.Popup?.Child.HasNativeKeyboardFocus!=true&&
            root.XamlRoot is { } xaml&&FocusManager.GetFocusedElement(xaml) is not (TextBox or PasswordBox or RichEditBox or AutoSuggestBox or WebView2)&&
            !VisualTreeHelper.GetOpenPopupsForXamlRoot(xaml).Any(popup=>popup.IsOpen&&popup.Child is not ToolTip);
    }
}
