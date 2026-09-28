using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using WidgetRail.WidgetPresentationSession;

namespace WidgetRail.OverlayFrontend.WinUI.Previews;

internal sealed class NativePreviewEngine : SafeHandleZeroOrMinusOneIsInvalid
{
    internal NativePreviewEngine() : base(true)
    { Marshal.ThrowExceptionForHR(PreviewNative.Create(out var value)); SetHandle(value); }
    protected override bool ReleaseHandle() { PreviewNative.Destroy(handle); return true; }
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct NativePreviewTarget
{
    public uint Size, Version;
    public ulong Window;
    public uint ProcessId, Reserved;
    public ulong ProcessCreated;
    public fixed char ClassName[256];
    internal static NativePreviewTarget From(WidgetHostWindowTarget target)
    {
        if (target.ClassName.Length is 0 or >= 256) throw new ArgumentException("Invalid window class identity.");
        NativePreviewTarget value = new() { Size = (uint)sizeof(NativePreviewTarget), Version = 1,
            Window = target.Handle, ProcessId = target.ProcessId, ProcessCreated = target.ProcessCreated };
        for (int index = 0; index < target.ClassName.Length; ++index) value.ClassName[index] = target.ClassName[index];
        return value;
    }
    internal readonly WidgetHostWindowTarget ToTarget(string id)
    { fixed (char* name = ClassName) return new(id, Window, ProcessId, ProcessCreated, new string(name)); }
}

[StructLayout(LayoutKind.Sequential)]
internal struct NativePreviewStats
{
    public uint Size, Version, State, ActiveCount;
    public int Error;
    public uint SourceWidth, SourceHeight, Width, Height, Reserved;
    public ulong Frames, SurfaceGeneration, TotalBytes;
    internal static NativePreviewStats Empty => new() { Size = (uint)Marshal.SizeOf<NativePreviewStats>(), Version = 1 };
}

internal static partial class PreviewNative
{
    private const string Library = "WinUiWindowPreviewNative.dll";
    [LibraryImport(Library, EntryPoint = "WrailPreviewCreate")]
    internal static partial int Create(out nint engine);
    [LibraryImport(Library, EntryPoint = "WrailPreviewDestroy")]
    internal static partial void Destroy(nint engine);
    [LibraryImport(Library, EntryPoint = "WrailPreviewAdd")]
    internal static partial int Add(NativePreviewEngine engine, in NativePreviewTarget target, ulong hostWindow,
        uint width, uint height, uint fit, nint authority, nint context, out ulong id);
    [LibraryImport(Library, EntryPoint = "WrailPreviewRenew")]
    internal static partial int Renew(NativePreviewEngine engine, ulong id, long deadline);
    [LibraryImport(Library, EntryPoint = "WrailPreviewResize")]
    internal static partial int Resize(NativePreviewEngine engine, ulong id, uint width, uint height, uint fit);
    [LibraryImport(Library, EntryPoint = "WrailPreviewRemove")]
    internal static partial int Remove(NativePreviewEngine engine, ulong id);
    [LibraryImport(Library, EntryPoint = "WrailPreviewSwapChain")]
    internal static partial int SwapChain(NativePreviewEngine engine, ulong id, out nint surface, out ulong generation);
    [LibraryImport(Library, EntryPoint = "WrailPreviewInspect")]
    internal static partial int Inspect(NativePreviewEngine engine, ulong id, ref NativePreviewStats stats);
    [LibraryImport(Library, EntryPoint = "WrailPreviewResetDevice")]
    internal static partial int ResetDevice(NativePreviewEngine engine);
    [LibraryImport(Library, EntryPoint = "WrailPreviewReadIdentity")]
    internal static partial int ReadIdentity(ulong window, ref NativePreviewTarget target);
}
