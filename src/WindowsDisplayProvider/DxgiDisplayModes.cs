using System.Runtime.InteropServices;

namespace WidgetRail.WindowsDisplayProvider;

/// <summary>Bounded read-only DXGI enumeration; releases every COM reference before returning.</summary>
internal static unsafe partial class DxgiDisplayModes
{
    [StructLayout(LayoutKind.Sequential)] internal struct Mode
    { public uint Width, Height; public NativeRational Refresh; public uint Format, Scanline, Scaling; }
    [StructLayout(LayoutKind.Sequential)] private struct OutputDescription
    { public fixed char DeviceName[32]; public int Left, Top, Right, Bottom, Attached; public uint Rotation; public nint Monitor; }
    internal static IReadOnlyDictionary<string, Mode[]> Read()
    {
        var result = new Dictionary<string, Mode[]>(StringComparer.OrdinalIgnoreCase);
        var iid = new Guid("7b7166ec-21c7-44ae-b21a-c9ae321ae369"); // IDXGIFactory
        nint factory = 0;
        if (CreateDXGIFactory(&iid, &factory) < 0 || factory == 0) return result;
        try
        {
            for (uint adapterIndex = 0; adapterIndex < 16; ++adapterIndex)
            {
                nint adapter = 0;
                if (((delegate* unmanaged[Stdcall]<nint, uint, nint*, int>)Vtable(factory)[7])(factory, adapterIndex, &adapter) < 0) break;
                try
                {
                    for (uint outputIndex = 0; outputIndex < 16; ++outputIndex)
                    {
                        nint output = 0;
                        if (((delegate* unmanaged[Stdcall]<nint, uint, nint*, int>)Vtable(adapter)[7])(adapter, outputIndex, &output) < 0) break;
                        try
                        {
                            OutputDescription description;
                            if (((delegate* unmanaged[Stdcall]<nint, OutputDescription*, int>)Vtable(output)[7])(output, &description) < 0 || description.Attached == 0) continue;
                            uint count = 0;
                            var enumerate = (delegate* unmanaged[Stdcall]<nint, uint, uint, uint*, Mode*, int>)Vtable(output)[8];
                            if (enumerate(output, 28, 0, &count, null) < 0 || count is 0 or > 4096) continue; // DXGI_FORMAT_R8G8B8A8_UNORM
                            var modes = new Mode[count];
                            fixed (Mode* pointer = modes)
                                if (enumerate(output, 28, 0, &count, pointer) < 0 || count > modes.Length) continue;
                            result[new string(description.DeviceName)] = modes[..(int)count];
                        }
                        finally { Release(output); }
                    }
                }
                finally { Release(adapter); }
            }
        }
        finally { Release(factory); }
        return result;
    }
    private static nint* Vtable(nint instance) => *(nint**)instance;
    private static void Release(nint instance)
    { if (instance != 0) ((delegate* unmanaged[Stdcall]<nint, uint>)Vtable(instance)[2])(instance); }
    [LibraryImport("dxgi.dll"), DefaultDllImportSearchPaths(DllImportSearchPath.System32)] private static partial int CreateDXGIFactory(Guid* iid, nint* factory);
}
