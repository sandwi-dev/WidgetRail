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
    ("D-pad and analog focus graph is explicit for every network control", ExplicitFocusGraph),
    ("Dashboard routes only local read actions and open routes control", ControllerRoutes),
    ("Switch acknowledgement waits for authoritative status completion", AcceptedSwitchWaitsForEvent),
    ("Terminal and immediate switch failures roll back safely", SwitchFailureRollback),
    ("Saved-profile churn preserves identity selection", StableSelectionDuringChurn),
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
    Assert.Equal("Connected", Button(wifi.Root, "network.profile.connect").Text);
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

static async Task ExplicitFocusGraph()
{
    var fake = ReadyHost();
    var widget = Create(fake);
    await ActivateInteractive(widget);
    await WaitUntil(() => widget.ViewState == NetworkControlsViewState.Ready);
    var snapshot = Snapshot(widget, 1);
    var expectedIds = new[]
    {
        "network.profile.previous",
        "network.profile.next",
        "network.profile.connect",
    };
    Assert.SequenceEqual(expectedIds, Buttons(snapshot.Root).Select(button => button.Id));
    foreach (var button in Buttons(snapshot.Root))
    {
        Assert.True(button.Focus is { Up: not null, Down: not null, Left: not null, Right: not null },
            $"'{button.Id}' does not have an explicit four-way focus graph.");
        foreach (var neighbor in new[]
                 {
                     button.Focus!.Up!, button.Focus.Down!, button.Focus.Left!, button.Focus.Right!,
                 })
            Assert.True(expectedIds.Contains(neighbor, StringComparer.Ordinal),
                $"'{button.Id}' points to missing focus target '{neighbor}'.");
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
    Assert.SequenceEqual(
        new[] { "profile.previous", "profile.next" },
        visible.QuickActions.Select(action => action.ActionId));
    Assert.SequenceEqual(
        new[] { ControllerButton.LeftBumper, ControllerButton.RightBumper },
        visible.QuickActions.Select(action => action.Button));
    Assert.True(!visible.Root.Shortcuts.Any(shortcut =>
            shortcut.Button is ControllerButton.B or ControllerButton.Y or
                ControllerButton.DPadUp or ControllerButton.DPadDown or
                ControllerButton.DPadLeft or ControllerButton.DPadRight),
        "The open surface captured host close/reorder/navigation input.");

    Assert.True(await Route(widget, visible, ControllerButton.RightBumper,
        ControllerInputContext.DashboardQuickAction));
    await WaitUntil(() => widget.SelectedProfileId == "office");
    await widget.OnActionAsync(new("profile.connect", "dashboard.invalid"));
    Assert.Equal(0, fake.SwitchCalls);
    Assert.Contains("Open Network Controls", Text(Snapshot(widget, 2).Root, "network.status").Text!);

    await WidgetTestHost.SetLifecycleStateAsync(widget, WidgetLifecycleState.Interactive);
    var open = Snapshot(widget, 3);
    Assert.True(await Route(widget, open, ControllerButton.LeftBumper, ControllerInputContext.OpenWidget));
    await WaitUntil(() => widget.SelectedProfileId == "home");
    Assert.True(await Route(widget, Snapshot(widget, 4), ControllerButton.X,
        ControllerInputContext.OpenWidget));
    await WaitUntil(() => fake.SwitchCalls == 1);
    Assert.Equal("home", fake.SwitchRequests.Single().ProfileId);
    Assert.True(widget.ControlBusy, "Accepted switch should wait for a terminal status event.");
    fake.Emit(Status(WidgetNetworkConnectivity.Internet, WidgetNetworkTransportKind.Wifi,
            activeId: "home", name: "Home", signal: 80),
        [Profile("home", "Home", connected: true, signal: 80), Profile("office", "Office")]);
    await WaitUntil(() => !widget.ControlBusy);
    await Background(widget);
}

static async Task AcceptedSwitchWaitsForEvent()
{
    var fake = ReadyHost();
    var widget = Create(fake);
    await ActivateInteractive(widget);
    await WaitUntil(() => widget.ViewState == NetworkControlsViewState.Ready);
    await widget.OnActionAsync(new("profile.next", "network.profile.next"));
    await widget.OnActionAsync(new("profile.connect", "network.profile.connect"));
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
    Assert.Equal("Connecting…", Button(Snapshot(widget, 2).Root, "network.profile.connect").Text);

    fake.Emit(Status(WidgetNetworkConnectivity.Internet, WidgetNetworkTransportKind.Wifi,
            activeId: "office", name: "Office", signal: 68),
        [Profile("home", "Home", signal: 78), Profile("office", "Office", connected: true, signal: 68)]);
    await WaitUntil(() => !widget.ControlBusy && widget.NetworkStatus?.ActiveProfileId == "office");
    Assert.Equal("Connected", Button(Snapshot(widget, 3).Root, "network.profile.connect").Text);
    await Background(widget);
}

static async Task SwitchFailureRollback()
{
    var immediate = ReadyHost();
    immediate.SwitchException = new WidgetCapabilityException("permission_denied", "private detail");
    var widget = Create(immediate);
    await ActivateInteractive(widget);
    await WaitUntil(() => widget.ViewState == NetworkControlsViewState.Ready);
    await widget.OnActionAsync(new("profile.next", "network.profile.next"));
    await widget.OnActionAsync(new("profile.connect", "network.profile.connect"));
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
    await terminalWidget.OnActionAsync(new("profile.next", "network.profile.next"));
    await terminalWidget.OnActionAsync(new("profile.connect", "network.profile.connect"));
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
    await widget.OnActionAsync(new("profile.next", "network.profile.next"));
    Assert.Equal("b", widget.SelectedProfileId);

    fake.Emit(fake.Status,
        [Profile("c", "Gamma"), Profile("b", "Beta renamed"), Profile("d", "Delta")]);
    await WaitUntil(() => widget.Profiles.First().ProfileId == "c");
    Assert.Equal("b", widget.SelectedProfileId);
    Assert.Equal("Beta renamed", Text(Snapshot(widget, 1).Root, "network.profile.name").Text);

    fake.Emit(fake.Status, [Profile("c", "Gamma"), Profile("d", "Delta")]);
    await WaitUntil(() => widget.Profiles.Count == 2);
    Assert.Equal("d", widget.SelectedProfileId);
    Assert.Equal("network.profile.connect", Snapshot(widget, 2).InitialFocusId);
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
    await widget.OnActionAsync(new("profile.next", "network.profile.next"));
    using var cancellation = new CancellationTokenSource();
    var command = widget.OnActionAsync(
        new("profile.connect", "network.profile.connect"), cancellation.Token).AsTask();
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
    Assert.Contains("width: 44vw", style);
    Assert.Contains("min-width: 280px", style);
    Assert.Contains("max-width: 620px", style);
    Assert.True(!style.Contains("min-width: 440px", StringComparison.Ordinal),
        "The network widget retained a desktop-only hard width floor.");

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
    Assert.Equal(2, quickActions.Length);
    Assert.SequenceEqual(["profile.previous", "profile.next"],
        quickActions.Select(item => item.GetProperty("actionId").GetString()!));
    Assert.SequenceEqual(["leftBumper", "rightBumper"],
        quickActions.Select(item => item.GetProperty("controllerButton").GetString()!));
    Assert.SequenceEqual(["dashboard.network.previous", "dashboard.network.next"],
        quickActions.Select(item => item.GetProperty("sourceElementId").GetString()!));
}

static void AssertResponsiveLayoutBudget(GbssTheme theme)
{
    var root = Resolve(theme, "stack", "network.root", "network-controls-widget");
    var card = Resolve(theme, "stack", "network.profile.card", "network-profile-card");
    var switcher = Resolve(theme, "row", "network.profile.switcher", "network-profile-switcher");
    var profileButton = Resolve(theme, "button", "network.profile.previous", "network-profile-action");
    var profileCopy = Resolve(theme, "stack", "network.profile.copy", "network-profile-copy");
    var connect = Resolve(theme, "button", "network.profile.connect", "network-connect-action");
    Assert.True(Pixels(profileButton.Get("width")!, 280) >= 44,
        "Narrow layout reduced a controller target below 44px.");

    foreach (var viewport in new[] { 280D, 320D, 1280D, 3840D })
    {
        var preferred = Pixels(root.Get("width")!, viewport);
        var rootWidth = Math.Min(viewport, Math.Clamp(
            preferred,
            Pixels(root.Get("min-width")!, viewport),
            Pixels(root.Get("max-width")!, viewport)));
        var rootInner = rootWidth - HorizontalSpacing(root.Get("padding")!, viewport);
        var cardInner = rootInner - HorizontalSpacing(card.Get("padding")!, viewport);
        var switcherMinimum =
            2 * Pixels(profileButton.Get("width")!, viewport) +
            Pixels(profileCopy.Get("min-width")!, viewport) +
            2 * Pixels(switcher.Get("gap")!, viewport);
        Assert.True(switcherMinimum <= cardInner,
            $"Profile switcher needs {switcherMinimum}px but has {cardInner}px at {viewport}px.");
        Assert.True(Pixels(connect.Get("min-width")!, viewport) <= cardInner,
            $"Connect control exceeds card width at {viewport}px.");
        Assert.True(rootWidth <= 620 && rootWidth <= viewport,
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
    ControllerInputContext context) =>
    await widget.OnControllerInputAsync(new ControllerInputEvent(
        button,
        ControllerEventPhase.Pressed,
        context,
        FocusedElementId: "network.profile.connect",
        Sequence: 7,
        ActiveInputScopeId: snapshot.ActiveInputScopeId,
        SnapshotSequence: snapshot.Sequence));

static ViewSnapshot Snapshot(NetworkControlsWidget widget, long sequence) =>
    widget.RenderSnapshot("network.test", sequence);

static IEnumerable<ViewNode> Nodes(ViewNode node)
{
    yield return node;
    foreach (var child in node.Children)
    foreach (var descendant in Nodes(child))
        yield return descendant;
}

static IEnumerable<ViewNode> Buttons(ViewNode root) =>
    Nodes(root).Where(node => node.Kind == ViewNodeKind.Button);

static ViewNode Button(ViewNode root, string id) =>
    Nodes(root).Single(node => node.Id == id && node.Kind == ViewNodeKind.Button);

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
