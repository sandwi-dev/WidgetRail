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
        if (!IsActive) return WidgetOperationAdmission.RejectedInactive;
        var lifetime = ActiveCancellationToken;
        if (lifetime.IsCancellationRequested) return WidgetOperationAdmission.RejectedInactive;

        ActionQueueState queue;
        lock (_actionQueueLock)
        {
            if (!IsActive || lifetime.IsCancellationRequested)
                return WidgetOperationAdmission.RejectedInactive;
            if (_actionQueue is null || _actionQueue.Lifetime != lifetime)
            {
                queue = new ActionQueueState(lifetime);
                _actionQueue = queue;
                _ = ConsumeActionsAsync(queue);
            }
            else
            {
                queue = _actionQueue;
            }

            if (action.RequestedValue is not null && queue.Pending.Last is { } tail &&
                CanCoalesceSliderChange(tail.Value.Action, action))
            {
                tail.Value = new(action, gestureContext);
                return WidgetOperationAdmission.Replaced;
            }
            if (queue.Pending.Count >= ActionQueueCapacity)
                return WidgetOperationAdmission.RejectedCapacity;
            queue.Pending.AddLast(new QueuedAction(action, gestureContext));
            queue.Available.Release();
            return WidgetOperationAdmission.Enqueued;
        }
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
                try
                {
                    await OnActionAsync(queued.Action, queue.Lifetime).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (queue.Lifetime.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception exception)
                {
                    ReportActionFailure(queued.Action, exception);
                }
            }
        }
        catch (OperationCanceledException) when (queue.Lifetime.IsCancellationRequested)
        {
        }
        finally
        {
            lock (_actionQueueLock)
            {
                queue.Pending.Clear();
                if (ReferenceEquals(_actionQueue, queue)) _actionQueue = null;
            }
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
        InvokeFailureHandlers(ActionFailed, new WidgetActionFailedEventArgs(action, exception));
        InvokeFailureHandlers(
            ControllerActionFailed,
            new WidgetControllerActionFailedEventArgs(action, exception));
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
