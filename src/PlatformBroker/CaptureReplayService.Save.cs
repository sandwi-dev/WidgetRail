using WidgetRail.WidgetProtocol;

namespace WidgetRail.PlatformBroker;

public sealed partial class CaptureReplayService
{
    public async Task<CaptureReplaySaveResult> SaveAsync(BrokerWidgetIdentity actor, CaptureReplaySaveRequest request,
        CancellationToken token = default)
    {
        actor.Validate(); if (!request.IsWellFormed()) throw new BrokerException("invalid_payload", "Invalid replay save request.");
        PendingSave pending;
        await mutations.WaitAsync(token).ConfigureAwait(false);
        try
        {
            DemandAlive();
            foreach (var capability in new[] { PlatformCapabilities.WindowCaptureV1, PlatformCapabilities.CaptureLibrarySaveV1 })
            {
                var issue = await authorize(actor, capability, token).ConfigureAwait(false);
                if (issue != CaptureReplayIssue.None) return new(CaptureReplayOutcome.Unavailable, request.RequestId, Issue: issue);
            }
            token.ThrowIfCancellationRequested();
            var key = SaveKey(actor, request.ExpectedSessionId, request.RequestId);
            if (completedSaves.TryGetValue(key, out var completed)) return completed;
            if (save is { } previous)
            {
                if (previous.Actor != actor || previous.Session.Id != request.ExpectedSessionId || previous.RequestId != request.RequestId)
                    return new(CaptureReplayOutcome.Busy, request.RequestId, Issue: CaptureReplayIssue.SaveInProgress);
                pending = previous;
            }
            else
            {
                if (session is not { } active || active.Id != request.ExpectedSessionId || active.StopRequested || active.Ended)
                    return new(CaptureReplayOutcome.Conflict, request.RequestId, Issue: CaptureReplayIssue.SessionChanged);
                if (Status.BufferedDurationSeconds <= 0) return new(CaptureReplayOutcome.Unavailable, request.RequestId, Issue: CaptureReplayIssue.BufferNotReady);
                pending = new(active, actor, request.RequestId, clock.GetUtcNow().ToUnixTimeMilliseconds());
                lock (gate) save = pending;
                Publish(Status with { Saving = true, Issue = CaptureReplayIssue.None });
                pending.Work = RunSaveAsync(pending);
            }
        }
        finally { mutations.Release(); }
        // Cancellation retires the caller's wait, not a host-admitted save or recorder.
        return await pending.Completion.Task.WaitAsync(token).ConfigureAwait(false);
    }

    private async Task RunSaveAsync(PendingSave operation)
    {
        CaptureReplaySaveResult result;
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token, operation.Session.Lifetime.Token);
        stop.CancelAfter(TimeSpan.FromSeconds(45));
        bool Current() => !disposed && !stop.IsCancellationRequested && !Volatile.Read(ref operation.Revoked) &&
            ReferenceEquals(session, operation.Session) && ReferenceEquals(save, operation);
        try
        {
            using var output = await library.ReserveVideoOutputAsync(operation.Actor, 256L * 1024 * 1024,
                operation.Session.Settings.ReplayDurationSeconds, stop.Token).ConfigureAwait(false);
            await mutations.WaitAsync(stop.Token).ConfigureAwait(false);
            try
            {
                if (!Current()) throw new OperationCanceledException(stop.Token);
                lock (gate) operation.Command = new(Guid.NewGuid().ToString("N"), HostReplayCommandKind.Save, operation.Actor,
                    operation.Session.Id, operation.Session.Settings, configuration.Revision)
                    { OutputPath = output.Path, SaveRequestId = operation.RequestId };
            }
            finally { mutations.Release(); }
            var encoded = await operation.Output.Task.WaitAsync(stop.Token).ConfigureAwait(false);
            if (encoded.Outcome != CaptureReplayOutcome.Accepted)
                result = new(encoded.Outcome, operation.RequestId, Issue: encoded.Issue);
            else
            {
                if (!Current()) throw new OperationCanceledException(stop.Token);
                var permission = await authorize(operation.Session.Owner, PlatformCapabilities.WindowCaptureV1, stop.Token).ConfigureAwait(false);
                if (permission == CaptureReplayIssue.None)
                    permission = await authorize(operation.Actor, PlatformCapabilities.WindowCaptureV1, stop.Token).ConfigureAwait(false);
                if (permission == CaptureReplayIssue.None)
                    permission = await authorize(operation.Actor, PlatformCapabilities.CaptureLibrarySaveV1, stop.Token).ConfigureAwait(false);
                if (permission != CaptureReplayIssue.None)
                    result = new(CaptureReplayOutcome.Unavailable, operation.RequestId, Issue: permission);
                else
                {
                    if (encoded.Properties?.Encoder != CaptureEncoderMode.Hardware ||
                        encoded.Properties.Audio != operation.Session.Settings.Recording.Audio ||
                        encoded.Properties.RequestedFramesPerSecond != operation.Session.Settings.Recording.FramesPerSecond ||
                        encoded.ActualDurationSeconds > operation.Session.Settings.ReplayDurationSeconds ||
                        encoded.Width > operation.Session.Settings.Recording.MaximumWidth || encoded.Height > operation.Session.Settings.Recording.MaximumHeight)
                        throw new BrokerException("capture_library_invalid_media", "Replay output does not match the admitted recording.");
                    var media = new CaptureAttachment(output.Id, "video/mp4", encoded.Width, encoded.Height, encoded.ByteLength,
                        encoded.ActualDurationSeconds, long.MaxValue)
                    { Origin = CaptureAttachmentOrigin.SavedLibrary, Properties = encoded.Properties, SourceApplication = operation.Session.Application };
                    var item = await output.CommitAsync(media, operation.CapturedAt, Current, stop.Token).ConfigureAwait(false);
                    result = new(CaptureReplayOutcome.Accepted, operation.RequestId, item, item.Media.DurationSeconds);
                }
            }
        }
        catch (OperationCanceledException)
        { result = new(CaptureReplayOutcome.Cancelled, operation.RequestId, Issue: CaptureReplayIssue.Cancelled); }
        catch (BrokerException error) when (error.Code == "capture_library_edit_busy")
        { result = new(CaptureReplayOutcome.Busy, operation.RequestId, Issue: CaptureReplayIssue.SaveInProgress); }
        catch (Exception error) when (error is not (OutOfMemoryException or StackOverflowException or AccessViolationException))
        { result = new(CaptureReplayOutcome.Failed, operation.RequestId, Issue: CaptureReplayIssue.StorageUnavailable); }
        await mutations.WaitAsync().ConfigureAwait(false);
        try
        {
            if (completedSaves.Count >= 32) completedSaves.Remove(completedSaves.Keys.First());
            completedSaves[SaveKey(operation.Actor, operation.Session.Id, operation.RequestId)] = result;
            lock (gate) if (ReferenceEquals(save, operation)) save = null;
            if (ReferenceEquals(session, operation.Session))
            {
                if (operation.Session.Ended)
                { session = null; operation.Session.Lifetime.Dispose(); nextAutomaticStart = clock.GetUtcNow().AddSeconds(2);
                    Idle(operation.Session.TerminalIssue != CaptureReplayIssue.None ? operation.Session.TerminalIssue : result.Issue); }
                else Publish(Status with { Saving = false, Issue = result.Issue });
            }
            else operation.Session.Lifetime.Dispose();
            operation.Completion.TrySetResult(result);
        }
        finally { mutations.Release(); }
    }
    private static string SaveKey(BrokerWidgetIdentity actor, string sessionId, string requestId) =>
        actor.PackageId + "\n" + actor.PublisherId + "\n" + actor.InstanceId + "\n" + sessionId + "\n" + requestId;
}
