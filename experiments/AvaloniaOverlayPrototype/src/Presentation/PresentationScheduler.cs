using Avalonia.Threading;

namespace GameBarAlternative.AvaloniaPrototype.Presentation;

public interface IPresentationScheduler
{
    bool CheckAccess();

    Task InvokeAsync(Action action);
}

public sealed class AvaloniaUiScheduler : IPresentationScheduler
{
    private int marshalledInvocationCount;
    private int lastExecutionThreadId;

    public int MarshalledInvocationCount => Volatile.Read(ref marshalledInvocationCount);

    public int LastExecutionThreadId => Volatile.Read(ref lastExecutionThreadId);

    public bool CheckAccess() => Dispatcher.UIThread.CheckAccess();

    public Task InvokeAsync(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        if (CheckAccess())
        {
            Volatile.Write(ref lastExecutionThreadId, Environment.CurrentManagedThreadId);
            action();
            return Task.CompletedTask;
        }

        Interlocked.Increment(ref marshalledInvocationCount);
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Dispatcher.UIThread.Post(() =>
        {
            try
            {
                Volatile.Write(ref lastExecutionThreadId, Environment.CurrentManagedThreadId);
                action();
                completion.SetResult();
            }
            catch (Exception exception)
            {
                completion.SetException(exception);
            }
        }, DispatcherPriority.Normal);
        return completion.Task;
    }

}
