namespace WidgetRail.WindowsNetworkProvider;

internal interface INetworkDeadlineScheduler
{
    IDisposable Schedule(TimeSpan dueTime, Action callback);
}

internal sealed class NetworkDeadlineScheduler : INetworkDeadlineScheduler
{
    public IDisposable Schedule(TimeSpan dueTime, Action callback)
    {
        ArgumentNullException.ThrowIfNull(callback);
        return new Timer(
            static state => ((Action)state!).Invoke(),
            callback,
            dueTime,
            Timeout.InfiniteTimeSpan);
    }
}
