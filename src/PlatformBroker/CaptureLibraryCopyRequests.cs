namespace WidgetRail.PlatformBroker;

public sealed record HostCaptureCopyRequest(string Id, BrokerWidgetIdentity Widget, int Count);

/// <summary>One foreground-admitted folder selection. Only the host supplies a destination.</summary>
internal static class CaptureLibraryCopyRequests
{
    private static readonly object gate = new();
    private static HostCaptureCopyRequest? request;
    private static TaskCompletionSource<string?>? completion;
    internal static HostCaptureCopyRequest? Pending { get { lock (gate) return request; } }
    internal static async Task<string?> ChooseAsync(BrokerWidgetIdentity widget, int count, CancellationToken token)
    {
        TaskCompletionSource<string?> waiting;
        lock (gate)
        {
            if (request is not null) throw new BrokerException("capture_library_busy", "A copy destination is already being chosen.");
            request = new(Guid.NewGuid().ToString("N"), widget, count);
            completion = waiting = new(TaskCreationOptions.RunContinuationsAsynchronously);
        }
        try { return await waiting.Task.WaitAsync(TimeSpan.FromSeconds(BrokerTransportLimits.CaptureCopyPickerTimeoutSeconds), token).ConfigureAwait(false); }
        catch (TimeoutException) { return null; }
        finally { lock (gate) { if (ReferenceEquals(waiting, completion)) { request = null; completion = null; } } }
    }
    internal static void Complete(string id, string? destination)
    {
        if (destination is not null && (!Path.IsPathFullyQualified(destination) || destination.Length > 32760 || destination.Any(char.IsControl)))
            throw new BrokerException("invalid_payload", "Invalid capture copy destination.");
        lock (gate) { if (request?.Id == id) completion?.TrySetResult(destination); }
    }
}
