using System.Runtime.InteropServices;

namespace GameBarAlternative.WindowsNetworkProvider;

internal static class NetworkInterop
{
    private const uint CoinitMultithreaded = 0;
    private const int RpcEChangedMode = unchecked((int)0x80010106);

    [DllImport("ole32.dll")]
    private static extern int CoInitializeEx(IntPtr reserved, uint coInit);

    [DllImport("ole32.dll")]
    private static extern void CoUninitialize();

    internal static bool InitializeMta()
    {
        var result = CoInitializeEx(IntPtr.Zero, CoinitMultithreaded);
        if (result == RpcEChangedMode)
            throw new InvalidOperationException("The network provider owner thread is not an MTA.");
        Marshal.ThrowExceptionForHR(result);
        return true;
    }

    internal static void Uninitialize() => CoUninitialize();

    internal static void ReleaseCom(object? value)
    {
#pragma warning disable CA1416 // Provider is Windows-only and called from its guarded adapter.
        if (value is not null && Marshal.IsComObject(value)) Marshal.FinalReleaseComObject(value);
#pragma warning restore CA1416
    }
}
