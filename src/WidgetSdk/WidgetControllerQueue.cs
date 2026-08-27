namespace WidgetRail.WidgetSdk;

public record WidgetActionFailedEventArgs(
    WidgetActionEvent Action,
    Exception Exception);

/// <summary>Compatibility event payload for the former controller-only queue.</summary>
public sealed record WidgetControllerActionFailedEventArgs(
    WidgetActionEvent Action,
    Exception Exception) : WidgetActionFailedEventArgs(Action, Exception);

public abstract partial class Widget
{
    public const int ActionQueueCapacity = 16;
    public const int ControllerActionQueueCapacity = ActionQueueCapacity;

    private sealed class ActionQueueState(CancellationToken lifetime)
    {
        public CancellationToken Lifetime { get; } = lifetime;
        public LinkedList<QueuedAction> Pending { get; } = [];
        public SemaphoreSlim Available { get; } = new(0);
        public TaskCompletionSource Completion { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private sealed record QueuedAction(
        WidgetActionEvent Action,
        WidgetCapabilityGestureContext? GestureContext);

    private readonly object _actionQueueLock = new();
    private ActionQueueState? _actionQueue;

    /// <summary>
    /// Raised when an accepted action later fails. Admission acknowledgement is
    /// intentionally decoupled from action completion.
    /// </summary>
    public event EventHandler<WidgetActionFailedEventArgs>? ActionFailed;

    /// <summary>
    /// Raised when an accepted controller action later fails. Controller input
    /// acknowledgement is intentionally decoupled from action completion.
    /// </summary>
    public event EventHandler<WidgetControllerActionFailedEventArgs>? ControllerActionFailed;

    /// <summary>Observes bounded serial action-queue progress.</summary>
    protected virtual void OnActionDiagnostic(
        WidgetActionEvent action,
        string stage,
        string code)
    {
    }

    /// <summary>
    /// Appends to the active lifetime's bounded serial FIFO. A contiguous tail
    /// of absolute changes for the same slider is latest-wins coalesced; a
    /// button or different slider is an ordering boundary and is never crossed.
    /// </summary>
    internal WidgetOperationAdmission AdmitAction(
        WidgetActionEvent action,
        WidgetCapabilityGestureContext? gestureContext = null)
    {
        ArgumentNullException.ThrowIfNull(action);
        if (!IsActive)
        {
            ObserveActionDiagnostic(action, "admission", "rejected-inactive");
            return WidgetOperationAdmission.RejectedInactive;
        }
        var lifetime = ActiveCancellationToken;
        if (lifetime.IsCancellationRequested)
        {
            ObserveActionDiagnostic(action, "admission", "rejected-inactive");
            return WidgetOperationAdmission.RejectedInactive;
        }

        ActionQueueState queue;
        WidgetOperationAdmission admission;
        WidgetActionEvent? replacedAction = null;
        lock (_actionQueueLock)
        {
            if (!IsActive || lifetime.IsCancellationRequested)
                admission = WidgetOperationAdmission.RejectedInactive;
            else
            {
                if (_actionQueue is null || _actionQueue.Lifetime != lifetime)
                {
                    queue = new ActionQueueState(lifetime);
                    _actionQueue = queue;
                    _ = ConsumeActionsAsync(queue);
                }
                else queue = _actionQueue;

                if (action.RequestedValue is not null && queue.Pending.Last is { } tail &&
                    CanCoalesceSliderChange(tail.Value.Action, action))
                {
                    replacedAction = tail.Value.Action;
                    tail.Value = new(action, gestureContext);
                    admission = WidgetOperationAdmission.Replaced;
                }
                else if (queue.Pending.Count >= ActionQueueCapacity)
                    admission = WidgetOperationAdmission.RejectedCapacity;
                else
                {
                    queue.Pending.AddLast(new QueuedAction(action, gestureContext));
                    queue.Available.Release();
                    admission = WidgetOperationAdmission.Enqueued;
                }
            }
        }
        if (replacedAction is not null)
            ObserveActionDiagnostic(replacedAction, "terminal", "replaced");
        ObserveActionDiagnostic(action, "admission", AdmissionCode(admission));
        return admission;
    }

    private bool TryQueueControllerAction(
        WidgetActionEvent action,
        WidgetCapabilityGestureContext? gestureContext = null) =>
        IsAccepted(AdmitAction(action, gestureContext));

    private static bool IsAccepted(WidgetOperationAdmission admission) => admission is not (
        WidgetOperationAdmission.RejectedInactive or WidgetOperationAdmission.RejectedCapacity);

    private static bool CanCoalesceSliderChange(
        WidgetActionEvent previous,
        WidgetActionEvent current) =>
        previous.RequestedValue is not null && current.RequestedValue is not null &&
        string.Equals(previous.ActionId, current.ActionId, StringComparison.Ordinal) &&
        string.Equals(previous.SourceElementId, current.SourceElementId, StringComparison.Ordinal) &&
        string.Equals(previous.InputScopeId, current.InputScopeId, StringComparison.Ordinal);

    private async Task ConsumeActionsAsync(ActionQueueState queue)
    {
        try
        {
            while (true)
            {
                await queue.Available.WaitAsync(queue.Lifetime).ConfigureAwait(false);
                QueuedAction? queued;
                lock (_actionQueueLock)
                {
                    if (queue.Pending.First is not { } first) continue;
                    queued = first.Value;
                    queue.Pending.RemoveFirst();
                }

                using var invocation = WidgetCapabilityInvocationContext.Enter(
                    queued.GestureContext);
                ObserveActionDiagnostic(queued.Action, "dequeue", "started");
                try
                {
                    await OnActionAsync(queued.Action, queue.Lifetime).ConfigureAwait(false);
                    ObserveActionDiagnostic(queued.Action, "terminal", "succeeded");
                }
                catch (OperationCanceledException) when (queue.Lifetime.IsCancellationRequested)
                {
                    ObserveActionDiagnostic(queued.Action, "terminal", "canceled");
                    return;
                }
                catch (Exception exception)
                {
                    ObserveActionDiagnostic(queued.Action, "terminal", "failed");
                    ReportActionFailure(queued.Action, exception);
                }
            }
        }
        catch (OperationCanceledException) when (queue.Lifetime.IsCancellationRequested)
        {
        }
        finally
        {
            List<WidgetActionEvent> canceled = [];
            lock (_actionQueueLock)
            {
                canceled.AddRange(queue.Pending.Select(item => item.Action));
                queue.Pending.Clear();
                if (ReferenceEquals(_actionQueue, queue)) _actionQueue = null;
            }
            foreach (var action in canceled)
                ObserveActionDiagnostic(action, "terminal", "canceled");
            queue.Available.Dispose();
            queue.Completion.TrySetResult();
        }
    }

    private async Task DrainActionQueueAsync(
        CancellationToken lifetime,
        CancellationToken cancellationToken)
    {
        Task? completion = null;
        lock (_actionQueueLock)
        {
            if (_actionQueue?.Lifetime == lifetime) completion = _actionQueue.Completion.Task;
        }
        if (completion is not null)
            await completion.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    private void ReportActionFailure(WidgetActionEvent action, Exception exception)
    {
        var diagnosticAction = action with { CommittedText = null };
        InvokeFailureHandlers(ActionFailed, new WidgetActionFailedEventArgs(diagnosticAction, exception));
        InvokeFailureHandlers(
            ControllerActionFailed,
            new WidgetControllerActionFailedEventArgs(diagnosticAction, exception));
    }

    private static string AdmissionCode(WidgetOperationAdmission admission) => admission switch
    {
        WidgetOperationAdmission.Enqueued => "enqueued",
        WidgetOperationAdmission.Replaced => "replaced",
        WidgetOperationAdmission.RejectedInactive => "rejected-inactive",
        WidgetOperationAdmission.RejectedCapacity => "rejected-capacity",
        _ => "unknown",
    };

    private void ObserveActionDiagnostic(WidgetActionEvent action, string stage, string code)
    {
        // Diagnostics retain correlation metadata but never receive text-entry
        // contents. The widget action callback remains the sole consumer of a
        // committed value.
        try { OnActionDiagnostic(action with { CommittedText = null }, stage, code); }
        catch { }
    }

    private void InvokeFailureHandlers<T>(EventHandler<T>? handler, T args)
    {
        var handlers = handler?.GetInvocationList();
        if (handlers is null) return;
        foreach (var candidate in handlers)
        {
            try
            {
                ((EventHandler<T>)candidate)(this, args);
            }
            catch
            {
                // An observer cannot terminate the serial action consumer.
            }
        }
    }
}
