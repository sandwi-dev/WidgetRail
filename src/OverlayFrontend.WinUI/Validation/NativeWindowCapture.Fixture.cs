using System.Runtime.InteropServices;
using WidgetRail.OverlayFrontend.WinUI.Previews;
using WidgetRail.OverlayFrontend.WinUI.Shell;

namespace WidgetRail.OverlayFrontend.WinUI.Capture;

internal static partial class NativeWindowCapture
{
    // An end-to-end fixture may capture only its own generated external window.
    // If the fixture disappears, fail before entering native capture.
    private static bool TryGetFixtureCaptureTarget(out NativePreviewTarget target, FixedCaptureTarget? fixedTarget)
    {
        var args = Environment.GetCommandLineArgs();
        var window = FrontendArguments.Value(args, "--library-fixture-window");
        target = default;
        if (window is null) return false;
        if (!ulong.TryParse(window, out var handle) || handle == 0 ||
            !uint.TryParse(FrontendArguments.Value(args, "--library-fixture-pid"), out var pid) || pid == Environment.ProcessId)
            throw new InvalidOperationException("Invalid library capture fixture.");
        if (fixedTarget is not null)
        {
            // Keep the original identity for native stale-window checks, including closure during consent.
            target = ToNative(fixedTarget.Target);
            if (target.Window != handle || target.ProcessId != pid) throw new InvalidOperationException("Unexpected screenshot fixture target.");
            return true;
        }
        target.Size = (uint)Marshal.SizeOf<NativePreviewTarget>();
        target.Version = 1;
        Marshal.ThrowExceptionForHR(PreviewNative.ReadIdentity(handle, ref target));
        if (target.ProcessId != pid) throw new InvalidOperationException("Library capture fixture retired.");
        return true;
    }
}
