using System.Runtime.InteropServices;
using WinUIEx.Messaging;

namespace WidgetRail.OverlayFrontend.WinUI.Shell;

/// <summary>An unpainted host input frame for activation or one adjustment transaction.</summary>
internal sealed partial class PinnedSurfacePointer:IDisposable
{
    // Same input-window mechanism as OverlayMouseSession. Layered visual peers
    // cannot provide reliable native hit testing over empty composition pixels.
    private const string ClassName="WidgetRail.PinPointer";
    private static ushort windowClass;
    private WindowMessageMonitor? messages;
    private readonly Func<double> scale;
    private readonly Func<PinnedBounds,int,PinnedBounds> preview;
    private readonly Action<Exception> failed;
    private readonly Action<bool>? activate;
    private bool pressed;
    private bool rightPressed;
    private PinnedBounds bounds;
    private bool dragging,disposed;
    private int sizingEdge;
    private Point dragOrigin;
    private PinnedBounds dragBounds;
    internal nint Handle{get;}
    internal PinnedSurfacePointer(nint owner,PinnedBounds bounds,Action<bool> activate,Action<Exception> failed)
        :this(owner,bounds,()=>1,(next,_)=>next,failed,activate) { }
    internal PinnedSurfacePointer(nint owner,PinnedBounds bounds,Func<double> scale,Func<PinnedBounds,int,PinnedBounds> preview,Action<Exception> failed,Action<bool>? activate=null)
    {
        this.bounds=bounds;this.scale=scale;this.preview=preview;this.failed=failed;this.activate=activate;
        EnsureClass();
        Handle=CreateWindowExW(0x08200080,ClassName,"Pin placement",0x80000000,bounds.X,bounds.Y,bounds.Width,bounds.Height,
            owner,0,GetModuleHandleW(0),0); // NOACTIVATE | TOOLWINDOW | NOREDIRECTIONBITMAP, POPUP.
        if(Handle==0)throw new System.ComponentModel.Win32Exception(Marshal.GetLastPInvokeError());
        try
        {
            messages=new(Handle);messages.WindowMessageReceived+=OnMessage;
            if(SetWindowPos(Handle,-1,bounds.X,bounds.Y,bounds.Width,bounds.Height,0x0070)==0)
                throw new System.ComponentModel.Win32Exception(Marshal.GetLastPInvokeError());
        }
        catch {Dispose();throw;}
    }
    internal void Place(PinnedBounds next)
    {
        if(disposed||dragging||bounds==next)return;
        bounds=next;
        if(SetWindowPos(Handle,0,next.X,next.Y,next.Width,next.Height,0x0214)==0)
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastPInvokeError());
    }
    private void OnMessage(object? sender,WindowMessageEventArgs args)
    {
        try {HandleMessage(args);}
        catch(Exception error)when(error is not (OutOfMemoryException or StackOverflowException or AccessViolationException))
        {
            Dispose();args.Handled=true;args.Result=0;
            try {failed(error);}
            catch(Exception reportError)when(reportError is not (OutOfMemoryException or StackOverflowException or AccessViolationException))
            {System.Diagnostics.Trace.TraceWarning("Pin pointer adjustment failure reporting failed: {0}",reportError.GetType().Name);}
        }
    }
    private void HandleMessage(WindowMessageEventArgs args)
    {
        if(disposed)return;
        var message=args.Message.MessageId;
        if(message is 0x0083 or 0x0085 or 0x0014){args.Handled=true;args.Result=0;return;}
        if(activate is not null)
        {
            if(message==0x0084){args.Handled=true;args.Result=1;return;}
            if(message==0x0021){args.Handled=true;args.Result=3;return;}
            if(message is 0x0201 or 0x0204){if(!pressed){pressed=true;rightPressed=message==0x0204;SetCapture(Handle);}args.Handled=true;args.Result=0;return;}
            if(message==(rightPressed?0x0205u:0x0202u))
            {
                var clicked=pressed&&GetCursorPos(out var point)!=0&&point.X>=bounds.X&&point.X<bounds.X+bounds.Width&&
                    point.Y>=bounds.Y&&point.Y<bounds.Y+bounds.Height;
                pressed=false;ReleaseCapture();args.Handled=true;args.Result=0;
                // Consume the complete activating gesture, including its release.
                // The callback queues entry after the native message has returned.
                if(clicked)activate(rightPressed);
                return;
            }
            if(message is 0x0215 or 0x001f){if(pressed){pressed=false;ReleaseCapture();}return;}
            return;
        }
        if(message==0x0084)
        {
            var packed=(long)args.Message.LParam;
            var x=(short)(packed&0xffff);var y=(short)((packed>>16)&0xffff);
            var edge=Math.Max(6,(int)Math.Round(8*scale()));
            var left=x<bounds.X+edge;var right=x>=bounds.X+bounds.Width-edge;
            var top=y<bounds.Y+edge;var bottom=y>=bounds.Y+bounds.Height-edge;
            args.Result=top?(left?13:right?14:12):bottom?(left?16:right?17:15):left?10:right?11:2;
            args.Handled=true;return;
        }
        if(message==0x0021){args.Handled=true;args.Result=3;return;}
        if(message==0x00a3){args.Handled=true;args.Result=0;return;}
        if(message==0x00a1) // Host capture, not the default activating system move loop.
        {
            if(GetCursorPos(out dragOrigin)==0)return;
            sizingEdge=(int)args.Message.WParam switch{10=>1,11=>2,12=>3,13=>4,14=>5,15=>6,16=>7,17=>8,_=>0};
            dragBounds=bounds;dragging=true;SetCapture(Handle);
            args.Handled=true;args.Result=0;return;
        }
        if(message is 0x0200 or 0x00a0 && dragging)
        {
            if(GetCursorPos(out var point)!=0)
            {
                var dx=point.X-dragOrigin.X;var dy=point.Y-dragOrigin.Y;
                var next=dragBounds;
                if(sizingEdge==0)next=next with{X=next.X+dx,Y=next.Y+dy};
                else
                {
                    if(sizingEdge is 1 or 4 or 7)next=next with{X=next.X+dx,Width=next.Width-dx};
                    if(sizingEdge is 2 or 5 or 8)next=next with{Width=next.Width+dx};
                    if(sizingEdge is 3 or 4 or 5)next=next with{Y=next.Y+dy,Height=next.Height-dy};
                    if(sizingEdge is 6 or 7 or 8)next=next with{Height=next.Height+dy};
                }
                bounds=preview(next,sizingEdge);
                if(!disposed&&SetWindowPos(Handle,0,bounds.X,bounds.Y,bounds.Width,bounds.Height,0x0214)==0)
                    throw new System.ComponentModel.Win32Exception(Marshal.GetLastPInvokeError());
            }
            args.Handled=true;args.Result=0;return;
        }
        if(message is 0x0202 or 0x00a2 or 0x001f && dragging)
        {dragging=false;ReleaseCapture();args.Handled=true;args.Result=0;return;}
        if(message==0x0215)dragging=false; // Lost capture retires this gesture, not the outer preview.
    }
    public void Dispose()
    {
        if(disposed)return;disposed=true;
        // Revoke callback authority before synchronously releasing native capture;
        // queued pointer messages cannot overwrite restored/saved geometry.
        if(dragging||pressed){dragging=pressed=false;ReleaseCapture();}
        if(messages is not null){messages.WindowMessageReceived-=OnMessage;messages.Dispose();messages=null;}
        DestroyWindow(Handle);
    }
    private static unsafe void EnsureClass()
    {
        if(windowClass!=0)return;
        fixed(char* name=ClassName)
        {
            var registration=new WindowClass{Size=(uint)sizeof(WindowClass),Instance=GetModuleHandleW(0),Name=(nint)name,Cursor=LoadCursorW(0,32512),
                Procedure=(nint)(delegate* unmanaged[Stdcall]<nint,uint,nuint,nint,nint>)&WindowProcedure};
            windowClass=RegisterClassExW(in registration);
            if(windowClass==0)throw new System.ComponentModel.Win32Exception(Marshal.GetLastPInvokeError());
        }
    }
    [UnmanagedCallersOnly(CallConvs=[typeof(System.Runtime.CompilerServices.CallConvStdcall)])]
    private static nint WindowProcedure(nint window,uint message,nuint wparam,nint lparam)=>DefWindowProcW(window,message,wparam,lparam);
    [StructLayout(LayoutKind.Sequential)] private struct Point{internal int X,Y;}
    [StructLayout(LayoutKind.Sequential)] private struct WindowClass
    {internal uint Size,Style;internal nint Procedure;internal int ClassExtra,WindowExtra;internal nint Instance,Icon,Cursor,Background,Menu,Name,SmallIcon;}
    [LibraryImport("user32.dll",SetLastError=true),DefaultDllImportSearchPaths(DllImportSearchPath.System32)] private static partial ushort RegisterClassExW(in WindowClass registration);
    [LibraryImport("user32.dll"),DefaultDllImportSearchPaths(DllImportSearchPath.System32)] private static partial nint DefWindowProcW(nint window,uint message,nuint wparam,nint lparam);
    [LibraryImport("kernel32.dll"),DefaultDllImportSearchPaths(DllImportSearchPath.System32)] private static partial nint GetModuleHandleW(nint name);
    [LibraryImport("user32.dll",StringMarshalling=StringMarshalling.Utf16,SetLastError=true),DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial nint CreateWindowExW(uint extended,string className,string name,uint style,int x,int y,int width,int height,nint parent,nint menu,nint instance,nint parameter);
    [LibraryImport("user32.dll",SetLastError=true),DefaultDllImportSearchPaths(DllImportSearchPath.System32)] private static partial int SetWindowPos(nint window,nint after,int x,int y,int width,int height,uint flags);
    [LibraryImport("user32.dll"),DefaultDllImportSearchPaths(DllImportSearchPath.System32)] private static partial int GetCursorPos(out Point point);
    [LibraryImport("user32.dll"),DefaultDllImportSearchPaths(DllImportSearchPath.System32)] private static partial nint SetCapture(nint window);
    [LibraryImport("user32.dll"),DefaultDllImportSearchPaths(DllImportSearchPath.System32)] private static partial int ReleaseCapture();
    [LibraryImport("user32.dll"),DefaultDllImportSearchPaths(DllImportSearchPath.System32)] private static partial nint LoadCursorW(nint instance,nint name);
    [LibraryImport("user32.dll"),DefaultDllImportSearchPaths(DllImportSearchPath.System32)] private static partial int DestroyWindow(nint window);
}
