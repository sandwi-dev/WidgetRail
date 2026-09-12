using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace WidgetRail.WindowsAudioProvider;

// Windows exposes no documented Core Audio default-endpoint setter. Keep the
// policy COM ABI confined here; nothing native crosses the broker boundary.
[SupportedOSPlatform("windows")]
internal static class WindowsDefaultAudioDevicePolicy
{
    internal static bool TrySwitch(string deviceId, Func<ERole, string?> read)
    {
        object? policyObject = null;
        try
        {
            var type = Type.GetTypeFromCLSID(new Guid("870AF99C-171D-4F9E-AF0D-E63DF40C2BC9"), throwOnError: true)!;
            policyObject = Activator.CreateInstance(type);
            var policy = (IPolicyConfig)policyObject!;
            return DefaultAudioDeviceSwitch.TrySwitch(deviceId, read,
                (id, role) => policy.SetDefaultEndpoint(id, role) >= 0);
        }
        catch (Exception exception) when (exception is COMException or InvalidCastException or TypeLoadException)
        {
            return false;
        }
        finally { CoreAudioInterop.Release(policyObject); }
    }

    [ComImport, Guid("F8679F50-850A-41CF-9C72-430F290290C8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPolicyConfig
    {
        // Uncalled slots through SetPropertyValue, in Windows policy ABI order.
        [PreserveSig] int Reserved01();
        [PreserveSig] int Reserved02();
        [PreserveSig] int Reserved03();
        [PreserveSig] int Reserved04();
        [PreserveSig] int Reserved05();
        [PreserveSig] int Reserved06();
        [PreserveSig] int Reserved07();
        [PreserveSig] int Reserved08();
        [PreserveSig] int Reserved09();
        [PreserveSig] int Reserved10();
        [PreserveSig] int SetDefaultEndpoint([MarshalAs(UnmanagedType.LPWStr)] string deviceId, ERole role);
    }
}

internal static class DefaultAudioDeviceSwitch
{
    internal static bool TrySwitch(string target, Func<ERole, string?> read, Func<string, ERole, bool> write)
    {
        var console = read(ERole.Console);
        var multimedia = read(ERole.Multimedia);
        if ((console == target || write(target, ERole.Console)) &&
            (multimedia == target || write(target, ERole.Multimedia)) &&
            read(ERole.Console) == target && read(ERole.Multimedia) == target)
            return true;
        // Restore only our own partial changes, preserving an external change
        // that happened meanwhile. Communications is never written.
        if (console is not null && console != target && read(ERole.Console) == target)
            _ = write(console, ERole.Console);
        if (multimedia is not null && multimedia != target && read(ERole.Multimedia) == target)
            _ = write(multimedia, ERole.Multimedia);
        return false;
    }
}
