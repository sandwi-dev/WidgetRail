using System.Runtime.InteropServices;
using Microsoft.UI.Dispatching;
using Microsoft.Win32.SafeHandles;

namespace WidgetRail.OverlayFrontend.WinUI.Browser.Capture;

/// <summary>Private x64 helper ABI. Source identities and mappings never cross widget IPC.</summary>
internal static class CaptureWorkerNative
{
    private const string Library="CaptureWorkerHost.dll";
    [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)]
    internal struct Configuration
    {
        internal uint Size,Flags;
        internal ulong Epoch,Window,Created;
        internal uint ProcessId,IncludeAudio,MaximumWidth,MaximumHeight,FrameRate,Reserved;
        [MarshalAs(UnmanagedType.ByValTStr,SizeConst=256)] internal string ClassName;
    }
    [StructLayout(LayoutKind.Sequential)]
    internal struct Packet
    {
        internal uint Magic,Version,Kind,Bytes;
        internal ulong Epoch,Sequence;
        internal uint Width,Height;
        internal long Microseconds;
        internal int Result;
        internal uint Dropped;
    }
    [StructLayout(LayoutKind.Sequential)]
    internal struct StopResult {internal uint Forced,Exited;internal int Result;internal uint ProcessId;}

    internal sealed class WorkerHandle:SafeHandleZeroOrMinusOneIsInvalid
    {
        internal WorkerHandle(nint value):base(true)=>SetHandle(value);
        protected override bool ReleaseHandle(){WrailCaptureWorkerRelease(handle);return true;}
    }
    internal sealed class BufferHandle:SafeHandleZeroOrMinusOneIsInvalid
    {
        private readonly DispatcherQueue dispatcher;
        internal BufferHandle(nint value,DispatcherQueue dispatcher):base(true){this.dispatcher=dispatcher;SetHandle(value);}
        protected override bool ReleaseHandle()
        {
            // Normal disposal is on the creating UI thread. A retained pump may
            // release its last lease on a background thread after cancellation.
            var value=handle;
            if(dispatcher.HasThreadAccess){WrailCaptureBuffersRelease(value);return true;}
            return dispatcher.TryEnqueue(()=>WrailCaptureBuffersRelease(value));
        }
    }
    [DllImport(Library,CharSet=CharSet.Unicode,ExactSpelling=true,CallingConvention=CallingConvention.StdCall)]
    internal static extern int WrailCaptureWorkerStart(string executable,in Configuration configuration,ulong videoMapping,uint videoBytes,ulong audioMapping,uint audioBytes,out nint worker);
    [DllImport(Library,ExactSpelling=true,CallingConvention=CallingConvention.StdCall)]
    internal static extern int WrailCaptureWorkerRead(WorkerHandle worker,out Packet packet,uint timeoutMilliseconds);
    [DllImport(Library,ExactSpelling=true,CallingConvention=CallingConvention.StdCall)]
    internal static extern int WrailCaptureWorkerAcknowledge(WorkerHandle worker,ulong epoch,uint kind,ulong sequence);
    [DllImport(Library,ExactSpelling=true,CallingConvention=CallingConvention.StdCall)]
    internal static extern int WrailCaptureWorkerHeartbeat(WorkerHandle worker,ulong epoch);
    [DllImport(Library,ExactSpelling=true,CallingConvention=CallingConvention.StdCall)]
    internal static extern int WrailCaptureWorkerEncoding(WorkerHandle worker,ulong epoch,uint generation,uint bitrate,uint requestKeyframe);
    [DllImport(Library,ExactSpelling=true,CallingConvention=CallingConvention.StdCall)]
    internal static extern int WrailCaptureWorkerStop(WorkerHandle worker,uint timeoutMilliseconds,out StopResult result);
    [DllImport(Library,ExactSpelling=true,CallingConvention=CallingConvention.StdCall)]
    internal static extern uint WrailCaptureWorkerProcessId(WorkerHandle worker);
    [DllImport(Library,ExactSpelling=true,CallingConvention=CallingConvention.StdCall)]
    private static extern void WrailCaptureWorkerRelease(nint worker);
    [DllImport(Library,ExactSpelling=true,CallingConvention=CallingConvention.StdCall)]
    internal static extern int WrailCaptureBuffersCreate(nint core,uint videoBytes,uint audioBytes,out nint buffers);
    [DllImport(Library,ExactSpelling=true,CallingConvention=CallingConvention.StdCall)]
    internal static extern int WrailCaptureBuffersMappings(BufferHandle buffers,out ulong video,out ulong audio);
    [DllImport(Library,CharSet=CharSet.Unicode,ExactSpelling=true,CallingConvention=CallingConvention.StdCall)]
    internal static extern int WrailCaptureBuffersPost(nint core,BufferHandle buffers,uint kind,string metadata);
    [DllImport(Library,ExactSpelling=true,CallingConvention=CallingConvention.StdCall)]
    private static extern void WrailCaptureBuffersRelease(nint buffers);
}
