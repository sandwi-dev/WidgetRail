using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace WidgetRail.WindowsAudioProvider;

public sealed class CoreAudioNativeAdapterFactory : IWindowsAudioNativeAdapterFactory
{
    public IWindowsAudioNativeAdapter Create()
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Windows Core Audio is only available on Windows.");
        return new CoreAudioNativeAdapter();
    }
}

internal sealed class CoreAudioNativeAdapter : IWindowsAudioNativeAdapter
{
    private const uint ClsctxInprocServer = 0x1;
    private const uint DeviceStateActive = 0x1;
    private const uint PropertyStoreRead = 0;
    private static readonly PropertyKey DeviceFriendlyName =
        new(new Guid("A45C254E-DF1C-4EFD-8020-67D146A850E0"), 14);
    private readonly Guid _eventContext = Guid.NewGuid();
    private readonly IMMDeviceEnumerator _deviceEnumerator;
    private readonly EndpointNotificationClient _endpointNotifications;
    private readonly Dictionary<string, SessionRegistration> _sessions = new(StringComparer.Ordinal);
    private readonly EndpointGenerationQueue<IntPtr> _createdSessions = new();
    private readonly ConcurrentQueue<string> _disconnectedSessions = new();
    private readonly object _callbackGate = new();
    private IMMDevice? _device;
    private IAudioSessionManager2? _manager;
    private IAudioEndpointVolume? _endpointVolume;
    private SessionNotificationClient? _sessionNotifications;
    private EndpointVolumeEventsClient? _endpointVolumeNotifications;
    private IMMDevice? _inputDevice;
    private IAudioEndpointVolume? _inputVolume;
    private EndpointVolumeEventsClient? _inputVolumeNotifications;
    private long _endpointGeneration;
    private int _endpointDirty = 1;
    private int _degraded = 1;
    private int _outputDegraded = 1;
    private int _deviceListDegraded = 1;
    private int _inputDegraded = 1;
    private int _inputDirty = 1;
    private int _devicesDirty = 1;
    private IReadOnlyList<NativeAudioDeviceSnapshot> _cachedDevices = [];
    private int _disposed;
    internal string? DeviceListDiagnostic { get; private set; }

    public CoreAudioNativeAdapter()
    {
        _deviceEnumerator = (IMMDeviceEnumerator)(object)new MMDeviceEnumeratorComObject();
        _endpointNotifications = new EndpointNotificationClient(InvalidateAudioTopology);
        CoreAudioInterop.ThrowIfFailed(
            _deviceEnumerator.RegisterEndpointNotificationCallback(_endpointNotifications));
    }

    public event EventHandler? StateChanged;
    public bool IsDegraded => Volatile.Read(ref _degraded) != 0;
    public bool IsOutputDegraded => Volatile.Read(ref _outputDegraded) != 0;
    public bool IsDeviceListDegraded => Volatile.Read(ref _deviceListDegraded) != 0;
    public bool IsInputDegraded => Volatile.Read(ref _inputDegraded) != 0;

    public IReadOnlyList<NativeAudioSessionSnapshot> EnumerateSessions()
    {
        ThrowIfDisposed();
        if (Volatile.Read(ref _endpointDirty) != 0 || _manager is null) RebindDefaultEndpoint();
        DrainCreatedSessions();
        DrainDisconnectedSessions();
        if (_manager is null) return [];

        IAudioSessionEnumerator? enumerator = null;
        try
        {
            var hr = _manager.GetSessionEnumerator(out enumerator);
            if (hr < 0 || enumerator is null)
            {
                MarkEndpointDirty();
                return [];
            }
            CoreAudioInterop.ThrowIfFailed(enumerator.GetCount(out var count));
            count = Math.Clamp(count, 0, 512);
            for (var index = 0; index < count; index++)
            {
                IAudioSessionControl? rawControl = null;
                try
                {
                    if (enumerator.GetSession(index, out rawControl) < 0 || rawControl is not IAudioSessionControl2 control)
                        continue;
                    if (TryRegisterSession(control))
                        rawControl = null; // Registration owns this RCW/reference.
                }
                catch (COMException)
                {
                    // A session can expire between enumeration and querying; omit it.
                }
                finally
                {
                    CoreAudioInterop.Release(rawControl);
                }
            }
        }
        catch (COMException)
        {
            MarkEndpointDirty();
            return [];
        }
        finally
        {
            CoreAudioInterop.Release(enumerator);
        }

        var result = new List<NativeAudioSessionSnapshot>(_sessions.Count);
        foreach (var (key, registration) in _sessions.ToArray())
        {
            var status = TrySnapshot(key, registration, out var snapshot);
            if (status == SnapshotStatus.Available) result.Add(snapshot);
            else if (status == SnapshotStatus.Expired) RemoveSession(key);
        }
        return result;
    }

    public NativeAudioOutputSnapshot? GetDefaultOutput()
    {
        ThrowIfDisposed();
        if (Volatile.Read(ref _endpointDirty) != 0 || _endpointVolume is null) RebindDefaultEndpoint();
        if (_endpointVolume is null) return null;
        if (_endpointVolume.GetMasterVolumeLevelScalar(out var volume) < 0 ||
            _endpointVolume.GetMute(out var muted) < 0)
        {
            MarkEndpointDirty();
            return null;
        }
        return new NativeAudioOutputSnapshot(Math.Clamp(volume, 0, 1), muted);
    }

    public IReadOnlyList<NativeAudioDeviceSnapshot> EnumerateDevices()
    {
        ThrowIfDisposed();
        if (Volatile.Read(ref _devicesDirty) == 0) return _cachedDevices.ToArray();
        var devices = new List<NativeAudioDeviceSnapshot>();
        var stage = "default-output";
        try
        {
            var defaultOutput = GetDefaultDeviceId(EDataFlow.Render);
            stage = "default-input";
            var defaultInput = GetDefaultDeviceId(EDataFlow.Capture);
            stage = "enumerate-output";
            EnumerateDevices(EDataFlow.Render, NativeAudioDeviceDirection.Output, defaultOutput, devices);
            stage = "enumerate-input";
            EnumerateDevices(EDataFlow.Capture, NativeAudioDeviceDirection.Input, defaultInput, devices);
            Volatile.Write(ref _deviceListDegraded, 0);
            Volatile.Write(ref _devicesDirty, 0);
            _cachedDevices = devices.ToArray();
            DeviceListDiagnostic = null;
            return _cachedDevices.ToArray();
        }
        catch (Exception exception)
        {
            Volatile.Write(ref _deviceListDegraded, 1);
            Volatile.Write(ref _devicesDirty, 1);
            DeviceListDiagnostic = $"{stage}:{exception.GetType().Name}:0x{exception.HResult:X8}:{exception.Message}";
            return [];
        }
    }

    public NativeAudioInputSnapshot? GetDefaultInput()
    {
        ThrowIfDisposed();
        if (Volatile.Read(ref _inputDirty) != 0 || _inputVolume is null) RebindDefaultInput();
        if (_inputVolume is null) return null;
        if (_inputVolume.GetMasterVolumeLevelScalar(out var volume) < 0 ||
            _inputVolume.GetMute(out var muted) < 0)
        {
            MarkInputDirty();
            return null;
        }
        return new NativeAudioInputSnapshot(Math.Clamp(volume, 0, 1), muted);
    }

    public bool TrySetSessionVolume(string nativeSessionKey, double volume)
    {
        ThrowIfDisposed();
        if (!_sessions.TryGetValue(nativeSessionKey, out var session)) return false;
        var context = _eventContext;
        var hr = session.Volume.SetMasterVolume((float)Math.Clamp(volume, 0, 1), ref context);
        if (hr < 0)
        {
            MarkEndpointDirty();
            return false;
        }
        return true;
    }

    public bool TrySetSessionMuted(string nativeSessionKey, bool isMuted)
    {
        ThrowIfDisposed();
        if (!_sessions.TryGetValue(nativeSessionKey, out var session)) return false;
        var context = _eventContext;
        var hr = session.Volume.SetMute(isMuted, ref context);
        if (hr < 0)
        {
            MarkEndpointDirty();
            return false;
        }
        return true;
    }

    public bool TrySetDefaultOutputVolume(double volume)
    {
        ThrowIfDisposed();
        if (_endpointVolume is null) return false;
        var context = _eventContext;
        var hr = _endpointVolume.SetMasterVolumeLevelScalar(
            (float)Math.Clamp(volume, 0, 1), ref context);
        if (hr < 0)
        {
            MarkEndpointDirty();
            return false;
        }
        return true;
    }

    public bool TrySetDefaultOutputMuted(bool isMuted)
    {
        ThrowIfDisposed();
        if (_endpointVolume is null) return false;
        var context = _eventContext;
        var hr = _endpointVolume.SetMute(isMuted, ref context);
        if (hr < 0)
        {
            MarkEndpointDirty();
            return false;
        }
        return true;
    }

    public bool TrySetDefaultInputVolume(double volume)
    {
        ThrowIfDisposed();
        if (_inputVolume is null) return false;
        var context = _eventContext;
        var hr = _inputVolume.SetMasterVolumeLevelScalar(
            (float)Math.Clamp(volume, 0, 1), ref context);
        if (hr < 0)
        {
            MarkInputDirty();
            return false;
        }
        return true;
    }

    public bool TrySetDefaultInputMuted(bool isMuted)
    {
        ThrowIfDisposed();
        if (_inputVolume is null) return false;
        var context = _eventContext;
        var hr = _inputVolume.SetMute(isMuted, ref context);
        if (hr < 0)
        {
            MarkInputDirty();
            return false;
        }
        return true;
    }

    public bool TrySetDefaultDevice(string nativeDeviceKey, NativeAudioDeviceDirection direction)
    {
        if (!OperatingSystem.IsWindows()) return false;
        if (!EnumerateDevices().Any(device => device.NativeDeviceKey == nativeDeviceKey && device.Direction == direction))
            return false;
        var flow = direction == NativeAudioDeviceDirection.Output ? EDataFlow.Render : EDataFlow.Capture;
        var changed = WindowsDefaultAudioDevicePolicy.TrySwitch(nativeDeviceKey, role => GetDefaultDeviceId(flow, role));
        // Rebind endpoint/session subscriptions on the owner thread even when
        // only part of a failed policy operation changed Windows' state.
        Interlocked.Exchange(ref _endpointDirty, 1);
        Interlocked.Exchange(ref _inputDirty, 1);
        return changed;
    }

    private string? GetDefaultDeviceId(EDataFlow flow, ERole role = ERole.Multimedia)
    {
        IMMDevice? device = null;
        try
        {
            if (_deviceEnumerator.GetDefaultAudioEndpoint(flow, role, out device) < 0 ||
                device is null || device.GetId(out var id) < 0)
                return null;
            return id;
        }
        finally
        {
            CoreAudioInterop.Release(device);
        }
    }

    private void EnumerateDevices(
        EDataFlow flow,
        NativeAudioDeviceDirection direction,
        string? defaultId,
        List<NativeAudioDeviceSnapshot> result)
    {
        IMMDeviceCollection? collection = null;
        try
        {
            CoreAudioInterop.ThrowIfFailed(
                _deviceEnumerator.EnumAudioEndpoints(flow, DeviceStateActive, out collection));
            if (collection is null || collection.GetCount(out var count) < 0) return;
            count = Math.Min(count, 128);
            for (uint index = 0; index < count; index++)
            {
                IMMDevice? device = null;
                try
                {
                    if (collection.Item(index, out device) < 0 || device is null ||
                        device.GetId(out var id) < 0 || string.IsNullOrEmpty(id))
                        continue;
                    result.Add(new NativeAudioDeviceSnapshot(
                        id, GetDeviceFriendlyName(device), direction,
                        string.Equals(id, defaultId, StringComparison.Ordinal)));
                }
                finally
                {
                    CoreAudioInterop.Release(device);
                }
            }
        }
        finally
        {
            CoreAudioInterop.Release(collection);
        }
    }

    private static string? GetDeviceFriendlyName(IMMDevice device)
    {
        IPropertyStore? properties = null;
        var value = default(PropVariant);
        try
        {
            if (device.OpenPropertyStore(PropertyStoreRead, out properties) < 0 || properties is null)
                return null;
            var key = DeviceFriendlyName;
            if (properties.GetValue(ref key, out value) < 0) return null;
            return value.GetString();
        }
        finally
        {
            if (value.VariantType != 0) CoreAudioInterop.PropVariantClear(ref value);
            CoreAudioInterop.Release(properties);
        }
    }

    private void RebindDefaultEndpoint()
    {
        var generation = Interlocked.Increment(ref _endpointGeneration);
        ReleaseEndpoint();
        Volatile.Write(ref _degraded, 1);
        Volatile.Write(ref _outputDegraded, 1);
        IMMDevice? device = null;
        try
        {
            var hr = _deviceEnumerator.GetDefaultAudioEndpoint(
                EDataFlow.Render, ERole.Multimedia, out device);
            if (hr < 0 || device is null) return;
            _device = device;
            device = null;
            TryBindSessions(_device, generation);
            TryBindOutput(_device);
        }
        catch (COMException)
        {
            ReleaseEndpoint();
        }
        finally
        {
            Volatile.Write(ref _endpointDirty, _device is null ? 1 : 0);
            CoreAudioInterop.Release(device);
        }
    }

    private void TryBindSessions(IMMDevice device, long generation)
    {
        object? activation = null;
        IAudioSessionManager2? manager = null;
        SessionNotificationClient? notifications = null;
        try
        {
            var interfaceId = typeof(IAudioSessionManager2).GUID;
            if (device.Activate(ref interfaceId, ClsctxInprocServer, IntPtr.Zero, out activation) < 0 ||
                activation is not IAudioSessionManager2 activatedManager) return;
            manager = activatedManager;
            notifications = new SessionNotificationClient(
                pointer => QueueCreatedSession(generation, pointer));
            if (manager.RegisterSessionNotification(notifications) < 0) return;
            _manager = manager;
            activation = null;
            _sessionNotifications = notifications;
            manager = null;
            notifications = null;
            Volatile.Write(ref _degraded, 0);
        }
        catch (COMException) { }
        finally
        {
            if (manager is not null && notifications is not null)
            {
                try { manager.UnregisterSessionNotification(notifications); }
                catch (COMException) { }
            }
            CoreAudioInterop.ReleaseFinal(activation);
        }
    }

    private void TryBindOutput(IMMDevice device)
    {
        object? activation = null;
        IAudioEndpointVolume? volume = null;
        EndpointVolumeEventsClient? notifications = null;
        try
        {
            var interfaceId = typeof(IAudioEndpointVolume).GUID;
            if (device.Activate(ref interfaceId, ClsctxInprocServer, IntPtr.Zero, out activation) < 0 ||
                activation is not IAudioEndpointVolume activatedVolume) return;
            volume = activatedVolume;
            notifications = new EndpointVolumeEventsClient(_eventContext, SignalChanged);
            if (volume.RegisterControlChangeNotify(notifications) < 0) return;
            _endpointVolume = volume;
            activation = null;
            _endpointVolumeNotifications = notifications;
            volume = null;
            notifications = null;
            Volatile.Write(ref _outputDegraded, 0);
        }
        catch (COMException) { }
        finally
        {
            if (volume is not null && notifications is not null)
            {
                try { volume.UnregisterControlChangeNotify(notifications); }
                catch (COMException) { }
            }
            CoreAudioInterop.ReleaseFinal(activation);
        }
    }

    private void RebindDefaultInput()
    {
        ReleaseInputEndpoint();
        Volatile.Write(ref _inputDegraded, 1);
        IMMDevice? device = null;
        try
        {
            var hr = _deviceEnumerator.GetDefaultAudioEndpoint(
                EDataFlow.Capture, ERole.Multimedia, out device);
            if (hr < 0 || device is null) return;
            _inputDevice = device;
            device = null;
            TryBindInput(_inputDevice);
        }
        catch (COMException)
        {
            ReleaseInputEndpoint();
        }
        finally
        {
            Volatile.Write(ref _inputDirty, _inputDevice is null ? 1 : 0);
            CoreAudioInterop.Release(device);
        }
    }

    private void TryBindInput(IMMDevice device)
    {
        object? activation = null;
        IAudioEndpointVolume? volume = null;
        EndpointVolumeEventsClient? notifications = null;
        try
        {
            var interfaceId = typeof(IAudioEndpointVolume).GUID;
            if (device.Activate(ref interfaceId, ClsctxInprocServer, IntPtr.Zero, out activation) < 0 ||
                activation is not IAudioEndpointVolume activatedVolume) return;
            volume = activatedVolume;
            notifications = new EndpointVolumeEventsClient(_eventContext, SignalChanged);
            if (volume.RegisterControlChangeNotify(notifications) < 0) return;
            _inputVolume = volume;
            activation = null;
            _inputVolumeNotifications = notifications;
            volume = null;
            notifications = null;
            Volatile.Write(ref _inputDegraded, 0);
        }
        catch (COMException) { }
        finally
        {
            if (volume is not null && notifications is not null)
            {
                try { volume.UnregisterControlChangeNotify(notifications); }
                catch (COMException) { }
            }
            CoreAudioInterop.ReleaseFinal(activation);
        }
    }

    private bool TryRegisterSession(IAudioSessionControl2 control)
    {
        var key = GetSessionKey(control);
        if (string.IsNullOrEmpty(key) || _sessions.ContainsKey(key)) return false;
        var volume = (ISimpleAudioVolume)control;
        var events = new AudioSessionEventsClient(
            _eventContext,
            SignalChanged,
            () => QueueDisconnectedSession(key));
        CoreAudioInterop.ThrowIfFailed(control.RegisterAudioSessionNotification(events));
        _sessions.Add(key, new SessionRegistration(control, volume, events));
        return true;
    }

    private void DrainCreatedSessions()
    {
        foreach (var pointer in _createdSessions.DrainCurrent(
                     Volatile.Read(ref _endpointGeneration),
                     CoreAudioInterop.ReleasePointer))
        {
            object? raw = null;
            try
            {
                raw = CoreAudioInterop.GetObject(pointer);
                if (raw is IAudioSessionControl2 control && TryRegisterSession(control)) raw = null;
            }
            catch (COMException) { }
            finally
            {
                CoreAudioInterop.ReleasePointer(pointer);
                CoreAudioInterop.Release(raw);
            }
        }
    }

    private void DrainDisconnectedSessions()
    {
        while (_disconnectedSessions.TryDequeue(out var key)) RemoveSession(key);
    }

    private static string? GetSessionKey(IAudioSessionControl2 control)
    {
        if (control.GetSessionInstanceIdentifier(out var instanceId) >= 0 &&
            !string.IsNullOrWhiteSpace(instanceId)) return instanceId;
        if (control.GetSessionIdentifier(out var sessionId) >= 0 &&
            !string.IsNullOrWhiteSpace(sessionId)) return sessionId;
        return null;
    }

    private static SnapshotStatus TrySnapshot(
        string key,
        SessionRegistration session,
        out NativeAudioSessionSnapshot snapshot)
    {
        snapshot = null!;
        if (session.Control.GetState(out var state) < 0) return SnapshotStatus.Unavailable;
        if (state == AudioSessionState.Expired) return SnapshotStatus.Expired;
        if (session.Volume.GetMasterVolume(out var volume) < 0 ||
            session.Volume.GetMute(out var muted) < 0) return SnapshotStatus.Unavailable;
        string? displayName = null;
        if (session.Control.IsSystemSoundsSession() == 0)
            displayName = "System sounds";
        else if (session.Control.GetDisplayName(out var nativeName) >= 0)
            displayName = nativeName;
        if (string.IsNullOrWhiteSpace(displayName) || LooksLikePath(displayName))
            displayName = GetSafeProcessName(session.Control);
        snapshot = new NativeAudioSessionSnapshot(
            key,
            displayName,
            volume,
            muted,
            state == AudioSessionState.Active);
        return SnapshotStatus.Available;
    }

    private static string? GetSafeProcessName(IAudioSessionControl2 control)
    {
        if (control.GetProcessId(out var processId) < 0 || processId == 0 || processId > int.MaxValue)
            return null;
        try
        {
            using var process = Process.GetProcessById((int)processId);
            return process.ProcessName;
        }
        catch (ArgumentException) { return null; }
        catch (InvalidOperationException) { return null; }
        catch (System.ComponentModel.Win32Exception) { return null; }
        catch (NotSupportedException) { return null; }
    }

    private static bool LooksLikePath(string value) =>
        value.StartsWith("\\\\", StringComparison.Ordinal) ||
        value[0] == '/' ||
        value.Contains(":\\", StringComparison.Ordinal) ||
        value.Contains(":/", StringComparison.Ordinal);

    private void QueueCreatedSession(long generation, IntPtr sessionPointer)
    {
        if (sessionPointer == IntPtr.Zero) return;
        lock (_callbackGate)
        {
            if (Volatile.Read(ref _disposed) != 0) return;
            CoreAudioInterop.AddRef(sessionPointer);
            _createdSessions.Enqueue(generation, sessionPointer);
        }
        SignalChanged();
    }

    private void QueueDisconnectedSession(string key)
    {
        if (Volatile.Read(ref _disposed) != 0) return;
        _disconnectedSessions.Enqueue(key);
        SignalChanged();
    }

    private void InvalidateAudioTopology()
    {
        if (Volatile.Read(ref _disposed) != 0) return;
        Volatile.Write(ref _endpointDirty, 1);
        Volatile.Write(ref _degraded, 1);
        Volatile.Write(ref _outputDegraded, 1);
        Volatile.Write(ref _inputDirty, 1);
        Volatile.Write(ref _inputDegraded, 1);
        Volatile.Write(ref _deviceListDegraded, 1);
        Volatile.Write(ref _devicesDirty, 1);
        SignalChanged();
    }

    private void MarkEndpointDirty()
    {
        Volatile.Write(ref _endpointDirty, 1);
        Volatile.Write(ref _degraded, 1);
        Volatile.Write(ref _outputDegraded, 1);
    }

    private void MarkInputDirty()
    {
        Volatile.Write(ref _inputDirty, 1);
        Volatile.Write(ref _inputDegraded, 1);
    }

    private void SignalChanged()
    {
        if (Volatile.Read(ref _disposed) == 0) StateChanged?.Invoke(this, EventArgs.Empty);
    }

    private void RemoveSession(string key)
    {
        if (!_sessions.Remove(key, out var registration)) return;
        try { registration.Control.UnregisterAudioSessionNotification(registration.Events); }
        catch (COMException) { }
        CoreAudioInterop.ReleaseFinal(registration.Control);
    }

    private void ReleaseEndpoint()
    {
        foreach (var key in _sessions.Keys.ToArray()) RemoveSession(key);
        if (_endpointVolume is not null)
        {
            try
            {
                if (_endpointVolumeNotifications is not null)
                    _endpointVolume.UnregisterControlChangeNotify(_endpointVolumeNotifications);
            }
            catch (COMException) { }
            CoreAudioInterop.ReleaseFinal(_endpointVolume);
            _endpointVolume = null;
        }
        _endpointVolumeNotifications = null;
        if (_manager is not null)
        {
            try
            {
                if (_sessionNotifications is not null)
                    _manager.UnregisterSessionNotification(_sessionNotifications);
            }
            catch (COMException) { }
            CoreAudioInterop.ReleaseFinal(_manager);
            _manager = null;
        }
        _sessionNotifications = null;
        CoreAudioInterop.ReleaseFinal(_device);
        _device = null;
    }

    private void ReleaseInputEndpoint()
    {
        if (_inputVolume is not null)
        {
            try
            {
                if (_inputVolumeNotifications is not null)
                    _inputVolume.UnregisterControlChangeNotify(_inputVolumeNotifications);
            }
            catch (COMException) { }
            CoreAudioInterop.ReleaseFinal(_inputVolume);
            _inputVolume = null;
        }
        _inputVolumeNotifications = null;
        CoreAudioInterop.ReleaseFinal(_inputDevice);
        _inputDevice = null;
    }

    private void ThrowIfDisposed()
    {
        if (Volatile.Read(ref _disposed) != 0)
            throw new ObjectDisposedException(nameof(CoreAudioNativeAdapter));
    }

    public void Dispose()
    {
        lock (_callbackGate)
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        }
        ReleaseEndpoint();
        ReleaseInputEndpoint();
        try { _deviceEnumerator.UnregisterEndpointNotificationCallback(_endpointNotifications); }
        catch (COMException) { }
        _createdSessions.DrainAll(CoreAudioInterop.ReleasePointer);
        while (_disconnectedSessions.TryDequeue(out _)) { }
        CoreAudioInterop.ReleaseFinal(_deviceEnumerator);
    }

    private sealed record SessionRegistration(
        IAudioSessionControl2 Control,
        ISimpleAudioVolume Volume,
        AudioSessionEventsClient Events);

    private enum SnapshotStatus
    {
        Available,
        Expired,
        Unavailable,
    }
}

[ComVisible(true)]
[ClassInterface(ClassInterfaceType.None)]
internal sealed class EndpointVolumeEventsClient(
    Guid ownContext,
    Action changed) : IAudioEndpointVolumeCallback
{
    public int OnNotify(IntPtr notificationData)
    {
        if (notificationData == IntPtr.Zero) return 0;
        try
        {
            var notification = Marshal.PtrToStructure<AudioVolumeNotificationData>(notificationData);
            if (notification.EventContext != ownContext) changed();
        }
        catch
        {
            // Native callbacks never allow managed parsing failures to cross COM.
        }
        return 0;
    }
}

[ComVisible(true)]
[ClassInterface(ClassInterfaceType.None)]
internal sealed class EndpointNotificationClient(Action changed) : IMMNotificationClient
{
    public int OnDeviceStateChanged(string deviceId, uint newState) { changed(); return 0; }
    public int OnDeviceAdded(string deviceId) { changed(); return 0; }
    public int OnDeviceRemoved(string deviceId) { changed(); return 0; }
    public int OnDefaultDeviceChanged(EDataFlow flow, ERole role, string? defaultDeviceId)
    {
        if (flow is EDataFlow.Render or EDataFlow.Capture or EDataFlow.All && role == ERole.Multimedia)
            changed();
        return 0;
    }
    public int OnPropertyValueChanged(string deviceId, PropertyKey key) { changed(); return 0; }
}

[ComVisible(true)]
[ClassInterface(ClassInterfaceType.None)]
internal sealed class SessionNotificationClient(Action<IntPtr> created) : IAudioSessionNotification
{
    public int OnSessionCreated(IntPtr newSession) { created(newSession); return 0; }
}

[ComVisible(true)]
[ClassInterface(ClassInterfaceType.None)]
internal sealed class AudioSessionEventsClient(
    Guid ownContext,
    Action changed,
    Action disconnected) : IAudioSessionEvents
{
    public int OnDisplayNameChanged(string? newDisplayName, ref Guid eventContext) { changed(); return 0; }
    public int OnIconPathChanged(string? newIconPath, ref Guid eventContext) => 0;
    public int OnSimpleVolumeChanged(float newVolume, bool newMute, ref Guid eventContext)
    {
        if (eventContext != ownContext) changed();
        return 0;
    }
    public int OnChannelVolumeChanged(uint channelCount, IntPtr newChannelVolumeArray,
        uint changedChannel, ref Guid eventContext) => 0;
    public int OnGroupingParamChanged(ref Guid newGroupingParam, ref Guid eventContext) => 0;
    public int OnStateChanged(AudioSessionState newState)
    {
        if (newState == AudioSessionState.Expired) disconnected(); else changed();
        return 0;
    }
    public int OnSessionDisconnected(AudioSessionDisconnectReason disconnectReason)
    {
        disconnected();
        return 0;
    }
}
