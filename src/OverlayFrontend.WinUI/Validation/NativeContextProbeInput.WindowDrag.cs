using System.Runtime.InteropServices;
using Windows.Foundation;

namespace WidgetRail.OverlayFrontend.WinUI.Validation;

internal static partial class NativeContextProbeInput
{
    internal static async Task WithPointerParkedAsync(Point point,Func<Task> action)
    {
        DemandOwnForeground();
        if(GetCursorPos(out var previous)==0)throw new InvalidOperationException("Pointer position unavailable.");
        try
        {
            SendPointerMove(new(){X=(int)point.X,Y=(int)point.Y});await Task.Delay(100);
            await action();
        }
        finally{SendPointerMove(previous);}
    }
    internal static async Task<bool> CursorOverWindowAsync(nint target,Point point,int expected)
    {
        DemandOwnForeground();
        var position=new NativePoint{X=(int)point.X,Y=(int)point.Y};
        SendPointerMove(position);await Task.Delay(100);
        if(GetAncestor(WindowFromPoint(position),2)!=target)throw new InvalidOperationException("Cursor probe target changed.");
        var info=new CursorInfo{Size=(uint)Marshal.SizeOf<CursorInfo>()};
        return GetCursorInfo(ref info)!=0&&info.Cursor==LoadCursorW(0,expected);
    }
    internal static Task DragWindowAsync(nint target,Point start,Point end,Func<Task>? whilePressed=null)=>Task.Run(async()=>
    {
        DemandOwnForeground();GetWindowThreadProcessId(target,out var pid);
        if(pid!=Environment.ProcessId)throw new InvalidOperationException("Drag probe only targets its own native fixture.");
        if(GetCursorPos(out var previous)==0)throw new InvalidOperationException("Cursor unavailable.");
        var held=false;
        try
        {
            var origin=new NativePoint{X=(int)start.X,Y=(int)start.Y};
            SendPointerMove(origin);await Task.Delay(80);
            if(GetAncestor(WindowFromPoint(origin),2)!=target)
                throw new InvalidOperationException("Native drag target is not the fixture pin at the requested point.");
            SendLeftButton(false);held=true;await Task.Delay(80);
            if(whilePressed is not null)await whilePressed();
            for(var step=1;step<=8;step++)
            {
                DemandOwnForeground();
                SendPointerMove(new(){X=(int)Math.Round(start.X+(end.X-start.X)*step/8),
                    Y=(int)Math.Round(start.Y+(end.Y-start.Y)*step/8)});
                await Task.Delay(35);
            }
            SendLeftButton(true);held=false;await Task.Delay(120);
        }
        finally {if(held)SendLeftButton(true);SetCursorPos(previous.X,previous.Y);}
    });
    [LibraryImport("user32.dll"),DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial nint WindowFromPoint(NativePoint point);
    [LibraryImport("user32.dll"),DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial nint GetAncestor(nint window,uint flags);
    [LibraryImport("user32.dll"),DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static partial nint GetCapture();
    [LibraryImport("user32.dll"),DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static partial int ReleaseCapture();
    [StructLayout(LayoutKind.Sequential)] private struct CursorInfo{internal uint Size,Flags;internal nint Cursor;internal NativePoint Position;}
    [LibraryImport("user32.dll"),DefaultDllImportSearchPaths(DllImportSearchPath.System32)] private static partial int GetCursorInfo(ref CursorInfo info);
    [LibraryImport("user32.dll"),DefaultDllImportSearchPaths(DllImportSearchPath.System32)] private static partial nint LoadCursorW(nint instance,nint name);
}
