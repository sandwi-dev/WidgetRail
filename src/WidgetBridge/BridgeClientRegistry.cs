using WidgetRail.PlatformBroker;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetRuntime;
using WidgetRail.WidgetSdk;

namespace WidgetRail.WidgetBridge;

internal sealed record BridgeClientSnapshot(
    ConfiguredWidget Configured,
    ViewSnapshot Snapshot);

internal sealed record BridgeClientPresentation(
    ConfiguredWidget Configured,
    WidgetPresentationTransactionKind TransactionKind,
    long RequestBaseSequence,
    long RecoveryOriginSequence,
    ViewSnapshot Snapshot,
    PresentationUpdateBatch? Update);

internal sealed record BridgeResolvedArtwork(
    ConfiguredWidget Configured,
    string WorkerFingerprint,
    WidgetEncodedArtwork? Artwork);

internal sealed record BridgeClientWorkerStatus(
    string Id,
    string Name,
    bool IsRunning,
    int Starts,
    string? FailureCode,
    bool CanRestart);

internal sealed record BridgeClientRegistrySnapshot(
    BridgeCatalog Catalog,
    long CatalogRevision,
    IReadOnlyList<BridgeClientWorkerStatus> Workers,
    WorkerResidencyBudgetSnapshot Residency);

internal sealed record BridgeClientInvalidation(string WidgetId, long Revision);
internal sealed record BridgeClientActionFailure(
    string WidgetId,
    string RuntimeGeneration,
    WidgetActionFailure Failure);
internal sealed record BridgeClientRuntimeFailure(string WidgetId, WidgetFailure Failure);
internal sealed record BridgeWidgetRequestDiagnostic(
    string WidgetId,
    string RequestType,
    string WorkerErrorCode,
    long BridgeRequestId = 0,
    long WorkerRequestId = 0,
    string? ValidationPath = null,
    string? ValidationCode = null,
    string? ValidationField = null,
    string? ValidationState = null,
    string? ValidationIdentifier = null)
{
    private const string ValidationPrefix = "Widget protocol validation failed at ";
    private const int MaximumValidationPathLength = 256;

    internal static BridgeWidgetRequestDiagnostic From(
        BridgeWidgetRequestException exception,
        long bridgeRequestId = 0)
    {
        string? validationPath = null;
        string? validationCode = null;
        string? validationField = null;
        string? validationState = null;
        string? validationIdentifier = null;
        if (string.Equals(exception.WorkerErrorCode,
                WorkerErrorCodes.ProtocolValidationFailed, StringComparison.Ordinal))
            TryParseValidationDiagnostic(
                exception.WorkerDiagnosticMessage, out validationPath, out validationCode,
                out validationField, out validationState, out validationIdentifier);
        return new(
            exception.WidgetId,
            exception.RequestType!,
            exception.WorkerErrorCode!,
            bridgeRequestId,
            exception.WorkerRequestId ?? 0,
            validationPath,
            validationCode,
            validationField,
            validationState,
            validationIdentifier);
    }

    private static bool TryParseValidationDiagnostic(
        string? diagnostic,
        out string? path,
        out string? code,
        out string? field,
        out string? state,
        out string? identifier)
    {
        path = null;
        code = null;
        field = null;
        state = null;
        identifier = null;
        if (diagnostic is null ||
            !diagnostic.StartsWith(ValidationPrefix, StringComparison.Ordinal) ||
            !diagnostic.EndsWith(").", StringComparison.Ordinal))
            return false;

        var separator = diagnostic.LastIndexOf(" (", StringComparison.Ordinal);
        if (separator < ValidationPrefix.Length) return false;
        var candidatePath = diagnostic[ValidationPrefix.Length..separator];
        var components = diagnostic[(separator + 2)..^2].Split("; ",
            StringSplitOptions.None);
        var candidateCode = components[0];
        if (!IsSafeValidationPath(candidatePath) || !IsSafeValidationCode(candidateCode))
            return false;

        path = candidatePath;
        code = candidateCode;
        if (components.Length == 1) return true;
        if (components.Length is < 3 or > 4 ||
            !components[1].StartsWith("field=", StringComparison.Ordinal) ||
            !components[2].StartsWith("state=", StringComparison.Ordinal))
            return true;

        var candidateField = components[1]["field=".Length..];
        var candidateState = components[2]["state=".Length..];
        var candidateIdentifier = components.Length == 4 &&
            components[3].StartsWith("identifier=", StringComparison.Ordinal)
            ? components[3]["identifier=".Length..]
            : null;
        if (!IsSafeValidationField(candidateField) ||
            !IsSafeValidationState(candidateState) ||
            (candidateIdentifier is not null &&
             !ProtocolValidationIdentifierContext.IsSafeIdentifier(candidateIdentifier)) ||
            (candidateIdentifier is null && candidateState is not ("missing" or "unsafe_value")))
            return true;

        field = candidateField;
        state = candidateState;
        identifier = candidateIdentifier;
        return true;
    }

    internal static bool IsSafeValidationField(string field) => field is
        "initial_focus" or "return_focus" or "element_reference" or "action" or
        "context_action";

    internal static bool IsSafeValidationState(string state) => state is
        "missing" or "not_focusable" or "disabled" or "outside_active_scope" or
        "duplicate" or "unknown_action" or "unsafe_value";

    internal static bool IsSafeValidationCode(string code) =>
        code.Length is > 0 and <= 64 && code.All(character =>
            character >= 'a' && character <= 'z' || char.IsAsciiDigit(character) ||
            character == '_');

    internal static bool IsSafeValidationPath(string path)
    {
        if (path.Length is < 1 or > MaximumValidationPathLength || path[0] != '$')
            return false;
        for (var index = 1; index < path.Length;)
        {
            if (path[index] == '.')
            {
                index++;
                var start = index;
                while (index < path.Length &&
                       (char.IsAsciiLetterOrDigit(path[index]) || path[index] == '_'))
                    index++;
                if (index == start) return false;
                continue;
            }
            if (path[index] == '[')
            {
                index++;
                var start = index;
                while (index < path.Length && char.IsAsciiDigit(path[index])) index++;
                if (index == start || index >= path.Length || path[index] != ']') return false;
                index++;
                continue;
            }
            return false;
        }
        return true;
    }
}
internal sealed class BridgeWidgetRequestException : Exception
{
    internal BridgeWidgetRequestException(
        string widgetId,
        string failureCode,
        Exception innerException) : base(
            $"Widget '{widgetId}' runtime request failed ({failureCode}).", innerException)
    {
        WidgetId = widgetId;
        FailureCode = failureCode;
        if (innerException is WidgetProcessException processException)
        {
            RequestType = processException.RequestType;
            WorkerErrorCode = processException.WorkerErrorCode;
            WorkerDiagnosticMessage = processException.WorkerDiagnosticMessage;
            WorkerRequestId = processException.WorkerRequestId;
        }
    }

    internal string WidgetId { get; }
    internal string FailureCode { get; }
    internal string? RequestType { get; }
    internal string? WorkerErrorCode { get; }
    internal string? WorkerDiagnosticMessage { get; }
    internal long? WorkerRequestId { get; }
}
internal sealed record BridgeClientLifetimeDiagnostic(
    string WidgetId,
    long RegistryGeneration,
    WidgetResidencyMode ResidencyMode,
    WidgetProcessLifetimeDiagnostic Process);
internal sealed record BridgeClientNotificationStatus(
    int Pending,
    int DroppedFailures,
    int ActivePublications,
    bool IsRetiring);

internal sealed record BridgeClientReplacement<T>(
    BridgeClientPublication<WidgetLifecycleState> Publication,
    T Result);

internal sealed class BridgeClientPublication<TValue>(
    TValue value,
    Action release) : IDisposable
{
    private Action? _release = release;
    internal TValue Value { get; } = value;
    public void Dispose() => Interlocked.Exchange(ref _release, null)?.Invoke();

    internal BridgeClientPublication<TNext> Map<TNext>(Func<TValue, TNext> map)
    {
        ArgumentNullException.ThrowIfNull(map);
        var release = Interlocked.Exchange(ref _release, null) ??
            throw new ObjectDisposedException(nameof(BridgeClientPublication<TValue>));
        try { return new(map(Value), release); }
        catch
        {
            release();
            throw;
        }
    }
}

internal interface IBridgeWidgetClient : IAsyncDisposable
{
    event EventHandler<long>? Invalidated;
    event EventHandler<WidgetActionFailure>? ActionFailed;
    event EventHandler<WidgetFailure>? Failed;
    event EventHandler<WidgetProcessLifetimeDiagnostic>? LifetimeChanged;
    bool IsRunning { get; }
    int Starts { get; }
    Task<ViewSnapshot> GetSnapshotAsync(CancellationToken cancellationToken);
    async Task<WidgetRuntimePresentation> GetPresentationAsync(
        PresentationUpdateCapabilities capabilities,
        string presentationGeneration,
        long baseSequence,
        WidgetPresentationTransactionKind transactionKind,
        long recoveryOriginSequence,
        CancellationToken cancellationToken) =>
        new(
            transactionKind, baseSequence, recoveryOriginSequence,
            await GetSnapshotAsync(cancellationToken).ConfigureAwait(false), null);
    Task SetLifecycleStateAsync(WidgetLifecycleState state, CancellationToken cancellationToken);
    Task<bool> TryRestoreLifecycleStateAsync(
        WidgetLifecycleState state,
        int expectedStartOrdinal,
        CancellationToken cancellationToken) => Task.FromResult(false);
    Task<WidgetOperationAdmission> AdmitActionAsync(
        WidgetActionEvent action,
        CancellationToken cancellationToken);
    Task<WidgetEncodedArtwork?> ResolveArtworkAsync(
        string artworkHandle,
        CancellationToken cancellationToken) =>
        Task.FromResult<WidgetEncodedArtwork?>(null);
    Task SendEmbeddedMediaPlaybackEventAsync(
        EmbeddedMediaPlaybackEvent playbackEvent,
        CancellationToken cancellationToken);
    Task<bool> SendControllerInputAsync(
        ControllerInputEvent input,
        WidgetDashboardGestureAuthority? authority,
        CancellationToken cancellationToken);
    Task UnloadAsync(CancellationToken cancellationToken);
}

internal sealed class WidgetProcessBridgeClient(WidgetProcessClient client)
    : IBridgeWidgetClient
{
    public event EventHandler<long>? Invalidated
    {
        add => client.Invalidated += value;
        remove => client.Invalidated -= value;
    }

    public event EventHandler<WidgetActionFailure>? ActionFailed
    {
        add => client.ActionFailed += value;
        remove => client.ActionFailed -= value;
    }

    public event EventHandler<WidgetFailure>? Failed
    {
        add => client.Failed += value;
        remove => client.Failed -= value;
    }

    public event EventHandler<WidgetProcessLifetimeDiagnostic>? LifetimeChanged
    {
        add => client.LifetimeChanged += value;
        remove => client.LifetimeChanged -= value;
    }

    public bool IsRunning => client.IsRunning;
    public int Starts => client.Starts;
    public Task<ViewSnapshot> GetSnapshotAsync(CancellationToken cancellationToken) =>
        client.GetSnapshotAsync(cancellationToken);
    public Task<WidgetRuntimePresentation> GetPresentationAsync(
        PresentationUpdateCapabilities capabilities,
        string presentationGeneration,
        long baseSequence,
        WidgetPresentationTransactionKind transactionKind,
        long recoveryOriginSequence,
        CancellationToken cancellationToken) =>
        client.GetPresentationAsync(
            capabilities, presentationGeneration, baseSequence,
            transactionKind, recoveryOriginSequence, cancellationToken);
    public Task SetLifecycleStateAsync(
        WidgetLifecycleState state,
        CancellationToken cancellationToken) =>
        client.SetLifecycleStateAsync(state, cancellationToken);
    public Task<bool> TryRestoreLifecycleStateAsync(
        WidgetLifecycleState state,
        int expectedStartOrdinal,
        CancellationToken cancellationToken) =>
        client.TryRestoreLifecycleStateAsync(
            state, expectedStartOrdinal, cancellationToken);
    public Task<WidgetOperationAdmission> AdmitActionAsync(
        WidgetActionEvent action,
        CancellationToken cancellationToken) =>
        client.AdmitActionAsync(action, cancellationToken);
    public Task<WidgetEncodedArtwork?> ResolveArtworkAsync(
        string artworkHandle,
        CancellationToken cancellationToken) =>
        client.ResolveArtworkAsync(artworkHandle, cancellationToken);
    public Task SendEmbeddedMediaPlaybackEventAsync(
        EmbeddedMediaPlaybackEvent playbackEvent,
        CancellationToken cancellationToken) =>
        client.SendEmbeddedMediaPlaybackEventAsync(playbackEvent, cancellationToken);
    public Task<bool> SendControllerInputAsync(
        ControllerInputEvent input,
        WidgetDashboardGestureAuthority? authority,
        CancellationToken cancellationToken) =>
        client.SendControllerInputAsync(input, authority, cancellationToken);
    public Task UnloadAsync(CancellationToken cancellationToken) =>
        client.UnloadAsync(cancellationToken);
    public ValueTask DisposeAsync() => client.DisposeAsync();
}

internal sealed class BridgeClientRegistry : IAsyncDisposable
{
    private static readonly TimeSpan OperationDeadline = TimeSpan.FromSeconds(4);
    private static readonly TimeSpan RetireDeadline = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan RestartDeadline = TimeSpan.FromSeconds(12);
    private readonly object _gate = new();
    private readonly Dictionary<string, ClientRegistration> _clients =
        new(StringComparer.Ordinal);
    private readonly WorkerResidencyBudget _residentBudget;
    private readonly Func<ConfiguredWidget, Func<IDisposable>, IBridgeWidgetClient> _clientFactory;
    private readonly Func<BridgeClientInvalidation, CancellationToken, Task> _invalidated;
    private readonly Func<BridgeClientActionFailure, CancellationToken, Task> _actionFailed;
    private readonly Func<BridgeClientRuntimeFailure, CancellationToken, Task> _failed;
    private readonly Action<BridgeClientLifetimeDiagnostic>? _lifetimeDiagnostic;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;
    private readonly HashSet<ClientRegistration> _activeRetirements = [];
    private int _terminalFailureCount;
    private Exception? _firstTerminalFailure;
    private int _activeRestarts;
    private TaskCompletionSource? _restartsDrained;
    private BridgeCatalog _catalog;
    private long _catalogRevision;
    private long _nextRegistrationGeneration;
    private bool _disposed;
    private readonly TaskCompletionSource _terminal = new(
        TaskCreationOptions.RunContinuationsAsynchronously);

    internal BridgeClientRegistry(
        BridgeCatalog catalog,
        WorkerResidencyBudgetOptions residencyBudget,
        Func<ConfiguredWidget, Func<IDisposable>, IBridgeWidgetClient> clientFactory,
        Func<BridgeClientInvalidation, CancellationToken, Task> invalidated,
        Func<BridgeClientActionFailure, CancellationToken, Task> actionFailed,
        Func<BridgeClientRuntimeFailure, CancellationToken, Task> failed,
        Func<TimeSpan, CancellationToken, Task>? delay = null,
        Action<BridgeClientLifetimeDiagnostic>? lifetimeDiagnostic = null)
    {
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _residentBudget = new WorkerResidencyBudget(
            residencyBudget ?? throw new ArgumentNullException(nameof(residencyBudget)));
        _clientFactory = clientFactory ?? throw new ArgumentNullException(nameof(clientFactory));
        _invalidated = invalidated ?? throw new ArgumentNullException(nameof(invalidated));
        _actionFailed = actionFailed ?? throw new ArgumentNullException(nameof(actionFailed));
        _failed = failed ?? throw new ArgumentNullException(nameof(failed));
        _delay = delay ?? Task.Delay;
        _lifetimeDiagnostic = lifetimeDiagnostic;
    }

    // One admitted residency slot remains observable through terminal cleanup.
    // Reaching zero is therefore the exact worker-resource retirement boundary,
    // not merely the earlier point where a session stops accepting operations.
    internal int RunningWorkerCount => _residentBudget.Snapshot.TotalWorkers;

    internal WorkerResidencyBudgetSnapshot ResidencyBudget => _residentBudget.Snapshot;

    internal bool IsGateHeldByCurrentThread => Monitor.IsEntered(_gate);

    internal BridgeClientNotificationStatus NotificationStatus(string widgetId)
    {
        lock (_gate)
        {
            var registration = _clients.TryGetValue(widgetId, out var current)
                ? current
                : throw new BridgeProtocolException($"Unknown widget '{widgetId}'.");
            return new BridgeClientNotificationStatus(
                registration.NotificationLane.PendingCount,
                registration.NotificationLane.DroppedFailures,
                registration.ActivePublications,
                registration.IsRetiring);
        }
    }

    internal Task DrainNotificationsAsync(string widgetId)
    {
        lock (_gate)
        {
            var registration = _clients.TryGetValue(widgetId, out var current)
                ? current
                : throw new BridgeProtocolException($"Unknown widget '{widgetId}'.");
            return registration.NotificationLane.DrainAsync();
        }
    }

    internal (BridgeCatalog Catalog, long Revision) CatalogSnapshot()
    {
        lock (_gate) return (_catalog, _catalogRevision);
    }

    internal BridgeClientRegistrySnapshot DiagnosticsSnapshot()
    {
        lock (_gate)
        {
            var workers = _catalog.Widgets.Select(descriptor =>
            {
                _clients.TryGetValue(descriptor.Id, out var registration);
                var failure = registration?.LastFailure;
                return new BridgeClientWorkerStatus(
                    descriptor.Id,
                    descriptor.Name,
                    registration is { IsRetiring: false } && registration.Client.IsRunning,
                    registration?.Client.Starts ?? 0,
                    failure?.Code,
                    failure?.CanRestart ?? false);
            }).ToArray();
            return new BridgeClientRegistrySnapshot(
                _catalog, _catalogRevision, workers, _residentBudget.Snapshot);
        }
    }

    internal async Task<BridgeClientPublication<BridgeClientSnapshot>> GetSnapshotAsync(
        string widgetId,
        CancellationToken sessionCancellation,
        CancellationToken cancellationToken)
    {
        var publication = await GetPresentationAsync(
            widgetId,
            WidgetPresentationTransactionKind.OrdinaryCheckpoint,
            PresentationUpdateCapabilities.None,
            baseSequence: 0,
            recoveryOriginSequence: 0,
            sessionCancellation,
            cancellationToken).ConfigureAwait(false);
        return publication.Map(value =>
            new BridgeClientSnapshot(value.Configured, value.Snapshot));
    }

    internal async Task<BridgeClientPublication<BridgeClientPresentation>> GetPresentationAsync(
        string widgetId,
        WidgetPresentationTransactionKind transactionKind,
        PresentationUpdateCapabilities capabilities,
        long baseSequence,
        long recoveryOriginSequence,
        CancellationToken sessionCancellation,
        CancellationToken cancellationToken)
    {
        ValidatePresentationRequest(
            transactionKind, capabilities, baseSequence, recoveryOriginSequence);
        var registration = await GetOrCreateAsync(widgetId, cancellationToken).ConfigureAwait(false);
        await registration.OperationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            DemandCurrent(registration);
            var residencyMode = WidgetResidencyPolicies.Resolve(
                registration.Configured.ResidencyPolicy).Mode;
            var hiddenAndRestricted =
                registration.HostLifecycle == WidgetLifecycleState.Background &&
                residencyMode is WidgetResidencyMode.SuspendWhenHidden or
                    WidgetResidencyMode.UnloadAfterIdle;
            var incremental = transactionKind ==
                WidgetPresentationTransactionKind.IncrementalUpdate;
            if (incremental && registration.CachedSnapshot?.Sequence != baseSequence)
                throw new BridgeStalePresentationBaseException();
            var retainedWorkerStart = 0;
            WidgetRuntimePresentation presentation;
            if (hiddenAndRestricted)
            {
                var snapshot = registration.CachedSnapshot ??
                    throw new BridgeProtocolException(
                        "A hidden suspended widget has no cached snapshot. Make it Visible before rendering.");
                presentation = new(
                    transactionKind, baseSequence,
                    recoveryOriginSequence, snapshot, null);
            }
            else
            {
                retainedWorkerStart = incremental
                    ? registration.DemandCurrentPresentationBase(baseSequence)
                    : 0;
                registration.CancelIdleUnload();
                var generation = registration.Configured.PublicDescriptor().PresentationGeneration;
                presentation = await ExecuteClientOperationAsync(
                        registration,
                        (client, token) => client.GetPresentationAsync(
                            incremental ? capabilities : PresentationUpdateCapabilities.None,
                            generation,
                            incremental ? baseSequence : 0,
                            transactionKind,
                            recoveryOriginSequence,
                            token),
                        cancellationToken)
                    .ConfigureAwait(false);
                DemandCurrent(registration);
                if (incremental && registration.Client.Starts != retainedWorkerStart)
                    throw new BridgeStalePresentationBaseException();
                if (presentation.TransactionKind !=
                        transactionKind ||
                    presentation.RequestBaseSequence != baseSequence ||
                    presentation.RecoveryOriginSequence != recoveryOriginSequence)
                    throw new BridgeProtocolException(
                        "Worker returned a presentation for a different transaction kind.");
                registration.CommitCachedSnapshot(presentation.Snapshot);
                ScheduleIdleUnload(registration, sessionCancellation);
            }
            return AdmitPublication(
                registration,
                new BridgeClientPresentation(
                    registration.Configured,
                    transactionKind,
                    baseSequence,
                    recoveryOriginSequence,
                    presentation.Snapshot,
                    presentation.Update));
        }
        finally
        {
            registration.OperationGate.Release();
        }
    }

    private static void ValidatePresentationRequest(
        WidgetPresentationTransactionKind transactionKind,
        PresentationUpdateCapabilities capabilities,
        long baseSequence,
        long recoveryOriginSequence)
    {
        ArgumentNullException.ThrowIfNull(capabilities);
        var none = capabilities.MaximumProtocolVersion == 0 &&
            capabilities.MaximumOperationsPerBatch == 0 &&
            capabilities.MaximumBatchBytes == 0;
        var bounded = capabilities.MaximumProtocolVersion ==
                ProtocolConstants.AtomicPresentationUpdateVersion &&
            capabilities.MaximumOperationsPerBatch is > 0 and
                <= ProtocolConstants.MaximumPresentationUpdateOperations &&
            capabilities.MaximumBatchBytes is > 0 and
                <= ProtocolConstants.MaximumPresentationUpdateBytes;
        if (!none && !bounded)
            throw new BridgeProtocolException(
                "Presentation update capabilities are malformed or unsupported.");
        var incremental = transactionKind ==
            WidgetPresentationTransactionKind.IncrementalUpdate;
        var recovery = transactionKind ==
            WidgetPresentationTransactionKind.RecoveryCheckpoint;
        if (!Enum.IsDefined(transactionKind))
            throw new BridgeProtocolException(
                "Presentation transaction kind is unsupported.");
        if (bounded && (!incremental || baseSequence <= 0 ||
                recoveryOriginSequence != 0))
            throw new BridgeProtocolException(
                "Presentation updates require a positive current base sequence.");
        if (none && (incremental || baseSequence != 0 ||
                (recovery ? recoveryOriginSequence <= 0 :
                    recoveryOriginSequence != 0)))
            throw new BridgeProtocolException(
                "Checkpoint requests cannot claim a presentation base sequence.");
    }

    internal async Task<BridgeClientPublication<WidgetLifecycleState>> SetLifecycleAsync(
        string widgetId,
        WidgetLifecycleState state,
        CancellationToken sessionCancellation,
        CancellationToken cancellationToken)
    {
        var registration = await GetOrCreateAsync(widgetId, cancellationToken).ConfigureAwait(false);
        await registration.OperationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            DemandCurrent(registration);
            registration.CancelIdleUnload();
            await ExecuteClientOperationAsync(
                    registration,
                    (client, token) => client.SetLifecycleStateAsync(state, token),
                    cancellationToken)
                .ConfigureAwait(false);
            DemandCurrent(registration);
            registration.HostLifecycle = state;
            ScheduleIdleUnload(registration, sessionCancellation);
            return AdmitPublication(registration, state);
        }
        finally
        {
            registration.OperationGate.Release();
        }
    }

    internal async Task<BridgeClientPublication<BridgeClientPresentation>>
        EstablishPresentationAsync(
        string widgetId,
        WidgetLifecycleState state,
        WidgetPresentationTransactionKind transactionKind,
        PresentationUpdateCapabilities capabilities,
        long baseSequence,
        long recoveryOriginSequence,
            CancellationToken sessionCancellation,
            CancellationToken cancellationToken)
    {
        if (state == WidgetLifecycleState.Background)
            throw new BridgeProtocolException(
                "A background widget cannot establish a visible presentation.");
        ValidatePresentationRequest(
            transactionKind, capabilities, baseSequence, recoveryOriginSequence);
        var registration = await GetOrCreateAsync(widgetId, cancellationToken)
            .ConfigureAwait(false);
        await registration.OperationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            DemandCurrent(registration);
            var incremental = transactionKind ==
                WidgetPresentationTransactionKind.IncrementalUpdate;
            var retainedWorkerStart = incremental
                ? registration.DemandCurrentPresentationBase(baseSequence)
                : 0;
            var priorHostLifecycle = registration.HostLifecycle;
            registration.CancelIdleUnload();
            await ExecuteClientOperationAsync(
                    registration,
                    (client, token) => client.SetLifecycleStateAsync(state, token),
                    cancellationToken)
                .ConfigureAwait(false);
            if (incremental && registration.Client.Starts != retainedWorkerStart)
                throw new BridgeStalePresentationBaseException();
            var lifecycleStartOrdinal = registration.Client.Starts;
            try
            {
                DemandCurrent(registration);
                var generation = registration.Configured.PublicDescriptor().PresentationGeneration;
                var presentation = await ExecuteClientOperationAsync(
                        registration,
                        (client, token) => client.GetPresentationAsync(
                            incremental ? capabilities : PresentationUpdateCapabilities.None,
                            generation,
                            incremental ? baseSequence : 0,
                            transactionKind,
                            recoveryOriginSequence,
                            token),
                        cancellationToken)
                    .ConfigureAwait(false);
                DemandCurrent(registration);
                if (incremental && registration.Client.Starts != retainedWorkerStart)
                    throw new BridgeStalePresentationBaseException();
                if (presentation.TransactionKind !=
                        transactionKind ||
                    presentation.RequestBaseSequence != baseSequence ||
                    presentation.RecoveryOriginSequence != recoveryOriginSequence)
                    throw new BridgeProtocolException(
                        "Worker returned a presentation for a different transaction kind.");

                var publication = CommitEstablishmentPublication(
                    registration, state, transactionKind, presentation);
                ScheduleIdleUnload(registration, sessionCancellation);
                return publication;
            }
            catch
            {
                if (state != priorHostLifecycle)
                    await TryRestoreEstablishmentLifecycleAsync(
                            registration,
                            priorHostLifecycle,
                            lifecycleStartOrdinal,
                            sessionCancellation)
                        .ConfigureAwait(false);
                throw;
            }
        }
        finally
        {
            registration.OperationGate.Release();
        }
    }

    private BridgeClientPublication<BridgeClientPresentation>
        CommitEstablishmentPublication(
            ClientRegistration registration,
            WidgetLifecycleState state,
            WidgetPresentationTransactionKind transactionKind,
            WidgetRuntimePresentation presentation)
    {
        lock (_gate)
        {
            if (!IsCurrentLocked(registration))
                throw new BridgeProtocolException(
                    $"Widget '{registration.Configured.Id}' changed during establishment.");
            var publication = AdmitPublicationLocked(
                registration,
                new BridgeClientPresentation(
                    registration.Configured,
                    transactionKind,
                    presentation.RequestBaseSequence,
                    presentation.RecoveryOriginSequence,
                    presentation.Snapshot,
                    presentation.Update));
            // The bridge-visible lifecycle, retained base, and publication token
            // become observable together only after worker presentation succeeds.
            registration.CommitCachedSnapshot(presentation.Snapshot);
            registration.HostLifecycle = state;
            return publication;
        }
    }


    private async Task TryRestoreEstablishmentLifecycleAsync(
        ClientRegistration registration,
        WidgetLifecycleState priorHostLifecycle,
        int lifecycleStartOrdinal,
        CancellationToken sessionCancellation)
    {
        try
        {
            if (!IsCurrent(registration) ||
                registration.HostLifecycle != priorHostLifecycle ||
                !registration.Client.IsRunning ||
                registration.Client.Starts != lifecycleStartOrdinal)
                return;
            using var compensationDeadline = new CancellationTokenSource(OperationDeadline);
            _ = await registration.Client.TryRestoreLifecycleStateAsync(
                    priorHostLifecycle,
                    lifecycleStartOrdinal,
                    compensationDeadline.Token)
                .ConfigureAwait(false);
            if (IsCurrent(registration))
                ScheduleIdleUnload(registration, sessionCancellation);
        }
        catch (Exception)
        {
            // Compensation is bounded and must never mask the establishment failure.
        }
    }

    internal async Task<BridgeClientPublication<WidgetOperationAdmission>> AdmitActionAsync(
        string widgetId,
        WidgetActionEvent action,
        CancellationToken sessionCancellation,
        CancellationToken cancellationToken)
    {
        var registration = await GetOrCreateAsync(widgetId, cancellationToken).ConfigureAwait(false);
        return await AdmitActionAsync(
            registration, action, sessionCancellation, cancellationToken).ConfigureAwait(false);
    }

    internal async Task<BridgeClientPublication<WidgetOperationAdmission>> AdmitQuickActionAsync(
        string widgetId,
        string quickActionId,
        long sequence,
        long monotonicTimestampMicroseconds,
        CancellationToken sessionCancellation,
        CancellationToken cancellationToken)
    {
        var registration = await GetOrCreateAsync(widgetId, cancellationToken).ConfigureAwait(false);
        var quickAction = registration.Configured.QuickActions.SingleOrDefault(
            action => string.Equals(action.Id, quickActionId, StringComparison.Ordinal)) ??
            throw new BridgeProtocolException($"Unknown quick action '{quickActionId}'.");
        return await AdmitActionAsync(
            registration,
            new WidgetActionEvent(
                quickAction.ActionId,
                quickAction.SourceElementId,
                quickAction.ControllerButton,
                ControllerEventPhase.Pressed,
                sequence,
                monotonicTimestampMicroseconds),
            sessionCancellation,
            cancellationToken).ConfigureAwait(false);
    }

    internal async Task<BridgeClientPublication<bool>> SendControllerInputAsync(
        string widgetId,
        ControllerInputEvent input,
        string? expectedRuntimeGeneration,
        CancellationToken sessionCancellation,
        CancellationToken cancellationToken,
        string? expectedActionId = null,
        string? expectedSelectOptionActionId = null)
    {
        var registration = await GetOrCreateAsync(widgetId, cancellationToken).ConfigureAwait(false);
        await registration.OperationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (expectedActionId is not null && expectedSelectOptionActionId is not null)
                throw new BridgeProtocolException(
                    "Controller input cannot combine exact action authority kinds.");
            DemandCurrent(registration);
            if (input.Context == ControllerInputContext.PinnedSurface &&
                string.IsNullOrWhiteSpace(expectedRuntimeGeneration))
                throw new BridgeProtocolException(
                    "Host-owned controller notification requires exact runtime generation authority.");
            if (expectedRuntimeGeneration is not null &&
                !string.Equals(
                    registration.Configured.PublicDescriptor().RuntimeGeneration,
                    expectedRuntimeGeneration,
                    StringComparison.Ordinal))
                throw new BridgeProtocolException(
                    "Controller input runtime authority is stale or unavailable.");
            if (input.Context != ControllerInputContext.PinnedLayoutSelection)
                DemandInteractionAllowed(registration);
            var selectAction = DemandSelectActionAuthority(
                registration, input, expectedSelectOptionActionId);
            if (selectAction is not null)
            {
                registration.CancelIdleUnload();
                var admission = await ExecuteClientOperationAsync(
                        registration,
                        (client, token) => client.AdmitActionAsync(selectAction, token),
                        cancellationToken)
                    .ConfigureAwait(false);
                DemandCurrent(registration);
                ScheduleIdleUnload(registration, sessionCancellation);
                return AdmitPublication(
                    registration,
                    admission is not (WidgetOperationAdmission.RejectedInactive or
                        WidgetOperationAdmission.RejectedCapacity));
            }
            var admitted = DemandPinnedSurfaceAuthority(
                registration, input, expectedActionId);
            // A pinned button that binds to nothing here is an ordinary
            // not-handled outcome, not a loss of input authority. Report it the
            // way every other surface does instead of dispatching input the
            // admitted projection has no action for.
            if (admitted is null) return AdmitPublication(registration, false);
            input = admitted;
            registration.CancelIdleUnload();
            var handled = await ExecuteClientOperationAsync(
                    registration,
                    (client, token) => client.SendControllerInputAsync(
                        input,
                        ResolveDashboardGestureAuthority(registration, input),
                        token),
                    cancellationToken)
                .ConfigureAwait(false);
            DemandCurrent(registration);
            ScheduleIdleUnload(registration, sessionCancellation);
            return AdmitPublication(registration, handled);
        }
        finally
        {
            registration.OperationGate.Release();
        }
    }

    internal async Task<BridgeClientPublication<WidgetLifecycleState>> RestartAsync(
        string widgetId,
        CancellationToken cancellationToken)
    {
        AdmitRestart();
        try
        {
            var replacement = await ReplaceCoreAsync(
                widgetId,
                static (_, _) => Task.FromResult<object?>(null),
                honorCallerCancellationAfterReservation: true,
                cancellationToken).ConfigureAwait(false);
            return replacement.Publication;
        }
        finally
        {
            ReleaseRestart();
        }
    }

    internal async Task<BridgeClientReplacement<T>> ReplaceAsync<T>(
        string widgetId,
        Func<ConfiguredWidget, CancellationToken, Task<T>> operation,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(operation);
        AdmitRestart();
        try
        {
            return await ReplaceCoreAsync(
                    widgetId, operation,
                    honorCallerCancellationAfterReservation: false,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            ReleaseRestart();
        }
    }

    private async Task<BridgeClientReplacement<T>> ReplaceCoreAsync<T>(
        string widgetId,
        Func<ConfiguredWidget, CancellationToken, Task<T>> operation,
        bool honorCallerCancellationAfterReservation,
        CancellationToken cancellationToken)
    {
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ClientRegistration oldRegistration;
            Task? priorRetirement = null;
            lock (_gate)
            {
                DemandNotDisposed();
                var configured = _catalog.GetConfigured(widgetId);
                if (!_clients.TryGetValue(widgetId, out oldRegistration!))
                {
                    oldRegistration = CreateRegistration(configured);
                    _clients.Add(widgetId, oldRegistration);
                }
                if (oldRegistration.IsRetiring)
                    priorRetirement = oldRegistration.RetirementCompletion;
            }
            if (priorRetirement is not null)
            {
                await priorRetirement.WaitAsync(cancellationToken).ConfigureAwait(false);
                continue;
            }

            using var gateTimeout = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken);
            gateTimeout.CancelAfter(OperationDeadline);
            try
            {
                await oldRegistration.OperationGate.WaitAsync(gateTimeout.Token)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                throw new BridgeProtocolException(
                    $"Widget '{widgetId}' did not become available for restart.");
            }

            var reserved = false;
            var published = false;
            var startRetirement = false;
            ClientRegistration? freshRegistration = null;
            WidgetLifecycleState previousState;
            T operationResult = default!;
            Exception? operationFailure = null;
            try
            {
                lock (_gate)
                {
                    DemandNotDisposed();
                    _ = _catalog.GetConfigured(widgetId);
                    if (!_clients.TryGetValue(widgetId, out var current) ||
                        !ReferenceEquals(current, oldRegistration) ||
                        oldRegistration.IsRetiring)
                        continue;
                    previousState = oldRegistration.HostLifecycle;
                    startRetirement = ReserveRetirementLocked(
                        oldRegistration, restartReserved: true);
                    reserved = true;
                }
            }
            finally
            {
                oldRegistration.OperationGate.Release();
            }

            if (startRetirement) StartRetirement(oldRegistration);
            try
            {
                using var restartDeadline = honorCallerCancellationAfterReservation
                    ? CancellationTokenSource.CreateLinkedTokenSource(cancellationToken)
                    : new CancellationTokenSource();
                restartDeadline.CancelAfter(RestartDeadline);
                try
                {
                    await oldRegistration.ResourceRetired.WaitAsync(restartDeadline.Token)
                        .ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested ||
                    !honorCallerCancellationAfterReservation)
                {
                    throw new BridgeProtocolException(
                        $"Widget '{widgetId}' did not retire within the restart deadline.");
                }
                if (oldRegistration.RetirementFailure is { } retirementFailure)
                    throw new AggregateException(
                        $"Widget '{widgetId}' failed retirement.", retirementFailure);

                ConfiguredWidget configured;
                lock (_gate)
                {
                    DemandNotDisposed();
                    configured = _catalog.GetConfigured(widgetId);
                }
                try
                {
                    operationResult = await operation(configured, restartDeadline.Token)
                        .WaitAsync(restartDeadline.Token).ConfigureAwait(false);
                }
                catch (Exception exception)
                {
                    operationFailure = exception;
                }
                freshRegistration = CreateRegistration(configured);
                if (previousState != WidgetLifecycleState.Background)
                {
                    await freshRegistration.Client.SetLifecycleStateAsync(
                        previousState, restartDeadline.Token).ConfigureAwait(false);
                    freshRegistration.HostLifecycle = previousState;
                }

                BridgeClientPublication<WidgetLifecycleState> result;
                lock (_gate)
                {
                    DemandNotDisposed();
                    var latest = _catalog.GetConfigured(widgetId);
                    if (!_clients.TryGetValue(widgetId, out var current) ||
                        !ReferenceEquals(current, oldRegistration) ||
                        !oldRegistration.RestartReserved ||
                        !string.Equals(
                            latest.WorkerFingerprint,
                            freshRegistration.Configured.WorkerFingerprint,
                            StringComparison.Ordinal))
                        throw new BridgeProtocolException(
                            $"Widget '{widgetId}' changed while its fresh worker was prepared.");
                    _clients[widgetId] = freshRegistration;
                    result = AdmitPublicationLocked(freshRegistration, previousState);
                    oldRegistration.RestartReserved = false;
                    oldRegistration.CompleteRetirementLocked();
                    published = true;
                }
                if (operationFailure is not null)
                {
                    result.Dispose();
                    System.Runtime.ExceptionServices.ExceptionDispatchInfo
                        .Capture(operationFailure).Throw();
                }
                return new BridgeClientReplacement<T>(result, operationResult);
            }
            catch (Exception exception)
            {
                if (published) throw;
                if (freshRegistration is not null)
                {
                    try
                    {
                        await DisposeUnpublishedAsync(freshRegistration).ConfigureAwait(false);
                    }
                    catch (Exception cleanupFailure)
                    {
                        throw new AggregateException(exception, cleanupFailure);
                    }
                }
                throw;
            }
            finally
            {
                if (reserved && !published)
                {
                    lock (_gate) AbandonRestartLocked(oldRegistration);
                }
            }
        }
    }

    internal bool ApplyCatalog(BridgeCatalog catalog, long revision)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        if (revision < 0) throw new ArgumentOutOfRangeException(nameof(revision));
        List<ClientRegistration>? starts = null;
        lock (_gate)
        {
            DemandNotDisposed();
            if (revision < _catalogRevision ||
                (revision == _catalogRevision && _catalog.IsEquivalentTo(catalog)))
                return false;
            if (revision == _catalogRevision) return false;
            _catalog = catalog;
            _catalogRevision = revision;
            foreach (var pair in _clients.ToArray())
            {
                ConfiguredWidget? configured = null;
                try { configured = catalog.GetConfigured(pair.Key); }
                catch (BridgeProtocolException) { }
                if (configured is not null && string.Equals(
                        configured.WorkerFingerprint,
                        pair.Value.Configured.WorkerFingerprint,
                        StringComparison.Ordinal))
                {
                    pair.Value.Configured = configured;
                    continue;
                }
                if (ReserveRetirementLocked(pair.Value, restartReserved: false))
                    (starts ??= []).Add(pair.Value);
            }
        }
        if (starts is not null)
            foreach (var registration in starts) StartRetirement(registration);
        return true;
    }

    internal BridgeClientPublication<BridgeWidgetDescriptor>? TryAdmitHostEffect(
        string widgetId,
        string expectedWorkerFingerprint)
    {
        lock (_gate)
        {
            if (_clients.TryGetValue(widgetId, out var registration) &&
                !registration.IsRetiring &&
                string.Equals(
                    registration.Configured.WorkerFingerprint,
                    expectedWorkerFingerprint,
                    StringComparison.Ordinal) &&
                registration.HostLifecycle == WidgetLifecycleState.Interactive)
                return AdmitPublicationLocked(
                    registration, registration.Configured.PublicDescriptor());
        }
        return null;
    }

    internal BridgeClientPublication<ConfiguredWidget> AdmitArtwork(
        string widgetId,
        string artworkHandle,
        string? expectedRuntimeGeneration = null,
        string? expectedPresentationGeneration = null)
    {
        lock (_gate)
        {
            if (_clients.TryGetValue(widgetId, out var registration) &&
                !registration.IsRetiring &&
                registration.CachedSnapshot is { } snapshot &&
                ContainsArtwork(snapshot, artworkHandle) &&
                MatchesArtworkOrigin(
                    registration.Configured,
                    expectedRuntimeGeneration,
                    expectedPresentationGeneration))
                return AdmitPublicationLocked(registration, registration.Configured);
        }
        throw new BridgeStaleArtworkAuthorityException(
            "Artwork authority is stale or unavailable.");
    }

    internal async Task<BridgeClientPublication<BridgeResolvedArtwork>> ResolveArtworkAsync(
        string widgetId,
        string artworkHandle,
        string? expectedRuntimeGeneration,
        string? expectedPresentationGeneration,
        CancellationToken sessionCancellation,
        CancellationToken cancellationToken)
    {
        if (!BridgeRequestKey.IsBoundedIdentifier(artworkHandle))
            throw new BridgeProtocolException("Artwork handle is invalid.");
        var registration = await GetOrCreateAsync(widgetId, cancellationToken)
            .ConfigureAwait(false);
        await registration.OperationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            DemandCurrent(registration);
            DemandArtworkOrigin(
                registration.Configured,
                expectedRuntimeGeneration,
                expectedPresentationGeneration);
            if (registration.CachedSnapshot is not { } snapshot ||
                !ContainsArtwork(snapshot, artworkHandle))
                throw new BridgeProtocolException(
                    "Artwork authority is stale or unavailable.");
            registration.CancelIdleUnload();
            var artwork = await ExecuteClientOperationAsync(
                    registration,
                    (client, token) => client.ResolveArtworkAsync(artworkHandle, token),
                    cancellationToken)
                .ConfigureAwait(false);
            DemandCurrent(registration);
            DemandArtworkOrigin(
                registration.Configured,
                expectedRuntimeGeneration,
                expectedPresentationGeneration);
            if (registration.CachedSnapshot is not { } current ||
                !ContainsArtwork(current, artworkHandle))
                throw new BridgeProtocolException(
                    "Artwork authority retired during resolution.");
            ScheduleIdleUnload(registration, sessionCancellation);
            return AdmitPublication(
                registration,
                new BridgeResolvedArtwork(
                    registration.Configured,
                    registration.Configured.WorkerFingerprint,
                    artwork));
        }
        finally
        {
            registration.OperationGate.Release();
        }
    }

    private static bool ContainsArtwork(ViewSnapshot snapshot, string artworkHandle) =>
        Contains(snapshot.Root, artworkHandle) ||
        snapshot.PinnedLayouts.Any(layout =>
            layout.Root is not null && Contains(layout.Root, artworkHandle));

    private static bool MatchesArtworkOrigin(
        ConfiguredWidget configured,
        string? expectedRuntimeGeneration,
        string? expectedPresentationGeneration)
    {
        if (expectedRuntimeGeneration is null && expectedPresentationGeneration is null)
            return true;
        if (expectedRuntimeGeneration is null || expectedPresentationGeneration is null)
            return false;
        var descriptor = configured.PublicDescriptor();
        return string.Equals(
                   descriptor.RuntimeGeneration,
                   expectedRuntimeGeneration,
                   StringComparison.Ordinal) &&
               string.Equals(
                   descriptor.PresentationGeneration,
                   expectedPresentationGeneration,
                   StringComparison.Ordinal);
    }

    private static void DemandArtworkOrigin(
        ConfiguredWidget configured,
        string? expectedRuntimeGeneration,
        string? expectedPresentationGeneration)
    {
        if (!MatchesArtworkOrigin(
                configured,
                expectedRuntimeGeneration,
                expectedPresentationGeneration))
            throw new BridgeStaleArtworkAuthorityException(
                "Artwork authority retired before resolution.");
    }

    private static bool Contains(ViewNode node, string artworkHandle)
    {
        if (string.Equals(node.ArtworkHandle, artworkHandle, StringComparison.Ordinal) ||
            string.Equals(node.FocusBackgroundArtworkHandle, artworkHandle, StringComparison.Ordinal))
            return true;
        if (node.FocusPresentation is not null && Contains(node.FocusPresentation, artworkHandle))
            return true;
        if (node.DefaultFocusPresentation is not null &&
            Contains(node.DefaultFocusPresentation, artworkHandle))
            return true;
        foreach (var child in node.Children)
            if (Contains(child, artworkHandle)) return true;
        return false;
    }

    internal BridgeClientPublication<BridgeClientSnapshot> AdmitEmbeddedMedia(
        BridgeEmbeddedMediaRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        lock (_gate)
        {
            if (_clients.TryGetValue(request.WidgetId, out var registration) &&
                !registration.IsRetiring && registration.CachedSnapshot is { } snapshot)
            {
                var descriptor = registration.Configured.PublicDescriptor();
                var media = snapshot.EmbeddedMediaSession;
                if (media is not null &&
                    snapshot.Sequence == request.Sequence &&
                    string.Equals(snapshot.WidgetInstanceId, request.InstanceId, StringComparison.Ordinal) &&
                    string.Equals(descriptor.RuntimeGeneration, request.RuntimeGeneration, StringComparison.Ordinal) &&
                    string.Equals(descriptor.PresentationGeneration, request.PresentationGeneration, StringComparison.Ordinal) &&
                    string.Equals(media.Id, request.SessionId, StringComparison.Ordinal))
                    return AdmitPublicationLocked(
                        registration,
                        new BridgeClientSnapshot(registration.Configured, snapshot));
            }
        }
        throw new BridgeProtocolException(
            "Embedded media authority is stale or unavailable.");
    }

    internal async Task PublishEmbeddedMediaPlaybackEventAsync(
        BridgeEmbeddedMediaPlaybackEventRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var registration = await GetOrCreateAsync(request.WidgetId, cancellationToken)
            .ConfigureAwait(false);
        await registration.OperationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            DemandCurrent(registration);
            var snapshot = registration.CachedSnapshot ?? throw new BridgeProtocolException(
                "Embedded media event has no current snapshot authority.");
            var descriptor = registration.Configured.PublicDescriptor();
            var media = snapshot.EmbeddedMediaSession;
            var playbackEvent = request.Event;
            if (playbackEvent.Sequence <= 0 || playbackEvent.CommandSequence < 0 ||
                string.IsNullOrWhiteSpace(playbackEvent.MediaKey) ||
                playbackEvent.MediaKey.Length > ProtocolConstants.MaximumEmbeddedMediaKeyLength ||
                !playbackEvent.MediaKey.All(character =>
                    char.IsAsciiLetterOrDigit(character) || character is '-' or '_' or '.') ||
                !Enum.IsDefined(playbackEvent.State) ||
                !double.IsFinite(playbackEvent.PositionSeconds) ||
                !double.IsFinite(playbackEvent.DurationSeconds) ||
                !double.IsFinite(playbackEvent.Volume) ||
                !double.IsFinite(playbackEvent.PlaybackRate) ||
                playbackEvent.PositionSeconds < 0 || playbackEvent.DurationSeconds < 0 ||
                playbackEvent.PositionSeconds > playbackEvent.DurationSeconds ||
                playbackEvent.DurationSeconds > 86_400 ||
                playbackEvent.Volume is < 0 or > 1 ||
                playbackEvent.PlaybackRate < ProtocolConstants.MinimumEmbeddedMediaPlaybackRate ||
                playbackEvent.PlaybackRate > ProtocolConstants.MaximumEmbeddedMediaPlaybackRate ||
                (playbackEvent.ErrorCode is { } errorCode &&
                    (!BridgeRequestKey.IsBoundedIdentifier(errorCode))))
                throw new BridgeProtocolException(
                    "Embedded media playback event is invalid.");
            if (media is null ||
                !string.Equals(snapshot.WidgetInstanceId, request.InstanceId, StringComparison.Ordinal) ||
                !string.Equals(descriptor.RuntimeGeneration, request.RuntimeGeneration, StringComparison.Ordinal) ||
                !string.Equals(descriptor.PresentationGeneration, request.PresentationGeneration, StringComparison.Ordinal) ||
                !string.Equals(media.Id, request.Event.SessionId, StringComparison.Ordinal) ||
                !registration.AdmitsEmbeddedMediaPlaybackEvent(
                    request.Sequence, playbackEvent, snapshot))
                throw new BridgeProtocolException(
                    "Embedded media event authority is stale or unavailable.");
            if (playbackEvent.CommandSequence > 0 &&
                (media.PendingCommand is not { } pendingCommand ||
                 pendingCommand.Sequence != playbackEvent.CommandSequence ||
                 !string.Equals(pendingCommand.MediaKey, playbackEvent.MediaKey,
                     StringComparison.Ordinal)))
                throw new BridgeProtocolException(
                    "Embedded media playback command authority is stale or unavailable.");
            if (playbackEvent.CommandSequence > 0 &&
                playbackEvent.ErrorCode is null &&
                media.PendingCommand is { } preferenceCommand &&
                ((preferenceCommand.Kind == EmbeddedMediaPlaybackCommandKind.SetPlaybackRate &&
                  preferenceCommand.PlaybackRate != playbackEvent.PlaybackRate) ||
                 (preferenceCommand.Kind == EmbeddedMediaPlaybackCommandKind.SetMuted &&
                  preferenceCommand.Muted != playbackEvent.Muted) ||
                 (preferenceCommand.Kind == EmbeddedMediaPlaybackCommandKind.SetLoop &&
                  preferenceCommand.Loop != playbackEvent.Loop)))
                throw new BridgeProtocolException(
                    "Embedded media playback preference terminal state does not match the pending command.");
            await registration.Client.SendEmbeddedMediaPlaybackEventAsync(
                request.Event, cancellationToken).ConfigureAwait(false);
            if (playbackEvent.CommandSequence > 0)
                registration.RecordEmbeddedMediaPlaybackTerminal(playbackEvent);
            DemandCurrent(registration);
        }
        finally
        {
            registration.OperationGate.Release();
        }
    }

    internal BridgeClientPublication<ConfiguredWidget> AdmitProtectedWifi(
        string widgetId,
        string runtimeGeneration)
    {
        lock (_gate)
        {
            if (_clients.TryGetValue(widgetId, out var registration) &&
                !registration.IsRetiring &&
                registration.HostLifecycle == WidgetLifecycleState.Interactive &&
                string.Equals(
                    registration.Configured.PublicDescriptor().RuntimeGeneration,
                    runtimeGeneration,
                    StringComparison.Ordinal) &&
                registration.Configured.PublicDescriptor().ProtectedWifiPromptSupported)
                return AdmitPublicationLocked(registration, registration.Configured);
        }
        throw new BridgeProtocolException(
            "Protected Wi-Fi authority is stale or unavailable.");
    }

    internal BridgeClientPublication<ConfiguredWidget> AdmitLocalWidgetPackageImport(
        BridgeLocalWidgetPackageOrigin origin)
    {
        ArgumentNullException.ThrowIfNull(origin);
        lock (_gate)
        {
            if (_clients.TryGetValue(origin.WidgetId, out var registration) &&
                !registration.IsRetiring &&
                registration.HostLifecycle == WidgetLifecycleState.Interactive &&
                WidgetBridgeServer.IsTrustedSettings(registration.Configured))
            {
                var descriptor = registration.Configured.PublicDescriptor();
                if (string.Equals(descriptor.Id, origin.WidgetId, StringComparison.Ordinal) &&
                    string.Equals(registration.Configured.PackageId, origin.PackageId, StringComparison.Ordinal) &&
                    string.Equals(registration.Configured.PublisherId, origin.PublisherId, StringComparison.Ordinal) &&
                    string.Equals(descriptor.InstanceId, origin.InstanceId, StringComparison.Ordinal) &&
                    string.Equals(descriptor.RuntimeGeneration, origin.RuntimeGeneration, StringComparison.Ordinal) &&
                    string.Equals(descriptor.PresentationGeneration, origin.PresentationGeneration, StringComparison.Ordinal))
                    return AdmitPublicationLocked(registration, registration.Configured);
            }
        }
        throw new BridgeProtocolException(
            "Local widget package import authority is stale or unavailable.");
    }

    internal BridgeClientPublication<ConfiguredWidget>? TryAdmitArtwork(
        string widgetId,
        string expectedWorkerFingerprint,
        string? expectedRuntimeGeneration = null,
        string? expectedPresentationGeneration = null)
    {
        lock (_gate)
        {
            if (_clients.TryGetValue(widgetId, out var registration) &&
                !registration.IsRetiring &&
                string.Equals(
                    registration.Configured.WorkerFingerprint,
                    expectedWorkerFingerprint,
                    StringComparison.Ordinal) &&
                MatchesArtworkOrigin(
                    registration.Configured,
                    expectedRuntimeGeneration,
                    expectedPresentationGeneration))
                return AdmitPublicationLocked(registration, registration.Configured);
        }
        return null;
    }

    public async ValueTask DisposeAsync()
    {
        ClientRegistration[]? registrations = null;
        List<ClientRegistration>? starts = null;
        Task? restartsDrained = null;
        lock (_gate)
        {
            if (!_disposed)
            {
                _disposed = true;
                registrations = _clients.Values.ToArray();
                foreach (var registration in registrations)
                {
                    registration.RestartReserved = false;
                    if (ReserveRetirementLocked(registration, restartReserved: false))
                        (starts ??= []).Add(registration);
                    if (registration.ResourceRetired.IsCompleted)
                        CompleteRegistrationRetirementLocked(registration);
                }
                restartsDrained = _restartsDrained?.Task ?? Task.CompletedTask;
            }
        }
        if (registrations is null)
        {
            await _terminal.Task.ConfigureAwait(false);
            return;
        }

        try
        {
            if (starts is not null)
                foreach (var registration in starts) StartRetirement(registration);
            await Task.WhenAll(registrations.Select(item => item.RetirementCompletion))
                .ConfigureAwait(false);
            await restartsDrained!.ConfigureAwait(false);
            while (true)
            {
                Task[] active;
                lock (_gate)
                    active = _activeRetirements.Select(item => item.ResourceRetired).ToArray();
                if (active.Length == 0) break;
                await Task.WhenAll(active).ConfigureAwait(false);
            }

            Exception? firstFailure;
            int failureCount;
            lock (_gate)
            {
                firstFailure = _firstTerminalFailure;
                failureCount = _terminalFailureCount;
            }
            if (firstFailure is not null)
                throw new AggregateException(
                    $"{failureCount} bridge client terminal operation(s) failed; only the first failure is retained.",
                    firstFailure);
            _terminal.TrySetResult();
        }
        catch (Exception exception)
        {
            _terminal.TrySetException(exception);
            throw;
        }
    }

    private async Task<ClientRegistration> GetOrCreateAsync(
        string widgetId,
        CancellationToken cancellationToken)
    {
        while (true)
        {
            Task? retirement = null;
            ClientRegistration? start = null;
            lock (_gate)
            {
                DemandNotDisposed();
                var configured = _catalog.GetConfigured(widgetId);
                if (_clients.TryGetValue(widgetId, out var existing))
                {
                    if (!existing.IsRetiring && string.Equals(
                            existing.Configured.WorkerFingerprint,
                            configured.WorkerFingerprint,
                            StringComparison.Ordinal))
                        return existing;
                    if (ReserveRetirementLocked(existing, restartReserved: false))
                        start = existing;
                    retirement = existing.RetirementCompletion;
                }
                else
                {
                    var registration = CreateRegistration(configured);
                    _clients.Add(widgetId, registration);
                    return registration;
                }
            }

            if (start is not null) StartRetirement(start);
            await retirement.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private bool ReserveRetirementLocked(
        ClientRegistration registration,
        bool restartReserved)
    {
        registration.BeginRetirementLocked();
        if (restartReserved) registration.RestartReserved = true;
        if (registration.RetirementStarted) return false;
        registration.RetirementStarted = true;
        _activeRetirements.Add(registration);
        return true;
    }

    private void StartRetirement(ClientRegistration registration)
    {
        // The method catches every terminal exception and publishes completion
        // through ResourceRetired/RetirementCompletion. _activeRetirements is
        // the bounded owner used by terminal registry disposal.
        _ = RetireRegistrationAsync(registration);
    }

    private async Task RetireRegistrationAsync(ClientRegistration registration)
    {
        Exception? failure = null;
        try
        {
            RecordLifetime(
                registration,
                new WidgetProcessLifetimeDiagnostic(
                    WidgetProcessLifetimeEventKind.LifecycleRequested,
                    registration.Client.Starts,
                    null,
                    WidgetLifecycleState.Destroying,
                    FailureCode: "registry-retirement"));
            await registration.NotificationLane.CloseAndDrainAsync().ConfigureAwait(false);
            await DisposeRegistrationAsync(registration).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            failure = exception;
        }
        finally
        {
            lock (_gate)
            {
                registration.RetirementFailure = failure;
                if (failure is not null) RecordTerminalFailureLocked(failure);
                _activeRetirements.Remove(registration);
                registration.CompleteResourceRetirementLocked();
                if (!registration.RestartReserved)
                    CompleteRegistrationRetirementLocked(registration);
            }
        }
    }

    private void CompleteRegistrationRetirementLocked(ClientRegistration registration)
    {
        if (_clients.TryGetValue(registration.Configured.Id, out var current) &&
            ReferenceEquals(current, registration))
            _clients.Remove(registration.Configured.Id);
        registration.CompleteRetirementLocked();
    }

    private void AbandonRestartLocked(ClientRegistration registration)
    {
        registration.RestartReserved = false;
        if (registration.ResourceRetired.IsCompleted)
            CompleteRegistrationRetirementLocked(registration);
    }

    private ClientRegistration CreateRegistration(ConfiguredWidget configured)
    {
        var reservationOwner = new object();
        var client = _clientFactory(
            configured,
            () => _residentBudget.Reserve(
                reservationOwner,
                configured.Id,
                configured.MemoryRequestMb,
                WidgetBridgeServer.IsTrustedSettings(configured)));
        var registration = new ClientRegistration(
            configured,
            client,
            Interlocked.Increment(ref _nextRegistrationGeneration),
            RecordTerminalFailure);
        var runtimeGeneration = configured.PublicDescriptor().RuntimeGeneration;
        client.Invalidated += (_, revision) =>
            EnqueueNotification(
                registration,
                BridgeClientNotificationKind.Invalidation,
                cancellationToken => _invalidated(
                    new BridgeClientInvalidation(configured.Id, revision), cancellationToken),
                requireInvalidationAuthority: true);
        client.ActionFailed += (_, failure) =>
            EnqueueNotification(
                registration,
                BridgeClientNotificationKind.Failure,
                cancellationToken => _actionFailed(new BridgeClientActionFailure(
                    configured.Id, runtimeGeneration, failure), cancellationToken));
        client.Failed += (_, failure) =>
        {
            registration.RecordFailure(failure);
            EnqueueNotification(
                registration,
                BridgeClientNotificationKind.Failure,
                cancellationToken => _failed(
                    new BridgeClientRuntimeFailure(configured.Id, failure), cancellationToken));
        };
        client.LifetimeChanged += (_, diagnostic) => RecordLifetime(registration, diagnostic);
        return registration;
    }

    private void RecordLifetime(
        ClientRegistration registration,
        WidgetProcessLifetimeDiagnostic diagnostic)
    {
        try
        {
            _lifetimeDiagnostic?.Invoke(new BridgeClientLifetimeDiagnostic(
                registration.Configured.Id,
                registration.Generation,
                WidgetResidencyPolicies.Resolve(
                    registration.Configured.ResidencyPolicy).Mode,
                diagnostic));
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            // Lifetime diagnostics are observational and cannot own retirement.
        }
    }

    private void EnqueueNotification(
        ClientRegistration registration,
        BridgeClientNotificationKind kind,
        Func<CancellationToken, Task> publish,
        bool requireInvalidationAuthority = false)
    {
        var startPump = false;
        lock (_gate)
        {
            if (!IsCurrentLocked(registration) ||
                (requireInvalidationAuthority && !registration.MayPublishInvalidation))
                return;
            var admission = registration.NotificationLane.Enqueue(
                kind,
                publish,
                () => ReleasePublication(registration),
                out startPump);
            if (admission == BridgeClientNotificationAdmission.Accepted)
                registration.AdmitPublicationLocked();
        }
        if (startPump) registration.NotificationLane.StartPump();
    }

    private BridgeClientPublication<TValue> AdmitPublication<TValue>(
        ClientRegistration registration,
        TValue value)
    {
        lock (_gate)
        {
            if (!IsCurrentLocked(registration))
                throw new BridgeProtocolException(
                    $"Widget '{registration.Configured.Id}' changed during the operation.");
            return AdmitPublicationLocked(registration, value);
        }
    }

    private BridgeClientPublication<TValue> AdmitPublicationLocked<TValue>(
        ClientRegistration registration,
        TValue value)
    {
        registration.AdmitPublicationLocked();
        return new BridgeClientPublication<TValue>(
            value,
            () => ReleasePublication(registration));
    }

    private void ReleasePublication(ClientRegistration registration)
    {
        lock (_gate) registration.ReleasePublicationLocked();
    }

    private void RecordTerminalFailure(Exception failure)
    {
        lock (_gate) RecordTerminalFailureLocked(failure);
    }

    private void RecordTerminalFailureLocked(Exception failure)
    {
        _firstTerminalFailure ??= failure;
        if (_terminalFailureCount < int.MaxValue) _terminalFailureCount++;
    }

    private void AdmitRestart()
    {
        lock (_gate)
        {
            DemandNotDisposed();
            if (_activeRestarts++ == 0)
                _restartsDrained = new TaskCompletionSource(
                    TaskCreationOptions.RunContinuationsAsynchronously);
        }
    }

    private void ReleaseRestart()
    {
        lock (_gate)
        {
            if (_activeRestarts <= 0)
                throw new InvalidOperationException("Restart admission was released twice.");
            if (--_activeRestarts == 0) _restartsDrained!.TrySetResult();
        }
    }

    private async Task<BridgeClientPublication<WidgetOperationAdmission>> AdmitActionAsync(
        ClientRegistration registration,
        WidgetActionEvent action,
        CancellationToken sessionCancellation,
        CancellationToken cancellationToken)
    {
        await registration.OperationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            DemandCurrent(registration);
            if (registration.HostLifecycle == WidgetLifecycleState.Background)
                return AdmitPublication(registration, WidgetOperationAdmission.RejectedInactive);
            registration.CancelIdleUnload();
            var admission = await ExecuteClientOperationAsync(
                    registration,
                    (client, token) => client.AdmitActionAsync(action, token),
                    cancellationToken)
                .ConfigureAwait(false);
            DemandCurrent(registration);
            ScheduleIdleUnload(registration, sessionCancellation);
            return AdmitPublication(registration, admission);
        }
        finally
        {
            registration.OperationGate.Release();
        }
    }

    private void ScheduleIdleUnload(
        ClientRegistration registration,
        CancellationToken sessionCancellation)
    {
        var policy = WidgetResidencyPolicies.Resolve(registration.Configured.ResidencyPolicy);
        if (policy.Mode != WidgetResidencyMode.UnloadAfterIdle ||
            registration.HostLifecycle != WidgetLifecycleState.Background ||
            !registration.Client.IsRunning || policy.IdleDuration is not { } delay)
            return;
        registration.ScheduleIdleUnload(
            sessionCancellation,
            (generation, cancellationToken) =>
                RunIdleUnloadAsync(registration, generation, delay, cancellationToken));
    }

    private async Task<T> ExecuteClientOperationAsync<T>(
        ClientRegistration registration,
        Func<IBridgeWidgetClient, CancellationToken, Task<T>> operation,
        CancellationToken cancellationToken)
    {
        try
        {
            return await operation(registration.Client, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (IsWidgetRuntimeFailure(exception))
        {
            // WidgetProcessClient owns retirement of its failed process session
            // and preserves restart-count authority on this registration.
            throw new BridgeWidgetRequestException(
                registration.Configured.Id,
                ClassifyWidgetRuntimeFailure(exception),
                exception);
        }
    }

    private async Task ExecuteClientOperationAsync(
        ClientRegistration registration,
        Func<IBridgeWidgetClient, CancellationToken, Task> operation,
        CancellationToken cancellationToken)
    {
        await ExecuteClientOperationAsync(
                registration,
                async (client, token) =>
                {
                    await operation(client, token).ConfigureAwait(false);
                    return true;
                },
                cancellationToken)
            .ConfigureAwait(false);
    }

    private static bool IsWidgetRuntimeFailure(Exception exception) => exception is
        WidgetProcessException or
        WidgetProcessAdmissionException or
        WidgetProtocolViolationException or
        IOException or
        TimeoutException or
        ObjectDisposedException or
        OperationCanceledException;

    private static string ClassifyWidgetRuntimeFailure(Exception exception) => exception switch
    {
        WidgetProcessAdmissionException => "worker-admission-failed",
        WidgetProtocolViolationException => "worker-protocol-failed",
        TimeoutException => "worker-request-timeout",
        IOException => "worker-transport-failed",
        ObjectDisposedException => "worker-session-ended",
        OperationCanceledException => "worker-request-cancelled",
        WidgetProcessException => "worker-runtime-failed",
        _ => "worker-request-failed",
    };

    private async Task RunIdleUnloadAsync(
        ClientRegistration registration,
        long generation,
        TimeSpan delay,
        CancellationToken cancellationToken)
    {
        try
        {
            await _delay(delay, cancellationToken).ConfigureAwait(false);
            await registration.OperationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (!registration.IsIdleUnloadCurrent(generation) ||
                    !IsCurrent(registration) ||
                    registration.HostLifecycle != WidgetLifecycleState.Background ||
                    !registration.Client.IsRunning ||
                    WidgetResidencyPolicies.Resolve(registration.Configured.ResidencyPolicy).Mode !=
                        WidgetResidencyMode.UnloadAfterIdle)
                    return;
                using var shutdown = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                shutdown.CancelAfter(RetireDeadline);
                await registration.Client.UnloadAsync(shutdown.Token).ConfigureAwait(false);
            }
            finally
            {
                registration.OperationGate.Release();
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
        }
    }

    private static void DemandInteractionAllowed(ClientRegistration registration)
    {
        if (registration.HostLifecycle != WidgetLifecycleState.Background) return;
        var mode = WidgetResidencyPolicies.Resolve(registration.Configured.ResidencyPolicy).Mode;
        if (mode is WidgetResidencyMode.SuspendWhenHidden or WidgetResidencyMode.UnloadAfterIdle)
            throw new BridgeProtocolException(
                "Hidden interaction is disabled by this widget's residency policy.");
    }

    private void DemandCurrent(ClientRegistration registration)
    {
        if (!IsCurrent(registration))
            throw new BridgeProtocolException(
                $"Widget '{registration.Configured.Id}' changed during the operation.");
    }

    private bool IsCurrent(ClientRegistration registration)
    {
        lock (_gate) return IsCurrentLocked(registration);
    }

    private bool IsCurrentLocked(ClientRegistration registration) =>
        !registration.IsRetiring &&
        _clients.TryGetValue(registration.Configured.Id, out var current) &&
        ReferenceEquals(current, registration);

    private async Task DisposeRegistrationAsync(ClientRegistration registration)
    {
        await registration.PublicationsDrained.ConfigureAwait(false);
        await registration.BeginTerminalAndDrainIdleUnloadAsync(OperationDeadline)
            .ConfigureAwait(false);
        var gateEntered = false;
        using var gateDeadline = new CancellationTokenSource(OperationDeadline);
        try
        {
            gateEntered = await registration.OperationGate.WaitAsync(
                OperationDeadline, gateDeadline.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }

        Task? disposal = null;
        try
        {
            disposal = registration.Client.DisposeAsync().AsTask();
            using var retireDeadline = new CancellationTokenSource(RetireDeadline);
            await disposal.WaitAsync(retireDeadline.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            RecordTerminalFailure(new TimeoutException(
                $"Widget '{registration.Configured.Id}' client disposal exceeded its deadline."));
            if (disposal is not null) ObserveLateCompletion(disposal);
        }
        catch (Exception exception) when (exception is IOException or
                                               InvalidOperationException or
                                               ObjectDisposedException)
        {
        }
        finally
        {
            if (gateEntered)
            {
                registration.OperationGate.Release();
                registration.OperationGate.Dispose();
            }
        }
    }

    private async Task DisposeUnpublishedAsync(ClientRegistration registration)
    {
        bool start;
        lock (_gate) start = ReserveRetirementLocked(registration, restartReserved: false);
        if (start) StartRetirement(registration);
        await registration.ResourceRetired.ConfigureAwait(false);
        if (registration.RetirementFailure is { } failure)
            throw new AggregateException("An unpublished bridge client failed disposal.", failure);
    }

    private void ObserveLateCompletion(Task task)
    {
        _ = task.ContinueWith(
            completed =>
            {
                if (completed.Exception is { } failure) RecordTerminalFailure(failure);
            },
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    private void DemandNotDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);

    private static WidgetDashboardGestureAuthority? ResolveDashboardGestureAuthority(
        ClientRegistration registration,
        ControllerInputEvent input)
    {
        if (input.Context != ControllerInputContext.DashboardQuickAction) return null;
        if (registration.HostLifecycle != WidgetLifecycleState.Visible)
            throw new BridgeProtocolException(
                "Dashboard quick actions require the widget to remain Visible.");
        var snapshot = registration.CachedSnapshot ?? throw new BridgeProtocolException(
            "Dashboard quick action has no cached rendered snapshot.");
        if (snapshot.Sequence != input.SnapshotSequence)
            throw new BridgeProtocolException(
                "Dashboard quick action targets a stale snapshot sequence.");
        var quickAction = snapshot.QuickActions.SingleOrDefault(
            action => action.Button == input.Button) ?? throw new BridgeProtocolException(
                "Dashboard button is not exposed by the cached snapshot.");
        if (input.Phase == ControllerEventPhase.Repeated &&
            quickAction.RepeatPolicy != ControllerActionRepeatPolicy.WhileHeld)
            throw new BridgeProtocolException(
                "Dashboard button does not admit held repetition.");
        if (input.Phase != ControllerEventPhase.Pressed ||
            input.Origin != ControllerInputOrigin.PhysicalController ||
            quickAction.Capability is null)
        {
            registration.AcceptDashboardInputSequence(input.Sequence);
            return null;
        }

        var requested = quickAction.Capability;
        if (!PlatformCapabilities.TryGet(requested.CapabilityId, out var capability) ||
            capability.KindForOperation(requested.OperationId) != BrokerCapabilityKind.Control ||
            !capability.Operations.Contains(requested.OperationId))
            throw new BridgeProtocolException(
                "Dashboard quick action names an unsupported control operation.");
        if (!registration.Configured.DeclaredCapabilities.Contains(
                requested.CapabilityId, StringComparer.Ordinal))
            throw new BridgeProtocolException(
                "Dashboard quick action capability is not declared by its package.");
        registration.AcceptDashboardInputSequence(input.Sequence);
        return new WidgetDashboardGestureAuthority(
            requested.CapabilityId,
            requested.OperationId,
            input.Sequence,
            input.SnapshotSequence,
            PlatformCapabilityBroker.MaximumDashboardGestureLifetime);
    }

    /// Returns the admitted input, or null when the pinned projection binds no
    /// action to this button. Every genuine authority failure still throws.
    private static ControllerInputEvent? DemandPinnedSurfaceAuthority(
        ClientRegistration registration,
        ControllerInputEvent input,
        string? expectedActionId)
    {
        if (input.Context != ControllerInputContext.PinnedSurface) return input;
        var snapshot = registration.CachedSnapshot ?? throw new BridgeProtocolException(
            "Pinned-surface input has no cached rendered snapshot.");
        var origin = registration.FindPinnedInputOriginSnapshot(input.SnapshotSequence) ??
            throw new BridgeStalePinnedInputAuthorityException(
                "Pinned-surface input origin snapshot authority is no longer available.");
        var originBinding = ResolvePinnedSurfaceInputBinding(origin, input, "origin");
        var currentBinding = ResolvePinnedSurfaceInputBinding(snapshot, input, "current");
        // A binding that appears or disappears between admission and now is an
        // authority change even when one side binds nothing.
        if (originBinding != currentBinding)
            throw new BridgeStalePinnedInputAuthorityException(
                "Pinned-surface input action binding changed after admission.");
        // An expected action ID is the host asserting one exact admitted
        // binding, so its absence stays an authority failure.
        if (expectedActionId is not null &&
            (originBinding is null || currentBinding is null ||
             !string.Equals(
                 originBinding.ActionId, expectedActionId, StringComparison.Ordinal) ||
             !string.Equals(
                 currentBinding.ActionId, expectedActionId, StringComparison.Ordinal)))
            throw new BridgeStalePinnedInputAuthorityException(
                "Pinned-surface input does not match its admitted action binding.");
        if (originBinding is null) return null;
        return input with { SnapshotSequence = snapshot.Sequence };
    }

    private static WidgetActionEvent? DemandSelectActionAuthority(
        ClientRegistration registration,
        ControllerInputEvent input,
        string? expectedActionId)
    {
        if (expectedActionId is null || input.Button != ControllerButton.A ||
            input.Phase != ControllerEventPhase.Pressed ||
            input.Context is not (ControllerInputContext.OpenWidget or
                ControllerInputContext.PinnedSurface)) return null;
        var current = registration.CachedSnapshot ?? throw new BridgeProtocolException(
            "Select input has no cached rendered snapshot.");
        var currentBinding = ResolveSelectBinding(current, input, expectedActionId, "current");
        if (input.Context == ControllerInputContext.PinnedSurface)
        {
            var origin = registration.FindPinnedInputOriginSnapshot(input.SnapshotSequence) ??
                throw new BridgeStalePinnedInputAuthorityException(
                    "Pinned Select origin authority is no longer available.");
            var originBinding = ResolveSelectBinding(origin, input, expectedActionId, "origin");
            if (originBinding != currentBinding)
                throw new BridgeStalePinnedInputAuthorityException(
                    "Pinned Select option authority changed after admission.");
        }
        else if (input.SnapshotSequence != current.Sequence)
        {
            throw new BridgeProtocolException("Open Select snapshot authority is stale.");
        }
        return new WidgetActionEvent(
            currentBinding.ActionId,
            currentBinding.SourceElementId,
            ControllerButton.A,
            ControllerEventPhase.Pressed,
            input.Sequence,
            input.MonotonicTimestampMicroseconds,
            InputScopeId: input.ActiveInputScopeId);
    }

    private static SelectActionBinding ResolveSelectBinding(
        ViewSnapshot snapshot,
        ControllerInputEvent input,
        string expectedActionId,
        string authority)
    {
        ViewNode root;
        string scope;
        if (input.Context == ControllerInputContext.PinnedSurface &&
            !string.Equals(input.PinnedLayoutId,
                PinnedSurfaceContract.FullWidgetLayoutId, StringComparison.Ordinal))
        {
            var layout = snapshot.PinnedLayouts.SingleOrDefault(candidate =>
                string.Equals(candidate.Id, input.PinnedLayoutId, StringComparison.Ordinal));
            if (layout?.Root is null || string.IsNullOrWhiteSpace(layout.ActiveInputScopeId))
                throw SelectAuthorityException(
                    input, $"Select targets an unavailable {authority} layout.");
            root = layout.Root;
            scope = layout.ActiveInputScopeId;
        }
        else
        {
            root = snapshot.Root;
            scope = snapshot.ActiveInputScopeId;
        }
        if (!string.Equals(input.ActiveInputScopeId, scope, StringComparison.Ordinal))
            throw SelectAuthorityException(input,
                $"Select targets a stale {authority} input scope.");
        var scopeRoot = FindInputScope(root, scope, isRoot: true);
        var node = scopeRoot is null || input.FocusedElementId is null ? null :
            FindNodeInScope(scopeRoot, input.FocusedElementId, isScopeRoot: true);
        if (node is null || node.Kind != ViewNodeKind.Select ||
            node.IsDisabled is true || node.IsBusy is true)
            throw SelectAuthorityException(input,
                $"Select targets an unavailable {authority} opener.");
        var option = node.SelectOptions.SingleOrDefault(candidate =>
            string.Equals(candidate.ActionId, expectedActionId, StringComparison.Ordinal));
        if (option is null || option.IsDisabled || option.IsBusy)
            throw SelectAuthorityException(input,
                $"Select targets an unavailable {authority} option.");
        return new(node.Id, option.Id, option.ActionId, option.Label,
            option.Glyph, option.AccessibilityLabel, option.IsDisabled, option.IsBusy);
    }

    private static Exception SelectAuthorityException(
        ControllerInputEvent input,
        string message) => input.Context == ControllerInputContext.PinnedSurface
            ? new BridgeStalePinnedInputAuthorityException(message)
            : new BridgeProtocolException(message);

    private sealed record SelectActionBinding(
        string SourceElementId,
        string OptionId,
        string ActionId,
        string Label,
        WidgetGlyph? Glyph,
        string? AccessibilityLabel,
        bool IsDisabled,
        bool IsBusy);

    /// Returns the exact action this button reaches in one admitted pinned
    /// projection, or null when the projection binds nothing to it. Stale or
    /// unavailable authority always throws.
    private static PinnedSurfaceInputBinding? ResolvePinnedSurfaceInputBinding(
        ViewSnapshot snapshot,
        ControllerInputEvent input,
        string authority)
    {
        ViewNode root;
        string inputScopeId;
        if (string.Equals(input.PinnedLayoutId,
                PinnedSurfaceContract.FullWidgetLayoutId, StringComparison.Ordinal))
        {
            root = snapshot.Root;
            inputScopeId = snapshot.ActiveInputScopeId;
        }
        else
        {
            var selectedLayout = snapshot.PinnedLayouts.SingleOrDefault(layout =>
                string.Equals(layout.Id, input.PinnedLayoutId, StringComparison.Ordinal));
            if (selectedLayout?.Root is null ||
                string.IsNullOrWhiteSpace(selectedLayout.ActiveInputScopeId))
                throw new BridgeStalePinnedInputAuthorityException(
                    $"Pinned-surface input targets an unavailable {authority} layout projection.");
            root = selectedLayout.Root;
            inputScopeId = selectedLayout.ActiveInputScopeId;
        }
        if (!string.Equals(input.ActiveInputScopeId, inputScopeId, StringComparison.Ordinal))
            throw new BridgeStalePinnedInputAuthorityException(
                $"Pinned-surface input targets a stale {authority} input scope.");
        var scopeRoot = FindInputScope(root, inputScopeId, isRoot: true);
        var focusedNode = scopeRoot is null ? null :
            FindNodeInScope(scopeRoot, input.FocusedElementId!, isScopeRoot: true);
        if (focusedNode is null)
            throw new BridgeStalePinnedInputAuthorityException(
                $"Pinned-surface input targets a missing {authority} focused element.");

        if (input.Button == ControllerButton.A)
        {
            if (!ControllerShortcutResolutionContract.OwnerAvailable(focusedNode))
                throw new BridgeStalePinnedInputAuthorityException(
                    $"Pinned-surface input targets an unavailable {authority} focused element.");
            if (input.Phase == ControllerEventPhase.Pressed &&
                focusedNode.Kind is ViewNodeKind.Button or ViewNodeKind.Slider or
                    ViewNodeKind.ActionSurface &&
                !string.IsNullOrWhiteSpace(focusedNode.ActionId))
                return new(focusedNode.ActionId, focusedNode.Id, focusedNode.Kind);
            return null;
        }
        if (focusedNode.Kind == ViewNodeKind.Slider &&
            input.Button is ControllerButton.DPadLeft or ControllerButton.DPadRight &&
            !string.IsNullOrWhiteSpace(focusedNode.ValueChangedActionId))
        {
            if (!ControllerShortcutResolutionContract.OwnerAvailable(focusedNode))
                throw new BridgeStalePinnedInputAuthorityException(
                    $"Pinned-surface input targets an unavailable {authority} focused element.");
            return new(
                focusedNode.ValueChangedActionId, focusedNode.Id, focusedNode.Kind,
                focusedNode.Minimum, focusedNode.Maximum, focusedNode.Step);
        }

        var shortcutResolution = ControllerShortcutResolver.Resolve(
            scopeRoot!, focusedNode.Id, input.Button, input.Phase);
        if (shortcutResolution.Status == ControllerShortcutResolutionStatus.FocusNotFound)
            throw new BridgeStalePinnedInputAuthorityException(
                $"Pinned-surface input lost its {authority} focused-element path.");
        if (shortcutResolution.Status == ControllerShortcutResolutionStatus.OwnerUnavailable)
            throw new BridgeStalePinnedInputAuthorityException(
                $"Pinned-surface input targets an unavailable {authority} shortcut owner.");
        return shortcutResolution.Status == ControllerShortcutResolutionStatus.Resolved
            ? new(
                shortcutResolution.Shortcut!.ActionId,
                shortcutResolution.Owner!.Id,
                shortcutResolution.Owner.Kind)
            : null;
    }

    private sealed record PinnedSurfaceInputBinding(
        string ActionId,
        string SourceElementId,
        ViewNodeKind SourceKind,
        double? Minimum = null,
        double? Maximum = null,
        double? Step = null);

    private static ViewNode? FindInputScope(ViewNode node, string scopeId, bool isRoot)
    {
        if ((isRoot || node.InputScopeId is not null) &&
            string.Equals(node.InputScopeId ?? node.Id, scopeId, StringComparison.Ordinal))
            return node;
        foreach (var child in node.Children)
        {
            var match = FindInputScope(child, scopeId, isRoot: false);
            if (match is not null) return match;
        }
        return null;
    }

    private static ViewNode? FindNodeInScope(ViewNode node, string id, bool isScopeRoot)
    {
        if (!isScopeRoot && node.InputScopeId is not null) return null;
        if (string.Equals(node.Id, id, StringComparison.Ordinal)) return node;
        foreach (var child in node.Children)
        {
            var match = FindNodeInScope(child, id, isScopeRoot: false);
            if (match is not null) return match;
        }
        return null;
    }

    private sealed class ClientRegistration(
        ConfiguredWidget configured,
        IBridgeWidgetClient client,
        long generation,
        Action<Exception> recordFailure)
    {
        private const int MaximumPinnedInputOriginSnapshots = 16;
        private ConfiguredWidget _configured = configured;
        internal ConfiguredWidget Configured
        {
            get => Volatile.Read(ref _configured);
            set => Volatile.Write(ref _configured, value);
        }

        internal IBridgeWidgetClient Client { get; } = client;
        internal long Generation { get; } = generation;
        internal BridgeClientNotificationLane NotificationLane { get; } = new(recordFailure);
        internal SemaphoreSlim OperationGate { get; } = new(1, 1);
        private int _hostLifecycle = (int)WidgetLifecycleState.Background;
        internal WidgetLifecycleState HostLifecycle
        {
            get => (WidgetLifecycleState)Volatile.Read(ref _hostLifecycle);
            set => Volatile.Write(ref _hostLifecycle, (int)value);
        }

        internal ViewSnapshot? CachedSnapshot { get; private set; }
        private int _cachedSnapshotWorkerStart;
        private readonly Queue<ViewSnapshot> _pinnedInputOriginSnapshots = new();
        private EmbeddedMediaCommandAuthority? _embeddedMediaCommandAuthority;
        private long _lastDashboardInputSequence;
        private readonly object _residencyGate = new();
        private CancellationTokenSource? _idleUnloadCancellation;
        private readonly HashSet<Task> _idleUnloadTasks = [];
        private long _idleUnloadGeneration;
        private bool _terminal;
        private WorkerFailureDiagnostic? _lastFailure;
        private int _activePublications;
        private readonly TaskCompletionSource _publicationsDrained = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _retirementCompletion = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _resourceRetired = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        internal WorkerFailureDiagnostic? LastFailure => Volatile.Read(ref _lastFailure);
        internal bool IsRetiring { get; private set; }
        internal bool RetirementStarted { get; set; }
        internal bool RestartReserved { get; set; }
        internal Exception? RetirementFailure { get; set; }
        internal Task RetirementCompletion => _retirementCompletion.Task;
        internal Task ResourceRetired => _resourceRetired.Task;
        internal Task PublicationsDrained => _publicationsDrained.Task;
        internal int ActivePublications => _activePublications;
        internal bool MayPublishInvalidation =>
            HostLifecycle != WidgetLifecycleState.Background ||
            WidgetResidencyPolicies.Resolve(Configured.ResidencyPolicy).Mode ==
                WidgetResidencyMode.KeepAlive;

        internal int DemandCurrentPresentationBase(long baseSequence)
        {
            var cached = CachedSnapshot;
            var workerStart = _cachedSnapshotWorkerStart;
            if (cached?.Sequence != baseSequence ||
                workerStart <= 0 ||
                !Client.IsRunning ||
                Client.Starts != workerStart)
                throw new BridgeStalePresentationBaseException();
            return workerStart;
        }

        internal void CommitCachedSnapshot(ViewSnapshot snapshot)
        {
            ArgumentNullException.ThrowIfNull(snapshot);
            var workerStart = Client.Starts;
            if (workerStart <= 0 || !Client.IsRunning)
                throw new BridgeProtocolException(
                    $"Widget '{Configured.Id}' lost its worker before snapshot publication.");
            if (_cachedSnapshotWorkerStart != 0 && _cachedSnapshotWorkerStart != workerStart)
                _pinnedInputOriginSnapshots.Clear();
            if (CachedSnapshot is { } prior && prior.Sequence != snapshot.Sequence)
            {
                _pinnedInputOriginSnapshots.Enqueue(prior);
                while (_pinnedInputOriginSnapshots.Count > MaximumPinnedInputOriginSnapshots)
                    _pinnedInputOriginSnapshots.Dequeue();
            }
            var media = snapshot.EmbeddedMediaSession;
            var pending = media?.PendingCommand;
            if (pending is null || media is null)
            {
                _embeddedMediaCommandAuthority = null;
            }
            else if (_embeddedMediaCommandAuthority is not { } current ||
                !string.Equals(current.InstanceId, snapshot.WidgetInstanceId,
                    StringComparison.Ordinal) ||
                !string.Equals(current.SessionId, media.Id, StringComparison.Ordinal) ||
                current.CommandSequence != pending.Sequence ||
                !string.Equals(current.MediaKey, pending.MediaKey,
                    StringComparison.Ordinal) ||
                current.Command != pending)
            {
                _embeddedMediaCommandAuthority = new(
                    snapshot.Sequence,
                    snapshot.WidgetInstanceId,
                    media.Id,
                    pending.Sequence,
                    pending.MediaKey,
                    pending with { },
                    EmbeddedMediaResourceAuthority.Capture(media));
            }
            CachedSnapshot = snapshot;
            _cachedSnapshotWorkerStart = workerStart;
        }

        internal ViewSnapshot? FindPinnedInputOriginSnapshot(long sequence)
        {
            if (CachedSnapshot?.Sequence == sequence) return CachedSnapshot;
            return _pinnedInputOriginSnapshots.FirstOrDefault(candidate =>
                candidate.Sequence == sequence);
        }

        internal bool AdmitsEmbeddedMediaPlaybackEvent(
            long requestSequence,
            EmbeddedMediaPlaybackEvent playbackEvent,
            ViewSnapshot snapshot)
        {
            if (playbackEvent.CommandSequence == 0)
                return requestSequence == snapshot.Sequence;
            var media = snapshot.EmbeddedMediaSession;
            var pending = media?.PendingCommand;
            return _embeddedMediaCommandAuthority is { } authority &&
                !authority.TerminalPublished &&
                (requestSequence == authority.OriginSnapshotSequence ||
                 requestSequence == snapshot.Sequence) &&
                string.Equals(authority.InstanceId, snapshot.WidgetInstanceId,
                    StringComparison.Ordinal) &&
                string.Equals(authority.SessionId, media?.Id, StringComparison.Ordinal) &&
                media is not null && authority.ResourceAuthority.Matches(media) &&
                authority.CommandSequence == playbackEvent.CommandSequence &&
                string.Equals(authority.MediaKey, playbackEvent.MediaKey,
                    StringComparison.Ordinal) &&
                authority.Command == pending &&
                pending?.Sequence == playbackEvent.CommandSequence &&
                string.Equals(pending.MediaKey, playbackEvent.MediaKey,
                    StringComparison.Ordinal);
        }

        internal void RecordEmbeddedMediaPlaybackTerminal(
            EmbeddedMediaPlaybackEvent playbackEvent)
        {
            if (_embeddedMediaCommandAuthority is not { } authority ||
                authority.TerminalPublished ||
                authority.CommandSequence != playbackEvent.CommandSequence ||
                !string.Equals(authority.MediaKey, playbackEvent.MediaKey,
                    StringComparison.Ordinal))
                throw new BridgeProtocolException(
                    "Embedded media playback terminal authority changed during publication.");
            authority.TerminalPublished = true;
        }

        private sealed class EmbeddedMediaCommandAuthority(
            long originSnapshotSequence,
            string instanceId,
            string sessionId,
            long commandSequence,
            string mediaKey,
            EmbeddedMediaPlaybackCommand command,
            EmbeddedMediaResourceAuthority resourceAuthority)
        {
            internal long OriginSnapshotSequence { get; } = originSnapshotSequence;
            internal string InstanceId { get; } = instanceId;
            internal string SessionId { get; } = sessionId;
            internal long CommandSequence { get; } = commandSequence;
            internal string MediaKey { get; } = mediaKey;
            internal EmbeddedMediaPlaybackCommand Command { get; } = command;
            internal EmbeddedMediaResourceAuthority ResourceAuthority { get; } =
                resourceAuthority;
            internal bool TerminalPublished { get; set; }
        }

        private sealed class EmbeddedMediaResourceAuthority
        {
            private string Id { get; init; } = string.Empty;
            private string EntryAsset { get; init; } = string.Empty;
            private EmbeddedMediaResource[] Resources { get; init; } = [];
            private string[] AllowedFrameOrigins { get; init; } = [];
            private string[] AllowedFrameDomainFamilies { get; init; } = [];

            internal static EmbeddedMediaResourceAuthority Capture(
                EmbeddedMediaSession media) => new()
            {
                Id = media.Id,
                EntryAsset = media.EntryAsset,
                Resources = media.Resources.Select(resource => resource with { }).ToArray(),
                AllowedFrameOrigins = media.AllowedFrameOrigins.ToArray(),
                AllowedFrameDomainFamilies = media.AllowedFrameDomainFamilies.ToArray(),
            };

            internal bool Matches(EmbeddedMediaSession media) =>
                string.Equals(Id, media.Id, StringComparison.Ordinal) &&
                string.Equals(EntryAsset, media.EntryAsset, StringComparison.Ordinal) &&
                Resources.SequenceEqual(media.Resources) &&
                AllowedFrameOrigins.SequenceEqual(
                    media.AllowedFrameOrigins, StringComparer.Ordinal) &&
                AllowedFrameDomainFamilies.SequenceEqual(
                    media.AllowedFrameDomainFamilies, StringComparer.Ordinal);
        }

        internal void AcceptDashboardInputSequence(long sequence)
        {
            if (sequence <= _lastDashboardInputSequence)
                throw new BridgeProtocolException(
                    "Dashboard controller input sequence was replayed.");
            _lastDashboardInputSequence = sequence;
        }

        internal void RecordFailure(WidgetFailure failure) => Volatile.Write(
            ref _lastFailure,
            new WorkerFailureDiagnostic(
                failure.DiagnosticCode ?? failure.Reason.ToString(),
                failure.CanRestart));

        internal void BeginRetirementLocked()
        {
            if (IsRetiring) return;
            IsRetiring = true;
            if (_activePublications == 0) _publicationsDrained.TrySetResult();
        }

        internal void CompleteRetirementLocked() =>
            _retirementCompletion.TrySetResult();

        internal void CompleteResourceRetirementLocked() =>
            _resourceRetired.TrySetResult();

        internal void AdmitPublicationLocked()
        {
            if (IsRetiring)
                throw new BridgeProtocolException(
                    $"Widget '{Configured.Id}' changed before publication.");
            _activePublications++;
        }

        internal void ReleasePublicationLocked()
        {
            if (_activePublications <= 0)
                throw new InvalidOperationException("Publication admission was released twice.");
            _activePublications--;
            if (IsRetiring && _activePublications == 0)
                _publicationsDrained.TrySetResult();
        }

        internal void ScheduleIdleUnload(
            CancellationToken bridgeCancellation,
            Func<long, CancellationToken, Task> start)
        {
            ArgumentNullException.ThrowIfNull(start);
            lock (_residencyGate)
            {
                if (_terminal) return;
                CancelIdleUnloadLocked();
                var cancellation = CancellationTokenSource.CreateLinkedTokenSource(
                    bridgeCancellation);
                var generation = ++_idleUnloadGeneration;
                Task task;
                try
                {
                    task = start(generation, cancellation.Token);
                }
                catch
                {
                    cancellation.Dispose();
                    throw;
                }
                _idleUnloadCancellation = cancellation;
                _idleUnloadTasks.Add(task);
                _ = ObserveIdleUnloadAsync(task, cancellation);
            }
        }

        internal bool IsIdleUnloadCurrent(long generation)
        {
            lock (_residencyGate)
                return generation == _idleUnloadGeneration &&
                    _idleUnloadCancellation is { IsCancellationRequested: false };
        }

        internal void CancelIdleUnload()
        {
            lock (_residencyGate) CancelIdleUnloadLocked();
        }

        internal async Task BeginTerminalAndDrainIdleUnloadAsync(TimeSpan deadline)
        {
            Task[] tasks;
            lock (_residencyGate)
            {
                _terminal = true;
                CancelIdleUnloadLocked();
                tasks = _idleUnloadTasks.ToArray();
            }
            if (tasks.Length == 0) return;

            var drain = Task.WhenAll(tasks);
            using var cancellation = new CancellationTokenSource(deadline);
            try
            {
                await drain.WaitAsync(cancellation.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                recordFailure(new TimeoutException(
                    $"Widget '{Configured.Id}' idle-unload drain exceeded its deadline."));
                _ = drain.ContinueWith(
                    completed =>
                    {
                        if (completed.Exception is { } failure) recordFailure(failure);
                    },
                    CancellationToken.None,
                    TaskContinuationOptions.ExecuteSynchronously,
                    TaskScheduler.Default);
            }
        }

        private async Task ObserveIdleUnloadAsync(
            Task task,
            CancellationTokenSource cancellation)
        {
            try
            {
                await task.ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                recordFailure(exception);
            }
            finally
            {
                lock (_residencyGate)
                {
                    _idleUnloadTasks.Remove(task);
                    if (ReferenceEquals(_idleUnloadCancellation, cancellation))
                        _idleUnloadCancellation = null;
                }
                cancellation.Dispose();
            }
        }

        private void CancelIdleUnloadLocked()
        {
            _idleUnloadGeneration++;
            if (_idleUnloadCancellation is null) return;
            _idleUnloadCancellation.Cancel();
            _idleUnloadCancellation = null;
        }
    }

    private sealed record WorkerFailureDiagnostic(string Code, bool CanRestart);
}
