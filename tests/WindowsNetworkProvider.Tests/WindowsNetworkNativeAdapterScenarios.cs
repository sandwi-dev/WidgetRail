using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Text;
using WidgetRail.PlatformBroker;
using WidgetRail.WindowsNetworkProvider;

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

    public static async Task RecoveryAndDisposalAreLinearized()
    {
        using var calls = ControlledNetworkNativeCalls.CreateDefault();
        calls.IpRegistrationResults.Enqueue(1);
        calls.ConnectivityRegistrationResults.Enqueue(1);
        using var registrationEntered = new ManualResetEventSlim();
        using var continueRegistration = new ManualResetEventSlim();
        using var disposalAttempted = new ManualResetEventSlim();
        var adapter = new WindowsNetworkNativeAdapter(
            51,
            calls,
            new(BeforeDisposalGate: disposalAttempted.Set));
        calls.IpRegistrationEntered = registrationEntered;
        calls.ContinueIpRegistration = continueRegistration;

        var read = Task.Run(adapter.ReadSnapshot);
        Assert.True(registrationEntered.Wait(TimeSpan.FromSeconds(5)));
        var dispose = Task.Run(adapter.Dispose);
        Assert.True(disposalAttempted.Wait(TimeSpan.FromSeconds(5)));
        Assert.False(dispose.IsCompleted);

        continueRegistration.Set();
        _ = await read.ConfigureAwait(false);
        await dispose.ConfigureAwait(false);

        Assert.Equal(2, calls.IpRegistrationCalls);
        Assert.Equal(2, calls.ConnectivityRegistrationCalls);
        Assert.Equal(2, calls.CancelNotificationCalls);
        Assert.Equal(0, calls.ActiveChangeNotificationCount);
        Assert.Equal(1, calls.WlanUnregistrationCalls);
        Assert.Equal(1, calls.CloseCalls);
        adapter.Dispose();
        Assert.Equal(2, calls.CancelNotificationCalls);
        Assert.Equal(1, calls.WlanUnregistrationCalls);
        Assert.Equal(1, calls.CloseCalls);
    }

    public static async Task DisposalDrainsAdmittedPublication()
    {
        using var calls = ControlledNetworkNativeCalls.CreateDefault();
        using var publicationAdmitted = new ManualResetEventSlim();
        using var continuePublication = new ManualResetEventSlim();
        using var disposalLinearized = new ManualResetEventSlim();
        using var disposalEntrances = new CountdownEvent(2);
        var adapter = new WindowsNetworkNativeAdapter(
            52,
            calls,
            new(
                BeforeDisposalGate: () => disposalEntrances.Signal(),
                DisposalLinearized: disposalLinearized.Set,
                BeforeEventPublication: () =>
                {
                    publicationAdmitted.Set();
                    Assert.True(continuePublication.Wait(TimeSpan.FromSeconds(5)));
                }));
        var publications = 0;
        adapter.StateChanged += (_, _) => Interlocked.Increment(ref publications);

        var callback = Task.Run(calls.FireIpChange);
        Assert.True(publicationAdmitted.Wait(TimeSpan.FromSeconds(5)));
        var firstDispose = Task.Run(adapter.Dispose);
        Assert.True(disposalLinearized.Wait(TimeSpan.FromSeconds(5)));
        var secondDispose = Task.Run(adapter.Dispose);
        Assert.True(disposalEntrances.Wait(TimeSpan.FromSeconds(5)));
        Assert.False(firstDispose.IsCompleted);
        Assert.False(secondDispose.IsCompleted);
        Assert.Equal(0, Volatile.Read(ref publications));

        continuePublication.Set();
        await callback.ConfigureAwait(false);
        await Task.WhenAll(firstDispose, secondDispose).ConfigureAwait(false);
        Assert.Equal(0, Volatile.Read(ref publications));
        calls.FireIpChange();
        calls.FireConnectivityChange();
        calls.FireScanComplete(calls.InterfaceId);
        Assert.Equal(0, Volatile.Read(ref publications));
        Assert.Equal(2, calls.CancelNotificationCalls);
        Assert.Equal(0, calls.ActiveChangeNotificationCount);
        Assert.Equal(1, calls.WlanUnregistrationCalls);
        Assert.Equal(1, calls.CloseCalls);
    }

    public static Task ReentrantPublicationDisposalIsTerminal()
    {
        using var calls = ControlledNetworkNativeCalls.CreateDefault();
        var adapter = new WindowsNetworkNativeAdapter(53, calls);
        var publications = 0;
        adapter.StateChanged += (_, _) =>
        {
            publications++;
            adapter.Dispose();
        };

        calls.FireIpChange();
        Assert.Equal(1, publications);
        calls.FireIpChange();
        Assert.Equal(1, publications);
        Assert.Equal(2, calls.CancelNotificationCalls);
        Assert.Equal(0, calls.ActiveChangeNotificationCount);
        Assert.Equal(1, calls.WlanUnregistrationCalls);
        Assert.Equal(1, calls.CloseCalls);
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
        _ = wlan.ProcessNotification(calls, calls.WlanHandle, ref scan);
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

    public static Task ConnectionDetailsArePrivacyBounded()
    {
        using var calls = ControlledNetworkNativeCalls.CreateDefault();
        calls.ConnectivityHint = NetworkConnectivityLevelHint.InternetAccess;
        calls.BestInterfaceIndex = 7;
        calls.ConnectionInterfaces =
        [
            new(NetworkInterfaceType.Ethernet, OperationalStatus.Up, 4,
                ["192.0.2.9"], ["192.0.2.1"], ["1.1.1.1"]),
            new(NetworkInterfaceType.Wireless80211, OperationalStatus.Up, 7,
                ["2001:db8::7"], ["2001:db8::1"], ["2606:4700:4700::1111"]),
        ];
        var selected = WindowsNetworkConnectionDetailsPolicy.Read(calls);
        Assert.Equal(NetworkConnectionDetailsState.Available, selected.State);
        Assert.Equal(NetworkConnectionDetailsConnectivity.Internet, selected.Connectivity);
        Assert.Equal(NativeNetworkMedium.WiFi, selected.Transport);
        Assert.SequenceEqual(new[] { "2001:db8::7" }, selected.IpAddresses);

        calls.BestInterfaceResult = 1;
        var ambiguous = WindowsNetworkConnectionDetailsPolicy.Read(calls);
        Assert.Equal(NetworkConnectionDetailsState.Ambiguous, ambiguous.State);
        Assert.Equal(0, ambiguous.IpAddresses.Count);

        calls.ConnectivityHint = NetworkConnectivityLevelHint.None;
        var offline = WindowsNetworkConnectionDetailsPolicy.Read(calls);
        Assert.Equal(NetworkConnectionDetailsState.Offline, offline.State);
        Assert.Equal(NetworkConnectionDetailsConnectivity.None, offline.Connectivity);
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

    public static Task ProtectedPasswordConnectsAndKeepsItsTarget()
    {
        foreach (var refreshProfile in new[] { false, true })
        {
            using var calls = ControlledNetworkNativeCalls.CreateDefault();
            using var adapter = new WindowsNetworkNativeAdapter(91, calls);
            var ssid = Encoding.UTF8.GetBytes("New network");
            calls.AvailableNetworks = [ControlledNetworkNativeCalls.AvailableNetwork("", ssid, (uint)ssid.Length,
                securityEnabled: true, authentication: 7, cipher: 4)];
            adapter.TryStartWifiScan();
            calls.FireScanComplete(calls.InterfaceId);
            var initial = Assert.Single(adapter.ReadAvailableWifiSnapshot().Networks);
            var secret = Secret(12, 2);
            Assert.Equal(NativeProtectedWifiConnectStartResult.Started,
                adapter.TryConnectProtectedWifiNetwork(initial.NativeNetworkKey, secret));
            Array.Clear(secret);
            Assert.Equal(1, calls.ConnectRequests.Count);
            var createdProfile = calls.SetProfileRequests.Single().ProfileName;
            Assert.Equal(createdProfile, calls.ConnectRequests.Single().Request.Profile);
            if (refreshProfile)
            {
                var saved = ControlledNetworkNativeCalls.AvailableNetwork(createdProfile, ssid, (uint)ssid.Length,
                    securityEnabled: true, authentication: 7, cipher: 4);
                saved.Flags = 2; // Profile was saved; connection is still completing.
                calls.AvailableNetworks = [saved];
                var changed = new WlanNotificationData { NotificationSource = 8, NotificationCode = 15, InterfaceGuid = calls.InterfaceId };
                calls.WlanCallback!(ref changed, IntPtr.Zero);
                Assert.Equal(initial.NativeNetworkKey, Assert.Single(adapter.ReadAvailableWifiSnapshot().Networks).NativeNetworkKey);
            }
            calls.FireConnectionComplete(calls.InterfaceId, createdProfile, ssid);
            var connected = Assert.Single(adapter.ReadAvailableWifiSnapshot().Networks);
            Assert.Equal(initial.NativeNetworkKey, connected.NativeNetworkKey);
            if (!connected.IsConnected || !connected.HasSavedProfile || connected.CredentialRequired)
                throw new InvalidOperationException("A completed password connection was left as merely saved or still requiring credentials.");
            Assert.Equal(1, calls.ConnectRequests.Count);
            adapter.TryStartWifiScan();
            calls.FireScanComplete(calls.InterfaceId);
            if (Assert.Single(adapter.ReadAvailableWifiSnapshot().Networks).NativeNetworkKey == initial.NativeNetworkKey)
                throw new InvalidOperationException("A new scan reused a retired target.");
        }
        return Task.CompletedTask;
    }

    public static Task ProtectedProfileRollbackIsExact()
    {
        using var calls = ControlledNetworkNativeCalls.CreateDefault();
        using var adapter = new WindowsNetworkNativeAdapter(91, calls);
        Assert.Equal(NativeWifiScanStartResult.Started, adapter.TryStartWifiScan());
        calls.FireScanComplete(calls.InterfaceId);
        calls.AvailableNetworks =
        [
            ControlledNetworkNativeCalls.AvailableNetwork(
                "", Encoding.UTF8.GetBytes("Home"), 4,
                securityEnabled: true, authentication: 7, cipher: 4),
        ];
        var network = Assert.Single(adapter.ReadAvailableWifiSnapshot().Networks);
        calls.SetProfileResults.Enqueue(183);
        var conflictingSecret = Secret(12, 3);
        Assert.Equal(NativeProtectedWifiConnectStartResult.ProfileAlreadyExists,
            adapter.TryConnectProtectedWifiNetwork(network.NativeNetworkKey, conflictingSecret));
        Array.Clear(conflictingSecret);
        Assert.Equal(0, calls.ConnectRequests.Count);
        Assert.Equal(0, calls.DeleteProfileRequests.Count);
        Assert.Equal(0, calls.SetProfileCustomDataRequests.Count);

        var secret = Secret(14, 5);
        var expectedSentinel = SecretSentinel(secret);
        Assert.Equal(NativeProtectedWifiConnectStartResult.Started,
            adapter.TryConnectProtectedWifiNetwork(network.NativeNetworkKey, secret));
        Array.Clear(secret);
        Assert.Equal(2, calls.SetProfileRequests.Count);
        var profile = calls.SetProfileRequests[^1];
        Assert.Equal(calls.InterfaceId, profile.InterfaceId);
        Assert.Equal("WPA2PSK", profile.Authentication);
        Assert.Equal(expectedSentinel, profile.SecretSentinel);
        Assert.True(profile.ProfileName.StartsWith("WidgetRail-", StringComparison.Ordinal));
        Assert.Equal(profile.ProfileName, calls.ConnectRequests[^1].Request.Profile);
        var ownership = Assert.Single(calls.SetProfileCustomDataRequests);
        Assert.Equal(profile.ProfileName, ownership.ProfileName);
        Assert.Equal(32, ownership.Data.Length);
        calls.FireConnectionAttemptFail(
            calls.InterfaceId, profile.ProfileName, Encoding.UTF8.GetBytes("Home"));
        Assert.Equal(
            (calls.InterfaceId, profile.ProfileName),
            Assert.Single(calls.DeleteProfileRequests));

        Assert.Equal(NativeWifiScanStartResult.Started, adapter.TryStartWifiScan());
        calls.FireScanComplete(calls.InterfaceId);
        calls.AvailableNetworks =
        [
            ControlledNetworkNativeCalls.AvailableNetwork(
                "", Encoding.UTF8.GetBytes("Office"), 6,
                securityEnabled: true, authentication: 9, cipher: 4),
        ];
        var successful = Assert.Single(adapter.ReadAvailableWifiSnapshot().Networks);
        var successfulSecret = Secret(18, 9);
        Assert.Equal(NativeProtectedWifiConnectStartResult.Started,
            adapter.TryConnectProtectedWifiNetwork(successful.NativeNetworkKey, successfulSecret));
        Array.Clear(successfulSecret);
        var successfulProfile = calls.SetProfileRequests[^1];
        calls.FireConnectionComplete(
            calls.InterfaceId, successfulProfile.ProfileName, Encoding.UTF8.GetBytes("Office"));
        Assert.Equal(1, calls.DeleteProfileRequests.Count);
        Assert.Equal("WPA3SAE", successfulProfile.Authentication);
        Assert.False(calls.ProfileCustomData.ContainsKey(
            (calls.InterfaceId, successfulProfile.ProfileName)));
        Assert.Equal(0, calls.SetProfileCustomDataRequests[^1].Data.Length);

        Assert.Equal(NativeWifiScanStartResult.Started, adapter.TryStartWifiScan());
        calls.FireScanComplete(calls.InterfaceId);
        calls.AvailableNetworks =
        [
            ControlledNetworkNativeCalls.AvailableNetwork(
                "", Encoding.UTF8.GetBytes("Raced"), 5,
                securityEnabled: true, authentication: 7, cipher: 4),
        ];
        var raced = Assert.Single(adapter.ReadAvailableWifiSnapshot().Networks);
        calls.ProfileCustomDataReads.Enqueue(Enumerable.Repeat((byte)0x4D, 32).ToArray());
        var connectsBeforeRace = calls.ConnectRequests.Count;
        var racedSecret = Secret(14, 21);
        Assert.Equal(NativeProtectedWifiConnectStartResult.RollbackUnverified,
            adapter.TryConnectProtectedWifiNetwork(raced.NativeNetworkKey, racedSecret));
        Array.Clear(racedSecret);
        Assert.Equal(connectsBeforeRace, calls.ConnectRequests.Count);
        Assert.Equal(1, calls.DeleteProfileRequests.Count);

        Assert.Equal(NativeWifiScanStartResult.Started, adapter.TryStartWifiScan());
        calls.FireScanComplete(calls.InterfaceId);
        calls.AvailableNetworks =
        [
            ControlledNetworkNativeCalls.AvailableNetwork(
                "", Encoding.UTF8.GetBytes("Lab"), 3,
                securityEnabled: true, authentication: 7, cipher: 4),
        ];
        var replaced = Assert.Single(adapter.ReadAvailableWifiSnapshot().Networks);
        var replacedSecret = Secret(15, 11);
        Assert.Equal(NativeProtectedWifiConnectStartResult.Started,
            adapter.TryConnectProtectedWifiNetwork(replaced.NativeNetworkKey, replacedSecret));
        Array.Clear(replacedSecret);
        var replacedProfile = calls.SetProfileRequests[^1].ProfileName;
        calls.ProfileCustomData[(calls.InterfaceId, replacedProfile)] =
            Enumerable.Repeat((byte)0xA5, 32).ToArray();
        var deletesBeforeReplacement = calls.DeleteProfileRequests.Count;
        Assert.Equal(
            NativeProtectedWifiRollbackResult.OwnershipMismatch,
            adapter.RollbackProtectedWifiConnection(replaced.NativeNetworkKey));
        Assert.Equal(deletesBeforeReplacement, calls.DeleteProfileRequests.Count);

        Assert.Equal(NativeWifiScanStartResult.Started, adapter.TryStartWifiScan());
        calls.FireScanComplete(calls.InterfaceId);
        calls.AvailableNetworks =
        [
            ControlledNetworkNativeCalls.AvailableNetwork(
                "", Encoding.UTF8.GetBytes("Guest"), 5,
                securityEnabled: true, authentication: 7, cipher: 4),
        ];
        var unverifiable = Assert.Single(adapter.ReadAvailableWifiSnapshot().Networks);
        calls.SetProfileCustomDataResults.Enqueue(5);
        var unverifiableSecret = Secret(16, 13);
        Assert.Equal(NativeProtectedWifiConnectStartResult.RollbackUnverified,
            adapter.TryConnectProtectedWifiNetwork(
                unverifiable.NativeNetworkKey,
                unverifiableSecret));
        Array.Clear(unverifiableSecret);
        Assert.Equal(deletesBeforeReplacement, calls.DeleteProfileRequests.Count);

        calls.SetProfileCustomDataResults.Clear();
        Assert.Equal(NativeWifiScanStartResult.Started, adapter.TryStartWifiScan());
        calls.FireScanComplete(calls.InterfaceId);
        calls.AvailableNetworks =
        [
            ControlledNetworkNativeCalls.AvailableNetwork(
                "", Encoding.UTF8.GetBytes("Unreadable"), 10,
                securityEnabled: true, authentication: 7, cipher: 4),
        ];
        var unreadable = Assert.Single(adapter.ReadAvailableWifiSnapshot().Networks);
        var unreadableSecret = Secret(16, 15);
        Assert.Equal(NativeProtectedWifiConnectStartResult.Started,
            adapter.TryConnectProtectedWifiNetwork(
                unreadable.NativeNetworkKey,
                unreadableSecret));
        Array.Clear(unreadableSecret);
        var unreadableProfile = calls.SetProfileRequests[^1].ProfileName;
        calls.ProfileCustomData.Remove((calls.InterfaceId, unreadableProfile));
        Assert.Equal(
            NativeProtectedWifiRollbackResult.VerificationUnavailable,
            adapter.RollbackProtectedWifiConnection(unreadable.NativeNetworkKey));
        Assert.Equal(deletesBeforeReplacement, calls.DeleteProfileRequests.Count);

        Assert.Equal(NativeWifiScanStartResult.Started, adapter.TryStartWifiScan());
        calls.FireScanComplete(calls.InterfaceId);
        calls.AvailableNetworks =
        [
            ControlledNetworkNativeCalls.AvailableNetwork(
                "", Encoding.UTF8.GetBytes("DeleteFail"), 10,
                securityEnabled: true, authentication: 7, cipher: 4),
        ];
        var deleteFailure = Assert.Single(adapter.ReadAvailableWifiSnapshot().Networks);
        var deleteFailureSecret = Secret(17, 17);
        Assert.Equal(NativeProtectedWifiConnectStartResult.Started,
            adapter.TryConnectProtectedWifiNetwork(
                deleteFailure.NativeNetworkKey,
                deleteFailureSecret));
        Array.Clear(deleteFailureSecret);
        calls.DeleteProfileResults.Enqueue(5);
        Assert.Equal(
            NativeProtectedWifiRollbackResult.DeleteFailed,
            adapter.RollbackProtectedWifiConnection(deleteFailure.NativeNetworkKey));
        Assert.True(adapter.IsDegraded);
        Assert.Equal(0, calls.OutstandingAllocations);
        return Task.CompletedTask;
    }

    private static char[] Secret(int length, int seed) =>
        Enumerable.Range(0, length)
            .Select(index => (char)('A' + ((index + seed) % 26)))
            .ToArray();

    private static int SecretSentinel(ReadOnlySpan<char> secret)
    {
        var value = unchecked((int)2166136261);
        foreach (var character in secret)
            value = unchecked((value ^ character) * 16777619);
        return value;
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
    private readonly HashSet<IntPtr> _activeChangeNotifications = [];
    private readonly object _notificationGate = new();
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
    public int ActiveChangeNotificationCount
    {
        get { lock (_notificationGate) return _activeChangeNotifications.Count; }
    }
    public ManualResetEventSlim? IpRegistrationEntered { get; set; }
    public ManualResetEventSlim? ContinueIpRegistration { get; set; }
    public int? InterfaceDeclaredCount { get; set; }
    public int? RadioDeclaredCount { get; set; }
    public int BestInterfaceIndex { get; set; } = 7;
    public uint BestInterfaceResult { get; set; }
    public bool ManagedNetworkAvailable { get; set; } = true;
    public NetworkConnectivityLevelHint ConnectivityHint { get; set; } =
        NetworkConnectivityLevelHint.InternetAccess;
    public IReadOnlyList<ManagedNetworkInterfaceData> ManagedInterfaces { get; set; } = [];
    public IReadOnlyList<ManagedNetworkConnectionInterfaceData> ConnectionInterfaces { get; set; } = [];
    public IReadOnlyList<WlanInterfaceInfo> Interfaces { get; set; } = [];
    public IReadOnlyList<WlanProfileInfo> Profiles { get; set; } = [];
    public IReadOnlyList<WlanAvailableNetwork> AvailableNetworks { get; set; } = [];
    public IReadOnlyList<WlanPhyRadioState> RadioStates { get; set; } = [];
    public List<(Guid InterfaceId, WlanConnectRequest Request)> ConnectRequests { get; } = [];
    public Queue<uint> SetProfileResults { get; } = [];
    public Queue<uint> SetProfileCustomDataResults { get; } = [];
    public Queue<uint> GetProfileCustomDataResults { get; } = [];
    public Queue<byte[]> ProfileCustomDataReads { get; } = [];
    public Queue<uint> DeleteProfileResults { get; } = [];
    public List<ProfileSetObservation> SetProfileRequests { get; } = [];
    public List<(Guid InterfaceId, string ProfileName, byte[] Data)>
        SetProfileCustomDataRequests { get; } = [];
    public List<(Guid InterfaceId, string ProfileName)>
        GetProfileCustomDataRequests { get; } = [];
    public List<(Guid InterfaceId, string ProfileName)> DeleteProfileRequests { get; } = [];
    public List<(Guid InterfaceId, WlanPhyRadioState State)> SetRadioRequests { get; } = [];
    public NativeWifiNotificationCallback? WlanCallback { get; private set; }
    public IpInterfaceChangeCallback? IpCallback { get; private set; }
    public NetworkConnectivityHintChangeCallback? ConnectivityCallback { get; private set; }

    public int ProfileReads { get; private set; }
    public string? ProfileXml { get; set; }
    public uint ProfileFlags { get; set; }
    public uint ProfileAccess { get; set; } = 0x00070023;
    public uint ProfileReadResult { get; set; }
    public uint ProfileUpdateResult { get; set; }
    public List<(Guid Adapter, string Xml, uint Flags)> ProfileUpdates { get; } = [];
    public List<Guid> Disconnects { get; } = [];
    public uint DisconnectWlan(IntPtr handle, Guid interfaceId)
    { Disconnects.Add(interfaceId); return 0; }
    public uint ReadWlanProfile(IntPtr handle, Guid interfaceId, string name, out string xml, out uint flags, out uint access)
    { ProfileReads++; xml = ProfileXml ?? string.Empty; flags = ProfileFlags; access = ProfileAccess; return ProfileXml is null ? 50 : ProfileReadResult; }
    public uint UpdateWlanProfile(IntPtr handle, Guid interfaceId, string xml, uint flags)
    { ProfileUpdates.Add((interfaceId, xml, flags)); if (ProfileUpdateResult == 0) ProfileXml = xml; return ProfileUpdateResult; }

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
        uint ssidLength,
        bool securityEnabled = false,
        uint authentication = 1,
        uint cipher = 0) => new()
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
        SecurityEnabled = securityEnabled ? 1 : 0,
        DefaultAuthenticationAlgorithm = authentication,
        DefaultCipherAlgorithm = cipher,
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

    public uint SetWlanProfile(
        IntPtr handle,
        Guid interfaceId,
        char[] profileXml,
        out uint reasonCode)
    {
        Assert.Equal(WlanHandle, handle);
        reasonCode = 0;
        var xml = profileXml.AsSpan();
        SetProfileRequests.Add(new(
            interfaceId,
            ReadElement(xml, "<name>".AsSpan(), "</name>".AsSpan()),
            ReadElement(
                xml,
                "<authentication>".AsSpan(),
                "</authentication>".AsSpan()),
            SecretSentinel(ReadElementSpan(
                xml,
                "<keyMaterial>".AsSpan(),
                "</keyMaterial>".AsSpan()))));
        return Next(SetProfileResults);
    }

    public uint DeleteWlanProfile(IntPtr handle, Guid interfaceId, string profileName)
    {
        Assert.Equal(WlanHandle, handle);
        DeleteProfileRequests.Add((interfaceId, profileName));
        var result = Next(DeleteProfileResults);
        if (result == ErrorSuccess) ProfileCustomData.Remove((interfaceId, profileName));
        return result;
    }

    public Dictionary<(Guid InterfaceId, string ProfileName), byte[]> ProfileCustomData { get; } = [];

    public uint SetWlanProfileCustomUserData(
        IntPtr handle,
        Guid interfaceId,
        string profileName,
        byte[] data)
    {
        Assert.Equal(WlanHandle, handle);
        SetProfileCustomDataRequests.Add((interfaceId, profileName, data.ToArray()));
        var result = Next(SetProfileCustomDataResults);
        if (result == ErrorSuccess)
        {
            if (data.Length == 0) ProfileCustomData.Remove((interfaceId, profileName));
            else ProfileCustomData[(interfaceId, profileName)] = data.ToArray();
        }
        return result;
    }

    public uint GetWlanProfileCustomUserData(
        IntPtr handle,
        Guid interfaceId,
        string profileName,
        out byte[] data)
    {
        Assert.Equal(WlanHandle, handle);
        GetProfileCustomDataRequests.Add((interfaceId, profileName));
        var result = Next(GetProfileCustomDataResults);
        if (result == ErrorSuccess && ProfileCustomDataReads.TryDequeue(out var controlled))
        {
            data = controlled;
            return ErrorSuccess;
        }
        if (result == ErrorSuccess &&
            ProfileCustomData.TryGetValue((interfaceId, profileName), out var stored))
        {
            data = stored.ToArray();
            return ErrorSuccess;
        }
        data = [];
        return result == ErrorSuccess ? 1168u : result;
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
        if (IpRegistrationEntered is not null)
        {
            IpRegistrationEntered.Set();
            Assert.True(ContinueIpRegistration?.Wait(TimeSpan.FromSeconds(5)) == true);
        }
        handle = result == ErrorSuccess ? NewHandle() : IntPtr.Zero;
        if (result == ErrorSuccess)
        {
            IpCallback = callback;
            lock (_notificationGate) _activeChangeNotifications.Add(handle);
        }
        return result;
    }

    public uint RegisterConnectivityHintChange(
        NetworkConnectivityHintChangeCallback callback,
        out IntPtr handle)
    {
        ConnectivityRegistrationCalls++;
        var result = Next(ConnectivityRegistrationResults);
        handle = result == ErrorSuccess ? NewHandle() : IntPtr.Zero;
        if (result == ErrorSuccess)
        {
            ConnectivityCallback = callback;
            lock (_notificationGate) _activeChangeNotifications.Add(handle);
        }
        return result;
    }

    public uint CancelChangeNotification(IntPtr handle)
    {
        Assert.True(handle != IntPtr.Zero);
        lock (_notificationGate) Assert.True(_activeChangeNotifications.Remove(handle));
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
        return BestInterfaceResult;
    }

    public bool IsNetworkAvailable() => ManagedNetworkAvailable;

    public IReadOnlyList<ManagedNetworkInterfaceData> ReadManagedInterfaces() =>
        ManagedInterfaces;

    public IReadOnlyList<ManagedNetworkConnectionInterfaceData> ReadConnectionInterfaces() =>
        ConnectionInterfaces;

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

    public void FireConnectionComplete(Guid interfaceId, string profileName, byte[] ssid) =>
        FireConnection(interfaceId, profileName, ssid, 10);

    public void FireConnectionAttemptFail(Guid interfaceId, string profileName, byte[] ssid) =>
        FireConnection(interfaceId, profileName, ssid, 11);

    private void FireConnection(
        Guid interfaceId,
        string profileName,
        byte[] ssid,
        uint notificationCode)
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
                NotificationCode = notificationCode,
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

    private static string ReadElement(
        ReadOnlySpan<char> xml,
        ReadOnlySpan<char> start,
        ReadOnlySpan<char> end) =>
        new(ReadElementSpan(xml, start, end));

    private static ReadOnlySpan<char> ReadElementSpan(
        ReadOnlySpan<char> xml,
        ReadOnlySpan<char> start,
        ReadOnlySpan<char> end)
    {
        var startIndex = xml.IndexOf(start);
        Assert.True(startIndex >= 0);
        var value = xml[(startIndex + start.Length)..];
        var endIndex = value.IndexOf(end);
        Assert.True(endIndex >= 0);
        return value[..endIndex];
    }

    private static int SecretSentinel(ReadOnlySpan<char> secret)
    {
        var value = unchecked((int)2166136261);
        foreach (var character in secret)
            value = unchecked((value ^ character) * 16777619);
        return value;
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

internal sealed record ProfileSetObservation(
    Guid InterfaceId,
    string ProfileName,
    string Authentication,
    int SecretSentinel);
