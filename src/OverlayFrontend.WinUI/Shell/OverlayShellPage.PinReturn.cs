using System.Runtime.InteropServices;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using WinUIEx.Messaging;

namespace WidgetRail.OverlayFrontend.WinUI.Shell;

internal sealed partial class OverlayShellPage
{
    private WindowMessageMonitor? returnPointerMonitor;
    private bool returnPointerPending;
    private void InitializePinReturnBoundary()
    {
        // Native cancellation/deactivation ends a pending Return gesture. The
        // interaction owner captures its start before changing the rail layout.
        if(hostWindow!=0)
        {
            returnPointerMonitor=new((nint)hostWindow);
            returnPointerMonitor.WindowMessageReceived+=(_,args)=>
            {
                if(args.Message.MessageId==0x001f||args.Message.MessageId==0x0006&&((long)args.Message.WParam&0xffff)==0)
                    CompleteReturnPointer();
            };
        }
        // A browser pin can already own native focus through the main HWND.
        // Observe before the registered pin's main-root return handler runs.
        AddHandler(UIElement.PointerPressedEvent,new PointerEventHandler((_,args)=>
        {
            if(!args.GetCurrentPoint(this).Properties.IsLeftButtonPressed)return;
            var point=args.GetCurrentPoint(null).Position;PreserveReturnPointer(point.X,point.Y);
        }),true);
        AddHandler(UIElement.PointerReleasedEvent,new PointerEventHandler((_,_)=>CompleteReturnPointer()),true);
        AddHandler(UIElement.PointerCanceledEvent,new PointerEventHandler((_,_)=>CompleteReturnPointer()),true);
        AddHandler(UIElement.PointerCaptureLostEvent,new PointerEventHandler((_,_)=>CompleteReturnPointer()),true);
    }
    private void PreserveReturnPointerOnFocusLoss()
    {
        // The pin owns this transition. Inspect the initiating mouse state before
        // revoking interaction; WinUI island activation can precede the main HWND's
        // mouse messages, so observing only that HWND is too late.
        if((GetPinReturnKeyState(1)&0x8000)==0||GetPinReturnCursor(out var point)==0)return;
        var origin=App.Window!.AppWindow.Position;var scale=XamlRoot?.RasterizationScale??1;
        PreserveReturnPointer((point.X-origin.X)/scale,(point.Y-origin.Y)/scale);
    }
    private void PreserveReturnPointer(double x,double y)
    {
        if(!ReturnFromPinButton.IsEnabled||ReturnFromPinButton.Visibility!=Visibility.Visible||ReturnFromPinButton.XamlRoot is null)return;
        var bounds=ReturnFromPinButton.TransformToVisual(null).TransformBounds(new(0,0,ReturnFromPinButton.ActualWidth,ReturnFromPinButton.ActualHeight));
        if(bounds.Contains(new(x,y)))returnPointerPending=true;
    }
    private void CompleteReturnPointer()
    {
        if(!returnPointerPending)return;
        DispatcherQueue.TryEnqueue(()=>{returnPointerPending=false;RefreshPinManagementCommands();});
    }
    [StructLayout(LayoutKind.Sequential)]private struct ReturnCursor{internal int X,Y;}
    [LibraryImport("user32.dll",EntryPoint="GetCursorPos"),DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial int GetPinReturnCursor(out ReturnCursor point);
    [LibraryImport("user32.dll",EntryPoint="GetAsyncKeyState"),DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial short GetPinReturnKeyState(int key);
}
