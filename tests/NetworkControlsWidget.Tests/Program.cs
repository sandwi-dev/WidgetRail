using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading.Channels;
using GameBarAlternative.FirstPartyWidgets.NetworkControls;
using GameBarAlternative.WidgetProtocol;
using GameBarAlternative.WidgetSdk;
using GameBarAlternative.WidgetStyling;

var tests = new (string Name, Func<Task> Run)[]
{
    ("Visible lifecycle reads available Wi-Fi without scanning or polling", VisibleReadDoesNotScan),
    ("Explicit scan is controller initiated and reconciles provider events", ExplicitScan),
    ("Only current scan results render with honest connection metadata", AvailableNetworksRender),
    ("Precise-location denial has a bounded permission state", PreciseLocationState),
    ("Available network focus graph preserves host back and tray boundaries", ControllerFocusGraph),
    ("Opaque network identity preserves selection across event churn", StableOpaqueSelection),
    ("Saved and open visible networks start typed host connections without optimistic success", EligibleConnections),
    ("Credential and unsupported authentication never prompt in the worker", CredentialsStayOutOfWorker),
    ("Connection failures are typed sanitized and recoverable", ConnectionFailures),
    ("Capability failures render bounded recovery surfaces", CapabilityFailureStates),
    ("Lifecycle cancellation tears down both event streams", LifecycleCancellation),
    ("Wi-Fi radio toggle is controller-native and reconciles authoritative state", WifiRadioToggle),
    ("Bluetooth rows pair or manage explicitly without optimistic connection state", BluetoothDeviceListing),
    ("Bluetooth radio control is optional typed and authoritatively reconciled", BluetoothRadioToggle),
    ("Bluetooth permission failure does not break Wi-Fi controls", BluetoothPermissionIsolation),
    ("Radio cancellation cannot overwrite a later widget lifecycle", RadioCancellationIsGenerationBound),
    ("Provider events outrun command acknowledgements and refresh replaces one run", ProviderCommandAndRefreshInterleavings),
    ("Provider and command policies are pure typed boundaries", PoliciesArePureAndTyped),
    ("Action routing is an exact closed policy", ActionRoutingIsExact),
    ("Pure presentation repeats the same semantic snapshot", PresentationIsDeterministic),
    ("Responsibility split retains one lifecycle and committed-state owner", ResponsibilityBoundariesAreSingular),
    ("Manifest catalog and responsive GBSS ship the Wi-Fi capabilities", ShippedAssetsValidate),
};

var failures = new List<string>();
foreach (var test in tests)
{
    try
    {
        await test.Run();
        Console.WriteLine($"PASS {test.Name}");
    }
    catch (Exception exception)
    {
        var detail = exception is ProtocolValidationException protocol
            ? $"{exception.Message}{Environment.NewLine}{string.Join(Environment.NewLine, protocol.Errors)}"
            : exception.Message;
        failures.Add($"FAIL {test.Name}: {detail}");
        Console.Error.WriteLine(failures[^1]);
    }
}

Console.WriteLine($"{tests.Length - failures.Count}/{tests.Length} tests passed.");
return failures.Count == 0 ? 0 : 1;

static async Task VisibleReadDoesNotScan()
{
    var fake = ReadyHost(WidgetWifiScanState.NotScanned, []);
    var widget = Create(fake);
    await WidgetTestHost.InitializeAsync(widget);
    Assert.Equal(0, fake.StatusCalls);
    Assert.Equal(0, fake.WifiCalls);
    Assert.Equal(0, fake.ScanCalls);

    await ActivateVisible(widget);
    await WaitUntil(() => widget.ViewState == NetworkControlsViewState.NotScanned);
    Assert.Equal(1, fake.StatusCalls);
    Assert.Equal(1, fake.WifiCalls);
    Assert.Equal(0, fake.ScanCalls);
    Assert.Equal(1, fake.StatusSubscriptionCount);
    Assert.Equal(1, fake.WifiSubscriptionCount);
    Assert.Equal("network.wifi.scan", Snapshot(widget, 1).InitialFocusId);

    await WidgetTestHost.SetLifecycleStateAsync(widget, WidgetLifecycleState.Interactive);
    await Task.Delay(125);
    Assert.Equal(1, fake.StatusCalls);
    Assert.Equal(1, fake.WifiCalls);
    Assert.Equal(0, fake.ScanCalls);
    await Background(widget);
}

static async Task ExplicitScan()
{
    var fake = ReadyHost(WidgetWifiScanState.NotScanned, []);
    var widget = Create(fake);
    await ActivateVisible(widget);
    await WaitUntil(() => widget.ViewState == NetworkControlsViewState.NotScanned);

    await widget.OnActionAsync(new("wifi.scan", "network.wifi.scan"));
    Assert.Equal(0, fake.ScanCalls);
    Assert.Contains("Open Network Controls", Text(Snapshot(widget, 1).Root, "network.status").Text!);

    await WidgetTestHost.SetLifecycleStateAsync(widget, WidgetLifecycleState.Interactive);
    await widget.OnActionAsync(new("wifi.scan", "network.wifi.scan"));
    Assert.Equal(1, fake.ScanCalls);
    Assert.True(widget.ScanBusy, "Explicit scan did not expose an immediate busy state.");
    Assert.Equal(NetworkControlsViewState.Scanning, widget.ViewState);

    fake.EmitWifi(Wifi(WidgetWifiScanState.Ready,
        Network("scan-a", "Studio", 86, WidgetWifiSecurityKind.Personal, saved: true),
        Network("scan-b", "Guest", 62, WidgetWifiSecurityKind.Open)));
    await WaitUntil(() => widget.ViewState == NetworkControlsViewState.Ready);
    Assert.Equal(2, widget.Networks.Count);
    Assert.True(!widget.ScanBusy);
    Assert.Equal(1, fake.ScanCalls);
    Assert.Valid(Snapshot(widget, 2));
    await Background(widget);
}

static async Task AvailableNetworksRender()
{
    var fake = ReadyHost(WidgetWifiScanState.Ready,
    [
        Network("opaque-connected", "Home 5G", 91, WidgetWifiSecurityKind.Personal,
            saved: true, connected: true),
        Network("opaque-saved", "Office", 73, WidgetWifiSecurityKind.Personal, saved: true),
        Network("opaque-open", "Cafe", 44, WidgetWifiSecurityKind.Open),
        Network("opaque-password", "Neighbor", 38, WidgetWifiSecurityKind.Personal,
            credential: true),
    ]);
    var widget = Create(fake);
    await ActivateInteractive(widget);
    await WaitUntil(() => widget.ViewState == NetworkControlsViewState.Ready);
    var snapshot = Snapshot(widget, 1);
    var rows = NetworkButtons(snapshot.Root).ToArray();
    Assert.SequenceEqual(["Home 5G", "Office", "Cafe", "Neighbor"], rows.Select(row => row.Text!));
    Assert.Equal("CONNECTED", NetworkState(snapshot.Root, "Home 5G").Text);
    Assert.Equal("SAVED", NetworkState(snapshot.Root, "Office").Text);
    Assert.Equal("OPEN", NetworkState(snapshot.Root, "Cafe").Text);
    Assert.Equal("PASSWORD REQUIRED", NetworkState(snapshot.Root, "Neighbor").Text);
    Assert.True(rows.All(row => row.IsSelected is not true),
        "Remembered Wi-Fi focus was exposed as a connected checkmark.");
    Assert.Equal(WidgetGlyph.Check, rows.Single(row => row.Text == "Home 5G").Glyph);
    Assert.Equal(WidgetGlyph.Wifi, rows.Single(row => row.Text == "Office").Glyph);
    Assert.True(!Buttons(snapshot.Root).Any(button =>
            button.ActionId?.Contains("profile", StringComparison.OrdinalIgnoreCase) == true),
        "Product UI still exposed the legacy saved-profile list.");
    Assert.Equal(0, fake.ScanCalls);
    Assert.Valid(snapshot);
    await Background(widget);
}

static async Task PreciseLocationState()
{
    var fake = ReadyHost(WidgetWifiScanState.PreciseLocationDenied, []);
    fake.Status = fake.Status with { DetailsAccess = WidgetNetworkDetailsAccess.PrivacyRestricted };
    var widget = Create(fake);
    await ActivateInteractive(widget);
    await WaitUntil(() => widget.ViewState == NetworkControlsViewState.PreciseLocationDenied);
    var snapshot = Snapshot(widget, 1);
    Assert.Contains("PRECISE LOCATION REQUIRED", Text(snapshot.Root, "network.wifi.note.title").Text!);
    Assert.Contains("Location permission required", Text(snapshot.Root, "network.wifi.state.title").Text!);
    Assert.Contains("Windows Settings", Text(snapshot.Root, "network.wifi.state.help").Text!);
    Assert.True(!Nodes(snapshot.Root).Any(node =>
            node.ActionId?.Contains("location", StringComparison.OrdinalIgnoreCase) == true),
        "The untrusted worker offered an unbrokered location-consent action.");
    Assert.Equal(0, fake.ScanCalls);
    Assert.Valid(snapshot);
    await Background(widget);
}

static async Task ControllerFocusGraph()
{
    var fake = ReadyHost(WidgetWifiScanState.Ready,
    [
        Network("opaque-a", "Alpha", 80, WidgetWifiSecurityKind.Open),
        Network("opaque-b", "Beta", 70, WidgetWifiSecurityKind.Personal, saved: true),
        Network("opaque-c", "Gamma", 60, WidgetWifiSecurityKind.Personal, credential: true),
    ]);
    var widget = Create(fake);
    await ActivateInteractive(widget);
    await WaitUntil(() => widget.ViewState == NetworkControlsViewState.Ready);
    var snapshot = Snapshot(widget, 1);
    var scan = Button(snapshot.Root, "network.wifi.scan");
    var rows = NetworkButtons(snapshot.Root).ToArray();
    Assert.Equal(rows[0].Id, scan.Focus!.Down);
    Assert.Equal("network.wifi.scan", rows[0].Focus!.Up);
    Assert.Equal(rows[1].Id, rows[0].Focus!.Down);
    Assert.Equal(rows[2].Id, rows[1].Focus!.Down);
    Assert.True(rows[2].Focus!.Down is null,
        "Final Wi-Fi network must leave Down unclaimed for the host tray boundary.");
    foreach (var row in rows)
    {
        Assert.Equal(row.Id, row.Focus!.Left);
        Assert.Equal(row.Id, row.Focus.Right);
        Assert.True(row.Shortcuts.Any(shortcut =>
            shortcut.Button == ControllerButton.X && shortcut.ActionId == "wifi.connect.item"));
    }
    Assert.True(!await Route(widget, snapshot, ControllerButton.B,
        ControllerInputContext.OpenWidget, rows[1].Id),
        "Root B must remain unclaimed so the host can return focus to the tray.");
    Assert.True(!await Route(widget, snapshot, ControllerButton.DPadDown,
        ControllerInputContext.OpenWidget, rows[^1].Id),
        "The widget must not capture Down at its final root control.");
    Assert.True(!Buttons(snapshot.Root).Any(button =>
            button.Id.StartsWith("network.bluetooth.", StringComparison.Ordinal)),
        "Bluetooth controls remained in the Wi-Fi focus tree.");
    Assert.True(await Route(widget, snapshot, ControllerButton.RightBumper,
        ControllerInputContext.OpenWidget, rows[1].Id),
        "RB did not route the widget-owned tab shortcut.");
    await WaitUntil(() => widget.ActiveTab == NetworkControlsTab.Bluetooth);
    var bluetooth = Snapshot(widget, 2);
    Assert.True(!NetworkButtons(bluetooth.Root).Any(),
        "Wi-Fi network rows remained in the Bluetooth focus tree.");
    Assert.Equal("network.bluetooth.radio", bluetooth.InitialFocusId);
    Assert.True(await Route(widget, bluetooth, ControllerButton.LeftBumper,
        ControllerInputContext.OpenWidget, "network.bluetooth.radio"),
        "LB did not route the widget-owned tab shortcut.");
    await WaitUntil(() => widget.ActiveTab == NetworkControlsTab.Wifi);
    Assert.Equal(rows[1].Id, Snapshot(widget, 3).InitialFocusId);
    Assert.Equal(0, snapshot.QuickActions.Count);
    Assert.Valid(snapshot);
    await Background(widget);
}

static async Task StableOpaqueSelection()
{
    var fake = ReadyHost(WidgetWifiScanState.Ready,
    [
        Network("opaque-a", "Alpha", 80, WidgetWifiSecurityKind.Open),
        Network("opaque-b", "Beta", 70, WidgetWifiSecurityKind.Personal, credential: true),
        Network("opaque-c", "Gamma", 60, WidgetWifiSecurityKind.Open),
    ]);
    var widget = Create(fake);
    await ActivateInteractive(widget);
    await WaitUntil(() => widget.ViewState == NetworkControlsViewState.Ready);
    var initial = Snapshot(widget, 1);
    await widget.OnActionAsync(new("wifi.connect.item", NetworkButton(initial.Root, "Beta").Id));
    Assert.Equal("opaque-b", widget.SelectedNetworkId);
    Assert.Equal(0, fake.ConnectCalls);

    fake.EmitWifi(Wifi(WidgetWifiScanState.Ready,
        Network("opaque-c", "Gamma", 61, WidgetWifiSecurityKind.Open),
        Network("opaque-b", "Beta renamed", 72, WidgetWifiSecurityKind.Personal, credential: true),
        Network("opaque-a", "Alpha", 82, WidgetWifiSecurityKind.Open)));
    await WaitUntil(() => widget.Networks[0].NetworkId == "opaque-c");
    Assert.Equal("opaque-b", widget.SelectedNetworkId);
    var retained = Snapshot(widget, 2);
    Assert.Equal(NetworkButton(retained.Root, "Beta renamed").Id, retained.InitialFocusId);

    fake.EmitWifi(Wifi(WidgetWifiScanState.Ready,
        Network("generation-2-a", "Delta", 75, WidgetWifiSecurityKind.Open),
        Network("generation-2-b", "Epsilon", 65, WidgetWifiSecurityKind.Open)));
    await WaitUntil(() => widget.Networks[0].NetworkId == "generation-2-a");
    Assert.Equal("generation-2-b", widget.SelectedNetworkId);
    Assert.Equal(NetworkButton(Snapshot(widget, 3).Root, "Epsilon").Id,
        Snapshot(widget, 4).InitialFocusId);
    await Background(widget);
}

static async Task EligibleConnections()
{
    foreach (var candidate in new[]
             {
                 Network("saved", "Saved Home", 80, WidgetWifiSecurityKind.Personal, saved: true),
                 Network("open", "Public Guest", 60, WidgetWifiSecurityKind.Open),
             })
    {
        var fake = ReadyHost(WidgetWifiScanState.Ready, [candidate]);
        var widget = Create(fake);
        await ActivateInteractive(widget);
        await WaitUntil(() => widget.ViewState == NetworkControlsViewState.Ready);
        var snapshot = Snapshot(widget, 1);
        Assert.True(await Route(widget, snapshot, ControllerButton.A,
            ControllerInputContext.OpenWidget, NetworkButton(snapshot.Root, candidate.DisplayName).Id));
        await WaitUntil(() => fake.ConnectCalls == 1);
        Assert.Equal(candidate.NetworkId, fake.ConnectRequests.Single().NetworkId);
        Assert.True(widget.ControlBusy, "Broker ACK was incorrectly treated as connection success.");
        var acknowledged = Snapshot(widget, 2);
        Assert.Equal(WidgetGlyph.Wifi,
            NetworkButton(acknowledged.Root, candidate.DisplayName).Glyph);
        Assert.Equal("CONNECTING",
            NetworkState(acknowledged.Root, candidate.DisplayName).Text);
        Assert.True(Button(acknowledged.Root, "network.wifi.scan").IsDisabled is true,
            "Scan remained actionable while a connection attempt owned the provider.");

        fake.EmitStatus(fake.Status with
        {
            ConnectionAttemptState = WidgetNetworkConnectionAttemptState.Connecting,
            AttemptProfileId = candidate.NetworkId,
        });
        await WaitUntil(() => widget.NetworkStatus?.ConnectionAttemptState ==
                              WidgetNetworkConnectionAttemptState.Connecting);
        fake.EmitStatus(fake.Status with
        {
            ConnectionAttemptState = WidgetNetworkConnectionAttemptState.None,
            AttemptProfileId = null,
            ActiveProfileId = candidate.NetworkId,
            ActiveProfileName = candidate.DisplayName,
        });
        fake.EmitWifi(Wifi(WidgetWifiScanState.Ready, candidate with { IsConnected = true }));
        await WaitUntil(() => !widget.ControlBusy && widget.Networks[0].IsConnected);
        Assert.Equal("CONNECTED", NetworkState(Snapshot(widget, 2).Root, candidate.DisplayName).Text);
        await Background(widget);
    }
}

static async Task CredentialsStayOutOfWorker()
{
    var fake = ReadyHost(WidgetWifiScanState.Ready,
    [
        Network("locked", "Locked", 70, WidgetWifiSecurityKind.Personal, credential: true),
        Network("enterprise", "Enterprise", 65, WidgetWifiSecurityKind.Enterprise),
        Network("unknown", "Unknown", 55, WidgetWifiSecurityKind.Unknown),
    ]);
    var widget = Create(fake);
    await ActivateInteractive(widget);
    await WaitUntil(() => widget.ViewState == NetworkControlsViewState.Ready);

    var snapshot = Snapshot(widget, 1);
    await widget.OnActionAsync(new("wifi.connect.item", NetworkButton(snapshot.Root, "Locked").Id));
    Assert.Equal(0, fake.ConnectCalls);
    Assert.Contains("Windows Quick Settings", Text(Snapshot(widget, 2).Root, "network.status").Text!);

    await widget.OnActionAsync(new("wifi.connect.item", NetworkButton(Snapshot(widget, 3).Root, "Enterprise").Id));
    Assert.Equal(0, fake.ConnectCalls);
    Assert.Contains("Windows network settings", Text(Snapshot(widget, 4).Root, "network.status").Text!);
    Assert.True(!Nodes(Snapshot(widget, 5).Root).Any(node =>
            node.Id.Contains("password", StringComparison.OrdinalIgnoreCase) ||
            node.ActionId?.Contains("credential", StringComparison.OrdinalIgnoreCase) == true),
        "The worker rendered a secret-entry route.");
    await Background(widget);
}

static async Task ConnectionFailures()
{
    var cases = new[]
    {
        ("credential_required", "needs a password"),
        ("unsupported_authentication", "Windows network settings"),
        ("resource_not_found", "scan result expired"),
        ("provider_busy", "already in progress"),
    };
    foreach (var (code, expected) in cases)
    {
        var fake = ReadyHost(WidgetWifiScanState.Ready,
            [Network("eligible", "Eligible", 70, WidgetWifiSecurityKind.Personal, saved: true)]);
        fake.ConnectException = new WidgetCapabilityException(code, "private-provider-detail");
        var widget = Create(fake);
        await ActivateInteractive(widget);
        await WaitUntil(() => widget.ViewState == NetworkControlsViewState.Ready);
        var row = NetworkButton(Snapshot(widget, 1).Root, "Eligible");
        await widget.OnActionAsync(new("wifi.connect.item", row.Id));
        Assert.Equal(1, fake.ConnectCalls);
        Assert.True(!widget.ControlBusy);
        var feedback = Text(Snapshot(widget, 2).Root, "network.status").Text!;
        Assert.Contains(expected, feedback);
        Assert.True(!feedback.Contains("private-provider-detail", StringComparison.Ordinal));
        await Background(widget);
    }
}

static async Task CapabilityFailureStates()
{
    var cases = new[]
    {
        ("permission_denied", NetworkControlsViewState.PermissionDenied, "permission required"),
        ("capability_not_declared", NetworkControlsViewState.PermissionDenied, "permission required"),
        ("lifecycle_denied", NetworkControlsViewState.LifecycleDenied, "paused"),
        ("channel_closed", NetworkControlsViewState.ChannelClosed, "disconnected"),
        ("platform_unavailable", NetworkControlsViewState.ServiceUnavailable, "unavailable"),
    };
    foreach (var (code, expectedState, title) in cases)
    {
        var fake = ReadyHost(WidgetWifiScanState.NotScanned, []);
        fake.ReadException = new WidgetCapabilityException(code, "private-provider-path");
        var widget = Create(fake);
        await ActivateVisible(widget);
        await WaitUntil(() => widget.ViewState == expectedState);
        var snapshot = Snapshot(widget, 1);
        Assert.Contains(title, Text(snapshot.Root, "network.state.title").Text!);
        Assert.True(!Nodes(snapshot.Root).Any(node =>
            node.Text?.Contains("private-provider-path", StringComparison.Ordinal) == true));
        Assert.Valid(snapshot);
        await Background(widget);
    }
}

static async Task LifecycleCancellation()
{
    var fake = ReadyHost(WidgetWifiScanState.Ready,
        [Network("one", "One", 70, WidgetWifiSecurityKind.Open)]);
    var widget = Create(fake);
    await ActivateVisible(widget);
    await WaitUntil(() => widget.ViewState == NetworkControlsViewState.Ready &&
                          fake.StatusSubscriptionCount == 1 && fake.WifiSubscriptionCount == 1 &&
                          fake.RadioSubscriptionCount == 1 &&
                          fake.BluetoothSubscriptionCount == 1);
    await Background(widget);
    await WaitUntil(() => fake.CanceledStatusSubscriptions == 1 &&
                          fake.CanceledWifiSubscriptions == 1 &&
                          fake.CanceledRadioSubscriptions == 1 &&
                          fake.CanceledBluetoothSubscriptions == 1);
    var statusCalls = fake.StatusCalls;
    var wifiCalls = fake.WifiCalls;
    await Task.Delay(125);
    Assert.Equal(statusCalls, fake.StatusCalls);
    Assert.Equal(wifiCalls, fake.WifiCalls);
    Assert.Equal(0, fake.ScanCalls);

    await ActivateVisible(widget);
    await WaitUntil(() => fake.StatusCalls == 2 && fake.WifiCalls == 2);
    Assert.Equal(2, widget.ActivationCount);
    await Background(widget);
}

static async Task WifiRadioToggle()
{
    var fake = ReadyHost(WidgetWifiScanState.Ready,
        [Network("one", "One", 70, WidgetWifiSecurityKind.Open)]);
    var widget = Create(fake);
    await ActivateInteractive(widget);
    await WaitUntil(() => widget.ViewState == NetworkControlsViewState.Ready);

    var on = Button(Snapshot(widget, 1).Root, "network.wifi.radio");
    Assert.True(on.IsSelected is true);
    Assert.Equal("network.wifi.scan", on.Focus?.Down);
    await widget.OnActionAsync(new("wifi.radio.toggle", on.Id));
    await WaitUntil(() => widget.WifiRadio?.State == WidgetWifiRadioState.Off &&
                          !widget.RadioBusy);
    Assert.Equal(1, fake.RadioSetCalls);
    var off = Button(Snapshot(widget, 2).Root, "network.wifi.radio");
    Assert.True(off.IsSelected is not true);
    Assert.Equal("network.wifi.radio", off.Id);

    fake.EmitRadio(new WidgetWifiRadio(WidgetWifiRadioState.HardwareDisabled, false));
    await WaitUntil(() => widget.WifiRadio?.State == WidgetWifiRadioState.HardwareDisabled);
    Assert.True(Button(Snapshot(widget, 3).Root, "network.wifi.radio").IsDisabled is true);
    await Background(widget);
}

static async Task BluetoothDeviceListing()
{
    var fake = ReadyHost(WidgetWifiScanState.Ready,
        [Network("one", "One", 70, WidgetWifiSecurityKind.Open)]);
    fake.Bluetooth = new WidgetBluetoothSnapshot(
        WidgetBluetoothRadioState.On, true, WidgetBluetoothDiscoveryState.Ready,
        [
            new("bluetooth-a", "Wireless controller", true, true, true),
            new("bluetooth-b", "Paired headset", true, false, true),
            new("bluetooth-c", "Nearby keyboard", false, false, true),
        ]);
    var widget = Create(fake);
    await ActivateInteractive(widget);
    await WaitUntil(() => widget.Bluetooth?.Devices.Count == 3);
    await WaitUntil(() => fake.BluetoothSubscriptionCount == 1);
    await widget.OnActionAsync(new("network.tab.select", "network.tab.bluetooth"));
    var snapshot = Snapshot(widget, 1);
    var radio = Button(snapshot.Root, "network.bluetooth.radio");
    var devices = Buttons(snapshot.Root)
        .Where(button => button.Id.StartsWith(
            "network.bluetooth.item.", StringComparison.Ordinal)).ToArray();
    Assert.Equal(3, devices.Length);
    Assert.Equal("bluetooth.device.manage", devices[0].ActionId);
    Assert.Equal("bluetooth.device.manage", devices[1].ActionId);
    Assert.Equal("bluetooth.device.pair", devices[2].ActionId);
    Assert.Equal("Wireless controller", devices[0].Text);
    Assert.True(devices.All(device => device.IsSelected is not true),
        "Remembered Bluetooth focus was exposed as a connected checkmark.");
    Assert.True(devices.All(device => device.Glyph == WidgetGlyph.Connection),
        "Bluetooth focus or activation was visually confused with authoritative connection state.");
    Assert.Equal(devices[0].Id, radio.Focus!.Down);
    Assert.Equal("network.bluetooth.radio", devices[0].Focus!.Up);
    Assert.Equal(devices[1].Id, devices[0].Focus!.Down);
    Assert.Equal(devices[2].Id, devices[1].Focus!.Down);
    Assert.True(devices[2].Focus!.Down is null,
        "Final Bluetooth device must leave Down unclaimed for the host tray boundary.");
    Assert.True(!Nodes(snapshot.Root).Any(node =>
            node.Text?.Contains("bluetooth-a", StringComparison.Ordinal) == true),
        "Opaque Bluetooth identity leaked into visible UI text.");
    Assert.True(!Buttons(snapshot.Root).Any(button =>
            button.ActionId?.Contains("connect", StringComparison.OrdinalIgnoreCase) == true &&
            button.Id.StartsWith("network.bluetooth", StringComparison.Ordinal)),
        "Widget claimed a generic Bluetooth connect operation.");

    await widget.OnActionAsync(new("bluetooth.device.manage", devices[0].Id));
    Assert.Equal(1, fake.BluetoothManageCalls);
    Assert.Equal("bluetooth-a", fake.LastBluetoothDeviceId);
    Assert.Contains("Windows Bluetooth Settings opened for Wireless controller",
        Text(Snapshot(widget, 2).Root, "network.bluetooth.summary").Text!);

    fake.BluetoothPairingOutcome = WidgetBluetoothPairingOutcome.UserInteractionRequired;
    await widget.OnActionAsync(new("bluetooth.device.pair", devices[2].Id));
    var guidance = Snapshot(widget, 4);
    Assert.Equal(1, fake.BluetoothPairCalls);
    Assert.Equal(1, fake.BluetoothManageCalls);
    Assert.Equal("bluetooth-c", fake.LastBluetoothDeviceId);
    Assert.Contains("needs Windows confirmation · press X",
        Text(guidance.Root, "network.bluetooth.summary").Text!);
    Assert.Equal(WidgetGlyph.Connection,
        Buttons(guidance.Root).Single(button => button.Text == "Nearby keyboard").Glyph);
    Assert.True(widget.Bluetooth!.Devices.Single(device =>
        device.DisplayName == "Wireless controller").IsConnected,
        "A read-only details action disconnected an authoritative Bluetooth device.");
    Assert.True(widget.Bluetooth.Devices.Single(device =>
        device.DisplayName == "Paired headset") is { IsPaired: true, IsConnected: false },
        "A read-only details action connected an authoritative paired Bluetooth device.");
    Assert.True(widget.Bluetooth.Devices.Single(device =>
        device.DisplayName == "Nearby keyboard") is { IsPaired: false, IsConnected: false },
        "A read-only details action mutated authoritative Bluetooth connection state.");

    await widget.OnActionAsync(new("bluetooth.device.manage", devices[2].Id));
    Assert.Equal(2, fake.BluetoothManageCalls);
    Assert.Equal("bluetooth-c", fake.LastBluetoothDeviceId);

    fake.BluetoothPairingOutcome = WidgetBluetoothPairingOutcome.Paired;
    await widget.OnActionAsync(new("bluetooth.device.pair", devices[2].Id));
    Assert.Equal(2, fake.BluetoothPairCalls);
    Assert.True(widget.Bluetooth.Devices.Single(device =>
        device.DisplayName == "Nearby keyboard") is { IsPaired: true, IsConnected: false },
        "Successful pairing was either not reconciled or was misreported as connected.");
    Assert.Contains("paired · waiting for Windows connection state",
        Text(Snapshot(widget, 5).Root, "network.bluetooth.summary").Text!);

    fake.EmitBluetooth(fake.Bluetooth with
    {
        Devices = [new("bluetooth-a", "Wireless controller", true, false, true)],
    });
    await WaitUntil(() => widget.Bluetooth?.Devices.Count == 1 &&
                          widget.Bluetooth.Devices[0].IsConnected == false);
    var reconciled = Snapshot(widget, 6);
    Assert.Equal("PAIRED", Text(reconciled.Root,
        $"{Buttons(reconciled.Root).Single(button => button.Text == "Wireless controller").Id}.state").Text);
    Assert.Valid(reconciled);
    await Background(widget);
}

static async Task BluetoothRadioToggle()
{
    var fake = ReadyHost(WidgetWifiScanState.NotScanned, []);
    fake.Bluetooth = new WidgetBluetoothSnapshot(
        WidgetBluetoothRadioState.On, true, WidgetBluetoothDiscoveryState.Ready, []);
    var widget = Create(fake);
    await ActivateInteractive(widget);
    await WaitUntil(() => widget.Bluetooth?.RadioState == WidgetBluetoothRadioState.On);
    await widget.OnActionAsync(new("network.tab.select", "network.tab.bluetooth"));
    var radio = Button(Snapshot(widget, 1).Root, "network.bluetooth.radio");
    Assert.True(radio.IsSelected is true);
    await widget.OnActionAsync(new("bluetooth.radio.toggle", radio.Id));
    await WaitUntil(() => widget.Bluetooth?.RadioState == WidgetBluetoothRadioState.Off);
    Assert.Equal(1, fake.BluetoothRadioSetCalls);
    Assert.Equal(1, widget.BluetoothRadioControlCount);
    Assert.True(Button(Snapshot(widget, 2).Root, "network.bluetooth.radio").IsSelected is not true);

    fake.EmitBluetooth(new WidgetBluetoothSnapshot(
        WidgetBluetoothRadioState.HardwareDisabled, false,
        WidgetBluetoothDiscoveryState.Ready, []));
    await WaitUntil(() => widget.Bluetooth?.RadioState ==
                          WidgetBluetoothRadioState.HardwareDisabled);
    Assert.True(Button(Snapshot(widget, 3).Root, "network.bluetooth.radio").IsDisabled is true);
    await Background(widget);
}

static async Task BluetoothPermissionIsolation()
{
    var fake = ReadyHost(WidgetWifiScanState.Ready,
        [Network("one", "One", 70, WidgetWifiSecurityKind.Open)]);
    fake.BluetoothException = new WidgetCapabilityException(
        "permission_denied", "native bluetooth identifier");
    var widget = Create(fake);
    await ActivateInteractive(widget);
    await WaitUntil(() => widget.ViewState == NetworkControlsViewState.Ready &&
                          widget.Bluetooth?.DiscoveryState ==
                          WidgetBluetoothDiscoveryState.Unavailable);
    var snapshot = Snapshot(widget, 1);
    Assert.Equal("One", NetworkButtons(snapshot.Root).Single().Text);
    Assert.True(!Nodes(snapshot.Root).Any(node => node.Id == "network.bluetooth.summary"),
        "Inactive Bluetooth content remained in the Wi-Fi tree.");
    await widget.OnActionAsync(new("network.tab.select", "network.tab.bluetooth"));
    snapshot = Snapshot(widget, 2);
    Assert.Contains("Settings", Text(snapshot.Root, "network.bluetooth.summary").Text!);
    Assert.True(!Nodes(snapshot.Root).Any(node =>
            node.Text?.Contains("native bluetooth identifier", StringComparison.Ordinal) == true),
        "Provider detail escaped the optional Bluetooth error boundary.");
    await Background(widget);
}

static async Task RadioCancellationIsGenerationBound()
{
    var wifiFake = ReadyHost(WidgetWifiScanState.NotScanned, []);
    wifiFake.HoldWifiRadioSet = true;
    var wifiWidget = Create(wifiFake);
    await ActivateInteractive(wifiWidget);
    await WaitUntil(() => wifiWidget.ViewState == NetworkControlsViewState.NotScanned);
    var wifiAction = wifiWidget.OnActionAsync(new(
        "wifi.radio.toggle", "network.wifi.radio")).AsTask();
    await wifiFake.WifiRadioSetStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
    await Background(wifiWidget);
    await Assert.Canceled(wifiAction);
    wifiFake.HoldWifiRadioSet = false;
    await ActivateVisible(wifiWidget);
    await WaitUntil(() => wifiWidget.ViewState == NetworkControlsViewState.NotScanned);
    Assert.True(!Text(Snapshot(wifiWidget, 1).Root, "network.status").Text!
        .Contains("could not", StringComparison.OrdinalIgnoreCase));
    await Background(wifiWidget);

    var bluetoothFake = ReadyHost(WidgetWifiScanState.NotScanned, []);
    bluetoothFake.HoldBluetoothRadioSet = true;
    var bluetoothWidget = Create(bluetoothFake);
    await ActivateInteractive(bluetoothWidget);
    await WaitUntil(() => bluetoothWidget.Bluetooth is not null);
    var bluetoothAction = bluetoothWidget.OnActionAsync(new(
        "bluetooth.radio.toggle", "network.bluetooth.radio")).AsTask();
    await bluetoothFake.BluetoothRadioSetStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
    await Background(bluetoothWidget);
    await Assert.Canceled(bluetoothAction);
    bluetoothFake.HoldBluetoothRadioSet = false;
    await ActivateVisible(bluetoothWidget);
    await WaitUntil(() => bluetoothWidget.Bluetooth is not null);
    await bluetoothWidget.OnActionAsync(new("network.tab.select", "network.tab.bluetooth"));
    Assert.True(!Text(Snapshot(bluetoothWidget, 1).Root, "network.bluetooth.summary").Text!
        .Contains("could not", StringComparison.OrdinalIgnoreCase));
    await Background(bluetoothWidget);

    var pairFake = ReadyHost(WidgetWifiScanState.NotScanned, []);
    pairFake.Bluetooth = new WidgetBluetoothSnapshot(
        WidgetBluetoothRadioState.On, true, WidgetBluetoothDiscoveryState.Ready,
        [new("bluetooth-pair", "Pairing pad", false, false, true)]);
    pairFake.HoldBluetoothPair = true;
    var pairWidget = Create(pairFake);
    await ActivateInteractive(pairWidget);
    await WaitUntil(() => pairWidget.Bluetooth?.Devices.Count == 1);
    await pairWidget.OnActionAsync(new("network.tab.select", "network.tab.bluetooth"));
    var pairRow = Buttons(Snapshot(pairWidget, 1).Root).Single(button =>
        button.ActionId == "bluetooth.device.pair");
    var pairAction = pairWidget.OnActionAsync(new(
        "bluetooth.device.pair", pairRow.Id)).AsTask();
    await pairFake.BluetoothPairStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
    var pairingSnapshot = Snapshot(pairWidget, 2);
    Assert.True(Button(pairingSnapshot.Root, pairRow.Id).IsBusy is true,
        "The in-flight pairing row did not expose busy state on its stable focus ID.");
    Assert.Contains("Pairing Pairing pad",
        Text(pairingSnapshot.Root, "network.bluetooth.summary").Text!);
    await Background(pairWidget);
    await Assert.Canceled(pairAction);
    Assert.Equal(1, pairFake.CanceledBluetoothPairCalls);
    Assert.True(pairWidget.Bluetooth?.Devices.Single().IsPaired is false,
        "A canceled pairing operation optimistically mutated device state.");
    pairFake.HoldBluetoothPair = false;
    await ActivateVisible(pairWidget);
    await WaitUntil(() => pairWidget.Bluetooth?.Devices.Count == 1);
    await pairWidget.OnActionAsync(new("network.tab.select", "network.tab.bluetooth"));
    Assert.True(!Text(Snapshot(pairWidget, 3).Root, "network.bluetooth.summary").Text!
        .Contains("could not", StringComparison.OrdinalIgnoreCase));
    await Background(pairWidget);
}

static async Task ProviderCommandAndRefreshInterleavings()
{
    var fake = ReadyHost(WidgetWifiScanState.Ready,
        [Network("event-network", "Event network", 68, WidgetWifiSecurityKind.Open)]);
    fake.HoldScan = true;
    fake.HoldConnect = true;
    var widget = Create(fake);
    await ActivateInteractive(widget);
    await WaitUntil(() => widget.ViewState == NetworkControlsViewState.Ready &&
                          fake.StatusSubscriptionCount == 1 &&
                          fake.BluetoothSubscriptionCount == 1);

    var scan = widget.OnActionAsync(new("wifi.scan", "network.wifi.scan")).AsTask();
    await fake.ScanStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
    fake.EmitWifi(Wifi(WidgetWifiScanState.Ready,
        Network("event-network", "Event network", 71, WidgetWifiSecurityKind.Open)));
    await WaitUntil(() => widget.ViewState == NetworkControlsViewState.Ready &&
                          !widget.ScanBusy && widget.Networks.Count == 1);
    fake.ScanRelease.TrySetResult();
    await scan;
    Assert.True(!Text(Snapshot(widget, 1).Root, "network.status").Text!
        .Contains("scanning", StringComparison.OrdinalIgnoreCase),
        "A late scan acknowledgement replaced the authoritative Ready event.");

    var row = NetworkButtons(Snapshot(widget, 2).Root).Single();
    var connect = widget.OnActionAsync(new("wifi.connect.item", row.Id)).AsTask();
    await fake.ConnectStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
    fake.EmitStatus(fake.Status with
    {
        Connectivity = WidgetNetworkConnectivity.Internet,
        Transport = WidgetNetworkTransportKind.Wifi,
        ConnectionAttemptState = WidgetNetworkConnectionAttemptState.None,
        AttemptProfileId = null,
        ActiveProfileId = "event-network",
        ActiveProfileName = "Event network",
        SignalPercent = 72,
    });
    fake.EmitWifi(Wifi(WidgetWifiScanState.Ready,
        Network("event-network", "Event network", 72, WidgetWifiSecurityKind.Open,
            connected: true)));
    await WaitUntil(() => widget.Networks.Single().IsConnected && !widget.ControlBusy);
    fake.ConnectRelease.TrySetResult();
    await connect;
    var connected = Snapshot(widget, 3);
    Assert.Equal("CONNECTED", NetworkState(connected.Root, "Event network").Text);
    Assert.True(!Text(connected.Root, "network.status").Text!
        .Contains("waiting", StringComparison.OrdinalIgnoreCase),
        "A late connect acknowledgement replaced the authoritative completion event.");

    await widget.OnActionAsync(new("retry", "network.retry"));
    await WaitUntil(() => fake.StatusCalls == 2 && fake.WifiCalls == 2 &&
                          fake.CanceledStatusSubscriptions == 1 &&
                          fake.CanceledWifiSubscriptions == 1 &&
                          fake.CanceledRadioSubscriptions == 1 &&
                          fake.CanceledBluetoothSubscriptions == 1 &&
                          fake.StatusSubscriptionCount == 2 &&
                          fake.BluetoothSubscriptionCount == 2);
    Assert.Equal(NetworkControlsViewState.Ready, widget.ViewState);
    Assert.Equal("event-network", widget.Networks.Single().NetworkId);
    await Background(widget);
    await WaitUntil(() => fake.CanceledStatusSubscriptions == 2 &&
                          fake.CanceledWifiSubscriptions == 2 &&
                          fake.CanceledRadioSubscriptions == 2 &&
                          fake.CanceledBluetoothSubscriptions == 2);
}

static Task PoliciesArePureAndTyped()
{
    var status = NetworkControlsProviderPolicy.Normalize(new WidgetNetworkStatus(
        WidgetNetworkConnectivity.Local,
        WidgetNetworkTransportKind.Wifi,
        WidgetNetworkWirelessAvailability.Available,
        WidgetNetworkDetailsAccess.Available,
        WidgetNetworkConnectionAttemptState.Connecting,
        "  opaque-b  ", "  opaque-a  ", "  Studio  ", 180));
    Assert.Equal("opaque-b", status.AttemptProfileId);
    Assert.Equal("opaque-a", status.ActiveProfileId);
    Assert.Equal("Studio", status.ActiveProfileName);
    Assert.Equal(100, status.SignalPercent);

    var wifi = NetworkControlsProviderPolicy.Normalize(Wifi(WidgetWifiScanState.Ready,
        Network(" opaque-a ", " Alpha ", 130, WidgetWifiSecurityKind.Open),
        Network("opaque-a", "Duplicate", 20, WidgetWifiSecurityKind.Open),
        Network("opaque-b", "Beta", -4, WidgetWifiSecurityKind.Personal, saved: true)));
    Assert.SequenceEqual(["opaque-a", "opaque-b"], wifi.Networks.Select(item => item.NetworkId));
    Assert.SequenceEqual(["Alpha", "Beta"], wifi.Networks.Select(item => item.DisplayName));
    Assert.SequenceEqual([100, 0], wifi.Networks.Select(item => item.SignalPercent));

    var selection = NetworkControlsProviderPolicy.ReconcileWifiSelection(
        wifi, status, "missing", 0);
    Assert.Equal("opaque-b", selection.Id);
    Assert.Equal(1, selection.Index);
    var command = NetworkControlsProviderPolicy.ReconcileWifiCommand(status, wifi);
    Assert.Equal("opaque-b", command.PendingNetworkId);
    Assert.True(command.ControlBusy);
    Assert.Contains("Beta", command.Status);

    var connectedDevice = new WidgetBluetoothDevice(
        " device-a ", " Controller ", false, true, true);
    var bluetooth = NetworkControlsProviderPolicy.Normalize(new WidgetBluetoothSnapshot(
        WidgetBluetoothRadioState.On, true, WidgetBluetoothDiscoveryState.Ready,
        [connectedDevice, connectedDevice with { DisplayName = "Duplicate" }]));
    Assert.Equal(1, bluetooth.Devices.Count);
    Assert.True(bluetooth.Devices[0].IsPaired);
    Assert.Equal("device-a", bluetooth.Devices[0].DeviceId);

    var unsupported = NetworkControlsCommandPolicy.AdmitConnection(
        Network("secure", "Secure", 40, WidgetWifiSecurityKind.Enterprise), false, false);
    Assert.Equal(NetworkConnectionAdmissionKind.Guidance, unsupported.Kind);
    Assert.True(unsupported.IsError);
    var eligible = NetworkControlsCommandPolicy.AdmitConnection(
        Network("open", "Open", 60, WidgetWifiSecurityKind.Open), false, false);
    Assert.Equal(NetworkConnectionAdmissionKind.Start, eligible.Kind);
    Assert.Contains("Open", eligible.Message);
    Assert.Equal("Another network connection is already in progress",
        NetworkControlsCommandPolicy.MapConnectFailure("provider_busy"));
    return Task.CompletedTask;
}

static Task ActionRoutingIsExact()
{
    var expected = new Dictionary<string, NetworkControlsAction>(StringComparer.Ordinal)
    {
        ["network.tab.select"] = NetworkControlsAction.SelectTab,
        ["network.tab.previous"] = NetworkControlsAction.ToggleTab,
        ["network.tab.next"] = NetworkControlsAction.ToggleTab,
        ["wifi.scan"] = NetworkControlsAction.Scan,
        ["wifi.connect.item"] = NetworkControlsAction.ConnectWifi,
        ["wifi.radio.toggle"] = NetworkControlsAction.ToggleWifiRadio,
        ["bluetooth.radio.toggle"] = NetworkControlsAction.ToggleBluetoothRadio,
        ["bluetooth.device.details"] = NetworkControlsAction.ShowBluetoothDetails,
        ["bluetooth.device.pair"] = NetworkControlsAction.PairBluetooth,
        ["bluetooth.device.manage"] = NetworkControlsAction.ManageBluetooth,
        ["retry"] = NetworkControlsAction.Retry,
    };
    foreach (var pair in expected)
        Assert.Equal(pair.Value, NetworkControlsActionPolicy.Resolve(pair.Key));
    Assert.Equal(NetworkControlsAction.None, NetworkControlsActionPolicy.Resolve("wifi.scan.extra"));
    Assert.Equal(NetworkControlsAction.None, NetworkControlsActionPolicy.Resolve(""));
    return Task.CompletedTask;
}

static Task PresentationIsDeterministic()
{
    var status = new WidgetNetworkStatus(
        WidgetNetworkConnectivity.Internet,
        WidgetNetworkTransportKind.Wifi,
        WidgetNetworkWirelessAvailability.Available,
        WidgetNetworkDetailsAccess.Available,
        WidgetNetworkConnectionAttemptState.None,
        null, "opaque-a", "Studio", 81);
    var wifi = Wifi(WidgetWifiScanState.Ready,
        Network("opaque-a", "Studio", 81, WidgetWifiSecurityKind.Personal,
            saved: true, connected: true),
        Network("opaque-b", "Guest", 52, WidgetWifiSecurityKind.Open));
    var bluetooth = new WidgetBluetoothSnapshot(
        WidgetBluetoothRadioState.On, true, WidgetBluetoothDiscoveryState.Ready,
        [new("device-a", "Controller", true, true, true)]);
    var state = new NetworkControlsPresentationState(
        NetworkControlsViewState.Ready,
        "Internet access · 2 nearby · scan complete",
        false,
        status,
        wifi,
        new WidgetWifiRadio(WidgetWifiRadioState.On, true),
        false,
        false,
        false,
        bluetooth,
        "1 Bluetooth device",
        false,
        false,
        null,
        "device-a",
        null,
        "opaque-b",
        NetworkControlsTab.Wifi,
        true);
    var first = NetworkControlsPresentation.Render(state)
        .CreateSnapshot("network.presentation", 17);
    var second = NetworkControlsPresentation.Render(state)
        .CreateSnapshot("network.presentation", 17);
    Assert.SequenceEqual(SnapshotJson.Serialize(first), SnapshotJson.Serialize(second));
    Assert.Valid(first);
    Assert.Equal(NetworkControlsElementIds.Wifi("opaque-b"), first.InitialFocusId);
    return Task.CompletedTask;
}

static async Task ResponsibilityBoundariesAreSingular()
{
    var project = ProjectDirectory();
    var widget = await File.ReadAllTextAsync(Path.Combine(project, "NetworkControlsWidget.cs"));
    var presentation = await File.ReadAllTextAsync(
        Path.Combine(project, "NetworkControlsPresentation.cs"));
    var provider = await File.ReadAllTextAsync(
        Path.Combine(project, "NetworkControlsProviderPolicy.cs"));
    var command = await File.ReadAllTextAsync(
        Path.Combine(project, "NetworkControlsCommandPolicy.cs"));
    var action = await File.ReadAllTextAsync(
        Path.Combine(project, "NetworkControlsActionPolicy.cs"));

    Assert.Equal(1, CountOccurrences(widget, "private readonly object _stateLock"));
    Assert.Equal(1, CountOccurrences(widget, "private readonly SemaphoreSlim _commandGate"));
    Assert.Equal(1, CountOccurrences(widget, "private long _runGeneration"));
    Assert.Equal(1, CountOccurrences(widget, "Operations.RunLatest("));
    Assert.Equal(0, CountOccurrences(widget, "_runLifetime"));
    Assert.Equal(0, CountOccurrences(widget, "_ = Observe"));
    Assert.Contains("NetworkControlsPresentation.Render(CapturePresentationState())", widget);
    Assert.True(!presentation.Contains("HostServices", StringComparison.Ordinal) &&
                !presentation.Contains("_stateLock", StringComparison.Ordinal) &&
                !presentation.Contains("Operations.", StringComparison.Ordinal),
        "Pure presentation acquired platform, state-lock, or operation ownership.");
    foreach (var policy in new[] { provider, command, action })
    {
        Assert.True(!policy.Contains("HostServices", StringComparison.Ordinal) &&
                    !policy.Contains("_stateLock", StringComparison.Ordinal) &&
                    !policy.Contains("UI.", StringComparison.Ordinal),
            "A policy boundary acquired platform, committed-state, or view ownership.");
    }
}

static async Task ShippedAssetsValidate()
{
    var project = ProjectDirectory();
    var manifest = ManifestJson.Deserialize(await File.ReadAllBytesAsync(Path.Combine(project, "manifest.json")));
    var errors = WidgetManifestValidator.Validate(manifest);
    Assert.True(errors.Count == 0, string.Join(Environment.NewLine, errors));
    Assert.Equal("org.gbar.firstparty.network-controls", manifest.Id);
    Assert.SequenceEqual([
        "system.network.read.v1",
        "system.network.wifi.read.v1",
        "system.network.wifi.radio.read.v1"], manifest.Permissions);
    Assert.SequenceEqual([
        "system.network.wifi.connect.v1",
        "system.network.wifi.radio.control.v1",
        "system.network.bluetooth.read.v1",
        "system.network.bluetooth.radio.control.v1",
        "system.network.bluetooth.pair.v1",
        "system.network.bluetooth.manage.v1"], manifest.OptionalPermissions);
    var residency = WidgetResidencyPolicies.Resolve(manifest);
    Assert.Equal(WidgetResidencyMode.UnloadAfterIdle, residency.Mode);
    Assert.Equal(TimeSpan.FromSeconds(120), residency.IdleDuration);
    Assert.Equal(64, manifest.ResourceRequest.MemoryMb);
    Assert.Equal(10, manifest.ResourceRequest.UpdateHz);

    var package = GbssPackageLoader.LoadFile(
        Path.Combine(project, "styles", "default.gbss"), Path.Combine(project, "styles"));
    var compiled = GbssThemeCompiler.Compile(package);
    Assert.True(compiled.IsValid, string.Join(Environment.NewLine, compiled.Diagnostics));
    AssertResponsiveLayoutBudget(compiled.Theme!);
    var style = await File.ReadAllTextAsync(Path.Combine(project, "styles", "default.gbss"));
    Assert.Contains("width: 100vw", style);
    Assert.Contains("min-width: 0px", style);
    Assert.Contains("max-width: 560px", style);
    Assert.True(!style.Contains("scale:", StringComparison.Ordinal),
        "Full-width Wi-Fi rows may not scale beyond their clipped scroll viewport.");

    var catalogPath = Path.Combine(project, "..", "..", "OverlayHost", "widget-catalog.json");
    using var catalog = JsonDocument.Parse(await File.ReadAllBytesAsync(catalogPath));
    var bundled = catalog.RootElement.GetProperty("bundledWidgets").EnumerateArray().Single(item =>
        item.GetProperty("packageId").GetString() == manifest.Id);
    Assert.Equal("runtime/NetworkControls", bundled.GetProperty("packageRoot").GetString());
    Assert.True(!manifest.Permissions.Concat(manifest.OptionalPermissions)
            .Contains("system.network.saved-profile.switch.v1", StringComparer.Ordinal),
        "Installed Network Controls still grants the legacy saved-profile switch capability.");
    Assert.Equal(0, bundled.GetProperty("quickActions").GetArrayLength());
    Assert.True(!bundled.TryGetProperty("workerExecutable", out _) &&
                !bundled.TryGetProperty("declaredCapabilities", out _) &&
                !bundled.TryGetProperty("residencyPolicy", out _),
        "Bundled runtime authority must be derived from manifest.json, not duplicated in shell metadata.");
}

static void AssertResponsiveLayoutBudget(GbssTheme theme)
{
    var root = Resolve(theme, "stack", "network.root", "network-controls-widget");
    var list = Resolve(theme, "scroll", "network.wifi.body.scroll", "network-view-scroll");
    var row = Resolve(theme, "stack", "network.wifi.test.row", "network-profile-row");
    var button = Resolve(theme, "button", "network.wifi.test", "network-profile-button");
    var scan = Resolve(theme, "button", "network.wifi.scan", "network-scan-action");
    var focused = theme.Resolve(new GbssElement("button", "network.wifi.test",
        new HashSet<string>(["network-profile-button"], StringComparer.Ordinal),
        new HashSet<GbssPseudoState>([GbssPseudoState.Focused])));
    Assert.True(Pixels(button.Get("min-height")!, 320) >= 44);
    Assert.True(Pixels(scan.Get("min-height")!, 320) >= 44);
    Assert.True(Pixels(list.Get("min-height")!, 560) >= 120);
    Assert.Equal("0", row.Get("flex-shrink")!.Text);
    var inset = HorizontalSpacing(list.Get("padding")!, 560) / 2;
    var focusInset = Math.Abs(Pixels(focused.Get("outline-offset")!, 560));
    Assert.True(inset >= focusInset, "Wi-Fi focus outline can clip against the scroll edge.");
    foreach (var viewport in new[] { 280D, 320D, 1280D, 3840D })
    {
        var width = Math.Min(viewport, Math.Clamp(Pixels(root.Get("width")!, viewport),
            Pixels(root.Get("min-width")!, viewport), Pixels(root.Get("max-width")!, viewport)));
        Assert.True(width <= 560 && width <= viewport,
            $"Root width {width}px escaped viewport/max bound at {viewport}px.");
    }
}

static GbssResolvedStyle Resolve(GbssTheme theme, string role, string id, string styleClass) =>
    theme.Resolve(new GbssElement(role, id,
        new HashSet<string>([styleClass], StringComparer.Ordinal)));

static double Pixels(GbssComputedValue value, double viewport)
{
    var unit = value.Unit;
    var number = value.Number;
    if (unit is null)
    {
        unit = value.Text.EndsWith("px", StringComparison.Ordinal) ? "px" :
            value.Text.EndsWith("vw", StringComparison.Ordinal) ? "vw" : null;
        if (unit is not null && double.TryParse(value.Text[..^unit.Length], out var parsed))
            number = parsed;
    }
    return unit switch
    {
        "px" => number ?? throw new InvalidOperationException("Length has no numeric value."),
        "vw" => (number ?? 0) * viewport / 100,
        _ => throw new InvalidOperationException($"Unsupported test length '{value.Text}'."),
    };
}

static double HorizontalSpacing(GbssComputedValue value, double viewport)
{
    var parts = value.Text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
    GbssComputedValue Part(string text)
    {
        var unit = text.EndsWith("px", StringComparison.Ordinal) ? "px" :
            text.EndsWith("vw", StringComparison.Ordinal) ? "vw" : null;
        if (unit is null || !double.TryParse(text[..^unit.Length], out var number))
            throw new InvalidOperationException($"Unsupported spacing '{text}'.");
        return new GbssComputedValue(GbssValueKind.Length, text, number, unit);
    }
    return parts.Length switch
    {
        1 => 2 * Pixels(Part(parts[0]), viewport),
        2 or 3 => 2 * Pixels(Part(parts[1]), viewport),
        4 => Pixels(Part(parts[1]), viewport) + Pixels(Part(parts[3]), viewport),
        _ => throw new InvalidOperationException($"Unsupported spacing list '{value.Text}'."),
    };
}

static FakeNetworkHost ReadyHost(
    WidgetWifiScanState scanState,
    IReadOnlyList<WidgetAvailableWifiNetwork> networks) => new()
{
    Status = Status(WidgetNetworkConnectivity.Internet, WidgetNetworkTransportKind.Ethernet,
        name: "Wired connection"),
    Wifi = new WidgetAvailableWifiNetworks(scanState, networks),
};

static WidgetNetworkStatus Status(
    WidgetNetworkConnectivity connectivity,
    WidgetNetworkTransportKind transport,
    WidgetNetworkWirelessAvailability wireless = WidgetNetworkWirelessAvailability.Available,
    WidgetNetworkDetailsAccess details = WidgetNetworkDetailsAccess.Available,
    string? name = null) => new(connectivity, transport, wireless, details,
        WidgetNetworkConnectionAttemptState.None, null, null, name, null);

static WidgetAvailableWifiNetworks Wifi(
    WidgetWifiScanState state,
    params WidgetAvailableWifiNetwork[] networks) => new(state, networks);

static WidgetAvailableWifiNetwork Network(
    string id,
    string name,
    int signal,
    WidgetWifiSecurityKind security,
    bool credential = false,
    bool connected = false,
    bool saved = false) => new(id, name, signal, security, credential, connected, saved);

static NetworkControlsWidget Create(FakeNetworkHost fake) =>
    WidgetTestHost.Attach(new NetworkControlsWidget(), fake.BuildServices());

static async Task ActivateVisible(NetworkControlsWidget widget) =>
    await WidgetTestHost.SetLifecycleStateAsync(widget, WidgetLifecycleState.Visible);

static async Task ActivateInteractive(NetworkControlsWidget widget) =>
    await WidgetTestHost.SetLifecycleStateAsync(widget, WidgetLifecycleState.Interactive);

static async Task Background(NetworkControlsWidget widget) =>
    await WidgetTestHost.SetLifecycleStateAsync(widget, WidgetLifecycleState.Background);

static async ValueTask<bool> Route(
    NetworkControlsWidget widget,
    ViewSnapshot snapshot,
    ControllerButton button,
    ControllerInputContext context,
    string? focusedElementId = null) =>
    await widget.OnControllerInputAsync(new ControllerInputEvent(button,
        ControllerEventPhase.Pressed, context,
        FocusedElementId: focusedElementId,
        Sequence: 7,
        ActiveInputScopeId: snapshot.ActiveInputScopeId,
        SnapshotSequence: snapshot.Sequence));

static ViewSnapshot Snapshot(NetworkControlsWidget widget, long sequence)
{
    var snapshot = widget.RenderSnapshot("network.test", sequence);
    Assert.Equal(ProtocolConstants.ScrollContainerVersion, snapshot.ProtocolVersion);
    Assert.Equal(WidgetSurfaceMode.Compact, snapshot.Surface!.Mode);
    Assert.Equal(560D, snapshot.Surface.PreferredWidth);
    Assert.Equal(700D, snapshot.Surface.PreferredHeight);
    Assert.Equal(320D, snapshot.Surface.MinimumWidth);
    Assert.Equal(420D, snapshot.Surface.MinimumHeight);
    return snapshot;
}

static IEnumerable<ViewNode> Nodes(ViewNode node)
{
    yield return node;
    foreach (var child in node.Children)
    foreach (var descendant in Nodes(child))
        yield return descendant;
}

static IEnumerable<ViewNode> Buttons(ViewNode root) =>
    Nodes(root).Where(node => node.Kind == ViewNodeKind.Button);

static IEnumerable<ViewNode> NetworkButtons(ViewNode root) =>
    Buttons(root).Where(node => node.ActionId == "wifi.connect.item");

static ViewNode NetworkButton(ViewNode root, string name) =>
    NetworkButtons(root).Single(node => node.Text == name);

static ViewNode NetworkState(ViewNode root, string name)
{
    var button = NetworkButton(root, name);
    return Nodes(root).Single(node => node.Id == $"{button.Id}.state");
}

static ViewNode Button(ViewNode root, string id) =>
    Buttons(root).Single(node => node.Id == id);

static ViewNode Text(ViewNode root, string id) =>
    Nodes(root).Single(node => node.Id == id && node.Kind == ViewNodeKind.Text);

static async Task WaitUntil(Func<bool> condition, int timeoutMilliseconds = 2_000)
{
    var deadline = Environment.TickCount64 + timeoutMilliseconds;
    while (!condition())
    {
        if (Environment.TickCount64 >= deadline)
            throw new TimeoutException("Timed out waiting for asynchronous widget state.");
        await Task.Delay(10);
    }
}

static int CountOccurrences(string value, string fragment)
{
    var count = 0;
    var offset = 0;
    while ((offset = value.IndexOf(fragment, offset, StringComparison.Ordinal)) >= 0)
    {
        count++;
        offset += fragment.Length;
    }
    return count;
}

static string ProjectDirectory()
{
    var current = new DirectoryInfo(AppContext.BaseDirectory);
    while (current is not null)
    {
        var candidate = Path.Combine(current.FullName,
            "src", "FirstPartyWidgets", "NetworkControlsWidget");
        if (Directory.Exists(candidate)) return candidate;
        current = current.Parent;
    }
    throw new DirectoryNotFoundException("Could not locate NetworkControlsWidget project directory.");
}

file sealed class FakeNetworkHost
{
    private readonly object _gate = new();
    private readonly List<Channel<WidgetNetworkStatusChanged>> _statusSubscribers = [];
    private readonly List<Channel<WidgetAvailableWifiNetworksChanged>> _wifiSubscribers = [];
    private readonly List<Channel<WidgetWifiRadioChanged>> _radioSubscribers = [];
    private readonly List<Channel<WidgetBluetoothChanged>> _bluetoothSubscribers = [];
    private int _statusCalls;
    private int _wifiCalls;
    private int _scanCalls;
    private int _connectCalls;
    private int _radioSetCalls;
    private int _bluetoothRadioSetCalls;
    private int _bluetoothPairCalls;
    private int _bluetoothManageCalls;
    private int _statusSubscriptionCount;
    private int _wifiSubscriptionCount;
    private int _radioSubscriptionCount;
    private int _bluetoothSubscriptionCount;
    private int _canceledStatusSubscriptions;
    private int _canceledWifiSubscriptions;
    private int _canceledRadioSubscriptions;
    private int _canceledBluetoothSubscriptions;
    private int _canceledBluetoothPairCalls;

    public WidgetNetworkStatus Status { get; set; } = StatusDefault();
    public WidgetAvailableWifiNetworks Wifi { get; set; } =
        new(WidgetWifiScanState.NotScanned, []);
    public WidgetWifiRadio Radio { get; set; } = new(WidgetWifiRadioState.On, true);
    public WidgetBluetoothSnapshot Bluetooth { get; set; } = new(
        WidgetBluetoothRadioState.On, true, WidgetBluetoothDiscoveryState.Ready, []);
    public Exception? ReadException { get; set; }
    public Exception? ScanException { get; set; }
    public Exception? ConnectException { get; set; }
    public Exception? RadioException { get; set; }
    public Exception? BluetoothException { get; set; }
    public bool HoldScan { get; set; }
    public bool HoldConnect { get; set; }
    public bool HoldWifiRadioSet { get; set; }
    public bool HoldBluetoothRadioSet { get; set; }
    public bool HoldBluetoothPair { get; set; }
    public WidgetBluetoothPairingOutcome BluetoothPairingOutcome { get; set; } =
        WidgetBluetoothPairingOutcome.Paired;
    public TaskCompletionSource WifiRadioSetStarted { get; } =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource ScanStarted { get; } =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource ScanRelease { get; } =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource ConnectStarted { get; } =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource ConnectRelease { get; } =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource BluetoothRadioSetStarted { get; } =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource BluetoothPairStarted { get; } =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    public List<ConnectWidgetAvailableWifiNetworkRequest> ConnectRequests { get; } = [];
    public int StatusCalls => Volatile.Read(ref _statusCalls);
    public int WifiCalls => Volatile.Read(ref _wifiCalls);
    public int ScanCalls => Volatile.Read(ref _scanCalls);
    public int ConnectCalls => Volatile.Read(ref _connectCalls);
    public int RadioSetCalls => Volatile.Read(ref _radioSetCalls);
    public int BluetoothRadioSetCalls => Volatile.Read(ref _bluetoothRadioSetCalls);
    public int BluetoothPairCalls => Volatile.Read(ref _bluetoothPairCalls);
    public int BluetoothManageCalls => Volatile.Read(ref _bluetoothManageCalls);
    public string? LastBluetoothDeviceId { get; private set; }
    public int StatusSubscriptionCount => Volatile.Read(ref _statusSubscriptionCount);
    public int WifiSubscriptionCount => Volatile.Read(ref _wifiSubscriptionCount);
    public int RadioSubscriptionCount => Volatile.Read(ref _radioSubscriptionCount);
    public int BluetoothSubscriptionCount => Volatile.Read(ref _bluetoothSubscriptionCount);
    public int CanceledStatusSubscriptions => Volatile.Read(ref _canceledStatusSubscriptions);
    public int CanceledWifiSubscriptions => Volatile.Read(ref _canceledWifiSubscriptions);
    public int CanceledRadioSubscriptions => Volatile.Read(ref _canceledRadioSubscriptions);
    public int CanceledBluetoothSubscriptions => Volatile.Read(ref _canceledBluetoothSubscriptions);
    public int CanceledBluetoothPairCalls => Volatile.Read(ref _canceledBluetoothPairCalls);

    public WidgetHostServices BuildServices() => new WidgetTestHostServicesBuilder()
        .WithHandler(WidgetNetworkCapabilities.GetStatus, GetStatusAsync)
        .WithHandler(WidgetNetworkCapabilities.GetAvailableWifi, GetWifiAsync)
        .WithHandler(WidgetNetworkCapabilities.RequestWifiScan, ScanAsync)
        .WithHandler(WidgetNetworkCapabilities.ConnectAvailableWifi, ConnectAsync)
        .WithHandler(WidgetNetworkCapabilities.GetWifiRadio, GetRadioAsync)
        .WithHandler(WidgetNetworkCapabilities.SetWifiRadio, SetRadioAsync)
        .WithHandler(WidgetNetworkCapabilities.GetBluetooth, GetBluetoothAsync)
        .WithHandler(WidgetNetworkCapabilities.SetBluetoothRadio, SetBluetoothRadioAsync)
        .WithHandler(WidgetNetworkCapabilities.PairBluetoothDevice, PairBluetoothDeviceAsync)
        .WithHandler(WidgetNetworkCapabilities.OpenBluetoothDeviceSettings,
            OpenBluetoothDeviceSettingsAsync)
        .WithEventStream(WidgetNetworkCapabilities.StatusChanged, OpenStatusStream)
        .WithEventStream(WidgetNetworkCapabilities.AvailableWifiChanged, OpenWifiStream)
        .WithEventStream(WidgetNetworkCapabilities.WifiRadioChanged, OpenRadioStream)
        .WithEventStream(WidgetNetworkCapabilities.BluetoothChanged, OpenBluetoothStream)
        .Build();

    private ValueTask<WidgetNetworkStatus> GetStatusAsync(
        WidgetCapabilityQuery request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Interlocked.Increment(ref _statusCalls);
        if (ReadException is not null) throw ReadException;
        return ValueTask.FromResult(Status);
    }

    private ValueTask<WidgetAvailableWifiNetworks> GetWifiAsync(
        WidgetCapabilityQuery request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Interlocked.Increment(ref _wifiCalls);
        if (ReadException is not null) throw ReadException;
        return ValueTask.FromResult(Wifi with { Networks = Wifi.Networks.ToArray() });
    }

    private ValueTask<WidgetWifiRadio> GetRadioAsync(
        WidgetCapabilityQuery request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (ReadException is not null) throw ReadException;
        return ValueTask.FromResult(Radio);
    }

    private async ValueTask<WidgetCapabilityAcknowledgement> SetRadioAsync(
        SetWidgetWifiRadioRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Interlocked.Increment(ref _radioSetCalls);
        WifiRadioSetStarted.TrySetResult();
        if (HoldWifiRadioSet)
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        if (RadioException is not null) throw RadioException;
        EmitRadio(new WidgetWifiRadio(
            request.Enabled ? WidgetWifiRadioState.On : WidgetWifiRadioState.Off, true));
        return new WidgetCapabilityAcknowledgement(true);
    }

    private ValueTask<WidgetBluetoothSnapshot> GetBluetoothAsync(
        WidgetCapabilityQuery request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (BluetoothException is not null) throw BluetoothException;
        return ValueTask.FromResult(Bluetooth with { Devices = Bluetooth.Devices.ToArray() });
    }

    private async ValueTask<WidgetCapabilityAcknowledgement> SetBluetoothRadioAsync(
        SetWidgetBluetoothRadioRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Interlocked.Increment(ref _bluetoothRadioSetCalls);
        BluetoothRadioSetStarted.TrySetResult();
        if (HoldBluetoothRadioSet)
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        if (BluetoothException is not null) throw BluetoothException;
        EmitBluetooth(Bluetooth with
        {
            RadioState = request.Enabled
                ? WidgetBluetoothRadioState.On
                : WidgetBluetoothRadioState.Off,
        });
        return new WidgetCapabilityAcknowledgement(true);
    }

    private async ValueTask<WidgetBluetoothPairingResult> PairBluetoothDeviceAsync(
        PairWidgetBluetoothDeviceRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Interlocked.Increment(ref _bluetoothPairCalls);
        LastBluetoothDeviceId = request.DeviceId;
        BluetoothPairStarted.TrySetResult();
        if (HoldBluetoothPair)
        {
            try { await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken); }
            catch (OperationCanceledException)
            {
                Interlocked.Increment(ref _canceledBluetoothPairCalls);
                throw;
            }
        }
        if (BluetoothException is not null) throw BluetoothException;
        if (BluetoothPairingOutcome is WidgetBluetoothPairingOutcome.Paired or
            WidgetBluetoothPairingOutcome.AlreadyPaired)
        {
            EmitBluetooth(Bluetooth with
            {
                Devices = Bluetooth.Devices.Select(device => string.Equals(
                        device.DeviceId, request.DeviceId, StringComparison.Ordinal)
                    ? device with { IsPaired = true }
                    : device).ToArray(),
            });
        }
        return new WidgetBluetoothPairingResult(BluetoothPairingOutcome);
    }

    private ValueTask<WidgetCapabilityAcknowledgement> OpenBluetoothDeviceSettingsAsync(
        OpenWidgetBluetoothDeviceSettingsRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Interlocked.Increment(ref _bluetoothManageCalls);
        LastBluetoothDeviceId = request.DeviceId;
        if (BluetoothException is not null) throw BluetoothException;
        return ValueTask.FromResult(new WidgetCapabilityAcknowledgement(true));
    }

    private async ValueTask<WidgetCapabilityAcknowledgement> ScanAsync(
        WidgetCapabilityQuery request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Interlocked.Increment(ref _scanCalls);
        ScanStarted.TrySetResult();
        if (HoldScan)
            await ScanRelease.Task.WaitAsync(cancellationToken);
        if (ScanException is not null) throw ScanException;
        return new WidgetCapabilityAcknowledgement(true);
    }

    private async ValueTask<WidgetCapabilityAcknowledgement> ConnectAsync(
        ConnectWidgetAvailableWifiNetworkRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Interlocked.Increment(ref _connectCalls);
        lock (_gate) ConnectRequests.Add(request);
        ConnectStarted.TrySetResult();
        if (HoldConnect)
            await ConnectRelease.Task.WaitAsync(cancellationToken);
        if (ConnectException is not null) throw ConnectException;
        return new WidgetCapabilityAcknowledgement(true);
    }

    private IAsyncEnumerable<WidgetNetworkStatusChanged> OpenStatusStream(
        CancellationToken cancellationToken)
    {
        var channel = NewChannel<WidgetNetworkStatusChanged>();
        lock (_gate) _statusSubscribers.Add(channel);
        Interlocked.Increment(ref _statusSubscriptionCount);
        return ReadStatusEvents(channel, cancellationToken);
    }

    private IAsyncEnumerable<WidgetAvailableWifiNetworksChanged> OpenWifiStream(
        CancellationToken cancellationToken)
    {
        var channel = NewChannel<WidgetAvailableWifiNetworksChanged>();
        lock (_gate) _wifiSubscribers.Add(channel);
        Interlocked.Increment(ref _wifiSubscriptionCount);
        return ReadWifiEvents(channel, cancellationToken);
    }

    private IAsyncEnumerable<WidgetWifiRadioChanged> OpenRadioStream(
        CancellationToken cancellationToken)
    {
        var channel = NewChannel<WidgetWifiRadioChanged>();
        lock (_gate) _radioSubscribers.Add(channel);
        Interlocked.Increment(ref _radioSubscriptionCount);
        return ReadRadioEvents(channel, cancellationToken);
    }

    private IAsyncEnumerable<WidgetBluetoothChanged> OpenBluetoothStream(
        CancellationToken cancellationToken)
    {
        if (BluetoothException is not null) throw BluetoothException;
        var channel = NewChannel<WidgetBluetoothChanged>();
        lock (_gate) _bluetoothSubscribers.Add(channel);
        Interlocked.Increment(ref _bluetoothSubscriptionCount);
        return ReadBluetoothEvents(channel, cancellationToken);
    }

    private static Channel<T> NewChannel<T>() => Channel.CreateBounded<T>(
        new BoundedChannelOptions(4)
        {
            SingleReader = true,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.DropOldest,
        });

    private async IAsyncEnumerable<WidgetNetworkStatusChanged> ReadStatusEvents(
        Channel<WidgetNetworkStatusChanged> channel,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var item in channel.Reader.ReadAllAsync(cancellationToken))
                yield return item;
        }
        finally
        {
            lock (_gate) _statusSubscribers.Remove(channel);
            Interlocked.Increment(ref _canceledStatusSubscriptions);
        }
    }

    private async IAsyncEnumerable<WidgetAvailableWifiNetworksChanged> ReadWifiEvents(
        Channel<WidgetAvailableWifiNetworksChanged> channel,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var item in channel.Reader.ReadAllAsync(cancellationToken))
                yield return item;
        }
        finally
        {
            lock (_gate) _wifiSubscribers.Remove(channel);
            Interlocked.Increment(ref _canceledWifiSubscriptions);
        }
    }

    private async IAsyncEnumerable<WidgetWifiRadioChanged> ReadRadioEvents(
        Channel<WidgetWifiRadioChanged> channel,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var item in channel.Reader.ReadAllAsync(cancellationToken))
                yield return item;
        }
        finally
        {
            lock (_gate) _radioSubscribers.Remove(channel);
            Interlocked.Increment(ref _canceledRadioSubscriptions);
        }
    }

    private async IAsyncEnumerable<WidgetBluetoothChanged> ReadBluetoothEvents(
        Channel<WidgetBluetoothChanged> channel,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var item in channel.Reader.ReadAllAsync(cancellationToken))
                yield return item;
        }
        finally
        {
            lock (_gate) _bluetoothSubscribers.Remove(channel);
            Interlocked.Increment(ref _canceledBluetoothSubscriptions);
        }
    }

    public void EmitStatus(WidgetNetworkStatus status)
    {
        Status = status;
        Channel<WidgetNetworkStatusChanged>[] subscribers;
        lock (_gate) subscribers = _statusSubscribers.ToArray();
        foreach (var subscriber in subscribers)
            subscriber.Writer.TryWrite(new WidgetNetworkStatusChanged(status));
    }

    public void EmitWifi(WidgetAvailableWifiNetworks snapshot)
    {
        Wifi = snapshot;
        Channel<WidgetAvailableWifiNetworksChanged>[] subscribers;
        lock (_gate) subscribers = _wifiSubscribers.ToArray();
        foreach (var subscriber in subscribers)
            subscriber.Writer.TryWrite(new WidgetAvailableWifiNetworksChanged(snapshot));
    }

    public void EmitRadio(WidgetWifiRadio radio)
    {
        Radio = radio;
        Channel<WidgetWifiRadioChanged>[] subscribers;
        lock (_gate) subscribers = _radioSubscribers.ToArray();
        foreach (var subscriber in subscribers)
            subscriber.Writer.TryWrite(new WidgetWifiRadioChanged(radio));
    }

    public void EmitBluetooth(WidgetBluetoothSnapshot snapshot)
    {
        Bluetooth = snapshot;
        Channel<WidgetBluetoothChanged>[] subscribers;
        lock (_gate) subscribers = _bluetoothSubscribers.ToArray();
        foreach (var subscriber in subscribers)
            subscriber.Writer.TryWrite(new WidgetBluetoothChanged(snapshot));
    }

    private static WidgetNetworkStatus StatusDefault() => new(
        WidgetNetworkConnectivity.None,
        WidgetNetworkTransportKind.None,
        WidgetNetworkWirelessAvailability.Available,
        WidgetNetworkDetailsAccess.Available,
        WidgetNetworkConnectionAttemptState.None,
        null, null, null, null);
}

file static class Assert
{
    public static void True(bool condition, string message = "Expected condition to be true.")
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    public static void Equal<T>(T expected, T actual)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException($"Expected '{expected}', got '{actual}'.");
    }

    public static void Contains(string expected, string actual)
    {
        if (!actual.Contains(expected, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Expected '{actual}' to contain '{expected}'.");
    }

    public static void SequenceEqual<T>(IEnumerable<T> expected, IEnumerable<T> actual)
    {
        if (!expected.SequenceEqual(actual))
            throw new InvalidOperationException(
                $"Expected [{string.Join(", ", expected)}], got [{string.Join(", ", actual)}].");
    }

    public static void Valid(ViewSnapshot snapshot)
    {
        var errors = ViewSnapshotValidator.Validate(snapshot);
        if (errors.Count != 0)
            throw new InvalidOperationException(string.Join(Environment.NewLine, errors));
    }

    public static async Task Canceled(Task action)
    {
        try { await action; }
        catch (OperationCanceledException) { return; }
        throw new InvalidOperationException("Expected the lifecycle-bound action to be canceled.");
    }
}
