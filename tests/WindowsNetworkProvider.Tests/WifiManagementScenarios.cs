using System.Xml.Linq;
using WidgetRail.PlatformBroker;
using WidgetRail.WindowsNetworkProvider;

internal static class WifiManagementScenarios
{
    internal static Task Run()
    {
        using var calls = ControlledNetworkNativeCalls.CreateDefault();
        calls.ProfileXml = """
            <WLANProfile xmlns="http://www.microsoft.com/networking/WLAN/profile/v1"><name>Saved</name><SSIDConfig><SSID><name>Home</name></SSID></SSIDConfig><connectionType>ESS</connectionType><connectionMode>auto</connectionMode><MSM><security><sharedKey><protected>true</protected><keyMaterial>encrypted-test-value</keyMaterial></sharedKey></security></MSM><extension xmlns="urn:test">keep</extension></WLANProfile>
            """;
        using var adapter = new WindowsNetworkNativeAdapter(1, calls);
        var profile = adapter.ReadSnapshot().SavedProfiles.Single();
        Check(profile.CanManage && profile.AutoConnect is true, "Writable auto profile was not exposed.");
        var reads = calls.ProfileReads;
        _ = adapter.ReadSnapshot();
        Check(calls.ProfileReads == reads, "An unchanged snapshot reread every profile XML.");
        var original = XDocument.Parse(calls.ProfileXml);
        calls.ProfileFlags = 2; // Per-user scope must survive the update.
        adapter.ManageWifiProfile(profile.NativeProfileKey, false);
        Check(calls.ProfileUpdates.Count == 1 && calls.ProfileUpdates[0].Flags == 2, "Profile scope changed.");
        XNamespace ns = "http://www.microsoft.com/networking/WLAN/profile/v1";
        original.Root!.Element(ns + "connectionMode")!.Value = "manual";
        Check(XNode.DeepEquals(original, XDocument.Parse(calls.ProfileXml)), "Unrelated profile data changed.");
        Check(adapter.ReadSnapshot().SavedProfiles.Single().AutoConnect is false, "Auto setting not reconciled.");
        adapter.ManageWifiProfile(profile.NativeProfileKey, false);
        Check(calls.ProfileUpdates.Count == 1, "An idempotent change rewrote the profile.");
        calls.ProfileFlags = 1;
        Denied(() => adapter.ManageWifiProfile(profile.NativeProfileKey, null), "wifi_profile_policy_denied");
        Check(calls.DeleteProfileRequests.Count == 0, "Group policy profile deleted.");
        calls.ProfileFlags = 0;
        calls.ProfileAccess = 0x20001;
        Denied(() => adapter.ManageWifiProfile(profile.NativeProfileKey, true), "wifi_profile_policy_denied");
        calls.ProfileAccess = 0x70023;
        calls.ProfileUpdateResult = 5;
        Denied(() => adapter.ManageWifiProfile(profile.NativeProfileKey, true), "wifi_profile_policy_denied");
        Check(adapter.ReadSnapshot().SavedProfiles.Single().AutoConnect is false, "Failed write changed readback.");
        adapter.ManageWifiProfile(profile.NativeProfileKey, null);
        Check(calls.DeleteProfileRequests is [{ ProfileName: "Saved" }], "Forget targeted the wrong profile.");
        Denied(() => adapter.ManageWifiProfile("missing", null), "resource_not_found");

        var network = ControlledNetworkNativeCalls.AvailableNetwork("Saved", [72, 111, 109, 101], 4);
        network.Flags |= 1;
        calls.AvailableNetworks = [network];
        adapter.TryStartWifiScan();
        var completed = new WlanNotificationData { NotificationSource = 8, NotificationCode = 7, InterfaceGuid = calls.InterfaceId };
        calls.WlanCallback!(ref completed, IntPtr.Zero);
        var current = adapter.ReadAvailableWifiSnapshot().Networks.Single();
        adapter.DisconnectWifi(current.NativeNetworkKey);
        Check(calls.Disconnects.SequenceEqual([calls.InterfaceId]), "Disconnect targeted another adapter.");
        _ = adapter.ReadAvailableWifiSnapshot(); // Still connected when command acknowledgement arrives.
        network.Flags &= ~1u;
        calls.AvailableNetworks = [network];
        var disconnected = new WlanNotificationData { NotificationSource = 8, NotificationCode = 21, InterfaceGuid = calls.InterfaceId };
        calls.WlanCallback!(ref disconnected, IntPtr.Zero);
        Check(adapter.ReadAvailableWifiSnapshot().Networks.All(item => !item.IsConnected),
            "Disconnect event left the old connected flag cached.");
        calls.AvailableNetworks = [];
        Denied(() => adapter.DisconnectWifi(current.NativeNetworkKey), "resource_not_found");
        Check(calls.Disconnects.Count == 1, "Stale selection disconnected a different connection.");
        Check(calls.OutstandingAllocations == 0, "Native list allocation leaked.");
        return Task.CompletedTask;
    }

    private static void Check(bool condition, string message)
    { if (!condition) throw new InvalidOperationException(message); }
    private static void Denied(Action action, string code)
    {
        try { action(); }
        catch (BrokerException exception) when (exception.Code == code) { return; }
        throw new InvalidOperationException("Expected refusal: " + code);
    }
}
