namespace WidgetRail.PlatformBroker;

public sealed record HostCaptureDestinationReview(string Id, BrokerWidgetIdentity Widget, string Capture, string Destination);

internal static class CaptureDestinationReviews
{
    private sealed record Review(HostCaptureDestinationReview Request, TaskCompletionSource<bool> Answer);
    private static Review? active;
    internal static HostCaptureDestinationReview? Pending => Volatile.Read(ref active)?.Request;

    internal static async Task<bool> AskAsync(BrokerWidgetIdentity actor, string capture, string destination, CancellationToken token)
    {
        var review = new Review(new(Guid.NewGuid().ToString("N"), actor, capture, destination),
            new(TaskCreationOptions.RunContinuationsAsynchronously));
        if (Interlocked.CompareExchange(ref active, review, null) is not null)
            throw new BrokerException("capture_library_busy", "Another capture link is awaiting review.");
        try { return await review.Answer.Task.WaitAsync(TimeSpan.FromMinutes(2), token).ConfigureAwait(false); }
        catch (TimeoutException) { return false; }
        finally { Interlocked.CompareExchange(ref active, null, review); }
    }

    internal static void Complete(string id, bool accepted)
    {
        var review = Volatile.Read(ref active);
        if (review?.Request.Id == id) review.Answer.TrySetResult(accepted);
    }
}
