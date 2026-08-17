using WidgetRail.PlatformBroker;

namespace WidgetRail.WindowsNetworkProvider;

internal readonly record struct NetworkConnectionAttempt(
    NetworkConnectionAttemptState State,
    string? ProfileId,
    string? NativeKey,
    long Generation)
{
    public static NetworkConnectionAttempt None { get; } =
        new(NetworkConnectionAttemptState.None, null, null, 0);
}

/// <summary>
/// Owner-thread-only connection and scan transition policy. It owns transient operation
/// generations, but never commits provider snapshots or schedules work.
/// </summary>
internal sealed class WindowsNetworkOperationPolicy
{
    private NetworkConnectionAttempt _connection = NetworkConnectionAttempt.None;
    private long _nextConnectionGeneration;
    private long _nextScanGeneration;

    public NetworkConnectionAttempt Connection => _connection;

    public void EnsureConnectionCanStart()
    {
        if (_connection.State == NetworkConnectionAttemptState.Connecting)
            throw new BrokerException("provider_busy", "A network connection is already in progress.");
    }

    public NetworkConnectionAttempt BeginConnection(string profileId, string nativeKey)
    {
        EnsureConnectionCanStart();
        _connection = new(
            NetworkConnectionAttemptState.Connecting,
            profileId,
            nativeKey,
            ++_nextConnectionGeneration);
        return _connection;
    }

    public bool ApplyConnectionOutcome(NativeNetworkConnectionOutcome outcome)
    {
        if (_connection.NativeKey is null ||
            !string.Equals(outcome.NativeProfileKey, _connection.NativeKey, StringComparison.Ordinal))
            return false;

        _connection = outcome.Result == NativeNetworkConnectionResult.Failed
            ? _connection with { State = NetworkConnectionAttemptState.Failed }
            : NetworkConnectionAttempt.None with { Generation = _connection.Generation };
        return true;
    }

    public bool ApplyConnectedSnapshot(string? activeNativeKey, bool wirelessAccessRestricted)
    {
        if (wirelessAccessRestricted || _connection.NativeKey is null ||
            !string.Equals(activeNativeKey, _connection.NativeKey, StringComparison.Ordinal))
            return false;
        _connection = NetworkConnectionAttempt.None with { Generation = _connection.Generation };
        return true;
    }

    public bool ApplyConnectionTimeout(long generation)
    {
        if (_connection.State != NetworkConnectionAttemptState.Connecting ||
            _connection.Generation != generation) return false;
        _connection = _connection with { State = NetworkConnectionAttemptState.Failed };
        return true;
    }

    public long BeginScanDeadline() => ++_nextScanGeneration;

    public bool IsCurrentScanDeadline(long generation, WifiScanState currentState) =>
        generation == _nextScanGeneration && currentState == WifiScanState.Scanning;

    public void InvalidateScanDeadline() => ++_nextScanGeneration;

    public NetworkStatusSummary ApplyTo(NetworkStatusSummary status) => status with
    {
        ConnectionAttemptState = _connection.State,
        AttemptProfileId = _connection.State == NetworkConnectionAttemptState.None
            ? null
            : _connection.ProfileId,
    };

    public void Reset()
    {
        _connection = NetworkConnectionAttempt.None with
        {
            Generation = ++_nextConnectionGeneration,
        };
        InvalidateScanDeadline();
    }
}
