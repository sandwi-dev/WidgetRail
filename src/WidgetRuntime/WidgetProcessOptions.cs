namespace GameBarAlternative.WidgetRuntime;

public sealed record WidgetProcessOptions
{
    public required string ExecutablePath { get; init; }
    public IReadOnlyList<string> Arguments { get; init; } = [];
    public required string WidgetInstanceId { get; init; }
    public TimeSpan ConnectTimeout { get; init; } = TimeSpan.FromSeconds(3);
    public TimeSpan RequestTimeout { get; init; } = TimeSpan.FromSeconds(2);
    public int MaximumMessageBytes { get; init; } = WidgetRuntimeProtocol.DefaultMaximumMessageBytes;
    public int MaximumRestartAttempts { get; init; } = 2;
    /// <summary>
    /// Trusted host policy applied to the Windows Job Object. This value is
    /// never accepted from the worker process or its protocol messages.
    /// </summary>
    public long MemoryLimitBytes { get; init; } = 64L * 1024 * 1024;

    internal void Validate()
    {
        if (string.IsNullOrWhiteSpace(ExecutablePath))
            throw new ArgumentException("Worker executable path is required.", nameof(ExecutablePath));
        if (!File.Exists(ExecutablePath))
            throw new FileNotFoundException("Worker executable was not found.", ExecutablePath);
        if (string.IsNullOrWhiteSpace(WidgetInstanceId) || WidgetInstanceId.Length > 128 ||
            !WidgetInstanceId.All(ch => char.IsAsciiLetterOrDigit(ch) || ch is '-' or '_' or '.'))
            throw new ArgumentException("Widget instance ID is invalid.", nameof(WidgetInstanceId));
        if (ConnectTimeout <= TimeSpan.Zero || ConnectTimeout > TimeSpan.FromMinutes(1))
            throw new ArgumentOutOfRangeException(nameof(ConnectTimeout));
        if (RequestTimeout <= TimeSpan.Zero || RequestTimeout > TimeSpan.FromMinutes(1))
            throw new ArgumentOutOfRangeException(nameof(RequestTimeout));
        if (MaximumMessageBytes is < 256 or > WidgetRuntimeProtocol.AbsoluteMaximumMessageBytes)
            throw new ArgumentOutOfRangeException(nameof(MaximumMessageBytes));
        if (MaximumRestartAttempts is < 0 or > 10)
            throw new ArgumentOutOfRangeException(nameof(MaximumRestartAttempts));
        if (MemoryLimitBytes is < 16L * 1024 * 1024 or > 512L * 1024 * 1024)
            throw new ArgumentOutOfRangeException(nameof(MemoryLimitBytes));
        if (Arguments.Any(argument => argument is null))
            throw new ArgumentException("Worker arguments cannot contain null entries.", nameof(Arguments));
    }
}

public enum WidgetFailureReason
{
    ProcessExited,
    ConnectionFailed,
    RequestTimedOut,
    ProtocolViolation,
    TransportFailure,
}

public sealed record WidgetFailure(
    WidgetFailureReason Reason,
    int? ExitCode,
    Exception? Exception,
    int RestartsUsed,
    bool CanRestart);

/// <summary>
/// An action was accepted from the controller queue, then failed asynchronously.
/// This does not crash or restart the widget worker.
/// </summary>
public sealed record WidgetControllerActionFailure(
    string ActionId,
    string SourceElementId,
    string Message);
