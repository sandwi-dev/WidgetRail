using GameBarAlternative.WidgetSdk;

namespace GameBarAlternative.WidgetRuntime;

/// <summary>
/// Trusted host metadata for one dashboard gesture. It never crosses the
/// widget-worker protocol and can only be installed through the companion
/// bound to the worker process.
/// </summary>
public sealed record WidgetDashboardGestureAuthority(
    string CapabilityId,
    string OperationId,
    long InputSequence,
    long SnapshotSequence,
    TimeSpan ValidFor);

/// <summary>
/// A host-owned service channel that is created afresh for each worker process.
/// The worker receives only the bounded launch arguments; it cannot select the
/// companion identity or implementation.
/// </summary>
public interface IWidgetProcessCompanionSession : IAsyncDisposable
{
    IReadOnlyList<string> WorkerArguments { get; }
    /// <summary>
    /// Binds an already-created host endpoint to the exact worker PID before
    /// the companion begins accepting IPC. Implementations that expose no IPC
    /// may retain the default no-op behavior.
    /// </summary>
    void BindWorkerProcess(int processId) { }
    Task RunAsync(CancellationToken cancellationToken);
    Task SetLifecycleStateAsync(
        WidgetLifecycleState state,
        CancellationToken cancellationToken = default);
    Task GrantDashboardGestureAuthorityAsync(
        WidgetDashboardGestureAuthority authority,
        CancellationToken cancellationToken = default) =>
        Task.FromException(new NotSupportedException(
            "This companion does not provide a capability broker."));
    Task RevokeDashboardGestureAuthorityAsync(
        long inputSequence,
        CancellationToken cancellationToken = default) => Task.CompletedTask;
}

/// <summary>
/// Host-only authority for the exact content exposed to one isolated worker
/// session. Implementations retain their byte and namespace locks until the
/// runtime disposes the lease after process teardown.
/// </summary>
internal interface IWidgetProcessContentLease : IDisposable
{
    IReadOnlyList<AppContainerAuthorityExpectedTarget> Targets { get; }
}

internal static class WidgetProcessContentLimits
{
    internal const int MaximumDirectories = 1_024;
    internal const int MaximumFiles = 1_024;
    internal const int MaximumTargets = MaximumDirectories + MaximumFiles;
}

internal static class WidgetProcessContentTargets
{
    internal static IReadOnlyList<AppContainerAuthorityExpectedTarget> NormalizeAndValidate(
        IEnumerable<AppContainerAuthorityExpectedTarget>? targets)
    {
        if (targets is null)
            throw new WidgetProcessAdmissionException(
                "Worker content admission returned no object identity evidence.");
        var boundedTargets = targets
            .Take(WidgetProcessContentLimits.MaximumTargets + 1)
            .ToArray();
        if (boundedTargets.Length is < 2 or > WidgetProcessContentLimits.MaximumTargets)
            throw new WidgetProcessAdmissionException(
                "Worker content admission returned invalid authority bounds.");

        var normalized = new List<AppContainerAuthorityExpectedTarget>(boundedTargets.Length);
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var expected in boundedTargets)
        {
            if (!Enum.IsDefined(expected.Target.Kind) ||
                string.IsNullOrWhiteSpace(expected.Target.Path) ||
                !expected.ObjectIdentity.HasValidFormat())
                throw new WidgetProcessAdmissionException(
                    "Worker content admission returned invalid object identity evidence.");
            string fullPath;
            try { fullPath = Path.GetFullPath(expected.Target.Path); }
            catch (Exception exception) when (exception is ArgumentException or
                                               NotSupportedException or
                                               PathTooLongException)
            {
                throw new WidgetProcessAdmissionException(
                    "Worker content admission returned an invalid exact path.", exception);
            }
            if (!paths.Add(fullPath))
                throw new WidgetProcessAdmissionException(
                    "Worker content admission returned conflicting object identity evidence.");
            normalized.Add(new AppContainerAuthorityExpectedTarget(
                expected.Target with { Path = fullPath }, expected.ObjectIdentity));
        }

        var roots = normalized
            .Where(target => target.Target.Kind ==
                AppContainerAuthorityTargetKind.AuthorityRootDirectory)
            .Select(target => target.Target.Path)
            .ToArray();
        var directories = normalized
            .Where(target => target.Target.Kind ==
                AppContainerAuthorityTargetKind.VerifiedDirectory)
            .Select(target => target.Target.Path)
            .ToArray();
        var files = normalized
            .Where(target => target.Target.Kind == AppContainerAuthorityTargetKind.VerifiedFile)
            .Select(target => target.Target.Path)
            .ToArray();
        if (roots.Length != 1 ||
            roots.Length + directories.Length > WidgetProcessContentLimits.MaximumDirectories ||
            files.Length is < 1 or > WidgetProcessContentLimits.MaximumFiles ||
            normalized.Count != roots.Length + directories.Length + files.Length ||
            !Directory.Exists(roots[0]) || IsReparsePoint(roots[0]))
            throw new WidgetProcessAdmissionException(
                "Worker content admission returned invalid authority bounds.");

        ValidateExactPaths(directories, roots[0], expectDirectory: true);
        ValidateExactPaths(files, roots[0], expectDirectory: false);
        return normalized.AsReadOnly();
    }

    private static void ValidateExactPaths(
        IReadOnlyList<string> paths,
        string root,
        bool expectDirectory)
    {
        foreach (var fullPath in paths)
        {
            if (!IsWithinOrEqual(root, fullPath) ||
                (expectDirectory ? !Directory.Exists(fullPath) : !File.Exists(fullPath)) ||
                IsReparsePoint(fullPath))
                throw new WidgetProcessAdmissionException(
                    "Worker content admission returned an invalid exact path.");
        }
    }

    private static bool IsWithinOrEqual(string root, string path)
    {
        var normalizedRoot = Path.TrimEndingDirectorySeparator(root);
        return string.Equals(normalizedRoot, path, StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith(
                normalizedRoot + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsReparsePoint(string path) =>
        (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;
}

/// <summary>
/// Trusted launch context supplied by the runtime to a host-owned companion
/// factory. Community workers receive a per-platform AppContainer SID and the
/// exact SID needed to ACL its host-created plain named-pipe endpoint.
/// </summary>
public sealed record WidgetProcessCompanionContext(
    WidgetWorkerIsolationPolicy IsolationPolicy,
    string? IsolationKey,
    string? AppContainerSid);

public enum WidgetWorkerIsolationPolicy
{
    /// <summary>For platform-owned workers that require the desktop user's authority.</summary>
    HostTrustedJobOnly,
    /// <summary>Requires a capability-free AppContainer in addition to Job Object limits.</summary>
    RequireAppContainer,
    /// <summary>
    /// An explicitly approved installed Community executable running with the
    /// ordinary current user's authority. The host retains process lifetime
    /// ownership but applies no AppContainer or UI restriction policy.
    /// </summary>
    FullTrustCommunity,
}

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
    /// Trusted, closed mapping for a known worker executable's safe startup
    /// exit diagnostics. Packages and worker messages cannot provide it.
    /// </summary>
    public IReadOnlyDictionary<int, string> StartupExitDiagnostics { get; init; } =
        new Dictionary<int, string>();
    /// <summary>
    /// Trusted host factory invoked once for every worker start or restart.
    /// Widget packages and worker protocol messages cannot provide this value.
    /// </summary>
    public Func<WidgetProcessCompanionContext, IWidgetProcessCompanionSession>?
        CompanionSessionFactory { get; init; }
    /// <summary>
    /// Trusted host admission hook invoked immediately before a worker session
    /// allocates process resources. The returned lease is disposed only after
    /// that exact process exits or its pipe, process, and Job are detached.
    /// Widget packages and worker messages cannot provide this value.
    /// </summary>
    public Func<IDisposable>? ProcessLeaseFactory { get; init; }
    /// <summary>
    /// Trusted host factory invoked after residency admission and before any
    /// AppContainer authority or worker process is created. The lease is held
    /// for the exact process session and reacquired on every restart.
    /// </summary>
    internal Func<CancellationToken, IWidgetProcessContentLease>?
        ContentLeaseFactory { get; init; }
    internal TimeSpan ContentLeaseTimeout { get; init; } = TimeSpan.FromSeconds(5);
    internal IAppContainerAuthorityOperations? ContentAuthorityOperations { get; init; }
    internal IAppContainerAuthorityJournal? ContentAuthorityJournal { get; init; }
    /// <summary>
    /// Host-owned isolation decision. Package manifests and worker arguments
    /// never control this value.
    /// </summary>
    public WidgetWorkerIsolationPolicy IsolationPolicy { get; init; } =
        WidgetWorkerIsolationPolicy.HostTrustedJobOnly;
    /// <summary>
    /// Host-policy authority identity used to derive a distinct AppContainer
    /// profile. Never accept a raw widget-supplied key; unsigned installations
    /// must at least bind the asserted identity to the exact immutable version.
    /// </summary>
    public string? IsolationKey { get; init; }
    /// <summary>
    /// Additional package/data roots exposed read-only to an AppContainer
    /// worker. The executable directory is granted separately by the runtime.
    /// </summary>
    public IReadOnlyList<string> ReadOnlyPaths { get; init; } = [];
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
        if (StartupExitDiagnostics is null || StartupExitDiagnostics.Count > 16 ||
            StartupExitDiagnostics.Any(pair =>
                pair.Key is < 1 or > 255 ||
                string.IsNullOrWhiteSpace(pair.Value) ||
                pair.Value.Length > 64 ||
                pair.Value.Any(character =>
                    !char.IsAsciiLetterOrDigit(character) && character is not '_' and not '-')))
            throw new ArgumentException(
                "Worker startup diagnostics must be a closed bounded safe-code map.",
                nameof(StartupExitDiagnostics));
        if (ContentLeaseTimeout <= TimeSpan.Zero || ContentLeaseTimeout > TimeSpan.FromMinutes(1))
            throw new ArgumentOutOfRangeException(nameof(ContentLeaseTimeout));
        if (Arguments.Any(argument => argument is null))
            throw new ArgumentException("Worker arguments cannot contain null entries.", nameof(Arguments));
        if (!Enum.IsDefined(IsolationPolicy))
            throw new ArgumentOutOfRangeException(nameof(IsolationPolicy));
        if (IsolationPolicy == WidgetWorkerIsolationPolicy.RequireAppContainer &&
            (string.IsNullOrWhiteSpace(IsolationKey) || IsolationKey.Length > 512))
            throw new ArgumentException(
                "AppContainer workers require a bounded host-owned isolation key.", nameof(IsolationKey));
        if (IsolationPolicy != WidgetWorkerIsolationPolicy.RequireAppContainer && IsolationKey is not null)
            throw new ArgumentException(
                "Isolation keys apply only to AppContainer workers.", nameof(IsolationKey));
        if (ReadOnlyPaths is null || ReadOnlyPaths.Any(path => string.IsNullOrWhiteSpace(path)))
            throw new ArgumentException("Read-only paths cannot contain null or blank entries.", nameof(ReadOnlyPaths));
        foreach (var path in ReadOnlyPaths)
        {
            var fullPath = Path.GetFullPath(path);
            if (!File.Exists(fullPath) && !Directory.Exists(fullPath))
                throw new FileNotFoundException("A worker read-only path was not found.", fullPath);
            if ((File.GetAttributes(fullPath) & FileAttributes.ReparsePoint) != 0)
                throw new ArgumentException("Worker read-only roots cannot be reparse points.", nameof(ReadOnlyPaths));
        }
        if (IsolationPolicy != WidgetWorkerIsolationPolicy.RequireAppContainer && ReadOnlyPaths.Count != 0)
            throw new ArgumentException(
                "Read-only paths apply only to AppContainer workers.", nameof(ReadOnlyPaths));
        if (ContentLeaseFactory is not null &&
            (IsolationPolicy is not (WidgetWorkerIsolationPolicy.RequireAppContainer or
                WidgetWorkerIsolationPolicy.FullTrustCommunity) ||
             ReadOnlyPaths.Count != 0))
            throw new ArgumentException(
                "Content leases require an installed worker with no broad read-only roots.",
                nameof(ContentLeaseFactory));
        if ((ContentAuthorityOperations is not null || ContentAuthorityJournal is not null) &&
            (ContentLeaseFactory is null ||
             IsolationPolicy != WidgetWorkerIsolationPolicy.RequireAppContainer))
            throw new ArgumentException(
                "Content authority overrides require an AppContainer content lease.",
                nameof(ContentLeaseFactory));
    }
}

public sealed class WidgetProcessAdmissionException : Exception
{
    public WidgetProcessAdmissionException(string message) : base(message) { }

    internal WidgetProcessAdmissionException(string message, Exception innerException)
        : base(message, innerException) { }
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
    bool CanRestart,
    string? DiagnosticCode = null);

/// <summary>
/// An action was admitted by the runtime queue, then failed asynchronously.
/// This does not crash or restart the widget worker.
/// </summary>
public record WidgetActionFailure(
    string ActionId,
    string SourceElementId,
    string Message);

/// <summary>Compatibility payload for the former controller-only failure event.</summary>
public sealed record WidgetControllerActionFailure(
    string ActionId,
    string SourceElementId,
    string Message) : WidgetActionFailure(ActionId, SourceElementId, Message);
