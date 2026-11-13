using WidgetRail.WidgetProtocol;

namespace WidgetRail.PlatformBroker;

public sealed partial class WebSessionRegistry
{
    private const int MaximumLiveWebCaptures=2;
    private const int MaximumTerminalWebCaptures=64;
    // Records outlive a backend session so closing/reconnecting the browser cannot
    // turn an uncertain teardown into a successful Stop acknowledgement.
    private readonly Dictionary<string,CaptureRecord> captures=new(StringComparer.Ordinal);
    private readonly Queue<string> terminalCaptures=new();
    private sealed class CaptureRecord(Owner owner,object authority,WebCaptureStreamTicket ticket,IReadOnlyList<WebCaptureStreamOptions> allowedQualities,
        WebCaptureContentPreference? fixedContentPreference)
    {
        // Do not retain Session: its sealed page assets and queues can be large.
        internal Owner Owner { get; }=owner;
        internal object Authority { get; }=authority;
        internal WebCaptureStreamTicket Ticket { get; }=ticket;
        internal IReadOnlyList<WebCaptureStreamOptions> AllowedQualities { get; }=Array.AsReadOnly(allowedQualities.ToArray());
        internal WebCaptureContentPreference? FixedContentPreference { get; }=fixedContentPreference;
        internal WebCaptureStreamOptions? SelectedQuality;
        internal WebCaptureStreamStatus Status=new(WebCaptureStreamPhase.Unavailable){Ticket=ticket};
        internal bool Revoking;
        internal bool Terminal;
        internal string? CleanupRequest;
        internal TaskCompletionSource TerminalCompletion { get; }=new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
    private static bool IsCaptureOperation(WebSessionOperation operation) =>
        operation is WebSessionOperation.PrepareWebCapture or WebSessionOperation.PrepareWebCaptureConstrained or WebSessionOperation.StopWebCapture;

    internal async ValueTask<WebCaptureStreamStatus> PrepareWebCaptureAsync(object authority,BrokerWidgetIdentity identity,
        WebCaptureStreamRequest request,CancellationToken token,Owner expectedOwner)
    {
        if(request?.IsWellFormed()!=true)throw Failure("invalid_payload");
        Session session;CaptureRecord capture;
        lock(gate)
        {
            session=CaptureSessionLocked(identity,request.Session,expectedOwner);
            if(session.Capture is {Terminal:false})throw Failure("capture_already_active");
            if(captures.Values.Count(c=>!c.Terminal)>=MaximumLiveWebCaptures)throw Failure("capture_limit");
            capture=new(session.Owner,authority,new(request.Session,Guid.NewGuid().ToString("N")),request.AllowedQualities??WebCaptureStreamOptions.LegacyQualities,
                request.FixedContentPreference?request.Options.ContentPreference:null);
            captures.Add(capture.Ticket.Id,capture);
            session.Capture=capture;
        }
        try
        {
            await EnqueueAsync(session,new(request.AllowedQualities is null?WebSessionOperation.PrepareWebCapture:WebSessionOperation.PrepareWebCaptureConstrained)
                {Session=request.Session,CaptureOptions=request.Options,CaptureQualities=request.AllowedQualities,CaptureTicket=capture.Ticket,
                    CaptureFixedContentPreference=request.FixedContentPreference},token).ConfigureAwait(false);
            lock(gate)
            {
                if(!sessions.ContainsKey(request.Session.Id) || capture.Revoking)throw Failure("capture_retired");
                return capture.Status;
            }
        }
        catch
        {
            lock(gate) RevokeCaptureLocked(capture);
            throw;
        }
    }

    internal WebCaptureStreamStatus WebCaptureStatus(object authority,BrokerWidgetIdentity identity,WebCaptureStreamTicket ticket,Owner expectedOwner)
    {
        if(ticket?.IsWellFormed()!=true)throw Failure("invalid_payload");
        lock(gate)return CaptureRecordLocked(authority,identity,ticket,expectedOwner).Status;
    }

    internal async ValueTask StopWebCaptureAsync(object authority,BrokerWidgetIdentity identity,WebCaptureStreamTicket ticket,CancellationToken token,Owner expectedOwner)
    {
        if(ticket?.IsWellFormed()!=true)throw Failure("invalid_payload");
        CaptureRecord capture;Session? session;bool retired;
        lock(gate)
        {
            capture=CaptureRecordLocked(authority,identity,ticket,expectedOwner);
            if(capture.Terminal)return;
            sessions.TryGetValue(ticket.Session.Id,out session);
            retired=capture.Revoking || session is null;
            if(retired)
            {
                RevokeCaptureLocked(capture);
                QueueCaptureCleanupLocked(capture);
            }
        }
        if(retired)
        {
            try { await capture.TerminalCompletion.Task.WaitAsync(TimeSpan.FromSeconds(30),token).ConfigureAwait(false); }
            catch(TimeoutException){throw Failure("session_timeout");}
            return;
        }
        // Complete validates the exact ticket and Stopped phase before it can
        // release this waiter. A merely well-formed Capturing result is not Stop.
        await EnqueueAsync(session!,new(WebSessionOperation.StopWebCapture)
            {Session=ticket.Session,CaptureTicket=ticket},token).ConfigureAwait(false);
    }

    internal void RetireWebCaptures(object authority)
    {
        lock(gate)
            foreach(var capture in captures.Values.Where(c=>ReferenceEquals(c.Authority,authority)&&!c.Terminal).ToArray())
            {
                foreach(var item in pending.Where(p=>p.Value.Session.Reference==capture.Ticket.Session&&IsCaptureOperation(p.Value.Command.Operation)).ToArray())
                { pending.Remove(item.Key);item.Value.Completion.TrySetException(Failure("capture_retired")); }
                RevokeCaptureLocked(capture);
            }
    }

    private CaptureRecord CaptureRecordLocked(object authority,BrokerWidgetIdentity identity,WebCaptureStreamTicket ticket,Owner expectedOwner)
    {
        if(expectedOwner.Retired || expectedOwner.Identity!=identity || !ReferenceEquals(owners.GetValueOrDefault(expectedOwner.WidgetId),expectedOwner))
            throw Failure("session_closed");
        if(!captures.TryGetValue(ticket.Id,out var capture) || capture.Ticket!=ticket ||
            !ReferenceEquals(capture.Owner,expectedOwner) || !ReferenceEquals(capture.Authority,authority))
            throw Failure("capture_retired");
        return capture;
    }
    private Session CaptureSessionLocked(BrokerWidgetIdentity identity,WebSessionReference reference,Owner expectedOwner)
    {
        if(reference?.IsWellFormed()!=true || !sessions.TryGetValue(reference.Id,out var session) || session.Owner.Retired ||
            !ReferenceEquals(session.Owner,expectedOwner) || session.Owner.Identity!=identity || !ReferenceEquals(owners.GetValueOrDefault(session.Owner.WidgetId),session.Owner))
            throw Failure("session_closed");
        if(!session.Owner.AllowsPageBackend || !session.Definition.NativeCapture ||
            session.Definition.PageAdapter?.Mode!=WebSessionPageAdapterMode.SessionBackend)throw Failure("permission_denied");
        return session;
    }
    private void RevokeCaptureLocked(CaptureRecord capture)
    {
        if(capture.Terminal || capture.Revoking)return;
        capture.Revoking=true;
        QueueCaptureCleanupLocked(capture);
    }
    private void QueueCaptureCleanupLocked(CaptureRecord capture)
    {
        if(capture.Terminal || capture.CleanupRequest is not null)return;
        // Preserve the last known state and reserve capacity until the native
        // owner confirms teardown. Queue admission is not proof of cleanup.
        var reference=capture.Ticket.Session;
        capture.CleanupRequest=Guid.NewGuid().ToString("N");
        if(!work.Writer.TryWrite(new(capture.CleanupRequest,capture.Owner.WidgetId,capture.Owner.Identity,reference,
            new(WebSessionOperation.StopWebCapture){Session=reference,CaptureTicket=capture.Ticket},CaptureRevoked:true)))
        {
            capture.CleanupRequest=null;
            if(sessions.TryGetValue(reference.Id,out var session))CloseLocked(session,"capture_retired");
        }
    }
    private void CompleteCaptureCleanupLocked(HostWebSessionResult result)
    {
        var capture=captures.Values.FirstOrDefault(c=>c.CleanupRequest==result.RequestId);
        if(capture is null)return;
        capture.CleanupRequest=null;
        // A canceled preparation may not have allocated any native transport.
        // Its Stop reply is still an exact teardown confirmation even though
        // no state-change event was necessary. Errors keep capacity reserved.
        if(result.ErrorCode is null && result.Microphone is null && result.Capture is {Phase:WebCaptureStreamPhase.Stopped} status &&
            status.IsWellFormed() && status.Ticket==capture.Ticket && IsCaptureQualityAllowedLocked(status,capture.Ticket.Session))
            UpdateCaptureLocked(status,capture.Ticket.Session);
    }
    private bool IsCaptureQualityAllowedLocked(WebCaptureStreamStatus status,WebSessionReference session)
    {
        if(status.Ticket is not { } ticket||ticket.Session!=session||!captures.TryGetValue(ticket.Id,out var capture)||capture.Ticket!=ticket)return true;
        return status.Quality is null || capture.AllowedQualities.Any(status.Quality.IsSameQuality)&&
            (capture.FixedContentPreference is null || capture.FixedContentPreference==status.Quality.ContentPreference)&&
            (capture.SelectedQuality is null||capture.SelectedQuality==status.Quality);
    }
    private void UpdateCaptureLocked(WebCaptureStreamStatus status,WebSessionReference session)
    {
        if(status.Ticket is not { } ticket || ticket.Session!=session || !captures.TryGetValue(ticket.Id,out var capture) || capture.Ticket!=ticket)return;
        var terminal=status.Phase is WebCaptureStreamPhase.Stopped or WebCaptureStreamPhase.Cancelled or WebCaptureStreamPhase.Failed or WebCaptureStreamPhase.Unavailable;
        if(capture.Terminal || capture.Revoking && !terminal)return;
        if(capture.Status.Phase==WebCaptureStreamPhase.Capturing && status.Phase==WebCaptureStreamPhase.Prepared)return;
        if(status.Phase is WebCaptureStreamPhase.Prepared or WebCaptureStreamPhase.Capturing)capture.SelectedQuality??=status.Quality;
        capture.Status=status;
        if(!terminal)return;
        capture.Terminal=true;
        capture.CleanupRequest=null;
        capture.TerminalCompletion.TrySetResult();
        terminalCaptures.Enqueue(ticket.Id);
        while(terminalCaptures.Count>MaximumTerminalWebCaptures)
            captures.Remove(terminalCaptures.Dequeue());
    }
}
