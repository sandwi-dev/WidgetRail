using Windows.Devices.Enumeration;
using Windows.Devices.Radios;

namespace WidgetRail.WindowsBluetoothProvider;

internal sealed class WindowsBluetoothNativeAdapterFactory : IWindowsBluetoothNativeAdapterFactory
{
    public IWindowsBluetoothNativeAdapter Create() => new WindowsBluetoothNativeAdapter();
}

/// <summary>
/// Owns supported Windows Radio and AssociationEndpoint APIs. Native device identifiers
/// never leave this assembly and are used only as keys for host-generated opaque tokens.
/// </summary>
internal sealed class WindowsBluetoothNativeAdapter : IWindowsBluetoothNativeAdapter
{
    private const string BluetoothProtocolQuery =
        "System.Devices.Aep.ProtocolId:=\"{e0cbf06c-cd8b-4647-bb8a-263b43f0f974}\" OR " +
        "System.Devices.Aep.ProtocolId:=\"{bb7bb05e-5972-42b5-94fc-76eaa7084d49}\"";
    private static readonly TimeSpan EnumerationDeadline = TimeSpan.FromSeconds(8);
    private static readonly string[] RequestedProperties =
    [
        "System.ItemNameDisplay",
        "System.Devices.Aep.IsPaired",
        "System.Devices.Aep.IsConnected",
        "System.Devices.Aep.IsPresent",
    ];

    private readonly object _gate = new();
    private readonly Dictionary<string, NativeBluetoothDevice> _devices =
        new(StringComparer.Ordinal);
    private readonly List<Radio> _radios = [];
    private DeviceWatcher? _watcher;
    private CancellationTokenSource? _enumerationDeadline;
    private NativeBluetoothDiscoveryState _discoveryState = NativeBluetoothDiscoveryState.Enumerating;
    private bool _started;
    private bool _disposed;

    public event EventHandler? StateChanged;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            ThrowIfDisposed();
            if (_started) return;
            _started = true;
        }

        try
        {
            var radios = await Radio.GetRadiosAsync().AsTask(cancellationToken).ConfigureAwait(false);
            lock (_gate)
            {
                ThrowIfDisposed();
                foreach (var radio in radios.Where(radio => radio.Kind == RadioKind.Bluetooth))
                {
                    radio.StateChanged += OnRadioStateChanged;
                    _radios.Add(radio);
                }

                _watcher = DeviceInformation.CreateWatcher(
                    BluetoothProtocolQuery,
                    RequestedProperties,
                    DeviceInformationKind.AssociationEndpoint);
                _watcher.Added += OnDeviceAdded;
                _watcher.Updated += OnDeviceUpdated;
                _watcher.Removed += OnDeviceRemoved;
                _watcher.EnumerationCompleted += OnEnumerationCompleted;
                _watcher.Stopped += OnWatcherStopped;
                _watcher.Start();
                _enumerationDeadline = new CancellationTokenSource();
                _ = CompleteEnumerationAtDeadlineAsync(_enumerationDeadline.Token);
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            lock (_gate)
                if (!_disposed) _discoveryState = NativeBluetoothDiscoveryState.Unavailable;
            RaiseChanged();
        }
    }

    public NativeBluetoothSnapshot ReadSnapshot()
    {
        lock (_gate)
        {
            ThrowIfDisposed();
            var state = AggregateRadioState(_radios);
            var canControl = _radios.Count != 0 &&
                state is NativeBluetoothRadioState.On or NativeBluetoothRadioState.Off;
            var devices = _discoveryState == NativeBluetoothDiscoveryState.Ready
                ? _devices.Values.Where(device => device.IsPaired || device.IsPresent).ToArray()
                : [];
            return new NativeBluetoothSnapshot(state, canControl, _discoveryState, devices);
        }
    }

    public async Task<NativeBluetoothRadioSetResult> SetRadioAsync(
        bool enabled, CancellationToken cancellationToken)
    {
        Radio[] radios;
        lock (_gate)
        {
            ThrowIfDisposed();
            radios = _radios.ToArray();
        }
        if (radios.Length == 0) return NativeBluetoothRadioSetResult.NoAdapter;
        var mutableRadios = radios.Where(radio => radio.State != RadioState.Disabled).ToArray();
        if (mutableRadios.Length == 0)
            return NativeBluetoothRadioSetResult.HardwareDisabled;

        RadioAccessStatus access;
        try
        {
            access = await Radio.RequestAccessAsync().AsTask(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception) { return NativeBluetoothRadioSetResult.Unavailable; }

        if (access != RadioAccessStatus.Allowed)
            return access == RadioAccessStatus.DeniedByUser
                ? NativeBluetoothRadioSetResult.DeniedByUser
                : NativeBluetoothRadioSetResult.DeniedBySystem;

        var target = enabled ? RadioState.On : RadioState.Off;
        var priorStates = mutableRadios.ToDictionary(radio => radio, radio => radio.State);
        var changed = new List<Radio>();
        foreach (var radio in mutableRadios)
        {
            RadioAccessStatus result;
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                result = await radio.SetStateAsync(target).AsTask(cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                await CompensateAsync(changed, priorStates).ConfigureAwait(false);
                throw;
            }
            catch (Exception)
            {
                return await CompensateAsync(changed, priorStates).ConfigureAwait(false)
                    ? NativeBluetoothRadioSetResult.Unavailable
                    : NativeBluetoothRadioSetResult.PartialFailure;
            }
            if (result != RadioAccessStatus.Allowed)
            {
                var restored = await CompensateAsync(changed, priorStates).ConfigureAwait(false);
                if (!restored) return NativeBluetoothRadioSetResult.PartialFailure;
                return result == RadioAccessStatus.DeniedByUser
                    ? NativeBluetoothRadioSetResult.DeniedByUser
                    : NativeBluetoothRadioSetResult.DeniedBySystem;
            }
            if (priorStates[radio] != target) changed.Add(radio);
        }
        RaiseChanged();
        return NativeBluetoothRadioSetResult.Succeeded;
    }

    public async Task<BluetoothPairingOutcome> PairAsync(
        string nativeDeviceId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(nativeDeviceId);
        lock (_gate)
        {
            ThrowIfDisposed();
            if (!_devices.TryGetValue(nativeDeviceId, out var known) || !known.IsPresent)
                return BluetoothPairingOutcome.DeviceUnavailable;
            if (known.IsPaired) return BluetoothPairingOutcome.AlreadyPaired;
        }

        DeviceInformation information;
        try
        {
            information = await DeviceInformation.CreateFromIdAsync(
                    nativeDeviceId, RequestedProperties, DeviceInformationKind.AssociationEndpoint)
                .AsTask(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { throw; }
        catch (UnauthorizedAccessException) { return BluetoothPairingOutcome.AccessDenied; }
        catch (Exception) { return BluetoothPairingOutcome.DeviceUnavailable; }

        // Discovery yields association endpoint IDs, not device-interface IDs.
        // A vanished endpoint can also return null without throwing.
        if (information is null) return BluetoothPairingOutcome.DeviceUnavailable;
        if (information.Pairing.IsPaired)
        {
            MarkPaired(nativeDeviceId);
            return BluetoothPairingOutcome.AlreadyPaired;
        }
        if (!information.Pairing.CanPair) return BluetoothPairingOutcome.NotReady;

        DevicePairingResult result;
        try
        {
            // Basic PairAsync delegates supported consent/authentication UI to
            // Windows. Ceremonies that require an app-owned handler are
            // reported as UserInteractionRequired; this headless provider must
            // never invent a PIN UI or silently accept a confirmation value.
            result = await information.Pairing.PairAsync()
                .AsTask(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { throw; }
        catch (UnauthorizedAccessException) { return BluetoothPairingOutcome.AccessDenied; }
        catch (Exception) { return BluetoothPairingOutcome.Failed; }

        var outcome = MapPairingStatus(result.Status);
        if (outcome is BluetoothPairingOutcome.Paired or
            BluetoothPairingOutcome.AlreadyPaired)
            MarkPaired(nativeDeviceId);
        return outcome;
    }

    public async Task<BluetoothUnpairingOutcome> UnpairAsync(
        string nativeDeviceId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(nativeDeviceId);
        lock (_gate)
        {
            ThrowIfDisposed();
            if (!_devices.TryGetValue(nativeDeviceId, out var known) || !known.IsPresent)
                return BluetoothUnpairingOutcome.DeviceUnavailable;
            if (!known.IsPaired) return BluetoothUnpairingOutcome.AlreadyUnpaired;
        }

        DeviceInformation information;
        try
        {
            information = await DeviceInformation.CreateFromIdAsync(
                    nativeDeviceId, RequestedProperties, DeviceInformationKind.AssociationEndpoint)
                .AsTask(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { throw; }
        catch (UnauthorizedAccessException) { return BluetoothUnpairingOutcome.AccessDenied; }
        catch (Exception) { return BluetoothUnpairingOutcome.DeviceUnavailable; }

        if (information is null) return BluetoothUnpairingOutcome.DeviceUnavailable;
        if (!information.Pairing.IsPaired)
            return BluetoothUnpairingOutcome.AlreadyUnpaired;
        try
        {
            var result = await information.Pairing.UnpairAsync()
                .AsTask(cancellationToken).ConfigureAwait(false);
            return MapUnpairingStatus(result.Status);
        }
        catch (OperationCanceledException) { throw; }
        catch (UnauthorizedAccessException) { return BluetoothUnpairingOutcome.AccessDenied; }
        catch (Exception) { return BluetoothUnpairingOutcome.Failed; }
    }

    internal static BluetoothPairingOutcome MapPairingStatus(
        DevicePairingResultStatus status) => status switch
    {
        DevicePairingResultStatus.Paired => BluetoothPairingOutcome.Paired,
        DevicePairingResultStatus.AlreadyPaired => BluetoothPairingOutcome.AlreadyPaired,
        DevicePairingResultStatus.NotReadyToPair or DevicePairingResultStatus.NotPaired =>
            BluetoothPairingOutcome.NotReady,
        DevicePairingResultStatus.ConnectionRejected or
            DevicePairingResultStatus.RejectedByHandler => BluetoothPairingOutcome.Rejected,
        DevicePairingResultStatus.TooManyConnections =>
            BluetoothPairingOutcome.TooManyConnections,
        DevicePairingResultStatus.HardwareFailure => BluetoothPairingOutcome.HardwareFailure,
        DevicePairingResultStatus.AuthenticationTimeout =>
            BluetoothPairingOutcome.AuthenticationTimedOut,
        DevicePairingResultStatus.AuthenticationNotAllowed =>
            BluetoothPairingOutcome.AuthenticationNotAllowed,
        DevicePairingResultStatus.AuthenticationFailure =>
            BluetoothPairingOutcome.AuthenticationFailed,
        DevicePairingResultStatus.NoSupportedProfiles =>
            BluetoothPairingOutcome.NoSupportedProfiles,
        DevicePairingResultStatus.ProtectionLevelCouldNotBeMet =>
            BluetoothPairingOutcome.ProtectionLevelNotMet,
        DevicePairingResultStatus.AccessDenied => BluetoothPairingOutcome.AccessDenied,
        DevicePairingResultStatus.InvalidCeremonyData =>
            BluetoothPairingOutcome.InvalidCeremonyData,
        DevicePairingResultStatus.PairingCanceled => BluetoothPairingOutcome.CanceledByUser,
        DevicePairingResultStatus.OperationAlreadyInProgress =>
            BluetoothPairingOutcome.OperationInProgress,
        DevicePairingResultStatus.RequiredHandlerNotRegistered =>
            BluetoothPairingOutcome.UserInteractionRequired,
        DevicePairingResultStatus.RemoteDeviceHasAssociation =>
            BluetoothPairingOutcome.RemoteAlreadyAssociated,
        _ => BluetoothPairingOutcome.Failed,
    };

    internal static BluetoothUnpairingOutcome MapUnpairingStatus(
        DeviceUnpairingResultStatus status) => status switch
    {
        DeviceUnpairingResultStatus.Unpaired => BluetoothUnpairingOutcome.Unpaired,
        DeviceUnpairingResultStatus.AlreadyUnpaired =>
            BluetoothUnpairingOutcome.AlreadyUnpaired,
        DeviceUnpairingResultStatus.OperationAlreadyInProgress =>
            BluetoothUnpairingOutcome.OperationInProgress,
        DeviceUnpairingResultStatus.AccessDenied => BluetoothUnpairingOutcome.AccessDenied,
        _ => BluetoothUnpairingOutcome.Failed,
    };

    private void MarkPaired(string nativeDeviceId)
    {
        var changed = false;
        lock (_gate)
        {
            if (_disposed || !_devices.TryGetValue(nativeDeviceId, out var prior)) return;
            if (!prior.IsPaired)
            {
                _devices[nativeDeviceId] = prior with { IsPaired = true };
                changed = true;
            }
        }
        if (changed) RaiseChanged();
    }

    private static async Task<bool> CompensateAsync(
        IReadOnlyList<Radio> changed,
        IReadOnlyDictionary<Radio, RadioState> priorStates)
    {
        var restored = true;
        for (var index = changed.Count - 1; index >= 0; index--)
        {
            var radio = changed[index];
            try
            {
                var result = await radio.SetStateAsync(priorStates[radio]).AsTask()
                    .WaitAsync(TimeSpan.FromSeconds(3)).ConfigureAwait(false);
                restored &= result == RadioAccessStatus.Allowed;
            }
            catch (Exception) { restored = false; }
        }
        return restored;
    }

    private void OnRadioStateChanged(Radio sender, object args) => RaiseChanged();

    private void OnDeviceAdded(DeviceWatcher sender, DeviceInformation information)
    {
        var device = ReadDevice(information.Id, information.Name, information.Properties,
            information.Pairing.IsPaired);
        lock (_gate)
            if (!_disposed && device is not null) _devices[information.Id] = device;
        RaiseChanged();
    }

    private void OnDeviceUpdated(DeviceWatcher sender, DeviceInformationUpdate update)
    {
        lock (_gate)
        {
            if (_disposed || !_devices.TryGetValue(update.Id, out var prior)) return;
            _devices[update.Id] = ReadDevice(update.Id, prior.DisplayName, update.Properties,
                prior.IsPaired, prior) ?? prior;
        }
        RaiseChanged();
    }

    private void OnDeviceRemoved(DeviceWatcher sender, DeviceInformationUpdate update)
    {
        lock (_gate)
            if (!_disposed) _devices.Remove(update.Id);
        RaiseChanged();
    }

    private void OnEnumerationCompleted(DeviceWatcher sender, object args)
    {
        lock (_gate)
        {
            if (_disposed) return;
            _enumerationDeadline?.Cancel();
            _discoveryState = NativeBluetoothDiscoveryState.Ready;
        }
        RaiseChanged();
    }

    private async Task CompleteEnumerationAtDeadlineAsync(CancellationToken cancellationToken)
    {
        try { await Task.Delay(EnumerationDeadline, cancellationToken).ConfigureAwait(false); }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { return; }
        lock (_gate)
        {
            if (_disposed || _discoveryState != NativeBluetoothDiscoveryState.Enumerating) return;
            // DeviceWatcher discovery can remain active while a nearby device is advertising.
            // Publish the bounded set accumulated so far, then continue consuming watcher events.
            _discoveryState = NativeBluetoothDiscoveryState.Ready;
        }
        RaiseChanged();
    }

    private void OnWatcherStopped(DeviceWatcher sender, object args)
    {
        lock (_gate)
        {
            if (_disposed) return;
            _discoveryState = NativeBluetoothDiscoveryState.Unavailable;
            _devices.Clear();
        }
        RaiseChanged();
    }

    private static NativeBluetoothDevice? ReadDevice(
        string nativeId,
        string fallbackName,
        IReadOnlyDictionary<string, object> properties,
        bool fallbackPaired,
        NativeBluetoothDevice? prior = null)
    {
        if (string.IsNullOrWhiteSpace(nativeId)) return null;
        var name = Property<string>(properties, "System.ItemNameDisplay") ?? fallbackName;
        if (string.IsNullOrWhiteSpace(name)) return null;
        var paired = Property<bool?>(properties, "System.Devices.Aep.IsPaired") ??
            prior?.IsPaired ?? fallbackPaired;
        var connected = Property<bool?>(properties, "System.Devices.Aep.IsConnected") ??
            prior?.IsConnected ?? false;
        var present = Property<bool?>(properties, "System.Devices.Aep.IsPresent") ??
            prior?.IsPresent ?? false;
        if (connected) paired = true;
        return new NativeBluetoothDevice(nativeId, name, paired, connected, present);
    }

    private static T? Property<T>(IReadOnlyDictionary<string, object> properties, string name)
    {
        if (!properties.TryGetValue(name, out var value) || value is null) return default;
        return value is T typed ? typed : default;
    }

    private static NativeBluetoothRadioState AggregateRadioState(IReadOnlyList<Radio> radios)
    {
        if (radios.Count == 0) return NativeBluetoothRadioState.NoAdapter;
        if (radios.Any(radio => radio.State == RadioState.On)) return NativeBluetoothRadioState.On;
        if (radios.Any(radio => radio.State == RadioState.Off)) return NativeBluetoothRadioState.Off;
        if (radios.All(radio => radio.State == RadioState.Disabled))
            return NativeBluetoothRadioState.HardwareDisabled;
        return NativeBluetoothRadioState.Unavailable;
    }

    private void RaiseChanged()
    {
        bool emit;
        lock (_gate) emit = !_disposed;
        if (emit) StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public ValueTask DisposeAsync()
    {
        DeviceWatcher? watcher;
        Radio[] radios;
        CancellationTokenSource? enumerationDeadline;
        lock (_gate)
        {
            if (_disposed) return ValueTask.CompletedTask;
            _disposed = true;
            watcher = _watcher;
            _watcher = null;
            enumerationDeadline = _enumerationDeadline;
            _enumerationDeadline = null;
            radios = _radios.ToArray();
            _radios.Clear();
            _devices.Clear();
        }
        foreach (var radio in radios) radio.StateChanged -= OnRadioStateChanged;
        enumerationDeadline?.Cancel();
        enumerationDeadline?.Dispose();
        if (watcher is not null)
        {
            watcher.Added -= OnDeviceAdded;
            watcher.Updated -= OnDeviceUpdated;
            watcher.Removed -= OnDeviceRemoved;
            watcher.EnumerationCompleted -= OnEnumerationCompleted;
            watcher.Stopped -= OnWatcherStopped;
            if (watcher.Status is DeviceWatcherStatus.Started or DeviceWatcherStatus.EnumerationCompleted)
                watcher.Stop();
        }
        return ValueTask.CompletedTask;
    }

    private void ThrowIfDisposed()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(WindowsBluetoothNativeAdapter));
    }
}
