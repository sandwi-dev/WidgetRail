using System.Threading.Channels;

namespace GameBarAlternative.WidgetSdk;

public sealed record WidgetControllerActionFailedEventArgs(
    WidgetActionEvent Action,
    Exception Exception);

public abstract partial class Widget
{
    public const int ControllerActionQueueCapacity = 16;

    private readonly object _controllerQueueLock = new();
    private readonly SemaphoreSlim _controllerActionGate = new(1, 1);
    private Channel<WidgetActionEvent>? _controllerQueue;
    private CancellationToken _controllerQueueLifetime = new(canceled: true);

    /// <summary>
    /// Raised when an accepted controller action later fails. Controller input
    /// acknowledgement is intentionally decoupled from network-backed action
    /// completion, so failures are reported asynchronously through this event.
    /// </summary>
    public event EventHandler<WidgetControllerActionFailedEventArgs>? ControllerActionFailed;

    /// <summary>
    /// Attempts to append an action to the current active lifetime's bounded
    /// FIFO. Returns false immediately when inactive or saturated.
    /// </summary>
    private bool TryQueueControllerAction(WidgetActionEvent action)
    {
        if (!IsActive) return false;
        var lifetime = ActiveCancellationToken;
        if (lifetime.IsCancellationRequested) return false;

        lock (_controllerQueueLock)
        {
            if (!IsActive || lifetime.IsCancellationRequested) return false;
            if (_controllerQueue is null || _controllerQueueLifetime != lifetime)
            {
                _controllerQueue = Channel.CreateBounded<WidgetActionEvent>(
                    new BoundedChannelOptions(ControllerActionQueueCapacity)
                    {
                        SingleReader = true,
                        SingleWriter = false,
                        FullMode = BoundedChannelFullMode.Wait,
                        AllowSynchronousContinuations = false,
                    });
                _controllerQueueLifetime = lifetime;
                _ = ConsumeControllerActionsAsync(_controllerQueue.Reader, lifetime);
            }
            return _controllerQueue.Writer.TryWrite(action);
        }
    }

    private async Task ConsumeControllerActionsAsync(
        ChannelReader<WidgetActionEvent> reader,
        CancellationToken activeLifetime)
    {
        try
        {
            while (await reader.WaitToReadAsync(activeLifetime).ConfigureAwait(false))
            {
                while (reader.TryRead(out var action))
                {
                    await _controllerActionGate.WaitAsync(activeLifetime).ConfigureAwait(false);
                    try
                    {
                        await OnActionAsync(action, activeLifetime).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (activeLifetime.IsCancellationRequested)
                    {
                        return;
                    }
                    catch (Exception exception)
                    {
                        ReportControllerActionFailure(action, exception);
                    }
                    finally
                    {
                        _controllerActionGate.Release();
                    }
                }
            }
        }
        catch (OperationCanceledException) when (activeLifetime.IsCancellationRequested)
        {
            // Deactivation drops queued work and is the normal completion path.
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
