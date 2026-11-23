using WidgetRail.WidgetProtocol;

namespace WidgetRail.PlatformBroker;

internal sealed partial class WindowCaptureRegistry
{
    // This object owns the staging file after admission. A worker's ordinary
    // retirement only releases that worker's temporary attachments.
    private sealed record LibraryJob(CaptureLibraryStore Library, long? TraySequence, TaskCompletionSource? Completion);

    internal async Task<WindowCaptureTicket> EnqueueSavedAsync(BrokerWidgetIdentity actor, CaptureSessionRequest options,
        CaptureCapabilities available, CaptureLibraryStore library, long? traySequence, CancellationToken token,
        HostActionCompletion? hostCompletion = null)
    {
        token.ThrowIfCancellationRequested();
        var job = new LibraryJob(library, traySequence, hostCompletion is null ? null :
            new(TaskCreationOptions.RunContinuationsAsynchronously));
        if (job.Completion is { } completion) hostCompletion!.Register(completion.Task);
        try
        {
            return options.Recording is not null
                ? await EnqueueLibraryAsync(job, actor, options, available, library, token).ConfigureAwait(false)
                : EnqueueConfigured(job, actor, options, available);
        }
        catch
        {
            job.Completion?.TrySetResult();
            throw;
        }
    }

    internal async Task<bool> FinishAsync(HostCaptureCompletion completion, CancellationToken token,
        Func<HostWindowCapture, CancellationToken, Task<bool>>? reauthorize = null)
    {
        Entry entry;
        LibraryJob job;
        HostCaptureAttachment source;
        lock (gate)
        {
            if (!IsCurrent(completion.RequestId)) return false;
            entry = Demand(completion.RequestId);
            Complete(completion);
            if (entry.Owner is not LibraryJob owner) return true;
            job = owner;
            if (entry.Attachment is null)
            {
                entries.Remove(completion.RequestId);
                return completion.ErrorCode is not null;
            }
            source = Resolved(entry);
        }
        bool Current()
        {
            lock (gate) return !disposed && entries.TryGetValue(completion.RequestId, out var live) &&
                ReferenceEquals(live, entry) && entry.Expires > clock.GetUtcNow();
        }
        async Task VerifyAccess(CancellationToken cancellation)
        {
            if (reauthorize is not null && !await reauthorize(entry.Request, cancellation).ConfigureAwait(false))
                throw Error("permission_denied");
        }
        try
        {
            if (entry.OutputSession is not null)
                await SaveLibraryDraftAsync(job, source.Attachment, token, VerifyAccess).ConfigureAwait(false);
            else
                await job.Library.SaveCaptureJobAsync(source, entry.Request.Widget, Current, VerifyAccess, token).ConfigureAwait(false);
            return true;
        }
        finally
        {
            lock (gate)
                if (entries.Remove(completion.RequestId)) ReleaseEntry(entry);
        }
    }

    internal void RevokeSavedCaptures(BrokerWidgetIdentity actor)
    {
        lock (gate)
            foreach (var (id, entry) in entries.Where(pair => pair.Value.Owner is LibraryJob &&
                         pair.Value.Request.Widget == actor).ToArray())
            {
                entries.Remove(id);
                ReleaseEntry(entry);
            }
    }
}
