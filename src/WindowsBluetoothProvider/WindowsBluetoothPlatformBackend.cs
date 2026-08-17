using WidgetRail.PlatformBroker;

namespace WidgetRail.WindowsBluetoothProvider;

/// <summary>
/// Sanitizing broker backend for Bluetooth radio and association-endpoint state.
/// It is lazy, event-driven, bounded, and never exposes native identifiers.
/// </summary>
public sealed class WindowsBluetoothPlatformBackend : IBluetoothPlatformBrokerBackend, IAsyncDisposable
{
    private const int MaximumDevices = 64;
    private const int MaximumRetainedOpaqueIds = 128;
    private const int MaximumDisplayNameLength = 160;
    private static readonly IReadOnlySet<string> EmptyNativeIds =
        new HashSet<string>(StringComparer.Ordinal);
    private readonly IWindowsBluetoothNativeAdapterFactory _factory;
    private readonly IWindowsBluetoothSettingsLauncher _settingsLauncher;
    private readonly SemaphoreSlim _startGate = new(1, 1);
    private readonly object _stateGate = new();
    private readonly Dictionary<string, string> _opaqueIds = new(StringComparer.Ordinal);
    private IWindowsBluetoothNativeAdapter? _adapter;
    private BluetoothSummary _snapshot = new(
        BluetoothRadioState.Unavailable, false, BluetoothDiscoveryState.Enumerating, []);
    private long _nextOpaqueId;
    private bool _started;
    private bool _disposed;

    public WindowsBluetoothPlatformBackend(
        IWindowsBluetoothNativeAdapterFactory? factory = null,
        IWindowsBluetoothSettingsLauncher? settingsLauncher = null)
    {
        _factory = factory ?? new WindowsBluetoothNativeAdapterFactory();
        _settingsLauncher = settingsLauncher ?? new WindowsBluetoothSettingsLauncher();
    }

    public event EventHandler<BrokerPlatformEvent>? EventPublished;

    public async Task<BluetoothSummary> GetBluetoothAsync(CancellationToken cancellationToken)
    {
        await EnsureStartedAsync(cancellationToken).ConfigureAwait(false);
        lock (_stateGate)
            return _snapshot with { Devices = _snapshot.Devices.ToArray() };
    }

    public async Task SetBluetoothRadioAsync(bool enabled, CancellationToken cancellationToken)
    {
        await EnsureStartedAsync(cancellationToken).ConfigureAwait(false);
        IWindowsBluetoothNativeAdapter adapter;
        lock (_stateGate)
        {
            ThrowIfDisposed();
            adapter = _adapter ?? throw new BrokerException(
                "platform_unavailable", "Windows Bluetooth is unavailable.");
        }
        NativeBluetoothRadioSetResult result;
        try
        {
            result = await adapter.SetRadioAsync(enabled, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            // A multi-radio request can be canceled or fail after one adapter changed.
            // Always reconcile and publish the effective Windows state.
            RefreshAndPublish(adapter);
        }
        switch (result)
        {
            case NativeBluetoothRadioSetResult.Succeeded:
                return;
            case NativeBluetoothRadioSetResult.NoAdapter:
                throw new BrokerException("no_adapter", "No Bluetooth adapter is available.");
            case NativeBluetoothRadioSetResult.HardwareDisabled:
                throw new BrokerException(
                    "hardware_disabled", "Bluetooth is disabled by hardware or device policy.");
            case NativeBluetoothRadioSetResult.DeniedByUser:
                throw new BrokerException(
                    "permission_denied", "Windows denied Bluetooth radio access for this user.");
            case NativeBluetoothRadioSetResult.DeniedBySystem:
                throw new BrokerException(
                    "platform_denied", "Windows policy denied Bluetooth radio control.");
            case NativeBluetoothRadioSetResult.PartialFailure:
                throw new BrokerException(
                    "partial_failure",
                    "Bluetooth changed only partially; the current Windows state was refreshed.");
            default:
                throw new BrokerException(
                    "platform_unavailable", "Windows Bluetooth radio control is unavailable.");
        }
    }

    /// <summary>
    /// Attempts Windows Association Endpoint pairing for one currently listed
    /// opaque device. Cancellation is propagated and every completed Windows
    /// outcome is returned without claiming profile connectivity.
    /// </summary>
    public async Task<BluetoothPairingResultSummary> PairBluetoothDeviceAsync(
        string deviceId, CancellationToken cancellationToken)
    {
        await EnsureStartedAsync(cancellationToken).ConfigureAwait(false);
        IWindowsBluetoothNativeAdapter adapter;
        string nativeDeviceId;
        lock (_stateGate)
        {
            ThrowIfDisposed();
            adapter = _adapter ?? throw new BrokerException(
                "platform_unavailable", "Windows Bluetooth is unavailable.");
            nativeDeviceId = NativeIdForOpaqueLocked(deviceId);
        }

        try
        {
            var outcome = await adapter.PairAsync(nativeDeviceId, cancellationToken)
                .ConfigureAwait(false);
            return new BluetoothPairingResultSummary(
                Enum.Parse<BluetoothPairingResultStatus>(
                    outcome.ToString(), ignoreCase: false));
        }
        finally
        {
            // PairAsync can complete, fail, or be canceled after Windows has
            // changed association state. Reconcile the effective snapshot in
            // every case instead of trusting optimistic UI state.
            RefreshAndPublish(adapter);
        }
    }

    public async Task<BluetoothUnpairingResultSummary> UnpairBluetoothDeviceAsync(
        string deviceId, CancellationToken cancellationToken)
    {
        await EnsureStartedAsync(cancellationToken).ConfigureAwait(false);
        IWindowsBluetoothNativeAdapter adapter;
        string nativeDeviceId;
        lock (_stateGate)
        {
            ThrowIfDisposed();
            adapter = _adapter ?? throw new BrokerException(
                "platform_unavailable", "Windows Bluetooth is unavailable.");
            nativeDeviceId = NativeIdForOpaqueLocked(deviceId);
            var current = _snapshot.Devices.FirstOrDefault(device =>
                string.Equals(device.DeviceId, deviceId, StringComparison.Ordinal));
            if (current is not { IsPaired: true })
                throw new BrokerException(
                    "resource_not_found", "The paired Bluetooth device is no longer available.");
        }
        try
        {
            var outcome = await adapter.UnpairAsync(nativeDeviceId, cancellationToken)
                .ConfigureAwait(false);
            return new BluetoothUnpairingResultSummary(
                Enum.Parse<BluetoothUnpairingResultStatus>(outcome.ToString(), false));
        }
        finally
        {
            RefreshAndPublish(adapter);
        }
    }

    /// <summary>
    /// Opens the exact Windows Bluetooth Settings page for management that is
    /// profile-specific or unsupported by the generic pairing API. The native
    /// identifier remains private and is never embedded in the launch URI.
    /// </summary>
    public async Task OpenBluetoothDeviceSettingsAsync(
        string deviceId, CancellationToken cancellationToken)
    {
        await EnsureStartedAsync(cancellationToken).ConfigureAwait(false);
        lock (_stateGate)
        {
            ThrowIfDisposed();
            _ = NativeIdForOpaqueLocked(deviceId);
        }

        bool opened;
        try
        {
            opened = await _settingsLauncher.OpenAsync(cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception exception)
        {
            throw new BrokerException(
                "settings_unavailable",
                "Windows Bluetooth Settings could not be opened.", exception);
        }
        if (!opened)
            throw new BrokerException(
                "settings_unavailable", "Windows Bluetooth Settings could not be opened.");
    }

    private async Task EnsureStartedAsync(CancellationToken cancellationToken)
    {
        lock (_stateGate)
        {
            ThrowIfDisposed();
            if (_started) return;
        }
        await _startGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            IWindowsBluetoothNativeAdapter adapter;
            lock (_stateGate)
            {
                ThrowIfDisposed();
                if (_started) return;
                adapter = _factory.Create();
                _adapter = adapter;
                adapter.StateChanged += OnAdapterStateChanged;
            }
            try
            {
                await adapter.StartAsync(cancellationToken).ConfigureAwait(false);
                Refresh(adapter);
                lock (_stateGate)
                {
                    ThrowIfDisposed();
                    if (!ReferenceEquals(_adapter, adapter))
                        throw new OperationCanceledException("Bluetooth startup was superseded.");
                    _started = true;
                }
            }
            catch (OperationCanceledException)
            {
                await RollBackStartupAsync(adapter).ConfigureAwait(false);
                throw;
            }
            catch (Exception exception)
            {
                await RollBackStartupAsync(adapter).ConfigureAwait(false);
                throw new BrokerException(
                    "platform_unavailable", "Windows Bluetooth is unavailable.", exception);
            }
        }
        finally { _startGate.Release(); }
    }

    private async Task RollBackStartupAsync(IWindowsBluetoothNativeAdapter adapter)
    {
        lock (_stateGate)
        {
            if (ReferenceEquals(_adapter, adapter))
            {
                adapter.StateChanged -= OnAdapterStateChanged;
                _adapter = null;
                _started = false;
                _snapshot = new BluetoothSummary(
                    BluetoothRadioState.Unavailable, false,
                    BluetoothDiscoveryState.Enumerating, []);
            }
        }
        await adapter.DisposeAsync().ConfigureAwait(false);
    }

    private void OnAdapterStateChanged(object? sender, EventArgs args)
    {
        if (sender is IWindowsBluetoothNativeAdapter adapter) RefreshAndPublish(adapter);
    }

    private void RefreshAndPublish(IWindowsBluetoothNativeAdapter adapter)
    {
        BluetoothSummary snapshot;
        try { snapshot = MapSnapshot(adapter.ReadSnapshot()); }
        catch (Exception)
        {
            snapshot = new BluetoothSummary(
                BluetoothRadioState.Unavailable, false,
                BluetoothDiscoveryState.Unavailable, []);
        }
        bool changed;
        lock (_stateGate)
        {
            if (_disposed || !ReferenceEquals(adapter, _adapter)) return;
            changed = !SnapshotsEqual(_snapshot, snapshot);
            _snapshot = snapshot;
        }
        if (changed)
            EventPublished?.Invoke(this, new BrokerPlatformEvent(
                PlatformCapabilities.NetworkBluetoothReadV1,
                PlatformCapabilities.NetworkBluetoothChanged,
                new BluetoothChangedEvent(snapshot)));
    }

    private void Refresh(IWindowsBluetoothNativeAdapter adapter)
    {
        var snapshot = MapSnapshot(adapter.ReadSnapshot());
        lock (_stateGate)
        {
            if (_disposed || !ReferenceEquals(adapter, _adapter)) return;
            _snapshot = snapshot;
        }
    }

    private BluetoothSummary MapSnapshot(NativeBluetoothSnapshot native)
    {
        var state = native.RadioState switch
        {
            NativeBluetoothRadioState.On => BluetoothRadioState.On,
            NativeBluetoothRadioState.Off => BluetoothRadioState.Off,
            NativeBluetoothRadioState.HardwareDisabled => BluetoothRadioState.HardwareDisabled,
            NativeBluetoothRadioState.NoAdapter => BluetoothRadioState.NoAdapter,
            _ => BluetoothRadioState.Unavailable,
        };
        var discovery = native.DiscoveryState switch
        {
            NativeBluetoothDiscoveryState.Enumerating => BluetoothDiscoveryState.Enumerating,
            NativeBluetoothDiscoveryState.Ready => BluetoothDiscoveryState.Ready,
            _ => BluetoothDiscoveryState.Unavailable,
        };
        var devices = new List<BluetoothDeviceSummary>();
        if (discovery == BluetoothDiscoveryState.Ready)
        {
            var seenNative = new HashSet<string>(StringComparer.Ordinal);
            foreach (var device in native.Devices
                         .Where(device => device.IsPaired || device.IsPresent)
                         .OrderByDescending(device => device.IsConnected)
                         .ThenByDescending(device => device.IsPaired)
                         .ThenBy(device => device.DisplayName, StringComparer.OrdinalIgnoreCase))
            {
                if (devices.Count >= MaximumDevices || string.IsNullOrWhiteSpace(device.NativeId) ||
                    device.NativeId.Length > 2048 || !seenNative.Add(device.NativeId)) continue;
                var displayName = SanitizeDisplayName(device.DisplayName);
                if (displayName is null) continue;
                var opaqueId = OpaqueIdFor(device.NativeId);
                devices.Add(new BluetoothDeviceSummary(
                    opaqueId, displayName, device.IsPaired || device.IsConnected,
                    device.IsConnected, device.IsPresent));
            }
            PruneOpaqueIds(seenNative);
        }
        else PruneOpaqueIds(EmptyNativeIds);
        return new BluetoothSummary(state,
            native.CanControlRadio && state is BluetoothRadioState.On or BluetoothRadioState.Off,
            discovery, devices);
    }

    private string OpaqueIdFor(string nativeId)
    {
        lock (_stateGate)
        {
            if (_opaqueIds.TryGetValue(nativeId, out var opaque)) return opaque;
            opaque = $"bluetooth-{++_nextOpaqueId:x}";
            _opaqueIds.Add(nativeId, opaque);
            return opaque;
        }
    }

    private string NativeIdForOpaqueLocked(string opaqueId)
    {
        if (string.IsNullOrWhiteSpace(opaqueId) || opaqueId.Length > 64 ||
            !opaqueId.StartsWith("bluetooth-", StringComparison.Ordinal))
            throw new BrokerException("unknown_device", "The Bluetooth device is unavailable.");
        foreach (var (nativeId, retainedOpaqueId) in _opaqueIds)
            if (string.Equals(retainedOpaqueId, opaqueId, StringComparison.Ordinal))
                return nativeId;
        throw new BrokerException("unknown_device", "The Bluetooth device is unavailable.");
    }

    private void PruneOpaqueIds(IReadOnlySet<string> retainedNativeIds)
    {
        lock (_stateGate)
        {
            foreach (var nativeId in _opaqueIds.Keys
                         .Where(id => !retainedNativeIds.Contains(id)).ToArray())
                _opaqueIds.Remove(nativeId);
            if (_opaqueIds.Count <= MaximumRetainedOpaqueIds) return;
            foreach (var nativeId in _opaqueIds.Keys
                         .Skip(MaximumRetainedOpaqueIds).ToArray())
                _opaqueIds.Remove(nativeId);
        }
    }

    private static string? SanitizeDisplayName(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        var sanitized = new string(name.Trim().Where(character => !char.IsControl(character)).ToArray());
        if (sanitized.Length == 0) return null;
        return sanitized.Length <= MaximumDisplayNameLength
            ? sanitized
            : sanitized[..MaximumDisplayNameLength];
    }

    private static bool SnapshotsEqual(BluetoothSummary left, BluetoothSummary right) =>
        left.RadioState == right.RadioState && left.CanControlRadio == right.CanControlRadio &&
        left.DiscoveryState == right.DiscoveryState && left.Devices.SequenceEqual(right.Devices);

    public async ValueTask DisposeAsync()
    {
        IWindowsBluetoothNativeAdapter? adapter;
        lock (_stateGate)
        {
            if (_disposed) return;
            _disposed = true;
            adapter = _adapter;
            _adapter = null;
            if (adapter is not null) adapter.StateChanged -= OnAdapterStateChanged;
            _opaqueIds.Clear();
        }
        if (adapter is not null) await adapter.DisposeAsync().ConfigureAwait(false);
        _startGate.Dispose();
    }

    private void ThrowIfDisposed()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(WindowsBluetoothPlatformBackend));
    }
}
