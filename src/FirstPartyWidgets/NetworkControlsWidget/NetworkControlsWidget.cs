using GameBarAlternative.WidgetProtocol;
using GameBarAlternative.WidgetSdk;

namespace GameBarAlternative.FirstPartyWidgets.NetworkControls;

public enum NetworkControlsViewState
{
    Initial,
    Loading,
    Ready,
    Offline,
    EmptyProfiles,
    RadioOff,
    WirelessUnavailable,
    PermissionDenied,
    LifecycleDenied,
    ChannelClosed,
    ServiceUnavailable,
    Error,
}

/// <summary>
/// Event-driven, controller-first network status and saved-profile switcher.
/// All platform work crosses the typed host network service; widget code never
/// opens WLAN, IP Helper, registry, or socket handles.
/// </summary>
public sealed class NetworkControlsWidget : Widget
{
    private static readonly IReadOnlyList<WidgetQuickAction> ProfileQuickActions =
    [
        new(ControllerButton.LeftBumper, "profile.previous", "Previous saved network"),
        new(ControllerButton.RightBumper, "profile.next", "Next saved network"),
    ];

    private readonly object _stateLock = new();
    private readonly SemaphoreSlim _commandGate = new(1, 1);
    private WidgetNetworkStatus? _networkStatus;
    private WidgetNetworkStatus? _authoritativeStatus;
    private IReadOnlyList<WidgetSavedNetworkProfile> _profiles = [];
    private IReadOnlyList<WidgetSavedNetworkProfile> _authoritativeProfiles = [];
    private NetworkControlsViewState _viewState = NetworkControlsViewState.Initial;
    private string? _selectedProfileId;
    private int _selectedIndex;
    private string _status = "Network status loads when this widget becomes visible";
    private bool _statusIsError;
    private bool _controlBusy;
    private string? _pendingProfileId;
    private CancellationTokenSource? _runLifetime;
    private long _runGeneration;
    private int _activationCount;
    private int _statusFetchCount;
    private int _profilesFetchCount;

    public NetworkControlsViewState ViewState
    {
        get { lock (_stateLock) return _viewState; }
    }

    public WidgetNetworkStatus? NetworkStatus
    {
        get { lock (_stateLock) return _networkStatus; }
    }

    public IReadOnlyList<WidgetSavedNetworkProfile> Profiles
    {
        get { lock (_stateLock) return _profiles.ToArray(); }
    }

    public string? SelectedProfileId
    {
        get { lock (_stateLock) return _selectedProfileId; }
    }

    public bool ControlBusy
    {
        get { lock (_stateLock) return _controlBusy; }
    }

    public int ActivationCount => Volatile.Read(ref _activationCount);
    public int StatusFetchCount => Volatile.Read(ref _statusFetchCount);
    public int ProfilesFetchCount => Volatile.Read(ref _profilesFetchCount);

    public override WidgetView Render()
    {
        WidgetNetworkStatus? status;
        IReadOnlyList<WidgetSavedNetworkProfile> profiles;
        WidgetSavedNetworkProfile? selected;
        NetworkControlsViewState state;
        int selectedIndex;
        string statusText;
        bool statusIsError;
        bool controlBusy;
        string? pendingProfileId;
        lock (_stateLock)
        {
            status = _networkStatus;
            profiles = _profiles;
            selected = SelectedProfileLocked();
            state = _viewState;
            selectedIndex = _selectedIndex;
            statusText = _status;
            statusIsError = _statusIsError;
            controlBusy = _controlBusy;
            pendingProfileId = _pendingProfileId;
        }

        var header = UI.Stack("network.header",
            UI.Text("CONTROL CENTER", "network.eyebrow", "Control Center")
                .Classes("network-eyebrow"),
            UI.Text("Network Controls", "network.title", "Network Controls")
                .Classes("network-title"),
            UI.Text(statusText, "network.status", statusText).Classes(
                "network-status",
                statusIsError ? "is-error" : IsConnected(status) ? "is-live" : "is-neutral"))
            .Classes("network-header");

        if (status is null)
            return RenderProviderState(header, state);

        var content = new List<WidgetElement>
        {
            header,
            RenderConnectionCard(status),
        };

        var wirelessNote = WirelessNote(status);
        if (wirelessNote is not null)
        {
            content.Add(UI.Stack("network.wifi.note",
                    UI.Text(wirelessNote.Value.Title, "network.wifi.note.title", wirelessNote.Value.Title)
                        .Classes("network-card-state", wirelessNote.Value.IsError ? "is-error" : "is-warning"),
                    UI.Text(wirelessNote.Value.Detail, "network.wifi.note.detail", wirelessNote.Value.Detail)
                        .Classes("network-card-detail"))
                .Classes("network-wifi-note"));
        }

        string? initialFocus;
        IReadOnlyList<WidgetQuickAction> quickActions;
        if (selected is null)
        {
            var retry = UI.Button("Refresh saved networks", "retry", "network.retry")
                .Icon(WidgetGlyph.Refresh, "Refresh saved networks")
                .Classes("network-retry-action");
            content.Add(UI.Stack("network.profiles.empty",
                    UI.Text("No saved Wi-Fi networks", "network.profiles.empty.title",
                            "No saved Wi-Fi networks")
                        .Classes("network-state-title"),
                    UI.Text("Windows has no saved profiles available to this widget. Add one in Windows Settings.",
                            "network.profiles.empty.help")
                        .Classes("network-help"),
                    retry)
                .Classes("network-state-card"));
            initialFocus = "network.retry";
            quickActions = [];
        }
        else
        {
            content.Add(RenderProfileCard(
                selected, selectedIndex, profiles.Count, controlBusy, pendingProfileId));
            initialFocus = "network.profile.connect";
            quickActions = profiles.Count > 1 ? ProfileQuickActions : [];
        }

        var root = UI.Stack("network.root", content.ToArray())
            .InputScope("network-controls")
            .Shortcut(ControllerButton.LeftBumper, "profile.previous")
            .Shortcut(ControllerButton.RightBumper, "profile.next")
            .Shortcut(ControllerButton.X, "profile.connect")
            .Classes("network-controls-widget", selected is null ? "has-state" : "has-profiles");
        return new WidgetView(root, InitialFocusId: initialFocus, QuickActions: quickActions);
    }

    protected override ValueTask OnActivatedAsync(CancellationToken activeLifetime)
    {
        Interlocked.Increment(ref _activationCount);
        StartActiveRun(activeLifetime);
        return ValueTask.CompletedTask;
    }

    protected override ValueTask OnLifecycleStateChangedAsync(
        WidgetLifecycleState previous,
        WidgetLifecycleState current,
        CancellationToken stateLifetime)
    {
        if (previous is WidgetLifecycleState.Visible or WidgetLifecycleState.Interactive ||
            current is WidgetLifecycleState.Visible or WidgetLifecycleState.Interactive)
            Invalidate();
        return ValueTask.CompletedTask;
    }

    protected override ValueTask OnDeactivatedAsync(CancellationToken transitionToken)
    {
        StopActiveRun();
        return ValueTask.CompletedTask;
    }

    protected override ValueTask OnDestroyingAsync(CancellationToken shutdownToken)
    {
        StopActiveRun();
        return ValueTask.CompletedTask;
    }

    public override async ValueTask OnActionAsync(
        WidgetActionEvent action,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(action);
        switch (action.ActionId)
        {
            case "profile.previous":
                SelectRelativeProfile(-1);
                break;
            case "profile.next":
                SelectRelativeProfile(1);
                break;
            case "profile.connect":
                await ConnectSelectedProfileAsync(cancellationToken).ConfigureAwait(false);
                break;
            case "retry":
                if (IsActive) StartActiveRun(ActiveCancellationToken);
                break;
        }
    }

    private StackElement RenderConnectionCard(WidgetNetworkStatus status)
    {
        var title = status.ActiveProfileName ?? status.Transport switch
        {
            WidgetNetworkTransportKind.Ethernet => "Wired connection",
            WidgetNetworkTransportKind.Wifi => "Wi-Fi connection",
            WidgetNetworkTransportKind.Other => "Network connection",
            _ => "No active network",
        };
        var transport = status.Transport switch
        {
            WidgetNetworkTransportKind.Ethernet => "ETHERNET",
            WidgetNetworkTransportKind.Wifi => "WI-FI",
            WidgetNetworkTransportKind.Other => "NETWORK",
            _ => "OFFLINE",
        };
        var connectivity = status.Connectivity switch
        {
            WidgetNetworkConnectivity.Internet => "Internet access",
            WidgetNetworkConnectivity.Local => "Local network only",
            _ => "Not connected",
        };
        var stateClass = status.Connectivity switch
        {
            WidgetNetworkConnectivity.Internet => "is-online",
            WidgetNetworkConnectivity.Local => "is-warning",
            _ => "is-error",
        };

        var children = new List<WidgetElement>
        {
            UI.Row("network.connection.top",
                    UI.Icon(WidgetGlyph.Connection, "network.connection.icon", transport)
                        .Classes("network-connection-icon"),
                    UI.Stack("network.connection.copy",
                            UI.Text(title, "network.connection.title", title).Classes("network-card-title"),
                            UI.Text(transport, "network.connection.transport", transport)
                                .Classes("network-card-state", stateClass),
                            UI.Text(connectivity, "network.connection.detail", connectivity)
                                .Classes("network-card-detail"))
                        .Classes("network-connection-copy"))
                .Classes("network-connection-top"),
        };

        if (status.SignalPercent is { } signal)
        {
            children.Add(UI.Row("network.signal.row",
                    UI.Progress(signal, 100, "network.signal.progress", $"Wi-Fi signal {signal} percent")
                        .Classes("network-signal-progress"),
                    UI.Text($"{signal}%", "network.signal.value", $"Signal {signal} percent")
                        .Classes("network-signal-value"))
                .Classes("network-signal-row"));
        }

        return UI.Stack("network.connection.card", children.ToArray())
            .Classes("network-connection-card");
    }

    private RowElement RenderProfileSwitcher(
        WidgetSavedNetworkProfile selected,
        int selectedIndex,
        int profileCount,
        bool controlBusy,
        string? pendingProfileId)
    {
        var hasMultipleProfiles = profileCount > 1;
        var isPending = string.Equals(
            pendingProfileId, selected.ProfileId, StringComparison.Ordinal);
        var previous = UI.Button("", "profile.previous", "network.profile.previous")
            .Icon(WidgetGlyph.Previous, "Previous saved network")
            .Disabled(!hasMultipleProfiles)
            .FocusUp("network.profile.connect")
            .FocusLeft("network.profile.next")
            .FocusRight("network.profile.next")
            .FocusDown("network.profile.connect")
            .Classes("network-profile-action");
        var next = UI.Button("", "profile.next", "network.profile.next")
            .Icon(WidgetGlyph.Next, "Next saved network")
            .Disabled(!hasMultipleProfiles)
            .FocusUp("network.profile.connect")
            .FocusLeft("network.profile.previous")
            .FocusRight("network.profile.previous")
            .FocusDown("network.profile.connect")
            .Classes("network-profile-action");
        var state = isPending ? "CONNECTING" : selected.IsConnected ? "CONNECTED" : "SAVED";
        var detail = selected.SignalPercent is { } signal
            ? $"Signal {signal}% · Profile {selectedIndex + 1} of {profileCount}"
            : $"Profile {selectedIndex + 1} of {profileCount}";
        return UI.Row("network.profile.switcher",
                previous,
                UI.Stack("network.profile.copy",
                        UI.Text(selected.DisplayName, "network.profile.name", selected.DisplayName)
                            .Classes("network-profile-name"),
                        UI.Text(state, "network.profile.state", state).Classes(
                            "network-profile-state",
                            isPending ? "is-connecting" : selected.IsConnected ? "is-connected" : "is-saved"),
                        UI.Text(detail, "network.profile.detail", detail).Classes("network-profile-detail"))
                    .Classes("network-profile-copy"),
                next)
            .Classes("network-profile-switcher");
    }

    private StackElement RenderProfileCard(
        WidgetSavedNetworkProfile selected,
        int selectedIndex,
        int profileCount,
        bool controlBusy,
        string? pendingProfileId)
    {
        var interactive = LifecycleState == WidgetLifecycleState.Interactive;
        var isPending = string.Equals(
            pendingProfileId, selected.ProfileId, StringComparison.Ordinal);
        var connectLabel = isPending ? "Connecting…" : selected.IsConnected ? "Connected" : "Connect";
        var accessibilityLabel = !interactive && !selected.IsConnected
            ? $"Open Network Controls to connect to {selected.DisplayName}"
            : selected.IsConnected
                ? $"{selected.DisplayName} is connected"
                : $"Connect to {selected.DisplayName}";
        var connect = UI.Button(connectLabel, "profile.connect", "network.profile.connect")
            .Icon(WidgetGlyph.Connection, accessibilityLabel)
            .Selected(selected.IsConnected)
            .Busy(controlBusy && isPending)
            .Disabled(!interactive || selected.IsConnected || controlBusy)
            .FocusUp("network.profile.previous")
            .FocusDown("network.profile.previous")
            .FocusLeft("network.profile.previous")
            .FocusRight("network.profile.next")
            .Classes("network-connect-action");
        return UI.Stack("network.profile.card",
                RenderProfileSwitcher(selected, selectedIndex, profileCount, controlBusy, pendingProfileId),
                connect,
                UI.Text("LB/RB  SAVED NETWORK     X  CONNECT", "network.shortcuts",
                        "Left and right bumper select a saved network. X connects while this widget is open.")
                    .Classes("network-shortcuts"))
            .Classes("network-profile-card");
    }

    private WidgetView RenderProviderState(StackElement header, NetworkControlsViewState state)
    {
        var (title, help, buttonLabel, error) = state switch
        {
            NetworkControlsViewState.Initial => (
                "Ready when you are",
                "Open the widget to load Windows network status.",
                "Load network status",
                false),
            NetworkControlsViewState.Loading => (
                "Checking network status",
                "The host network provider is loading one current snapshot.",
                "Loading…",
                false),
            NetworkControlsViewState.PermissionDenied => (
                "Network access is off",
                "Grant network read access in Settings → Permissions, then try again.",
                "Try again",
                true),
            NetworkControlsViewState.LifecycleDenied => (
                "Network status paused by lifecycle",
                "Return this widget to the foreground before requesting network status.",
                "Try again",
                true),
            NetworkControlsViewState.ChannelClosed => (
                "Network service disconnected",
                "The protected host channel closed. Reopen or retry the widget.",
                "Reconnect",
                true),
            NetworkControlsViewState.ServiceUnavailable => (
                "Network service unavailable",
                "This worker was not given the host network service.",
                "Try again",
                true),
            _ => (
                "Network status could not be loaded",
                "The provider returned an unexpected error. No system details were exposed.",
                "Try again",
                true),
        };
        var retry = UI.Button(buttonLabel, "retry", "network.retry")
            .Icon(WidgetGlyph.Refresh, buttonLabel)
            .Busy(state == NetworkControlsViewState.Loading)
            .Disabled(state == NetworkControlsViewState.Loading)
            .Classes("network-retry-action");
        var root = UI.Stack("network.root",
                header,
                UI.Stack("network.state.card",
                        UI.Text(title, "network.state.title", title).Classes("network-state-title"),
                        UI.Text(help, "network.state.help", help)
                            .Classes("network-help", error ? "is-error" : "is-neutral"),
                        retry)
                    .Classes("network-state-card"))
            .InputScope("network-controls")
            .Classes("network-controls-widget", error ? "has-error" : "has-state");
        return new WidgetView(root, InitialFocusId: "network.retry");
    }

    private void StartActiveRun(CancellationToken activeLifetime)
    {
        CancellationTokenSource? previous;
        CancellationTokenSource current;
        long generation;
        lock (_stateLock)
        {
            previous = _runLifetime;
            current = CancellationTokenSource.CreateLinkedTokenSource(activeLifetime);
            _runLifetime = current;
            generation = ++_runGeneration;
            RestoreAuthoritativeLocked();
            _viewState = NetworkControlsViewState.Loading;
            _status = "Loading network status…";
            _statusIsError = false;
        }
        previous?.Cancel();
        previous?.Dispose();
        Invalidate();
        _ = ObserveNetworkAsync(generation, current.Token);
    }

    private void StopActiveRun()
    {
        CancellationTokenSource? lifetime;
        lock (_stateLock)
        {
            ++_runGeneration;
            lifetime = _runLifetime;
            _runLifetime = null;
            RestoreAuthoritativeLocked();
        }
        lifetime?.Cancel();
        lifetime?.Dispose();
    }

    private async Task ObserveNetworkAsync(long generation, CancellationToken cancellationToken)
    {
        try
        {
            // Subscription acknowledgement happens before either current read.
            // Full status events buffered during those reads reconcile the gap.
            await using var subscription = await HostServices.Network
                .OpenStatusSubscriptionAsync(cancellationToken).ConfigureAwait(false);
            var (status, profiles) = await ReadCurrentAsync(cancellationToken).ConfigureAwait(false);
            if (!IsCurrentRun(generation, cancellationToken)) return;
            ApplySnapshot(status, profiles, generation);

            await foreach (var change in subscription.ReadAllAsync(cancellationToken)
                               .WithCancellation(cancellationToken).ConfigureAwait(false))
            {
                if (!IsCurrentRun(generation, cancellationToken)) return;
                Interlocked.Increment(ref _profilesFetchCount);
                var updatedProfiles = await HostServices.Network
                    .GetSavedProfilesAsync(cancellationToken).ConfigureAwait(false);
                if (!IsCurrentRun(generation, cancellationToken)) return;
                ApplySnapshot(change.Status, updatedProfiles, generation);
            }

            if (IsCurrentRun(generation, cancellationToken))
                SetProviderError(NetworkControlsViewState.ChannelClosed,
                    "Network service channel closed", generation);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Background and Destroying cancel active work without rendering an error.
        }
        catch (WidgetCapabilityUnavailableException)
        {
            SetProviderError(NetworkControlsViewState.ServiceUnavailable,
                "Host network service unavailable", generation);
        }
        catch (WidgetCapabilityException exception)
        {
            var (state, message) = MapCapabilityFailure(exception.ErrorCode);
            SetProviderError(state, message, generation);
        }
        catch (Exception)
        {
            SetProviderError(NetworkControlsViewState.Error,
                "Network provider returned an unexpected error", generation);
        }
    }

    private async Task<(WidgetNetworkStatus Status, IReadOnlyList<WidgetSavedNetworkProfile> Profiles)>
        ReadCurrentAsync(CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _statusFetchCount);
        var status = await HostServices.Network.GetStatusAsync(cancellationToken).ConfigureAwait(false);
        Interlocked.Increment(ref _profilesFetchCount);
        var profiles = await HostServices.Network.GetSavedProfilesAsync(cancellationToken)
            .ConfigureAwait(false);
        return (status, profiles);
    }

    private void ApplySnapshot(
        WidgetNetworkStatus incomingStatus,
        IReadOnlyList<WidgetSavedNetworkProfile>? incomingProfiles,
        long generation)
    {
        ArgumentNullException.ThrowIfNull(incomingStatus);
        var status = NormalizeStatus(incomingStatus);
        var profiles = NormalizeProfiles(incomingProfiles);
        lock (_stateLock)
        {
            if (_runGeneration != generation) return;
            var previousId = _selectedProfileId;
            var previousIndex = _selectedIndex;
            _authoritativeStatus = status;
            _authoritativeProfiles = profiles;
            _networkStatus = status;
            _profiles = profiles;

            var preferredId = status.AttemptProfileId ?? previousId;
            var retained = preferredId is null
                ? -1
                : profiles.FindIndex(profile =>
                    string.Equals(profile.ProfileId, preferredId, StringComparison.Ordinal));
            _selectedIndex = retained >= 0
                ? retained
                : Math.Clamp(previousIndex, 0, Math.Max(0, profiles.Count - 1));
            _selectedProfileId = profiles.Count == 0 ? null : profiles[_selectedIndex].ProfileId;

            switch (status.ConnectionAttemptState)
            {
                case WidgetNetworkConnectionAttemptState.Connecting:
                    _pendingProfileId = status.AttemptProfileId;
                    _controlBusy = _pendingProfileId is not null;
                    _status = _pendingProfileId is { } connectingId
                        ? $"Connecting to {ProfileNameLocked(connectingId)}…"
                        : "Connecting to saved network…";
                    _statusIsError = false;
                    break;
                case WidgetNetworkConnectionAttemptState.Failed:
                    _pendingProfileId = null;
                    _controlBusy = false;
                    _status = status.AttemptProfileId is { } failedId
                        ? $"Could not connect to {ProfileNameLocked(failedId)} · previous connection retained"
                        : "Could not connect · previous connection retained";
                    _statusIsError = true;
                    break;
                default:
                    _pendingProfileId = null;
                    _controlBusy = false;
                    _status = LiveStatus(status, profiles.Count);
                    _statusIsError = false;
                    break;
            }
            _viewState = DeriveViewState(status, profiles.Count);
        }
        Invalidate();
    }

    private void SelectRelativeProfile(int delta)
    {
        lock (_stateLock)
        {
            if (_profiles.Count <= 1 || _networkStatus is null) return;
            _selectedIndex = (_selectedIndex + delta) % _profiles.Count;
            if (_selectedIndex < 0) _selectedIndex += _profiles.Count;
            _selectedProfileId = _profiles[_selectedIndex].ProfileId;
            _status = $"Selected {_profiles[_selectedIndex].DisplayName}";
            _statusIsError = false;
        }
        Invalidate();
    }

    private async ValueTask ConnectSelectedProfileAsync(CancellationToken cancellationToken)
    {
        if (LifecycleState != WidgetLifecycleState.Interactive)
        {
            lock (_stateLock)
            {
                _status = "Open Network Controls before connecting";
                _statusIsError = false;
            }
            Invalidate();
            return;
        }

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken, ActiveCancellationToken);
        await _commandGate.WaitAsync(linked.Token).ConfigureAwait(false);
        try
        {
            WidgetSavedNetworkProfile? selected;
            lock (_stateLock)
            {
                selected = SelectedProfileLocked();
                if (selected is null || selected.IsConnected || _controlBusy) return;
                _pendingProfileId = selected.ProfileId;
                _controlBusy = true;
                _status = $"Requesting {selected.DisplayName}…";
                _statusIsError = false;
                if (_networkStatus is { } current)
                    _networkStatus = current with
                    {
                        ConnectionAttemptState = WidgetNetworkConnectionAttemptState.Connecting,
                        AttemptProfileId = selected.ProfileId,
                    };
            }
            Invalidate();

            try
            {
                await HostServices.Network.SwitchSavedProfileAsync(
                    selected.ProfileId, linked.Token).ConfigureAwait(false);
                lock (_stateLock)
                {
                    if (string.Equals(_pendingProfileId, selected.ProfileId, StringComparison.Ordinal))
                    {
                        // ACK means Windows accepted the request, not that the
                        // connection succeeded. A provider status event is terminal.
                        _status = $"Connection request accepted for {selected.DisplayName} · waiting for Windows";
                        _statusIsError = false;
                    }
                }
                Invalidate();
            }
            catch (OperationCanceledException) when (linked.IsCancellationRequested)
            {
                RollbackControl(null);
                throw;
            }
            catch (WidgetCapabilityUnavailableException)
            {
                RollbackControl("Host network control service unavailable · previous connection retained");
            }
            catch (WidgetCapabilityException exception)
            {
                RollbackControl(MapControlFailure(exception.ErrorCode));
            }
            catch (Exception)
            {
                RollbackControl("Connection request failed · previous connection retained");
            }
        }
        finally
        {
            _commandGate.Release();
        }
    }

    private void RollbackControl(string? message)
    {
        lock (_stateLock)
        {
            RestoreAuthoritativeLocked();
            if (message is not null)
            {
                _status = message;
                _statusIsError = true;
            }
        }
        Invalidate();
    }

    private void RestoreAuthoritativeLocked()
    {
        _networkStatus = _authoritativeStatus;
        _profiles = _authoritativeProfiles;
        _pendingProfileId = null;
        _controlBusy = false;
        if (_networkStatus is not null)
            _viewState = DeriveViewState(_networkStatus, _profiles.Count);
    }

    private WidgetSavedNetworkProfile? SelectedProfileLocked()
    {
        if (_profiles.Count == 0 || _selectedIndex < 0 || _selectedIndex >= _profiles.Count)
            return null;
        return _profiles[_selectedIndex];
    }

    private string ProfileNameLocked(string profileId) =>
        _profiles.FirstOrDefault(profile =>
            string.Equals(profile.ProfileId, profileId, StringComparison.Ordinal))?.DisplayName
        ?? "saved network";

    private bool IsCurrentRun(long generation, CancellationToken cancellationToken)
    {
        lock (_stateLock)
            return _runGeneration == generation && !cancellationToken.IsCancellationRequested;
    }

    private void SetProviderError(NetworkControlsViewState state, string message, long generation)
    {
        lock (_stateLock)
        {
            if (_runGeneration != generation) return;
            _networkStatus = null;
            _authoritativeStatus = null;
            _profiles = [];
            _authoritativeProfiles = [];
            _selectedProfileId = null;
            _selectedIndex = 0;
            _pendingProfileId = null;
            _controlBusy = false;
            _viewState = state;
            _status = message;
            _statusIsError = true;
        }
        Invalidate();
    }

    private static WidgetNetworkStatus NormalizeStatus(WidgetNetworkStatus status) => status with
    {
        ActiveProfileId = string.IsNullOrWhiteSpace(status.ActiveProfileId)
            ? null
            : status.ActiveProfileId.Trim(),
        ActiveProfileName = string.IsNullOrWhiteSpace(status.ActiveProfileName)
            ? null
            : status.ActiveProfileName.Trim(),
        SignalPercent = status.SignalPercent is { } signal ? Math.Clamp(signal, 0, 100) : null,
        AttemptProfileId = string.IsNullOrWhiteSpace(status.AttemptProfileId)
            ? null
            : status.AttemptProfileId.Trim(),
    };

    private static List<WidgetSavedNetworkProfile> NormalizeProfiles(
        IReadOnlyList<WidgetSavedNetworkProfile>? profiles)
    {
        if (profiles is null) return [];
        var result = new List<WidgetSavedNetworkProfile>(profiles.Count);
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var profile in profiles)
        {
            if (profile is null || string.IsNullOrWhiteSpace(profile.ProfileId)) continue;
            var id = profile.ProfileId.Trim();
            if (!ids.Add(id)) continue;
            result.Add(profile with
            {
                ProfileId = id,
                DisplayName = string.IsNullOrWhiteSpace(profile.DisplayName)
                    ? "Saved network"
                    : profile.DisplayName.Trim(),
                SignalPercent = profile.SignalPercent is { } signal
                    ? Math.Clamp(signal, 0, 100)
                    : null,
            });
        }
        return result;
    }

    private static NetworkControlsViewState DeriveViewState(
        WidgetNetworkStatus status,
        int profileCount)
    {
        if (status.Connectivity == WidgetNetworkConnectivity.None)
        {
            if (status.WirelessAvailability == WidgetNetworkWirelessAvailability.RadioOff)
                return NetworkControlsViewState.RadioOff;
            if (status.WirelessAvailability == WidgetNetworkWirelessAvailability.ServiceUnavailable)
                return NetworkControlsViewState.WirelessUnavailable;
            return NetworkControlsViewState.Offline;
        }
        return profileCount == 0
            ? NetworkControlsViewState.EmptyProfiles
            : NetworkControlsViewState.Ready;
    }

    private static string LiveStatus(WidgetNetworkStatus status, int profileCount)
    {
        var profiles = profileCount == 1 ? "1 saved network" : $"{profileCount} saved networks";
        return status.Connectivity switch
        {
            WidgetNetworkConnectivity.Internet => $"Internet access · {profiles} · live updates",
            WidgetNetworkConnectivity.Local => $"Local network only · {profiles} · live updates",
            _ when status.WirelessAvailability == WidgetNetworkWirelessAvailability.RadioOff =>
                "Wi-Fi radio is off · live updates",
            _ => $"Offline · {profiles} · live updates",
        };
    }

    private static bool IsConnected(WidgetNetworkStatus? status) =>
        status?.Connectivity is WidgetNetworkConnectivity.Internet or WidgetNetworkConnectivity.Local;

    private static (string Title, string Detail, bool IsError)? WirelessNote(WidgetNetworkStatus status)
    {
        if (status.DetailsAccess == WidgetNetworkDetailsAccess.PrivacyRestricted)
            return ("WI-FI DETAILS HIDDEN",
                "Windows privacy access is required to show the current Wi-Fi name and signal. Ethernet remains available.",
                false);
        if (status.DetailsAccess == WidgetNetworkDetailsAccess.Unavailable)
            return ("WI-FI DETAILS UNAVAILABLE",
                "Windows did not provide Wi-Fi profile or signal details.",
                false);
        return status.WirelessAvailability switch
        {
            WidgetNetworkWirelessAvailability.RadioOff => (
                "WI-FI RADIO OFF",
                "Turn Wi-Fi on in Windows to use saved wireless profiles. Wired networking is unaffected.",
                false),
            WidgetNetworkWirelessAvailability.NoAdapter => (
                "NO WI-FI ADAPTER",
                "No wireless adapter is currently available. Wired networking is unaffected.",
                false),
            WidgetNetworkWirelessAvailability.ServiceUnavailable => (
                "WI-FI SERVICE UNAVAILABLE",
                "Windows wireless service is unavailable. The widget will update when the provider reports recovery.",
                true),
            _ => null,
        };
    }

    private static (NetworkControlsViewState State, string Message) MapCapabilityFailure(
        string errorCode) => errorCode switch
        {
            "permission_denied" or "capability_revoked" or "capability_not_declared" =>
                (NetworkControlsViewState.PermissionDenied, "Network read permission denied"),
            "lifecycle_denied" =>
                (NetworkControlsViewState.LifecycleDenied, "Network request denied by widget lifecycle"),
            "channel_closed" =>
                (NetworkControlsViewState.ChannelClosed, "Network service channel closed"),
            _ => (NetworkControlsViewState.Error, "Network provider request failed"),
        };

    private static string MapControlFailure(string errorCode) => errorCode switch
    {
        "permission_denied" or "capability_revoked" or "capability_not_declared" =>
            "Saved-network switching permission denied · previous connection retained",
        "lifecycle_denied" =>
            "Network control paused by lifecycle · previous connection retained",
        "channel_closed" =>
            "Network service disconnected · previous connection retained",
        _ => "Connection request failed · previous connection retained",
    };
}
