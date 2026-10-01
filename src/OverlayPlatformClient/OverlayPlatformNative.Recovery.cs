using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace WidgetRail.OverlayPlatformClient;

public sealed partial class OverlayPlatformNative : IControllerIsolationRecoveryNative
{
    public unsafe PlatformStatus RecoverControllerIsolation(Span<char> message)
    {
        if (message.IsEmpty) throw new ArgumentException("Recovery requires a diagnostic buffer.", nameof(message));
        fixed (char* buffer = message)
            return NativeRecoverControllerIsolation(buffer, checked((uint)message.Length));
    }

    [LibraryImport("OverlayPlatformInterop.dll", EntryPoint = "WidgetRailOverlayPlatformRecoverControllerIsolation")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvStdcall)])]
    [DefaultDllImportSearchPaths(DllImportSearchPath.AssemblyDirectory | DllImportSearchPath.System32)]
    private static unsafe partial PlatformStatus NativeRecoverControllerIsolation(char* message, uint capacity);
}
