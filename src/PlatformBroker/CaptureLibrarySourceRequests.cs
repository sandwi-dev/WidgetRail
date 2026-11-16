namespace WidgetRail.PlatformBroker;

public sealed record HostCaptureSourceRequest(string Id, BrokerWidgetIdentity Widget, string Label);

/// <summary>Only the trusted host can complete a source folder selection.</summary>
internal static class CaptureLibrarySourceRequests
{
    private static readonly object gate = new();
    private static HostCaptureSourceRequest? pending;
    private static TaskCompletionSource<string?>? completion;
    internal static HostCaptureSourceRequest? Pending { get { lock (gate) return pending; } }
    internal static async Task<string?> ChooseAsync(BrokerWidgetIdentity widget, string label, CancellationToken token)
    {
        TaskCompletionSource<string?> waiting;
        lock (gate)
        {
            if (pending is not null) throw new BrokerException("capture_library_busy", "A capture folder is already being chosen.");
            pending = new(Guid.NewGuid().ToString("N"), widget, label);
            completion = waiting = new(TaskCreationOptions.RunContinuationsAsynchronously);
        }
        try { return await waiting.Task.WaitAsync(TimeSpan.FromSeconds(120), token).ConfigureAwait(false); }
        catch (TimeoutException) { return null; }
        finally { lock (gate) { if (ReferenceEquals(waiting, completion)) { pending = null; completion = null; } } }
    }
    internal static void Complete(string id, string? directory)
    {
        if (directory is not null && (!Path.IsPathFullyQualified(directory) || directory.Length > 32760 || directory.Any(char.IsControl)))
            throw new BrokerException("invalid_payload", "Invalid capture source folder.");
        lock (gate) { if (pending?.Id == id) completion?.TrySetResult(directory); }
    }
}
