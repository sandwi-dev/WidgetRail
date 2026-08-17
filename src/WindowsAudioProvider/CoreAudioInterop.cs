using System.Runtime.InteropServices;

namespace WidgetRail.WindowsAudioProvider;

internal static class CoreAudioInterop
{
    private const uint CoinitMultithreaded = 0;
    private const int RpcEChangedMode = unchecked((int)0x80010106);

    [DllImport("ole32.dll")]
    private static extern int CoInitializeEx(IntPtr reserved, uint coInit);

    [DllImport("ole32.dll")]
    private static extern void CoUninitialize();

    [DllImport("ole32.dll")]
    internal static extern int PropVariantClear(ref PropVariant value);

    internal static bool InitializeMta()
    {
        var hr = CoInitializeEx(IntPtr.Zero, CoinitMultithreaded);
        if (hr == RpcEChangedMode)
            throw new InvalidOperationException("The Core Audio owner thread is not an MTA.");
        ThrowIfFailed(hr);
        return true;
    }

    internal static void Uninitialize() => CoUninitialize();
    internal static void ThrowIfFailed(int hr) => Marshal.ThrowExceptionForHR(hr);

    internal static void Release(object? value)
    {
#pragma warning disable CA1416 // Called only by the Windows-guarded Core Audio adapter.
        if (value is not null && Marshal.IsComObject(value)) Marshal.ReleaseComObject(value);
#pragma warning restore CA1416
    }

    internal static void ReleaseFinal(object? value)
    {
#pragma warning disable CA1416 // Called only by the Windows-guarded Core Audio adapter.
        if (value is not null && Marshal.IsComObject(value)) Marshal.FinalReleaseComObject(value);
#pragma warning restore CA1416
    }

    internal static void AddRef(IntPtr value)
    {
#pragma warning disable CA1416 // Called only from a Windows Core Audio callback.
        Marshal.AddRef(value);
#pragma warning restore CA1416
    }

    internal static void ReleasePointer(IntPtr value)
    {
#pragma warning disable CA1416 // Balances callback AddRef on the Windows owner thread.
        Marshal.Release(value);
#pragma warning restore CA1416
    }

    internal static object GetObject(IntPtr value)
    {
#pragma warning disable CA1416 // Called only by the Windows-guarded Core Audio adapter.
        return Marshal.GetObjectForIUnknown(value);
#pragma warning restore CA1416
    }
}

internal enum EDataFlow
{
    Render,
    Capture,
    All,
}

internal enum ERole
{
    Console,
    Multimedia,
    Communications,
}

internal enum AudioSessionState
{
    Inactive,
    Active,
    Expired,
}

internal enum AudioSessionDisconnectReason
{
    DeviceRemoval,
    ServerShutdown,
    FormatChanged,
    SessionLogoff,
    SessionDisconnected,
    ExclusiveModeOverride,
}

[StructLayout(LayoutKind.Sequential)]
internal readonly struct PropertyKey
{
    internal PropertyKey(Guid formatId, uint propertyId)
    {
        FormatId = formatId;
        PropertyId = propertyId;
    }

    internal readonly Guid FormatId;
    internal readonly uint PropertyId;
}

[StructLayout(LayoutKind.Explicit, Size = 24)]
internal struct PropVariant
{
    [FieldOffset(0)] internal ushort VariantType;
    [FieldOffset(8)] internal IntPtr PointerValue;

    internal string? GetString() => VariantType == 31 && PointerValue != IntPtr.Zero
        ? Marshal.PtrToStringUni(PointerValue)
        : null;
}

[ComImport]
[Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
internal sealed class MMDeviceEnumeratorComObject
{
}

[ComImport]
[Guid("A95664D2-9614-4F35-A746-DE8DB63617E6")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMMDeviceEnumerator
{
    [PreserveSig] int EnumAudioEndpoints(EDataFlow dataFlow, uint stateMask, out IMMDeviceCollection? devices);
    [PreserveSig] int GetDefaultAudioEndpoint(EDataFlow dataFlow, ERole role, out IMMDevice? endpoint);
    [PreserveSig] int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string id, out IMMDevice? device);
    [PreserveSig] int RegisterEndpointNotificationCallback(IMMNotificationClient client);
    [PreserveSig] int UnregisterEndpointNotificationCallback(IMMNotificationClient client);
}

[ComImport]
[Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMMDeviceCollection
{
    [PreserveSig] int GetCount(out uint count);
    [PreserveSig] int Item(uint index, out IMMDevice? device);
}

[ComImport]
[Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IPropertyStore
{
    [PreserveSig] int GetCount(out uint count);
    [PreserveSig] int GetAt(uint index, out PropertyKey key);
    [PreserveSig] int GetValue(ref PropertyKey key, out PropVariant value);
    [PreserveSig] int SetValue(ref PropertyKey key, ref PropVariant value);
    [PreserveSig] int Commit();
}

[ComImport]
[Guid("D666063F-1587-4E43-81F1-B948E807363F")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMMDevice
{
    [PreserveSig] int Activate(
        ref Guid interfaceId,
        uint classContext,
        IntPtr activationParameters,
        [MarshalAs(UnmanagedType.IUnknown)] out object? activatedInterface);
    [PreserveSig] int OpenPropertyStore(uint access, out IPropertyStore? properties);
    [PreserveSig] int GetId([MarshalAs(UnmanagedType.LPWStr)] out string? id);
    [PreserveSig] int GetState(out uint state);
}

[ComVisible(true)]
[Guid("7991EEC9-7E89-4D85-8390-6C703CEC60C0")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMMNotificationClient
{
    [PreserveSig] int OnDeviceStateChanged(
        [MarshalAs(UnmanagedType.LPWStr)] string deviceId, uint newState);
    [PreserveSig] int OnDeviceAdded([MarshalAs(UnmanagedType.LPWStr)] string deviceId);
    [PreserveSig] int OnDeviceRemoved([MarshalAs(UnmanagedType.LPWStr)] string deviceId);
    [PreserveSig] int OnDefaultDeviceChanged(
        EDataFlow flow,
        ERole role,
        [MarshalAs(UnmanagedType.LPWStr)] string? defaultDeviceId);
    [PreserveSig] int OnPropertyValueChanged(
        [MarshalAs(UnmanagedType.LPWStr)] string deviceId,
        PropertyKey key);
}

[ComImport]
[Guid("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAudioSessionManager2
{
    [PreserveSig] int GetAudioSessionControl(ref Guid groupingId, uint streamFlags,
        out IAudioSessionControl? sessionControl);
    [PreserveSig] int GetSimpleAudioVolume(ref Guid groupingId, uint streamFlags,
        out ISimpleAudioVolume? audioVolume);
    [PreserveSig] int GetSessionEnumerator(out IAudioSessionEnumerator? sessionEnumerator);
    [PreserveSig] int RegisterSessionNotification(IAudioSessionNotification sessionNotification);
    [PreserveSig] int UnregisterSessionNotification(IAudioSessionNotification sessionNotification);
    [PreserveSig] int RegisterDuckNotification(
        [MarshalAs(UnmanagedType.LPWStr)] string sessionId, IntPtr duckNotification);
    [PreserveSig] int UnregisterDuckNotification(IntPtr duckNotification);
}

[ComImport]
[Guid("E2F5BB11-0570-40CA-ACDD-3AA01277DEE8")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAudioSessionEnumerator
{
    [PreserveSig] int GetCount(out int sessionCount);
    [PreserveSig] int GetSession(int sessionIndex, out IAudioSessionControl? sessionControl);
}

[ComImport]
[Guid("F4B1A599-7266-4319-A8CA-E70ACB11E8CD")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAudioSessionControl
{
    [PreserveSig] int GetState(out AudioSessionState state);
    [PreserveSig] int GetDisplayName([MarshalAs(UnmanagedType.LPWStr)] out string? displayName);
    [PreserveSig] int SetDisplayName([MarshalAs(UnmanagedType.LPWStr)] string value, ref Guid eventContext);
    [PreserveSig] int GetIconPath([MarshalAs(UnmanagedType.LPWStr)] out string? iconPath);
    [PreserveSig] int SetIconPath([MarshalAs(UnmanagedType.LPWStr)] string value, ref Guid eventContext);
    [PreserveSig] int GetGroupingParam(out Guid groupingId);
    [PreserveSig] int SetGroupingParam(ref Guid groupingId, ref Guid eventContext);
    [PreserveSig] int RegisterAudioSessionNotification(IAudioSessionEvents client);
    [PreserveSig] int UnregisterAudioSessionNotification(IAudioSessionEvents client);
}

[ComImport]
[Guid("BFB7FF88-7239-4FC9-8FA2-07C950BE9C6D")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAudioSessionControl2
{
    [PreserveSig] int GetState(out AudioSessionState state);
    [PreserveSig] int GetDisplayName([MarshalAs(UnmanagedType.LPWStr)] out string? displayName);
    [PreserveSig] int SetDisplayName([MarshalAs(UnmanagedType.LPWStr)] string value, ref Guid eventContext);
    [PreserveSig] int GetIconPath([MarshalAs(UnmanagedType.LPWStr)] out string? iconPath);
    [PreserveSig] int SetIconPath([MarshalAs(UnmanagedType.LPWStr)] string value, ref Guid eventContext);
    [PreserveSig] int GetGroupingParam(out Guid groupingId);
    [PreserveSig] int SetGroupingParam(ref Guid groupingId, ref Guid eventContext);
    [PreserveSig] int RegisterAudioSessionNotification(IAudioSessionEvents client);
    [PreserveSig] int UnregisterAudioSessionNotification(IAudioSessionEvents client);
    [PreserveSig] int GetSessionIdentifier([MarshalAs(UnmanagedType.LPWStr)] out string? sessionId);
    [PreserveSig] int GetSessionInstanceIdentifier([MarshalAs(UnmanagedType.LPWStr)] out string? sessionInstanceId);
    [PreserveSig] int GetProcessId(out uint processId);
    [PreserveSig] int IsSystemSoundsSession();
    [PreserveSig] int SetDuckingPreference([MarshalAs(UnmanagedType.Bool)] bool optOut);
}

[ComImport]
[Guid("87CE5498-68D6-44E5-9215-6DA47EF883D8")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface ISimpleAudioVolume
{
    [PreserveSig] int SetMasterVolume(float level, ref Guid eventContext);
    [PreserveSig] int GetMasterVolume(out float level);
    [PreserveSig] int SetMute([MarshalAs(UnmanagedType.Bool)] bool muted, ref Guid eventContext);
    [PreserveSig] int GetMute([MarshalAs(UnmanagedType.Bool)] out bool muted);
}

[ComImport]
[Guid("5CDF2C82-841E-4546-9722-0CF74078229A")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAudioEndpointVolume
{
    [PreserveSig] int RegisterControlChangeNotify(IAudioEndpointVolumeCallback notify);
    [PreserveSig] int UnregisterControlChangeNotify(IAudioEndpointVolumeCallback notify);
    [PreserveSig] int GetChannelCount(out uint channelCount);
    [PreserveSig] int SetMasterVolumeLevel(float levelDb, ref Guid eventContext);
    [PreserveSig] int SetMasterVolumeLevelScalar(float level, ref Guid eventContext);
    [PreserveSig] int GetMasterVolumeLevel(out float levelDb);
    [PreserveSig] int GetMasterVolumeLevelScalar(out float level);
    [PreserveSig] int SetChannelVolumeLevel(uint channel, float levelDb, ref Guid eventContext);
    [PreserveSig] int SetChannelVolumeLevelScalar(uint channel, float level, ref Guid eventContext);
    [PreserveSig] int GetChannelVolumeLevel(uint channel, out float levelDb);
    [PreserveSig] int GetChannelVolumeLevelScalar(uint channel, out float level);
    [PreserveSig] int SetMute([MarshalAs(UnmanagedType.Bool)] bool muted, ref Guid eventContext);
    [PreserveSig] int GetMute([MarshalAs(UnmanagedType.Bool)] out bool muted);
    [PreserveSig] int GetVolumeStepInfo(out uint step, out uint stepCount);
    [PreserveSig] int VolumeStepUp(ref Guid eventContext);
    [PreserveSig] int VolumeStepDown(ref Guid eventContext);
    [PreserveSig] int QueryHardwareSupport(out uint hardwareSupportMask);
    [PreserveSig] int GetVolumeRange(out float minDb, out float maxDb, out float incrementDb);
}

[ComVisible(true)]
[Guid("657804FA-D6AD-4496-8A60-352752AF4F89")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAudioEndpointVolumeCallback
{
    [PreserveSig] int OnNotify(IntPtr notificationData);
}

[StructLayout(LayoutKind.Sequential)]
internal struct AudioVolumeNotificationData
{
    internal Guid EventContext;
    [MarshalAs(UnmanagedType.Bool)] internal bool IsMuted;
    internal float MasterVolume;
    internal uint ChannelCount;
}

[ComVisible(true)]
[Guid("C3B284D4-6D39-4359-B3CF-B56DDB3BB39C")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAudioSessionNotification
{
    // Borrowed IAudioSessionControl pointer is intentionally not marshalled into an RCW;
    // the callback only invalidates and owner-thread enumeration obtains an owned reference.
    [PreserveSig] int OnSessionCreated(IntPtr newSession);
}

[ComVisible(true)]
[Guid("24918ACC-64B3-37C1-8CA9-74A66E9957A8")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAudioSessionEvents
{
    [PreserveSig] int OnDisplayNameChanged(
        [MarshalAs(UnmanagedType.LPWStr)] string? newDisplayName, ref Guid eventContext);
    [PreserveSig] int OnIconPathChanged(
        [MarshalAs(UnmanagedType.LPWStr)] string? newIconPath, ref Guid eventContext);
    [PreserveSig] int OnSimpleVolumeChanged(
        float newVolume, [MarshalAs(UnmanagedType.Bool)] bool newMute, ref Guid eventContext);
    [PreserveSig] int OnChannelVolumeChanged(
        uint channelCount, IntPtr newChannelVolumeArray, uint changedChannel, ref Guid eventContext);
    [PreserveSig] int OnGroupingParamChanged(ref Guid newGroupingParam, ref Guid eventContext);
    [PreserveSig] int OnStateChanged(AudioSessionState newState);
    [PreserveSig] int OnSessionDisconnected(AudioSessionDisconnectReason disconnectReason);
}
