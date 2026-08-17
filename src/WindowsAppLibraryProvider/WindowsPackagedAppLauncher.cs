using System.Runtime.InteropServices;

namespace WidgetRail.WindowsAppLibraryProvider;

[Flags]
internal enum ActivateOptions : uint
{
    None = 0,
}

/// <summary>
/// Activates one provider-revalidated AppsFolder application. Arguments are
/// always null and the returned process ID is deliberately discarded.
/// </summary>
internal sealed class WindowsPackagedAppLauncher : IWindowsPackagedAppLauncher
{
    public void Launch(string exactAumid, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        if (WindowsAppsFolderApplicationSource.NormalizeAumid(exactAumid) is null)
            throw new ArgumentException("A canonical AppUserModelID is required.", nameof(exactAumid));

        object activationObject = new ApplicationActivationManagerComObject();
        nint activationUnknown = 0;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var manager = (IApplicationActivationManager)activationObject;
            activationUnknown = Marshal.GetIUnknownForObject(activationObject);
            var foregroundResult = CoAllowSetForegroundWindow(activationUnknown, 0);
            if (foregroundResult < 0) Marshal.ThrowExceptionForHR(foregroundResult);
            cancellationToken.ThrowIfCancellationRequested();
            var result = manager.ActivateApplication(
                exactAumid, null, ActivateOptions.None, out _);
            if (result < 0) Marshal.ThrowExceptionForHR(result);
        }
        finally
        {
            if (activationUnknown != 0) Marshal.Release(activationUnknown);
            if (Marshal.IsComObject(activationObject))
                Marshal.FinalReleaseComObject(activationObject);
        }
    }

    [ComImport]
    [Guid("45BA127D-10A8-46EA-8AB7-56EA9078943C")]
    private sealed class ApplicationActivationManagerComObject;

    [ComImport]
    [Guid("2E941141-7F97-4756-BA1D-9DECDE894A3D")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IApplicationActivationManager
    {
        [PreserveSig]
        int ActivateApplication(
            [MarshalAs(UnmanagedType.LPWStr)] string appUserModelId,
            [MarshalAs(UnmanagedType.LPWStr)] string? arguments,
            ActivateOptions options,
            out uint processId);

        [PreserveSig]
        int ActivateForFile(
            [MarshalAs(UnmanagedType.LPWStr)] string appUserModelId,
            nint shellItemArray,
            [MarshalAs(UnmanagedType.LPWStr)] string? verb,
            out uint processId);

        [PreserveSig]
        int ActivateForProtocol(
            [MarshalAs(UnmanagedType.LPWStr)] string appUserModelId,
            nint shellItemArray,
            out uint processId);
    }

    [DllImport("ole32.dll")]
    private static extern int CoAllowSetForegroundWindow(
        nint unknown, nint reserved);
}
