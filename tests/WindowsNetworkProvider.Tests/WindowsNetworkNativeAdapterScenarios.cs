using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Text;
using GameBarAlternative.PlatformBroker;
using GameBarAlternative.WindowsNetworkProvider;

internal static class WindowsNetworkNativeAdapterScenarios
{
    public static Task NativeLifetimeOwnerIsSingular()
    {
        var adapterFields = typeof(WindowsNetworkNativeAdapter)
            .GetFields(System.Reflection.BindingFlags.Instance |
                       System.Reflection.BindingFlags.NonPublic);
        Assert.Equal(3, adapterFields.Count(field => field.FieldType == typeof(IntPtr)));
        Assert.Equal(1, adapterFields.Count(field => field.FieldType == typeof(NativeWifiNotificationCallback)));
        Assert.Equal(1, adapterFields.Count(field => field.FieldType == typeof(IpInterfaceChangeCallback)));
        Assert.Equal(1, adapterFields.Count(field =>
            field.FieldType == typeof(NetworkConnectivityHintChangeCallback)));
        Assert.Equal(1, adapterFields.Count(field => field.FieldType == typeof(object)));
        foreach (var policy in new[]
        {
            typeof(WindowsNetworkConnectivityPolicy),
            typeof(WindowsNetworkWlanPolicy),
            typeof(WindowsNetworkRadioPolicy),
            typeof(WindowsNetworkNativeCalls),
        })
        {
            var fields = policy.GetFields(System.Reflection.BindingFlags.Instance |
                                          System.Reflection.BindingFlags.NonPublic |
                                          System.Reflection.BindingFlags.Public);
            Assert.False(fields.Any(field => field.FieldType == typeof(IntPtr)));
            Assert.False(fields.Any(field => typeof(Delegate).IsAssignableFrom(field.FieldType)));
            Assert.False(typeof(IDisposable).IsAssignableFrom(policy));
        }
        Assert.True(typeof(IDisposable).IsAssignableFrom(typeof(WindowsNetworkNativeAdapter)));
        return Task.CompletedTask;
    }

    public static Task LifetimeRecoveryAndDisposalAreSingular()
    {
        using var calls = ControlledNetworkNativeCalls.CreateDefault();
        calls.WlanRegistrationResults.Enqueue(1);
        calls.IpRegistrationResults.Enqueue(1);
        calls.ConnectivityRegistrationResults.Enqueue(1);
        var adapter = new WindowsNetworkNativeAdapter(41, calls);
        var published = new List<NativeNetworkStateChangedEventArgs>();
        adapter.StateChanged += (_, value) => published.Add(value);

        Assert.Equal(1, calls.OpenCalls);
        Assert.Equal(1, calls.WlanRegistrationCalls);
        _ = adapter.ReadSnapshot();
        Assert.Equal(1, calls.OpenCalls);
        Assert.Equal(2, calls.WlanRegistrationCalls);
        Assert.Equal(2, calls.IpRegistrationCalls);
        Assert.Equal(2, calls.ConnectivityRegistrationCalls);
        Assert.False(adapter.IsDegraded);

        calls.FireIpChange();
        calls.FireConnectivityChange();
        Assert.Equal(2, published.Count);
        Assert.True(published.All(value => value.Generation == 41));

        adapter.Dispose();
        adapter.Dispose();
        Assert.Equal(1, calls.WlanUnregistrationCalls);
        Assert.Equal(1, calls.CloseCalls);
        Assert.Equal(2, calls.CancelNotificationCalls);
        Assert.Equal(0, calls.OutstandingAllocations);

        calls.FireIpChange();
        calls.FireConnectivityChange();
        calls.FireScanComplete(calls.InterfaceId);
        Assert.Equal(2, published.Count);

        using var replacementCalls = ControlledNetworkNativeCalls.CreateDefault();
        using var replacement = new WindowsNetworkNativeAdapter(42, replacementCalls);
        replacement.StateChanged += (_, value) => published.Add(value);
        replacementCalls.FireIpChange();
        Assert.Equal(3, published.Count);
        Assert.Equal(42L, published[^1].Generation);
        return Task.CompletedTask;
    }

    public static Task FailedOpenRecoversWithoutDuplicateHandle()
    {
        using var calls = ControlledNetworkNativeCalls.CreateDefault();
        calls.OpenResults.Enqueue(1);
        calls.OpenResults.Enqueue(0);
        using var adapter = new WindowsNetworkNativeAdapter(9, calls);
        Assert.Equal(1, calls.OpenCalls);
        Assert.Equal(0, calls.WlanRegistrationCalls);

        _ = adapter.ReadSnapshot();
        _ = adapter.ReadSnapshot();
        Assert.Equal(2, calls.OpenCalls);
        Assert.Equal(1, calls.WlanRegistrationCalls);
        Assert.Equal(0, calls.DuplicateOpenAttempts);
        adapter.Dispose();
        Assert.Equal(1, calls.CloseCalls);
        return Task.CompletedTask;
    }

    public static Task ConnectivityAndNativeBuffersAreBounded()
    {
        using var calls = ControlledNetworkNativeCalls.CreateDefault();
        calls.ConnectivityHint = NetworkConnectivityLevelHint.ConstrainedInternetAccess;
        calls.ManagedInterfaces =
        [
            new(NetworkInterfaceType.Loopback, OperationalStatus.Up, true, 1),
            new(NetworkInterfaceType.Ethernet, OperationalStatus.Up, true, 4),
            new(NetworkInterfaceType.Wireless80211, OperationalStatus.Up, true, 7),
        ];
        calls.BestInterfaceIndex = 7;
        var connectivity = WindowsNetworkConnectivityPolicy.Read(calls);
        Assert.Equal(NetworkConnectivity.Local, connectivity.Connectivity);
        Assert.Equal(NativeNetworkMedium.WiFi, connectivity.Interfaces.DefaultMedium);
        Assert.True(connectivity.Interfaces.HasWireless);
        Assert.True(connectivity.Interfaces.WirelessUp);

        _ = calls.OpenWlan(out _);

        calls.Interfaces = Enumerable.Range(0, 32)
            .Select(index => new WlanInterfaceInfo
            {
                InterfaceGuid = index == 0
                    ? calls.InterfaceId
                    : Guid.Parse($"00000000-0000-0000-0000-{index + 1:000000000000}"),
                Description = $"Interface {index}",
                State = 1,
            })
            .ToArray();
        calls.InterfaceDeclaredCount = 1_000;
        var wlan = new WindowsNetworkWlanPolicy();
        var interfaces = wlan.EnumerateInterfaces(calls, calls.WlanHandle);
        Assert.Equal(32, interfaces.Count);

        calls.RadioDeclaredCount = 65;
        calls.RadioStates = [new() { PhyIndex = 1, SoftwareRadioState = 1, HardwareRadioState = 1 }];
        var radio = WindowsNetworkRadioPolicy.Read(calls, calls.WlanHandle, [interfaces[0]]);
        Assert.Equal(NativeWifiRadioState.Unavailable, radio.State);

        calls.Interfaces =
        [
            new()
            {
                InterfaceGuid = calls.InterfaceId,
                Description = "Wi-Fi",
                State = 1,
            },
        ];
        calls.InterfaceDeclaredCount = null;
        calls.AvailableNetworks =
        [
            ControlledNetworkNativeCalls.AvailableNetwork(
                "",
                Encoding.UTF8.GetBytes("oversized"),
                ssidLength: 33),
        ];
        Assert.Equal(NativeWifiScanStartResult.Started,
            wlan.TryStartScan(calls, calls.WlanHandle));
        var scan = new WlanNotificationData
        {
            NotificationSource = 0x00000008,
            NotificationCode = 7,
            InterfaceGuid = interfaces[0].InterfaceId,
        };
        _ = wlan.ProcessNotification(ref scan);
        var available = wlan.ReadAvailableSnapshot(calls, calls.WlanHandle);
        Assert.Equal(0, available.Networks.Count);
        Assert.Equal(0, calls.OutstandingAllocations);

        var negativeCount = Marshal.AllocHGlobal(sizeof(int));
        try
        {
            Marshal.WriteInt32(negativeCount, -1);
            Assert.Equal(0, WindowsNetworkWlanPolicy.ReadBoundedCount(negativeCount, 32));
            Marshal.WriteInt32(negativeCount, int.MaxValue);
            Assert.Equal(32, WindowsNetworkWlanPolicy.ReadBoundedCount(negativeCount, 32));
        }
        finally
        {
            Marshal.FreeHGlobal(negativeCount);
        }
        _ = calls.CloseWlan(calls.WlanHandle);
        return Task.CompletedTask;
    }

    public static Task ScanAndConnectCallbacksAreGenerationBound()
    {
        using var calls = ControlledNetworkNativeCalls.CreateDefault();
        using var adapter = new WindowsNetworkNativeAdapter(73, calls);
        var published = new List<NativeNetworkStateChangedEventArgs>();
        adapter.StateChanged += (_, value) => published.Add(value);

        var snapshot = adapter.ReadSnapshot();
        var saved = Assert.Single(snapshot.SavedProfiles);
        Assert.True(adapter.TryConnectSavedProfile(saved.NativeProfileKey));
        Assert.Equal("Saved", calls.ConnectRequests[^1].Request.Profile);
        calls.FireConnectionComplete(calls.InterfaceId, "Saved", []);
        var savedOutcome = published[^1].ConnectionOutcome;
        Assert.Equal(saved.NativeProfileKey, savedOutcome?.NativeProfileKey);
        Assert.Equal(NativeNetworkConnectionResult.Succeeded, savedOutcome?.Result);
        Assert.Equal(73L, published[^1].Generation);

        Assert.Equal(NativeWifiScanStartResult.Started, adapter.TryStartWifiScan());
        calls.FireScanComplete(calls.InterfaceId);
        Assert.Equal(NativeWifiScanOutcome.Completed, published[^1].WifiScanOutcome);
        calls.AvailableNetworks =
        [
            ControlledNetworkNativeCalls.AvailableNetwork(
                "",
                Encoding.UTF8.GetBytes("Cafe"),
                ssidLength: 4),
        ];
        var network = Assert.Single(adapter.ReadAvailableWifiSnapshot().Networks);
        Assert.Equal(
            NativeWifiConnectStartResult.Started,
            adapter.TryConnectAvailableWifiNetwork(network.NativeNetworkKey));
        Assert.SequenceEqual(Encoding.UTF8.GetBytes("Cafe"), calls.ConnectRequests[^1].Request.Ssid!);
        calls.FireConnectionComplete(calls.InterfaceId, "", Encoding.UTF8.GetBytes("Cafe"));
        Assert.Equal(network.NativeNetworkKey, published[^1].ConnectionOutcome?.NativeProfileKey);
        Assert.Equal(73L, published[^1].Generation);
        Assert.Equal(0, calls.OutstandingAllocations);
        return Task.CompletedTask;
    }

    public static Task RadioRollbackUsesOneInjectedTransaction()
    {
        using var calls = ControlledNetworkNativeCalls.CreateDefault();
        calls.RadioStates =
        [
            new() { PhyIndex = 1, SoftwareRadioState = 1, HardwareRadioState = 1 },
            new() { PhyIndex = 2, SoftwareRadioState = 1, HardwareRadioState = 1 },
        ];
        calls.SetRadioResults.Enqueue(0);
        calls.SetRadioResults.Enqueue(5);
        calls.SetRadioResults.Enqueue(0);
        using var adapter = new WindowsNetworkNativeAdapter(5, calls);

        Assert.Equal(NativeWifiRadioSetResult.PolicyDenied, adapter.TrySetWifiRadio(false));
        Assert.Equal(3, calls.SetRadioRequests.Count);
        Assert.SequenceEqual([2, 2, 1],
            calls.SetRadioRequests.Select(request => request.State.SoftwareRadioState).ToArray());
        Assert.SequenceEqual([1u, 2u, 1u],
            calls.SetRadioRequests.Select(request => request.State.PhyIndex).ToArray());
        Assert.Equal(0, calls.OutstandingAllocations);
        return Task.CompletedTask;
    }
}

internal sealed class ControlledNetworkNativeCalls : IWindowsNetworkNativeCalls, IDisposable
{
    private const uint ErrorSuccess = 0;
    private readonly HashSet<IntPtr> _allocations = [];
    private long _nextHandle = 100;
    private bool _wlanOpen;

    public Guid InterfaceId { get; } = Guid.Parse("11111111-2222-3333-4444-555555555555");
    public IntPtr WlanHandle { get; private set; }
    public Queue<uint> OpenResults { get; } = [];
    public Queue<uint> WlanRegistrationResults { get; } = [];
    public Queue<uint> IpRegistrationResults { get; } = [];
    public Queue<uint> ConnectivityRegistrationResults { get; } = [];
    public Queue<uint> SetRadioResults { get; } = [];
    public int OpenCalls { get; private set; }
    public int CloseCalls { get; private set; }
    public int DuplicateOpenAttempts { get; private set; }
    public int WlanRegistrationCalls { get; private set; }
    public int WlanUnregistrationCalls { get; private set; }
    public int IpRegistrationCalls { get; private set; }
    public int ConnectivityRegistrationCalls { get; private set; }
    public int CancelNotificationCalls { get; private set; }
    public int FreeCalls { get; private set; }
    public int OutstandingAllocations => _allocations.Count;
    public int? InterfaceDeclaredCount { get; set; }
    public int? RadioDeclaredCount { get; set; }
    public int BestInterfaceIndex { get; set; } = 7;
    public bool ManagedNetworkAvailable { get; set; } = true;
    public NetworkConnectivityLevelHint ConnectivityHint { get; set; } =
        NetworkConnectivityLevelHint.InternetAccess;
    public IReadOnlyList<ManagedNetworkInterfaceData> ManagedInterfaces { get; set; } = [];
    public IReadOnlyList<WlanInterfaceInfo> Interfaces { get; set; } = [];
    public IReadOnlyList<WlanProfileInfo> Profiles { get; set; } = [];
    public IReadOnlyList<WlanAvailableNetwork> AvailableNetworks { get; set; } = [];
    public IReadOnlyList<WlanPhyRadioState> RadioStates { get; set; } = [];
    public List<(Guid InterfaceId, WlanConnectRequest Request)> ConnectRequests { get; } = [];
    public List<(Guid InterfaceId, WlanPhyRadioState State)> SetRadioRequests { get; } = [];
    public NativeWifiNotificationCallback? WlanCallback { get; private set; }
    public IpInterfaceChangeCallback? IpCallback { get; private set; }
    public NetworkConnectivityHintChangeCallback? ConnectivityCallback { get; private set; }

    public static ControlledNetworkNativeCalls CreateDefault()
    {
        var calls = new ControlledNetworkNativeCalls();
        calls.Interfaces =
        [
            new()
            {
                InterfaceGuid = calls.InterfaceId,
                Description = "Wi-Fi",
                State = 1,
            },
        ];
        calls.Profiles = [new() { ProfileName = "Saved", Flags = 0 }];
        calls.RadioStates =
        [
            new() { PhyIndex = 1, SoftwareRadioState = 1, HardwareRadioState = 1 },
        ];
        calls.ManagedInterfaces =
        [
            new(NetworkInterfaceType.Wireless80211, OperationalStatus.Up, true, 7),
        ];
        return calls;
    }

    public static WlanAvailableNetwork AvailableNetwork(
        string profileName,
        byte[] ssid,
        uint ssidLength) => new()
    {
        ProfileName = profileName,
        Dot11Ssid = new()
        {
            SsidLength = ssidLength,
            Ssid = ssid.Take(32).Concat(Enumerable.Repeat((byte)0, 32)).Take(32).ToArray(),
        },
        BssType = 3,
        NetworkConnectable = 1,
        SignalQuality = 80,
        SecurityEnabled = 0,
        PhyTypes = new int[8],
    };

    public uint OpenWlan(out IntPtr handle)
    {
        OpenCalls++;
        var result = Next(OpenResults);
        if (result != ErrorSuccess)
        {
            handle = IntPtr.Zero;
            return result;
        }
        if (_wlanOpen) DuplicateOpenAttempts++;
        _wlanOpen = true;
        WlanHandle = handle = new IntPtr(Interlocked.Increment(ref _nextHandle));
        return ErrorSuccess;
    }

    public uint CloseWlan(IntPtr handle)
    {
        Assert.True(_wlanOpen);
        Assert.Equal(WlanHandle, handle);
        _wlanOpen = false;
        CloseCalls++;
        return ErrorSuccess;
    }

    public uint RegisterWlanNotification(
        IntPtr handle,
        uint source,
        NativeWifiNotificationCallback? callback)
    {
        Assert.Equal(WlanHandle, handle);
        if (source == 0)
        {
            WlanUnregistrationCalls++;
            return ErrorSuccess;
        }
        WlanRegistrationCalls++;
        var result = Next(WlanRegistrationResults);
        if (result == ErrorSuccess) WlanCallback = callback;
        return result;
    }

    public uint EnumerateWlanInterfaces(IntPtr handle, out IntPtr list)
    {
        Assert.Equal(WlanHandle, handle);
        list = AllocateList(Interfaces, InterfaceDeclaredCount ?? Interfaces.Count);
        return ErrorSuccess;
    }

    public uint EnumerateWlanProfiles(IntPtr handle, Guid interfaceId, out IntPtr list)
    {
        Assert.Equal(WlanHandle, handle);
        Assert.True(Interfaces.Any(value => value.InterfaceGuid == interfaceId));
        list = AllocateList(Profiles, Profiles.Count);
        return ErrorSuccess;
    }

    public uint StartWlanScan(IntPtr handle, Guid interfaceId)
    {
        Assert.Equal(WlanHandle, handle);
        Assert.Equal(InterfaceId, interfaceId);
        return ErrorSuccess;
    }

    public uint EnumerateAvailableNetworks(IntPtr handle, Guid interfaceId, out IntPtr list)
    {
        Assert.Equal(WlanHandle, handle);
        Assert.Equal(InterfaceId, interfaceId);
        list = AllocateList(AvailableNetworks, AvailableNetworks.Count);
        return ErrorSuccess;
    }

    public uint QueryRadio(IntPtr handle, Guid interfaceId, out uint size, out IntPtr data)
    {
        Assert.Equal(WlanHandle, handle);
        var declared = RadioDeclaredCount ?? RadioStates.Count;
        size = checked((uint)(sizeof(int) + RadioStates.Count * 12));
        data = Marshal.AllocHGlobal(checked((int)size));
        _allocations.Add(data);
        Marshal.WriteInt32(data, declared);
        for (var index = 0; index < RadioStates.Count; index++)
        {
            var offset = sizeof(int) + index * 12;
            Marshal.WriteInt32(data, offset, checked((int)RadioStates[index].PhyIndex));
            Marshal.WriteInt32(data, offset + 4, RadioStates[index].SoftwareRadioState);
            Marshal.WriteInt32(data, offset + 8, RadioStates[index].HardwareRadioState);
        }
        return ErrorSuccess;
    }

    public uint SetRadio(IntPtr handle, Guid interfaceId, WlanPhyRadioState state)
    {
        Assert.Equal(WlanHandle, handle);
        SetRadioRequests.Add((interfaceId, state));
        return Next(SetRadioResults);
    }

    public uint ConnectWlan(IntPtr handle, Guid interfaceId, WlanConnectRequest request)
    {
        Assert.Equal(WlanHandle, handle);
        ConnectRequests.Add((interfaceId, request));
        return ErrorSuccess;
    }

    public void FreeWlanMemory(IntPtr memory)
    {
        Assert.True(_allocations.Remove(memory));
        Marshal.FreeHGlobal(memory);
        FreeCalls++;
    }

    public uint RegisterIpInterfaceChange(IpInterfaceChangeCallback callback, out IntPtr handle)
    {
        IpRegistrationCalls++;
        var result = Next(IpRegistrationResults);
        handle = result == ErrorSuccess ? NewHandle() : IntPtr.Zero;
        if (result == ErrorSuccess) IpCallback = callback;
        return result;
    }

    public uint RegisterConnectivityHintChange(
        NetworkConnectivityHintChangeCallback callback,
        out IntPtr handle)
    {
        ConnectivityRegistrationCalls++;
        var result = Next(ConnectivityRegistrationResults);
        handle = result == ErrorSuccess ? NewHandle() : IntPtr.Zero;
        if (result == ErrorSuccess) ConnectivityCallback = callback;
        return result;
    }

    public uint CancelChangeNotification(IntPtr handle)
    {
        Assert.True(handle != IntPtr.Zero);
        CancelNotificationCalls++;
        return ErrorSuccess;
    }

    public uint ReadConnectivityHint(out NetworkConnectivityHint hint)
    {
        hint = new() { ConnectivityLevel = ConnectivityHint };
        return ErrorSuccess;
    }

    public uint ReadBestInterface(uint destinationAddress, out uint interfaceIndex)
    {
        interfaceIndex = checked((uint)BestInterfaceIndex);
        return ErrorSuccess;
    }

    public bool IsNetworkAvailable() => ManagedNetworkAvailable;

    public IReadOnlyList<ManagedNetworkInterfaceData> ReadManagedInterfaces() =>
        ManagedInterfaces;

    public void FireIpChange() => IpCallback?.Invoke(IntPtr.Zero, IntPtr.Zero, 0);

    public void FireConnectivityChange() =>
        ConnectivityCallback?.Invoke(IntPtr.Zero, new() { ConnectivityLevel = ConnectivityHint });

    public void FireScanComplete(Guid interfaceId)
    {
        var data = new WlanNotificationData
        {
            NotificationSource = 0x00000008,
            NotificationCode = 7,
            InterfaceGuid = interfaceId,
        };
        WlanCallback?.Invoke(ref data, IntPtr.Zero);
    }

    public void FireConnectionComplete(Guid interfaceId, string profileName, byte[] ssid)
    {
        var ssidSize = Marshal.SizeOf<Dot11Ssid>();
        var pointer = Marshal.AllocHGlobal(516 + ssidSize);
        try
        {
            Marshal.Copy(new byte[516 + ssidSize], 0, pointer, 516 + ssidSize);
            var profileBytes = Encoding.Unicode.GetBytes(profileName + '\0');
            Marshal.Copy(profileBytes, 0, pointer + 4, Math.Min(profileBytes.Length, 512));
            Marshal.StructureToPtr(new Dot11Ssid
            {
                SsidLength = checked((uint)Math.Min(ssid.Length, 32)),
                Ssid = ssid.Take(32).Concat(Enumerable.Repeat((byte)0, 32)).Take(32).ToArray(),
            }, pointer + 516, false);
            var data = new WlanNotificationData
            {
                NotificationSource = 0x00000008,
                NotificationCode = 10,
                InterfaceGuid = interfaceId,
                DataSize = checked((uint)(516 + ssidSize)),
                DataPointer = pointer,
            };
            WlanCallback?.Invoke(ref data, IntPtr.Zero);
        }
        finally
        {
            Marshal.FreeHGlobal(pointer);
        }
    }

    public void Dispose()
    {
        foreach (var allocation in _allocations.ToArray())
        {
            Marshal.FreeHGlobal(allocation);
            _allocations.Remove(allocation);
        }
    }

    private IntPtr AllocateList<T>(IReadOnlyList<T> values, int declaredCount) where T : struct
    {
        var size = Marshal.SizeOf<T>();
        var pointer = Marshal.AllocHGlobal(checked(8 + values.Count * size));
        _allocations.Add(pointer);
        Marshal.WriteInt32(pointer, declaredCount);
        Marshal.WriteInt32(pointer, 4, 0);
        for (var index = 0; index < values.Count; index++)
            Marshal.StructureToPtr(values[index], pointer + 8 + index * size, false);
        return pointer;
    }

    private IntPtr NewHandle() => new(Interlocked.Increment(ref _nextHandle));

    private static uint Next(Queue<uint> values) =>
        values.Count == 0 ? ErrorSuccess : values.Dequeue();
}
