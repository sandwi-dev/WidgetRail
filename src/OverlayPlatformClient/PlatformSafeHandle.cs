using Microsoft.Win32.SafeHandles;

namespace WidgetRail.OverlayPlatformClient;

internal sealed class PlatformSafeHandle : SafeHandleZeroOrMinusOneIsInvalid
{
    private readonly IOverlayPlatformNative native;
    private readonly PlatformCallbacks callbacks;

    public PlatformSafeHandle(nint value, IOverlayPlatformNative native, PlatformCallbacks callbacks) : base(true)
    {
        this.native = native;
        this.callbacks = callbacks;
        SetHandle(value);
    }

    protected override bool ReleaseHandle()
    {
        callbacks.CloseAdmission();
        // Native Destroy includes idempotent Shutdown and waits for callbacks.
        // Context must remain allocated until this call has completed.
        native.Destroy(handle);
        callbacks.Dispose();
        return true;
    }
}
