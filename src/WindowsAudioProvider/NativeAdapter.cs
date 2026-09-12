namespace WidgetRail.WindowsAudioProvider;

/// <summary>
/// Host-internal snapshot. NativeSessionKey is never returned across the broker boundary.
/// Implementations are called only on the provider's dedicated MTA thread.
/// </summary>
public sealed record NativeAudioSessionSnapshot(
    string NativeSessionKey,
    string? DisplayName,
    double Volume,
    bool IsMuted,
    bool IsActive);

public sealed record NativeAudioOutputSnapshot(
    double Volume,
    bool IsMuted);

public enum NativeAudioDeviceDirection
{
    Output,
    Input,
}

/// <summary>Host-internal endpoint identity. NativeDeviceKey never crosses the broker.</summary>
public sealed record NativeAudioDeviceSnapshot(
    string NativeDeviceKey,
    string? DisplayName,
    NativeAudioDeviceDirection Direction,
    bool IsDefault);

public sealed record NativeAudioInputSnapshot(
    double Volume,
    bool IsMuted);

/// <summary>
/// Test seam around Core Audio. StateChanged may be raised from an arbitrary native callback
/// thread and must contain no platform data; all other members are MTA-owner-thread only.
/// </summary>
public sealed record NativeSpatialAudioSnapshot(string NativeDeviceKey, bool IsSupported,
    string SelectedFormatId, string ActiveFormatId, IReadOnlyList<WidgetRail.PlatformBroker.AudioSpatialFormat> Formats);

public interface IWindowsAudioNativeAdapter : IDisposable
{
    event EventHandler? StateChanged;

    /// <summary>True when the default render endpoint session service is temporarily unavailable.</summary>
    bool IsDegraded { get; }

    /// <summary>True when the current default render endpoint master control is unavailable.</summary>
    bool IsOutputDegraded { get; }

    bool IsDeviceListDegraded { get; }

    bool IsInputDegraded { get; }

    IReadOnlyList<NativeAudioSessionSnapshot> EnumerateSessions();
    NativeAudioOutputSnapshot? GetDefaultOutput();
    IReadOnlyList<NativeAudioDeviceSnapshot> EnumerateDevices();
    NativeAudioInputSnapshot? GetDefaultInput();
    bool TrySetSessionVolume(string nativeSessionKey, double volume);
    bool TrySetSessionMuted(string nativeSessionKey, bool isMuted);
    bool TrySetDefaultOutputVolume(double volume);
    bool TrySetDefaultOutputMuted(bool isMuted);
    bool TrySetDefaultInputVolume(double volume);
    bool TrySetDefaultInputMuted(bool isMuted);
    NativeSpatialAudioSnapshot? GetSpatialAudio() => null;
    string SetSpatialFormat(string nativeDeviceKey, string formatId) => "spatial_unavailable";
    bool TrySetDefaultDevice(string nativeDeviceKey, NativeAudioDeviceDirection direction) => false;
}

public interface IWindowsAudioNativeAdapterFactory
{
    IWindowsAudioNativeAdapter Create();
}
