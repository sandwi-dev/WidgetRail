namespace GameBarAlternative.WindowsAudioProvider;

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

/// <summary>
/// Test seam around Core Audio. StateChanged may be raised from an arbitrary native callback
/// thread and must contain no platform data; all other members are MTA-owner-thread only.
/// </summary>
public interface IWindowsAudioNativeAdapter : IDisposable
{
    event EventHandler? StateChanged;

    /// <summary>True when the default render endpoint session service is temporarily unavailable.</summary>
    bool IsDegraded { get; }

    /// <summary>True when the current default render endpoint master control is unavailable.</summary>
    bool IsOutputDegraded { get; }

    IReadOnlyList<NativeAudioSessionSnapshot> EnumerateSessions();
    NativeAudioOutputSnapshot? GetDefaultOutput();
    bool TrySetSessionVolume(string nativeSessionKey, double volume);
    bool TrySetSessionMuted(string nativeSessionKey, bool isMuted);
    bool TrySetDefaultOutputVolume(double volume);
    bool TrySetDefaultOutputMuted(bool isMuted);
}

public interface IWindowsAudioNativeAdapterFactory
{
    IWindowsAudioNativeAdapter Create();
}
