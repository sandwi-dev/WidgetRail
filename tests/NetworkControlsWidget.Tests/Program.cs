using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading.Channels;
using GameBarAlternative.FirstPartyWidgets.NetworkControls;
using GameBarAlternative.WidgetProtocol;
using GameBarAlternative.WidgetSdk;
using GameBarAlternative.WidgetStyling;

var tests = new (string Name, Func<Task> Run)[]
{
    ("Connection and empty-profile surfaces remain orthogonal", ConnectionSurfaces),
    ("Radio privacy adapter and service states are controller readable", WirelessStates),
    ("Unavailable Wi-Fi disables connect without invoking the broker", UnavailableWifiBlocksConnect),
    ("D-pad and analog focus graph is explicit for every network control", ExplicitFocusGraph),
    ("Dashboard routes only local read actions and open routes control", ControllerRoutes),
    ("Switch acknowledgement waits for authoritative status completion", AcceptedSwitchWaitsForEvent),
    ("Terminal and immediate switch failures roll back safely", SwitchFailureRollback),
    ("Saved-profile churn preserves identity selection", StableSelectionDuringChurn),
    ("Focused A and X route the exact network row", FocusedProfileRoutes),
    ("Large saved-profile lists stay bounded and identity stable", LargeProfileList),
    ("Capability failures render bounded recovery surfaces", CapabilityFailureStates),
    ("Unavailable host service fails closed without platform fallback", UnavailableService),
    ("Visible lifecycle subscribes once and never polls", LifecycleAndNoPolling),
    ("Acknowledged subscription closes the snapshot event gap", SubscriptionPrecedesSnapshot),
    ("Lifecycle cancellation restores authoritative network state", CancellationRollback),
    ("Manifest permissions and responsive GBSS validate", ShippedAssetsValidate),
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
        failures.Add($"FAIL {test.Name}: {exception.Message}");
        Console.Error.WriteLine(failures[^1]);
    }
}

Console.WriteLine($"{tests.Length - failures.Count}/{tests.Length} tests passed.");
return failures.Count == 0 ? 0 : 1;

static async Task ConnectionSurfaces()
{
    var ethernet = new FakeNetworkHost
    {
        Status = Status(
            WidgetNetworkConnectivity.Internet,
            WidgetNetworkTransportKind.Ethernet,
            WidgetNetworkWirelessAvailability.RadioOff,
            name: "Studio LAN"),
    };
    var widget = Create(ethernet);
    Assert.Equal(NetworkControlsViewState.Initial, widget.ViewState);
    Assert.Valid(Snapshot(widget, 0));
    await ActivateVisible(widget);
    await WaitUntil(() => widget.ViewState == NetworkControlsViewState.EmptyProfiles);
    var wired = Snapshot(widget, 1);
    Assert.Equal("Studio LAN", Text(wired.Root, "network.connection.title").Text);
    Assert.Equal("ETHERNET", Text(wired.Root, "network.connection.transport").Text);
    Assert.Contains("RADIO OFF", Text(wired.Root, "network.wifi.note.title").Text!);
    Assert.Equal("network.retry", wired.InitialFocusId);
    Assert.True(!Buttons(wired.Root).Any(button => button.ActionId == "profile.connect"),
        "An empty saved-profile surface offered a connect action.");
    Assert.Valid(wired);

    ethernet.Emit(
        Status(WidgetNetworkConnectivity.Internet, WidgetNetworkTransportKind.Wifi,
            activeId: "home", name: "Home 5G", signal: 87),
        [Profile("home", "Home 5G", connected: true, signal: 87)]);
    await WaitUntil(() => widget.ViewState == NetworkControlsViewState.Ready);
    var wifi = Snapshot(widget, 2);
    Assert.Equal("WI-FI", Text(wifi.Root, "network.connection.transport").Text);
    Assert.Equal("87%", Text(wifi.Root, "network.signal.value").Text);
    var connected = ProfileButton(wifi.Root, "Home 5G");
    Assert.Equal(true, connected.IsSelected);
    Assert.Equal(WidgetGlyph.Check, connected.Glyph);
    Assert.Valid(wifi);
    await Background(widget);
}

static async Task WirelessStates()
{
    var cases = new[]
    {
        (Status(WidgetNetworkConnectivity.None, WidgetNetworkTransportKind.None,
                WidgetNetworkWirelessAvailability.RadioOff),
            NetworkControlsViewState.RadioOff, "RADIO OFF"),
        (Status(WidgetNetworkConnectivity.None, WidgetNetworkTransportKind.None,
                WidgetNetworkWirelessAvailability.NoAdapter),
            NetworkControlsViewState.Offline, "NO WI-FI ADAPTER"),
        (Status(WidgetNetworkConnectivity.None, WidgetNetworkTransportKind.None,
                WidgetNetworkWirelessAvailability.ServiceUnavailable),
            NetworkControlsViewState.WirelessUnavailable, "SERVICE UNAVAILABLE"),
        (Status(WidgetNetworkConnectivity.Internet, WidgetNetworkTransportKind.Ethernet,
                details: WidgetNetworkDetailsAccess.PrivacyRestricted,
                name: "Wired network"),
            NetworkControlsViewState.EmptyProfiles, "DETAILS HIDDEN"),
    };

    foreach (var (status, expectedState, expectedNote) in cases)
    {
        var fake = new FakeNetworkHost { Status = status };
        if (status.WirelessAvailability == WidgetNetworkWirelessAvailability.RadioOff)
            fake.Profiles = [Profile("home", "Home")];
        var widget = Create(fake);
        await ActivateVisible(widget);
        await WaitUntil(() => widget.ViewState == expectedState);
        var snapshot = Snapshot(widget, 1);
        Assert.Contains(expectedNote, Text(snapshot.Root, "network.wifi.note.title").Text!);
        Assert.True(!Buttons(snapshot.Root).Any(button =>
                button.ActionId?.Contains("privacy", StringComparison.OrdinalIgnoreCase) == true ||
                button.ActionId?.Contains("access", StringComparison.OrdinalIgnoreCase) == true),
            "Privacy-restricted status offered an unbrokered prompt action.");
        Assert.Valid(snapshot);
        await Background(widget);
    }
}

static async Task UnavailableWifiBlocksConnect()
{
    var cases = new[]
    {
        (WidgetNetworkWirelessAvailability.RadioOff, "Wi-Fi is off", "Turn Wi-Fi on"),
        (WidgetNetworkWirelessAvailability.NoAdapter, "No Wi-Fi adapter", "No Wi-Fi adapter"),
        (WidgetNetworkWirelessAvailability.ServiceUnavailable, "Wi-Fi unavailable", "wireless service is unavailable"),
    };

    foreach (var (availability, expectedButton, expectedStatus) in cases)
    {
        var fake = new FakeNetworkHost
        {
            Status = Status(
                WidgetNetworkConnectivity.Internet,
                WidgetNetworkTransportKind.Ethernet,
                availability),
            Profiles = [Profile("home", "Home")],
        };
        var widget = Create(fake);
        await ActivateInteractive(widget);
        await WaitUntil(() => widget.Profiles.Count == 1);

        var snapshot = Snapshot(widget, 1);
        var connect = ProfileButton(snapshot.Root, "Home");
        Assert.Equal(true, connect.IsDisabled);
        Assert.Contains(expectedButton, connect.AccessibilityLabel!);

        await widget.OnActionAsync(new("profile.connect.item", connect.Id));
        Assert.Equal(0, fake.SwitchCalls);
        Assert.Contains(expectedStatus, Text(Snapshot(widget, 2).Root, "network.status").Text!);

        _ = await Route(widget, Snapshot(widget, 3), ControllerButton.X,
            ControllerInputContext.OpenWidget);
        Assert.Equal(0, fake.SwitchCalls);
        Assert.Contains(expectedStatus, Text(Snapshot(widget, 4).Root, "network.status").Text!);
        await Background(widget);
    }
}

static async Task ExplicitFocusGraph()
{
    var fake = ReadyHost();
    var widget = Create(fake);
    await ActivateInteractive(widget);
    await WaitUntil(() => widget.ViewState == NetworkControlsViewState.Ready);
    var snapshot = Snapshot(widget, 1);
    var profileButtons = ProfileButtons(snapshot.Root).ToArray();
    Assert.Equal(2, profileButtons.Length);
    Assert.Equal(ViewNodeKind.Scroll, Node(snapshot.Root, "network.profiles.scroll").Kind);
    Assert.Equal(profileButtons[0].Id, snapshot.InitialFocusId);
    foreach (var button in profileButtons)
    {
        Assert.True(button.Focus is { Up: not null, Down: not null, Left: not null, Right: not null },
            $"'{button.Id}' does not have an explicit four-way focus graph.");
        foreach (var neighbor in new[] { button.Focus!.Up!, button.Focus.Down! })
            Assert.True(profileButtons.Any(candidate => candidate.Id == neighbor),
                $"'{button.Id}' points to missing focus target '{neighbor}'.");
        Assert.Equal(button.Id, button.Focus.Left);
        Assert.Equal(button.Id, button.Focus.Right);
        Assert.True(button.Shortcuts.Any(shortcut =>
                shortcut.Button == ControllerButton.X &&
                shortcut.ActionId == "profile.connect.item"),
            $"'{button.Id}' does not own its focused X shortcut.");
    }
    Assert.Valid(snapshot);
    await Background(widget);
}

static async Task ControllerRoutes()
{
    var fake = ReadyHost(allDisconnected: true);
    var widget = Create(fake);
    await ActivateVisible(widget);
    await WaitUntil(() => widget.ViewState == NetworkControlsViewState.Ready);
    var visible = Snapshot(widget, 1);
    Assert.Equal(0, visible.QuickActions.Count);
    Assert.True(!visible.Root.Shortcuts.Any(shortcut =>
            shortcut.Button is ControllerButton.B or ControllerButton.Y or
                ControllerButton.DPadUp or ControllerButton.DPadDown or
                ControllerButton.DPadLeft or ControllerButton.DPadRight),
        "The open surface captured host close/reorder/navigation input.");

    Assert.True(!await Route(widget, visible, ControllerButton.RightBumper,
        ControllerInputContext.DashboardQuickAction));
    Assert.Equal("home", widget.SelectedProfileId);
    Assert.Equal(0, fake.SwitchCalls);

    await WidgetTestHost.SetLifecycleStateAsync(widget, WidgetLifecycleState.Interactive);
    var open = Snapshot(widget, 2);
    Assert.True(!await Route(widget, open, ControllerButton.LeftBumper,
        ControllerInputContext.OpenWidget, ProfileButton(open.Root, "Office").Id));
    Assert.Equal("home", widget.SelectedProfileId);
    Assert.True(await Route(widget, open, ControllerButton.X,
        ControllerInputContext.OpenWidget, ProfileButton(open.Root, "Office").Id));
    await WaitUntil(() => fake.SwitchCalls == 1);
    Assert.Equal("office", fake.SwitchRequests.Single().ProfileId);
    Assert.True(widget.ControlBusy, "Accepted switch should wait for a terminal status event.");
    fake.Emit(Status(WidgetNetworkConnectivity.Internet, WidgetNetworkTransportKind.Wifi,
            activeId: "office", name: "Office", signal: 80),
        [Profile("home", "Home", signal: 80), Profile("office", "Office", connected: true)]);
    await WaitUntil(() => !widget.ControlBusy);
    await Background(widget);
}

static async Task AcceptedSwitchWaitsForEvent()
{
    var fake = ReadyHost();
    var widget = Create(fake);
    await ActivateInteractive(widget);
    await WaitUntil(() => widget.ViewState == NetworkControlsViewState.Ready);
    var ready = Snapshot(widget, 0);
    await widget.OnActionAsync(new("profile.connect.item", ProfileButton(ready.Root, "Office").Id));
    Assert.Equal(1, fake.SwitchCalls);
    Assert.True(widget.ControlBusy, "Broker ACK was incorrectly treated as connection success.");
    Assert.Equal("home", widget.NetworkStatus!.ActiveProfileId);
    Assert.Equal(false, widget.Profiles.Single(profile => profile.ProfileId == "office").IsConnected);
    Assert.Contains("waiting for Windows", Text(Snapshot(widget, 1).Root, "network.status").Text!);

    fake.Emit(Status(WidgetNetworkConnectivity.Internet, WidgetNetworkTransportKind.Wifi,
            attempt: WidgetNetworkConnectionAttemptState.Connecting,
            attemptId: "office", activeId: "home", name: "Home", signal: 78),
        fake.Profiles);
    await WaitUntil(() => widget.NetworkStatus?.ConnectionAttemptState ==
                          WidgetNetworkConnectionAttemptState.Connecting);
    Assert.Equal("office", widget.SelectedProfileId);
    var connecting = ProfileButton(Snapshot(widget, 2).Root, "Office");
    Assert.True(connecting.StyleClasses.Contains("is-pending"),
        "Pending profile did not expose a stable focus-preserving visual state.");
    Assert.Equal(null, connecting.IsBusy);
    Assert.Equal(null, connecting.IsDisabled);

    fake.Emit(Status(WidgetNetworkConnectivity.Internet, WidgetNetworkTransportKind.Wifi,
            activeId: "office", name: "Office", signal: 68),
        [Profile("home", "Home", signal: 78), Profile("office", "Office", connected: true, signal: 68)]);
    await WaitUntil(() => !widget.ControlBusy && widget.NetworkStatus?.ActiveProfileId == "office");
    var connected = ProfileButton(Snapshot(widget, 3).Root, "Office");
    Assert.Equal(true, connected.IsSelected);
    Assert.Equal(WidgetGlyph.Check, connected.Glyph);
    await Background(widget);
}

static async Task SwitchFailureRollback()
{
    var immediate = ReadyHost();
    immediate.SwitchException = new WidgetCapabilityException("permission_denied", "private detail");
    var widget = Create(immediate);
    await ActivateInteractive(widget);
    await WaitUntil(() => widget.ViewState == NetworkControlsViewState.Ready);
    var ready = Snapshot(widget, 0);
    await widget.OnActionAsync(new("profile.connect.item", ProfileButton(ready.Root, "Office").Id));
    Assert.True(!widget.ControlBusy, "Immediate denial left the controller surface busy.");
    Assert.Equal("home", widget.NetworkStatus!.ActiveProfileId);
    var denied = Text(Snapshot(widget, 1).Root, "network.status").Text!;
    Assert.Contains("permission denied", denied);
    Assert.True(!denied.Contains("private detail", StringComparison.Ordinal),
        "Broker-private details leaked into widget feedback.");
    await Background(widget);

    var terminal = ReadyHost();
    var terminalWidget = Create(terminal);
    await ActivateInteractive(terminalWidget);
    await WaitUntil(() => terminalWidget.ViewState == NetworkControlsViewState.Ready);
    var terminalReady = Snapshot(terminalWidget, 0);
    await terminalWidget.OnActionAsync(new(
        "profile.connect.item", ProfileButton(terminalReady.Root, "Office").Id));
    terminal.Emit(Status(WidgetNetworkConnectivity.Internet, WidgetNetworkTransportKind.Wifi,
            attempt: WidgetNetworkConnectionAttemptState.Failed,
            attemptId: "office", activeId: "home", name: "Home", signal: 80),
        terminal.Profiles);
    await WaitUntil(() => !terminalWidget.ControlBusy &&
                          terminalWidget.NetworkStatus?.ConnectionAttemptState ==
                          WidgetNetworkConnectionAttemptState.Failed);
    Assert.Equal("home", terminalWidget.NetworkStatus!.ActiveProfileId);
    Assert.Contains("Could not connect to Office", Text(Snapshot(terminalWidget, 2).Root, "network.status").Text!);
    await Background(terminalWidget);
}

static async Task StableSelectionDuringChurn()
{
    var fake = new FakeNetworkHost
    {
        Status = Status(WidgetNetworkConnectivity.Internet, WidgetNetworkTransportKind.Wifi,
            activeId: "a", name: "Alpha", signal: 70),
        Profiles = [Profile("a", "Alpha", true), Profile("b", "Beta"), Profile("c", "Gamma")],
    };
    var widget = Create(fake);
    await ActivateVisible(widget);
    await WaitUntil(() => widget.ViewState == NetworkControlsViewState.Ready);
    var initial = Snapshot(widget, 0);
    await widget.OnActionAsync(new("profile.connect.item", ProfileButton(initial.Root, "Beta").Id));
    Assert.Equal("b", widget.SelectedProfileId);

    fake.Emit(fake.Status,
        [Profile("c", "Gamma"), Profile("b", "Beta renamed"), Profile("d", "Delta")]);
    await WaitUntil(() => widget.Profiles.First().ProfileId == "c");
    Assert.Equal("b", widget.SelectedProfileId);
    var retained = Snapshot(widget, 1);
    Assert.Equal(true, ProfileButton(retained.Root, "Beta renamed").IsSelected);

    fake.Emit(fake.Status, [Profile("c", "Gamma"), Profile("d", "Delta")]);
    await WaitUntil(() => widget.Profiles.Count == 2);
    Assert.Equal("d", widget.SelectedProfileId);
    var fallback = Snapshot(widget, 2);
    Assert.Equal(ProfileButton(fallback.Root, "Delta").Id, fallback.InitialFocusId);
    await Background(widget);
}

static async Task FocusedProfileRoutes()
{
    var fake = ReadyHost(allDisconnected: true);
    var widget = Create(fake);
    await ActivateInteractive(widget);
    await WaitUntil(() => widget.ViewState == NetworkControlsViewState.Ready);

    var snapshot = Snapshot(widget, 1);
    var office = ProfileButton(snapshot.Root, "Office");
    Assert.True(await Route(widget, snapshot, ControllerButton.A,
        ControllerInputContext.OpenWidget, office.Id));
    await WaitUntil(() => fake.SwitchCalls == 1);
    Assert.Equal("office", fake.SwitchRequests.Single().ProfileId);
    Assert.Equal("office", widget.SelectedProfileId);

    fake.Emit(Status(WidgetNetworkConnectivity.Internet, WidgetNetworkTransportKind.Wifi,
            activeId: "office", name: "Office", signal: 66),
        [Profile("home", "Home", signal: 80), Profile("office", "Office", true, 66)]);
    await WaitUntil(() => !widget.ControlBusy);
    var connected = Snapshot(widget, 2);
    Assert.True(await Route(widget, connected, ControllerButton.X,
        ControllerInputContext.OpenWidget, ProfileButton(connected.Root, "Home").Id));
    await WaitUntil(() => fake.SwitchCalls == 2);
    Assert.Equal("home", fake.SwitchRequests.Last().ProfileId);
    await Background(widget);
}

static async Task LargeProfileList()
{
    var profiles = Enumerable.Range(0, 128)
        .Select(index => Profile($"profile-{index:D3}", $"Saved network {index:D3}",
            connected: index == 73, signal: index % 101))
        .ToArray();
    var fake = new FakeNetworkHost
    {
        Status = Status(WidgetNetworkConnectivity.Internet, WidgetNetworkTransportKind.Wifi,
            activeId: profiles[73].ProfileId, name: profiles[73].DisplayName, signal: 73),
        Profiles = profiles,
    };
    var widget = Create(fake);
    await ActivateInteractive(widget);
    await WaitUntil(() => widget.Profiles.Count == profiles.Length);

    var snapshot = Snapshot(widget, 1);
    var buttons = ProfileButtons(snapshot.Root).ToArray();
    Assert.Equal(128, buttons.Length);
    Assert.Equal(128, buttons.Select(button => button.Id).Distinct(StringComparer.Ordinal).Count());
    Assert.Equal(ProfileButton(snapshot.Root, profiles[73].DisplayName).Id, snapshot.InitialFocusId);
    Assert.Equal(ViewNodeKind.Scroll, Node(snapshot.Root, "network.profiles.scroll").Kind);
    Assert.Equal(buttons[0].Id, buttons[0].Focus!.Up);
    Assert.Equal(buttons[^1].Id, buttons[^1].Focus!.Down);

    var retainedId = ProfileButton(snapshot.Root, profiles[73].DisplayName).Id;
    fake.Emit(fake.Status, profiles.Reverse().ToArray());
    await WaitUntil(() => widget.Profiles[0].ProfileId == profiles[^1].ProfileId);
    var reordered = Snapshot(widget, 2);
    Assert.Equal(retainedId, ProfileButton(reordered.Root, profiles[73].DisplayName).Id);
    Assert.Equal(retainedId, reordered.InitialFocusId);
    Assert.Valid(reordered);
    await Background(widget);
}

static async Task CapabilityFailureStates()
{
    var cases = new[]
    {
        ("permission_denied", NetworkControlsViewState.PermissionDenied, "Network access is off"),
        ("capability_revoked", NetworkControlsViewState.PermissionDenied, "Network access is off"),
        ("lifecycle_denied", NetworkControlsViewState.LifecycleDenied, "paused by lifecycle"),
        ("channel_closed", NetworkControlsViewState.ChannelClosed, "disconnected"),
        ("platform_unavailable", NetworkControlsViewState.ServiceUnavailable, "service unavailable"),
    };
    foreach (var (code, state, expectedTitle) in cases)
    {
        var fake = new FakeNetworkHost
        {
            ReadException = new WidgetCapabilityException(code, "private provider path"),
        };
        var widget = Create(fake);
        await ActivateVisible(widget);
        await WaitUntil(() => widget.ViewState == state);
        var snapshot = Snapshot(widget, 1);
        Assert.Contains(expectedTitle, Text(snapshot.Root, "network.state.title").Text!);
        Assert.True(!Nodes(snapshot.Root).Any(node =>
                node.Text?.Contains("private provider path", StringComparison.Ordinal) == true),
            "Provider-private details leaked into a recovery surface.");
        Assert.Valid(snapshot);
        await Background(widget);
    }
}

static async Task UnavailableService()
{
    var widget = new NetworkControlsWidget();
    await ActivateVisible(widget);
    await WaitUntil(() => widget.ViewState == NetworkControlsViewState.ServiceUnavailable);
    var snapshot = Snapshot(widget, 1);
    Assert.Contains("service unavailable", Text(snapshot.Root, "network.state.title").Text!);
    Assert.Valid(snapshot);
    await Background(widget);
}

static async Task LifecycleAndNoPolling()
{
    var fake = ReadyHost();
    var widget = Create(fake);
    await WidgetTestHost.InitializeAsync(widget);
    Assert.Equal(0, fake.StatusCalls);
    Assert.Equal(0, fake.ProfilesCalls);
    Assert.Equal(0, fake.SubscriptionCount);

    await ActivateVisible(widget);
    await WaitUntil(() => widget.ViewState == NetworkControlsViewState.Ready &&
                          fake.SubscriptionCount == 1);
    Assert.Equal(1, widget.ActivationCount);
    Assert.Equal(1, fake.StatusCalls);
    Assert.Equal(1, fake.ProfilesCalls);
    await WidgetTestHost.SetLifecycleStateAsync(widget, WidgetLifecycleState.Interactive);
    await Task.Delay(150);
    Assert.Equal(1, fake.StatusCalls);
    Assert.Equal(1, fake.ProfilesCalls);
    Assert.Equal(1, fake.SubscriptionCount);

    await Background(widget);
    await WaitUntil(() => fake.CanceledSubscriptions == 1);
    var statusCalls = fake.StatusCalls;
    var profileCalls = fake.ProfilesCalls;
    await Task.Delay(150);
    Assert.Equal(statusCalls, fake.StatusCalls);
    Assert.Equal(profileCalls, fake.ProfilesCalls);

    await ActivateVisible(widget);
    await WaitUntil(() => fake.StatusCalls == 2 && fake.SubscriptionCount == 2);
    Assert.Equal(2, widget.ActivationCount);
    await Background(widget);
}

static async Task SubscriptionPrecedesSnapshot()
{
    var fake = ReadyHost();
    var gapStatus = Status(WidgetNetworkConnectivity.Local, WidgetNetworkTransportKind.Wifi,
        activeId: "office", name: "Gap Office", signal: 51);
    fake.OnGetStatus = () => fake.Emit(gapStatus,
        [Profile("office", "Gap Office", connected: true, signal: 51)]);
    var widget = Create(fake);
    await ActivateVisible(widget);
    await WaitUntil(() => widget.NetworkStatus?.ActiveProfileId == "office");
    Assert.Equal(1, fake.SubscriptionCount);
    Assert.Equal(1, fake.StatusCalls);
    Assert.True(fake.ProfilesCalls >= 2,
        "Buffered gap event did not reconcile saved profiles after the initial snapshot.");
    Assert.Equal("Gap Office", widget.Profiles.Single().DisplayName);
    Assert.Equal(WidgetNetworkConnectivity.Local, widget.NetworkStatus!.Connectivity);
    await Background(widget);
}

static async Task CancellationRollback()
{
    var never = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var fake = ReadyHost();
    fake.SwitchGate = never.Task;
    var widget = Create(fake);
    await ActivateInteractive(widget);
    await WaitUntil(() => widget.ViewState == NetworkControlsViewState.Ready);
    using var cancellation = new CancellationTokenSource();
    var ready = Snapshot(widget, 0);
    var command = widget.OnActionAsync(
        new("profile.connect.item", ProfileButton(ready.Root, "Office").Id),
        cancellation.Token).AsTask();
    await WaitUntil(() => fake.SwitchCalls == 1);
    cancellation.Cancel();
    await Assert.ThrowsCanceled(command);
    Assert.True(!widget.ControlBusy, "Canceled control retained a pending state.");
    Assert.Equal("home", widget.NetworkStatus!.ActiveProfileId);
    Assert.True(!Text(Snapshot(widget, 1).Root, "network.status").StyleClasses.Contains("is-error"),
        "Normal cancellation rendered as provider failure.");
    await Background(widget);
}

static async Task ShippedAssetsValidate()
{
    var project = ProjectDirectory();
    var manifest = ManifestJson.Deserialize(await File.ReadAllBytesAsync(Path.Combine(project, "manifest.json")));
    var errors = WidgetManifestValidator.Validate(manifest);
    Assert.True(errors.Count == 0, string.Join(Environment.NewLine, errors));
    Assert.Equal("org.gbar.firstparty.network-controls", manifest.Id);
    Assert.SequenceEqual(["system.network.read.v1"], manifest.Permissions);
    Assert.SequenceEqual(["system.network.saved-profile.switch.v1"], manifest.OptionalPermissions);
    Assert.Equal("suspend", manifest.BackgroundPolicy);
    Assert.Equal(64, manifest.ResourceRequest.MemoryMb);
    Assert.Equal(10, manifest.ResourceRequest.UpdateHz);
    Assert.SequenceEqual(["x64"], manifest.Architectures);

    var package = GbssPackageLoader.LoadFile(
        Path.Combine(project, "styles", "default.gbss"),
        Path.Combine(project, "styles"));
    var compiled = GbssThemeCompiler.Compile(package);
    Assert.True(compiled.IsValid, string.Join(Environment.NewLine, compiled.Diagnostics));
    AssertResponsiveLayoutBudget(compiled.Theme!);
    var style = await File.ReadAllTextAsync(Path.Combine(project, "styles", "default.gbss"));
    Assert.Contains("width: 100vw", style);
    Assert.Contains("min-width: 0px", style);
    Assert.Contains("max-width: 560px", style);
    Assert.True(!style.Contains("min-width: 440px", StringComparison.Ordinal),
        "The network widget retained a desktop-only hard width floor.");
    Assert.True(!style.Contains("scale:", StringComparison.Ordinal),
        "Full-width network rows may not scale beyond their clipped scroll viewport.");

    var catalogPath = Path.Combine(project, "..", "..", "OverlayHost", "widget-catalog.json");
    using var catalog = JsonDocument.Parse(await File.ReadAllBytesAsync(catalogPath));
    var trusted = catalog.RootElement.GetProperty("widgets").EnumerateArray().Single(item =>
        item.GetProperty("packageId").GetString() == manifest.Id);
    Assert.Equal(manifest.Publisher, trusted.GetProperty("publisherId").GetString());
    Assert.Equal(manifest.ResourceRequest.MemoryMb, trusted.GetProperty("memoryLimitMb").GetInt32());
    Assert.Equal("runtime/NetworkControls/NetworkControlsWidget.Worker.exe",
        trusted.GetProperty("workerExecutable").GetString());
    Assert.Equal("runtime/NetworkControls/styles/default.gbss",
        trusted.GetProperty("styleFile").GetString());
    var manifestCapabilities = manifest.Permissions.Concat(manifest.OptionalPermissions)
        .Order(StringComparer.Ordinal).ToArray();
    var trustedCapabilities = trusted.GetProperty("declaredCapabilities").EnumerateArray()
        .Select(item => item.GetString()!).Order(StringComparer.Ordinal).ToArray();
    Assert.SequenceEqual(manifestCapabilities, trustedCapabilities);
    var quickActions = trusted.GetProperty("quickActions").EnumerateArray().ToArray();
    Assert.Equal(0, quickActions.Length);
}

static void AssertResponsiveLayoutBudget(GbssTheme theme)
{
    var root = Resolve(theme, "stack", "network.root", "network-controls-widget");
    var list = Resolve(theme, "scroll", "network.profiles.scroll", "network-profile-list");
    var row = Resolve(theme, "stack", "network.profile.test.row", "network-profile-row");
    var profileButton = Resolve(theme, "button", "network.profile.test", "network-profile-button");
    var focusedProfileButton = theme.Resolve(new GbssElement(
        "button", "network.profile.test",
        new HashSet<string>(["network-profile-button"], StringComparer.Ordinal),
        new HashSet<GbssPseudoState>([GbssPseudoState.Focused])));
    Assert.True(Pixels(profileButton.Get("min-height")!, 280) >= 44,
        "Saved-network row reduced its controller height below 44px.");
    Assert.True(Pixels(list.Get("max-height")!, 560) <= 300,
        "Saved-network list can escape the compact surface height budget.");

    var listHorizontalInset = HorizontalSpacing(list.Get("padding")!, 560) / 2;
    var focusInset = Math.Abs(Pixels(focusedProfileButton.Get("outline-offset")!, 560));
    Assert.True(listHorizontalInset >= focusInset,
        "Saved-network focus outline can clip against the scroll viewport edge.");

    foreach (var viewport in new[] { 280D, 320D, 1280D, 3840D })
    {
        var preferred = Pixels(root.Get("width")!, viewport);
        var rootWidth = Math.Min(viewport, Math.Clamp(
            preferred,
            Pixels(root.Get("min-width")!, viewport),
            Pixels(root.Get("max-width")!, viewport)));
        var rootInner = rootWidth - HorizontalSpacing(root.Get("padding")!, viewport);
        var listInner = rootInner - HorizontalSpacing(list.Get("padding")!, viewport);
        var rowInner = listInner - HorizontalSpacing(row.Get("padding")!, viewport);
        Assert.True(Pixels(profileButton.Get("min-width")!, viewport) <= rowInner,
            $"Saved-network control exceeds row width at {viewport}px.");
        Assert.True(rootWidth <= 560 && rootWidth <= viewport,
            $"Root width {rootWidth}px escaped viewport/max bound at {viewport}px.");
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
    static GbssComputedValue Part(string text)
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

static FakeNetworkHost ReadyHost(bool allDisconnected = false) => new()
{
    Status = Status(
        WidgetNetworkConnectivity.Internet,
        WidgetNetworkTransportKind.Wifi,
        activeId: allDisconnected ? null : "home",
        name: allDisconnected ? null : "Home",
        signal: 80),
    Profiles =
    [
        Profile("home", "Home", connected: !allDisconnected, signal: 80),
        Profile("office", "Office", signal: 66),
    ],
};

static WidgetNetworkStatus Status(
    WidgetNetworkConnectivity connectivity,
    WidgetNetworkTransportKind transport,
    WidgetNetworkWirelessAvailability wireless = WidgetNetworkWirelessAvailability.Available,
    WidgetNetworkDetailsAccess details = WidgetNetworkDetailsAccess.Available,
    WidgetNetworkConnectionAttemptState attempt = WidgetNetworkConnectionAttemptState.None,
    string? attemptId = null,
    string? activeId = null,
    string? name = null,
    int? signal = null) =>
    new(connectivity, transport, wireless, details, attempt, attemptId, activeId, name, signal);

static WidgetSavedNetworkProfile Profile(
    string id,
    string name,
    bool connected = false,
    int? signal = null) => new(id, name, connected, signal);

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
    await widget.OnControllerInputAsync(new ControllerInputEvent(
        button,
        ControllerEventPhase.Pressed,
        context,
        FocusedElementId: focusedElementId ?? ProfileButtons(snapshot.Root).FirstOrDefault()?.Id,
        Sequence: 7,
        ActiveInputScopeId: snapshot.ActiveInputScopeId,
        SnapshotSequence: snapshot.Sequence));

static ViewSnapshot Snapshot(NetworkControlsWidget widget, long sequence)
{
    var snapshot = widget.RenderSnapshot("network.test", sequence);
    Assert.Equal(ProtocolConstants.ScrollContainerVersion, snapshot.ProtocolVersion);
    Assert.True(snapshot.Surface is not null, "Network Controls omitted its bounded surface hint.");
    Assert.Equal(WidgetSurfaceMode.Compact, snapshot.Surface!.Mode);
    Assert.Equal(560D, snapshot.Surface.PreferredWidth);
    Assert.Equal(520D, snapshot.Surface.PreferredHeight);
    Assert.Equal(320D, snapshot.Surface.MinimumWidth);
    Assert.Equal(360D, snapshot.Surface.MinimumHeight);
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

static IEnumerable<ViewNode> ProfileButtons(ViewNode root) =>
    Buttons(root).Where(node => node.ActionId == "profile.connect.item");

static ViewNode ProfileButton(ViewNode root, string displayName) =>
    ProfileButtons(root).Single(node => node.Text == displayName);

static ViewNode Node(ViewNode root, string id) =>
    Nodes(root).Single(node => node.Id == id);

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
    private readonly List<Channel<WidgetNetworkStatusChanged>> _subscribers = [];
    private int _statusCalls;
    private int _profilesCalls;
    private int _switchCalls;
    private int _subscriptionCount;
    private int _canceledSubscriptions;

    public WidgetNetworkStatus Status { get; set; } = StatusDefault();
    public IReadOnlyList<WidgetSavedNetworkProfile> Profiles { get; set; } = [];
    public Exception? ReadException { get; set; }
    public Exception? SwitchException { get; set; }
    public Task? SwitchGate { get; set; }
    public Action? OnGetStatus { get; set; }
    public List<SwitchWidgetSavedNetworkProfileRequest> SwitchRequests { get; } = [];
    public int StatusCalls => Volatile.Read(ref _statusCalls);
    public int ProfilesCalls => Volatile.Read(ref _profilesCalls);
    public int SwitchCalls => Volatile.Read(ref _switchCalls);
    public int SubscriptionCount => Volatile.Read(ref _subscriptionCount);
    public int CanceledSubscriptions => Volatile.Read(ref _canceledSubscriptions);

    public WidgetHostServices BuildServices() => new WidgetTestHostServicesBuilder()
        .WithHandler(WidgetNetworkCapabilities.GetStatus, GetStatusAsync)
        .WithHandler(WidgetNetworkCapabilities.GetSavedProfiles, GetProfilesAsync)
        .WithHandler(WidgetNetworkCapabilities.SwitchSavedProfile, SwitchAsync)
        .WithEventStream(WidgetNetworkCapabilities.StatusChanged, OpenEventStream)
        .Build();

    private ValueTask<WidgetNetworkStatus> GetStatusAsync(
        WidgetCapabilityQuery request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Interlocked.Increment(ref _statusCalls);
        if (ReadException is not null) throw ReadException;
        var snapshot = Status;
        OnGetStatus?.Invoke();
        return ValueTask.FromResult(snapshot);
    }

    private ValueTask<IReadOnlyList<WidgetSavedNetworkProfile>> GetProfilesAsync(
        WidgetCapabilityQuery request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Interlocked.Increment(ref _profilesCalls);
        if (ReadException is not null) throw ReadException;
        return ValueTask.FromResult<IReadOnlyList<WidgetSavedNetworkProfile>>(Profiles.ToArray());
    }

    private async ValueTask<WidgetCapabilityAcknowledgement> SwitchAsync(
        SwitchWidgetSavedNetworkProfileRequest request,
        CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _switchCalls);
        lock (_gate) SwitchRequests.Add(request);
        if (SwitchGate is not null) await SwitchGate.WaitAsync(cancellationToken);
        if (SwitchException is not null) throw SwitchException;
        return new WidgetCapabilityAcknowledgement(true);
    }

    private IAsyncEnumerable<WidgetNetworkStatusChanged> OpenEventStream(
        CancellationToken cancellationToken)
    {
        var channel = Channel.CreateBounded<WidgetNetworkStatusChanged>(new BoundedChannelOptions(1)
        {
            SingleReader = true,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.DropOldest,
        });
        lock (_gate) _subscribers.Add(channel);
        Interlocked.Increment(ref _subscriptionCount);
        return ReadEvents(channel, cancellationToken);
    }

    private async IAsyncEnumerable<WidgetNetworkStatusChanged> ReadEvents(
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
            lock (_gate) _subscribers.Remove(channel);
            Interlocked.Increment(ref _canceledSubscriptions);
        }
    }

    public void Emit(
        WidgetNetworkStatus status,
        IReadOnlyList<WidgetSavedNetworkProfile> profiles)
    {
        Status = status;
        Profiles = profiles;
        Channel<WidgetNetworkStatusChanged>[] subscribers;
        lock (_gate) subscribers = _subscribers.ToArray();
        foreach (var subscriber in subscribers)
            subscriber.Writer.TryWrite(new WidgetNetworkStatusChanged(status));
    }

    private static WidgetNetworkStatus StatusDefault() => new(
        WidgetNetworkConnectivity.None,
        WidgetNetworkTransportKind.None,
        WidgetNetworkWirelessAvailability.Available,
        WidgetNetworkDetailsAccess.Available,
        WidgetNetworkConnectionAttemptState.None,
        null,
        null,
        null,
        null);
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

    public static async Task ThrowsCanceled(Task task)
    {
        try
        {
            await task;
        }
        catch (OperationCanceledException)
        {
            return;
        }
        throw new InvalidOperationException("Expected an OperationCanceledException.");
    }

    public static void Valid(ViewSnapshot snapshot)
    {
        var errors = ViewSnapshotValidator.Validate(snapshot);
        if (errors.Count != 0)
            throw new InvalidOperationException(string.Join(Environment.NewLine, errors));
    }
}
