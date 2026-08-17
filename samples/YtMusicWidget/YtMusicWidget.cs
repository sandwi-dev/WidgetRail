using System.Diagnostics;
using WidgetRail.WidgetSdk;

namespace WidgetRail.Samples.YtMusicWidget;

public enum YtMusicWidgetConnectionState
{
    Disconnected,
    Connecting,
    Pairing,
    Connected,
    Error,
}

public class YtMusicWidget : Widget
{
    private const string ConnectionOperation = "ytmusic.connection";
    private const string ProgressOperation = "ytmusic.progress";
    private const string PollOperation = "ytmusic.poll";
    private const string ExplicitRefreshOperation = "ytmusic.explicit-refresh";
    private const string TransportRefreshOperation = "ytmusic.transport-refresh";
    private IYtMusicClient? _client;
    // Auto-connect starts from activation rather than action admission, so it
    // shares this narrow connection-workflow gate with explicit connect/pair.
    private readonly SemaphoreSlim _connectionGate = new(1, 1);
    private readonly SemaphoreSlim _clientGate = new(1, 1);
    private readonly object _stateLock = new();
    private readonly TimeProvider _timeProvider;
    private readonly YtMusicUpdatePolicy _updatePolicy;
    private YtMusicPresentationState _presentation;

    public YtMusicWidget(
        IYtMusicClient? client = null,
        YtMusicUpdatePolicy? updatePolicy = null,
        TimeProvider? timeProvider = null)
    {
        _client = client;
        _updatePolicy = updatePolicy ?? new YtMusicUpdatePolicy();
        _updatePolicy.Validate();
        _timeProvider = timeProvider ?? TimeProvider.System;
        _presentation = YtMusicPresentationState.Initial(_timeProvider.GetTimestamp());
    }

    public YtMusicWidgetConnectionState ConnectionState
    {
        get { lock (_stateLock) return _presentation.ConnectionState; }
    }

    private IYtMusicClient Client
    {
        get
        {
            var current = Volatile.Read(ref _client);
            if (current is not null) return current;
            var created = new YtmDesktopApiClient(HostServices);
            var winner = Interlocked.CompareExchange(ref _client, created, null);
            if (winner is null) return created;
            created.Dispose();
            return winner;
        }
    }

    public override WidgetView Render()
    {
        YtMusicPresentationState presentation;
        lock (_stateLock)
            presentation = _presentation;
        var snapshot = YtMusicCompanionPolicy.ProjectForPresentation(
            presentation, _timeProvider.GetTimestamp(), _timeProvider);
        return YtMusicPresentation.Compose(presentation, snapshot);
    }

    protected override ValueTask OnActivatedAsync(CancellationToken activeLifetime)
    {
        lock (_stateLock)
            Operations.RunLatest(
                ConnectionOperation,
                AutoConnectAsync,
                WidgetOperationLifetime.Active);
        Operations.RunSingleFlight(
            ProgressOperation,
            RunProgressLoopAsync,
            WidgetOperationLifetime.Active);
        Operations.RunSingleFlight(
            PollOperation,
            RunPollLoopAsync,
            WidgetOperationLifetime.Active);
        return ValueTask.CompletedTask;
    }

    protected override ValueTask OnDeactivatedAsync(CancellationToken transitionToken)
    {
        lock (_stateLock)
            _presentation = YtMusicConnectionPolicy.Deactivate(_presentation);
        return ValueTask.CompletedTask;
    }

    protected override ValueTask OnLifecycleStateChangedAsync(
        WidgetLifecycleState previous,
        WidgetLifecycleState current,
        CancellationToken stateLifetime)
    {
        Trace.WriteLine($"YT Music widget lifecycle: {previous} -> {current}");
        return ValueTask.CompletedTask;
    }

    protected override ValueTask OnDestroyingAsync(CancellationToken shutdownToken)
    {
        var client = Interlocked.Exchange(ref _client, null);
        (client as IDisposable)?.Dispose();
        return ValueTask.CompletedTask;
    }

    private async ValueTask AutoConnectAsync(WidgetOperationContext context)
    {
        var activeLifetime = context.CancellationToken;
        try
        {
            YtMusicPresentationState presentation;
            lock (_stateLock) presentation = _presentation;
            if (context.IsCurrent &&
                YtMusicConnectionPolicy.ShouldAutoConnect(presentation))
                await ConnectAsync(activeLifetime, context).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (activeLifetime.IsCancellationRequested)
        {
            // Deactivation is the normal cancellation path.
        }
        catch (Exception exception)
        {
            TrySetError(context, exception);
        }
    }

    private ValueTask RunProgressLoopAsync(WidgetOperationContext context) => new(
        WidgetTicker.RunWhileActiveAsync(
            _updatePolicy.ProgressInterval,
            _ =>
            {
                YtMusicPresentationState presentation;
                lock (_stateLock) presentation = _presentation;
                if (context.IsCurrent &&
                    presentation.Snapshot.IsPlaying &&
                    presentation.ConnectionState == YtMusicWidgetConnectionState.Connected)
                    Invalidate();
                return ValueTask.CompletedTask;
            },
            context.CancellationToken));

    private ValueTask RunPollLoopAsync(WidgetOperationContext context) => new(
        WidgetTicker.RunWhileActiveAsync(
            _updatePolicy.PollInterval,
            token => PollConnectedStateAsync(context, token),
            context.CancellationToken));

    private async ValueTask PollConnectedStateAsync(
        WidgetOperationContext context,
        CancellationToken activeLifetime)
    {
        if (!context.IsCurrent || ConnectionState != YtMusicWidgetConnectionState.Connected) return;
        if (!await _clientGate.WaitAsync(0, activeLifetime).ConfigureAwait(false)) return;
        try
        {
            await FetchConnectedSnapshotAsync(
                force: false,
                activeLifetime,
                context).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (activeLifetime.IsCancellationRequested)
        {
            // Deactivation is the normal cancellation path.
        }
        catch (YtMusicAuthorizationRequiredException)
        {
            TrySetAuthorizationRequired(context);
        }
        catch (Exception)
        {
            TrySetConnectedStatus(context, "Playback update delayed · retrying…");
        }
        finally
        {
            _clientGate.Release();
        }
    }

    public override async ValueTask OnActionAsync(WidgetActionEvent action, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(action);
        cancellationToken.ThrowIfCancellationRequested();
        if (LifecycleState is WidgetLifecycleState.Background or WidgetLifecycleState.Destroying)
            return;
        var route = YtMusicActionPolicy.Resolve(action.ActionId);
        switch (route.Kind)
        {
            case YtMusicActionKind.Connect:
                await RunConnectionActionAsync(ConnectAsync, cancellationToken).ConfigureAwait(false);
                break;
            case YtMusicActionKind.Pair:
                await RunConnectionActionAsync(PairAsync, cancellationToken).ConfigureAwait(false);
                break;
            case YtMusicActionKind.Refresh:
                await RunExplicitRefreshAsync(route.Status, cancellationToken)
                    .ConfigureAwait(false);
                break;
            case YtMusicActionKind.Command when route.Command is { } command:
                await RunCommandAsync(
                    command, route.Status, cancellationToken).ConfigureAwait(false);
                break;
        }
    }

    private async Task RunExplicitRefreshAsync(
        string message,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        WidgetOperationHandle operation;
        lock (_stateLock)
            operation = Operations.RunLatest(
                ExplicitRefreshOperation,
                context => new ValueTask(RefreshAsync(
                    message, context.CancellationToken, context)),
                WidgetOperationLifetime.Active);
        if (operation.IsAccepted)
        {
            var result = await operation.Completion.WaitAsync(cancellationToken)
                .ConfigureAwait(false);
            if (result.Status == WidgetOperationStatus.Failed &&
                result.Exception is { } failure)
                throw failure;
            return;
        }
        if (LifecycleState == WidgetLifecycleState.Created)
            await RefreshAsync(message, cancellationToken, operationContext: null)
                .ConfigureAwait(false);
    }

    private async Task RunConnectionActionAsync(
        Func<CancellationToken, WidgetOperationContext?, Task> action,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        WidgetOperationHandle operation;
        lock (_stateLock)
            operation = Operations.RunLatest(
                ConnectionOperation,
                context => new ValueTask(action(context.CancellationToken, context)),
                WidgetOperationLifetime.Active);
        if (operation.IsAccepted)
        {
            var result = await operation.Completion.WaitAsync(cancellationToken).ConfigureAwait(false);
            if (result.Status == WidgetOperationStatus.Failed && result.Exception is { } failure)
                throw failure;
            return;
        }

        // Direct invocation remains available for deterministic source-level
        // tests before host lifecycle initialization. Installed widgets always
        // use the runtime-owned operation lane above.
        if (LifecycleState != WidgetLifecycleState.Created) return;
        await _connectionGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await action(cancellationToken, null).ConfigureAwait(false);
        }
        finally
        {
            _connectionGate.Release();
        }
    }

    private async Task ConnectAsync(
        CancellationToken cancellationToken,
        WidgetOperationContext? operationContext)
    {
        if (!TrySetState(
                operationContext,
                YtMusicWidgetConnectionState.Connecting,
                "Connecting to YTMDesktop2…",
                pairingCode: null))
            return;
        try
        {
            await _clientGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                var status = await Client.GetStatusAsync(cancellationToken).ConfigureAwait(false);
                if (cancellationToken.IsCancellationRequested ||
                    operationContext is { IsCurrent: false })
                    return;
                if (status.AuthRequired && !status.HasCredential)
                {
                    TrySetState(
                        operationContext,
                        YtMusicWidgetConnectionState.Disconnected,
                        "Connected · pairing required",
                        pairingCode: null);
                    return;
                }
                await FetchConnectedSnapshotAsync(
                    force: true,
                    cancellationToken,
                    operationContext,
                    establishConnection: true).ConfigureAwait(false);
            }
            finally
            {
                _clientGate.Release();
            }
        }
        catch (OperationCanceledException)
        {
            TrySetState(
                operationContext,
                YtMusicWidgetConnectionState.Disconnected,
                "Connection canceled",
                pairingCode: null);
            throw;
        }
        catch (YtMusicAuthorizationRequiredException)
        {
            if (operationContext is null)
                await SetAuthorizationRequiredAsync().ConfigureAwait(false);
            else
                TrySetAuthorizationRequired(operationContext);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            TrySetError(operationContext, exception);
        }
    }

    private async Task PairAsync(
        CancellationToken cancellationToken,
        WidgetOperationContext? operationContext)
    {
        if (!TrySetState(
                operationContext,
                YtMusicWidgetConnectionState.Pairing,
                "Requesting a pairing code…",
                pairingCode: null))
            return;
        try
        {
            await _clientGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                var pairing = await Client.RequestPairingCodeAsync(cancellationToken).ConfigureAwait(false);
                if (cancellationToken.IsCancellationRequested ||
                    operationContext is { IsCurrent: false })
                    return;
                if (!TrySetState(
                        operationContext,
                        YtMusicWidgetConnectionState.Pairing,
                        "Approve this code in YTMDesktop2",
                        pairing.Code))
                    return;
                await Client.CompletePairingAsync(pairing.Code, cancellationToken).ConfigureAwait(false);
                if (cancellationToken.IsCancellationRequested ||
                    operationContext is { IsCurrent: false })
                    return;
                await FetchConnectedSnapshotAsync(
                    force: true,
                    cancellationToken,
                    operationContext,
                    establishConnection: true).ConfigureAwait(false);
            }
            finally
            {
                _clientGate.Release();
            }
        }
        catch (OperationCanceledException)
        {
            TrySetState(
                operationContext,
                YtMusicWidgetConnectionState.Disconnected,
                "Pairing canceled",
                pairingCode: null);
            throw;
        }
        catch (YtMusicAuthorizationRequiredException)
        {
            if (operationContext is null)
                await SetAuthorizationRequiredAsync().ConfigureAwait(false);
            else
                TrySetAuthorizationRequired(operationContext);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            TrySetError(operationContext, exception);
        }
    }

    private async Task RefreshAsync(
        string message,
        CancellationToken cancellationToken,
        WidgetOperationContext? operationContext)
    {
        if (ConnectionState != YtMusicWidgetConnectionState.Connected) return;
        if (operationContext is null)
            SetConnectedStatus(message);
        else if (!TrySetConnectedStatus(operationContext, message))
            return;
        try
        {
            await _clientGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await FetchConnectedSnapshotAsync(
                    force: true, cancellationToken, operationContext).ConfigureAwait(false);
            }
            finally
            {
                _clientGate.Release();
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            if (exception is YtMusicAuthorizationRequiredException)
            {
                if (operationContext is null)
                    await SetAuthorizationRequiredAsync().ConfigureAwait(false);
                else
                    TrySetAuthorizationRequired(operationContext);
            }
            else
                TrySetTransientRefreshError(operationContext, exception);
        }
    }

    private async Task RunCommandAsync(
        YtMusicCommand command,
        string message,
        CancellationToken cancellationToken)
    {
        var isTransport = command is
            YtMusicCommand.TogglePlayback or YtMusicCommand.Previous or YtMusicCommand.Next;
        YtMusicPendingOptimisticState optimistic;
        lock (_stateLock)
        {
            if (!_presentation.HasCurrentPlayback) return;
            var started = YtMusicCompanionPolicy.BeginOptimistic(
                _presentation,
                command,
                message,
                _timeProvider.GetTimestamp(),
                _updatePolicy,
                _timeProvider);
            optimistic = started.Pending;
            _presentation = started.Presentation;
        }
        Invalidate();
        try
        {
            await Client.SendCommandAsync(
                command,
                cancellationToken,
                optimistic.ToggleState).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            if (isTransport)
            {
                ScheduleTransportRefresh(command);
                return;
            }

            await _clientGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await FetchConnectedSnapshotAsync(force: false, cancellationToken).ConfigureAwait(false);
            }
            finally { _clientGate.Release(); }
        }
        catch (OperationCanceledException)
        {
            RollBackOptimisticState(optimistic, "Command canceled");
            throw;
        }
        catch (YtMusicAuthorizationRequiredException)
        {
            RollBackOptimisticState(optimistic, "Authorization expired");
            await SetAuthorizationRequiredAsync().ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            RollBackOptimisticState(
                optimistic, YtMusicConnectionPolicy.SafeStatus(exception));
        }
    }

    private void ScheduleTransportRefresh(YtMusicCommand command)
    {
        // Once the companion accepts a playback command, reconciliation belongs
        // to the active widget lifecycle rather than the completed action
        // request. The public operation lane cancels stale work, bounds it to
        // one running plus one replacement, and drains it before deactivation.
        WidgetOperationHandle operation;
        lock (_stateLock)
        {
            if (_presentation.ConnectionState != YtMusicWidgetConnectionState.Connected) return;
            // Admission shares the state lock with attempt-local commits. A
            // replacement therefore cannot become current between an older
            // attempt's final currency check and its state mutation.
            operation = Operations.RunLatest(
                TransportRefreshOperation,
                context => RunTransportRefreshBurstAsync(command, context),
                WidgetOperationLifetime.Active);
        }
        if (!operation.IsAccepted)
            SetConnectedStatus("Track update will refresh when the widget is active");
    }

    private async ValueTask RunTransportRefreshBurstAsync(
        YtMusicCommand command,
        WidgetOperationContext context)
    {
        var cancellationToken = context.CancellationToken;
        try
        {
            for (var attempt = 0; attempt < _updatePolicy.TransportRefreshAttempts; attempt++)
            {
                var delay = _updatePolicy.TransportRefreshInitialDelay +
                            (_updatePolicy.TransportRefreshDelayStep * attempt);
                await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
                if (!context.IsCurrent) return;

                try
                {
                    await _clientGate.WaitAsync(cancellationToken).ConfigureAwait(false);
                    try
                    {
                        var applied = await FetchConnectedSnapshotAsync(
                            force: false,
                            cancellationToken,
                            context).ConfigureAwait(false);
                        if (!applied) return;
                    }
                    finally
                    {
                        _clientGate.Release();
                    }
                }
                catch (YtMusicAuthorizationRequiredException)
                {
                    TrySetAuthorizationRequired(context);
                    return;
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    TrySetConnectedStatus(context, "Track update delayed · retrying…");
                    continue;
                }

                if (TransportTransitionResolved(command)) return;
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // A newer transport command or lifecycle exit superseded this burst.
        }
    }

    private bool TransportTransitionResolved(YtMusicCommand command)
    {
        lock (_stateLock)
        {
            // FetchConnectedSnapshotAsync removes the pending state only when
            // the unmerged companion snapshot confirms it or its bounded
            // confirmation window expires. Inspecting the rendered snapshot
            // here would mistake our optimistic value for server confirmation.
            return YtMusicCompanionPolicy.TransportTransitionResolved(
                _presentation, command);
        }
    }

    private async Task<bool> FetchConnectedSnapshotAsync(
        bool force,
        CancellationToken cancellationToken,
        WidgetOperationContext? operationContext = null,
        bool establishConnection = false)
    {
        if (cancellationToken.IsCancellationRequested ||
            operationContext is { IsCurrent: false })
            return false;
        var snapshot = await Client.GetSnapshotAsync(cancellationToken).ConfigureAwait(false);
        if (cancellationToken.IsCancellationRequested ||
            operationContext is { IsCurrent: false })
            return false;
        lock (_stateLock)
        {
            if (cancellationToken.IsCancellationRequested ||
                operationContext is { IsCurrent: false })
                return false;
            if (!establishConnection &&
                _presentation.ConnectionState != YtMusicWidgetConnectionState.Connected)
                return false;
            var now = _timeProvider.GetTimestamp();
            _presentation = YtMusicCompanionPolicy.ReconcileAuthoritative(
                _presentation, snapshot, now, force, _timeProvider);
        }
        Invalidate();
        return true;
    }

    private void RollBackOptimisticState(
        YtMusicPendingOptimisticState optimistic,
        string message)
    {
        var changed = false;
        lock (_stateLock)
        {
            changed = YtMusicCompanionPolicy.TryRollback(
                _presentation,
                optimistic,
                message,
                _timeProvider.GetTimestamp(),
                _timeProvider,
                out var next);
            if (changed) _presentation = next;
        }
        if (changed) Invalidate();
    }

    private void SetConnectedStatus(string message)
    {
        var changed = false;
        lock (_stateLock)
        {
            if (_presentation.ConnectionState != YtMusicWidgetConnectionState.Connected) return;
            changed = !string.Equals(_presentation.Status, message, StringComparison.Ordinal);
            _presentation = _presentation with { Status = message };
        }
        if (changed) Invalidate();
    }

    private bool TrySetTransientRefreshError(
        WidgetOperationContext? context,
        Exception exception)
    {
        lock (_stateLock)
        {
            if (context is { IsCurrent: false } ||
                _presentation.ConnectionState != YtMusicWidgetConnectionState.Connected)
                return false;
            _presentation = _presentation with
            {
                Status = YtMusicConnectionPolicy.SafeStatus(exception) +
                    (_presentation.HasCurrentPlayback
                        ? " · showing last known track"
                        : string.Empty),
            };
        }
        Invalidate();
        return true;
    }

    private bool TrySetConnectedStatus(WidgetOperationContext context, string message)
    {
        var changed = false;
        lock (_stateLock)
        {
            if (!context.IsCurrent ||
                _presentation.ConnectionState != YtMusicWidgetConnectionState.Connected)
                return false;
            changed = !string.Equals(_presentation.Status, message, StringComparison.Ordinal);
            _presentation = _presentation with { Status = message };
        }
        if (changed) Invalidate();
        return true;
    }

    private bool TrySetAuthorizationRequired(WidgetOperationContext context)
    {
        lock (_stateLock)
        {
            if (!context.IsCurrent) return false;
            _presentation = YtMusicConnectionPolicy.AuthorizationRequired(
                _presentation);
        }
        // This current delegate returns immediately, so canceling its own lane
        // is unnecessary. Avoiding a second lane-wide cancellation also keeps
        // a replacement admitted after this atomic commit from being revoked.
        Invalidate();
        return true;
    }

    private bool TrySetState(
        WidgetOperationContext? context,
        YtMusicWidgetConnectionState state,
        string status,
        string? pairingCode)
    {
        lock (_stateLock)
        {
            if (context is { IsCurrent: false }) return false;
            _presentation = _presentation with
            {
                ConnectionState = state,
                Status = status,
                PairingCode = pairingCode,
            };
        }
        Invalidate();
        return true;
    }

    private void SetState(YtMusicWidgetConnectionState state, string status, string? pairingCode) =>
        TrySetState(context: null, state, status, pairingCode);

    private void SetError(Exception exception)
    {
        SetState(
            YtMusicWidgetConnectionState.Error,
            YtMusicConnectionPolicy.SafeStatus(exception),
            pairingCode: null);
    }

    private bool TrySetError(WidgetOperationContext? context, Exception exception) =>
        TrySetState(
            context,
            YtMusicWidgetConnectionState.Error,
            YtMusicConnectionPolicy.SafeStatus(exception),
            pairingCode: null);

    private Task SetAuthorizationRequiredAsync()
    {
        Operations.Cancel(TransportRefreshOperation);
        lock (_stateLock)
            _presentation = YtMusicConnectionPolicy.AuthorizationRequired(
                _presentation);
        Invalidate();
        return Task.CompletedTask;
    }

    public static string FormatTime(double seconds) =>
        YtMusicPresentation.FormatTime(seconds);
}
