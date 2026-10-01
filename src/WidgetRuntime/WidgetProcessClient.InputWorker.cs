namespace WidgetRail.WidgetRuntime;

internal sealed class WidgetInputWorkerRetiredException : InvalidOperationException;

public sealed partial class WidgetProcessClient
{
    private WidgetProcessSession DemandInputWorker(int expectedStartOrdinal)
    {
        // Capture the existing pipe owner, never EnsureConnected: input must not
        // launch or recover a process after its displayed origin was retired.
        var session = Volatile.Read(ref _session);
        if (expectedStartOrdinal <= 0 || _disposed || _stopping || session is null ||
            session.IsTerminal || Starts != expectedStartOrdinal ||
            !ReferenceEquals(session, Volatile.Read(ref _session)))
            throw new WidgetInputWorkerRetiredException();
        return session;
    }
}
