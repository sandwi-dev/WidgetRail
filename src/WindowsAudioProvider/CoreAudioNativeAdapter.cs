using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace GameBarAlternative.WindowsAudioProvider;

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
    private readonly Guid _eventContext = Guid.NewGuid();
    private readonly IMMDeviceEnumerator _deviceEnumerator;
    private readonly EndpointNotificationClient _endpointNotifications;
    private readonly Dictionary<string, SessionRegistration> _sessions = new(StringComparer.Ordinal);
    private readonly EndpointGenerationQueue<IntPtr> _createdSessions = new();
    private readonly ConcurrentQueue<string> _disconnectedSessions = new();
    private readonly object _callbackGate = new();
    private IMMDevice? _device;
    private IAudioSessionManager2? _manager;
    private SessionNotificationClient? _sessionNotifications;
    private long _endpointGeneration;
    private int _endpointDirty = 1;
    private int _degraded = 1;
    private int _disposed;

    public CoreAudioNativeAdapter()
    {
        _deviceEnumerator = (IMMDeviceEnumerator)(object)new MMDeviceEnumeratorComObject();
        _endpointNotifications = new EndpointNotificationClient(InvalidateEndpoint);
        CoreAudioInterop.ThrowIfFailed(
            _deviceEnumerator.RegisterEndpointNotificationCallback(_endpointNotifications));
    }

    public event EventHandler? StateChanged;
    public bool IsDegraded => Volatile.Read(ref _degraded) != 0;

    public IReadOnlyList<NativeAudioSessionSnapshot> EnumerateSessions()
    {
        ThrowIfDisposed();
        if (Volatile.Read(ref _endpointDirty) != 0) RebindDefaultEndpoint();
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

    private void RebindDefaultEndpoint()
    {
        var generation = Interlocked.Increment(ref _endpointGeneration);
        ReleaseEndpoint();
        Volatile.Write(ref _degraded, 1);
        IMMDevice? device = null;
        object? activated = null;
        var bound = false;
        try
        {
            var hr = _deviceEnumerator.GetDefaultAudioEndpoint(
                EDataFlow.Render, ERole.Multimedia, out device);
            if (hr < 0 || device is null) return;
            var managerId = typeof(IAudioSessionManager2).GUID;
            hr = device.Activate(ref managerId, ClsctxInprocServer, IntPtr.Zero, out activated);
            if (hr < 0 || activated is not IAudioSessionManager2 manager) return;
            _device = device;
            device = null;
            _manager = manager;
            activated = null;
            _sessionNotifications = new SessionNotificationClient(
                pointer => QueueCreatedSession(generation, pointer));
            hr = manager.RegisterSessionNotification(_sessionNotifications);
            if (hr < 0)
            {
                ReleaseEndpoint();
                return;
            }
            bound = true;
            Volatile.Write(ref _degraded, 0);
        }
        catch (COMException)
        {
            ReleaseEndpoint();
        }
        finally
        {
            Volatile.Write(ref _endpointDirty, bound ? 0 : 1);
            CoreAudioInterop.ReleaseFinal(activated);
            CoreAudioInterop.Release(device);
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

    private void InvalidateEndpoint()
    {
        if (Volatile.Read(ref _disposed) != 0) return;
        Volatile.Write(ref _endpointDirty, 1);
        Volatile.Write(ref _degraded, 1);
        SignalChanged();
    }

    private void MarkEndpointDirty()
    {
        Volatile.Write(ref _endpointDirty, 1);
        Volatile.Write(ref _degraded, 1);
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
internal sealed class EndpointNotificationClient(Action changed) : IMMNotificationClient
{
    public int OnDeviceStateChanged(string deviceId, uint newState) { changed(); return 0; }
    public int OnDeviceAdded(string deviceId) { changed(); return 0; }
    public int OnDeviceRemoved(string deviceId) { changed(); return 0; }
    public int OnDefaultDeviceChanged(EDataFlow flow, ERole role, string? defaultDeviceId)
    {
        if (flow is EDataFlow.Render or EDataFlow.All && role == ERole.Multimedia) changed();
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
