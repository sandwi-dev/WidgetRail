namespace GameBarAlternative.WidgetSdk;

public sealed record WidgetControllerActionFailedEventArgs(
    WidgetActionEvent Action,
    Exception Exception);

public abstract partial class Widget
{
    public const int ControllerActionQueueCapacity = 16;

    private sealed class ControllerQueueState(CancellationToken lifetime)
    {
        public CancellationToken Lifetime { get; } = lifetime;
        public LinkedList<QueuedControllerAction> Pending { get; } = [];
        public SemaphoreSlim Available { get; } = new(0);
    }

    private sealed record QueuedControllerAction(
        WidgetActionEvent Action,
        WidgetCapabilityGestureContext? GestureContext);

    private readonly object _controllerQueueLock = new();
    private readonly SemaphoreSlim _controllerActionGate = new(1, 1);
    private ControllerQueueState? _controllerQueue;

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
    private bool TryQueueControllerAction(
        WidgetActionEvent action,
        WidgetCapabilityGestureContext? gestureContext = null)
    {
        if (!IsActive) return false;
        var lifetime = ActiveCancellationToken;
        if (lifetime.IsCancellationRequested) return false;

        ControllerQueueState queue;
        lock (_controllerQueueLock)
        {
            if (!IsActive || lifetime.IsCancellationRequested) return false;
            if (_controllerQueue is null || _controllerQueue.Lifetime != lifetime)
            {
                queue = new ControllerQueueState(lifetime);
                _controllerQueue = queue;
                _ = ConsumeControllerActionsAsync(queue);
            }
            else
            {
                queue = _controllerQueue;
            }

            if (action.RequestedValue is not null && queue.Pending.Last is { } tail &&
                CanCoalesceSliderChange(tail.Value.Action, action))
            {
                tail.Value = new(action, gestureContext);
                return true;
            }
            if (queue.Pending.Count >= ControllerActionQueueCapacity) return false;
            queue.Pending.AddLast(new QueuedControllerAction(action, gestureContext));
            queue.Available.Release();
            return true;
        }
    }

    private static bool CanCoalesceSliderChange(
        WidgetActionEvent previous,
        WidgetActionEvent current) =>
        previous.RequestedValue is not null && current.RequestedValue is not null &&
        string.Equals(previous.ActionId, current.ActionId, StringComparison.Ordinal) &&
        string.Equals(previous.SourceElementId, current.SourceElementId, StringComparison.Ordinal) &&
        string.Equals(previous.InputScopeId, current.InputScopeId, StringComparison.Ordinal);

    private async Task ConsumeControllerActionsAsync(ControllerQueueState queue)
    {
        try
        {
            while (true)
            {
                await queue.Available.WaitAsync(queue.Lifetime).ConfigureAwait(false);
                QueuedControllerAction? queued;
                lock (_controllerQueueLock)
                {
                    if (queue.Pending.First is not { } first) continue;
                    queued = first.Value;
                    queue.Pending.RemoveFirst();
                }

                await _controllerActionGate.WaitAsync(queue.Lifetime).ConfigureAwait(false);
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
                    ReportControllerActionFailure(queued.Action, exception);
                }
                finally
                {
                    _controllerActionGate.Release();
                }
            }
        }
        catch (OperationCanceledException) when (queue.Lifetime.IsCancellationRequested)
        {
            lock (_controllerQueueLock)
            {
                queue.Pending.Clear();
            }
        }
    }

    private void ReportControllerActionFailure(WidgetActionEvent action, Exception exception)
    {
        var args = new WidgetControllerActionFailedEventArgs(action, exception);
        var handlers = ControllerActionFailed?.GetInvocationList();
        if (handlers is null) return;
        foreach (var candidate in handlers)
        {
            try
            {
                ((EventHandler<WidgetControllerActionFailedEventArgs>)candidate)(this, args);
            }
            catch
            {
                // An observer cannot terminate the serial action consumer.
            }
        }
    }
}
