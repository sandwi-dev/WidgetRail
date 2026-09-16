using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

namespace WidgetRail.FirstPartyWidgets.NetworkControls;

public enum NetworkControlsViewState
{
    Initial,
    Loading,
    Ready,
    NotScanned,
    Scanning,
    EmptyNetworks,
    RadioOff,
    WirelessUnavailable,
    PreciseLocationDenied,
    PermissionDenied,
    LifecycleDenied,
    ChannelClosed,
    ServiceUnavailable,
    Error,
}

public enum NetworkControlsTab
{
    Wifi,
    Bluetooth,
}

/// <summary>
/// Event-driven, controller-first view of the Wi-Fi networks in the host's most
/// recent bounded scan. Platform work and network credentials remain in trusted
/// host services; this worker receives only sanitized display data and opaque,
/// scan-generation-bound network IDs.
/// </summary>
public sealed class NetworkControlsWidget : Widget
{
    private const string ProviderObservationOperation = "network.providers";
    private const string ConnectionDetailsRefreshOperation = "network.details.refresh";

    private readonly object _stateLock = new();
    private readonly SemaphoreSlim _commandGate = new(1, 1);
    private WidgetNetworkStatus? _networkStatus;
    private WidgetNetworkStatus? _authoritativeStatus;
    private WidgetNetworkConnectionDetails? _connectionDetails;
    private WidgetNetworkConnectionDetails? _authoritativeConnectionDetails;
    private string _connectionDetailsMessage = "Connection details are loading";
    private bool _connectionDetailsOpen;
    private WidgetAvailableWifiNetworks? _wifiSnapshot;
    private WidgetAvailableWifiNetworks? _authoritativeWifiSnapshot;
    private WidgetWifiRadio? _wifiRadio;
    private WidgetWifiRadio? _authoritativeWifiRadio;
    private WidgetBluetoothSnapshot? _bluetooth;
    private WidgetBluetoothSnapshot? _authoritativeBluetooth;
    private string _bluetoothMessage = "Bluetooth loads when this widget becomes visible";
    private bool _bluetoothIsError;
    private bool _bluetoothBusy;
    private WidgetBluetoothDevice? _bluetoothGuidanceDevice;
    private WidgetBluetoothDevice? _unpairConfirmationDevice;
    private string? _pendingBluetoothDeviceId;
    private string? _selectedBluetoothDeviceId;
    private int _selectedBluetoothIndex;
    private string? _selectedNetworkId;
    private int _selectedIndex;
    private string? _pendingNetworkId;
    private NetworkControlsViewState _viewState = NetworkControlsViewState.Initial;
    private string _status = "Network status loads when this widget becomes visible";
    private bool _statusIsError;
    private bool _controlBusy;
    private bool _scanBusy;
    private bool _radioBusy;
    private long _runGeneration;
    private int _activationCount;
    private int _statusFetchCount;
    private int _wifiFetchCount;
    private int _scanRequestCount;
    private int _radioControlCount;
    private int _bluetoothFetchCount;
    private int _bluetoothRadioControlCount;
    private int _bluetoothPairCount;
    private int _bluetoothManageCount;
    private NetworkControlsTab _activeTab;
    private NetworkWifiManagementState _management = new();
    private string? _bluetoothDetailsId;

    public NetworkControlsViewState ViewState
    {
        get { lock (_stateLock) return _viewState; }
    }

    public WidgetNetworkStatus? NetworkStatus
    {
        get { lock (_stateLock) return _networkStatus; }
    }

    public WidgetAvailableWifiNetworks? WifiSnapshot
    {
        get
        {
            lock (_stateLock)
                return _wifiSnapshot is null
                    ? null
                    : _wifiSnapshot with { Networks = _wifiSnapshot.Networks.ToArray() };
        }
    }

    public WidgetNetworkConnectionDetails? ConnectionDetails
    {
        get
        {
            lock (_stateLock)
                return _connectionDetails is null ? null : CloneDetails(_connectionDetails);
        }
    }

    public IReadOnlyList<WidgetAvailableWifiNetwork> Networks
    {
        get { lock (_stateLock) return _wifiSnapshot?.Networks.ToArray() ?? []; }
    }

    public string? SelectedNetworkId
    {
        get { lock (_stateLock) return _selectedNetworkId; }
    }

    public bool ControlBusy
    {
        get { lock (_stateLock) return _controlBusy; }
    }

    public bool ScanBusy
    {
        get { lock (_stateLock) return _scanBusy; }
    }

    public WidgetWifiRadio? WifiRadio
    {
        get { lock (_stateLock) return _wifiRadio; }
    }

    public bool RadioBusy
    {
        get { lock (_stateLock) return _radioBusy; }
    }

    public int ActivationCount => Volatile.Read(ref _activationCount);
    public int StatusFetchCount => Volatile.Read(ref _statusFetchCount);
    public int WifiFetchCount => Volatile.Read(ref _wifiFetchCount);
    public int ScanRequestCount => Volatile.Read(ref _scanRequestCount);
    public int RadioControlCount => Volatile.Read(ref _radioControlCount);
    public int BluetoothFetchCount => Volatile.Read(ref _bluetoothFetchCount);
    public int BluetoothRadioControlCount => Volatile.Read(ref _bluetoothRadioControlCount);
    public int BluetoothPairCount => Volatile.Read(ref _bluetoothPairCount);
    public int BluetoothManageCount => Volatile.Read(ref _bluetoothManageCount);
    public NetworkControlsTab ActiveTab
    {
        get { lock (_stateLock) return _activeTab; }
    }
    public WidgetBluetoothSnapshot? Bluetooth
    {
        get
        {
            lock (_stateLock)
                return _bluetooth is null
                    ? null
                    : _bluetooth with { Devices = _bluetooth.Devices.ToArray() };
        }
    }

    public override WidgetView Render() =>
        NetworkControlsPresentation.Render(CapturePresentationState());

    private NetworkControlsPresentationState CapturePresentationState()
    {
        lock (_stateLock)
            return new(
                _viewState,
                _status,
                _statusIsError,
                _networkStatus,
                _connectionDetails is null ? null : CloneDetails(_connectionDetails),
                _connectionDetailsMessage,
                _connectionDetailsOpen,
                _wifiSnapshot is null
                    ? null : _wifiSnapshot with { Networks = _wifiSnapshot.Networks.ToArray() },
                _wifiRadio,
                _controlBusy,
                _scanBusy,
                _radioBusy,
                _bluetooth is null
                    ? null : _bluetooth with { Devices = _bluetooth.Devices.ToArray() },
                _bluetoothMessage,
                _bluetoothIsError,
                _bluetoothBusy,
                _pendingBluetoothDeviceId,
                _selectedBluetoothDeviceId,
                _pendingNetworkId,
                _selectedNetworkId,
                _activeTab,
                LifecycleState == WidgetLifecycleState.Interactive,
                _unpairConfirmationDevice)
            {
                Management = _management,
                BluetoothDetails = _bluetooth?.Devices.FirstOrDefault(device => device.DeviceId == _bluetoothDetailsId),
            };
    }

    protected override ValueTask OnActivatedAsync(CancellationToken activeLifetime)
    {
        Interlocked.Increment(ref _activationCount);
        StartActiveRun();
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
        switch (NetworkControlsActionPolicy.Resolve(action.ActionId))
        {
            case NetworkControlsAction.SelectTab:
                SelectTab(action.SourceElementId);
                break;
            case NetworkControlsAction.ToggleTab:
                ToggleTab();
                break;
            case NetworkControlsAction.ManageWifi:
                await HandleWifiManagementAsync(action, cancellationToken).ConfigureAwait(false);
                break;
            case NetworkControlsAction.CloseBluetoothDetails:
                lock (_stateLock) _bluetoothDetailsId = null;
                Invalidate();
                break;
            case NetworkControlsAction.Scan:
                await RequestScanAsync(cancellationToken).ConfigureAwait(false);
                break;
            case NetworkControlsAction.ConnectWifi:
                if (SelectNetworkFromElementId(action.SourceElementId))
                {
                    lock (_stateLock)
                        if (SelectedNetworkLocked() is { IsConnected: true } connected)
                            _management = new() { Open = true, Network = connected };
                    if (_management.Open) Invalidate();
                    else await ConnectSelectedNetworkAsync(cancellationToken).ConfigureAwait(false);
                }
                break;
            case NetworkControlsAction.ToggleWifiRadio:
                await ToggleWifiRadioAsync(cancellationToken).ConfigureAwait(false);
                break;
            case NetworkControlsAction.ToggleBluetoothRadio:
                await ToggleBluetoothRadioAsync(cancellationToken).ConfigureAwait(false);
                break;
            case NetworkControlsAction.ShowBluetoothDetails:
                ShowBluetoothDeviceGuidance(action.SourceElementId);
                break;
            case NetworkControlsAction.ShowConnectionDetails:
                lock (_stateLock) _connectionDetailsOpen = true;
                Invalidate();
                break;
            case NetworkControlsAction.CloseConnectionDetails:
                lock (_stateLock) _connectionDetailsOpen = false;
                Invalidate();
                break;
            case NetworkControlsAction.PairBluetooth:
                await PairBluetoothDeviceAsync(action.SourceElementId, cancellationToken)
                    .ConfigureAwait(false);
                break;
            case NetworkControlsAction.ManageBluetooth:
                await OpenBluetoothDeviceSettingsAsync(
                    action.SourceElementId, cancellationToken).ConfigureAwait(false);
                break;
            case NetworkControlsAction.OpenUnpairBluetooth:
                string removalId;
                lock (_stateLock) removalId = _bluetoothDetailsId is { } id
                    ? NetworkControlsElementIds.Bluetooth(id) : action.SourceElementId;
                OpenBluetoothUnpairConfirmation(removalId);
                break;
            case NetworkControlsAction.ConfirmUnpairBluetooth:
                await UnpairBluetoothDeviceAsync(cancellationToken).ConfigureAwait(false);
                break;
            case NetworkControlsAction.CancelUnpairBluetooth:
                CancelBluetoothUnpairConfirmation();
                break;
            case NetworkControlsAction.Retry:
                if (IsActive) StartActiveRun();
                break;
        }
    }

    public override ValueTask<bool> OnControllerInputAsync(
        ControllerInputEvent input,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (input.Context == ControllerInputContext.OpenWidget &&
            input.FocusedElementId is { } focusedId)
        {
            // The host owns directional focus movement. Remember the last row
            // it reports so switching tabs, returning to the tray, or reopening
            // this live worker restores each view independently.
            if (ActiveTab == NetworkControlsTab.Wifi)
                SelectNetworkFromElementId(focusedId);
            else
                SelectBluetoothFromElementId(focusedId);
        }
        return base.OnControllerInputAsync(input, cancellationToken);
    }

    private async ValueTask HandleWifiManagementAsync(WidgetActionEvent action, CancellationToken cancellationToken)
    {
        if (LifecycleState != WidgetLifecycleState.Interactive) return;
        lock (_stateLock)
        {
            if (_management.Busy || _controlBusy || _radioBusy) return;
            if (!_management.Open && action.ActionId != "wifi.saved.open") return;
            var selectedProfile = _management.Profiles.FirstOrDefault(item => item.ProfileId == _management.ProfileId);
            if (action.ActionId == "wifi.forget.confirm" && (!_management.ConfirmForget || selectedProfile is not { CanManage: true }) ||
                action.ActionId == "wifi.auto.toggle" && selectedProfile is not { CanManage: true, AutoConnect: not null } ||
                action.ActionId == "wifi.saved.connect" && selectedProfile is null ||
                action.ActionId == "wifi.disconnect" && _management.Network is null) return;
            switch (action.ActionId)
            {
                case "wifi.manage.close":
                    _management = _management with { Open = _management.ProfileId is not null, ProfileId = null, Network = null, ConfirmForget = false, IsError = false, Message = "Saved networks are remembered by Windows." };
                    Invalidate(); return;
                case "wifi.profile.open":
                    if (!_management.Open || _management.Network is not null) return;
                    var profile = _management.Profiles.FirstOrDefault(item => NetworkControlsElementIds.SavedProfile(item.ProfileId) == action.SourceElementId);
                    if (profile is null) return;
                    _management = _management with { ProfileId = profile.ProfileId, ConfirmForget = false, IsError = false,
                        Message = "Choose how Windows connects to this network." };
                    Invalidate(); return;
                case "wifi.forget.open":
                    if (!_management.Profiles.Any(item => item.ProfileId == _management.ProfileId && item.CanManage)) return;
                    _management = _management with { ConfirmForget = true };
                    Invalidate(); return;
                case "wifi.forget.cancel":
                    _management = _management with { ConfirmForget = false };
                    Invalidate(); return;
            }
        }
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, ActiveCancellationToken);
        if (!await _commandGate.WaitAsync(0, linked.Token).ConfigureAwait(false)) return;
        long generation;
        NetworkWifiManagementState state;
        lock (_stateLock)
        {
            generation = _runGeneration;
            state = _management;
            _management = state with { Open = true, Busy = true, IsError = false, Message = "Updating Wi-Fi…" };
        }
        Invalidate();
        try
        {
            var selected = state.Profiles.FirstOrDefault(item => item.ProfileId == state.ProfileId);
            var message = "Saved networks are remembered by Windows.";
            switch (action.ActionId)
            {
                case "wifi.saved.open":
                case "wifi.saved.refresh": break;
                case "wifi.disconnect":
                    if (state.Network is null) return;
                    await HostServices.Network.DisconnectWifiAsync(state.Network.NetworkId, linked.Token).ConfigureAwait(false);
                    message = "Disconnect requested. Wi-Fi remains on.";
                    break;
                case "wifi.auto.toggle":
                    if (selected is not { CanManage: true, AutoConnect: not null }) return;
                    await HostServices.Network.SetWifiAutoConnectAsync(selected.ProfileId, !selected.AutoConnect.Value, linked.Token).ConfigureAwait(false);
                    message = "Automatic connection preference updated.";
                    break;
                case "wifi.forget.confirm":
                    if (!state.ConfirmForget || selected is not { CanManage: true }) return;
                    await HostServices.Network.ForgetWifiProfileAsync(selected.ProfileId, linked.Token).ConfigureAwait(false);
                    message = "Saved network forgotten. Connecting again may require a password.";
                    break;
                case "wifi.saved.connect":
                    if (selected is null) return;
                    await HostServices.Network.SwitchSavedProfileAsync(selected.ProfileId, linked.Token).ConfigureAwait(false);
                    message = "Connection requested. The network must be nearby.";
                    break;
                default: return;
            }
            var profiles = await HostServices.Network.GetSavedProfilesAsync(linked.Token).ConfigureAwait(false);
            lock (_stateLock)
            {
                if (generation != _runGeneration || linked.IsCancellationRequested) return;
                _management = _management with
                {
                    Profiles = profiles.Take(128).ToArray(), Message = message, IsError = false,
                    ProfileId = profiles.Any(item => item.ProfileId == state.ProfileId) ? state.ProfileId : null,
                    ConfirmForget = false,
                    Open = action.ActionId != "wifi.disconnect",
                    Network = action.ActionId == "wifi.disconnect" ? null : _management.Network,
                };
                if (action.ActionId == "wifi.disconnect") { _status = message; _statusIsError = false; }
            }
        }
        catch (OperationCanceledException) when (linked.IsCancellationRequested) { }
        catch (Exception exception)
        {
            var message = exception is WidgetCapabilityException failure ? failure.ErrorCode switch
            {
                "permission_denied" or "capability_revoked" or "capability_not_declared" =>
                    "Allow Wi-Fi management or saved-network connection in Settings → Permissions.",
                "wifi_profile_policy_denied" => "Windows policy prevents changing this network.",
                "resource_not_found" => "That network has changed. Go back and refresh the list.",
                "provider_busy" => "A connection is already in progress. Try again when it finishes.",
                "lifecycle_denied" => "Open Network Controls before changing Wi-Fi.",
                _ => "Windows could not complete the Wi-Fi action. Try again.",
            } : "Windows could not complete the Wi-Fi action. Try again.";
            lock (_stateLock)
                if (generation == _runGeneration)
                    _management = _management with { Message = message, IsError = true, ConfirmForget = false };
        }
        finally
        {
            lock (_stateLock)
                if (generation == _runGeneration) _management = _management with { Busy = false };
            _commandGate.Release();
            Invalidate();
        }
    }

    private void SelectTab(string sourceElementId)
    {
        var next = sourceElementId switch
        {
            "network.tab.wifi" => NetworkControlsTab.Wifi,
            "network.tab.bluetooth" => NetworkControlsTab.Bluetooth,
            _ => (NetworkControlsTab?)null,
        };
        if (next is null) return;
        lock (_stateLock) _activeTab = next.Value;
        Invalidate();
    }

    private void ToggleTab()
    {
        lock (_stateLock)
            _activeTab = _activeTab == NetworkControlsTab.Wifi
                ? NetworkControlsTab.Bluetooth
                : NetworkControlsTab.Wifi;
        Invalidate();
    }

    private async Task ObserveProvidersAsync(
        long generation,
        WidgetOperationContext operation)
    {
        await Task.WhenAll(
            ObserveNetworkAsync(generation, operation),
            ObserveConnectionDetailsAsync(generation, operation),
            ObserveBluetoothAsync(generation, operation)).ConfigureAwait(false);
    }

    private async Task ObserveConnectionDetailsAsync(
        long generation,
        WidgetOperationContext operation)
    {
        var cancellationToken = operation.CancellationToken;
        try
        {
            await using var subscription = await HostServices.Network
                .OpenConnectionDetailsSubscriptionAsync(cancellationToken)
                .ConfigureAwait(false);
            await RefreshConnectionDetailsAsync(generation, operation).ConfigureAwait(false);
            await foreach (var change in subscription.ReadAllAsync(cancellationToken)
                               .WithCancellation(cancellationToken).ConfigureAwait(false))
            {
                _ = change;
                if (!IsCurrentRun(generation, operation)) return;
                _ = Operations.RunLatest(
                    ConnectionDetailsRefreshOperation,
                    refresh => new ValueTask(RefreshConnectionDetailsAsync(generation, refresh)),
                    WidgetOperationLifetime.Active);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (WidgetCapabilityException exception)
        {
            SetConnectionDetailsUnavailable(exception.ErrorCode switch
            {
                "permission_denied" or "capability_not_declared" =>
                    "Allow Connection details in Settings → Permissions",
                "lifecycle_denied" => "Connection details are paused in the background",
                _ => "Connection details are unavailable",
            }, generation,
            exception.ErrorCode is "permission_denied" or "capability_not_declared"
                ? WidgetNetworkConnectionDetailsState.PrivacyDenied
                : WidgetNetworkConnectionDetailsState.Unavailable);
        }
        catch (Exception)
        {
            SetConnectionDetailsUnavailable(
                "Connection details are unavailable", generation,
                WidgetNetworkConnectionDetailsState.Unavailable);
        }
    }

    private async Task RefreshConnectionDetailsAsync(
        long generation,
        WidgetOperationContext operation)
    {
        try
        {
            var details = await HostServices.Network.GetConnectionDetailsAsync(
                operation.CancellationToken).ConfigureAwait(false);
            if (!IsCurrentRun(generation, operation)) return;
            var normalized = NormalizeDetails(details);
            lock (_stateLock)
            {
                if (_runGeneration != generation || !operation.IsCurrent) return;
                _authoritativeConnectionDetails = normalized;
                _connectionDetails = normalized;
                _connectionDetailsMessage = DetailsMessage(normalized.State);
            }
            Invalidate();
        }
        catch (OperationCanceledException) when (operation.CancellationToken.IsCancellationRequested) { }
        catch (WidgetCapabilityException exception)
        {
            SetConnectionDetailsUnavailable(exception.ErrorCode switch
            {
                "permission_denied" or "capability_not_declared" =>
                    "Allow Connection details in Settings → Permissions",
                _ => "Connection details are unavailable",
            }, generation,
            exception.ErrorCode is "permission_denied" or "capability_not_declared"
                ? WidgetNetworkConnectionDetailsState.PrivacyDenied
                : WidgetNetworkConnectionDetailsState.Unavailable);
        }
    }

    private async Task ObserveNetworkAsync(
        long generation,
        WidgetOperationContext operation)
    {
        var cancellationToken = operation.CancellationToken;
        try
        {
            await using var statusSubscription = await HostServices.Network
                .OpenStatusSubscriptionAsync(cancellationToken).ConfigureAwait(false);
            await using var wifiSubscription = await HostServices.Network
                .OpenAvailableWifiSubscriptionAsync(cancellationToken).ConfigureAwait(false);
            await using var radioSubscription = await HostServices.Network
                .OpenWifiRadioSubscriptionAsync(cancellationToken).ConfigureAwait(false);

            Interlocked.Increment(ref _statusFetchCount);
            var statusTask = HostServices.Network.GetStatusAsync(cancellationToken).AsTask();
            Interlocked.Increment(ref _wifiFetchCount);
            var wifiTask = HostServices.Network.GetAvailableWifiAsync(cancellationToken).AsTask();
            var radioTask = HostServices.Network.GetWifiRadioAsync(cancellationToken).AsTask();
            await Task.WhenAll(statusTask, wifiTask, radioTask).ConfigureAwait(false);
            if (!IsCurrentRun(generation, operation)) return;
            ApplySnapshot(statusTask.Result, wifiTask.Result, radioTask.Result, generation);

            using var eventLifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var statusLoop = ObserveStatusEventsAsync(
                statusSubscription, generation, operation, eventLifetime.Token);
            var wifiLoop = ObserveWifiEventsAsync(
                wifiSubscription, generation, operation, eventLifetime.Token);
            var radioLoop = ObserveRadioEventsAsync(
                radioSubscription, generation, operation, eventLifetime.Token);
            await Task.WhenAny(statusLoop, wifiLoop, radioLoop).ConfigureAwait(false);
            eventLifetime.Cancel();
            try { await Task.WhenAll(statusLoop, wifiLoop, radioLoop).ConfigureAwait(false); }
            catch (OperationCanceledException) when (eventLifetime.IsCancellationRequested) { }

            if (IsCurrentRun(generation, operation))
                SetProviderError(NetworkControlsViewState.ChannelClosed,
                    "Network service channel closed", generation);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (WidgetCapabilityUnavailableException)
        {
            SetProviderError(NetworkControlsViewState.ServiceUnavailable,
                "Host Wi-Fi service unavailable", generation);
        }
        catch (WidgetCapabilityException exception)
        {
            var (state, message) = NetworkControlsProviderPolicy.MapFailure(
                exception.ErrorCode);
            SetProviderError(state, message, generation);
        }
        catch (Exception)
        {
            SetProviderError(NetworkControlsViewState.Error,
                "Network provider returned an unexpected error", generation);
        }
    }

    private async Task ObserveStatusEventsAsync(
        IWidgetCapabilitySubscription<WidgetNetworkStatusChanged> subscription,
        long generation,
        WidgetOperationContext operation,
        CancellationToken cancellationToken)
    {
        await foreach (var change in subscription.ReadAllAsync(cancellationToken)
                           .WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            if (!IsCurrentRun(generation, operation)) return;
            ApplyStatus(change.Status, generation);
        }
    }

    private async Task ObserveWifiEventsAsync(
        IWidgetCapabilitySubscription<WidgetAvailableWifiNetworksChanged> subscription,
        long generation,
        WidgetOperationContext operation,
        CancellationToken cancellationToken)
    {
        await foreach (var change in subscription.ReadAllAsync(cancellationToken)
                           .WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            if (!IsCurrentRun(generation, operation)) return;
            ApplyWifi(change.Snapshot, generation);
        }
    }

    private async Task ObserveRadioEventsAsync(
        IWidgetCapabilitySubscription<WidgetWifiRadioChanged> subscription,
        long generation,
        WidgetOperationContext operation,
        CancellationToken cancellationToken)
    {
        await foreach (var change in subscription.ReadAllAsync(cancellationToken)
                           .WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            if (!IsCurrentRun(generation, operation)) return;
            ApplyRadio(change.Radio, generation);
        }
    }

    private async Task ObserveBluetoothAsync(
        long generation,
        WidgetOperationContext operation)
    {
        var cancellationToken = operation.CancellationToken;
        try
        {
            await using var subscription = await HostServices.Network
                .OpenBluetoothSubscriptionAsync(cancellationToken).ConfigureAwait(false);
            Interlocked.Increment(ref _bluetoothFetchCount);
            var snapshot = await HostServices.Network.GetBluetoothAsync(cancellationToken)
                .ConfigureAwait(false);
            if (!IsCurrentRun(generation, operation)) return;
            ApplyBluetooth(snapshot, generation);
            await foreach (var change in subscription.ReadAllAsync(cancellationToken)
                               .WithCancellation(cancellationToken).ConfigureAwait(false))
            {
                if (!IsCurrentRun(generation, operation)) return;
                ApplyBluetooth(change.Snapshot, generation);
            }
            if (IsCurrentRun(generation, operation))
                SetBluetoothUnavailable("Bluetooth service channel closed", true, generation);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (WidgetCapabilityUnavailableException)
        {
            SetBluetoothUnavailable("Host Bluetooth service unavailable", true, generation);
        }
        catch (WidgetCapabilityException exception)
        {
            var message = exception.ErrorCode switch
            {
                "permission_denied" or "capability_not_declared" =>
                    "Allow Bluetooth device access in Settings → Permissions",
                "lifecycle_denied" => "Bluetooth is paused while this widget is in the background",
                "channel_closed" => "Bluetooth service channel closed",
                _ => "Windows Bluetooth service unavailable",
            };
            SetBluetoothUnavailable(message, true, generation);
        }
        catch (Exception)
        {
            SetBluetoothUnavailable("Windows Bluetooth service unavailable", true, generation);
        }
    }

    private void ApplyBluetooth(WidgetBluetoothSnapshot incoming, long generation)
    {
        var snapshot = NetworkControlsProviderPolicy.Normalize(incoming);
        lock (_stateLock)
        {
            if (_runGeneration != generation) return;
            var preserveGuidance = _bluetoothGuidanceDevice is { } guidance &&
                snapshot.Devices.FirstOrDefault(device => string.Equals(
                    device.DeviceId, guidance.DeviceId, StringComparison.Ordinal)) is { } current &&
                current == guidance;
            var preserveOperationMessage = _pendingBluetoothDeviceId is not null;
            _authoritativeBluetooth = snapshot;
            _bluetooth = snapshot;
            if (_unpairConfirmationDevice is { } confirmation &&
                snapshot.Devices.FirstOrDefault(device => string.Equals(
                    device.DeviceId, confirmation.DeviceId, StringComparison.Ordinal)) is not
                    { IsPaired: true })
                _unpairConfirmationDevice = null;
            if (_pendingBluetoothDeviceId is null) _bluetoothBusy = false;
            _bluetoothIsError = false;
            if (!preserveGuidance && !preserveOperationMessage)
            {
                _bluetoothGuidanceDevice = null;
                _bluetoothMessage = snapshot.DiscoveryState switch
                {
                    WidgetBluetoothDiscoveryState.Enumerating => "Discovering devices…",
                    WidgetBluetoothDiscoveryState.Unavailable => "Device discovery unavailable",
                    _ when snapshot.Devices.Count == 0 => "No paired or nearby devices",
                    _ => $"{snapshot.Devices.Count} paired or nearby",
                };
            }
            ReconcileBluetoothSelectionLocked();
        }
        Invalidate();
    }

    private void SetBluetoothUnavailable(
        string message, bool error, long generation)
    {
        lock (_stateLock)
        {
            if (_runGeneration != generation) return;
            _bluetooth = new WidgetBluetoothSnapshot(
                WidgetBluetoothRadioState.Unavailable, false,
                WidgetBluetoothDiscoveryState.Unavailable, []);
            _authoritativeBluetooth = _bluetooth;
            _bluetoothBusy = false;
            _pendingBluetoothDeviceId = null;
            _bluetoothGuidanceDevice = null;
            _unpairConfirmationDevice = null;
            _bluetoothMessage = message;
            _bluetoothIsError = error;
            _selectedBluetoothDeviceId = null;
            _selectedBluetoothIndex = 0;
        }
        Invalidate();
    }

    private void ApplySnapshot(
        WidgetNetworkStatus incomingStatus,
        WidgetAvailableWifiNetworks incomingWifi,
        WidgetWifiRadio incomingRadio,
        long generation)
    {
        var status = NetworkControlsProviderPolicy.Normalize(incomingStatus);
        var wifi = NetworkControlsProviderPolicy.Normalize(incomingWifi);
        var radio = NetworkControlsProviderPolicy.Normalize(incomingRadio);
        lock (_stateLock)
        {
            if (_runGeneration != generation) return;
            _authoritativeStatus = status;
            _authoritativeWifiSnapshot = wifi;
            _authoritativeWifiRadio = radio;
            _networkStatus = status;
            _wifiSnapshot = wifi;
            _wifiRadio = radio;
            ReconcileSelectionLocked();
            ReconcileCommandStateLocked();
        }
        Invalidate();
    }

    private void ApplyStatus(WidgetNetworkStatus incoming, long generation)
    {
        var status = NetworkControlsProviderPolicy.Normalize(incoming);
        lock (_stateLock)
        {
            if (_runGeneration != generation) return;
            _authoritativeStatus = status;
            _networkStatus = status;
            ReconcileCommandStateLocked();
        }
        Invalidate();
    }

    private void ApplyWifi(WidgetAvailableWifiNetworks incoming, long generation)
    {
        var wifi = NetworkControlsProviderPolicy.Normalize(incoming);
        lock (_stateLock)
        {
            if (_runGeneration != generation) return;
            _authoritativeWifiSnapshot = wifi;
            _wifiSnapshot = wifi;
            _scanBusy = wifi.ScanState == WidgetWifiScanState.Scanning;
            ReconcileSelectionLocked();
            ReconcileCommandStateLocked();
        }
        Invalidate();
    }

    private void ApplyRadio(WidgetWifiRadio incoming, long generation)
    {
        var radio = NetworkControlsProviderPolicy.Normalize(incoming);
        lock (_stateLock)
        {
            if (_runGeneration != generation) return;
            _authoritativeWifiRadio = radio;
            _wifiRadio = radio;
            _radioBusy = false;
            ReconcileCommandStateLocked();
        }
        Invalidate();
    }

    private void ReconcileSelectionLocked()
    {
        var selection = NetworkControlsProviderPolicy.ReconcileWifiSelection(
            _wifiSnapshot, _networkStatus, _selectedNetworkId, _selectedIndex);
        _selectedNetworkId = selection.Id;
        _selectedIndex = selection.Index;
    }

    private void ReconcileCommandStateLocked()
    {
        if (_networkStatus is null || _wifiSnapshot is null || _wifiRadio is null) return;
        var command = NetworkControlsProviderPolicy.ReconcileWifiCommand(
            _networkStatus, _wifiSnapshot);
        _pendingNetworkId = command.PendingNetworkId;
        _controlBusy = command.ControlBusy;
        _scanBusy = command.ScanBusy;
        _status = command.Status;
        _statusIsError = command.StatusIsError;
        _viewState = command.ViewState;
    }

    private async ValueTask RequestScanAsync(CancellationToken cancellationToken)
    {
        if (LifecycleState != WidgetLifecycleState.Interactive)
        {
            SetFeedback("Open Network Controls before scanning", false);
            return;
        }
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken, ActiveCancellationToken);
        await _commandGate.WaitAsync(linked.Token).ConfigureAwait(false);
        try
        {
            lock (_stateLock)
            {
                if (_scanBusy || _controlBusy || _networkStatus is null ||
                    !NetworkControlsProviderPolicy.CanScan(_networkStatus)) return;
                _scanBusy = true;
                _status = "Requesting one Wi-Fi scan…";
                _statusIsError = false;
                _wifiSnapshot = new WidgetAvailableWifiNetworks(WidgetWifiScanState.Scanning, []);
                _viewState = NetworkControlsViewState.Scanning;
            }
            Invalidate();
            try
            {
                Interlocked.Increment(ref _scanRequestCount);
                await HostServices.Network.RequestWifiScanAsync(linked.Token).ConfigureAwait(false);
                lock (_stateLock)
                {
                    // A very fast provider can publish Ready before the ACK is
                    // observed. Do not replace that terminal event with stale
                    // local "Scanning" copy.
                    if (_scanBusy)
                    {
                        _status = "Scanning for nearby Wi-Fi networks…";
                        _statusIsError = false;
                    }
                }
                Invalidate();
            }
            catch (OperationCanceledException) when (linked.IsCancellationRequested)
            {
                RestoreAuthoritative();
                throw;
            }
            catch (WidgetCapabilityException exception)
            {
                RestoreAuthoritative(NetworkControlsCommandPolicy.MapScanFailure(
                    exception.ErrorCode));
            }
            catch (Exception)
            {
                RestoreAuthoritative("Wi-Fi scan could not be started");
            }
        }
        finally
        {
            _commandGate.Release();
        }
    }

    private async ValueTask ToggleWifiRadioAsync(CancellationToken cancellationToken)
    {
        if (LifecycleState != WidgetLifecycleState.Interactive)
        {
            SetFeedback("Open Network Controls before changing Wi-Fi", false);
            return;
        }
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken, ActiveCancellationToken);
        await _commandGate.WaitAsync(linked.Token).ConfigureAwait(false);
        try
        {
            bool enabled;
            long generation;
            lock (_stateLock)
            {
                if (_radioBusy || _controlBusy || _wifiRadio is null || !_wifiRadio.CanControl) return;
                enabled = _wifiRadio.State != WidgetWifiRadioState.On;
                generation = _runGeneration;
                _radioBusy = true;
                _status = enabled ? "Turning Wi-Fi on…" : "Turning Wi-Fi off…";
                _statusIsError = false;
            }
            Invalidate();
            try
            {
                Interlocked.Increment(ref _radioControlCount);
                await HostServices.Network.SetWifiRadioAsync(enabled, linked.Token).ConfigureAwait(false);
                // One explicit read handles the idempotent/no-event edge; normal
                // reconciliation arrives through the radio changed subscription.
                var authoritative = await HostServices.Network.GetWifiRadioAsync(linked.Token)
                    .ConfigureAwait(false);
                ApplyRadio(authoritative, generation);
            }
            catch (OperationCanceledException) when (linked.IsCancellationRequested)
            {
                RestoreWifiRadioAfterCancellation(generation);
                throw;
            }
            catch (WidgetCapabilityException exception)
            {
                SetWifiRadioFailure(NetworkControlsCommandPolicy.MapWifiRadioFailure(
                    exception.ErrorCode), generation);
            }
            catch (Exception)
            {
                SetWifiRadioFailure("Wi-Fi radio could not be changed", generation);
            }
        }
        finally { _commandGate.Release(); }
    }

    private async ValueTask ToggleBluetoothRadioAsync(CancellationToken cancellationToken)
    {
        if (LifecycleState != WidgetLifecycleState.Interactive)
        {
            SetFeedback("Open Network Controls before changing Bluetooth", false);
            return;
        }
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken, ActiveCancellationToken);
        await _commandGate.WaitAsync(linked.Token).ConfigureAwait(false);
        try
        {
            bool enabled;
            long generation;
            lock (_stateLock)
            {
                if (_bluetoothBusy || _bluetooth is null || !_bluetooth.CanControlRadio) return;
                enabled = _bluetooth.RadioState != WidgetBluetoothRadioState.On;
                generation = _runGeneration;
                _bluetoothBusy = true;
                _pendingBluetoothDeviceId = null;
                _bluetoothGuidanceDevice = null;
                _bluetoothMessage = enabled ? "Turning Bluetooth on…" : "Turning Bluetooth off…";
                _bluetoothIsError = false;
            }
            Invalidate();
            try
            {
                Interlocked.Increment(ref _bluetoothRadioControlCount);
                await HostServices.Network.SetBluetoothRadioAsync(enabled, linked.Token)
                    .ConfigureAwait(false);
                var authoritative = await HostServices.Network.GetBluetoothAsync(linked.Token)
                    .ConfigureAwait(false);
                ApplyBluetooth(authoritative, generation);
            }
            catch (OperationCanceledException) when (linked.IsCancellationRequested)
            {
                RestoreBluetoothAfterCancellation(generation);
                throw;
            }
            catch (WidgetCapabilityException exception)
            {
                var message = NetworkControlsCommandPolicy.MapBluetoothRadioFailure(
                    exception.ErrorCode);
                SetBluetoothFailure(message, generation);
            }
            catch (Exception)
            {
                SetBluetoothFailure("Bluetooth radio could not be changed", generation);
            }
        }
        finally { _commandGate.Release(); }
    }

    private async ValueTask PairBluetoothDeviceAsync(
        string sourceElementId, CancellationToken cancellationToken)
    {
        if (LifecycleState != WidgetLifecycleState.Interactive)
        {
            SetBluetoothTransientMessage(
                "Open Network Controls before pairing a Bluetooth device", true);
            return;
        }
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken, ActiveCancellationToken);
        await _commandGate.WaitAsync(linked.Token).ConfigureAwait(false);
        try
        {
            WidgetBluetoothDevice? device;
            long generation;
            lock (_stateLock)
            {
                device = BluetoothDeviceFromElementIdLocked(sourceElementId);
                if (device is null || device.IsPaired || _bluetoothBusy) return;
                generation = _runGeneration;
                _selectedBluetoothDeviceId = device.DeviceId;
                _selectedBluetoothIndex = NetworkControlsProviderPolicy.IndexOf(
                    _bluetooth?.Devices ?? [], candidate => string.Equals(
                        candidate.DeviceId, device.DeviceId, StringComparison.Ordinal));
                _pendingBluetoothDeviceId = device.DeviceId;
                _bluetoothBusy = true;
                _bluetoothGuidanceDevice = null;
                _bluetoothMessage = $"Pairing {device.DisplayName}…";
                _bluetoothIsError = false;
            }
            Invalidate();
            try
            {
                Interlocked.Increment(ref _bluetoothPairCount);
                var result = await HostServices.Network.PairBluetoothDeviceAsync(
                    device.DeviceId, linked.Token).ConfigureAwait(false);
                var authoritative = await HostServices.Network.GetBluetoothAsync(linked.Token)
                    .ConfigureAwait(false);
                ApplyBluetooth(authoritative, generation);
                CompleteBluetoothPairing(device, result.Outcome, generation);
            }
            catch (OperationCanceledException) when (linked.IsCancellationRequested)
            {
                RestoreBluetoothAfterCancellation(generation);
                throw;
            }
            catch (WidgetCapabilityException exception)
            {
                SetBluetoothFailure(NetworkControlsCommandPolicy.MapBluetoothPairFailure(
                    exception.ErrorCode), generation);
            }
            catch (Exception)
            {
                SetBluetoothFailure("Windows could not start Bluetooth pairing", generation);
            }
        }
        finally { _commandGate.Release(); }
    }

    private async ValueTask OpenBluetoothDeviceSettingsAsync(
        string sourceElementId, CancellationToken cancellationToken)
    {
        if (LifecycleState != WidgetLifecycleState.Interactive)
        {
            SetBluetoothTransientMessage(
                "Open Network Controls before managing a Bluetooth device", true);
            return;
        }
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken, ActiveCancellationToken);
        await _commandGate.WaitAsync(linked.Token).ConfigureAwait(false);
        try
        {
            WidgetBluetoothDevice? device;
            long generation;
            lock (_stateLock)
            {
                device = BluetoothDeviceFromElementIdLocked(sourceElementId);
                if (device is null || _bluetoothBusy) return;
                generation = _runGeneration;
                _selectedBluetoothDeviceId = device.DeviceId;
                _selectedBluetoothIndex = NetworkControlsProviderPolicy.IndexOf(
                    _bluetooth?.Devices ?? [], candidate => string.Equals(
                        candidate.DeviceId, device.DeviceId, StringComparison.Ordinal));
                _pendingBluetoothDeviceId = device.DeviceId;
                _bluetoothBusy = true;
                _bluetoothGuidanceDevice = null;
                _bluetoothMessage = $"Opening Windows controls for {device.DisplayName}…";
                _bluetoothIsError = false;
            }
            Invalidate();
            try
            {
                Interlocked.Increment(ref _bluetoothManageCount);
                await HostServices.Network.OpenBluetoothDeviceSettingsAsync(
                    device.DeviceId, linked.Token).ConfigureAwait(false);
                var authoritative = await HostServices.Network.GetBluetoothAsync(linked.Token)
                    .ConfigureAwait(false);
                ApplyBluetooth(authoritative, generation);
                lock (_stateLock)
                {
                    if (_runGeneration != generation) return;
                    _pendingBluetoothDeviceId = null;
                    _bluetoothBusy = false;
                    _bluetoothGuidanceDevice = CurrentBluetoothDeviceLocked(device.DeviceId);
                    _bluetoothMessage = $"Windows Bluetooth Settings opened for {device.DisplayName}";
                    _bluetoothIsError = false;
                }
                Invalidate();
            }
            catch (OperationCanceledException) when (linked.IsCancellationRequested)
            {
                RestoreBluetoothAfterCancellation(generation);
                throw;
            }
            catch (WidgetCapabilityException exception)
            {
                SetBluetoothFailure(NetworkControlsCommandPolicy.MapBluetoothManageFailure(
                    exception.ErrorCode), generation);
            }
            catch (Exception)
            {
                SetBluetoothFailure(
                    "Windows Bluetooth Settings could not be opened", generation);
            }
        }
        finally { _commandGate.Release(); }
    }

    private void OpenBluetoothUnpairConfirmation(string sourceElementId)
    {
        lock (_stateLock)
        {
            if (LifecycleState != WidgetLifecycleState.Interactive || _bluetoothBusy) return;
            var device = BluetoothDeviceFromElementIdLocked(sourceElementId);
            if (device is not { IsPaired: true }) return;
            _selectedBluetoothDeviceId = device.DeviceId;
            _unpairConfirmationDevice = device;
            _bluetoothDetailsId = null;
        }
        Invalidate();
    }

    private void CancelBluetoothUnpairConfirmation()
    {
        lock (_stateLock)
        {
            if (_bluetoothBusy) return;
            _unpairConfirmationDevice = null;
        }
        Invalidate();
    }

    private async ValueTask UnpairBluetoothDeviceAsync(CancellationToken cancellationToken)
    {
        if (LifecycleState != WidgetLifecycleState.Interactive) return;
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken, ActiveCancellationToken);
        await _commandGate.WaitAsync(linked.Token).ConfigureAwait(false);
        try
        {
            WidgetBluetoothDevice? device;
            long generation = 0;
            lock (_stateLock)
            {
                device = _unpairConfirmationDevice;
                var current = device is null ? null : CurrentBluetoothDeviceLocked(device.DeviceId);
                if (current is not { IsPaired: true })
                {
                    _unpairConfirmationDevice = null;
                    device = null;
                }
                else if (!_bluetoothBusy)
                {
                    generation = _runGeneration;
                    _pendingBluetoothDeviceId = current.DeviceId;
                    _bluetoothBusy = true;
                    _bluetoothMessage = $"Removing {current.DisplayName}…";
                    _bluetoothIsError = false;
                    device = current;
                }
                else device = null;
            }
            Invalidate();
            if (device is null) return;
            try
            {
                var result = await HostServices.Network.UnpairBluetoothDeviceAsync(
                    device.DeviceId, linked.Token).ConfigureAwait(false);
                var authoritative = await HostServices.Network.GetBluetoothAsync(linked.Token)
                    .ConfigureAwait(false);
                ApplyBluetooth(authoritative, generation);
                lock (_stateLock)
                {
                    if (_runGeneration != generation) return;
                    var retained = CurrentBluetoothDeviceLocked(device.DeviceId);
                    _pendingBluetoothDeviceId = null;
                    _bluetoothBusy = false;
                    _unpairConfirmationDevice = null;
                    var removed = retained is not { IsPaired: true } &&
                        result.Outcome is WidgetBluetoothUnpairingOutcome.Unpaired or
                            WidgetBluetoothUnpairingOutcome.AlreadyUnpaired;
                    _bluetoothMessage = removed
                        ? $"Removed {device.DisplayName}"
                        : result.Outcome switch
                        {
                            WidgetBluetoothUnpairingOutcome.AccessDenied =>
                                "Windows denied Bluetooth device removal",
                            WidgetBluetoothUnpairingOutcome.OperationInProgress =>
                                "Another Bluetooth operation is still in progress",
                            WidgetBluetoothUnpairingOutcome.DeviceUnavailable =>
                                "The Bluetooth device is no longer available",
                            _ => "Windows could not remove the Bluetooth device",
                        };
                    _bluetoothIsError = !removed;
                }
                Invalidate();
            }
            catch (OperationCanceledException) when (linked.IsCancellationRequested)
            {
                lock (_stateLock) _unpairConfirmationDevice = null;
                RestoreBluetoothAfterCancellation(generation);
                throw;
            }
            catch (WidgetCapabilityException exception)
            {
                lock (_stateLock) _unpairConfirmationDevice = null;
                SetBluetoothFailure(
                    NetworkControlsCommandPolicy.MapBluetoothUnpairFailure(exception.ErrorCode),
                    generation);
            }
            catch (Exception)
            {
                lock (_stateLock) _unpairConfirmationDevice = null;
                SetBluetoothFailure("Windows could not remove the Bluetooth device", generation);
            }
        }
        finally { _commandGate.Release(); }
    }

    private void CompleteBluetoothPairing(
        WidgetBluetoothDevice requested,
        WidgetBluetoothPairingOutcome outcome,
        long generation)
    {
        var feedback = NetworkControlsCommandPolicy.PairingFeedback(requested, outcome);
        lock (_stateLock)
        {
            if (_runGeneration != generation) return;
            _pendingBluetoothDeviceId = null;
            _bluetoothBusy = false;
            _bluetoothGuidanceDevice = CurrentBluetoothDeviceLocked(requested.DeviceId);
            _bluetoothMessage = feedback.Message;
            _bluetoothIsError = feedback.IsError;
        }
        Invalidate();
    }

    private void RestoreWifiRadioAfterCancellation(long generation)
    {
        lock (_stateLock)
        {
            if (_runGeneration != generation) return;
            _wifiRadio = _authoritativeWifiRadio;
            _radioBusy = false;
            if (_networkStatus is not null && _wifiSnapshot is not null)
            {
                _status = NetworkControlsProviderPolicy.LiveStatus(
                    _networkStatus, _wifiSnapshot);
                _statusIsError = false;
            }
        }
        Invalidate();
    }

    private void SetWifiRadioFailure(string message, long generation)
    {
        lock (_stateLock)
        {
            if (_runGeneration != generation) return;
            _wifiRadio = _authoritativeWifiRadio;
            _radioBusy = false;
            _status = message;
            _statusIsError = true;
        }
        Invalidate();
    }

    private void RestoreBluetoothAfterCancellation(long generation)
    {
        lock (_stateLock)
        {
            if (_runGeneration != generation) return;
            _bluetooth = _authoritativeBluetooth;
            _bluetoothBusy = false;
            _pendingBluetoothDeviceId = null;
            _bluetoothGuidanceDevice = null;
            _bluetoothIsError = false;
        }
        Invalidate();
    }

    private void SetBluetoothFailure(string message, long generation)
    {
        lock (_stateLock)
        {
            if (_runGeneration != generation) return;
            _bluetoothBusy = false;
            _pendingBluetoothDeviceId = null;
            _bluetooth = _authoritativeBluetooth;
            _bluetoothMessage = message;
            _bluetoothIsError = true;
        }
        Invalidate();
    }

    private async ValueTask ConnectSelectedNetworkAsync(CancellationToken cancellationToken)
    {
        if (LifecycleState != WidgetLifecycleState.Interactive)
        {
            SetFeedback("Open Network Controls before connecting", false);
            return;
        }
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken, ActiveCancellationToken);
        await _commandGate.WaitAsync(linked.Token).ConfigureAwait(false);
        try
        {
            WidgetAvailableWifiNetwork? selected;
            lock (_stateLock)
            {
                selected = SelectedNetworkLocked();
                var admission = NetworkControlsCommandPolicy.AdmitConnection(
                    selected, _controlBusy, _scanBusy);
                if (admission.Kind == NetworkConnectionAdmissionKind.Rejected) return;
                _status = admission.Message;
                _statusIsError = admission.IsError;
                if (admission.Kind == NetworkConnectionAdmissionKind.Guidance)
                    selected = null;
                else
                {
                    _pendingNetworkId = selected!.NetworkId;
                    _controlBusy = true;
                }
            }
            Invalidate();
            if (selected is null) return;
            try
            {
                await HostServices.Network.ConnectAvailableWifiAsync(
                    selected.NetworkId, linked.Token).ConfigureAwait(false);
                lock (_stateLock)
                {
                    // Status completion can outrun the acknowledgement. Only
                    // retain the waiting copy while this exact attempt is live.
                    if (_controlBusy && string.Equals(
                            _pendingNetworkId, selected.NetworkId, StringComparison.Ordinal))
                    {
                        _status = $"Connection request accepted for {selected.DisplayName} · waiting for Windows";
                        _statusIsError = false;
                    }
                }
                Invalidate();
            }
            catch (OperationCanceledException) when (linked.IsCancellationRequested)
            {
                RestoreAuthoritative();
                throw;
            }
            catch (WidgetCapabilityException exception)
            {
                RestoreAuthoritative(NetworkControlsCommandPolicy.MapConnectFailure(
                    exception.ErrorCode));
            }
            catch (Exception)
            {
                RestoreAuthoritative("Connection request failed · previous connection retained");
            }
        }
        finally
        {
            _commandGate.Release();
        }
    }

    private bool SelectNetworkFromElementId(string elementId)
    {
        lock (_stateLock)
        {
            var networks = _wifiSnapshot?.Networks ?? [];
            var index = NetworkControlsProviderPolicy.IndexOf(networks, network =>
                string.Equals(NetworkControlsElementIds.Wifi(network.NetworkId), elementId,
                    StringComparison.Ordinal));
            if (index < 0) return false;
            _selectedIndex = index;
            _selectedNetworkId = networks[index].NetworkId;
            return true;
        }
    }

    private bool SelectBluetoothFromElementId(string elementId)
    {
        lock (_stateLock)
        {
            var devices = _bluetooth?.Devices ?? [];
            var index = NetworkControlsProviderPolicy.IndexOf(devices, device => string.Equals(
                NetworkControlsElementIds.Bluetooth(device.DeviceId), elementId,
                StringComparison.Ordinal));
            if (index < 0) return false;
            _selectedBluetoothIndex = index;
            _selectedBluetoothDeviceId = devices[index].DeviceId;
            return true;
        }
    }

    private WidgetBluetoothDevice? BluetoothDeviceFromElementIdLocked(string elementId)
    {
        var devices = _bluetooth?.Devices ?? [];
        return devices.FirstOrDefault(device => string.Equals(
            NetworkControlsElementIds.Bluetooth(device.DeviceId), elementId,
            StringComparison.Ordinal));
    }

    private WidgetBluetoothDevice? CurrentBluetoothDeviceLocked(string deviceId) =>
        (_bluetooth?.Devices ?? []).FirstOrDefault(device => string.Equals(
            device.DeviceId, deviceId, StringComparison.Ordinal));

    private void SetBluetoothTransientMessage(string message, bool error)
    {
        lock (_stateLock)
        {
            _bluetoothMessage = message;
            _bluetoothIsError = error;
        }
        Invalidate();
    }

    private void ShowBluetoothDeviceGuidance(string elementId)
    {
        lock (_stateLock)
        {
            var devices = _bluetooth?.Devices ?? [];
            var index = NetworkControlsProviderPolicy.IndexOf(devices, device => string.Equals(
                NetworkControlsElementIds.Bluetooth(device.DeviceId), elementId,
                StringComparison.Ordinal));
            if (index < 0) return;

            _selectedBluetoothIndex = index;
            _selectedBluetoothDeviceId = devices[index].DeviceId;
            var device = devices[index];
            _bluetoothGuidanceDevice = device;
            _bluetoothDetailsId = device.DeviceId;
            _bluetoothMessage = device.IsConnected
                ? $"{device.DisplayName} is connected · press A to manage it in Windows Settings"
                : device.IsPaired && device.IsPresent
                    ? $"{device.DisplayName} is paired · press A to manage it in Windows Settings"
                    : device.IsPaired
                        ? $"{device.DisplayName} is paired but not nearby · manage it in Windows Settings"
                        : $"{device.DisplayName} is nearby · press A to pair";
            _bluetoothIsError = false;
        }
        Invalidate();
    }

    private WidgetAvailableWifiNetwork? SelectedNetworkLocked()
    {
        var networks = _wifiSnapshot?.Networks ?? [];
        return _selectedIndex >= 0 && _selectedIndex < networks.Count ? networks[_selectedIndex] : null;
    }

    private static string BluetoothDeviceDetail(WidgetBluetoothDevice device) =>
        device.IsConnected
            ? "Connected · press A to manage in Windows Settings"
            : device.IsPaired
                ? device.IsPresent
                    ? "Paired · press A to manage in Windows Settings"
                    : "Paired · not currently nearby · press A to manage"
                : "Nearby · press A to pair";

    private void StartActiveRun()
    {
        long generation;
        lock (_stateLock)
        {
            generation = ++_runGeneration;
            RestoreAuthoritativeLocked();
            _viewState = NetworkControlsViewState.Loading;
            _status = "Loading network status…";
            _statusIsError = false;
        }
        Invalidate();
        Operations.RunLatest(
            ProviderObservationOperation,
            operation => new ValueTask(ObserveProvidersAsync(generation, operation)),
            WidgetOperationLifetime.Active);
    }

    private void StopActiveRun()
    {
        lock (_stateLock)
        {
            ++_runGeneration;
            RestoreAuthoritativeLocked();
        }
    }

    private void RestoreAuthoritative(string? message = null)
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
        _management = new();
        _bluetoothDetailsId = null;
        _networkStatus = _authoritativeStatus;
        _connectionDetails = _authoritativeConnectionDetails is null
            ? null : CloneDetails(_authoritativeConnectionDetails);
        _wifiSnapshot = _authoritativeWifiSnapshot;
        _wifiRadio = _authoritativeWifiRadio;
        _bluetooth = _authoritativeBluetooth;
        _pendingNetworkId = null;
        _controlBusy = false;
        _scanBusy = _wifiSnapshot?.ScanState == WidgetWifiScanState.Scanning;
        _radioBusy = false;
        _bluetoothBusy = false;
        _pendingBluetoothDeviceId = null;
        _bluetoothGuidanceDevice = null;
        _unpairConfirmationDevice = null;
        if (_networkStatus is not null && _wifiSnapshot is not null && _wifiRadio is not null)
        {
            ReconcileSelectionLocked();
            _viewState = NetworkControlsProviderPolicy.DeriveViewState(
                _networkStatus, _wifiSnapshot);
        }
    }

    private void SetFeedback(string message, bool error, bool preserveBusy = false)
    {
        lock (_stateLock)
        {
            _status = message;
            _statusIsError = error;
            if (!preserveBusy)
            {
                _controlBusy = false;
                _pendingNetworkId = null;
            }
        }
        Invalidate();
    }

    private bool IsCurrentRun(long generation, WidgetOperationContext operation)
    {
        lock (_stateLock)
            return _runGeneration == generation && operation.IsCurrent;
    }

    private void SetProviderError(NetworkControlsViewState state, string message, long generation)
    {
        lock (_stateLock)
        {
            if (_runGeneration != generation) return;
            _networkStatus = null;
            _authoritativeStatus = null;
            _wifiSnapshot = null;
            _authoritativeWifiSnapshot = null;
            _wifiRadio = null;
            _authoritativeWifiRadio = null;
            _selectedNetworkId = null;
            _selectedIndex = 0;
            _pendingNetworkId = null;
            _controlBusy = false;
            _scanBusy = false;
            _radioBusy = false;
            _viewState = state;
            _status = message;
            _statusIsError = true;
        }
        Invalidate();
    }

    private void SetConnectionDetailsUnavailable(
        string message,
        long generation,
        WidgetNetworkConnectionDetailsState state)
    {
        lock (_stateLock)
        {
            if (_runGeneration != generation) return;
            var unavailable = new WidgetNetworkConnectionDetails(
                (_connectionDetails?.Revision ?? 0) + 1,
                state,
                WidgetNetworkConnectionDetailsConnectivity.None,
                WidgetNetworkTransportKind.None, [], [], []);
            _authoritativeConnectionDetails = unavailable;
            _connectionDetails = unavailable;
            _connectionDetailsMessage = message;
        }
        Invalidate();
    }

    private static WidgetNetworkConnectionDetails NormalizeDetails(
        WidgetNetworkConnectionDetails details)
    {
        ArgumentNullException.ThrowIfNull(details);
        if (details.Revision < 0 || !Enum.IsDefined(details.State) ||
            !Enum.IsDefined(details.Connectivity) || !Enum.IsDefined(details.Transport) ||
            details.IpAddresses is null || details.DefaultGateways is null ||
            details.DnsServers is null || details.IpAddresses.Count > 8 ||
            details.DefaultGateways.Count > 4 || details.DnsServers.Count > 8)
            throw new WidgetCapabilityException(
                "malformed_response", "Connection details are invalid.");
        var values = details.IpAddresses.Concat(details.DefaultGateways)
            .Concat(details.DnsServers).ToArray();
        if (values.Any(value => string.IsNullOrWhiteSpace(value) || value.Length > 64 ||
                !System.Net.IPAddress.TryParse(value, out _)) ||
            details.IpAddresses.Distinct(StringComparer.Ordinal).Count() !=
                details.IpAddresses.Count ||
            details.DefaultGateways.Distinct(StringComparer.Ordinal).Count() !=
                details.DefaultGateways.Count ||
            details.DnsServers.Distinct(StringComparer.Ordinal).Count() !=
                details.DnsServers.Count ||
            details.State != WidgetNetworkConnectionDetailsState.Available &&
                values.Length != 0)
            throw new WidgetCapabilityException(
                "malformed_response", "Connection details are invalid.");
        return CloneDetails(details);
    }

    private static WidgetNetworkConnectionDetails CloneDetails(
        WidgetNetworkConnectionDetails details) => details with
        {
            IpAddresses = details.IpAddresses.ToArray(),
            DefaultGateways = details.DefaultGateways.ToArray(),
            DnsServers = details.DnsServers.ToArray(),
        };

    private static string DetailsMessage(WidgetNetworkConnectionDetailsState state) => state switch
    {
        WidgetNetworkConnectionDetailsState.Available => "Current Windows connection details",
        WidgetNetworkConnectionDetailsState.Offline => "This device is offline",
        WidgetNetworkConnectionDetailsState.Ambiguous =>
            "Windows reported multiple active routes; no adapter details were guessed",
        WidgetNetworkConnectionDetailsState.PrivacyDenied =>
            "Allow Connection details in Settings → Permissions",
        _ => "Connection details are unavailable",
    };

    private void ReconcileBluetoothSelectionLocked()
    {
        var selection = NetworkControlsProviderPolicy.ReconcileBluetoothSelection(
            _bluetooth, _selectedBluetoothDeviceId, _selectedBluetoothIndex);
        _selectedBluetoothDeviceId = selection.Id;
        _selectedBluetoothIndex = selection.Index;
    }

}
