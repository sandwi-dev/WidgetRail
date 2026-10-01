using System.Runtime.InteropServices;
using WidgetRail.OverlayFrontend.WinUI.Shell;
using WidgetRail.OverlayPlatformClient;

namespace WidgetRail.OverlayFrontend.WinUI;

/// <summary>No XAML application, input adapter, profile or Bridge is needed to report recovery.</summary>
internal static partial class ControllerRecoveryStartup
{
    internal static bool TryRun(IReadOnlyList<string> commandLine, IReadOnlyList<string> activation, out int exitCode) =>
        ControllerRecoveryCommand.TryRun(commandLine, activation, ControllerIsolationRecovery.Recover, result =>
        {
            if (MessageBoxW(0, result.Message, ControllerRecoveryCommand.Title,
                result.Succeeded ? 0x40U /* MB_ICONINFORMATION */ : 0x10U /* MB_ICONERROR */) == 0)
                System.Diagnostics.Trace.TraceError("Controller recovery result dialog failed: {0}", Marshal.GetLastPInvokeError());
        }, out exitCode);

    [LibraryImport("user32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial int MessageBoxW(nint owner, string message, string title, uint flags);
}
