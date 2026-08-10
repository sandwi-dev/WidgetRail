using System.Runtime.InteropServices;
using GameBarAlternative.PlatformBroker;

namespace GameBarAlternative.WindowsNetworkProvider;

internal sealed record WirelessInterface(Guid InterfaceId, int State);
internal sealed record PhyRadioState(uint PhyIndex, int SoftwareState, int HardwareState);

/// <summary>Stateless query/projection and compensating transaction policy for WLAN radios.</summary>
internal static class WindowsNetworkRadioPolicy
{
    private const uint ErrorSuccess = 0;
    private const uint ErrorAccessDenied = 5;
    private const int Dot11RadioStateUnknown = 0;
    private const int Dot11RadioStateOn = 1;
    private const int Dot11RadioStateOff = 2;

    public static NetworkWirelessAvailability ResolveWirelessAvailability(
        IWindowsNetworkNativeCalls calls,
        IntPtr wlanHandle,
        ManagedInterfaceState managed,
        IReadOnlyList<WirelessInterface> nativeInterfaces,
        bool wlanServiceAvailable,
        bool wlanNotificationsRegistered)
    {
        if (!wlanServiceAvailable) return managed.HasWireless
            ? NetworkWirelessAvailability.ServiceUnavailable
            : NetworkWirelessAvailability.NoAdapter;
        if (!wlanNotificationsRegistered)
            return NetworkWirelessAvailability.ServiceUnavailable;
        if (nativeInterfaces.Count == 0) return managed.HasWireless
            ? NetworkWirelessAvailability.ServiceUnavailable
            : NetworkWirelessAvailability.NoAdapter;
        return ResolveRadioAvailability(nativeInterfaces
            .Select(item => QueryRadioEnabled(calls, wlanHandle, item.InterfaceId))
            .ToArray());
    }

    internal static NetworkWirelessAvailability ResolveRadioAvailability(
        IReadOnlyList<bool?> radioStates)
    {
        if (radioStates.Any(enabled => enabled == true))
            return NetworkWirelessAvailability.Available;
        return radioStates.All(enabled => enabled == false)
            ? NetworkWirelessAvailability.RadioOff
            : NetworkWirelessAvailability.Available;
    }

    public static NativeWifiRadioSnapshot Read(
        IWindowsNetworkNativeCalls calls,
        IntPtr wlanHandle,
        IReadOnlyList<WirelessInterface> interfaces)
    {
        if (wlanHandle == IntPtr.Zero)
            return new(NativeWifiRadioState.Unavailable, false);
        if (interfaces.Count == 0)
            return new(NativeWifiRadioState.NoAdapter, false);
        var phys = interfaces
            .SelectMany(item => QueryPhyRadioStates(calls, wlanHandle, item.InterfaceId))
            .ToArray();
        if (phys.Length == 0) return new(NativeWifiRadioState.Unavailable, false);
        if (phys.Any(item => item.HardwareState == Dot11RadioStateOn &&
                             item.SoftwareState == Dot11RadioStateOn))
            return new(NativeWifiRadioState.On, true);
        if (phys.Any(item => item.HardwareState == Dot11RadioStateOn))
            return new(NativeWifiRadioState.Off, true);
        if (phys.All(item => item.HardwareState == Dot11RadioStateOff))
            return new(NativeWifiRadioState.HardwareDisabled, false);
        return new(NativeWifiRadioState.Unavailable, false);
    }

    public static NativeWifiRadioSetResult Set(
        IWindowsNetworkNativeCalls calls,
        IntPtr wlanHandle,
        IReadOnlyList<WirelessInterface> interfaces,
        bool enabled)
    {
        if (wlanHandle == IntPtr.Zero) return NativeWifiRadioSetResult.Unavailable;
        if (interfaces.Count == 0) return NativeWifiRadioSetResult.NoAdapter;
        var targets = interfaces.SelectMany(wireless =>
                QueryPhyRadioStates(calls, wlanHandle, wireless.InterfaceId)
                    .Select(phy => (wireless.InterfaceId, Phy: phy)))
            .ToArray();
        return ApplySoftwareRadioTransaction(
            targets,
            enabled,
            target => target.Phy.SoftwareState,
            target => target.Phy.HardwareState,
            (target, desired) => SetSoftwareRadioState(
                calls,
                wlanHandle,
                target.InterfaceId,
                target.Phy.PhyIndex,
                desired));
    }

    internal static NativeWifiRadioSetResult ApplySoftwareRadioTransaction<T>(
        IReadOnlyList<T> targets,
        bool enabled,
        Func<T, int> softwareState,
        Func<T, int> hardwareState,
        Func<T, int, NativeWifiRadioSetResult> apply)
    {
        ArgumentNullException.ThrowIfNull(targets);
        ArgumentNullException.ThrowIfNull(softwareState);
        ArgumentNullException.ThrowIfNull(hardwareState);
        ArgumentNullException.ThrowIfNull(apply);
        if (targets.Count == 0) return NativeWifiRadioSetResult.Unavailable;

        var originals = new (T Target, int Software, int Hardware)[targets.Count];
        for (var index = 0; index < targets.Count; index++)
        {
            var software = softwareState(targets[index]);
            var hardware = hardwareState(targets[index]);
            if (software is not Dot11RadioStateOn and not Dot11RadioStateOff ||
                hardware is not Dot11RadioStateOn and not Dot11RadioStateOff)
                return NativeWifiRadioSetResult.Unavailable;
            originals[index] = (targets[index], software, hardware);
        }

        if (enabled && originals.All(target => target.Hardware == Dot11RadioStateOff))
            return NativeWifiRadioSetResult.HardwareDisabled;

        var desired = enabled ? Dot11RadioStateOn : Dot11RadioStateOff;
        var changed = new List<(T Target, int Software)>();
        foreach (var target in originals)
        {
            if (target.Software == desired) continue;
            var result = apply(target.Target, desired);
            if (result == NativeWifiRadioSetResult.Succeeded)
            {
                changed.Add((target.Target, target.Software));
                continue;
            }
            var restored = true;
            for (var index = changed.Count - 1; index >= 0; index--)
                restored &= apply(changed[index].Target, changed[index].Software) ==
                    NativeWifiRadioSetResult.Succeeded;
            return restored ? result : NativeWifiRadioSetResult.PartialFailure;
        }
        return NativeWifiRadioSetResult.Succeeded;
    }

    internal static IReadOnlyList<PhyRadioState> ParsePhyRadioStates(IntPtr data, uint dataSize)
    {
        if (data == IntPtr.Zero || dataSize < sizeof(uint)) return [];
        var declared = Marshal.ReadInt32(data);
        if (declared <= 0 || declared > 64) return [];
        var required = checked(4u + (uint)declared * 12u);
        if (dataSize < required) return [];
        var states = new List<PhyRadioState>(declared);
        for (var index = 0; index < declared; index++)
        {
            var offset = sizeof(uint) + index * 12;
            states.Add(new(
                checked((uint)Marshal.ReadInt32(data, offset)),
                Marshal.ReadInt32(data, offset + 4),
                Marshal.ReadInt32(data, offset + 8)));
        }
        return states;
    }

    private static bool? QueryRadioEnabled(
        IWindowsNetworkNativeCalls calls,
        IntPtr handle,
        Guid interfaceId)
    {
        var result = calls.QueryRadio(handle, interfaceId, out var size, out var data);
        if (result != ErrorSuccess || data == IntPtr.Zero) return null;
        try
        {
            var states = ParsePhyRadioStates(data, size);
            if (states.Count == 0) return null;
            if (states.Any(state => state.SoftwareState == Dot11RadioStateOn &&
                                    state.HardwareState == Dot11RadioStateOn)) return true;
            return states.All(state => state.SoftwareState == Dot11RadioStateOff ||
                                       state.HardwareState == Dot11RadioStateOff)
                ? false
                : null;
        }
        finally
        {
            calls.FreeWlanMemory(data);
        }
    }

    private static IReadOnlyList<PhyRadioState> QueryPhyRadioStates(
        IWindowsNetworkNativeCalls calls,
        IntPtr handle,
        Guid interfaceId)
    {
        var result = calls.QueryRadio(handle, interfaceId, out var size, out var data);
        if (result != ErrorSuccess || data == IntPtr.Zero) return [];
        try { return ParsePhyRadioStates(data, size); }
        finally { calls.FreeWlanMemory(data); }
    }

    private static NativeWifiRadioSetResult SetSoftwareRadioState(
        IWindowsNetworkNativeCalls calls,
        IntPtr handle,
        Guid interfaceId,
        uint phyIndex,
        int softwareState)
    {
        var result = calls.SetRadio(handle, interfaceId, new WlanPhyRadioState
        {
            PhyIndex = phyIndex,
            SoftwareRadioState = softwareState,
            HardwareRadioState = Dot11RadioStateUnknown,
        });
        if (result == ErrorAccessDenied) return NativeWifiRadioSetResult.PolicyDenied;
        return result == ErrorSuccess
            ? NativeWifiRadioSetResult.Succeeded
            : NativeWifiRadioSetResult.Unavailable;
    }
}
